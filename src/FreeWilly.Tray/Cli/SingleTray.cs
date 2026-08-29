namespace FreeWilly.Tray.Cli;

/// <summary>
/// One tray per session, and what a second launch does instead of starting another (DD81).
/// </summary>
/// <remarks>
/// Nothing held this before, so every extra click was another process: another icon in the overflow,
/// another event stream open on the daemon, and another window. DD80 made that easier to reach —
/// a bare launch now opens a window, so a user who clicks twice gets two of everything.
///
/// <para><b>The tray surface only.</b> The console verbs stay concurrent: an agent running
/// <c>read context</c> while the tray is open must not be refused, so this is claimed inside the
/// tray branch of <c>Main</c> and never around the whole of it.</para>
///
/// <para><b>Local rather than global.</b> An unprefixed name lives in the session's own namespace,
/// so two people logged into one machine each get a tray — which is what they each installed.</para>
///
/// <para><b>The second instance reports nothing to the screen.</b> A double click from Explorer has
/// no console to print into, and a message box on every accidental double click would be worse than
/// the silence DD80 fixed. It signals the live instance, which raises its window, and exits zero:
/// that is the message, and it is what every Windows application does. Where a console is attached
/// the caller typed a command and expects prose, so one line goes to standard error there.</para>
/// </remarks>
internal sealed class SingleTray : IDisposable
{
    /// <summary>The name both halves agree on. Unprefixed, so it is this session's.</summary>
    /// <remarks>
    /// Internal rather than private since DD103: the suite claims this very object, so the message
    /// it prints when a running tray already holds it has to name the object and not a second
    /// spelling of it.
    /// </remarks>
    internal const string Name = "FreeWilly.tray";

    /// <summary>What a second launch sets to ask the live one to show itself.</summary>
    private const string RaiseName = "FreeWilly.tray.raise";

    /// <summary>What <c>--quit</c> sets to ask the live one to go away (DD121).</summary>
    /// <remarks>
    /// A second object rather than a flag beside the first, because an auto-reset event carries no
    /// payload: one handle serving both would make "show yourself" and "close yourself" the same
    /// signal, and the uninstaller would raise a window on its way to deleting it.
    /// </remarks>
    private const string QuitName = "FreeWilly.tray.quit";

    /// <summary>What a <c>docker-desktop://</c> link sets to open a build in the live one (DD126).</summary>
    /// <remarks>
    /// A third object, for the reason the second one was added: an auto-reset event carries no
    /// payload, so a handle shared with <see cref="RaiseName"/> would make "show yourself" and "show
    /// this build" the same signal. The ref itself travels beside it in
    /// <see cref="Core.Builds.BuildHandoff"/>, which is what an event cannot carry.
    /// </remarks>
    private const string BuildName = "FreeWilly.tray.build";

    /// <summary>
    /// What a verb sets to say the stop about to happen was asked for (DD213).
    /// </summary>
    /// <remarks>
    /// A fourth object for the reason there are three: an auto-reset event carries no payload, so
    /// anything sharing a handle with one of the others would be the same signal saying a different
    /// thing. This one is the tray's half of what <c>FreeWilly.engine.stop</c> already tells the host
    /// (DD136) — two listeners with two different decisions to make, and a tray is very often running
    /// with no host of its own in the same process.
    /// </remarks>
    private const string StopName = "FreeWilly.tray.enginestop";

    private readonly Mutex _held;
    private readonly EventWaitHandle _raise;
    private readonly EventWaitHandle _quit;
    private readonly EventWaitHandle _build;
    private readonly EventWaitHandle _engineStop;
    private readonly CancellationTokenSource _stopping = new();

    private SingleTray(
        Mutex held,
        EventWaitHandle raise,
        EventWaitHandle quit,
        EventWaitHandle build,
        EventWaitHandle engineStop)
    {
        _held = held;
        _raise = raise;
        _quit = quit;
        _build = build;
        _engineStop = engineStop;
    }

    /// <summary>
    /// Take the one tray slot, or report that something else has it.
    /// </summary>
    /// <param name="only">The claim, which has to be disposed, or null.</param>
    /// <returns><see langword="true"/> where this process is the tray.</returns>
    internal static bool TryClaim(out SingleTray? only)
    {
        var mutex = new Mutex(initiallyOwned: false, Name);
        bool mine;
        try
        {
            mine = mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // The previous holder died without releasing it. The wait still succeeded — this process
            // owns it now — and treating that as "somebody else has the tray" would leave a machine
            // that crashed once unable to start one again until it was restarted.
            mine = true;
        }

        if (!mine)
        {
            mutex.Dispose();
            only = null;
            return false;
        }

        only = new SingleTray(
            mutex,
            new EventWaitHandle(false, EventResetMode.AutoReset, RaiseName),
            new EventWaitHandle(false, EventResetMode.AutoReset, QuitName),
            new EventWaitHandle(false, EventResetMode.AutoReset, BuildName),
            new EventWaitHandle(false, EventResetMode.AutoReset, StopName));
        return true;
    }

    /// <summary>Ask whatever holds the tray to show its window.</summary>
    /// <remarks>
    /// Opening the event by name rather than creating one: if it is not there the holder is between
    /// claiming the mutex and creating it, which is a window of microseconds, and a launch that
    /// silently did nothing is better than one that throws at a user who double-clicked.
    /// </remarks>
    internal static void RaiseTheLiveOne() => _ = Signal(RaiseName);

    /// <summary>Ask whatever holds the tray to close itself (DD121).</summary>
    /// <returns>
    /// <see langword="true"/> where something was there to hear it. False is not a failure: a
    /// machine with no tray running is already in the state the caller wanted.
    /// </returns>
    internal static bool AskTheLiveOneToQuit() => Signal(QuitName);

    /// <summary>Ask whatever holds the tray to open the build left in the handoff (DD126).</summary>
    /// <returns>
    /// <see langword="true"/> where something was there to hear it. False means this process should
    /// become the tray itself rather than exiting, or the link would do nothing at all.
    /// </returns>
    internal static bool AskTheLiveOneToShowABuild() => Signal(BuildName);

    /// <summary>
    /// Tell whatever holds the tray that the stop about to happen was asked for (DD213).
    /// </summary>
    /// <returns>
    /// <see langword="true"/> where a tray was there to hear it. False is not a failure: with no
    /// tray running there is nothing that could announce the stop as an outage.
    /// </returns>
    /// <remarks>
    /// The other half of what DD210 gave the window. The window says this in process, because it is
    /// the tray; a verb cannot, and a tray watching an engine that went away has fifteen seconds of
    /// patience and then a balloon about a stop the user typed themselves.
    /// </remarks>
    internal static bool AskTheLiveOneToExpectAStop() => Signal(StopName);

    /// <summary>Set one of the two named events, if anything is listening on it.</summary>
    private static bool Signal(string name)
    {
        try
        {
            using var handle = EventWaitHandle.OpenExisting(name);
            return handle.Set();
        }
        catch (Exception exception) when (exception is WaitHandleCannotBeOpenedException
            or UnauthorizedAccessException)
        {
            // Nothing to signal, or another session's. Either way this process is not the tray and
            // has nothing useful left to do.
            return false;
        }
    }

    /// <summary>
    /// Wait for the tray slot to come free, which is the tray actually being gone (DD121).
    /// </summary>
    /// <param name="budget">How long to wait before answering no.</param>
    /// <returns><see langword="true"/> where nothing holds the slot any more.</returns>
    /// <remarks>
    /// The mutex and not the signal is what is watched, because the signal only says the request was
    /// delivered. What the uninstaller needs to know is that the file is no longer open, and the slot
    /// is released in <see cref="Dispose"/> — after the message loop has ended and the process is on
    /// its way out. A caller told "quit" too early deletes into a lock and reports success.
    /// </remarks>
    internal static bool WaitUntilTheTrayIsGone(TimeSpan budget)
    {
        var deadline = DateTime.UtcNow + budget;
        while (true)
        {
            if (TryClaim(out var free))
            {
                // Claimed only to prove it was claimable. Held for any longer and a tray relaunched
                // by a user in the same second would find the slot taken by a probe.
                free!.Dispose();
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(100));
        }
    }

    /// <summary>Run <paramref name="raise"/> whenever a second launch asks for the window.</summary>
    /// <param name="raise">
    /// What shows the window. It is called off the UI thread, so it has to marshal — which
    /// <c>TrayApplication</c> already does with the context it keeps for the event stream.
    /// </param>
    internal void OnRaise(Action raise) => Listen(_raise, "freewilly-tray-raise", raise);

    /// <summary>Run <paramref name="quit"/> when <c>--quit</c> asks the tray to go (DD121).</summary>
    /// <param name="quit">
    /// What ends the tray. Called off the UI thread, exactly like the raise above, so it has to
    /// marshal — ending the message loop from a background thread is not the same thing as ending it.
    /// </param>
    internal void OnQuit(Action quit) => Listen(_quit, "freewilly-tray-quit", quit);

    /// <summary>Run <paramref name="show"/> when a build link asks for one (DD126).</summary>
    /// <param name="show">
    /// What opens the build. Called off the UI thread like the other two, and it reads the ref from
    /// the handoff itself — the signal only says one is waiting.
    /// </param>
    internal void OnBuild(Action show) => Listen(_build, "freewilly-tray-build", show);

    /// <summary>Run <paramref name="expect"/> when a verb announces a stop (DD213).</summary>
    /// <param name="expect">
    /// What the tray does about a stop it did not make itself. Called off the UI thread like the
    /// other three, and it touches the icon and the journal, so it has to marshal.
    /// </param>
    internal void OnEngineStopAsked(Action expect) =>
        Listen(_engineStop, "freewilly-tray-enginestop", expect);

    /// <summary>Watch one named event until the claim is disposed.</summary>
    private void Listen(EventWaitHandle signal, string name, Action act)
    {
        ArgumentNullException.ThrowIfNull(act);

        // A background thread, so it cannot hold the process open by itself: quitting the tray ends
        // the message loop, and this must not outlive it.
        var listening = new Thread(() =>
        {
            var handles = new WaitHandle[] { signal, _stopping.Token.WaitHandle };
            while (WaitHandle.WaitAny(handles) == 0)
            {
                act();
            }
        })
        {
            IsBackground = true,
            Name = name,
        };

        listening.Start();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _stopping.Cancel();
        _held.ReleaseMutex();
        _held.Dispose();
        _raise.Dispose();
        _quit.Dispose();
        _build.Dispose();
        _engineStop.Dispose();
        _stopping.Dispose();
    }
}
