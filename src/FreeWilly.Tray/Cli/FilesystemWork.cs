using FreeWilly.Core.Api;
using FreeWilly.Core.Engine;

namespace FreeWilly.Tray.Cli;

/// <summary>
/// The check and the repair, wired to this machine and to the engine's own teardown (DD199).
/// </summary>
/// <remarks>
/// <para>Here rather than in Core because the hold on the virtual machine is a
/// <see cref="System.Diagnostics.Process"/> this assembly owns, and because acquiring the rootfs is
/// the same store the provision uses. What Core carries is the sequence and the decisions, which is
/// the half worth testing.</para>
///
/// <para><b>The engine is taken down by the ordinary route first</b>, so its containers get the stop
/// signal DD189 gives them. The repair terminates the distribution anyway; a database killed on the
/// way to mending the disk under it would be a poor trade, and it is the same order the CLI uses.
/// </para>
/// </remarks>
internal sealed class FilesystemWork : IFilesystemWork
{
    /// <summary>The check and the repair, against this machine.</summary>
    /// <returns>The work.</returns>
    /// <remarks>
    /// Reached by both surfaces since DD204. The verb and the window had each assembled the same
    /// five steps — the registered guard, the rootfs acquire, the engine stop, the construction and
    /// the call — and nothing was wrong with either copy, which is the state a duplicate is in until
    /// one of them is edited. What differs between them is the rendering, and that is all that
    /// should.
    /// </remarks>
    internal static IFilesystemWork OnThisMachine() => new FilesystemWork();

    /// <inheritdoc/>
    public RepairOutcome Check(Action<RepairStep> report) => Run(report, write: false);

    /// <inheritdoc/>
    public RepairOutcome Fix(Action<RepairStep> report) => Run(report, write: true);

    /// <inheritdoc/>
    /// <remarks>
    /// Shorter than the two above because it needs no rescue: nothing here reads the filesystem, so
    /// there is no root that has to be checked from outside itself, and the whole sequence is calls
    /// against the engine's own distribution. What it shares with them is the announced stop, and
    /// that is shared as code rather than as an order somebody has to remember twice.
    /// </remarks>
    public CompactionOutcome Compact(Action<RepairStep> report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var paths = new EnginePaths();
        if (Unregistered(paths, "virtual disk here to compact") is { } missing)
        {
            return new CompactionOutcome([missing]);
        }

        return new DiskCompaction(new Wsl(), paths, PruneBuildCache, StopTheEngine).Run(report);
    }

    private static RepairOutcome Run(Action<RepairStep> report, bool write)
    {
        ArgumentNullException.ThrowIfNull(report);

        var paths = new EnginePaths();
        if (Unregistered(paths, "filesystem to check") is { } missing)
        {
            return new RepairOutcome([missing]);
        }

        using var fetcher = new HttpArtefactFetcher();
        var acquired = new ArtefactStore(fetcher, paths.Downloads)
            .AcquireAsync(EngineManifest.Current.Rootfs).GetAwaiter().GetResult();
        if (acquired.Path is not { } rootfs)
        {
            return new RepairOutcome(
            [
                new RepairStep(
                    "bring up the rescue",
                    false,
                    "the rescue is imported from the Alpine rootfs this install pins, and it is "
                    + $"not available: {acquired.Failure}"),
            ]);
        }

        report(StopTheEngine());

        var repair = new FilesystemRepair(new Wsl(), paths, VmHold.On);
        return write ? repair.Fix(rootfs, report) : repair.Check(rootfs, report);
    }

    /// <summary>The guard, where there is nothing on this machine to work on.</summary>
    /// <param name="paths">Where the distribution would be.</param>
    /// <param name="what">What is missing, as the sentence ends.</param>
    /// <returns>The refusal, or <see langword="null"/> where the distribution is registered.</returns>
    private static RepairStep? Unregistered(EnginePaths paths, string what) =>
        paths.DistributionRegistered
            ? null
            : new RepairStep(
                "find the distribution",
                false,
                $"{paths.DistributionName} is not registered, so there is no {what}");

    /// <summary>
    /// Take the engine down the announced way, and say so.
    /// </summary>
    /// <returns>The step.</returns>
    /// <remarks>
    /// <para>Announced before it is done, exactly as <c>--stop</c> does, and DD207 is what skipping
    /// it cost. The host puts back an engine it loses (DD136), so a teardown it was not told about is
    /// indistinguishable in there from WSL2 dying under a suspend: measured on the first real run of
    /// the check, the host had the engine back nine seconds in and the distribution's root mounted
    /// read-write while <c>e2fsck</c> was still reading it. That run used <c>-fn</c> and wrote
    /// nothing; the repair uses <c>-fy</c>, and a write to a filesystem the kernel has mounted
    /// underneath it is the one way this tool could destroy the thing it exists to mend.</para>
    ///
    /// <para>Shared with the compaction since DD211 rather than copied into it. The hazard is the
    /// same one and so is the order, and the second copy of a sequence is the one that goes stale.
    /// </para>
    /// </remarks>
    private static RepairStep StopTheEngine()
    {
        var heard = SingleEngine.TellTheLiveOneToStop();

        new EngineLifecycle(new Wsl(), new WslDaemonProcess(), new WslSocatBackend())
            .StopAsync(EngineLifecycle.PatientGrace).GetAwaiter().GetResult();

        return new RepairStep(
            FilesystemRepair.StopStep,
            true,
            heard
                ? "told the host to stop, so it will not put the engine back under this"
                : "no host was running, so nothing will put the engine back");
    }

    /// <summary>
    /// Ask the daemon to drop the build cache it calls reclaimable (DD211).
    /// </summary>
    /// <returns>The step.</returns>
    /// <remarks>
    /// A failure here is reported and does not stop the compaction, which is
    /// <see cref="CompactionOutcome.Succeeded"/>'s rule: an engine that would not answer is not a
    /// reason to leave blocks the filesystem has already freed sitting on the Windows volume. It runs
    /// before the stop because it needs the daemon, which is the whole reason the sequence takes it
    /// as a seam rather than doing it itself.
    /// </remarks>
    private static RepairStep PruneBuildCache()
    {
        try
        {
            using var api = new DockerApi();
            if (!api.PingAsync().GetAwaiter().GetResult())
            {
                return new RepairStep(
                    DiskCompaction.PruneStep,
                    false,
                    "the engine is not answering, so its build cache stays where it is and only "
                    + "what the filesystem has already freed comes back");
            }

            var pruned = api.PruneBuildCacheAsync().GetAwaiter().GetResult();
            var records = pruned.CachesDeleted?.Count ?? 0;
            return new RepairStep(
                DiskCompaction.PruneStep,
                true,
                records == 0 && pruned.SpaceReclaimed == 0
                    ? "the daemon had no build cache it was finished with"
                    : $"{records} cache record(s) went, freeing "
                      + $"{MachineReport.Size(pruned.SpaceReclaimed)} inside the filesystem");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new RepairStep(
                DiskCompaction.PruneStep,
                false,
                $"the engine would not prune its build cache: {exception.Message}");
        }
    }
}
