using FreeWilly.Core.Engine;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// The sequence that hands blocks back to Windows, and what it refuses to do on the way (DD211).
/// </summary>
/// <remarks>
/// The order is the whole of it. Pruning and trimming free blocks inside the filesystem, terminating
/// releases the disk, and only then can Windows be handed anything — a run that did those in any
/// other order would report a reclaim it had not made.
/// </remarks>
public sealed class DiskCompactionTests
{
    private static EnginePaths Paths() =>
        new(Path.Combine(Path.GetTempPath(), $"fw-{Guid.NewGuid():N}"));

    /// <summary>What <c>df -k /</c> answers through the state script, as one reading.</summary>
    private static string Reading(long usedKb) =>
        "device=/dev/sdd\noptions=rw,relatime\nerrors=0\nfirst=\nlast=\n"
        + $"blocks=67108864\nused={usedKb}\n";

    private static DiskCompaction Compaction(
        FakeWsl wsl, RepairStep? prune = null, RepairStep? stop = null) =>
        new(
            wsl,
            Paths(),
            () => prune ?? new RepairStep(DiskCompaction.PruneStep, true, "nothing to drop"),
            () => stop ?? new RepairStep(FilesystemRepair.StopStep, true, "the host was told"));

    /// <summary>A machine that answers every call in a healthy run.</summary>
    private static FakeWsl Machine() =>
        new FakeWsl()
            .Answer(0, Reading(16_000_000)) // the reading before
            .Answer(0, "/: 12 GiB (12884901888 bytes) trimmed")
            .Answer(0)                      // --terminate
            .Answer(0)                      // --manage --set-sparse true
            .Answer(0, Reading(16_000_000)); // the reading after

    [Fact]
    public void The_disk_is_handed_back_only_after_the_distribution_is_terminated()
    {
        // --set-sparse against a disk still in use is what --allow-unsafe is for, and reaching for
        // that from a housekeeping button is how a tidy-up becomes the thing that breaks a
        // filesystem.
        var wsl = Machine();

        Compaction(wsl).Run();

        var terminated = wsl.Invocations.FindIndex(
            argv => argv.Length > 0 && argv[0] == "--terminate");
        var handed = wsl.Invocations.FindIndex(argv => argv.Contains("--set-sparse"));

        Assert.True(terminated >= 0, "the distribution was never terminated");
        Assert.True(handed >= 0, "nothing was ever handed back");
        Assert.True(terminated < handed, "the disk was handed back while it was still in use");
        Assert.DoesNotContain(
            wsl.Invocations, argv => argv.Contains("--allow-unsafe", StringComparer.Ordinal));
    }

    [Fact]
    public void The_filesystem_is_trimmed_before_the_distribution_goes_down()
    {
        // fstrim needs the filesystem mounted, so a trim after the terminate is a trim of nothing —
        // and the blocks it would have discarded stay on the Windows volume.
        var wsl = Machine();

        Compaction(wsl).Run();

        var trimmed = wsl.Invocations.FindIndex(
            argv => argv.Any(word => word.Contains("fstrim", StringComparison.Ordinal)));
        var terminated = wsl.Invocations.FindIndex(
            argv => argv.Length > 0 && argv[0] == "--terminate");

        Assert.True(trimmed >= 0, "the filesystem was never trimmed");
        Assert.True(trimmed < terminated, "the trim ran against a distribution already down");
    }

    [Fact]
    public void The_engine_is_told_to_stop_before_the_distribution_is_terminated()
    {
        // DD207's lesson, and the reason the stop is a seam rather than a wsl call: a host that was
        // not told puts the engine back, and it would put it back under the terminate this needs.
        var wsl = Machine();
        var stoppedAfter = -1;

        new DiskCompaction(
            wsl,
            Paths(),
            () => new RepairStep(DiskCompaction.PruneStep, true, "nothing to drop"),
            () =>
            {
                stoppedAfter = wsl.Invocations.Count;
                return new RepairStep(FilesystemRepair.StopStep, true, "the host was told");
            }).Run();

        var terminated = wsl.Invocations.FindIndex(
            argv => argv.Length > 0 && argv[0] == "--terminate");

        Assert.True(stoppedAfter >= 0, "the engine was never stopped");
        Assert.True(stoppedAfter <= terminated, "the terminate ran before the host was told");
    }

    [Fact]
    public void Nothing_here_removes_an_image_or_a_volume()
    {
        // The rule the confirmation promises. The only thing this deletes is cache the daemon calls
        // reclaimable, and that is a seam: nothing in the sequence itself may reach for docker, and
        // nothing in it may unregister the distribution the images are in.
        var wsl = Machine();

        Compaction(wsl).Run();

        foreach (var forbidden in new[] { "docker", "prune", "--unregister", "--set-default" })
        {
            Assert.DoesNotContain(
                wsl.Invocations,
                argv => argv.Any(word => word.Contains(forbidden, StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void A_daemon_that_would_not_prune_does_not_stop_the_run()
    {
        // Preparation, not the job. Every byte the prune would have freed is a byte this cannot hand
        // back, and refusing to hand back the rest would leave the disk larger for the sake of it.
        var wsl = Machine();

        var outcome = Compaction(
            wsl,
            prune: new RepairStep(DiskCompaction.PruneStep, false, "the engine is not answering"))
            .Run();

        Assert.True(outcome.Succeeded, outcome.Failure?.Detail);
        Assert.Contains(wsl.Invocations, argv => argv.Contains("--set-sparse"));
    }

    [Fact]
    public void A_filesystem_that_would_not_trim_does_not_stop_the_run()
    {
        // fstrim is a BusyBox applet as well as a util-linux one, but a distribution provisioned
        // before it was there would say so. WSL2 mounts with discard, so most of the reclaim is
        // already earned by the time this runs.
        var wsl = new FakeWsl()
            .Answer(0, Reading(16_000_000))
            .Answer(127, "sh: fstrim: not found")
            .Answer(0)
            .Answer(0)
            .Answer(0, Reading(16_000_000));

        var outcome = Compaction(wsl).Run();

        Assert.True(outcome.Succeeded, outcome.Failure?.Detail);
        Assert.Contains(
            outcome.Steps,
            step => step.What == DiskCompaction.TrimStep && !step.Ok);
    }

    [Fact]
    public void A_terminate_that_failed_stops_before_anything_is_handed_back()
    {
        // The one step in the sequence that is not preparation. A --set-sparse against a running
        // distribution is refused or is unsafe, and neither is a thing to try anyway.
        var wsl = new FakeWsl()
            .Answer(0, Reading(16_000_000))
            .Answer(0, "/: 0 B (0 bytes) trimmed")
            .Answer(1, "there is no distribution named freewilly");

        var outcome = Compaction(wsl).Run();

        Assert.False(outcome.Succeeded);
        Assert.DoesNotContain(wsl.Invocations, argv => argv.Contains("--set-sparse"));
        Assert.True(outcome.EngineWentDown, "the engine went down for this and the outcome hides it");
    }

    [Fact]
    public void A_virtual_disk_that_grew_is_reported_as_two_sizes_and_not_as_a_reclaim()
    {
        // Arithmetic over two readings this run took itself, and it refuses to invent a negative
        // one. A button claiming credit for a disk that got larger is worse than one claiming
        // nothing.
        var outcome = new CompactionOutcome([])
        {
            Before = new DiskSizes(50L * 1024 * 1024 * 1024, null),
            After = new DiskSizes(52L * 1024 * 1024 * 1024, null),
        };

        Assert.Null(outcome.HandedBack);

        var shrank = outcome with { After = new DiskSizes(30L * 1024 * 1024 * 1024, null) };
        Assert.Equal(20L * 1024 * 1024 * 1024, shrank.HandedBack);
    }

    [Fact]
    public void Both_readings_are_taken_by_the_run_itself()
    {
        // The panel above the button shows these two numbers, and the claim is that one of them
        // moved. A run that took the first reading and trusted the second would be reporting a
        // figure nobody measured.
        var wsl = Machine();

        var outcome = Compaction(wsl).Run();

        Assert.NotNull(outcome.Before);
        Assert.NotNull(outcome.After);
        Assert.Equal(16_000_000L * 1024, outcome.Before.UsedInside);
    }
}
