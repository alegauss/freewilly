using FreeWilly.Core.Api;
using FreeWilly.Core.Engine;
using FreeWilly.Tray;
using FreeWilly.Tray.Cli;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// The machine a session-ending teardown reaches, on a clock that costs nothing (DD188).
/// </summary>
/// <remarks>
/// The budget is four seconds of real time and this walks it in a few microseconds, which is the
/// only way the deadline itself is worth asserting: a test that actually waited would be measuring
/// the build machine.
/// </remarks>
internal sealed class FakeTeardown(bool heard, int answersFor) : IEngineTeardown
{
    private DateTimeOffset _clock = new(2026, 8, 29, 3, 0, 0, TimeSpan.Zero);
    private int _asked;

    /// <summary>How many times the distribution was taken down.</summary>
    internal int Terminates { get; private set; }

    /// <summary>How much of the budget the wait spent.</summary>
    internal TimeSpan Elapsed => _clock - new DateTimeOffset(2026, 8, 29, 3, 0, 0, TimeSpan.Zero);

    /// <summary>The clock, which only moves when the teardown waits.</summary>
    /// <returns>Now.</returns>
    internal DateTimeOffset Now() => _clock;

    /// <summary>Spend <paramref name="span"/> without spending it.</summary>
    /// <param name="span">How long the teardown thinks it waited.</param>
    internal void Pause(TimeSpan span) => _clock += span;

    /// <inheritdoc/>
    public bool TellTheLiveHost() => heard;

    /// <inheritdoc/>
    public bool EngineAnswers() => _asked++ < answersFor;

    /// <inheritdoc/>
    public string Terminate()
    {
        Terminates++;
        return "terminated freewilly";
    }
}

/// <summary>Records what it was asked to launch instead of launching it.</summary>
internal sealed class FakeLauncher(string? failure = null) : IProcessLauncher
{
    internal List<(string File, string Arguments)> Launched { get; } = [];

    public string? Launch(string fileName, string arguments)
    {
        Launched.Add((fileName, arguments));
        return failure;
    }
}

/// <summary>
/// The icon and the lifetime. The icon tests assert on ink rather than colour, because a state a
/// reader can only get from hue is a state some readers cannot get at all.
/// </summary>
public sealed class TrayTests
{
    // ---- the icon ---------------------------------------------------------------------------

    [Fact]
    public void The_three_states_are_told_apart_with_no_colour_at_all()
    {
        // Since DD85 the icon is the product mark with a state badge in its corner, so the
        // discriminating shape is that badge and not the whole bitmap. Everything asserted here is
        // what was asserted before, scoped to it — including both near-misses recorded below, which
        // are the reason this test is shaped the way it is rather than counting pixels.
        using var running = StateIcon.Draw(EngineState.Running, 32);
        using var starting = StateIcon.Draw(EngineState.Starting, 32);
        using var stopped = StateIcon.Draw(EngineState.Stopped, 32);

        var filled = InkedIn(running, StateIcon.BadgeAt(32));
        var gapped = InkedIn(starting, StateIcon.BadgeAt(32));
        var ring = InkedIn(stopped, StateIcon.BadgeAt(32));

        // The centre is the discriminator that is about shape rather than about how much ink there
        // happens to be: a disc is painted through the middle and a ring is hollow, whatever the
        // stroke width. Measured at 32px the badge areas are 150 and 116, which a threshold could
        // also catch — but only until somebody changes the pen.
        var (cx, cy) = Middle(StateIcon.BadgeAt(32));
        Assert.True(Inked(running, cx, cy), "a filled disc should be painted at its centre");
        Assert.False(Inked(stopped, cx, cy), "a ring should be hollow at its centre");
        Assert.False(Inked(starting, cx, cy), "a gapped ring should be hollow at its centre");

        // And the two hollow ones differ by the gap — measured as the pixels the whole ring has and
        // the gapped one does not. `gapped < ring` alone is not this assertion: closing the arc to
        // 360 degrees left it one pixel smaller and the suite stayed green, which is a test that
        // would have let Starting and Stopped become the same picture.
        var gap = MissingFrom(stopped, starting);
        Assert.True(gap > ring / 5, $"the gap ({gap}) should be a visible slice of the ring ({ring})");
        Assert.True(gapped > ring / 2, $"a gapped ring ({gapped}) should still read as a ring ({ring})");
        Assert.True(filled > ring, $"a disc ({filled}) should carry more ink than a ring ({ring})");
    }

    [Fact]
    public void The_mark_is_the_same_drawing_in_every_state_and_only_the_badge_moves()
    {
        // The claim DD85 exists for, stated the strongest way it can be: outside the badge the three
        // states are bit-identical, so what a user recognises at a glance is the product and not the
        // engine's mood. Measured at 0 differing pixels — a mark that shifted, rescaled or retinted
        // per state would fail here even where each state still looked fine on its own.
        using var running = StateIcon.Draw(EngineState.Running, 32);
        using var starting = StateIcon.Draw(EngineState.Starting, 32);
        using var stopped = StateIcon.Draw(EngineState.Stopped, 32);

        // The badge's punched halo is excluded generously: what is being asserted is the mark, and
        // the boundary between the two is anti-aliased.
        var badge = StateIcon.BadgeAt(32);
        var mark = 0;
        for (var x = 0; x < 32; x++)
        {
            for (var y = 0; y < 32; y++)
            {
                if (x >= badge.X - 4 && y >= badge.Y - 4)
                {
                    continue;
                }

                Assert.Equal(running.GetPixel(x, y), starting.GetPixel(x, y));
                Assert.Equal(running.GetPixel(x, y), stopped.GetPixel(x, y));
                if (Inked(running, x, y))
                {
                    mark++;
                }
            }
        }

        // And it is a drawing rather than a few stray pixels: the badge is 0.44 of the edge, so most
        // of the icon has to be mark or the identity is not what a reader sees.
        Assert.True(mark > 400, $"only {mark} pixels of the 32px icon are the mark");
    }

    [Fact]
    public void The_badge_is_the_smaller_half_of_the_icon()
    {
        // A badge large enough to dominate would answer the state at the cost of the identity, which
        // is the defect DD85 fixed arriving from the other side. Measured against the alternative at
        // 0.52, which reads as a badge with a mark behind it.
        Assert.True(StateIcon.BadgeFraction < 0.5, "the badge should not be half the icon");

        var badge = StateIcon.BadgeAt(24);
        Assert.True(badge.Right <= 24, "the badge should be inside the icon");
        Assert.True(badge.Bottom <= 24, "the badge should be inside the icon");
    }

    [Theory]
    [InlineData(EngineState.Running)]
    [InlineData(EngineState.Starting)]
    [InlineData(EngineState.Stopped)]
    public void Every_state_draws_something_at_the_size_a_taskbar_uses(EngineState state)
    {
        using var drawn = StateIcon.Draw(state);

        Assert.Equal(16, drawn.Width);
        Assert.True(StateIcon.InkedPixels(drawn) > 10, "an icon nobody can see is not an icon");
    }

    // ---- the size the shell asked for (DD99) --------------------------------------------------

    [Fact]
    public void The_icon_is_drawn_at_the_size_windows_asked_for_and_not_at_sixteen()
    {
        // The defect: `Icon` took `size = 16` and its only caller passed nothing, so a manifest
        // opting into PerMonitorV2 bought the right to draw sharp and nothing spent it. Asserted as
        // an identity rather than a number, because the number is the machine's — and this test
        // host is DPI-unaware whatever the tray is, so a literal here would assert the wrong
        // process. Measured on the development machine at 200%: the tray is told 32, this is told 16.
        using var icon = StateIcon.Icon(EngineState.Running);

        Assert.Equal(StateIcon.NotificationAreaSize(), icon.Width);
        Assert.Equal(icon.Width, icon.Height);
    }

    [Fact]
    public void A_size_that_is_named_is_still_honoured()
    {
        // Asking Windows is the default and not the law: the About page and any capture want a size
        // of their own, and a default that could not be overridden would be a constant again.
        using var icon = StateIcon.Icon(EngineState.Stopped, 48);

        Assert.Equal(48, icon.Width);
    }

    [Theory]
    [InlineData(0, 16)]      // what a failed metric returns, and Draw refuses anything under 8
    [InlineData(16, 16)]
    [InlineData(24, 24)]
    [InlineData(32, 32)]
    [InlineData(9999, 256)]  // past the largest frame the mark carries there is nothing to scale from
    public void What_windows_answers_is_kept_inside_what_can_be_drawn(int metric, int expected) =>
        // The floor is not tidiness: without it a machine that answered 0 would take the tray down
        // rather than look wrong, because `Draw` refuses a size under 8.
        Assert.Equal(expected, StateIcon.Bounded(metric));

    // ---- and the size the shell asks for next (DD99) ------------------------------------------

    [Fact]
    public void A_display_that_starts_asking_for_a_different_size_gets_a_redrawn_icon()
    {
        // The half the partial left open. Under PerMonitorV2 Windows resamples nothing for the app
        // and NotifyIcon wears whatever it was last handed, so a laptop docked to a 4K panel keeps
        // the icon drawn for the laptop's own scale until the process restarts.
        var asked = 16;
        var redraws = 0;
        using var scale = new TrayScale(() => redraws++, () => asked);

        _ = scale.Drawing();
        asked = 32;

        Assert.True(scale.RedrawIfMoved());
        Assert.Equal(1, redraws);
    }

    [Fact]
    public void A_display_event_that_moves_no_size_redraws_nothing()
    {
        // A display event fires for a resolution change, a monitor arriving and a monitor leaving as
        // well as for a scale change, and only the last of those changes what the tray should draw.
        // Redrawing on all of them would destroy and rebuild an unmanaged handle to no end.
        var redraws = 0;
        using var scale = new TrayScale(() => redraws++, () => 24);

        _ = scale.Drawing();

        Assert.False(scale.RedrawIfMoved());
        Assert.Equal(0, redraws);
    }

    [Fact]
    public void The_recorded_size_follows_the_drawing_so_a_second_change_is_noticed_too()
    {
        // Docking and undocking is the ordinary case, not a one-way trip: after the redraw the watch
        // has to be comparing against the size that redraw used, or the return to the laptop's own
        // display looks like no change at all.
        var asked = 16;
        using var scale = new TrayScale(() => { }, () => asked);

        _ = scale.Drawing();
        asked = 32;
        Assert.True(scale.RedrawIfMoved());

        // What the redraw itself does, in the tray: Show draws and records in one step.
        Assert.Equal(32, scale.Drawing());

        asked = 16;
        Assert.True(scale.RedrawIfMoved());
        Assert.Equal(32, scale.DrawnAt);
        Assert.Equal(16, scale.Drawing());
    }

    [Fact]
    public void A_size_a_caller_named_is_not_recorded_as_what_the_tray_is_wearing()
    {
        // StateIcon.Icon still honours an explicit size, and the About page and the capture verb both
        // pass one. Those are not the tray's icon, so they must not move what the watch compares
        // against — otherwise a capture at 48 makes the next display event redraw for no reason.
        var redraws = 0;
        using var scale = new TrayScale(() => redraws++, () => 24);

        _ = scale.Drawing();
        using var capture = StateIcon.Icon(EngineState.Stopped, 48);

        Assert.Equal(48, capture.Width);
        Assert.Equal(24, scale.DrawnAt);
        Assert.False(scale.RedrawIfMoved());
        Assert.Equal(0, redraws);
    }

    [Fact]
    public void The_three_states_are_also_three_colours()
    {
        var colours = new[] { EngineState.Running, EngineState.Starting, EngineState.Stopped }
            .Select(StateIcon.ColourFor)
            .ToList();

        Assert.Equal(3, colours.Distinct().Count());
    }

    [Fact]
    public void The_tooltip_names_the_state_in_words()
    {
        // The section's own reason for a tooltip: sixteen pixels is not always enough.
        Assert.Contains("running", StateIcon.TooltipFor(EngineState.Running), StringComparison.Ordinal);
        Assert.Contains("starting", StateIcon.TooltipFor(EngineState.Starting), StringComparison.Ordinal);
        Assert.Contains("stopped", StateIcon.TooltipFor(EngineState.Stopped), StringComparison.Ordinal);
    }

    [Fact]
    public void An_icon_smaller_than_a_state_can_be_drawn_in_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => StateIcon.Draw(EngineState.Running, 4));

    private static bool Inked(System.Drawing.Bitmap bitmap, int x, int y) =>
        bitmap.GetPixel(x, y).A > 128;

    /// <summary>The whole pixel nearest the middle of <paramref name="box"/>.</summary>
    private static (int X, int Y) Middle(System.Drawing.RectangleF box) =>
        ((int)(box.X + (box.Width / 2)), (int)(box.Y + (box.Height / 2)));

    /// <summary>Painted pixels inside <paramref name="box"/>, which is where the state is said.</summary>
    private static int InkedIn(System.Drawing.Bitmap bitmap, System.Drawing.RectangleF box)
    {
        var inked = 0;
        for (var x = (int)box.X; x < Math.Min(bitmap.Width, box.Right + 1); x++)
        {
            for (var y = (int)box.Y; y < Math.Min(bitmap.Height, box.Bottom + 1); y++)
            {
                if (Inked(bitmap, x, y))
                {
                    inked++;
                }
            }
        }

        return inked;
    }

    /// <summary>Pixels painted in <paramref name="whole"/> and not in <paramref name="cut"/>.</summary>
    private static int MissingFrom(System.Drawing.Bitmap whole, System.Drawing.Bitmap cut)
    {
        var missing = 0;
        for (var x = 0; x < whole.Width; x++)
        {
            for (var y = 0; y < whole.Height; y++)
            {
                if (Inked(whole, x, y) && !Inked(cut, x, y))
                {
                    missing++;
                }
            }
        }

        return missing;
    }

    // ---- what the indicator says --------------------------------------------------------------

    [Fact]
    public void The_engine_is_Running_exactly_when_the_event_stream_is_connected() =>
        Assert.Equal(EngineState.Running, TrayState.For(EventStreamState.Watching, false));

    [Theory]
    [InlineData(EventStreamState.Connecting)]
    [InlineData(EventStreamState.Reconnecting)]
    [InlineData(EventStreamState.Stopped)]
    public void A_stream_that_is_not_connected_reads_as_Stopped_when_nobody_asked_for_a_start(
        EventStreamState stream) =>
        Assert.Equal(EngineState.Stopped, TrayState.For(stream, startRequested: false));

    [Theory]
    [InlineData(EventStreamState.Connecting)]
    [InlineData(EventStreamState.Reconnecting)]
    public void The_same_stream_reads_as_Starting_once_somebody_asked(EventStreamState stream) =>
        Assert.Equal(EngineState.Starting, TrayState.For(stream, startRequested: true));

    [Fact]
    public void A_start_that_landed_is_Running_and_not_still_Starting() =>
        Assert.Equal(EngineState.Running, TrayState.For(EventStreamState.Watching, startRequested: true));

    // ---- a start that cannot land (DD120) -------------------------------------------------------

    [Fact]
    public void A_machine_with_no_distribution_is_told_so_rather_than_shown_Starting()
    {
        // The failure itself. `--run` refuses this case already and prints why, onto a hidden
        // console — so the tray showed Starting and kept showing it. The remedy is in the sentence
        // because "not installed" on its own is the same dead end one step later.
        var refusal = TrayState.WhyAStartWouldNotLand(
            engineInstalled: false, EnginePaths.CurrentDistribution);

        Assert.NotNull(refusal);
        Assert.Contains("freewilly", refusal, StringComparison.Ordinal);
        Assert.Contains("--provision", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_machine_that_has_the_distribution_is_not_stopped_from_trying() =>
        Assert.Null(TrayState.WhyAStartWouldNotLand(
            engineInstalled: true, EnginePaths.CurrentDistribution));

    [Fact]
    public void The_trays_budget_outlasts_the_one_the_engine_gives_itself()
    {
        // The ordering is the claim. StartAsync gives the daemon 60 seconds inside `--run`; a tray
        // that gave up first would report a failure over a start that was still going, which is the
        // same lie as Starting forever, told the other way round.
        Assert.True(
            TrayState.StartBudget > TimeSpan.FromSeconds(60),
            $"the tray gives up after {TrayState.StartBudget.TotalSeconds:0}s, before the engine does");
    }

    [Fact]
    public void A_start_that_never_answers_is_reported_against_the_log_that_holds_the_reason()
    {
        // Every silent death that is not a missing distribution ends here, and there is one useful
        // thing to say about all of them rather than a guess at which one it was.
        var said = TrayState.StartDidNotLand(
            TrayState.StartBudget, EnginePaths.CurrentDistribution);

        Assert.Contains(EngineLifecycle.LogPath, said, StringComparison.Ordinal);
        Assert.Contains(EnginePaths.CurrentDistribution, said, StringComparison.Ordinal);
        Assert.Contains("75s", said, StringComparison.Ordinal);
    }

    [Fact]
    public void A_distribution_no_machine_owns_never_reads_as_installed() =>
        // The branch that matters, and the only one a test machine can assert: the tray must not
        // launch a start on the strength of a name WSL has never heard of.
        Assert.False(
            new EnginePaths(@"C:\nowhere", $"freewilly-{Guid.NewGuid():N}").DistributionRegistered);

    // ---- the lifetime -------------------------------------------------------------------------

    [Fact]
    public void Starting_launches_the_engine_in_a_process_of_its_own()
    {
        var launcher = new FakeLauncher();
        var holder = new EngineHolder(@"C:\x\dockerdesk-engine.exe", launcher);

        holder.Start();

        Assert.Equal((@"C:\x\dockerdesk-engine.exe", "--run"), launcher.Launched[0]);
    }

    [Fact]
    public void Stopping_goes_through_the_engine_rather_than_killing_a_process()
    {
        // --stop terminates the distribution, so it reaches an engine this tray never started —
        // one left running from a terminal, or by a previous tray.
        var launcher = new FakeLauncher();
        var holder = new EngineHolder(@"C:\x\dockerdesk-engine.exe", launcher);

        holder.Stop();

        Assert.Equal((@"C:\x\dockerdesk-engine.exe", "--stop"), launcher.Launched[0]);
    }

    [Fact]
    public void An_engine_that_cannot_be_started_is_reported_rather_than_thrown()
    {
        // This crashed the tray from a click handler: the engine was simply not beside it in a dev
        // build, Process.Start threw, and the icon vanished. An icon that disappears when somebody
        // presses its own menu item is worse than any message it could have shown instead.
        var holder = new EngineHolder(@"C:\x\dockerdesk-engine.exe", new FakeLauncher("not there"));

        Assert.Equal("not there", holder.Start());
        Assert.Equal("not there", holder.Stop());
    }

    [Fact]
    public void A_start_that_worked_reports_nothing() =>
        Assert.Null(new EngineHolder(@"C:\x\dockerdesk-engine.exe", new FakeLauncher()).Start());

    [Fact]
    public void The_real_launcher_names_the_missing_file_instead_of_throwing()
    {
        var missing = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"dockerdesk-absent-{Guid.NewGuid():N}.exe");

        var failure = new DetachedLauncher().Launch(missing, "--run");

        Assert.NotNull(failure);
        Assert.Contains("is not in", failure, StringComparison.Ordinal);
        Assert.Contains(System.IO.Path.GetTempPath().TrimEnd('\\'), failure, StringComparison.Ordinal);
    }

    [Fact]
    public void The_engine_is_this_executable_and_not_a_file_beside_it()
    {
        // DD14: one .exe. The engine used to be a second file expected in the same folder, and a
        // copy that arrived without it had a Start engine menu item that could only apologise. What
        // this asserts is that the holder drives something that exists — itself.
        var path = EngineHolder.ThisProcess();

        Assert.True(System.IO.Path.IsPathRooted(path));
        Assert.True(System.IO.File.Exists(path), $"{path} should be the running executable");
    }

    [Fact]
    public void Both_verbs_go_to_the_same_executable()
    {
        var launcher = new FakeLauncher();
        var holder = new EngineHolder(EngineHolder.ThisProcess(), launcher);

        holder.Start();
        holder.Stop();

        Assert.Equal(holder.EnginePath, launcher.Launched[0].File);
        Assert.Equal(holder.EnginePath, launcher.Launched[1].File);
    }

    // ---- what quitting takes with it (DD128) --------------------------------------------------

    [Fact]
    public void Quitting_the_tray_stops_the_engine_before_the_icon_goes()
    {
        // Asserted on the source for the reason DD82's is: Quit hides a live NotifyIcon and ends a
        // message loop, and nothing in a test can construct the tray that owns them.
        //
        // The order is half the claim. Stop launches a detached process, so running it after
        // ExitThread would be a request made by something on its way out of existence — and the
        // reason it can go first at all is that the launch does not wait for the engine to be gone.
        var source = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Program.cs"));
        var quit = source.IndexOf("private void Quit()", StringComparison.Ordinal);
        Assert.True(quit >= 0, "the tray no longer has a quit path");

        var body = source[quit..];
        var stopped = body.IndexOf("StopTheEngine();", StringComparison.Ordinal);
        var hidden = body.IndexOf("_icon.Visible = false;", StringComparison.Ordinal);

        Assert.True(
            stopped >= 0,
            "quitting the tray no longer stops the engine, so the WSL2 virtual machine keeps the "
            + "gigabytes this project exists to give back (DD128)");
        Assert.True(hidden >= 0, "the tray no longer hides its icon on the way out");
        Assert.True(stopped < hidden, "the engine is asked to stop after the tray has gone");

        // And that the stop it reaches is the engine's own verb, rather than a second spelling of
        // it: --stop terminates the distribution, which is what reaches an engine this tray never
        // started and what gives the virtual machine back.
        var shared = source.IndexOf("private void StopTheEngine()", StringComparison.Ordinal);
        Assert.True(shared >= 0, "the shared stop is gone");
        Assert.Contains("_holder.Stop()", source[shared..], StringComparison.Ordinal);
    }

    [Fact]
    public void Quitting_does_not_reach_past_the_distribution_this_install_owns()
    {
        // `wsl --shutdown` would give the memory back a minute sooner and take somebody's Ubuntu
        // shell with it. Terminating our own distribution is the whole of what this install is
        // entitled to do, and WSL powers the machine down itself once nothing else is using it.
        //
        // The quoted spelling and not the bare word, because the reasoning above is written down in
        // Quit's own remarks — a test that searched for the prose would fail on the explanation of
        // why the thing it forbids is forbidden.
        var offenders = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains("\"--shutdown\"", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "something passes --shutdown to wsl, which takes every distribution on the machine down "
            + $"and not just the one this install owns: {string.Join(", ", offenders)}");
    }

    // ---- the exits nobody thinks of as quitting (DD129) ---------------------------------------

    [Fact]
    public void A_logoff_or_a_shutdown_stops_the_engine_too()
    {
        // Asserted on the source for the reason the rest of these are: nothing in a test can raise a
        // real SessionEnding at a live tray, and the consequence of getting it wrong is only visible
        // on a machine somebody has signed out of.
        var source = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Program.cs"));

        Assert.Contains("SessionEnding += OnSessionEnding", source, StringComparison.Ordinal);
        Assert.Contains(
            "SessionEnding -= OnSessionEnding",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_host_is_told_the_session_is_ending_as_well_as_the_tray()
    {
        // DD187. The tray has had this hook since DD129 and the host, which holds the wsl.exe
        // handle the daemon runs under, never asked for it — so seven session endings in the
        // journal are followed by neither a Stopped line nor a host-is-done line, while every Quit
        // writes both in the same second. Source-asserted for the reason the tray's is: nothing in
        // a test can raise a real SessionEnding, and the cost of getting it wrong is a distribution
        // reaped with its ext4 never unmounted.
        var source = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Cli/EngineCommand.cs"));

        Assert.Contains("SessionEnding += OnSessionEnding", source, StringComparison.Ordinal);
        Assert.Contains("SessionEnding -= OnSessionEnding", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_ending_host_is_released_only_once_its_journal_is_complete()
    {
        // The waiting is the whole fix (DD187): returning from the handler tells Windows this
        // process is ready to be killed, so a host that returns before `wsl --terminate` has run
        // has answered the question wrong. Asserted as an order because the failure is an ordering
        // one — a release moved above the last line is a teardown Windows may cut in half.
        var source = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Cli/EngineCommand.cs"));

        var waits = source.IndexOf(
            "torndown.Task.Wait(SessionEndingBudget)", StringComparison.Ordinal);
        var done = source.IndexOf("this host is done", StringComparison.Ordinal);
        var releases = source.IndexOf("torndown.TrySetResult()", StringComparison.Ordinal);

        Assert.True(waits >= 0, "the session-ending handler no longer waits for the teardown");
        Assert.True(done >= 0, "the host no longer writes its last line");
        Assert.True(
            releases > done,
            "the session-ending handler is released before the journal's last line, so Windows may "
            + "kill this host between the teardown and the account of it");
    }

    [Fact]
    public void The_wait_for_a_teardown_gives_way_before_Windows_calls_the_host_hung()
    {
        // Five seconds is where Windows stops waiting on a WM_QUERYENDSESSION and offers the user a
        // screen naming the app that is holding the shutdown up. Being that app is worse than a
        // distribution taken down hard, so the budget gives way first and the journal says so.
        Assert.InRange(
            EngineCommand.SessionEndingBudget,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(4.5));
    }

    [Fact]
    public void A_session_ending_no_longer_answers_by_spawning_a_process()
    {
        // DD188. The spawn is the defect and not an implementation detail of it: `ShellExecuteEx`
        // routes through a shell being torn down at the same moment, so the one thing this must not
        // go back to is launching the executable and returning. Asserted on the handler's own body,
        // because Quit still spawns and is right to — it is not running during a shutdown.
        var source = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Program.cs"));
        var handler = source.IndexOf(
            "private void OnSessionEnding(", StringComparison.Ordinal);
        var body = source[handler..source.IndexOf("private void StopTheEngine()", StringComparison.Ordinal)];

        Assert.True(handler >= 0, "the tray no longer hears the session ending");
        Assert.DoesNotContain("StopTheEngine()", body, StringComparison.Ordinal);
        Assert.Contains("SessionTeardown.Run(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_session_ending_with_no_host_to_hear_it_takes_the_distribution_down_itself()
    {
        // The case DD187 cannot cover, because the process it gave the SessionEnding subscription
        // to is not running. Something still has to unmount the distribution's ext4.
        var machine = new FakeTeardown(heard: false, answersFor: 0);

        var said = SessionTeardown.Run(machine, machine.Now, machine.Pause);

        Assert.Equal(1, machine.Terminates);
        Assert.Contains("no engine host to tell", said, StringComparison.Ordinal);
    }

    [Fact]
    public void A_host_that_takes_the_engine_down_is_not_raced_to_the_terminate()
    {
        // Two processes running `wsl --terminate` on the same distribution is how a teardown turns
        // back into the unclean unmount it exists to prevent, so the host doing its job is the one
        // outcome where this does nothing at all.
        var machine = new FakeTeardown(heard: true, answersFor: 2);

        var said = SessionTeardown.Run(machine, machine.Now, machine.Pause);

        Assert.Equal(0, machine.Terminates);
        Assert.Contains("the engine host took it down", said, StringComparison.Ordinal);
    }

    [Fact]
    public void A_host_that_runs_out_of_budget_is_finished_off_rather_than_left_to_Windows()
    {
        // Windows is not waiting longer either way. The choice at the deadline is one more
        // terminate or a virtual machine reaped with its root never unmounted, and the second is
        // the ext4 that had to be repaired by hand.
        var machine = new FakeTeardown(heard: true, answersFor: int.MaxValue);

        var said = SessionTeardown.Run(machine, machine.Now, machine.Pause);

        Assert.Equal(1, machine.Terminates);
        Assert.Contains("ran out of time", said, StringComparison.Ordinal);

        // Bounded by the budget rather than by the fake running out of answers, which is the whole
        // reason the loop is on a clock.
        Assert.InRange(machine.Elapsed, EngineCommand.SessionEndingBudget, TimeSpan.FromSeconds(6));
    }

    [Fact]
    public void The_stop_is_asked_for_once_however_many_ways_the_session_ends()
    {
        // A logoff raises SessionEnding and the tray may still process its own quit behind it. Both
        // reach the same stop, and without the guard the second is a process spawned during a
        // shutdown against a distribution already terminated.
        var source = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Program.cs"));
        var method = source.IndexOf("private void StopTheEngine()", StringComparison.Ordinal);

        Assert.True(method >= 0, "the two ways out no longer share one stop");
        Assert.Contains("_engineToldToStop", source[method..], StringComparison.Ordinal);
    }

    // ---- what the shell is told at add time (DD82) --------------------------------------------

    [Fact]
    public void The_icon_and_its_tooltip_are_set_before_the_entry_becomes_visible()
    {
        // Asserted on the source because that is where the fact lives: setting Visible is what emits
        // the shell's notify-add, and Windows persists whatever that one call carried. Nothing on
        // NotifyIcon reports what was sent, and the consequence — an overflow flyout entry with no
        // name — is only visible in the registry of a machine that has run it.
        var source = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Program.cs"));

        var shown = source.IndexOf("Show(EngineState.Stopped);", StringComparison.Ordinal);
        var visible = source.IndexOf("_icon.Visible = true;", StringComparison.Ordinal);

        // Both have to be there, or this passes by finding neither.
        Assert.True(shown >= 0, "the tray no longer draws an initial state");
        Assert.True(visible >= 0, "the tray no longer makes its icon visible");
        Assert.True(
            shown < visible,
            "the icon becomes visible before it has an image and a tooltip, so the shell persists "
            + "an empty one and the overflow flyout names nothing (DD82)");
    }

    // ---- what the icon does when it is clicked (DD140) ----------------------------------------

    [Fact]
    public void The_icon_opens_the_window_when_the_primary_button_is_clicked()
    {
        // Asserted on the source for the reason DD82's is: the handler is a lambda inside
        // TrayApplication's constructor, and nothing can construct one in a test — it wants a live
        // NotifyIcon, an event stream and a message loop. What can be pinned is that the
        // subscription exists at all, which is the whole of the defect: the icon carried a context
        // menu and no click handler, so the primary button did nothing on the one surface this
        // product keeps on screen.
        var source = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Program.cs"));

        Assert.Contains("_icon.MouseClick +=", source, StringComparison.Ordinal);
        Assert.Contains("MouseButtons.Left", source, StringComparison.Ordinal);

        // And it is MouseClick rather than Click. Click fires for the secondary button too, and that
        // gesture has already raised the context menu — so the window would open behind the popup
        // that asked for it, which is a worse answer than the silence it replaced.
        Assert.DoesNotContain("_icon.Click +=", source, StringComparison.Ordinal);
    }

    /// <summary>The repository root, found by walking up from the test binary.</summary>
    private static string RepositoryRoot()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && !File.Exists(Path.Combine(here.FullName, "FreeWilly.slnx")))
        {
            here = here.Parent;
        }

        Assert.True(here is not null, "the repository root was not found above the test binaries");
        return here!.FullName;
    }
}
