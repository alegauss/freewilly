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
            Before = new DiskSizes(50L * Gigabyte, null, 50L * Gigabyte),
            After = new DiskSizes(50L * Gigabyte, null, 52L * Gigabyte),
        };

        Assert.Null(outcome.HandedBack);

        var shrank = outcome with { After = new DiskSizes(50L * Gigabyte, null, 30L * Gigabyte) };
        Assert.Equal(20L * Gigabyte, shrank.HandedBack);
    }

    [Fact]
    public void The_figure_comes_off_what_the_volume_is_charging_for_and_not_off_the_length()
    {
        // DD225, and it is the whole claim the button makes. A hand-back turns the virtual disk
        // into a sparse file, which keeps its length while NTFS stops charging for the ranges
        // nothing wrote to — so a run measured by length reports no bytes on every occasion it
        // worked, which is the one sentence the button exists to be able to say.
        var handedBack = new CompactionOutcome([])
        {
            Before = new DiskSizes(50L * Gigabyte, null, 50L * Gigabyte),
            After = new DiskSizes(50L * Gigabyte, null, 12L * Gigabyte),
        };

        Assert.Equal(38L * Gigabyte, handedBack.HandedBack);

        // And the length is the fallback rather than a second opinion: on a volume that would not
        // answer, a figure taken the old way beats no figure at all, and on an ordinary file the
        // two are the same number anyway.
        var unreadable = new CompactionOutcome([])
        {
            Before = new DiskSizes(50L * Gigabyte, null),
            After = new DiskSizes(30L * Gigabyte, null),
        };

        Assert.Equal(20L * Gigabyte, unreadable.HandedBack);
    }

    [Fact]
    public void A_refusal_Windows_will_give_again_is_written_down_and_a_retryable_one_is_not()
    {
        // DD226. Sparse disks being off is a fact about the machine that a second press will meet
        // unchanged, and the price of meeting it is every container going down. Everything else that
        // can fail here is worth another try, and a machine remembering one of those would be a
        // button talking itself out of working.
        var paths = Paths();
        var withdrawn = new FakeWsl()
            .Answer(0, Reading(16_000_000))
            .Answer(0, "/: trimmed")
            .Answer(0)
            .Answer(1, "sparse VHD support is disabled. Use --set-sparse --allow-unsafe")
            .Answer(0, Reading(16_000_000));

        new DiskCompaction(
            withdrawn, paths, Prune, Stop).Run();

        Assert.True(
            DiskCompaction.WasRefusedHere(paths),
            "the machine met the one refusal a second press cannot get past and forgot it");

        var busy = Paths();
        var inUse = new FakeWsl()
            .Answer(0, Reading(16_000_000))
            .Answer(0, "/: trimmed")
            .Answer(0)
            .Answer(1, "the disk is still in use")
            .Answer(0, Reading(16_000_000));

        new DiskCompaction(inUse, busy, Prune, Stop).Run();

        Assert.False(
            DiskCompaction.WasRefusedHere(busy),
            "a refusal somebody could get a different answer to was written down as final");
    }

    [Fact]
    public void A_hand_back_that_worked_forgets_that_it_was_ever_refused()
    {
        // Windows disabled sparse disks rather than removing them, so a machine that starts
        // allowing them again must not go on being told it does not. A note that only ever
        // accumulates is a button that stops working permanently the first time it fails.
        var paths = Paths();
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.SparseRefusal, "refused, once");

        var wsl = new FakeWsl()
            .Answer(0, Reading(16_000_000))
            .Answer(0, "/: trimmed")
            .Answer(0)
            .Answer(0)
            .Answer(0, Reading(16_000_000));

        var outcome = new DiskCompaction(wsl, paths, Prune, Stop).Run();

        Assert.True(outcome.Succeeded, outcome.Failure?.Detail);
        Assert.False(
            DiskCompaction.WasRefusedHere(paths),
            "a hand-back that worked left the machine still marked as refusing them");
    }

    [Fact]
    public void A_failed_prune_does_not_become_the_outcome_of_a_refused_hand_back()
    {
        // DD244, found by running the verb with the engine stopped. The prune fails because there
        // is no daemon to answer, and Failure used to return the first step that did not pass — so
        // both surfaces reported the prune and neither could see the refusal underneath it.
        //
        // What that cost: RepairPrompt.Of asks WindowsWithdrewIt about Failure.Detail, so the
        // headline went back to the one DD224 removed and the elevated route DD237 added was not
        // offered on a machine that had nothing else left.
        var outcome = new CompactionOutcome(
        [
            new RepairStep(DiskCompaction.PruneStep, false, "the engine is not answering"),
            new RepairStep(DiskCompaction.TrimStep, true, "12 GiB trimmed"),
            new RepairStep(FilesystemRepair.StopStep, true, "told the host to stop"),
            new RepairStep(
                DiskCompaction.HandBackStep,
                false,
                $"Windows has turned this off and offers {DiskCompaction.UnsafeFlag} instead"),
        ]);

        Assert.Equal(DiskCompaction.HandBackStep, outcome.Failure?.What);
        Assert.True(DiskCompaction.WindowsWithdrewIt(outcome.Failure?.Detail));
        Assert.True(RepairPrompt.Of(outcome).OfferElevated);
        Assert.Equal("Windows has turned this off", RepairPrompt.Of(outcome).Headline);
    }

    [Fact]
    public void A_run_that_stopped_before_the_hand_back_still_says_where_it_stopped()
    {
        // The other half, and the reason this is not simply "read the hand-back". A run that never
        // reached the deciding step has no other account of itself, and reporting nothing at all
        // would be worse than reporting the preparation that stopped it.
        var outcome = new CompactionOutcome(
        [
            new RepairStep(DiskCompaction.PruneStep, true, "nothing to drop"),
            new RepairStep(FilesystemRepair.StopStep, true, "told the host to stop"),
            new RepairStep(
                FilesystemRepair.TerminateStep, false, "terminating freewilly failed"),
        ]);

        Assert.False(outcome.Succeeded);
        Assert.Equal(FilesystemRepair.TerminateStep, outcome.Failure?.What);
    }

    [Fact]
    public void The_elevated_route_decides_a_run_the_same_way_the_hand_back_does()
    {
        // Two names, one meaning, and they are read through one predicate so the pair cannot be
        // edited apart. A reader of the outcome should not have to know which route the machine
        // was able to take.
        var refused = new CompactionOutcome(
        [
            new RepairStep(DiskCompaction.PruneStep, false, "the engine is not answering"),
            new RepairStep(ElevatedCompaction.CompactStep, false, "diskpart exited 1"),
        ]);

        Assert.Equal(ElevatedCompaction.CompactStep, refused.Failure?.What);
        Assert.False(refused.Succeeded);
    }

    private static RepairStep Prune() =>
        new(DiskCompaction.PruneStep, true, "nothing to drop");

    private static RepairStep Stop() =>
        new(FilesystemRepair.StopStep, true, "the host was told");

    private const long Gigabyte = 1024L * 1024 * 1024;

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
