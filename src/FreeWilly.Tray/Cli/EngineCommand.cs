using FreeWilly.Core.Api;
using FreeWilly.Core.Engine;
using FreeWilly.Core.Preflight;
using FreeWilly.Core.Preflight.Windows;

namespace FreeWilly.Tray.Cli;

/// <summary>
/// Puts the engine on this machine, unattended. Three modes, because two of the three phases
/// change nothing outside this tool's own directory and are worth being able to run alone.
/// </summary>
internal static class EngineCommand
{
    private const int Ok = 0;
    private const int Failed = 1;
    private const int Usage = 2;

    /// <summary>
    /// How long a session ending waits for the teardown before letting Windows carry on (DD187).
    /// </summary>
    /// <remarks>
    /// Under the five seconds Windows allows a <c>WM_QUERYENDSESSION</c> before it calls the
    /// process hung and offers the user a screen naming it, and long enough for the one call that
    /// matters: <c>wsl --terminate</c>, which is what unmounts the distribution's ext4. A shutdown
    /// that shows the user this tool's name is a worse outcome than a distribution taken down
    /// hard, so the budget is what gives way and the journal says which happened.
    /// </remarks>
    internal static readonly TimeSpan SessionEndingBudget = TimeSpan.FromSeconds(4);

    /// <summary>How often a running host asks whether the distribution's root is still writable.</summary>
    /// <remarks>
    /// Long, deliberately (DD191). The probe is a <c>wsl.exe</c> child, and this loop's own rule
    /// since DD134 is that a subprocess on every poll is the load that times out the ping beside it.
    /// A filesystem does not go read-only twice, so what this interval buys is only how late the
    /// news is, and five minutes is inside the session that broke rather than in the next one.
    /// </remarks>
    internal static readonly TimeSpan FilesystemWatch = TimeSpan.FromMinutes(5);

    /// <summary>Run an engine verb.</summary>
    /// <param name="args">The verb and, for <c>--autostart</c>, its value.</param>
    /// <returns>The process exit code.</returns>
    internal static int Run(string[] args)
    {
        var mode = args.Length == 0 ? "--help" : args[0];

        // --autostart takes a value and --fsck takes a flag; everything else is a verb on its own.
        var allowed = mode is "--autostart" or "--fsck" ? 2 : 1;
        if (args.Length > allowed)
        {
            return Complain($"unexpected argument {args[allowed]}");
        }

        return mode switch
        {
            "--plan" => Plan(),
            "--acquire" => Provision(acquireOnly: true),
            "--provision" => Provision(acquireOnly: false),
            "--status" => Status(),
            "--api" => ApiProbe(),
            "--watch" => Watch(),
            "--run" => RunEngine(),
            "--stop" => Stop(),
            "--fsck" => Fsck(args.Length > 1 ? args[1] : ""),
            "--autostart" => AutostartMode(args.Length > 1 ? args[1] : "status"),
            "-h" or "--help" => Help(Ok),
            _ => Complain($"unknown argument {mode}"),
        };
    }

    private static EngineLifecycle NewLifecycle() => new(
        new Wsl(), new WslDaemonProcess(), new WslSocatBackend());

    private static void Report(EngineStatus status, EngineHostLog? journal = null)
    {
        Note(journal, $"  {status.State,-8}  {status.Detail}");
        if (status.ApiVersion is { } version)
        {
            Note(journal, $"  {"",-8}  Engine API {version}");
        }

        // DD190. Here rather than inside the detail, because a remedy is four commands and a detail
        // is one line: flattening them would produce a sentence nobody can copy. This is the one
        // place every status passes through, so the commands reach the journal the host keeps and
        // the console `--status` is being read at, without either growing its own copy.
        var paths = new EnginePaths();
        if (paths.DistributionRegistered
            && WslFailure.Of(status.Detail, paths.DistributionName, paths.Distribution)
                is { } failure)
        {
            foreach (var line in failure.Remedy)
            {
                Note(journal, $"  {"",-8}  {line}");
            }
        }
    }

    /// <summary>Write out a filesystem reading and the repair it carries (DD191).</summary>
    /// <param name="journal">Where the host writes.</param>
    /// <param name="failure">What was found.</param>
    /// <remarks>
    /// Its own column word, because this is neither a state the engine is in nor something the host
    /// did: the engine is running and the disk under it is not well, and a reader scanning the file
    /// for why a machine went wrong overnight is looking for exactly that distinction.
    /// </remarks>
    private static void NoteFilesystem(EngineHostLog? journal, WslFailure failure)
    {
        Note(journal, $"  {"fs",-8}  {failure.Meaning}");
        foreach (var line in failure.Remedy)
        {
            Note(journal, $"  {"",-8}  {line}");
        }
    }

    /// <summary>Say something the console shows and the journal keeps (DD137).</summary>
    /// <param name="journal">Where the host writes, or <see langword="null"/> for a foreground verb.</param>
    /// <param name="line">What happened.</param>
    /// <remarks>
    /// One call for both, which is the whole of it: the two cannot disagree about what the host saw,
    /// because there is no second place a line is written. The journal is null everywhere except the
    /// detached host — <c>--status</c> is somebody standing at a prompt reading the answer, and a
    /// file recording that they looked is noise in the file that matters.
    /// </remarks>
    private static void Note(EngineHostLog? journal, string line)
    {
        Console.WriteLine(line);
        journal?.Say(line.Trim());
    }

    private static int Status()
    {
        var status = NewLifecycle().StatusAsync().GetAwaiter().GetResult();
        Report(status);
        return status.Usable ? Ok : Failed;
    }

    /// <summary>
    /// Start the engine and stay in the foreground serving the pipe until interrupted.
    /// </summary>
    /// <remarks>
    /// Foreground on purpose. The relay has to outlive the start command — a Linux daemon cannot
    /// create the Windows pipe, so something here must hold it — and a resident background service
    /// is a stated non-goal. So the engine runs for exactly as long as somebody is running it, and
    /// Ctrl+C stops both halves.
    /// </remarks>
    private static int RunEngine()
    {
        // One engine host per session (DD133). Nothing held this before, and a second --run was not
        // refused so much as ignored: it found the pipe already answering, started neither a daemon
        // nor a relay, and settled into a poll loop whose only power was to terminate the
        // distribution the first one was serving. Two clicks of Start engine bought that.
        if (!SingleEngine.TryClaim(out var only))
        {
            // Not a failure. The caller wanted the engine served and it is being served, which is
            // the same reason a second tray launch exits zero.
            Console.Error.WriteLine(
                $"{CommandLine.ExecutableName}: another FreeWilly on this session is already "
                + @"serving \\.\pipe\" + EnginePipeRelay.DefaultPipeName + ".");
            return Ok;
        }

        using (only)
        {
            return Serve(only!);
        }
    }

    /// <summary>The engine host proper, once this process is the one that holds the slot.</summary>
    /// <param name="only">
    /// The claim, which since DD136 also carries the one thing <c>--stop</c> has to say out loud.
    /// </param>
    private static int Serve(SingleEngine only)
    {
        // DD137. Everything below is written to a console this process does not have: the host is
        // launched detached and hidden, so the account of what it saw has been going nowhere. From
        // here down every line goes to both, and the journal is what is left once the window is.
        //
        // First in the method since DD163, because the events either side of the engine start
        // before the engine does — a suspend arriving during the first sixty seconds is exactly the
        // kind of thing this file was missing.
        var journal = EngineHostLog.BesideTheInstall();

        // Who this is, so a reader can tell one run from the next. Without it the file is a
        // continuous stream in which two hosts a day apart are indistinguishable, and a restart of
        // the tool reads as a gap in a single long run.
        Note(
            journal,
            $"  {"host",-8}  serving as pid {Environment.ProcessId} "
            + $"(FreeWilly {Core.Licensing.BuildVersion.Current})");

        using var stopping = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stopping.Cancel();
        };

        // An asked-for stop is the one ending this host must not argue with (DD136). Without it,
        // `--stop` terminating the distribution from another process is indistinguishable in here
        // from WSL2 dying under a suspend — and the whole point of what follows is that the second
        // one gets the engine back.
        var asked = new CancellationTokenSource();
        only.OnStop(asked.Cancel);

        // A resume is not itself a failure, so this only shortens the wait for one. The virtual
        // machine is often gone when the machine comes back while the wsl.exe handle on this side
        // is still perfectly alive, which reads as a healthy daemon that has stopped answering —
        // true, and slow to act on. Setting this makes the next turn of the loop reconcile instead
        // of counting to six first.
        var resumed = new ManualResetEventSlim(false);
        void OnPower(object? _, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            // Written down since DD163, and both ends of it. The host has always acted on a resume
            // and never recorded one, which left the reader unable to tell the failure this whole
            // mechanism exists for — a virtual machine lost to a suspend — from a daemon that died
            // at a desk nobody had left. The two look identical in the file and have different
            // causes, and Windows already says which one it is.
            switch (e.Mode)
            {
                case Microsoft.Win32.PowerModes.Suspend:
                    Note(journal, $"  {"power",-8}  the machine is suspending");
                    break;
                case Microsoft.Win32.PowerModes.Resume:
                    Note(journal, $"  {"power",-8}  the machine came back");
                    resumed.Set();
                    break;
                default:
                    // StatusChange: a battery, a charger, a power plan. Nothing the engine cares
                    // about, and a line every time one moves is the poll this file refuses to be.
                    break;
            }
        }

        // Set once the finally below has written its last line, which is the whole teardown done
        // (DD187). A TaskCompletionSource rather than an event object because the thread waiting on
        // it is Windows', and setting then disposing a ManualResetEventSlim under a waiter is a
        // race this ending cannot afford.
        var torndown = new TaskCompletionSource();

        // Which teardown this host is going to get (DD189). Ctrl+C and an announced `--stop` are
        // somebody at a keyboard and can wait for the containers; a session ending is Windows, which
        // cannot. Read at the moment the stop runs rather than fixed here, because the ending has
        // not happened yet and only one of the two endings changes it.
        var grace = EngineLifecycle.PatientGrace;

        void OnSessionEnding(object? _, Microsoft.Win32.SessionEndingEventArgs e)
        {
            // DD187. The host held the two things a teardown needs and was never told the session
            // was ending: seven endings in the journal between 23 and 28 August 2026 have no
            // Stopped line and no host-is-done line, because this process was killed where it
            // stood. WSL2 then reaped dockerd with the distribution root never unmounted, and the
            // ext4 repaired by hand on 29 August is what that leaves behind.
            //
            // The reason is in the line because a logoff and a shutdown are different things to be
            // reading about the next morning, which is the same argument the tray's line makes.
            Note(journal, $"  {"session",-8}  Windows is ending the session ({e?.Reason})");
            grace = EngineLifecycle.HurriedGrace;
            stopping.Cancel();

            // Waiting is the point. Returning from here tells Windows this process is ready, and a
            // process that says so before terminating the distribution has answered the question
            // wrong: `wsl --terminate` unmounts ext4 and being killed does not.
            //
            // It is also what bounds the wait. Windows treats a slow WM_QUERYENDSESSION as a hung
            // app, so this leans on the budget rather than on the teardown finishing, and says
            // which of the two happened.
            if (!torndown.Task.Wait(SessionEndingBudget))
            {
                Note(
                    journal,
                    $"  {"session",-8}  still tearing down after "
                    + $"{SessionEndingBudget.TotalSeconds:0}s; Windows is not waiting longer");
            }
        }

        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPower;
        Microsoft.Win32.SystemEvents.SessionEnding += OnSessionEnding;

        var lifecycle = NewLifecycle();
        try
        {
            var started = lifecycle.StartAsync(cancellation: stopping.Token)
                .GetAwaiter().GetResult();
            Report(started, journal);
            if (!started.Usable)
            {
                return Failed;
            }

            // Asked once the engine is up and before anything is served, which is the moment the
            // 29 August 2026 start had the answer and nobody looked (DD191). WSL had already said
            // the filesystem needed checking; the mount succeeded, so the start was reported healthy
            // and the read-only remount arrived seconds later.
            if (lifecycle.CheckFilesystem() is { } dirty)
            {
                NoteFilesystem(journal, dirty);
            }

            Console.WriteLine();
            Note(journal, "Serving the engine. Ctrl+C stops it.");

            return Supervise(lifecycle, stopping, asked, resumed, journal, () => grace);
        }
        catch (OperationCanceledException)
        {
            Report(lifecycle.StopAsync(grace).GetAwaiter().GetResult(), journal);
            return Ok;
        }
        finally
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPower;
            Microsoft.Win32.SystemEvents.SessionEnding -= OnSessionEnding;
            resumed.Dispose();
            asked.Dispose();
            lifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();

            // The last line, and it is here for what its absence means (DD163). Every ending this
            // host reaches on its own passes through this finally, so a file whose final line is
            // anything else describes a host that did not end — it was killed, or the machine went
            // down under it. Before this, a journal that simply stopped was the same shape whether
            // the host had walked away deliberately or been shot, and telling those apart was the
            // question DD134 had to answer from Hyper-V events.
            Note(journal, $"  {"host",-8}  this host is done");

            // After that line and not before it, because what the session-ending handler is waiting
            // for is the journal being complete (DD187). Releasing it any earlier would let Windows
            // kill this process between the teardown and the account of it.
            torndown.TrySetResult();
        }
    }

    /// <summary>
    /// Keep the engine up until somebody asks for it to stop, or it proves it cannot be (DD136).
    /// </summary>
    /// <param name="lifecycle">The engine.</param>
    /// <param name="stopping">Ctrl+C.</param>
    /// <param name="asked">A <c>--stop</c> that announced itself.</param>
    /// <param name="resumed">Set when Windows says the machine came back.</param>
    /// <param name="journal">Where this host's account of itself is kept (DD137).</param>
    /// <param name="grace">
    /// How long the containers get to stop, asked at the moment they are stopped: which ending this
    /// turned out to be is not known when the loop starts (DD189).
    /// </param>
    /// <returns>The exit code.</returns>
    /// <remarks>
    /// What this replaced watched for the engine going away and came down with it, which was right
    /// while nothing was allowed to put it back. It is the wrong shape for a laptop: WSL2 does not
    /// survive every suspend, and a host that is awake, polling, and watching the daemon disappear
    /// is the one thing on the machine in a position to do something about it.
    ///
    /// <para><b>Every ending is still reachable.</b> Ctrl+C and an announced <c>--stop</c> come down
    /// at once and restart nothing, because both are somebody saying what they want. Running out of
    /// attempts comes down too, and says so — an engine that cannot start is a fact the user needs,
    /// and a loop that hides it behind another retry is worse than the failure.</para>
    /// </remarks>
    private static int Supervise(
        EngineLifecycle lifecycle,
        CancellationTokenSource stopping,
        CancellationTokenSource asked,
        ManualResetEventSlim resumed,
        EngineHostLog journal,
        Func<TimeSpan> grace)
    {
        var watch = new EngineWatch();
        var revival = new EngineRevival();

        // What the relay had already stumbled over when this loop began, so only a move is news.
        var stumbled = lifecycle.Stumbles;

        // Whether the accept loop's death has been reported for the relay now serving (DD179).
        // Reset where a revival replaces the relay, because the next one can die too and a flag
        // left standing would let the second death go the way the first one used to.
        var mourned = false;

        // When the engine was last answering, so a revival can say how long it was away (DD182).
        var quietSince = DateTimeOffset.UtcNow;

        // When the root was last known writable, and whether it has already been reported (DD191).
        // Said once, like every other crossing in this loop: a filesystem that went read-only stays
        // read-only, and repeating it every five minutes would report a state into a file that only
        // keeps events.
        var wroteLast = DateTimeOffset.UtcNow;
        var complained = false;

        using var ending = CancellationTokenSource.CreateLinkedTokenSource(
            stopping.Token, asked.Token);

        try
        {
            while (!ending.IsCancellationRequested)
            {
                Task.Delay(TimeSpan.FromSeconds(2), ending.Token).GetAwaiter().GetResult();
                var now = lifecycle.StatusAsync(ending.Token).GetAwaiter().GetResult();

                // A resume only skips the waiting. It never decides on its own that the engine is
                // gone — the status still has to say so — because a machine that came back with a
                // perfectly good daemon must not have it restarted for waking up.
                var justResumed = resumed.IsSet;
                if (justResumed)
                {
                    resumed.Reset();
                }

                // DD142. A pipe instance the machine refused is the one event here that leaves the
                // engine perfectly healthy and every docker client on the machine failing, so it is
                // said out loud the moment the count moves rather than left to a reader who thought
                // to ask. Written from inside the quiet path deliberately: it is the only thing a
                // healthy-looking supervisor has to report, and a burst nobody can explain is what
                // this whole task is about.
                if (lifecycle.Stumbles > stumbled)
                {
                    Note(
                        journal,
                        $"  relay     asked twice for a pipe instance "
                        + $"({lifecycle.Stumbles - stumbled} more, {lifecycle.Stumbles} in all)");
                    stumbled = lifecycle.Stumbles;
                }

                // DD179. The stumble above is the relay surviving something; this is the relay
                // having stopped. Nothing puts the accept loop back, so from here on the pipe has no
                // free instance and every docker client on the machine fails together — while the
                // daemon inside the distribution goes on answering, which is what makes this the one
                // failure a healthy-looking supervisor cannot otherwise account for.
                //
                // Said once, for the reason every other crossing in this file is: the loop is gone
                // for the rest of this relay's life, and repeating it every two seconds would report
                // a state where the journal only keeps events.
                if (!mourned && lifecycle.WhatEndedAccepting is { } ended)
                {
                    Note(journal, $"  relay     stopped accepting: {ended}");
                    mourned = true;
                }

                // DD191. On a long interval and never on the poll, which is the rule DD134 and DD175
                // established for this loop: a subprocess every two seconds is the load that times
                // out the ping beside it. Five minutes is cheap enough to be free and short enough
                // that a disk which went read-only is named while the session it broke is still
                // open, rather than being found by the next start.
                if (!complained && DateTimeOffset.UtcNow - wroteLast >= FilesystemWatch)
                {
                    wroteLast = DateTimeOffset.UtcNow;
                    if (lifecycle.CheckRootIsWritable() is { } gone)
                    {
                        NoteFilesystem(journal, gone);
                        complained = true;
                    }
                }

                var serving = watch.KeepServing(now) && !(justResumed && !now.Usable);

                // When the engine was last seen, for the line that closes the incident (DD182).
                // Read off the first poll that missed it rather than off the verdict, because the
                // verdict is six polls and up to thirty seconds late — and taken here rather than
                // inside the quiet branch below so a conclusive reading, which goes straight to the
                // verdict without ever writing a crossing, still dates its own outage.
                if (watch.JustWentQuiet)
                {
                    quietSince = DateTimeOffset.UtcNow;
                }

                if (serving)
                {
                    // DD174. Written from inside the quiet path, and it is the one thing written
                    // there that is not a stumble: the crossing out of a working engine. Placed
                    // after the branch has been taken rather than before it, so a conclusive
                    // reading — which ends the watch on its first poll — reports itself once below
                    // instead of twice, here and there, about the same observation.
                    if (watch.JustWentQuiet)
                    {
                        // The relay's figures travel with it since DD180, and the watch decides
                        // whether they belong: a ping that never got a handle on the pipe was
                        // refused by this process, and a ping the daemon left unanswered was not.
                        Note(journal, $"  {watch.WhenItWentQuiet(now, lifecycle.RelayFigures)}");
                    }

                    continue;
                }

                // Reached only where the watch has decided the engine is not being served, so a
                // line here is always something that happened. The `continue` above is the quiet
                // engine, and it writes nothing — which is what keeps this file worth opening.
                Note(journal, $"  {watch.WhyItStopped(now)}");
                if (!Revive(lifecycle, revival, ending.Token, journal, quietSince))
                {
                    // Reachable only by cancellation since DD164 — Revive no longer runs out of
                    // patience, so the one way it comes back empty-handed is somebody asking this
                    // host to stop. The ending is reported below, the way every other one is.
                    break;
                }

                // Back to a clean slate: the engine answered, so the run of silence that led here
                // describes an engine that no longer exists. The relay is a new one too — Revive
                // stops and starts the lifecycle, which builds another — so what killed the last
                // one's accept loop is said about a relay that is gone, and the next one is owed
                // the same sentence if it dies the same way (DD179).
                watch = new EngineWatch();
                mourned = false;
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
        }

        Report(lifecycle.StopAsync(grace()).GetAwaiter().GetResult(), journal);
        return Ok;
    }

    /// <summary>
    /// Get the engine back, backing off between attempts and then waiting (DD136, DD164).
    /// </summary>
    /// <param name="lifecycle">The engine.</param>
    /// <param name="revival">How long to wait, and whether the quick attempts are spent.</param>
    /// <param name="ending">Cancelled by Ctrl+C or an announced stop.</param>
    /// <param name="journal">Where every attempt is kept (DD137).</param>
    /// <param name="quietSince">When the engine was last answering, for the outage (DD182).</param>
    /// <returns><see langword="true"/> where it came back, <see langword="false"/> on cancellation.</returns>
    /// <remarks>
    /// The stop before the start is not tidiness. Whatever is left of the previous engine is holding
    /// the pipe name and a <c>wsl.exe</c> child that may still be alive, and a relay serving a
    /// socket inside a virtual machine that no longer exists is exactly the state being recovered
    /// from — starting on top of it would leave two of them.
    ///
    /// <para><b>The loop is on the cancellation and not on the count since DD164.</b> It used to end
    /// when <see cref="EngineRevival.WorthAnotherTry"/> went false, which took the host down with
    /// it; the count still decides the <em>wait</em>, and after five failures that wait becomes
    /// <see cref="EngineRevival.PatientWait"/>. So the two endings a user asked for are still the
    /// only endings, and an engine nobody can start costs a <c>wsl</c> call every five minutes
    /// instead of a machine that quietly stopped trying.</para>
    /// </remarks>
    private static bool Revive(
        EngineLifecycle lifecycle,
        EngineRevival revival,
        CancellationToken ending,
        EngineHostLog journal,
        DateTimeOffset quietSince)
    {
        while (!ending.IsCancellationRequested)
        {
            Task.Delay(revival.Wait, ending).GetAwaiter().GetResult();

            // Hurried, because this is a recovery and not a teardown (DD189): whatever is left of
            // the previous engine is in the way of the one being started, and a revival that spent
            // twenty seconds asking it nicely would be an engine kept down to be polite to it.
            lifecycle.StopAsync(EngineLifecycle.HurriedGrace, ending).GetAwaiter().GetResult();
            var back = lifecycle.StartAsync(cancellation: ending).GetAwaiter().GetResult();
            if (back.Usable)
            {
                revival.Revived();

                // Spelled by EngineRevival since DD165, because the window counts these lines and
                // the sentence was previously typed here and matched there. The outage travels with
                // it since DD182: this is the moment the engine answered again, and the poll that
                // first missed it is where the span starts.
                Note(journal, $"  {revival.BroughtItBack(back, DateTimeOffset.UtcNow - quietSince)}");
                return true;
            }

            revival.Failed();
            Note(journal, $"  {back.State,-8}  {back.Detail}");

            // Once, at the crossing. This is the sentence DD136 wanted the user to have, and it is
            // now a statement about what the host is doing rather than the last thing it said.
            if (revival.JustRanOutOfQuickAttempts)
            {
                Note(journal, $"  {revival.WhyItIsSlowingDown(back)}");
            }
        }

        return false;
    }

    /// <summary>
    /// Ask the engine everything the client can ask, through the Engine API rather than through
    /// docker.exe. This is what proves the client against a real daemon.
    /// </summary>
    private static int ApiProbe()
    {
        using var api = new DockerApi();
        if (!api.PingAsync().GetAwaiter().GetResult())
        {
            Console.Error.WriteLine(
                @"  the engine is not answering on \\.\pipe\" + DockerApi.DefaultPipeName);
            return Failed;
        }

        try
        {
            var version = api.VersionAsync().GetAwaiter().GetResult();
            Console.WriteLine(
                $"  engine {version.Version}, API {version.ApiVersion} "
                + $"(oldest {version.MinApiVersion}), {version.Os}/{version.Arch}");
            Console.WriteLine($"  this client asks for {DockerApi.ApiVersion}");
            Console.WriteLine();

            var containers = api.ContainersAsync().GetAwaiter().GetResult();
            if (containers.Count == 0)
            {
                Console.WriteLine("  no containers");
                return Ok;
            }

            foreach (var container in containers)
            {
                var ports = container.PublishedPorts.Count == 0
                    ? ""
                    : "  " + string.Join(", ", container.PublishedPorts);
                Console.WriteLine(
                    $"  {container.ShortId}  {container.State,-8}  {container.ImageName,-22}  "
                    + $"{container.DisplayName}{ports}");
            }

            return Ok;
        }
        catch (DockerApiException exception)
        {
            Console.Error.WriteLine($"  {exception.Message}");
            return Failed;
        }
    }

    /// <summary>
    /// Read /events until Ctrl+C, printing each one. What proves the watcher against a real daemon,
    /// including the part that matters most: the engine going away and coming back.
    /// </summary>
    private static int Watch()
    {
        using var stopping = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stopping.Cancel(); };

        using var api = new DockerApi();
        var events = new EngineEvents(new DockerApiEventSource(api));
        events.StateChanged += state => Console.WriteLine($"  [{state}]");
        events.Received += e => Console.WriteLine(
            $"  {e.Type,-9} {e.Action,-28} {e.ShortId,-12} {e.Name}"
            + (e.ChangesTheContainerList ? "  (refresh)" : ""));
        events.Start();

        try
        {
            Task.Delay(Timeout.InfiniteTimeSpan, stopping.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C is how this ends.
        }

        Console.WriteLine(
            $"  {events.Reconnects} reconnect(s), {events.Unreadable} unreadable line(s)");
        events.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return Ok;
    }

    private static int Stop()
    {
        // Announced before it is done (DD136). Terminating the distribution kills the daemon and
        // whatever `--run` is serving the pipe notices — which was the whole mechanism, and worked
        // from any process where a pid would not. What changed is that the host now puts back an
        // engine it loses, and from in there this teardown is indistinguishable from WSL2 dying
        // under a suspend. So the one thing it cannot infer is said out loud, and said first, or
        // the host starts reviving the engine this is in the middle of taking down.
        _ = SingleEngine.TellTheLiveOneToStop();

        // Patient, because somebody asked for this and nothing is waiting on it: Quit spawns this
        // verb and returns, so the icon is already gone while the containers are still stopping.
        var status = NewLifecycle().StopAsync(EngineLifecycle.PatientGrace)
            .GetAwaiter().GetResult();
        Report(status);
        return Ok;
    }

    /// <summary>Check the distribution's filesystem, and mend it where asked to (DD199).</summary>
    /// <param name="flag"><c>--repair</c> to write, anything else to read.</param>
    /// <returns>The exit code.</returns>
    /// <remarks>
    /// <para>The asymmetry is the design's (DD199). Reading cannot make a filesystem worse, so the
    /// check runs on the bare verb; the repair writes to the disk holding every image and volume the
    /// user has, so it is a flag they have to type. Both take the engine down for the duration,
    /// because a root cannot be checked while it is mounted.</para>
    ///
    /// <para>The check's own output is printed rather than summarised. What <c>e2fsck</c> found is
    /// the thing somebody is deciding on, and a verdict without it is a button that says "trust
    /// me".</para>
    /// </remarks>
    private static int Fsck(string flag)
    {
        var write = string.Equals(flag, "--repair", StringComparison.Ordinal);
        if (flag.Length > 0 && !write)
        {
            return Complain($"unexpected argument {flag}: --fsck takes --repair or nothing");
        }

        // Through the same object the window's button reaches (DD204). What this verb adds is the
        // rendering: the guard, the rootfs, the engine stop and the sequence itself are one
        // assembly, so the order the engine comes down in cannot differ between the two surfaces.
        var work = FilesystemWork.OnThisMachine();
        var outcome = write
            ? work.Fix(step => Console.WriteLine(Line(step)))
            : work.Check(step => Console.WriteLine(Line(step)));

        var paths = new EnginePaths();
        if (outcome.Findings is { Length: > 0 } said)
        {
            Console.WriteLine();
            Console.WriteLine(said);
        }

        Console.WriteLine();
        if (!outcome.Succeeded)
        {
            Console.WriteLine($"  {outcome.Failure?.Detail}");

            // The manual sequence only where there is a disk it would run against. A machine with no
            // distribution registered has nothing for e2fsck to check, and printing four commands
            // about one is worse than printing none.
            if (paths.DistributionRegistered)
            {
                foreach (var line in WslFailure
                    .OfDirtyFilesystem("by hand:", paths.DistributionName, paths.Distribution)
                    .Remedy)
                {
                    Console.WriteLine($"  {line}");
                }
            }

            return Failed;
        }

        Console.WriteLine(outcome switch
        {
            { Clean: true } => "  Nothing to mend. Start the engine when you are ready.",
            { } when write => "  Repaired. Start the engine when you are ready.",
            _ => $"  Run `{CommandLine.ExecutableName} --fsck --repair` to mend this.",
        });

        return Ok;
    }

    /// <summary>One repair step, in the column shape every other verb prints.</summary>
    /// <param name="step">The step.</param>
    /// <returns>The line.</returns>
    private static string Line(RepairStep step) =>
        $"  [{(step.Ok ? "ok  " : "FAIL")}]  {step.What,-22}  {step.Detail}";

    private static int AutostartMode(string mode)
    {
        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("this process has no path");
        var autostart = new Autostart($"\"{exe}\" --run");

        switch (mode)
        {
            case "on":
                autostart.Enable();
                Console.WriteLine($"  autostart on   {autostart.Registered}");
                return Ok;
            case "off":
                autostart.Disable();
                Console.WriteLine("  autostart off  the registry entry is gone");
                return Ok;
            case "status":
                Console.WriteLine(autostart.Enabled
                    ? $"  autostart {(autostart.Current ? "on " : "stale")}  {autostart.Registered}"
                    : "  autostart off  nothing is registered");

                // The tray's entry is a different setting under a different name (DD97), and it is
                // named here because the split would otherwise read as a bug: somebody who ticked
                // "Start FreeWilly with Windows" in the installer asks this verb, is told off, and
                // concludes the box did nothing. This says what is true — the tray starts, the
                // engine does not — and it is the only place both facts are visible at once.
                var tray = new Autostart(exe, Autostart.TrayEntryName);
                if (tray.Registered is { } atLogon)
                {
                    Console.WriteLine($"  tray      on   {atLogon}");
                }

                return Ok;
            default:
                return Complain($"--autostart takes on, off or status, not {mode}");
        }
    }

    /// <summary>What would be downloaded and where things would go. Reaches nothing.</summary>
    private static int Plan()
    {
        var manifest = EngineManifest.Current;
        var paths = new EnginePaths();

        Console.WriteLine("FreeWilly engine: pinned artefacts");
        Console.WriteLine();
        foreach (var artefact in manifest.Artefacts)
        {
            Console.WriteLine($"  {artefact.Id,-7} {artefact.Version,-9} {artefact.FileName}");
            Console.WriteLine($"  {"",-7} {"",-9} {artefact.Url}");
            Console.WriteLine($"  {"",-7} {"",-9} sha256 {artefact.Sha256}");
            Console.WriteLine();
        }

        Console.WriteLine($"  distribution   {paths.DistributionName}");
        Console.WriteLine($"  imported to    {paths.Distribution}");
        Console.WriteLine($"  downloads      {paths.Downloads}");
        Console.WriteLine($"  docker.exe     {paths.DockerCli}");
        Console.WriteLine($"  cli-plugins    {paths.PluginsDirectory}");
        Console.WriteLine();
        Console.WriteLine("  PATH is the installer's to change; this places the binary only.");

        // The plugin is placed under this install's own config directory, so `do compose up` finds
        // it and a plain shell does not (DD73). The variable that closes that gap is the user's
        // own environment, which is theirs to set — so the command is printed and never run.
        Console.WriteLine();
        Console.WriteLine("  `docker compose` in your own shell needs the config directory too:");
        Console.WriteLine(
            $"    setx {Core.Agent.BundledComposeCli.ConfigVariable} \"{paths.ConfigDirectory}\"");
        Console.WriteLine("  Not set for you: it is your environment, and it changes every docker");
        Console.WriteLine("  command in it. `freewilly do compose up` needs none of this.");

        // DD76. A developer whose shell is their own WSL2 distribution finds nothing: the daemon's
        // socket is inside the distribution this tool owns, and a Linux client cannot dial the
        // Windows pipe that carries it out. Docker Desktop answers that by writing a CLI and a
        // socket into each distribution the user ticks, which is the largest version of the thing
        // this project has refused to do since DD32 — and it hands the Engine API to every
        // distribution, which is what the pipe's single-account ACL exists to prevent.
        //
        // So the integration is not built and the fact is stated instead, here and in the README,
        // with the one thing that does work. Measured: WSL interop is on by default, and the
        // Windows docker.exe invoked from a Linux shell reaches the engine.
        Console.WriteLine();
        Console.WriteLine("  From a WSL shell of your own, the Linux `docker` reaches nothing:");
        Console.WriteLine("  the engine is on the Windows side of a named pipe. Run the Windows");
        Console.WriteLine("  binary instead, which WSL's interop makes work:");
        Console.WriteLine($"    {Wsl.ToDistributionPath(paths.DockerCli)} ps");
        Console.WriteLine("  It is a Windows process, so every path you hand it is read as a");
        Console.WriteLine("  Windows path: `-v $(pwd):/data` in a Linux shell mounts nothing.");
        return Ok;
    }

    private static int Provision(bool acquireOnly)
    {
        // The preflight is the same code the installer runs, and running it here is the point: an
        // engine unpacked onto a machine that cannot host one fails halfway.
        var report = PreflightInspection.Run(new WindowsMachineFacts());
        if (!acquireOnly && !report.CanHostEngine)
        {
            Console.Error.WriteLine("preflight blocks this install:");
            foreach (var row in report.Blockers)
            {
                Console.Error.WriteLine($"  {row.Title}: {row.Detail}");
                Console.Error.WriteLine($"    -> {row.Remedy}");
            }

            return Failed;
        }

        var paths = new EnginePaths();
        using var fetcher = new HttpArtefactFetcher();
        var provisioner = new EngineProvisioner(
            EngineManifest.Current,
            new ArtefactStore(fetcher, paths.Downloads),
            new Wsl(),
            paths);

        // Printed as each step lands rather than gathered and printed at the end (DD119). The lines
        // are identical either way; what changes is that the installer's page, which reads this
        // stream a line at a time, has something on it while a quarter of a gigabyte comes down.
        // Console.Out flushes on every write even when redirected, so no line waits on a buffer.
        var outcome = acquireOnly
            ? provisioner.AcquireAsync(Say).GetAwaiter().GetResult()
            : provisioner.ProvisionAsync(Say).GetAwaiter().GetResult();

        Console.WriteLine();
        if (outcome.Succeeded)
        {
            Console.WriteLine(acquireOnly
                ? "Every artefact is on disk and verified."
                : $"The engine is installed in {paths.DistributionName}.");
            return Ok;
        }

        Console.Error.WriteLine($"Stopped at {outcome.Failure!.Step}: {outcome.Failure.Detail}");
        return Failed;
    }

    /// <summary>Print one provisioning step, the moment it lands.</summary>
    private static void Say(StepResult step) => Console.WriteLine(StepLine(step));

    /// <summary>
    /// How one provisioning step is written, and the whole of what the installer parses (DD119).
    /// </summary>
    /// <param name="step">The step that just landed.</param>
    /// <returns>The line.</returns>
    /// <remarks>
    /// A method rather than an interpolation inside the loop, because installer.iss counts these
    /// lines to move its progress bar and matches the two verdicts at position 1 of the trimmed
    /// line. That makes the leading marker a contract between a C# format string and a Pascal
    /// <c>Pos</c> call, which nothing but a test would notice drifting — so there is one, and it
    /// asserts against this.
    /// </remarks>
    internal static string StepLine(StepResult step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return $"  [{(step.Ok ? "ok  " : "FAIL")}]  {step.Step,-19} {step.Detail}";
    }

    private static int Complain(string problem)
    {
        Console.Error.WriteLine($"{CommandLine.ExecutableName}: {problem}");
        return Help(Usage);
    }

    /// <summary>
    /// The one help text, and deliberately not a second one.
    /// </summary>
    /// <remarks>
    /// This used to print its own list of engine modes, which was correct while the engine was its
    /// own executable and became a duplicate the moment it stopped being one. A verb documented in
    /// one of two lists is a verb somebody cannot find.
    /// </remarks>
    private static int Help(int code)
    {
        (code == Ok ? Console.Out : Console.Error).Write(CommandLine.HelpText);
        return code;
    }
}
