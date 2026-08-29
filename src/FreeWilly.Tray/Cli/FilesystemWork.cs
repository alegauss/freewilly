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
    /// <summary>
    /// Everything the Engine destination reads and can start, against this machine.
    /// </summary>
    /// <returns>The seams.</returns>
    /// <remarks>
    /// The three wired in one place because they are one destination's, and because the shell's line
    /// budget refuses to hold a collaborator per seam. Here rather than in Core: the hold on the
    /// virtual machine is a process this assembly owns.
    /// </remarks>
    internal static EngineSeams OnThisMachine() => new(
        new EngineJournalFile(), LiveMachineReport.OnThisMachine(), new FilesystemWork());

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
