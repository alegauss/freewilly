namespace FreeWilly.Core.Engine;

/// <summary>One step of a check or a repair.</summary>
/// <param name="What">Which step, named so a failure says where it stopped.</param>
/// <param name="Ok">Whether it succeeded.</param>
/// <param name="Detail">What happened, in one line.</param>
public sealed record RepairStep(string What, bool Ok, string Detail);

/// <summary>What a check or a repair did, and what the filesystem turned out to be.</summary>
/// <param name="Steps">The steps, in the order they ran.</param>
public sealed record RepairOutcome(IReadOnlyList<RepairStep> Steps)
{
    /// <summary>Whether every step landed.</summary>
    public bool Succeeded => Steps.Count > 0 && Steps.All(step => step.Ok);

    /// <summary>The step that failed, or <see langword="null"/>.</summary>
    public RepairStep? Failure => Steps.FirstOrDefault(step => !step.Ok);

    /// <summary>Everything <c>e2fsck</c> printed, or <see langword="null"/> where it never ran.</summary>
    /// <remarks>
    /// Shown to the user before anything is approved, which is the design's own rule. A repair
    /// writes to the filesystem holding every image and volume they have, and "something is wrong,
    /// press to fix" is not a thing anybody can consent to.
    /// </remarks>
    public string? Findings { get; init; }

    /// <summary>Whether the filesystem needed nothing done to it.</summary>
    public bool Clean { get; init; }
}

/// <summary>
/// Checks and repairs the owned distribution's filesystem, from a rescue distribution (DD199).
/// </summary>
/// <remarks>
/// <para><b>The mechanism was measured before it was chosen.</b> Two could carry this: a documented
/// <c>wsl --mount --vhd --bare</c> costing a UAC prompt, or the fact that WSL leaves a terminated
/// distribution's disk attached to the shared utility VM. The second was measured on 29 August 2026
/// and holds: with a second distribution up, <c>wsl --terminate freewilly</c> left <c>/dev/sdd</c>
/// attached carrying the same ext4 UUID, and <c>e2fsck -fn</c> ran against it through all five
/// passes. No elevation, and nothing the user has to approve at the Windows level.</para>
///
/// <para><b>What that measurement also found is the constraint this class is built around.</b> The
/// attachment survives the terminate only while something else holds the virtual machine open, and
/// holding it takes a <em>running process</em> rather than a started distribution: the first attempt
/// lost the VM to WSL's idle timeout the moment the second distribution's command returned, and the
/// disk went with it. So the rescue is brought up and held <em>before</em> the engine's distribution
/// is terminated, and the order is not a preference.</para>
///
/// <para><b>The rescue is temporary.</b> It is imported from the Alpine rootfs the manifest already
/// pins, and unregistered when the work is done, so nothing this tool does not own is left in the
/// user's <c>wsl --list</c>. The cost is stated rather than hidden: <c>e2fsprogs</c> has to be
/// fetched into it at the moment of repair, which is a network call at the moment things are already
/// wrong. Where that fails this refuses and prints the manual sequence instead (DD190).</para>
///
/// <para>A root cannot check itself, which is why any of this is necessary: starting the engine's
/// own distribution to run <c>e2fsck</c> would mount the very filesystem being checked.</para>
/// </remarks>
public sealed class FilesystemRepair
{
    /// <summary>The distribution imported to run the check from, and then removed.</summary>
    /// <remarks>
    /// Named after this tool so a user who finds it in <c>wsl --list</c> after a crash knows what
    /// left it there and that removing it costs nothing.
    /// </remarks>
    public const string RescueName = "freewilly-rescue";

    private readonly IWsl _wsl;
    private readonly EnginePaths _paths;
    private readonly Func<string, IDisposable> _hold;

    /// <summary>Construct a repair.</summary>
    /// <param name="wsl">The WSL command.</param>
    /// <param name="paths">Where the distribution and the pinned rootfs are.</param>
    /// <param name="hold">
    /// Opens a long-running process in a distribution and keeps the virtual machine up until it is
    /// disposed. A seam because it is the one thing here that is not a <c>wsl.exe</c> call and
    /// returns, and it is also the step the measurement proved cannot be skipped.
    /// </param>
    public FilesystemRepair(IWsl wsl, EnginePaths paths, Func<string, IDisposable> hold)
    {
        ArgumentNullException.ThrowIfNull(wsl);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(hold);
        _wsl = wsl;
        _paths = paths;
        _hold = hold;
    }

    /// <summary>Where the rescue distribution is imported to.</summary>
    public string RescueRoot => Path.Combine(_paths.Root, "rescue");

    /// <summary>
    /// Read the filesystem and change nothing.
    /// </summary>
    /// <param name="rootfsPath">The verified Alpine rootfs tarball.</param>
    /// <param name="report">Called with each step as it lands.</param>
    /// <returns>What was found.</returns>
    /// <remarks>
    /// Runs freely, and that asymmetry is the design's. Reading a filesystem cannot make it worse,
    /// so gating it behind a confirmation would only mean a user pressing a button to be told
    /// nothing is wrong.
    /// </remarks>
    public RepairOutcome Check(string rootfsPath, Action<RepairStep>? report = null) =>
        Run(rootfsPath, write: false, report);

    /// <summary>
    /// Read the filesystem and mend what it finds.
    /// </summary>
    /// <param name="rootfsPath">The verified Alpine rootfs tarball.</param>
    /// <param name="report">Called with each step as it lands.</param>
    /// <returns>What was done.</returns>
    /// <remarks>
    /// The caller is responsible for having asked first and for having shown what
    /// <see cref="Check"/> found. This writes to the filesystem holding every image and volume the
    /// user has, and <c>e2fsck -fy</c> answers yes to questions that can discard a damaged inode.
    /// </remarks>
    public RepairOutcome Fix(string rootfsPath, Action<RepairStep>? report = null) =>
        Run(rootfsPath, write: true, report);

    private RepairOutcome Run(string rootfsPath, bool write, Action<RepairStep>? report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootfsPath);

        var steps = new List<RepairStep>();

        // Asked of the engine's own distribution while it is still up, because after the terminate
        // there is nothing left that knows which of the attached disks was its. A UUID rather than a
        // device name: /dev/sdX is assigned in attach order and moves between boots, and picking the
        // wrong one would run a repair against somebody else's distribution.
        var uuid = RootUuid();
        if (uuid is null)
        {
            Record(steps, report, new RepairStep(
                "note the disk",
                false,
                $"{_paths.DistributionName} could not say which filesystem is its root, so nothing "
                + "here can tell its disk from the others attached. Run the sequence by hand"));
            return new RepairOutcome(steps);
        }

        Record(steps, report, new RepairStep("note the disk", true, $"root filesystem is {uuid}"));

        if (!Record(steps, report, ImportRescue(rootfsPath)))
        {
            return new RepairOutcome(steps);
        }

        try
        {
            // Held before the terminate and not after it. Measured: the attachment survives only
            // while the virtual machine does, and the machine goes down as soon as no distribution
            // has a running process in it.
            using var holding = _hold(RescueName);

            if (!Record(steps, report, InstallTools()))
            {
                return new RepairOutcome(steps);
            }

            if (!Record(steps, report, TakeTheEngineDown()))
            {
                return new RepairOutcome(steps);
            }

            var device = DeviceFor(uuid);
            if (device is null)
            {
                Record(steps, report, new RepairStep(
                    "find the disk",
                    false,
                    $"no attached disk carries {uuid}, so the terminate took it away with the "
                    + "distribution rather than leaving it on the virtual machine"));
                return new RepairOutcome(steps);
            }

            Record(steps, report, new RepairStep(
                "find the disk", true, $"{uuid} is attached as {device}"));

            var read = Fsck(device, write);
            Record(steps, report, read.Step);
            return new RepairOutcome(steps) { Findings = read.Findings, Clean = read.Clean };
        }
        finally
        {
            // Always, including after a failure. A rescue distribution left registered is this tool
            // having put something in somebody's `wsl --list` that it told them it would not.
            Record(steps, report, RemoveRescue());
        }
    }

    private static bool Record(
        List<RepairStep> steps, Action<RepairStep>? report, RepairStep step)
    {
        steps.Add(step);
        report?.Invoke(step);
        return step.Ok;
    }

    /// <summary>Ask the engine's distribution which filesystem it is running on.</summary>
    /// <returns>The UUID, or <see langword="null"/> where it could not say.</returns>
    private string? RootUuid()
    {
        var asked = _wsl.Run(
            "-d", _paths.DistributionName, "-u", "root", "--exec",
            "/bin/sh", "-c", "findmnt -n -o UUID /");

        var uuid = asked.Output.Trim();
        return asked.Succeeded && uuid.Length > 0 ? uuid : null;
    }

    private RepairStep ImportRescue(string rootfsPath)
    {
        Directory.CreateDirectory(RescueRoot);
        var imported = _wsl.Run(
            WslBudget.Work, "--import", RescueName, RescueRoot, rootfsPath, "--version", "2");

        return imported.Succeeded
            ? new RepairStep("bring up the rescue", true, $"{RescueName} imported into {RescueRoot}")
            : new RepairStep(
                "bring up the rescue", false, $"importing {RescueName} failed: {Said(imported)}");
    }

    /// <summary>Put <c>e2fsck</c> into the rescue distribution.</summary>
    /// <returns>Whether it is there.</returns>
    /// <remarks>
    /// A network call at the moment things are already wrong, which is the cost of the rescue being
    /// temporary rather than provisioned up front. The check is <c>command -v</c> and not apk's own
    /// exit code, for the reason DD196 gives: a mirror can succeed and install nothing useful, and
    /// this is the one binary the whole operation is for.
    /// </remarks>
    private RepairStep InstallTools()
    {
        var added = _wsl.Run(
            WslBudget.Work, "-d", RescueName, "-u", "root", "--exec", "/bin/sh", "-c",
            "apk add --no-cache --no-progress e2fsprogs e2fsprogs-extra && command -v e2fsck");

        return added.Succeeded
            ? new RepairStep("fetch e2fsck", true, $"e2fsprogs is in {RescueName}")
            : new RepairStep(
                "fetch e2fsck",
                false,
                $"{RescueName} could not fetch e2fsprogs, which needs a network: {Said(added)}");
    }

    private RepairStep TakeTheEngineDown()
    {
        var down = _wsl.Run(WslBudget.Work, "--terminate", _paths.DistributionName);
        return down.Succeeded
            ? new RepairStep(
                "take the engine down",
                true,
                $"{_paths.DistributionName} terminated, so its root is unmounted")
            : new RepairStep(
                "take the engine down",
                false,
                $"terminating {_paths.DistributionName} failed: {Said(down)}");
    }

    /// <summary>Which attached disk carries that filesystem, asked of the rescue.</summary>
    /// <param name="uuid">The filesystem's UUID.</param>
    /// <returns>The device path, or <see langword="null"/> where nothing carries it.</returns>
    private string? DeviceFor(string uuid)
    {
        var found = _wsl.Run(
            "-d", RescueName, "-u", "root", "--exec", "/bin/sh", "-c", $"blkid -U '{uuid}'");

        var device = found.Output.Trim();
        return found.Succeeded && device.StartsWith("/dev/", StringComparison.Ordinal)
            ? device
            : null;
    }

    /// <summary>Run the check itself.</summary>
    /// <param name="device">The disk, as the rescue sees it.</param>
    /// <param name="write">Whether it may mend what it finds.</param>
    /// <returns>The step, everything it printed, and whether the filesystem was already clean.</returns>
    /// <remarks>
    /// <c>-f</c> on both, because a filesystem marked clean is exactly what an unclean shutdown
    /// leaves behind and a check that trusts the flag reports nothing on the disk that needs it
    /// most. <c>-n</c> answers no to every question and touches nothing; <c>-y</c> answers yes.
    ///
    /// <para>Exit codes are the answer rather than the text. 0 is clean, 1 is errors corrected, 2 is
    /// corrected and a reboot wanted, and 4 is errors left uncorrected — which is what <c>-n</c>
    /// returns on a dirty filesystem, and is a finding rather than a failure.</para>
    /// </remarks>
    private (RepairStep Step, string Findings, bool Clean) Fsck(string device, bool write)
    {
        var ran = _wsl.Run(
            WslBudget.Work, "-d", RescueName, "-u", "root", "--exec",
            "/bin/sh", "-c", $"e2fsck -f{(write ? 'y' : 'n')} '{device}'");

        var said = ran.Output.Trim();
        var clean = ran.ExitCode == 0;
        var corrected = ran.ExitCode is 1 or 2;
        var found = ran.ExitCode == 4;
        var what = write ? "repair" : "check";

        if (clean)
        {
            return (new RepairStep(what, true, "the filesystem is clean"), said, true);
        }

        if (write && corrected)
        {
            return (new RepairStep(what, true, "errors were found and corrected"), said, false);
        }

        if (!write && (found || corrected))
        {
            return (
                new RepairStep(what, true, "the filesystem has errors and a repair would mend them"),
                said,
                false);
        }

        return (
            new RepairStep(what, false, $"e2fsck exited {ran.ExitCode?.ToString() ?? "without a code"}"),
            said,
            false);
    }

    private RepairStep RemoveRescue()
    {
        var gone = _wsl.Run(WslBudget.Work, "--unregister", RescueName);
        return gone.Succeeded
            ? new RepairStep("put the rescue away", true, $"{RescueName} unregistered")
            : new RepairStep(
                "put the rescue away",
                false,
                $"{RescueName} is still registered and can be removed with "
                + $"`wsl --unregister {RescueName}`: {Said(gone)}");
    }

    /// <summary>What a failed call said, on one line.</summary>
    /// <param name="result">The call.</param>
    /// <returns>Its output, or its failure where it never ran.</returns>
    private static string Said(WslResult result) =>
        result.Failure ?? result.Output.Trim().ReplaceLineEndings(" ");
}
