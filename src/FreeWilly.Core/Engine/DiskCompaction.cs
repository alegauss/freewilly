namespace FreeWilly.Core.Engine;

/// <summary>
/// The two sizes the Engine panel puts side by side (DD197), read at one moment.
/// </summary>
/// <param name="VirtualDisk">
/// What <c>ext4.vhdx</c> costs on the Windows volume, or <see langword="null"/> where it could not
/// be measured.
/// </param>
/// <param name="UsedInside">
/// What the distribution says is in use inside it, or <see langword="null"/> where it would not say.
/// </param>
/// <remarks>
/// Both, and taken together rather than separately, because the whole reading is the gap between
/// them: a virtual disk of fifty gigabytes against sixteen in use is the sentence this task acts on,
/// and two numbers read minutes apart are not that sentence.
/// </remarks>
public sealed record DiskSizes(long? VirtualDisk, long? UsedInside);

/// <summary>What a compaction did, and what the disk was before and after it (DD211).</summary>
/// <param name="Steps">The steps, in the order they ran.</param>
public sealed record CompactionOutcome(IReadOnlyList<RepairStep> Steps)
{
    /// <summary>What the two sizes were before anything ran.</summary>
    public DiskSizes? Before { get; init; }

    /// <summary>What they were after, or <see langword="null"/> where the run never got that far.</summary>
    public DiskSizes? After { get; init; }

    /// <summary>
    /// Whether the blocks were handed back.
    /// </summary>
    /// <remarks>
    /// Read off the one step that does it, and not off all of them, because two of the steps here
    /// are preparation: pruning the daemon's cache and trimming the filesystem only make the reclaim
    /// larger. Neither failing is a reason to leave already-freed blocks sitting on the Windows
    /// volume, and a run that could not reach the daemon and still handed back four gigabytes did
    /// the thing the button is for.
    /// </remarks>
    public bool Succeeded => Steps.Any(
        step => step.What == DiskCompaction.HandBackStep && step.Ok);

    /// <summary>The first step that failed, or <see langword="null"/>.</summary>
    public RepairStep? Failure => Steps.FirstOrDefault(step => !step.Ok);

    /// <summary>
    /// How many bytes the virtual disk gave back, or <see langword="null"/> where it cannot be said.
    /// </summary>
    /// <remarks>
    /// Never negative. A virtual disk that grew across the run is a real reading and not a reclaim,
    /// and reporting it as one would be this button claiming credit for the opposite of its job —
    /// so the figure is withheld and the two sizes stand on their own.
    /// </remarks>
    public long? HandedBack =>
        Before?.VirtualDisk is { } before && After?.VirtualDisk is { } after && after < before
            ? before - after
            : null;

    /// <summary>Whether the engine went down for this before it stopped (DD210).</summary>
    /// <remarks>
    /// The same reading <see cref="RepairOutcome.EngineWentDown"/> takes, off the same two step
    /// names, and for the same reason: on the failing path it is the difference between a run that
    /// left the engine exactly as it found it and one that left it down deliberately.
    /// </remarks>
    public bool EngineWentDown => Steps.Any(
        step => step.What is FilesystemRepair.StopStep or FilesystemRepair.TerminateStep);
}

/// <summary>
/// Hands back what the virtual disk is holding and the filesystem no longer wants (DD211).
/// </summary>
/// <remarks>
/// <para><b>The gap it acts on is the one DD197 put on the page.</b> A WSL2 virtual disk grows and
/// never shrinks, so it keeps every gigabyte an image or a build cache ever used. The panel showed
/// that as two numbers side by side and offered nothing to do about it.</para>
///
/// <para><b>Two halves, two steps.</b> Deleted layers and buildx cache still hold blocks the
/// filesystem no longer counts, so the daemon's own reclaimable cache goes first and
/// <c>fstrim</c> tells ext4 to discard what is free. Only then is there anything to hand back, and
/// handing it back is <c>wsl --manage &lt;distro&gt; --set-sparse true</c>.</para>
///
/// <para><b>That mechanism was chosen because DD199 had already measured the alternatives.</b>
/// <c>diskpart compact vdisk</c> and <c>Optimize-VHD</c> would put a UAC prompt and a Hyper-V
/// dependency behind a housekeeping button; <c>--set-sparse</c> wants the distribution stopped and
/// wants no elevation, which is a cost this page already pays for Check filesystem.</para>
///
/// <para><b>What it must not do is remove what nobody offered.</b> Images and volumes stay. The one
/// thing pruned is cache the daemon itself calls reclaimable, and both readings are taken before and
/// after so the button is answerable for the bytes it claims.</para>
/// </remarks>
public sealed class DiskCompaction
{
    /// <summary>The step that drops the daemon's reclaimable build cache.</summary>
    /// <remarks>
    /// Named here with the two steps beside it even though the seam is what writes it, so the
    /// sequence's own vocabulary is in one place and a reader of this class can see all three
    /// without going looking for the wiring.
    /// </remarks>
    public const string PruneStep = "drop the build cache";

    /// <summary>The step that trims what the filesystem has freed.</summary>
    public const string TrimStep = "trim the filesystem";

    /// <summary>The step that hands the blocks back to Windows.</summary>
    /// <remarks>
    /// Named here because <see cref="CompactionOutcome.Succeeded"/> reads it back, and a literal at
    /// each end is a run that quietly stops counting as successful the day somebody rewords a step.
    /// </remarks>
    public const string HandBackStep = "hand the blocks back";

    private readonly IWsl _wsl;
    private readonly EnginePaths _paths;
    private readonly Func<RepairStep> _pruneCache;
    private readonly Func<RepairStep> _stopEngine;

    /// <summary>Construct a compaction.</summary>
    /// <param name="wsl">The WSL command.</param>
    /// <param name="paths">Where the distribution and its virtual disk are.</param>
    /// <param name="pruneCache">
    /// Asks the daemon to drop the build cache it calls reclaimable, and says what came back. A seam
    /// because it is an Engine API call rather than a <c>wsl.exe</c> one, and it has to run while the
    /// engine is still up — which is what puts it before the stop rather than inside it.
    /// </param>
    /// <param name="stopEngine">
    /// Takes the engine down the announced way, so containers get the stop signal DD189 gives them
    /// and the host does not read this teardown as the engine dying under a suspend (DD136). A seam
    /// for the same reason: it is a process on the Windows side, not a WSL call.
    /// </param>
    public DiskCompaction(
        IWsl wsl, EnginePaths paths, Func<RepairStep> pruneCache, Func<RepairStep> stopEngine)
    {
        ArgumentNullException.ThrowIfNull(wsl);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(pruneCache);
        ArgumentNullException.ThrowIfNull(stopEngine);
        _wsl = wsl;
        _paths = paths;
        _pruneCache = pruneCache;
        _stopEngine = stopEngine;
    }

    /// <summary>Where the virtual disk sits on the Windows volume.</summary>
    public string VirtualDiskPath => Path.Combine(_paths.Distribution, "ext4.vhdx");

    /// <summary>Run the whole sequence.</summary>
    /// <param name="report">Called with each step as it lands.</param>
    /// <returns>What was done, and what the disk was either side of it.</returns>
    public CompactionOutcome Run(Action<RepairStep>? report = null)
    {
        var steps = new List<RepairStep>();

        // Read while the distribution is still up, because the inside half of it is a `df` and there
        // is nothing to ask once the terminate has happened.
        var before = Sizes();
        Record(steps, report, new RepairStep("read the disk", true, Describe(before)));

        // Both preparation, and neither stops the run. Every byte they free is a byte the hand-back
        // can return, and a daemon that would not answer is not a reason to leave the blocks that
        // were already free sitting on the Windows volume.
        Record(steps, report, _pruneCache());
        Record(steps, report, Trim());

        // Announced before it is done, for the reason DD207 cost: the host puts back an engine it
        // loses (DD136), so a teardown it was not told about is indistinguishable in there from WSL2
        // dying under a suspend — and it would have the engine back, and the distribution running,
        // under the terminate this needs.
        Record(steps, report, _stopEngine());

        if (!Record(steps, report, TakeTheDistributionDown()))
        {
            return new CompactionOutcome(steps) { Before = before };
        }

        Record(steps, report, HandBack());

        // Taken after the hand-back rather than trusted, which is the whole of being answerable for
        // the figure: the panel says what the disk is now, and the difference is arithmetic over two
        // readings this run took itself.
        var after = Sizes();
        return new CompactionOutcome(steps) { Before = before, After = after };
    }

    private static bool Record(
        List<RepairStep> steps, Action<RepairStep>? report, RepairStep step)
    {
        steps.Add(step);
        report?.Invoke(step);
        return step.Ok;
    }

    /// <summary>Both sizes, at this moment.</summary>
    /// <returns>The reading, with either half null where it could not be taken.</returns>
    /// <remarks>
    /// The inside half asks the distribution, which starts it where it is stopped — which is why the
    /// second reading is taken after the hand-back and the hand-back is the last thing that needs the
    /// distribution down.
    /// </remarks>
    private DiskSizes Sizes()
    {
        long? onDisk = null;
        try
        {
            var vhdx = VirtualDiskPath;
            if (File.Exists(vhdx))
            {
                onDisk = new FileInfo(vhdx).Length;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A size that could not be read is a null reading rather than a failed run: the
            // compaction is still worth doing, and the panel already knows how to say it was unread.
            onDisk = null;
        }

        var asked = _wsl.Run(
            WslBudget.Work, "-d", _paths.DistributionName, "-u", "root", "--exec",
            "/bin/sh", "-c", DistributionState.Script);

        var used = asked.Succeeded ? DistributionState.Of(asked.Output)?.UsedKb : null;
        return new DiskSizes(onDisk, used is { } kb ? kb * 1024 : null);
    }

    /// <summary>Tell ext4 to discard the blocks it is no longer using.</summary>
    /// <returns>The step.</returns>
    /// <remarks>
    /// <c>fstrim</c> is a BusyBox applet as well as a util-linux one, which is what makes it askable
    /// of the distribution this tool provisions (DD201 is the rule it has to satisfy). Where it is
    /// not there the step fails and the run carries on: WSL2 mounts with <c>discard</c>, so a
    /// filesystem that has been deleting layers all week has already handed back most of it, and
    /// refusing to compact at all would be worse than compacting less.
    /// </remarks>
    private RepairStep Trim()
    {
        var trimmed = _wsl.Run(
            WslBudget.Work, "-d", _paths.DistributionName, "-u", "root", "--exec",
            "/bin/sh", "-c", "fstrim -v /");

        var said = Said(trimmed);
        if (!trimmed.Succeeded)
        {
            return new RepairStep(
                TrimStep,
                false,
                "the filesystem would not be trimmed, so only blocks already discarded come back: "
                + said);
        }

        // fstrim -v prints how much it discarded, which is the figure worth keeping. BusyBox's
        // applet is quieter than util-linux's and can say nothing at all, so the step still has a
        // sentence of its own to fall back on.
        return new RepairStep(
            TrimStep,
            true,
            said.Length > 0 ? said : "the filesystem discarded what it had freed");
    }

    private RepairStep TakeTheDistributionDown()
    {
        var down = _wsl.Run(WslBudget.Work, "--terminate", _paths.DistributionName);
        return down.Succeeded
            ? new RepairStep(
                FilesystemRepair.TerminateStep,
                true,
                $"{_paths.DistributionName} terminated, so its disk is not in use")
            : new RepairStep(
                FilesystemRepair.TerminateStep,
                false,
                $"terminating {_paths.DistributionName} failed, and the disk cannot be handed back "
                + $"while it is in use: {Said(down)}");
    }

    /// <summary>Hand the freed blocks back to Windows.</summary>
    /// <returns>The step.</returns>
    /// <remarks>
    /// No <c>--allow-unsafe</c>, which is the flag that would let this run against a disk still in
    /// use. The terminate above is what makes it unnecessary, and a housekeeping button reaching for
    /// the unsafe form of a call is how a tidy-up becomes the thing that corrupts a filesystem.
    /// </remarks>
    private RepairStep HandBack()
    {
        var sparse = _wsl.Run(
            WslBudget.Work, "--manage", _paths.DistributionName, "--set-sparse", "true");

        // What WSL said, and nothing added to it. This used to claim the call needed "a WSL new
        // enough to have it", which the DD221 rehearsal falsified on the first run: WSL has the
        // flag, has disabled it, and says so in a sentence naming the reason. A guess about the
        // cause printed over a tool's own explanation is worse than no guess at all.
        return sparse.Succeeded
            ? new RepairStep(
                HandBackStep, true, $"{_paths.DistributionName} is sparse, so Windows has the blocks")
            : new RepairStep(
                HandBackStep,
                false,
                $"`wsl --manage {_paths.DistributionName} --set-sparse true` was refused: "
                + Said(sparse));
    }

    /// <summary>One reading, in the words the panel uses for the same two numbers.</summary>
    private static string Describe(DiskSizes sizes) =>
        $"virtual disk {Size(sizes.VirtualDisk)}, used inside {Size(sizes.UsedInside)}";

    private static string Size(long? bytes) =>
        bytes is { } value ? MachineReport.Size(value) : MachineReport.Unread;

    /// <summary>What a call said, on one line.</summary>
    private static string Said(WslResult result) =>
        result.Failure ?? result.Output.Trim().ReplaceLineEndings(" ");
}
