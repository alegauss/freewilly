using System.Diagnostics;
using FreeWilly.Core.Engine;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// The window a provision announces while it rewrites the engine's binaries (DD269).
/// </summary>
/// <remarks>
/// Over real files in a real temporary directory, because the whole mechanism is what the operating
/// system does with a share mode. A fake would be asserting that this test's own idea of locking
/// matches itself, and the defect being prevented is a start exec'ing a file another process holds.
/// </remarks>
public sealed class EngineUnpackTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"freewilly-unpack-{Guid.NewGuid():N}");

    private string Lock => Path.Combine(_root, "unpack.lock");

    public EngineUnpackTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A suite that fails on a temporary directory Windows has not finished releasing is a
            // suite that goes red for a reason no reader can act on.
        }
    }

    [Fact]
    public void Nothing_is_in_flight_where_no_provision_has_ever_run()
    {
        // The common path, and the one every start on every machine takes.
        Assert.False(EngineUnpack.InFlight(Lock));
    }

    [Fact]
    public void A_held_lock_is_in_flight_and_a_released_one_is_not()
    {
        var held = EngineUnpack.Hold(Lock);
        Assert.NotNull(held);
        Assert.True(EngineUnpack.InFlight(Lock));

        held.Dispose();
        Assert.False(EngineUnpack.InFlight(Lock));
    }

    [Fact]
    public void A_file_left_behind_by_a_provision_that_died_is_not_in_flight()
    {
        // The reason this is a file rather than a named mutex. A provision killed mid-unpack leaves
        // the lock on disk, and by then nothing is writing the binaries, so the honest answer is no.
        // A start that treated the leftover as a live install would wait out its budget for nothing,
        // on every start, until somebody found the file and deleted it.
        File.WriteAllText(Lock, "");

        Assert.False(EngineUnpack.InFlight(Lock));
    }

    [Fact]
    public async Task Waiting_where_nothing_holds_the_lock_costs_nothing()
    {
        var waited = await EngineUnpack.WaitForIdleAsync(Lock, TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.Zero, waited);
    }

    [Fact]
    public async Task Waiting_ends_when_the_provision_releases_the_lock()
    {
        var held = EngineUnpack.Hold(Lock);
        Assert.NotNull(held);

        // Released from underneath the wait, which is the provision finishing its unpack.
        var releasing = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400));
            held.Dispose();
        });

        var waited = await EngineUnpack.WaitForIdleAsync(Lock, TimeSpan.FromSeconds(10));
        await releasing;

        Assert.True(waited > TimeSpan.Zero, "the wait should have noticed the lock was held");
        Assert.False(EngineUnpack.InFlight(Lock));
    }

    [Fact]
    public async Task Waiting_gives_up_at_the_budget_rather_than_hanging_the_start()
    {
        // The bound, and it is the property that keeps this a courtesy rather than a new way for a
        // start to never return. A provision wedged forever must not make every subsequent start
        // hang: past the budget the start goes ahead and the relaunch (DD267) is what catches it.
        using var held = EngineUnpack.Hold(Lock);
        Assert.NotNull(held);

        var clock = Stopwatch.StartNew();
        var waited = await EngineUnpack.WaitForIdleAsync(Lock, TimeSpan.FromMilliseconds(600));
        clock.Stop();

        Assert.True(EngineUnpack.InFlight(Lock));
        Assert.True(
            clock.Elapsed < TimeSpan.FromSeconds(5),
            $"the wait ran to {clock.Elapsed} against a budget of 600ms");
        Assert.True(waited > TimeSpan.Zero);
    }

    [Fact]
    public void The_lock_is_taken_under_a_root_that_does_not_exist_yet()
    {
        // A fresh install provisions before anything has created the directory, and a provision that
        // could not announce itself there would be one that races a start on exactly the install
        // where the unpack is longest.
        var fresh = Path.Combine(_root, "never-created", "unpack.lock");

        using var held = EngineUnpack.Hold(fresh);

        Assert.NotNull(held);
        Assert.True(EngineUnpack.InFlight(fresh));
    }
}
