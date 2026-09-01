using FreeWilly.Core.Api;
using FreeWilly.Core.Engine;
using FreeWilly.Core.Releases;
using FreeWilly.Core.Settings;
using FreeWilly.Tray.Ui;

namespace FreeWilly.Tray;

/// <summary>
/// The tray icon. Where this tool lives between tasks, because "is Docker up?" should be a glance.
/// </summary>
internal sealed class TrayApplication : ApplicationContext
{
    private readonly NotifyIcon _icon = new();
    private readonly TrayMenu _menu;
    private readonly EngineHolder _holder;
    private readonly DockerApi _api = new();
    private readonly EngineEvents _events;
    private readonly SynchronizationContext _ui;
    private readonly TrayScale _scale;
    private readonly EnginePaths _paths = new();
    private readonly ReleaseWatch _releases;

    /// <summary>
    /// The engine's journal, which this process writes to as well since DD163.
    /// </summary>
    /// <remarks>
    /// The tray is the only thing on the machine that knows two facts the host cannot: that the
    /// event stream went quiet, and that a human then pressed something. On 21 August 2026 those
    /// were the first and last events of the failure — the engine stopped answering at 14:35 and
    /// somebody clicked Start at 15:45 — and neither reached the file, so an hour of a machine
    /// doing nothing looked from the journal like a host that had simply finished.
    ///
    /// <para>The same file and not a second one. See <see cref="EngineHostLog"/>: two writers is
    /// what the pen in there is for, and one timeline is the whole point of writing any of this
    /// down.</para>
    /// </remarks>
    private readonly EngineHostLog _journal = EngineHostLog.BesideTheInstall();
    private Icon? _worn;
    private TraySettings _settings;
    private bool _startRequested;
    private bool _engineToldToStop;

    /// <summary>
    /// Whether the engine going away is something somebody asked for (DD164).
    /// </summary>
    /// <remarks>
    /// The one thing separating an engine that failed from an engine that was stopped, and the tray
    /// is the only place that knows it — from the event stream the two are the same disconnection.
    /// It exists so the balloon that announces a failure is not also shown to somebody who has just
    /// pressed Stop engine, which would be the tool reporting its own obedience as a fault.
    /// </remarks>
    private bool _stopAsked;
    private CancellationTokenSource? _landing;

    /// <summary>
    /// The wait between the engine going and the balloon that says so (DD183).
    /// </summary>
    /// <remarks>
    /// Cancelled the moment the engine answers, which is what makes a self-healing blip cost the
    /// user nothing at all rather than a notification and a correction.
    /// </remarks>
    private CancellationTokenSource? _grace;
    private EngineState _shown = EngineState.Stopped;
    private AvailableRelease? _available;
    private bool _installing;

    /// <summary>Construct the tray.</summary>
    /// <param name="openWindow">
    /// Whether to show the window straight away. A shortcut wants this, and so does a user whose
    /// icon Windows filed into the overflow, where the menu is a click away rather than in sight.
    /// </param>
    internal TrayApplication(bool openWindow = false)
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _holder = new EngineHolder(EngineHolder.ThisProcess(), new DetachedLauncher());

        // Read before the menu is built, because the boxes have to open showing what is true (DD135).
        _settings = TraySettings.Read(_paths.Settings);

        // Built by TrayMenu rather than here, so the menu `--show-menu` photographs is this one and
        // not a second one built for the camera (DD67).
        _menu = new TrayMenu(
            StartEngine, StopEngine, OpenWindow, Quit, _settings, Save, InstallTheUpdate);
        _icon.ContextMenuStrip = _menu.Strip;

        // Constructed whatever the setting says and started only where it is on, because the setting
        // can be turned on later in the same session and the watch is what has to notice (DD154). The
        // callback is posted: it reveals a menu item and may show a balloon, and both are the UI
        // thread's — the timer's is not.
        _releases = new ReleaseWatch(
            (release, first) => _ui.Post(_ => Offer(release, first), null));

        // The primary button, which the icon answered with nothing at all until DD140. NotifyIcon
        // raises the context menu on the secondary button by itself and does nothing whatsoever on
        // the other, so the one surface this product keeps on screen all day ignored the click every
        // tray tool teaches a user to make — which reads as an icon that is broken rather than as a
        // menu hiding under the other button.
        //
        // MouseClick and not Click, because Click fires for the secondary button too: the same
        // gesture has already raised the menu, and the window would arrive behind the popup that
        // asked for it. A double click reaches this twice and is harmless — the second is the
        // activate branch of OpenWindow, which is what a second launch does anyway (DD81).
        _icon.MouseClick += (_, click) =>
        {
            if (click.Button is MouseButtons.Left)
            {
                OpenWindow();
            }
        };

        // DD172. claude-tray installs from this click, and the balloon is the only thing on screen at
        // the moment the news arrives — the menu item is an icon, a right-click and a read away.
        // Guarded on the balloon showing now rather than on a release existing: this surface also
        // carries failures, and a click on "the engine went away" must not start an install.
        _icon.BalloonTipClicked += (_, _) =>
        {
            if (_balloonOffersUpdate)
            {
                InstallTheUpdate();
            }
        };

        _scale = new TrayScale(() => _ui.Post(_ => Show(_shown), null));

        // The image and the tooltip BEFORE visibility, and the order is the whole of DD82. Setting
        // Visible is what emits the shell's notify-add, and Windows persists what that call carried:
        // with the holder still empty the add went out with no icon flag and an empty string, and
        // although the very next line repaired the image with a modify, the tooltip Windows had
        // already stored stayed empty. Measured — this executable's notify-icon settings entry held a
        // zero-length tooltip beside an icon snapshot that decoded fine.
        //
        // It matters because of where the icon lives. DD21 established that Windows files a
        // first-seen icon into the overflow and that nothing here can promote it out, and the
        // overflow flyout labels each entry with exactly that persisted tooltip — so the one surface
        // a user has to read to find this tool was the one naming nothing.
        Show(EngineState.Stopped);
        _icon.Visible = true;

        // The size the icon was just drawn at is only right for the display it was drawn on, and that
        // display changes without the process restarting — a dock, an undock, the scale slider. The
        // watch fires on a thread of its own, so the redraw is posted through the same context the
        // event stream uses; calling Show from there would touch NotifyIcon off the UI thread (DD99).
        _scale.Watch();

        // The exits nobody thinks of as quitting (DD129). A logoff and a shutdown tear this process
        // down without Quit ever running, and the detached `--run` it launched has no parent to
        // notice — so the virtual machine outlived the session that asked for it, which is the same
        // held memory DD128 removed from one exit only. A kill gets no notice and this does not
        // pretend otherwise; what it buys is that signing out at the end of a day leaves nothing up.
        Microsoft.Win32.SystemEvents.SessionEnding += OnSessionEnding;

        // The indicator is the event loop's own connection state: connected exactly when the engine
        // is answering. No timer, and no second definition of "running".
        _events = new EngineEvents(new DockerApiEventSource(_api));
        _events.StateChanged += stream => _ui.Post(_ => Show(TrayState.For(stream, _startRequested)), null);

        // The window is a view of the engine, so what makes it correct is the same stream. Only the
        // events that change a container list ask for a read; the rest would be a poll in disguise.
        // The id travels with it: this event is also what confirms an action the user is waiting on.
        _events.Received += e =>
        {
            if (e.ChangesTheContainerList)
            {
                _ui.Post(_ => _ = _open?.RefreshAsync(e.Id), null);
            }

            // Images are their own list with their own reasons to change, and the window decides
            // whether anybody is looking at them.
            if (e.ChangesTheImageList)
            {
                _ui.Post(_ => _ = _open?.RefreshImagesIfShowingAsync(), null);
            }
        };
        _events.Start();

        if (openWindow)
        {
            OpenWindow();
        }

        // Last in the constructor, and that ordering is load-bearing (DD135). StartEngine complains
        // through the balloon and draws through Show, so both the icon and the menu have to exist
        // before it runs; the event stream has to be started too, or the start that lands has
        // nothing watching to turn the dot green.
        //
        // Through the same method the menu item uses rather than a quiet second path. A start that
        // cannot land — no distribution registered — then says so here exactly as it would if the
        // user had pressed it, which is the one case where starting unasked must not fail silently.
        if (_settings.StartWithTheTray)
        {
            StartEngine();
        }

        // Always, with nothing to enable (DD171). After the engine, and the watch's own delay is on
        // top of that: a release check is the least urgent thing this process does, and the first
        // twenty seconds of a launch may be provisioning a distribution.
        _releases.Start();
    }

    /// <summary>Remember what the menu's box now says (DD135).</summary>
    /// <param name="wanted">Every setting, as the menu has it.</param>
    /// <remarks>
    /// Deliberately does not start or stop the engine. That setting is about the next launch, and a
    /// tick that also started one would make the box a verb — there are already two of those directly
    /// above it, and a user who wanted the engine now would have pressed one.
    ///
    /// <para>It used to arm the release watch too, because that check was a second tick here. DD171
    /// took the tick away and the watch starts with the tray, so there is nothing left for a save to
    /// set in motion.</para>
    /// </remarks>
    private void Save(TraySettings wanted)
    {
        _settings = wanted;
        _settings.Write(_paths.Settings);
    }

    /// <summary>Say a release exists, and offer it (DD154).</summary>
    /// <param name="release">What the watch found.</param>
    /// <param name="announce">
    /// Whether this is the first tick to name this version, which is what separates the balloon from
    /// the menu item: the item stays for as long as the release is newer, and the balloon is said once.
    /// </param>
    private void Offer(AvailableRelease release, bool announce)
    {
        _available = release;
        _menu.Offer(release);

        if (announce)
        {
            Balloon(
                $"FreeWilly {release.Version.ToString(3)} has been released. "
                + "Click here to install it, or use the tray menu.",
                offersUpdate: true);
        }
    }

    /// <summary>Download the offered release, check it, and hand over to its installer (DD154).</summary>
    /// <remarks>
    /// <b>It asks first, and the dialog is the point.</b> Applying an update stops a tray that may be
    /// serving the pipe with containers on it, so this is the one place in the product where a modal
    /// is right: the user pressed the item, and what they are consenting to is not "restart the app"
    /// but "stop the engine". The balloon this class otherwise uses cannot ask a question.
    ///
    /// <para><b>And it never restarts the engine on their behalf.</b> The install goes through
    /// <see cref="Quit"/>, which since DD128 takes the engine down with the tray; what comes back is
    /// whatever "Start engine with FreeWilly" says, which is the user's own standing answer and not a
    /// decision taken here. The dialog says so rather than leaving it to be discovered.</para>
    /// </remarks>
    private void InstallTheUpdate()
    {
        if (_available is not { } release || _installing)
        {
            return;
        }

        var answer = MessageBox.Show(
            $"FreeWilly {release.Version.ToString(3)} will be downloaded from the release page and "
            + "checked against the SHA-256 that release publishes for it. Nothing is run unless the "
            + "digest matches.\n\n"
            + "FreeWilly then closes so it can be replaced, and the engine stops with it, so every "
            + "running container stops too. The engine is not started again on your behalf: it comes "
            + $"back with the tray only if \"{TrayMenu.OnLaunchText.Replace("&", string.Empty, StringComparison.Ordinal)}\" is ticked.\n\n"
            + "Install it now?",
            "FreeWilly",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (answer is not DialogResult.Yes)
        {
            return;
        }

        _installing = true;
        _ = ApplyAsync(release);
    }

    private async Task ApplyAsync(AvailableRelease release)
    {
        FetchedRelease fetched;
        using (var fetcher = new HttpArtefactFetcher())
        {
            fetched = await new ReleaseUpdate(fetcher, ReleaseUpdate.DefaultDirectory)
                .FetchAsync(release)
                .ConfigureAwait(true);
        }

        if (!fetched.Verified || fetched.Path is not { } installer)
        {
            // Said rather than swallowed, and this is the one release-check failure that is worth
            // saying: the user asked for this one, so silence would look like a menu item that does
            // nothing. A digest that did not match is also the most important thing this can find.
            _installing = false;
            Balloon($"The update was not installed. {fetched.Failure}");
            return;
        }

        try
        {
            ReleaseUpdate.Run(installer);
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception
            or InvalidOperationException or System.IO.IOException)
        {
            // Before the quit and not after it, which is the whole reason this is ordered this way: a
            // tray that had already closed itself and then failed to launch Setup would look like an
            // update that shut the product down for nothing.
            _installing = false;
            Balloon($"The update was not installed. {installer} would not start: {failure.Message}");
            return;
        }

        // The engine goes with it, which is what Quit has done since DD128 and what the dialog said
        // would happen. Nothing here brings it back — the installer relaunches the tray, and whether
        // that starts an engine is the user's own standing answer.
        Quit();
    }

    private Ui.MainWindow? _open;

    /// <summary>Show the window, or bring the one already open to the front.</summary>
    /// <summary>Show the window because a second launch asked for it (DD81).</summary>
    /// <remarks>
    /// Called from the thread waiting on the signal, so it marshals onto the UI thread through the
    /// same context the event stream already posts through. Activating from a background thread is
    /// how a window ends up behind the one that asked for it.
    /// </remarks>
    internal void RaiseWindow() => _ui.Post(_ => OpenWindow(), null);

    /// <summary>Close the tray because <c>--quit</c> asked for it (DD121).</summary>
    /// <remarks>
    /// Posted for the same reason <see cref="RaiseWindow"/> is: the signal arrives on a background
    /// thread, and <see cref="Quit"/> hides the notify icon and ends the message loop — both of which
    /// belong to the UI thread. It is the menu item's own exit and not a second one, which since
    /// DD128 is what makes this enough on its own: the engine comes down with the tray, so an
    /// uninstaller that used to have to run <c>--stop</c> as a second step no longer does.
    /// </remarks>
    internal void QuitFromSignal() => _ui.Post(_ => Quit(), null);

    /// <summary>
    /// Open the build a link left in the handoff, raising the window for it (DD126).
    /// </summary>
    /// <remarks>
    /// Posted for the reason <see cref="RaiseWindow"/> is: the signal arrives on a background thread
    /// and every line below touches the window. Taking the ref is what clears it, so a link that
    /// arrives while the window is already open does not leave one behind for the next launch.
    ///
    /// <para>Silent where there is nothing waiting. The signal and the file are written by two
    /// separate calls, and a raced or failed write should leave the window as it was rather than
    /// clearing whatever build is on screen.</para>
    /// </remarks>
    internal void ShowBuildFromSignal() => _ui.Post(_ => ShowPendingBuild(), null);

    /// <summary>Open one build, named directly rather than through the handoff (DD126).</summary>
    /// <param name="reference">The ref, already read out of the link.</param>
    /// <remarks>
    /// The path for the launch that opened the link itself: the ref never left this process, so
    /// there is no file to take. Posted like the signal's, because it runs before the message loop
    /// has started and the window has to be created on it.
    /// </remarks>
    internal void ShowBuild(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        _ui.Post(_ => Open(reference), null);
    }

    private void ShowPendingBuild()
    {
        if (new FreeWilly.Core.Builds.BuildHandoff().Take() is { } reference)
        {
            Open(reference);
        }
    }

    private void Open(string reference)
    {
        OpenWindow();
        _open?.ShowBuild(reference);
    }

    private void OpenWindow()
    {
        if (_open is not null)
        {
            // Restore before activating: a minimised window that is only activated stays minimised,
            // so a second launch would look like nothing happened — which is the whole failure
            // DD81 exists to remove.
            if (_open.WindowState is System.Windows.WindowState.Minimized)
            {
                _open.WindowState = System.Windows.WindowState.Normal;
            }

            _ = _open.Activate();
            return;
        }

        // WPF needs an Application carrying the chrome before a Window can resolve it, and a WinForms
        // process has none. How that is made is Ui.Theme's business and not this one's (DD34).
        Ui.Theme.Apply();

        // The Engine page is handed the tray's own stop and start rather than a start alone (DD210).
        // A filesystem check takes the engine down through the path `--stop` uses, which this
        // process sees only as an engine that went away — so it waited out the blip and announced a
        // failure about a stop the user had just pressed a button for.
        _open = new Ui.MainWindow(
            _api,
            () => _shown,
            StartEngine,
            engine: Ui.EngineDestination.OnThisMachine(
                new TrayInterlude(() => ExpectTheEngineDown(ThePage), StartEngine)));
        _open.Closed += (_, _) => _open = null;
        _open.Show();
        _ = _open.RefreshAsync();
    }

    private void StartEngine()
    {
        // Asked before anything is shown (DD120). A machine with no distribution has a start that
        // can only fail, and the failure used to be invisible: `--run` printed its refusal onto a
        // hidden console and exited, leaving the dot breathing Starting until the tray was quit.
        var refusal = TrayState.WhyAStartWouldNotLand(
            _paths.DistributionRegistered, _paths.DistributionName);
        if (refusal is not null)
        {
            // In the journal as well as the balloon (DD163). A balloon is gone in eight seconds and
            // a refusal that was never written down is the shape of failure this whole file exists
            // to remove — somebody reads the log a day later and finds a start that never happened.
            _journal.Say($"{"tray",-8}  a start was asked for and refused: {refusal}");
            Complain(refusal);
            return;
        }

        _journal.Say($"{"tray",-8}  a start was asked for");
        _stopAsked = false;
        _startRequested = true;
        Show(EngineState.Starting);

        var failure = _holder.Start();
        if (failure is not null)
        {
            _journal.Say($"{"tray",-8}  the host would not launch: {failure}");
            Complain(failure);
            return;
        }

        WatchTheStart();
    }

    /// <summary>
    /// Stop claiming a start is coming once it has had longer than the engine gives itself.
    /// </summary>
    /// <remarks>
    /// The stream is still the only definition of running, and nothing here polls the engine — this
    /// decides one thing the stream cannot, which is how long a request may outlive the evidence for
    /// it. Every way a launched start dies quietly ends up here: the daemon exits, the pipe is taken
    /// by something else, the distribution will not boot.
    ///
    /// <para>A timer that is cancelled rather than one that is checked, so a start that lands leaves
    /// nothing running behind it. The continuation posts through the same context the event stream
    /// uses, because <see cref="Show"/> touches the NotifyIcon and that is the UI thread's.</para>
    /// </remarks>
    private void WatchTheStart()
    {
        StopWatchingTheStart();

        var watch = new CancellationTokenSource();
        _landing = watch;
        _ = Task.Delay(TrayState.StartBudget, watch.Token).ContinueWith(
            _ => _ui.Post(_ => GiveUpOnTheStart(), null),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    private void StopWatchingTheStart()
    {
        _landing?.Cancel();
        _landing?.Dispose();
        _landing = null;
    }

    private void GiveUpOnTheStart()
    {
        // The engine may have landed between the delay elapsing and this reaching the UI thread, and
        // a stop pressed in the same window is the same question. Either way the request is over and
        // there is nothing to complain about.
        if (!_startRequested)
        {
            return;
        }

        var gaveUp = TrayState.StartDidNotLand(TrayState.StartBudget, _paths.HostLog);
        _journal.Say($"{"tray",-8}  {gaveUp}");
        Complain(gaveUp);
    }

    /// <summary>
    /// Say what went wrong without dying of it.
    /// </summary>
    /// <remarks>
    /// A balloon rather than a dialog: the tray has no window of its own to parent one to, and a
    /// modal from an icon in the corner is a jump scare. Silence is not the alternative — an
    /// unhandled exception in a click handler was the previous behaviour, and it took the tray with
    /// it, so pressing a menu item made the icon disappear.
    /// </remarks>
    private void Complain(string? failure)
    {
        if (failure is null)
        {
            return;
        }

        _startRequested = false;
        StopWatchingTheStart();
        Show(_shown);
        Balloon(failure);
    }

    /// <summary>Say something from the corner, without claiming anything went wrong.</summary>
    /// <param name="text">What to say.</param>
    /// <param name="offersUpdate">
    /// Whether clicking this balloon installs the release it is about (DD172). Default false, and
    /// that default is the guard: every other caller says a failure, and a click on one of those
    /// must not start an install.
    /// </param>
    /// <remarks>
    /// <see cref="Complain"/>'s balloon, lifted out when DD154 needed the same surface for news that
    /// is not a failure: a release exists, or the update the user asked for did not verify. What
    /// Complain adds around this is the state repair — a start that failed is no longer pending — and
    /// that is exactly what an announcement must not do.
    /// </remarks>
    private void Balloon(string text, bool offersUpdate = false)
    {
        // Set on every balloon and not only on the one that offers, because what the click has to
        // know is which balloon is on screen now — a release announced an hour ago must not make a
        // click on this afternoon's failure install anything.
        _balloonOffersUpdate = offersUpdate;
        _icon.BalloonTipTitle = "FreeWilly";
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(8000);
    }

    /// <summary>Whether the balloon currently on screen is the one announcing a release (DD172).</summary>
    private bool _balloonOffersUpdate;

    /// <summary>
    /// The engine is going down because somebody asked, not because it failed (DD210, DD213).
    /// </summary>
    /// <param name="asker">
    /// Who asked, as the journal line reads. Named rather than left generic because the two are
    /// different mornings-after: one is a button in this window, the other is a command somebody
    /// typed at a prompt, and a reader working out why the engine went down at 14:35 wants to know
    /// which.
    /// </param>
    /// <remarks>
    /// <see cref="StopEngine"/> without the stop, because the caller does the stopping: a filesystem
    /// check takes the engine down through the same route <c>--stop</c> takes and then holds the
    /// distribution terminated for as long as <c>e2fsck</c> runs. All this owes is the half
    /// <see cref="StopEngine"/> does beyond stopping, which is the half that decides what the
    /// disappearance means.
    ///
    /// <para>The icon is left to the event stream. It goes grey when the engine actually stops
    /// answering, which is true and is the same way every other stop reaches it; asserting it here
    /// would be this process claiming an outcome a second before it has one.</para>
    /// </remarks>
    private void ExpectTheEngineDown(string asker)
    {
        _journal.Say($"{"tray",-8}  {asker} asked for the engine to stop");
        _stopAsked = true;
        _startRequested = false;
        StopWatchingTheStart();
        StopWaitingOutTheBlip();
    }

    /// <summary>What the window's own interruption is called in the journal (DD213).</summary>
    internal const string ThePage = "the Engine page";

    /// <summary>What a verb's is called (DD213).</summary>
    internal const string ACommand = "a FreeWilly command";

    /// <summary>
    /// A verb somewhere on this session is about to take the engine down (DD213).
    /// </summary>
    /// <remarks>
    /// Posted for the reason <see cref="RaiseWindow"/> is: the signal arrives on a background thread
    /// and this touches the journal and the timers the UI thread owns.
    ///
    /// <para>It does not start the engine again at the other end, and there is no other end to hear
    /// about. That is the decision DD213 settles rather than leaves silent: a command is a scripting
    /// surface, and it leaves the machine where the script asked for it to be. The window starts the
    /// engine back because it interrupted somebody who had not asked for an interruption; a person
    /// who typed <c>--stop</c> asked for exactly this, and <c>--fsck</c> ends by naming the command
    /// that starts it again (DD205).</para>
    /// </remarks>
    internal void ExpectTheEngineDownFromSignal() =>
        _ui.Post(_ => ExpectTheEngineDown(ACommand), null);

    private void StopEngine()
    {
        _journal.Say($"{"tray",-8}  a stop was asked for");
        _stopAsked = true;
        _startRequested = false;
        StopWatchingTheStart();

        // A stop somebody asked for is not an outage (DD183). The callback checks _stopAsked and
        // would decline anyway; dropping the timer here means the tool is not holding a pending
        // announcement about an engine the user themselves put down.
        StopWaitingOutTheBlip();
        Show(EngineState.Stopped);
        Complain(_holder.Stop());
    }

    private void Show(EngineState state)
    {
        if (state is EngineState.Running)
        {
            _startRequested = false;

            // It landed, so the budget has nothing left to decide. Cancelled rather than left to
            // elapse into a check that finds nothing: a tray left open all day would otherwise hold
            // one of these per start it ever made.
            StopWatchingTheStart();

            // And the engine answering is the whole of what makes an outage a blip (DD183). The
            // check inside the callback would catch this too; cancelling here is what keeps a tray
            // that flaps all afternoon from holding one pending timer per spell.
            StopWaitingOutTheBlip();
        }

        var changed = _shown != state;
        var was = _shown;
        _shown = state;

        if (changed)
        {
            NoteTheEngineMoved(was, state);
        }

        // Asked here rather than inside Icon, so what the watch compares against is the size that
        // actually reached the shell and not a size something intended to use (DD99).
        var next = StateIcon.Icon(state, _scale.Drawing());
        _icon.Icon = next;

        // The previous icon owns an unmanaged handle from GetHicon; replacing it without destroying
        // it leaks one per state change, and this changes state whenever the engine does.
        _worn?.Dispose();
        _worn = next;

        _icon.Text = StateIcon.TooltipFor(state);
        _menu.Reflect(state);

        // The window shows the engine state too, and its empty state depends on it.
        if (changed)
        {
            _ = _open?.RefreshAsync();
        }
    }

    /// <summary>
    /// Write down that the engine came or went, as this process saw it (DD163).
    /// </summary>
    /// <param name="was">What was being shown.</param>
    /// <param name="now">What is being shown instead.</param>
    /// <remarks>
    /// <b>Transitions only, and only the two that are facts.</b> Running and Stopped are the event
    /// stream's own connection state — connected exactly when the engine is answering, which is the
    /// definition the whole tray is built on — so a move between them is something that happened to
    /// the engine. Starting is not: it means this tray asked for a start and has no evidence yet,
    /// which is already written by the line that asked, and recording it here would put a second
    /// entry in the file for one event.
    ///
    /// <para>This is deliberately a second opinion rather than a report. The host writes what its
    /// own polls found; this writes what a client connected to the pipe found, and the two
    /// disagreeing is itself worth reading — an engine the host believes is up while nothing on the
    /// machine can reach it is DD142's failure, and it has no other witness.</para>
    /// </remarks>
    private void NoteTheEngineMoved(EngineState was, EngineState now)
    {
        var line = now switch
        {
            EngineState.Running => "the engine is answering",
            EngineState.Stopped when was is EngineState.Running => "the engine stopped answering",

            // Stopped arriving from Starting is a start that did not land, and it is left to the
            // budget in WatchTheStart to report — that line carries how long it waited, which is
            // the part a reader needs and this branch does not know.
            _ => null,
        };

        if (line is not null)
        {
            _journal.Say($"{"tray",-8}  {line}");
        }

        // Said out loud, once, and only for an engine that went away on its own (DD164). The host
        // now keeps trying rather than exiting, which is the behaviour the user wanted and also the
        // one that could be mistaken for nothing happening — a tray that silently sits on Stopped
        // while a hidden process works is how somebody ends up clicking Start on an engine that was
        // already being restarted.
        //
        // Armed rather than shown, since DD183. The line above is written at the crossing and stays
        // there, because the journal wants the event dated; the announcement is the one that has to
        // wait, because it costs somebody their attention and a crossing is not yet an outage.
        if (now is EngineState.Stopped && was is EngineState.Running && !_stopAsked)
        {
            WaitOutTheBlip();
        }
    }

    /// <summary>
    /// Hold the announcement back until the engine has been gone long enough to be worth one
    /// (DD183).
    /// </summary>
    /// <remarks>
    /// DD164's reasoning for the balloon holds and is not being undone: a host that keeps trying
    /// instead of exiting is a host that can look like nothing happening, and a grey dot with no
    /// explanation is how somebody ends up clicking Start on an engine already being restarted.
    /// What it did not separate is the outage from the blip. On 24 August 2026 the crossing lasted
    /// ten seconds — gone at 14:01:14, back at 14:01:24 — and the user was pulled away from what
    /// they were doing to be told about a failure that had already been repaired.
    ///
    /// <para>The same shape as <see cref="WatchTheStart"/>, and for the same reasons: a timer that
    /// is cancelled rather than one that is checked, so an engine that comes back leaves nothing
    /// running behind it, and a continuation posted through the event stream's context because
    /// <see cref="Balloon"/> touches the NotifyIcon.</para>
    ///
    /// <para>This process cannot see the host's attempts — they are two processes and the only thing
    /// between them is the pipe — so what is waited out is the clock and not an outcome. That is why
    /// the sentence below says how long the engine has been gone rather than what the host has tried:
    /// the tray knows the first exactly and the second not at all, and claiming the second would be
    /// the mistake DD175 removed from the host's own line.</para>
    /// </remarks>
    private void WaitOutTheBlip()
    {
        StopWaitingOutTheBlip();

        var grace = new CancellationTokenSource();
        _grace = grace;
        _ = Task.Delay(EngineRevival.BlipGrace, grace.Token).ContinueWith(
            _ => _ui.Post(_ => SayTheEngineIsStillGone(), null),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    private void StopWaitingOutTheBlip()
    {
        _grace?.Cancel();
        _grace?.Dispose();
        _grace = null;
    }

    private void SayTheEngineIsStillGone()
    {
        // It may have come back between the delay elapsing and this reaching the UI thread, and a
        // stop pressed in that window is the same question — either way there is nothing to
        // announce, which is the check GiveUpOnTheStart makes for the same reason.
        if (_shown is EngineState.Running || _stopAsked)
        {
            return;
        }

        Balloon(
            $"The engine has not answered for {EngineRevival.BlipGrace.TotalSeconds:0} seconds. "
            + "FreeWilly is trying to bring it back, and keeps trying until it does, so there is "
            + "nothing to click.");
    }

    /// <summary>
    /// Quit the tray and take the engine with it (DD128).
    /// </summary>
    /// <remarks>
    /// This used to leave the engine exactly as it was, and the asymmetry was stated as the point: a
    /// container someone else is using should not die because an icon was closed. Measured against
    /// the complaint this project exists about, that trade costs more than it saves — a running
    /// engine holds a WSL2 virtual machine, and the only way to get those gigabytes back was to
    /// remember a second menu item before pressing this one. Somebody who quits has said they are
    /// done.
    ///
    /// <para>It runs the stop the menu item already runs, and deliberately not a second spelling of
    /// it: <see cref="EngineHolder.Stop"/> launches <c>--stop</c>, which stops serving the pipe,
    /// kills the daemon and terminates the distribution. That process is detached and outlives this
    /// one by design, so the icon still goes at once and nothing here waits on a virtual machine
    /// shutting down.</para>
    ///
    /// <para><b>The virtual machine is left to WSL rather than shut down from here.</b> Terminating
    /// the distribution this tool owns is the whole of what this install is entitled to do;
    /// <c>wsl --shutdown</c> would take every other distribution on the machine with it, which is
    /// somebody's Ubuntu shell in another window. With ours gone WSL powers the machine down itself
    /// once nothing else is using it, so the memory comes back either way — the difference is only
    /// whether this tool reaches outside what it owns to make it happen sooner.</para>
    ///
    /// <para>The failure is swallowed rather than shown. Every surface that reports one is on its
    /// way out of existence — the balloon needs an icon that is about to be hidden, and a modal
    /// would hold open the exit the user just asked for.</para>
    /// </remarks>
    private void Quit()
    {
        _journal.Say($"{"tray",-8}  quitting, and the engine goes with it");
        StopTheEngine();
        _icon.Visible = false;
        ExitThread();
    }

    /// <summary>Windows is ending the session, so leave nothing behind (DD129, DD188).</summary>
    /// <param name="sender">Unused.</param>
    /// <param name="ending">Unused; a logoff and a shutdown are answered the same way.</param>
    /// <remarks>
    /// A wait, and it is DD188 that made it one. What was here spawned this executable with
    /// <c>--stop</c> through <c>ShellExecuteEx</c> and returned at once: the shell it routes through
    /// is being torn down at the same moment, and nothing on the machine was left waiting, so no
    /// session ending since DD129 ever produced the Stopped line a Quit produces.
    ///
    /// <para>The icon and the message loop are still left alone. Windows is already taking them,
    /// and touching UI from this callback would be doing it from the wrong thread on the way
    /// out.</para>
    /// </remarks>
    private void OnSessionEnding(object sender, Microsoft.Win32.SessionEndingEventArgs ending)
    {
        // Named before the stop, and the reason is the same one the ending line in the host has
        // (DD163): this is the ordinary way a machine's engine disappears overnight, and until now
        // it left a journal identical to a crash. The reason Windows gave is in the line because a
        // logoff and a shutdown are different things to be reading about the next morning.
        _journal.Say($"{"session",-8}  Windows is ending the session ({ending?.Reason})");

        // The balloon that announces an engine going away on its own must not fire on the way out
        // (DD164), and the Quit that may follow this must not spawn a second stop (DD129).
        _stopAsked = true;
        _engineToldToStop = true;

        _journal.Say(
            $"{"session",-8}  {SessionTeardown.Run(
                new LiveEngineTeardown(_api), () => DateTimeOffset.UtcNow, Thread.Sleep)}");
    }

    /// <summary>Ask the engine to stop, at most once over this tray's life (DD129).</summary>
    /// <remarks>
    /// Guarded because there are now two callers and a session can reach both: Windows raises
    /// <c>SessionEnding</c> and a tray that then processes its own quit would
    /// launch a second <c>--stop</c> against a distribution already terminated. Harmless, but it is
    /// a process spawned during a shutdown for no reason, and the false one would report a failure
    /// nobody could act on.
    /// </remarks>
    private void StopTheEngine()
    {
        if (_engineToldToStop)
        {
            return;
        }

        // Both exits that run this — the menu's Quit and Windows ending the session — are somebody
        // asking, so the balloon that announces an engine going away on its own must not fire on
        // the way out (DD164).
        _stopAsked = true;
        _engineToldToStop = true;
        _ = _holder.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopWatchingTheStart();
            _releases.Dispose();
            _events.DisposeAsync().AsTask().GetAwaiter().GetResult();

            // SystemEvents is static and holds its subscribers alive, so leaving these attached is a
            // leak that outlives the tray they were for — the session hook (DD129) as much as the
            // scale watch, and it is the same static object holding both.
            Microsoft.Win32.SystemEvents.SessionEnding -= OnSessionEnding;
            _scale.Dispose();
            _api.Dispose();
            _icon.Dispose();
            _worn?.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// The one entry point, for the one executable.
/// </summary>
/// <remarks>
/// Every face of this tool is behind here: the tray with no arguments, and each console verb behind
/// its own. What that buys is DD14's whole shape — one file to publish, one to sign, one to install
/// and one to hand somebody. It also removes a failure the tray could not do anything about: it used
/// to look for <c>dockerdesk-engine.exe</c> beside itself, and a copy that arrived without it had a
/// Start engine menu item that could only apologise.
/// </remarks>
internal static class Program
{
    /// <summary>
    /// A ref this process must open itself, where the handoff file could not be written (DD126).
    /// </summary>
    /// <remarks>
    /// The file is how a ref reaches a tray in another process, and it is also the ordinary path
    /// here — the tray takes it a moment later. This field is only the fallback for a machine whose
    /// root is not writable, where the ref never leaves this process and so needs no file at all.
    /// </remarks>
    private static string? _pendingBuild;

    [STAThread]
    private static int Main(string[] args)
    {
        // Before anything, because everything below may start a wsl.exe and the error mode is what
        // a child inherits (DD270). A launch Windows refuses during a session ending puts up a modal
        // box nobody can click, and the teardown then spends its whole budget waiting on a process
        // that has already failed.
        _ = Cli.HardErrorBox.Suppress();

        var route = Cli.CommandLine.Of(args);

        // A build link, which is the shell invoking this install's handler for docker-desktop://
        // (DD126). Resolved before the tray branch because it may end in either place: a running tray
        // is told to open it, and a machine with none becomes one showing it.
        if (route.Surface is Cli.Surface.OpenBuild)
        {
            if (FreeWilly.Core.Builds.BuildAddress.RefIn(route.Arguments[0]) is not { } reference)
            {
                Cli.ParentConsole.Attach();
                Console.Error.WriteLine(
                    $"{Cli.CommandLine.ExecutableName}: that is not a build link.");
                return 2;
            }

            // Written before the signal, so the tray that hears it has something to take.
            var handoff = new FreeWilly.Core.Builds.BuildHandoff();

            if (handoff.Leave(reference) && Cli.SingleTray.AskTheLiveOneToShowABuild())
            {
                return 0;
            }

            // Nothing was listening, so this process becomes the tray and opens the build itself.
            // Falling through rather than exiting is what makes a link work on a machine where the
            // tray is not running, which is most machines most of the time.
            //
            // The file is taken back first. It was written for a reader that turned out not to
            // exist, and leaving it would make the next ordinary launch open a build nobody asked
            // for — the ref travels in this process from here, which needs no file.
            handoff.Take();
            route = route with { Surface = Cli.Surface.Tray, OpenWindow = true };
            _pendingBuild = reference;
        }

        if (route.Surface is Cli.Surface.Tray)
        {
            // One tray per session (DD81). A second launch raises the window of the one already
            // running and exits: two icons and two event streams on one daemon is what every extra
            // click used to buy, and DD80 made that easier to reach by opening a window on a bare
            // launch. The guard is here rather than around the whole of Main because the console
            // verbs stay concurrent — an agent reading while the tray is open must not be refused.
            if (!Cli.SingleTray.TryClaim(out var only))
            {
                Cli.SingleTray.RaiseTheLiveOne();

                // Only where somebody typed a command. From Explorer this attaches to nothing and
                // the line goes nowhere, which is the right amount of noise for a double click.
                Cli.ParentConsole.Attach();
                Console.Error.WriteLine(
                    $"{Cli.CommandLine.ExecutableName}: already running, so raised its window.");
                return 0;
            }

            using (only)
            {
                // The user's DOCKER_CONFIG, repaired before anything is drawn (DD124). The installer
                // writes it beside the PATH entry, so a fresh install arrives correct; this is what
                // reaches an install made before DD124, and what puts the value back if something
                // else cleared it. It writes nothing unless this install is on the user's PATH, and
                // nothing when the value is already right — so the common case is one registry read.
                _ = new DockerConfigEntry().Ensure();

                ApplicationConfiguration.Initialize();

                // Without this, no WPF window this process opens receives a single key press: the
                // pump below is WinForms' and WPF expects its own. See Ui/WpfInputBridge.cs — the
                // filter box was where it showed, and every capture of this window missed it.
                Ui.WpfInputBridge.Install();

                var tray = new TrayApplication(openWindow: route.OpenWindow);
                only!.OnRaise(tray.RaiseWindow);
                only.OnQuit(tray.QuitFromSignal);
                only.OnBuild(tray.ShowBuildFromSignal);

                // DD213. Without this the tray hears nothing about a stop a verb asked for, and
                // fifteen seconds after `freewilly --fsck` takes the engine down it announces the
                // outage the user themselves typed.
                only.OnEngineStopAsked(tray.ExpectTheEngineDownFromSignal);

                // The link this launch arrived on, if it arrived on one (DD126). Only then: reading
                // the handoff on every start would make a file left by an earlier run open a build
                // on an ordinary launch.
                if (_pendingBuild is { } direct)
                {
                    tray.ShowBuild(direct);
                }

                Application.Run(tray);
            }

            return 0;
        }

        // Every remaining surface writes, so the console comes first: attaching after something has
        // already printed means the first lines went to Stream.Null.
        Cli.ParentConsole.Attach();

        switch (route.Surface)
        {
            case Cli.Surface.Agent:
                return Cli.AgentSurface.Run(route.Arguments);
            case Cli.Surface.CaptureWindow:
                return Cli.WindowCapture.Run(route.Arguments);
            case Cli.Surface.DriveWindow:
                return Cli.WindowDriver.Run(route.Arguments);
            case Cli.Surface.ShowMenu:
                return Cli.MenuPreview.Run(route.Arguments);
            case Cli.Surface.Quit:
                return Cli.QuitCommand.Run(route.Arguments);
            case Cli.Surface.Preflight:
                return Cli.PreflightCommand.Run(route.Arguments);
            case Cli.Surface.Engine:
                return Cli.EngineCommand.Run(route.Arguments);
            case Cli.Surface.Version:
                Console.Out.WriteLine(Core.Licensing.BuildVersion.Current);
                return 0;
            case Cli.Surface.Help:
                Console.Out.Write(Cli.CommandLine.HelpText);
                return 0;
            default:
                Console.Error.WriteLine(
                    $"{Cli.CommandLine.ExecutableName}: "
                    + $"unknown argument {string.Join(' ', route.Arguments)}");
                Console.Error.Write(Cli.CommandLine.HelpText);
                return 2;
        }
    }
}
