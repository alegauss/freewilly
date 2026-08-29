namespace FreeWilly.Tray.Cli;

/// <summary>
/// One engine host per session, and what a second <c>--run</c> does instead of joining it (DD133).
/// </summary>
/// <remarks>
/// <see cref="SingleTray"/> held the tray to one process from DD81 and nothing held <c>--run</c>,
/// which is the half that could do real damage. A second one was not refused and did not fail
/// either: <see cref="Core.Engine.EngineLifecycle.StartAsync"/> finds the pipe already answering and
/// returns Running without launching a daemon or starting a relay, so the duplicate serves nothing
/// at all — and then polls, on a timer, with the authority to run <c>wsl --terminate</c> on the
/// distribution the first one is serving. A process that contributes nothing and can take the engine
/// down is the worst shape available, and it was reachable by clicking Start engine twice.
///
/// <para><b>Session-local, like the tray's.</b> The contended object is really the machine-wide
/// <c>\\.\pipe\docker_engine</c>, so a global name would be the honest scope — but creating one
/// needs a privilege a standard user does not have, and the pipe's own single-account ACL already
/// refuses the other user this would be protecting against. What is left is two hosts under one
/// login, which is the case that was actually observed and which this name covers.</para>
///
/// <para><b>One signal, and it exists because DD136 gave the host a reason to tell two identical
/// observations apart.</b> This used to carry none: a second engine host had nothing to ask, since
/// the one already serving the pipe was the whole of the answer. What changed is that the host now
/// restarts an engine it loses — and <c>--stop</c> terminating the distribution from another process
/// looks, from in here, exactly like WSL2 dying under a suspend. Without something said out loud,
/// the only two behaviours available are a host that ignores <c>--stop</c> and a host that ignores a
/// crash. So the stop is announced, and everything else is still inferred.</para>
/// </remarks>
internal sealed class SingleEngine : IDisposable
{
    /// <summary>The name both halves agree on. Unprefixed, so it is this session's.</summary>
    /// <remarks>
    /// Internal for the reason <see cref="SingleTray.Name"/> is: the suite claims this very object,
    /// and the message it prints when a running engine already holds it has to name the object
    /// rather than a second spelling of it.
    /// </remarks>
    /// <remarks>
    /// Spelled in Core since DD231, because the preflight opens the same object to tell this tool's
    /// own engine from a rival's, and two literals is a probe that stops recognising its own engine
    /// the day one of them is renamed.
    /// </remarks>
    internal const string Name = Core.Engine.EngineHostSlot.Name;

    /// <summary>What <c>--stop</c> sets to tell the live host the stop was asked for (DD136).</summary>
    /// <remarks>
    /// Named like the tray's, and unprefixed for the same reason: the host it is talking to is this
    /// session's. It carries no payload and needs none — the only thing it has to say is "this one
    /// is deliberate", and the teardown itself still happens the way it always did.
    /// </remarks>
    private const string StopName = "FreeWilly.engine.stop";

    private readonly Mutex _held;
    private readonly EventWaitHandle _stop;
    private readonly CancellationTokenSource _stopping = new();

    private SingleEngine(Mutex held, EventWaitHandle stop)
    {
        _held = held;
        _stop = stop;
    }

    /// <summary>
    /// Take the one engine-host slot, or report that something else has it.
    /// </summary>
    /// <param name="only">The claim, which has to be disposed, or null.</param>
    /// <returns><see langword="true"/> where this process is the engine host.</returns>
    internal static bool TryClaim(out SingleEngine? only)
    {
        var mutex = new Mutex(initiallyOwned: false, Name);
        bool mine;
        try
        {
            mine = mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // The previous host died without releasing it — which for this one is the ordinary
            // ending, since a machine that loses power mid-build leaves exactly this. The wait
            // still succeeded and this process owns it now; reading it as "somebody else is
            // serving" would leave a machine that crashed once unable to start its engine again
            // until the user logged out.
            mine = true;
        }

        if (!mine)
        {
            mutex.Dispose();
            only = null;
            return false;
        }

        only = new SingleEngine(
            mutex, new EventWaitHandle(false, EventResetMode.AutoReset, StopName));
        return true;
    }

    /// <summary>
    /// Tell whatever is serving the engine that the stop about to happen was asked for (DD136).
    /// </summary>
    /// <returns>
    /// <see langword="true"/> where a host was there to hear it. False is not a failure — it means
    /// no host is running, so there is nothing that could mistake the teardown for a crash.
    /// </returns>
    /// <remarks>
    /// Opened by name rather than created, like the tray's signals: a handle that is not there means
    /// nothing is listening, which is an answer rather than an error.
    /// </remarks>
    internal static bool TellTheLiveOneToStop()
    {
        try
        {
            using var handle = EventWaitHandle.OpenExisting(StopName);
            return handle.Set();
        }
        catch (Exception exception) when (exception is WaitHandleCannotBeOpenedException
            or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Run <paramref name="stop"/> when <c>--stop</c> announces itself (DD136).</summary>
    /// <param name="stop">
    /// What brings the host down. Called on a thread of its own, so it has to be safe to call from
    /// one — the host's is a cancellation, which is.
    /// </param>
    internal void OnStop(Action stop)
    {
        ArgumentNullException.ThrowIfNull(stop);

        // Background, so it cannot hold the process open by itself. The host's ordinary ending is
        // Ctrl+C, and a foreground thread parked on a handle would outlive it.
        var listening = new Thread(() =>
        {
            var handles = new WaitHandle[] { _stop, _stopping.Token.WaitHandle };
            if (WaitHandle.WaitAny(handles) == 0)
            {
                stop();
            }
        })
        {
            IsBackground = true,
            Name = "freewilly-engine-stop",
        };

        listening.Start();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _stopping.Cancel();
        _held.ReleaseMutex();
        _held.Dispose();
        _stop.Dispose();
        _stopping.Dispose();
    }
}
