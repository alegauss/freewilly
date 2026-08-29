using FreeWilly.Core.Agent;
using FreeWilly.Core.Api;
using FreeWilly.Core.Preflight;
using FreeWilly.Core.Preflight.Windows;

namespace FreeWilly.Core.Engine;

/// <summary>
/// The readings taken off the machine this is running on (DD197).
/// </summary>
/// <remarks>
/// <para>Every reading is guarded, and none of them is allowed to take the page down. A panel whose
/// job is describing a machine that is misbehaving is the last place an exception should escape
/// from: what a broken reading shows is <see cref="MachineReport.Unread"/> beside its own name,
/// which is a fact, while a page that failed to render is not.</para>
///
/// <para>The engine is asked last and asked once. Everything above it is a file or a
/// <c>wsl.exe</c> child, and the pipe request is the only one whose failure is itself one of the
/// answers somebody opened this page for.</para>
/// </remarks>
/// <param name="wsl">The WSL command.</param>
/// <param name="paths">Where the distribution and its virtual disk are.</param>
/// <param name="facts">What is installed of WSL, read the way the preflight reads it.</param>
/// <param name="api">
/// The engine, for the one question only it can answer. An <see cref="IEngineReads"/> rather than a
/// concrete client, because the agent surface hands its verbs the engine they are to use and a
/// report that opened its own would reach past the fake daemon every read verb is driven against.
/// </param>
public sealed class LiveMachineReport(
    IWsl wsl, EnginePaths paths, IMachineFacts facts, IEngineReads api) : IMachineReport
{
    /// <summary>The report for the machine this is running on, wired to the real seams.</summary>
    /// <returns>The report.</returns>
    /// <remarks>
    /// Here rather than at the window, because a shell that constructs four collaborators to fill
    /// one seam is the shell growing for a destination again, which <c>ShellAndPagesTests</c> exists
    /// to refuse. Its own <see cref="DockerApi"/> and not the window's <c>IEngineClient</c>: the
    /// report asks the pipe one question, and that interface is the one a fixture also implements,
    /// which would have a captured page reporting an engine that is not there.
    /// </remarks>
    public static IMachineReport OnThisMachine() => new LiveMachineReport(
        new Wsl(), new EnginePaths(), new WindowsMachineFacts(), new DockerApi());

    /// <summary>The same report, reading whichever engine a caller has already opened.</summary>
    /// <remarks>
    /// What <c>read health</c> reaches through. A verb on that surface is handed its engine so the
    /// whole of it can be driven against a fake, and a report that opened a second connection would
    /// be the one read on the surface nothing could put a fake behind.
    /// </remarks>
    public sealed class Reports : IMachineReports
    {
        /// <inheritdoc/>
        public IMachineReport Through(IEngineReads engine) => new LiveMachineReport(
            new Wsl(), new EnginePaths(), new WindowsMachineFacts(), engine);
    }

    /// <inheritdoc/>
    public async Task<MachineHealth> ReadAsync(CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(wsl);
        ArgumentNullException.ThrowIfNull(paths);

        var lifecycle = new EngineLifecycle(
            wsl, new NoDaemon(), new NoBackend(), EnginePipeRelay.DefaultPipeName,
            paths.DistributionName);

        var state = Guarded(lifecycle.ReadState);
        var answering = await Answering(cancellation).ConfigureAwait(false);
        var wrong = Verdict(state, answering.Answered);

        return new MachineHealth(
            wrong is null,
            wrong ?? "wsl, the distribution and the engine are well",
            [
                new MachineGroup("WSL", Wsl(lifecycle, state)),
                new MachineGroup("Filesystem", Filesystem(state)),
                new MachineGroup("Errors", Errors(state)),
                new MachineGroup("Disk", Disk(state)),
                new MachineGroup("Engine", Engine(answering)),
            ]);
    }

    /// <summary>
    /// What is wrong with this machine, or <see langword="null"/> where nothing is (DD198).
    /// </summary>
    /// <param name="state">What the root filesystem said, or null where it would not say.</param>
    /// <param name="answering">Whether the engine answered the pipe.</param>
    /// <returns>The one clause a caller acts on.</returns>
    /// <remarks>
    /// In the order a reader would work down it: nothing under the distribution matters if the
    /// distribution is not there, and an engine that is not answering is the last question rather
    /// than the first, because a read-only root is why it would not be.
    /// </remarks>
    private static string? Verdict(DistributionState? state, bool answering) => state switch
    {
        null => "the distribution is not running, so nothing under it could be read",
        { Writable: false } => "the distribution's root is mounted read-only",
        { Errors: > 0 } read =>
            $"{read.Errors} error(s) are recorded against the distribution's filesystem",
        _ when !answering => "the engine is not answering the pipe",
        _ => null,
    };

    private IReadOnlyList<MachineReading> Wsl(EngineLifecycle lifecycle, DistributionState? state)
    {
        var installed = Guarded(() => facts?.Wsl);
        return
        [
            new MachineReading("version", installed?.Version ?? MachineReport.Unread),
            new MachineReading("kernel", installed?.KernelVersion ?? MachineReport.Unread),
            new MachineReading("distribution", paths.DistributionName),
            new MachineReading(
                "registered",
                Guarded(() => (bool?)lifecycle.DistributionRegistered) switch
                {
                    true => "yes",
                    false => "no",
                    _ => MachineReport.Unread,
                }),
            new MachineReading("running", state is null ? "no" : "yes"),
        ];
    }

    private static IReadOnlyList<MachineReading> Filesystem(DistributionState? state) =>
    [
        new MachineReading("root device", state?.Device ?? MachineReport.Unread),
        new MachineReading("mount options", state?.Options ?? MachineReport.Unread),
        new MachineReading(
            "writable",
            state is null ? MachineReport.Unread : state.Writable ? "yes" : "no, remounted read-only"),
    ];

    /// <remarks>
    /// The counter and the function that recorded the first error, which is the pair that says
    /// whether a fault is current or is one this filesystem carries from before a repair. Both are
    /// this filesystem's own and both are cleared by <c>e2fsck</c>.
    /// </remarks>
    private static IReadOnlyList<MachineReading> Errors(DistributionState? state) =>
    [
        new MachineReading(
            "recorded",
            state?.Errors?.ToString(System.Globalization.CultureInfo.InvariantCulture)
            ?? MachineReport.Unread),
        new MachineReading("first in", state?.FirstError ?? "none"),
        new MachineReading("last in", state?.LastError ?? "none"),
    ];

    /// <remarks>
    /// <para>Four numbers rather than one, and the last is the one nobody thinks to look at. A
    /// distribution cannot grow past the space left on the volume its virtual disk sits on, so a
    /// full Windows drive is a full engine however much room ext4 believes it has.</para>
    ///
    /// <para><b>The second was added by DD225 and it is not a duplicate of the first.</b> A virtual
    /// disk handed back to Windows becomes a sparse file, and a sparse file keeps its length: NTFS
    /// records the ranges nothing wrote to and stops charging for them. So the file goes on saying
    /// fifty gigabytes while the volume charges for twelve, and only one of those two is the number
    /// somebody came to this page about.</para>
    /// </remarks>
    private IReadOnlyList<MachineReading> Disk(DistributionState? state)
    {
        var vhdx = Path.Combine(paths.Distribution, "ext4.vhdx");
        var length = Guarded(() => File.Exists(vhdx) ? (long?)new FileInfo(vhdx).Length : null);
        var charged = Guarded(() => FileOnDisk.Bytes(vhdx));
        var free = Guarded(() =>
            (long?)new DriveInfo(Path.GetPathRoot(paths.Root) ?? paths.Root).AvailableFreeSpace);

        return
        [
            new MachineReading(
                "virtual disk", length is { } size ? MachineReport.Size(size) : MachineReport.Unread),

            // Named to pair with the row below it: the same word either side of the boundary, so
            // the two are read as one comparison rather than as two unrelated figures.
            new MachineReading(
                "used on Windows",
                charged is { } cost ? MachineReport.Size(cost) : MachineReport.Unread),
            new MachineReading(
                "used inside",
                state?.UsedKb is { } used ? MachineReport.Size(used * 1024) : MachineReport.Unread),
            new MachineReading(
                "free on Windows",
                free is { } spare ? MachineReport.Size(spare) : MachineReport.Unread),
        ];
    }

    /// <summary>Ask the engine the one question only it can answer.</summary>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>Whether it answered, and what version it said it was.</returns>
    /// <remarks>
    /// Asked once and shared between the verdict and the reading, because a page that pinged twice
    /// could show an engine answering under a verdict saying it was not.
    /// </remarks>
    private async Task<(bool Answered, string? Version)> Answering(CancellationToken cancellation)
    {
        if (api is null)
        {
            return (false, null);
        }

        try
        {
            if (!await api.PingAsync(cancellation).ConfigureAwait(false))
            {
                return (false, null);
            }

            return (true, (await api.VersionAsync(cancellation).ConfigureAwait(false)).ApiVersion);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The engine not answering is one of the answers this page exists to give, so it is a
            // reading rather than a failure.
            return (false, null);
        }
    }

    private static IReadOnlyList<MachineReading> Engine((bool Answered, string? Version) engine) =>
    [
        new MachineReading(
            "pipe",
            engine.Answered
                ? $@"answers on \\.\pipe\{EnginePipeRelay.DefaultPipeName}"
                : "no answer"),
        new MachineReading("API version", engine.Version ?? MachineReport.Unread),
    ];

    /// <summary>Take one reading, or nothing where taking it went wrong.</summary>
    /// <typeparam name="T">What is being read.</typeparam>
    /// <param name="read">How to read it.</param>
    /// <returns>What it said, or <see langword="null"/>.</returns>
    private static T? Guarded<T>(Func<T?> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or System.Security.SecurityException
            or ArgumentException or InvalidOperationException)
        {
            return default;
        }
    }

    /// <summary>A daemon handle for a lifecycle that is only ever asked questions.</summary>
    /// <remarks>
    /// The report never starts or stops anything, so the two seams a lifecycle needs to do that are
    /// filled with objects that refuse to. Constructing a real
    /// <see cref="WslDaemonProcess"/> here would put a process launcher behind a page whose whole
    /// contract is that it reports and does not act.
    /// </remarks>
    private sealed class NoDaemon : IDaemonProcess
    {
        public bool Alive => false;

        public string? LastWords => null;

        public void Launch() => throw new NotSupportedException("the report does not start anything");

        public void Stop()
        {
            // Nothing was started, so nothing is stopped. Silent rather than throwing, because
            // EngineLifecycle disposes its daemon and a report must not fail on the way out.
        }

        public void Dispose()
        {
            // Nothing held.
        }
    }

    /// <summary>A backend for the same reason.</summary>
    private sealed class NoBackend : IEngineBackend
    {
        public IEngineChannel Open() =>
            throw new NotSupportedException("the report does not serve the pipe");
    }
}
