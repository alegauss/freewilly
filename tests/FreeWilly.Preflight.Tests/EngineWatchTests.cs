using FreeWilly.Core.Engine;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// A run of quiet polls and not one of them is what stops the engine host (DD133).
/// </summary>
public sealed class EngineWatchTests
{
    private static EngineStatus Running() =>
        new(EngineState.Running, @"the engine answered on \\.\pipe\docker_engine", "1.44");

    private static EngineStatus Quiet() =>
        new(EngineState.Starting, "the daemon is running and no answer within 3s");

    [Fact]
    public void One_missed_poll_does_not_take_the_engine_down()
    {
        // The whole of DD133. Before this, the single Starting below was enough to dispose the
        // relay and terminate the distribution — mid-build, against a daemon that was fine and had
        // only lost a race for process creation with the build's own wsl.exe children.
        var watch = new EngineWatch();

        Assert.True(watch.KeepServing(Quiet()));
        Assert.Equal(1, watch.QuietPolls);
    }

    [Fact]
    public void An_answer_clears_the_run_of_silence()
    {
        // The engine proved itself by replying, so what came before it is not evidence of anything.
        // Without this a host that was merely busy would accumulate quiet polls across a whole day
        // and come down on the sixth, hours apart, for no reason a reader could reconstruct.
        var watch = new EngineWatch();

        for (var i = 0; i < EngineWatch.ToleratedQuietPolls - 1; i++)
        {
            Assert.True(watch.KeepServing(Quiet()));
        }

        Assert.True(watch.KeepServing(Running()));
        Assert.Equal(0, watch.QuietPolls);

        Assert.True(watch.KeepServing(Quiet()));
    }

    [Fact]
    public void Unbroken_silence_still_ends_the_watch()
    {
        // The tolerance is not a refusal to ever come down. A `--stop` from another process, or a
        // distribution terminated by hand, has to bring the pipe down with it — a relay left serving
        // nothing is the defect the poll loop was added for in the first place.
        var watch = new EngineWatch();

        for (var i = 0; i < EngineWatch.ToleratedQuietPolls - 1; i++)
        {
            Assert.True(watch.KeepServing(Quiet()));
        }

        Assert.False(watch.KeepServing(Quiet()));
        Assert.Equal(EngineWatch.ToleratedQuietPolls, watch.QuietPolls);
    }

    [Fact]
    public void An_inconclusive_stopped_answer_is_tolerated_exactly_like_a_quiet_one()
    {
        // Stopped reads like the certain one — the daemon is gone — but a status that did not mark
        // itself conclusive reached Stopped through something load can forge, and on a saturated
        // machine `wsl --list` is as capable of being slow as the ping was. Absence of evidence.
        var watch = new EngineWatch();
        var stopped = new EngineStatus(EngineState.Stopped, "the daemon is not running");

        Assert.True(watch.KeepServing(stopped));
        Assert.Equal(1, watch.QuietPolls);
    }

    [Fact]
    public void A_conclusive_answer_ends_the_watch_on_the_first_poll()
    {
        // The other half of DD134. Waiting six polls to act on a process handle that has already
        // reported the daemon exited is not caution, it is twelve wasted seconds — and the point of
        // the tolerance was never to disbelieve evidence, only to stop inventing it.
        var watch = new EngineWatch();
        var gone = new EngineStatus(EngineState.Stopped, "the daemon exited") { Conclusive = true };

        Assert.False(watch.KeepServing(gone));
    }

    [Fact]
    public void A_conclusive_answer_is_reported_without_a_count_of_polls()
    {
        // "1 polls in a row" beside a conclusive reading describes a run of silence that never
        // happened, and sends the reader hunting a load problem instead of reading the detail.
        var watch = new EngineWatch();
        var gone = new EngineStatus(EngineState.Stopped, "the daemon exited") { Conclusive = true };
        watch.KeepServing(gone);

        var said = watch.WhyItStopped(gone);

        Assert.Contains("the daemon exited", said, StringComparison.Ordinal);
        Assert.DoesNotContain("polls in a row", said, StringComparison.Ordinal);
    }

    [Fact]
    public void No_run_of_slow_polls_can_reach_the_verdict_that_kills_a_working_daemon()
    {
        // The measured failure of 17 August 2026, as an assertion. A daemon that had logged its own
        // initialization and never logged a shutdown was cut off eleven minutes later because the
        // pings in front of it lost a race for process creation. Whatever else changes, a reading
        // that only says "nothing answered" must never be the one that terminates the distribution.
        var watch = new EngineWatch();
        var slow = new EngineStatus(EngineState.Starting, "the daemon is running and no answer within 3s");

        for (var i = 0; i < EngineWatch.ToleratedQuietPolls * 10; i++)
        {
            if (!watch.KeepServing(slow))
            {
                // It did come down — which is correct — but only ever on a run of silence, never on
                // a single reading mistaken for proof.
                Assert.Equal(EngineWatch.ToleratedQuietPolls, watch.QuietPolls);
                Assert.False(slow.Conclusive);
                return;
            }
        }

        Assert.Fail("the watch never came down, so the run of quiet polls is not bounded at all");
    }

    [Fact]
    public void The_line_it_prints_says_how_many_times_the_engine_was_asked()
    {
        // Without the count this is the line --run printed before DD133, and that line was
        // indistinguishable from the false alarm it usually was.
        var watch = new EngineWatch();
        var last = Quiet();
        while (watch.KeepServing(last))
        {
        }

        var said = watch.WhyItStopped(last);

        Assert.Contains($"{EngineWatch.ToleratedQuietPolls} polls in a row", said, StringComparison.Ordinal);
        Assert.Contains(last.Detail, said, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_poll_that_starts_the_silence_announces_it()
    {
        // DD174. The crossing is one event and the run is another, and a host that said "gone quiet"
        // on every poll of a failure would write six lines about one outage — which is the file
        // repeating itself, and the reason nothing was written at all before this.
        var watch = new EngineWatch();

        watch.KeepServing(Quiet());
        Assert.True(watch.JustWentQuiet);

        for (var i = 1; i < EngineWatch.ToleratedQuietPolls; i++)
        {
            watch.KeepServing(Quiet());
            Assert.False(watch.JustWentQuiet);
        }
    }

    [Fact]
    public void An_engine_that_answers_and_goes_quiet_again_is_a_second_crossing()
    {
        // A flap is not one long failure, and reporting it as one would lose the shape of it. Each
        // spell is a real crossing out of a working engine, and a journal showing several in a
        // minute is describing a machine its reader needs to know about.
        var watch = new EngineWatch();

        watch.KeepServing(Quiet());
        watch.KeepServing(Running());
        watch.KeepServing(Quiet());

        Assert.True(watch.JustWentQuiet);
    }

    [Fact]
    public void The_crossing_names_itself_rather_than_reading_as_the_verdict_written_twice()
    {
        // Both lines carry the same state and the same detail — they are the same engine, seen ten
        // seconds apart — so without the tail the pair reads as one observation logged twice, and
        // the earlier of them stops dating anything.
        var watch = new EngineWatch();
        var first = Quiet();
        watch.KeepServing(first);

        var said = watch.WhenItWentQuiet(first);

        Assert.Contains(first.Detail, said, StringComparison.Ordinal);
        Assert.Contains("first quiet poll", said, StringComparison.Ordinal);
        Assert.DoesNotContain("polls in a row", said, StringComparison.Ordinal);
    }

    [Fact]
    public void The_tolerance_outlasts_the_window_a_caller_was_promised()
    {
        // DD133 asked whether "is the engine ready" could be an answer that survives the next thirty
        // seconds. At the two seconds between polls and the three the ping is given, the tolerance
        // has to reach that far or the question is still open.
        var worstPoll = TimeSpan.FromSeconds(2) + TimeSpan.FromSeconds(3);

        Assert.True(
            worstPoll * EngineWatch.ToleratedQuietPolls >= TimeSpan.FromSeconds(30),
            "the run of quiet polls is shorter than the thirty seconds DD133 named");
    }
}
