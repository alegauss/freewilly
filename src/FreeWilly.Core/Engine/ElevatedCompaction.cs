namespace FreeWilly.Core.Engine;

/// <summary>
/// Compacts the virtual disk with administrator rights, once somebody has asked for it (DD237).
/// </summary>
/// <remarks>
/// <para><b>Offered only where the unelevated route is gone.</b> DD199 weighed <c>diskpart compact
/// vdisk</c> against <c>wsl --manage --set-sparse</c> and chose the second, because the first puts a
/// UAC prompt behind a housekeeping button. Windows has since disabled sparse VHDs over possible
/// data corruption (DD224), which leaves the operation with no unelevated route at all — so the
/// ending that was a wall becomes the offer this class is.</para>
///
/// <para><b>What is not being reversed is the product's own premise.</b> FreeWilly is not an
/// elevated program and this does not make it one. What elevates is one named command, for as long
/// as it takes to run, after a press that asked for it — the same bargain the installer struck for
/// turning the WSL feature on, and the reason a refused prompt here costs nothing at all.</para>
///
/// <para><b>Through <c>cmd</c> rather than straight at <c>diskpart</c>,</b> which buys the one thing
/// an elevated child otherwise cannot give back: its words. Standard handles belong to the elevated
/// process, so nothing here could read them; a redirect written into the command line puts the log
/// in a file this side can open once the child is gone. Without it a failure would be an exit code
/// with no way to say which of the four lines produced it.</para>
/// </remarks>
public sealed class ElevatedCompaction
{
    /// <summary>The step that takes the distribution down so its disk is not in use.</summary>
    /// <remarks>
    /// <c>compact vdisk</c> refuses a disk something has open, and WSL holds this one for as long as
    /// the distribution is up. The same terminate the unelevated sequence does, and needed here for
    /// the same reason rather than inherited from a run that may have happened minutes ago.
    /// </remarks>
    public const string TakeItDownStep = "take the disk out of use";

    /// <summary>The step that runs diskpart.</summary>
    public const string CompactStep = "compact the virtual disk";

    /// <summary>What diskpart is asked to do, one verb per line.</summary>
    /// <remarks>
    /// <para><c>attach vdisk readonly</c> is not decoration. <c>compact vdisk</c> reclaims nothing
    /// from a disk it cannot see the contents of, and read-only is what lets it look without
    /// anything being able to write while it does.</para>
    ///
    /// <para>The path is quoted because <c>{localappdata}</c> is under a user profile and a profile
    /// name with a space in it is ordinary.</para>
    /// </remarks>
    /// <param name="virtualDisk">The <c>ext4.vhdx</c> to compact.</param>
    /// <returns>The script, ready to be written to a file.</returns>
    public static string Script(string virtualDisk)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualDisk);
        return $"""
            select vdisk file="{virtualDisk}"
            attach vdisk readonly
            compact vdisk
            detach vdisk

            """;
    }

    private readonly IWsl _wsl;
    private readonly EnginePaths _paths;
    private readonly IElevated _elevated;
    private readonly Func<RepairStep> _stopEngine;

    /// <summary>Construct one.</summary>
    /// <param name="wsl">The WSL command.</param>
    /// <param name="paths">Where the distribution and its virtual disk are.</param>
    /// <param name="elevated">How a command is run with administrator rights.</param>
    /// <param name="stopEngine">
    /// Takes the engine down the announced way, so the host does not read this teardown as the
    /// engine dying under a suspend and put it back mid-compaction (DD136, DD207). The same seam
    /// <see cref="DiskCompaction"/> takes, and for the same reason.
    /// </param>
    public ElevatedCompaction(
        IWsl wsl, EnginePaths paths, IElevated elevated, Func<RepairStep> stopEngine)
    {
        ArgumentNullException.ThrowIfNull(wsl);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(elevated);
        ArgumentNullException.ThrowIfNull(stopEngine);
        _wsl = wsl;
        _paths = paths;
        _elevated = elevated;
        _stopEngine = stopEngine;
    }

    /// <summary>Where the virtual disk sits on the Windows volume.</summary>
    public string VirtualDiskPath => Path.Combine(_paths.Distribution, "ext4.vhdx");

    /// <summary>Where diskpart's own words are kept afterwards.</summary>
    public string LogPath => Path.Combine(_paths.Root, "diskpart.log");

    /// <summary>Run it.</summary>
    /// <param name="report">Called with each step as it lands.</param>
    /// <returns>What was done, and what the disk was either side of it.</returns>
    /// <remarks>
    /// No prune and no <c>fstrim</c>. This is offered at the end of a compaction that has just run
    /// both and been refused only at the hand-back, so repeating them would be two more minutes of
    /// work whose result is already on the disk.
    /// </remarks>
    public CompactionOutcome Run(Action<RepairStep>? report = null)
    {
        var steps = new List<RepairStep>();

        // Read while the distribution is still up, because the inside half of it is a `df` and there
        // is nothing to ask once the terminate has happened.
        var before = DiskCompaction.Read(_wsl, _paths);
        Record(steps, report, new RepairStep("read the disk", true, Describe(before)));

        Record(steps, report, _stopEngine());

        if (!Record(steps, report, TakeItDown()))
        {
            return new CompactionOutcome(steps) { Before = before };
        }

        if (!Record(steps, report, Compact()))
        {
            // Still measured. The terminate happened either way, and a reading taken after a
            // diskpart that refused is the evidence that nothing moved rather than an assumption
            // about it.
            return new CompactionOutcome(steps)
            {
                Before = before,
                After = DiskCompaction.Read(_wsl, _paths),
            };
        }

        return new CompactionOutcome(steps)
        {
            Before = before,
            After = DiskCompaction.Read(_wsl, _paths),
        };
    }

    private static bool Record(
        List<RepairStep> steps, Action<RepairStep>? report, RepairStep step)
    {
        steps.Add(step);
        report?.Invoke(step);
        return step.Ok;
    }

    private RepairStep TakeItDown()
    {
        var down = _wsl.Run(WslBudget.Work, "--terminate", _paths.DistributionName);
        return down.Succeeded
            ? new RepairStep(
                TakeItDownStep,
                true,
                $"{_paths.DistributionName} terminated, so diskpart can open its disk")
            : new RepairStep(
                TakeItDownStep,
                false,
                $"terminating {_paths.DistributionName} failed, and diskpart will not compact a "
                + $"disk that is in use: {Said(down)}");
    }

    /// <summary>Ask diskpart, elevated, and read back what it said.</summary>
    private RepairStep Compact()
    {
        string script;
        try
        {
            Directory.CreateDirectory(_paths.Root);
            script = Path.Combine(_paths.Root, "compact.diskpart");
            File.WriteAllText(script, Script(VirtualDiskPath));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return new RepairStep(
                CompactStep, false, $"the diskpart script could not be written: {exception.Message}");
        }

        // Quoted the way cmd wants a command line that itself contains quotes: the outer pair is
        // what /c strips, so both paths inside keep theirs.
        var run = _elevated.Run(
            "cmd.exe", $"/c \"\"{Diskpart}\" /s \"{script}\" > \"{LogPath}\" 2>&1\"");

        if (run.Refused)
        {
            // Not a failure and deliberately not worded as one. Somebody was asked and said no,
            // which is the whole point of asking rather than running elevated all the time.
            return new RepairStep(
                CompactStep,
                false,
                "administrator rights were declined, so nothing was compacted and nothing was "
                + "changed");
        }

        if (!run.Ran)
        {
            return new RepairStep(
                CompactStep,
                false,
                $"diskpart could not be started with administrator rights: "
                + (run.Failure ?? "no reason was given"));
        }

        return run.Succeeded
            ? new RepairStep(
                CompactStep, true, $"diskpart compacted the virtual disk; its log is {LogPath}")
            : new RepairStep(
                CompactStep,
                false,
                $"diskpart exited {run.ExitCode}: {Complaint()}");
    }

    /// <summary>Where diskpart lives, by full path.</summary>
    /// <remarks>
    /// Under System32 by name rather than trusted to the PATH, which is the rule this project
    /// already applies to elevated commands: what is about to run with administrator rights is not
    /// a name a directory earlier in somebody's PATH gets to answer for.
    /// </remarks>
    private static string Diskpart => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "diskpart.exe");

    /// <summary>What diskpart wrote, on one line, or a pointer to where it is.</summary>
    private string Complaint()
    {
        try
        {
            var said = File.ReadAllText(LogPath).Trim().ReplaceLineEndings(" ");
            return said.Length > 0 ? said : $"it wrote nothing to {LogPath}";
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            return $"and its log could not be read from {LogPath}";
        }
    }

    /// <summary>One reading, in the words the panel uses for the same three numbers.</summary>
    private static string Describe(DiskSizes sizes) =>
        $"virtual disk {Size(sizes.VirtualDisk)}, used on Windows {Size(sizes.OnDisk)}, "
        + $"used inside {Size(sizes.UsedInside)}";

    private static string Size(long? bytes) =>
        bytes is { } value ? MachineReport.Size(value) : MachineReport.Unread;

    private static string Said(WslResult result) =>
        result.Failure ?? result.Output.Trim().ReplaceLineEndings(" ");
}
