using FreeWilly.Tray.Cli;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// One engine host per session, and what a second <c>--run</c> does instead of joining it (DD133).
/// </summary>
/// <remarks>
/// These claim the real named object for the reason <see cref="SingleTrayTests"/> does: the name is
/// the product's, and parameterising it would test a different thing from the one that ships. So
/// they run in the console collection, and they stand aside — loudly — where a real engine host on
/// this session already holds the slot.
/// </remarks>
[Collection(ConsoleCollection.Name)]
public sealed class SingleEngineTests
{
    /// <summary>Stand aside where the product itself is holding the object these tests claim.</summary>
    /// <remarks>
    /// DD103's lesson, applied to the second slot: a suite run on a machine with the engine up would
    /// otherwise report that a claim succeeded when it should not have, and the cause — a
    /// <c>--run</c> serving the pipe in another window — would appear in no message.
    ///
    /// <para>Since DD202 the ordinary case never reaches here:
    /// <see cref="FactUnlessTheEngineIsRunningAttribute"/> asks the same question at discovery and
    /// these five are skipped instead. What is left is the race — a host that started after
    /// discovery — which is the one case where the body ran and asserted nothing.</para>
    /// </remarks>
    private static void RequireTheEngineSlot()
    {
        if (SingleEngine.TryClaim(out var probe))
        {
            probe!.Dispose();
            return;
        }

        Assert.Fail(
            $"a FreeWilly host took {SingleEngine.Name} after this run started, which is the very "
            + "object these tests claim — so nothing below was actually asserted. Stop it with "
            + "`freewilly --stop` and re-run.");
    }

    /// <summary>
    /// Try to claim from somewhere that is not this thread, which is what a second <c>--run</c> is.
    /// </summary>
    /// <remarks>
    /// A mutex is owned by a thread and is reentrant, so asking twice on one thread succeeds twice —
    /// which is not the question being asked.
    /// </remarks>
    private static bool ClaimedElsewhere()
    {
        var got = false;
        var thread = new Thread(() =>
        {
            if (SingleEngine.TryClaim(out var claim))
            {
                got = true;
                claim!.Dispose();
            }
        });

        thread.Start();
        thread.Join();
        return got;
    }

    [FactUnlessTheEngineIsRunning]
    public void The_first_host_wins_and_the_second_is_turned_away()
    {
        RequireTheEngineSlot();

        using var taken = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        // Held from another thread, because a mutex is reentrant on the one that owns it: a claim
        // made here would let the probe straight through and assert nothing.
        var holder = new Thread(() =>
        {
            var mine = SingleEngine.TryClaim(out var claim);
            taken.Set();
            release.Wait();
            if (mine)
            {
                claim!.Dispose();
            }
        });

        holder.Start();
        Assert.True(taken.Wait(TimeSpan.FromSeconds(5)), "the stand-in host never claimed the slot");

        // This is the whole of DD133's first half. Before it, the second --run got this far, found
        // the pipe answering, started nothing, and kept a timer that could terminate the
        // distribution the first one was serving.
        Assert.False(ClaimedElsewhere(), "a second engine host was allowed to start");

        release.Set();
        holder.Join(TimeSpan.FromSeconds(5));
    }

    [FactUnlessTheEngineIsRunning]
    public void The_slot_comes_free_when_the_host_goes_away()
    {
        RequireTheEngineSlot();

        // An engine stopped and started again must be startable, which means the release is real
        // and not merely a process ending.
        Assert.True(SingleEngine.TryClaim(out var first));
        first!.Dispose();

        Assert.True(ClaimedElsewhere(), "the slot stayed taken after the host released it");
    }

    [FactUnlessTheEngineIsRunning]
    public void The_engine_slot_is_not_the_trays()
    {
        RequireTheEngineSlot();

        // They are two objects on purpose: the tray and the engine host are ordinarily both running,
        // and one name would make the pair impossible. Named here because the two classes are near
        // enough to each other that a copied constant would go unnoticed.
        Assert.NotEqual(SingleTray.Name, SingleEngine.Name);
    }

    [FactUnlessTheEngineIsRunning]
    public void A_stop_announced_with_no_host_listening_is_not_a_failure()
    {
        RequireTheEngineSlot();

        // `--stop` on a machine with no engine host running is already in the state the caller
        // wanted, and DD136's signal must not turn that into an error — the teardown behind it still
        // runs either way.
        Assert.False(SingleEngine.TellTheLiveOneToStop());
    }

    [FactUnlessTheEngineIsRunning]
    public void The_host_hears_a_stop_that_announced_itself()
    {
        RequireTheEngineSlot();

        // The whole of why the signal exists (DD136). Once the host puts back an engine it loses,
        // `--stop` terminating the distribution is indistinguishable from WSL2 dying under a
        // suspend — so without this arriving, a deliberate stop would be undone by a restart.
        Assert.True(SingleEngine.TryClaim(out var host));
        using (host)
        {
            using var heard = new ManualResetEventSlim(false);
            host!.OnStop(heard.Set);

            Assert.True(SingleEngine.TellTheLiveOneToStop(), "nothing was listening for the stop");
            Assert.True(heard.Wait(TimeSpan.FromSeconds(5)), "the host never heard the stop");
        }
    }
}
