using FreeWilly.Tray.Cli;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// One tray per session, and what a second launch does instead of starting another (DD81).
/// </summary>
/// <remarks>
/// These claim the real named objects, so they run in the console collection to keep them off the
/// same instant as anything else — the names are the product's and there is nothing to parameterise
/// without testing a different thing from the one that ships.
/// </remarks>
[Collection(ConsoleCollection.Name)]
public sealed class SingleTrayTests
{
    /// <summary>
    /// Stand aside where the product itself is holding the object these tests claim (DD103).
    /// </summary>
    /// <remarks>
    /// The consequence of claiming the real names, which is right and which nobody wrote down: the
    /// suite cannot be run on a machine where the product is running, and that is every machine
    /// that uses it. Three tests failed and none of them said so —
    /// <see cref="The_first_claim_wins_and_the_second_is_told_to_step_aside"/> reported that a claim
    /// succeeded when it should not have, and the cause was a tray in the notification area left
    /// over from a smoke test.
    ///
    /// <para><b>The ordinary case is a skip now, and this is the narrow guard behind it</b> (DD202).
    /// <see cref="FactUnlessTheTrayIsRunningAttribute"/> asks the same question at discovery, so a
    /// machine with FreeWilly up gets nine skips rather than nine failures. What is left for this
    /// method is the race the attribute cannot see: a tray started between discovery and execution,
    /// where the body really did run and really did assert nothing.</para>
    ///
    /// <para><b>An earlier pass decided against skipping and its reason was too strong.</b> It said
    /// xUnit v2 had no supported way to ask for one, which is true of <c>Assert.Skip</c> and
    /// <c>SkipException</c> — both v3 — and not true of <see cref="FactAttribute.Skip"/>, which is
    /// virtual and read at discovery. Overriding it is typed rather than a magic string: the concern
    /// that it would stop working silently is what a compile error is for.</para>
    ///
    /// <para>The mutex is unprefixed and therefore session-local, so this is never about another
    /// user's tray. <c>TryClaim</c> answering false is the whole detection; what was missing was
    /// reading it before the assertions rather than through them.</para>
    /// </remarks>
    private static void RequireTheTraySlot()
    {
        if (SingleTray.TryClaim(out var probe))
        {
            probe!.Dispose();
            return;
        }

        // Named as an unmade assertion rather than a wrong one. Reached only where a tray appeared
        // after discovery had already found the slot free, so it says that rather than repeating
        // the attribute's sentence.
        Assert.Fail(
            $"FreeWilly's tray took {SingleTray.Name} after this run started, which is the very "
            + "object these tests claim — so nothing below was actually asserted. Quit it and "
            + "re-run.");
    }

    /// <summary>
    /// Try to claim from somewhere that is not this thread, which is what a second launch is.
    /// </summary>
    /// <remarks>
    /// A mutex is owned by a thread and is reentrant, so asking twice on one thread succeeds twice
    /// — which is not the question. The second launch is another process, and another thread is the
    /// nearest thing a test can be.
    /// </remarks>
    private static bool ClaimedElsewhere()
    {
        var got = false;
        var thread = new Thread(() =>
        {
            if (SingleTray.TryClaim(out var claim))
            {
                got = true;
                claim!.Dispose();
            }
        });

        thread.Start();
        thread.Join();
        return got;
    }

    [FactUnlessTheTrayIsRunning]
    public void A_running_tray_is_named_by_the_failure_rather_than_left_to_an_assertion()
    {
        // DD103 itself, asserted. Before this the reader of a red suite was told that a claim
        // succeeded when it should not have, and the cause — a tray in the notification area, left
        // over from a smoke test — appeared in no message. What is worth holding is not that it
        // fails but what it says while failing, because that sentence is the remedy.
        RequireTheTraySlot();

        using var taken = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        // From another thread and held there: a mutex is owned by a thread and is reentrant, so a
        // claim made on this one would let the probe straight through and test nothing.
        var holder = new Thread(() =>
        {
            var mine = SingleTray.TryClaim(out var claim);
            taken.Set();
            release.Wait();
            if (mine)
            {
                claim!.Dispose();
            }
        });

        holder.Start();
        Assert.True(taken.Wait(TimeSpan.FromSeconds(5)), "the stand-in tray never claimed the slot");

        var failure = Record.Exception(RequireTheTraySlot);

        release.Set();
        holder.Join();

        // The wording is the assertion, so it is held to the sentence that ships: DD202 rewrote the
        // message for the race it now guards and left these three checking the sentence before it,
        // which no machine with a tray on it ever ran.
        Assert.NotNull(failure);
        Assert.Contains("FreeWilly's tray took", failure.Message, StringComparison.Ordinal);
        Assert.Contains(SingleTray.Name, failure.Message, StringComparison.Ordinal);
        Assert.Contains("Quit it and re-run", failure.Message, StringComparison.Ordinal);
    }

    [FactUnlessTheTrayIsRunning]
    public void The_first_claim_wins_and_the_second_is_told_to_step_aside()
    {
        RequireTheTraySlot();

        Assert.True(SingleTray.TryClaim(out var first));
        using (first)
        {
            // The failure this removes: every extra click used to be another process, another icon
            // in the overflow and another event stream open on one daemon.
            Assert.False(ClaimedElsewhere());
        }
    }

    [FactUnlessTheTrayIsRunning]
    public void The_slot_is_free_again_once_the_tray_lets_it_go()
    {
        RequireTheTraySlot();

        // Quitting the tray has to leave a machine able to start one, or the fix would be worse
        // than the defect.
        Assert.True(SingleTray.TryClaim(out var first));
        first!.Dispose();

        Assert.True(ClaimedElsewhere());
    }

    [FactUnlessTheTrayIsRunning]
    public void A_second_launch_raises_the_live_one()
    {
        RequireTheTraySlot();

        Assert.True(SingleTray.TryClaim(out var only));
        using (only)
        {
            using var raised = new ManualResetEventSlim(false);
            only!.OnRaise(() => raised.Set());

            SingleTray.RaiseTheLiveOne();

            Assert.True(
                raised.Wait(TimeSpan.FromSeconds(5)),
                "the live instance was never asked to show its window");
        }
    }

    [FactUnlessTheTrayIsRunning]
    public void The_quit_signal_reaches_the_live_one_and_is_not_the_raise()
    {
        RequireTheTraySlot();

        Assert.True(SingleTray.TryClaim(out var only));
        using (only)
        {
            using var asked = new ManualResetEventSlim(false);
            using var raised = new ManualResetEventSlim(false);
            only!.OnRaise(() => raised.Set());
            only.OnQuit(() => asked.Set());

            Assert.True(SingleTray.AskTheLiveOneToQuit());

            Assert.True(
                asked.Wait(TimeSpan.FromSeconds(5)),
                "the live instance was never asked to close");

            // Two named objects rather than one, and this is why: an auto-reset event carries no
            // payload, so a single handle would make "show yourself" and "close yourself" the same
            // signal — and the uninstaller would put a window on screen on its way to deleting it.
            Assert.False(raised.IsSet, "asking the tray to quit also asked it to show its window");
        }
    }

    [FactUnlessTheTrayIsRunning]
    public void The_stop_a_verb_asks_for_reaches_the_tray_and_is_none_of_the_other_three()
    {
        // DD213. DD210 closed this for the window, which is in the tray's own process and can call
        // the method. A verb cannot, so `freewilly --fsck` took the engine down and the tray beside
        // it saw only an engine that had gone away — fifteen seconds later announcing the outage the
        // user had typed.
        RequireTheTraySlot();

        Assert.True(SingleTray.TryClaim(out var only));
        using (only)
        {
            using var expecting = new ManualResetEventSlim(false);
            using var raised = new ManualResetEventSlim(false);
            using var quit = new ManualResetEventSlim(false);
            using var build = new ManualResetEventSlim(false);
            only!.OnEngineStopAsked(() => expecting.Set());
            only.OnRaise(() => raised.Set());
            only.OnQuit(() => quit.Set());
            only.OnBuild(() => build.Set());

            Assert.True(SingleTray.AskTheLiveOneToExpectAStop());
            Assert.True(
                expecting.Wait(TimeSpan.FromSeconds(5)),
                "the tray was never told the stop was asked for");

            // A fourth named object, for the reason there is a third and a second: an auto-reset
            // event carries no payload, so a shared handle would make announcing a stop the same
            // signal as raising a window or closing the tray.
            Assert.False(raised.IsSet, "announcing a stop also raised the window");
            Assert.False(quit.IsSet, "announcing a stop also closed the tray");
            Assert.False(build.IsSet, "announcing a stop also opened a build");
        }
    }

    [FactUnlessTheTrayIsRunning]
    public void Announcing_a_stop_with_no_tray_running_is_not_a_failure()
    {
        // The ordinary case for a verb: nobody has the tray open. There is then nothing that could
        // mistake the teardown for an outage, which is the state the announcement was for.
        RequireTheTraySlot();

        Assert.False(SingleTray.AskTheLiveOneToExpectAStop());
    }

    [FactUnlessTheTrayIsRunning]
    public void Asking_a_machine_with_no_tray_to_quit_is_not_a_failure()
    {
        // What the uninstaller runs on a machine where nobody ever opened the tray. It asked for a
        // machine with no tray on it and that is what it has, so `--quit` reports success — exit 1
        // is kept for the one answer the uninstaller has to act on, which is a tray that stayed.
        RequireTheTraySlot();

        Assert.False(SingleTray.AskTheLiveOneToQuit());
    }

    [FactUnlessTheTrayIsRunning]
    public void The_wait_answers_yes_only_once_the_slot_is_actually_free()
    {
        // The half that makes the verb usable from an uninstaller. The signal only says the request
        // was delivered; what has to be true before a delete is attempted is that the process is
        // gone, and the slot is released on the way out — so the mutex is what is watched.
        RequireTheTraySlot();

        using var taken = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        // Held from another thread, because a mutex is owned by a thread and is reentrant: a claim
        // made on this one would let the wait straight through and assert nothing.
        var holder = new Thread(() =>
        {
            var mine = SingleTray.TryClaim(out var claim);
            taken.Set();
            release.Wait();
            if (mine)
            {
                claim!.Dispose();
            }
        });

        holder.Start();
        Assert.True(taken.Wait(TimeSpan.FromSeconds(5)), "the stand-in tray never claimed the slot");

        // Short, because this asserts that it waits rather than how long it is willing to.
        Assert.False(
            SingleTray.WaitUntilTheTrayIsGone(TimeSpan.FromMilliseconds(300)),
            "the wait reported the tray gone while something still held the slot");

        release.Set();
        holder.Join();

        Assert.True(
            SingleTray.WaitUntilTheTrayIsGone(TimeSpan.FromSeconds(5)),
            "the wait never noticed the slot come free");
    }

    [FactUnlessTheTrayIsRunning]
    public void Quitting_when_nothing_holds_the_tray_leaves_the_slot_claimable()
    {
        // The probe the wait makes has to give the slot straight back, or a tray relaunched in the
        // same second would find it taken by something that only wanted to look.
        RequireTheTraySlot();

        Assert.True(SingleTray.WaitUntilTheTrayIsGone(TimeSpan.FromSeconds(1)));
        Assert.True(ClaimedElsewhere());
    }

    [FactUnlessTheTrayIsRunning]
    public void Raising_when_nothing_holds_the_tray_is_silent()
    {
        // The fourth, and it did not fail — which is worse. Its premise is in its name, and with a
        // tray running the premise is false: the call below reaches a live instance, asserts nothing
        // about the silence it claims to test, and puts that instance's window on screen in the
        // middle of a test run.
        RequireTheTraySlot();

        // A launch that found nothing to signal has nothing useful left to do, and throwing at
        // somebody who double-clicked would be worse than the silence being fixed.
        var exception = Record.Exception(SingleTray.RaiseTheLiveOne);

        Assert.Null(exception);
    }
}
