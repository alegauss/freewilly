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

    private static RepairOutcome Run(Action<RepairStep> report, bool write)
    {
        ArgumentNullException.ThrowIfNull(report);

        var paths = new EnginePaths();
        if (!paths.DistributionRegistered)
        {
            return new RepairOutcome(
            [
                new RepairStep(
                    "find the distribution",
                    false,
                    $"{paths.DistributionName} is not registered, so there is no filesystem to check"),
            ]);
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

        report(new RepairStep(
            "stop the engine", true, "taking it down so its containers are stopped rather than cut"));

        new EngineLifecycle(new Wsl(), new WslDaemonProcess(), new WslSocatBackend())
            .StopAsync(EngineLifecycle.PatientGrace).GetAwaiter().GetResult();

        var repair = new FilesystemRepair(new Wsl(), paths, VmHold.On);
        return write ? repair.Fix(rootfs, report) : repair.Check(rootfs, report);
    }
}
