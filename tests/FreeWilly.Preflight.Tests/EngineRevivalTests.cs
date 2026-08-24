using FreeWilly.Core.Engine;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// Whether a lost engine gets another attempt, and how long the host waits first (DD136).
/// </summary>
public sealed class EngineRevivalTests
{
    private static EngineStatus Gone() =>
        new(EngineState.Stopped, "the daemon exited") { Conclusive = true };

    [Fact]
    public void A_fresh_host_owes_the_engine_an_attempt()
    {
        var revival = new EngineRevival();

        Assert.True(revival.WorthAnotherTry);
        Assert.Equal(0, revival.Failures);
        Assert.Equal(0, revival.Revivals);
    }

    [Fact]
    public void The_wait_grows_with_each_failure_rather_than_hammering_a_busy_machine()
    {
        // A machine still settling after a resume is made slower by being asked four times a second
        // for the thing it is busy doing.
        var revival = new EngineRevival();
        var waits = new List<TimeSpan>();

        for (var i = 0; i < EngineRevival.Attempts; i++)
        {
            waits.Add(revival.Wait);
            revival.Failed();
        }

        Assert.Equal(EngineRevival.FirstWait, waits[0]);
        for (var i = 1; i < waits.Count; i++)
        {
            Assert.True(
                waits[i] >= waits[i - 1],
                $"the wait shrank from {waits[i - 1]} to {waits[i]} at attempt {i}");
        }
    }

    /// <summary>
    /// The doubling is capped, so a fixed machine is not left waiting minutes (DD136).
    /// </summary>
    /// <remarks>
    /// The back-off exists to stop hammering a busy machine, not to punish a slow one: a user who
    /// repaired whatever was wrong should not sit in front of a working machine watching nothing.
    ///
    /// <para>Bounded to the quick attempts since DD164, which is where the doubling now lives.
    /// Past them the interval is <see cref="EngineRevival.PatientWait"/> by construction and this
    /// cap has nothing to say about it — a machine that has failed five times in a minute is no
    /// longer one somebody is standing in front of.</para>
    /// </remarks>
    [Fact]
    public void The_wait_is_capped_so_a_fixed_machine_is_not_left_waiting_minutes()
    {
        var revival = new EngineRevival();

        while (revival.WorthAnotherTry)
        {
            Assert.True(
                revival.Wait <= EngineRevival.LongestWait,
                $"the wait reached {revival.Wait}, past the {EngineRevival.LongestWait} cap");
            revival.Failed();
        }

        Assert.True(revival.Wait > TimeSpan.Zero, "the wait overflowed into nothing");
    }

    /// <summary>
    /// Running out of the quick attempts slows this down rather than ending it (DD164).
    /// </summary>
    /// <remarks>
    /// DD136 read this the other way and ended the host here, on the reasoning that an engine which
    /// cannot come up is a fact the user needs. Measured on 21 August 2026, what that bought was a
    /// machine offline for an hour and a sentence in a file nobody had been told to open — the fact
    /// reached nobody, and the silence the bound existed to prevent is what happened.
    /// </remarks>
    [Fact]
    public void Running_out_of_quick_attempts_slows_down_rather_than_stopping()
    {
        var revival = new EngineRevival();

        for (var i = 0; i < EngineRevival.Attempts; i++)
        {
            Assert.True(revival.WorthAnotherTry, $"slowed down early, at attempt {i}");
            Assert.False(revival.Patient, $"was patient early, at attempt {i}");
            revival.Failed();
        }

        Assert.False(revival.WorthAnotherTry);
        Assert.True(revival.Patient);
        Assert.Equal(EngineRevival.PatientWait, revival.Wait);
    }

    /// <summary>
    /// The wait stays at the long interval however many times it goes on failing (DD164).
    /// </summary>
    /// <remarks>
    /// A laptop left broken overnight reaches this a couple of hundred times. The interval must not
    /// grow with the count — the doubling exists to stop hammering a busy machine, and past the
    /// quick attempts that job belongs to <see cref="EngineRevival.PatientWait"/> alone — and it
    /// must not overflow, which is what the unclamped doubling would do at these counts.
    /// </remarks>
    [Fact]
    public void A_host_that_has_failed_all_night_still_asks_every_five_minutes()
    {
        var revival = new EngineRevival();
        while (revival.WorthAnotherTry)
        {
            revival.Failed();
        }

        for (var i = 0; i < 500; i++)
        {
            Assert.Equal(EngineRevival.PatientWait, revival.Wait);
            revival.Failed();
        }
    }

    /// <summary>
    /// The crossing into the long wait is announceable exactly once (DD164).
    /// </summary>
    /// <remarks>
    /// The host says it when this is true, so a flag that stayed true would put the same sentence in
    /// the journal every five minutes for as long as the machine stayed broken — and that file is
    /// worth opening because everything in it is something that happened.
    /// </remarks>
    [Fact]
    public void The_slowdown_is_worth_saying_once_and_not_every_five_minutes()
    {
        var revival = new EngineRevival();
        var crossings = 0;

        for (var i = 0; i < 50; i++)
        {
            revival.Failed();
            if (revival.JustRanOutOfQuickAttempts)
            {
                crossings++;
            }
        }

        Assert.Equal(1, crossings);
    }

    [Fact]
    public void An_engine_that_came_back_gets_the_full_budget_again()
    {
        // The failures counted are the consecutive ones. A laptop suspended twice a day for a week
        // must not run out of attempts on the Friday because of what happened on the Monday.
        var revival = new EngineRevival();
        for (var i = 0; i < EngineRevival.Attempts - 1; i++)
        {
            revival.Failed();
        }

        revival.Revived();

        Assert.True(revival.WorthAnotherTry);
        Assert.Equal(0, revival.Failures);
        Assert.Equal(EngineRevival.FirstWait, revival.Wait);
        Assert.Equal(1, revival.Revivals);
    }

    /// <summary>
    /// Slowing down says how many times it tried and what it will do next (DD136, DD164).
    /// </summary>
    /// <remarks>
    /// The count, because a host that has tried five times and one that has not tried at all are
    /// different machines to be sitting in front of. The interval, because this sentence used to be
    /// the last line in the file: a reader who found it an hour later could not tell whether
    /// anything was still watching, and now it says so.
    /// </remarks>
    [Fact]
    public void Slowing_down_says_how_many_times_it_tried_and_that_it_has_not_stopped()
    {
        var revival = new EngineRevival();
        while (revival.WorthAnotherTry)
        {
            revival.Failed();
        }

        var said = revival.WhyItIsSlowingDown(Gone());

        Assert.Contains($"{EngineRevival.Attempts} attempts", said, StringComparison.Ordinal);
        Assert.Contains("the daemon exited", said, StringComparison.Ordinal);
        Assert.Contains("still trying", said, StringComparison.Ordinal);
        Assert.Contains(
            $"{EngineRevival.PatientWait.TotalMinutes:0} minutes", said, StringComparison.Ordinal);
    }

    [Fact]
    public void The_whole_budget_covers_a_resume_without_outlasting_a_users_patience()
    {
        // The two numbers the shape has to satisfy at once. Too short and a laptop that takes its
        // time coming back is declared dead; too long and the tray sits amber while somebody waits.
        var revival = new EngineRevival();
        var total = TimeSpan.Zero;
        while (revival.WorthAnotherTry)
        {
            total += revival.Wait;
            revival.Failed();
        }

        Assert.True(total >= TimeSpan.FromSeconds(30), $"only {total} spent before giving up");
        Assert.True(total <= TimeSpan.FromMinutes(3), $"{total} is longer than anybody waits");
    }

    // ---- how long the engine was away, not only how often (DD182) -----------------------------

    private static EngineStatus Back() =>
        new(EngineState.Running, @"the engine answered on \\.\pipe\docker_engine", "1.55");

    [Fact]
    public void A_restart_says_how_long_the_engine_was_unreachable()
    {
        // DD182. The count says how often and never how bad, and on 24 August 2026 the answer was
        // ten seconds — which a reader could only get by subtracting two timestamps a scroll apart.
        var revival = new EngineRevival();
        revival.Revived();

        var said = revival.BroughtItBack(Back(), TimeSpan.FromSeconds(38));

        Assert.Contains("(restart 1)", said, StringComparison.Ordinal);
        Assert.Contains("38s down", said, StringComparison.Ordinal);
    }

    [Fact]
    public void An_outage_past_a_minute_is_spelled_in_minutes_and_seconds()
    {
        // Past a minute the minutes are what a reader compares two incidents on, and the seconds are
        // what keeps two of them from reading as the same number.
        var revival = new EngineRevival();
        revival.Revived();

        Assert.Contains(
            "4m 12s down",
            revival.BroughtItBack(Back(), TimeSpan.FromSeconds(252)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_clock_that_went_backwards_is_not_reported_as_a_negative_outage()
    {
        // Not hypothetical on the machines this supervisor exists for: a resume is exactly where a
        // clock steps, and "-3s down" reads as a defect in the tool rather than a fact about the
        // engine — which is the one thing a journal line must never do.
        var revival = new EngineRevival();
        revival.Revived();

        Assert.Contains(
            "0s down",
            revival.BroughtItBack(Back(), TimeSpan.FromSeconds(-3)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_caller_that_cannot_time_the_outage_still_gets_the_line_it_always_had()
    {
        // The span is optional because measuring it is the host's job, not this type's — counted
        // rather than timed is what the rest of this class is built on.
        var revival = new EngineRevival();
        revival.Revived();

        Assert.Equal(
            $"Running   {EngineRevival.RestartMark} (restart 1)",
            revival.BroughtItBack(Back()));
    }

    [Fact]
    public void The_window_still_counts_a_restart_that_carries_its_outage()
    {
        // The coupling DD165 exists for, re-asserted against the new tail. The digest matches on
        // the mark, so an appended clause is safe — and this is the assertion that keeps it safe,
        // because nothing fails to compile when the sentence grows a suffix that breaks the match.
        var revival = new EngineRevival();
        revival.Revived();

        var written = revival.BroughtItBack(Back(), TimeSpan.FromSeconds(38));

        Assert.Equal(1, JournalDigest.Of([$"2026-08-24 14:01:24  {written}"]).Restarts);
    }

    // ---- the wait before an outage is worth interrupting somebody about (DD183) ---------------

    [Fact]
    public void The_grace_outlasts_the_wait_before_the_host_even_tries()
    {
        // The ordering is the claim, the same shape TrayTests holds the start budget to. A balloon
        // that fired before the first attempt had been made would be announcing an outage nothing
        // had yet been done about, which is the crossing and not the outage.
        Assert.True(
            EngineRevival.BlipGrace > EngineRevival.FirstWait,
            $"the balloon fires {EngineRevival.BlipGrace.TotalSeconds:0}s in, before the host's "
            + $"first attempt at {EngineRevival.FirstWait.TotalSeconds:0}s");
    }

    [Fact]
    public void The_grace_covers_the_blip_that_was_actually_measured()
    {
        // 24 August 2026: gone at 14:01:14, back at 14:01:24. That incident is the whole reason for
        // this number, so a grace that would still have interrupted the user through it fails.
        Assert.True(
            EngineRevival.BlipGrace >= TimeSpan.FromSeconds(10),
            $"{EngineRevival.BlipGrace.TotalSeconds:0}s would have announced the ten-second blip "
            + "of 24 August 2026 anyway");
    }

    [Fact]
    public void The_grace_does_not_wait_out_the_whole_recovery()
    {
        // The other end, and it is DD164's ground being defended. An announcement held until the
        // quick attempts were spent would be one nobody ever saw — a tray sitting silently on
        // Stopped while a hidden process works is exactly the silence that task removed.
        var revival = new EngineRevival();
        var quick = TimeSpan.Zero;
        while (revival.WorthAnotherTry)
        {
            quick += revival.Wait;
            revival.Failed();
        }

        Assert.True(
            EngineRevival.BlipGrace < quick,
            $"{EngineRevival.BlipGrace.TotalSeconds:0}s outlasts the {quick.TotalSeconds:0}s of "
            + "quick attempts, so the user hears nothing while the host is still working");
    }
}
