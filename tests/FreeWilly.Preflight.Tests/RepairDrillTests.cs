using FreeWilly.Core.Engine;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// The rehearsal of the write path, and the reading both surfaces share (DD215).
/// </summary>
/// <remarks>
/// <para><b>The drill itself was run, and that is the point of it.</b> On 29 August 2026 it made a
/// 32 MB ext4 image, broke <c>lost+found</c>'s reference count and the two free counters, and walked
/// the whole path: <c>e2fsck -fn</c> exited 4 reporting <c>Inode 11 ref count is 7, should be 2</c>,
/// the panel would have said "The filesystem has errors" and offered Repair, <c>-fy</c> mended it,
/// and the second check came back clean. That is the first time the writing path has ever run
/// against a filesystem with errors on it.</para>
///
/// <para><b>The first attempt at the damage is worth keeping, because it failed usefully.</b>
/// Breaking only the superblock free counters produced an <c>e2fsck -fn</c> that printed
/// <c>Free blocks count wrong</c> in full and exited zero — so the product called the disk clean
/// while showing the reader a page of complaints about it. The counters are recomputed rather than
/// trusted, which no fake exit code would ever have shown.</para>
///
/// <para>What is asserted here is the part that is pure: the sequence of calls, the teardown, and
/// the reading of an exit code that both the product and the drill now go through.</para>
/// </remarks>
public sealed class RepairDrillTests
{
    private static EnginePaths Paths() =>
        new(Path.Combine(Path.GetTempPath(), $"fw-{Guid.NewGuid():N}"));

    /// <summary>A machine that answers a healthy drill, in order.</summary>
    /// <param name="checkExit">What the first <c>e2fsck -fn</c> exits with.</param>
    /// <param name="repairExit">What <c>e2fsck -fy</c> exits with.</param>
    /// <param name="afterExit">What the second <c>e2fsck -fn</c> exits with.</param>
    private static FakeWsl Machine(int checkExit = 4, int repairExit = 1, int afterExit = 0) =>
        new FakeWsl()
            // DD228 opens every import by taking the name back, and here it was free.
            .Answer(0)                      // --terminate any leftover of the drill's name
            .Answer(1, "no distribution with that name")
            .Answer(0)                      // --import the drill
            .Answer(0, "/sbin/e2fsck\n/sbin/debugfs")
            .Answer(0)                      // dd + mke2fs + a clean first read
            .Answer(0, "Filesystem state: not clean")
            .Answer(checkExit, "Inode 11 ref count is 7, should be 2.  Fix? no")
            .Answer(repairExit, "Inode 11 ref count is 7, should be 2.  Fix? yes")
            .Answer(afterExit, "12/8192 files (0.0% non-contiguous)")
            .Answer(0)                      // --terminate the drill
            .Answer(0);                     // --unregister it

    [Fact]
    public void The_drill_never_touches_the_engine_or_its_distribution()
    {
        // The rule this whole task is built on: the disk is a scratch image, and nothing here is
        // allowed to terminate, unregister or check anything the user owns. Asserted over the whole
        // sequence rather than at one call, because the next call added is the one nobody checks.
        var paths = Paths();
        var wsl = Machine();

        new RepairDrill(wsl, paths).Run(@"C:\downloads\rootfs.tar.gz");

        Assert.DoesNotContain(
            wsl.Invocations,
            argv => argv.Contains(paths.DistributionName, StringComparer.Ordinal));
        Assert.All(
            wsl.Invocations.Where(argv => argv.Length > 1
                && argv[0] is "--terminate" or "--unregister" or "--import"),
            argv => Assert.Equal(RepairDrill.DrillName, argv[1]));
    }

    [Fact]
    public void The_drill_has_a_distribution_of_its_own_and_not_the_rescue_the_check_uses()
    {
        // Two people a second apart is enough: a drill and a real check sharing one distribution
        // would have either teardown take the other's tools away mid-run.
        Assert.NotEqual(FilesystemRepair.RescueName, RepairDrill.DrillName);
    }

    [Fact]
    public void The_image_is_made_clean_and_then_broken_on_purpose()
    {
        // In that order, because an image that was never verified clean cannot say afterwards
        // whether the damage or the mke2fs is what the check is reporting.
        var wsl = Machine();

        new RepairDrill(wsl, Paths()).Run(@"C:\downloads\rootfs.tar.gz");

        var made = wsl.Invocations.FindIndex(
            argv => argv.Any(word => word.Contains("mke2fs", StringComparison.Ordinal)));
        var broken = wsl.Invocations.FindIndex(
            argv => argv.Any(word => word.Contains("debugfs", StringComparison.Ordinal)
                && word.Contains("links_count", StringComparison.Ordinal)));

        Assert.True(made >= 0, "no filesystem was made");
        Assert.True(broken >= 0, "nothing broke the reference count, so -fn would exit 0");
        Assert.True(made < broken, "the image was damaged before it was made");
    }

    [Fact]
    public void The_damage_breaks_a_reference_count_and_not_only_the_free_counters()
    {
        // Measured, and the reason this assertion exists at all. Breaking the superblock free
        // counters alone left `e2fsck -fn` printing "Free blocks count wrong" and exiting zero,
        // because those counts are recomputed rather than trusted — so the check found nothing, the
        // Repair button was never offered, and the drill rehearsed the path it exists to walk right
        // up to the point where the walking starts.
        var wsl = Machine();

        new RepairDrill(wsl, Paths()).Run(@"C:\downloads\rootfs.tar.gz");

        Assert.Contains(
            wsl.Invocations,
            argv => argv.Any(word => word.Contains("sif <11> links_count", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_check_answers_no_a_repair_answers_yes_and_the_disk_is_read_a_third_time()
    {
        // Three readings, and the third is what makes the rehearsal worth anything: a repair that
        // reported success on a disk still carrying errors is exactly the lie this drill is for.
        var wsl = Machine();

        var outcome = new RepairDrill(wsl, Paths()).Run(@"C:\downloads\rootfs.tar.gz");

        var reads = wsl.Invocations
            .Where(argv => argv.Any(word => word.Contains("e2fsck -f", StringComparison.Ordinal)))
            .Select(argv => argv.First(word => word.Contains("e2fsck -f", StringComparison.Ordinal)))
            .ToList();

        // The first is the one mke2fs is verified with, so four in all.
        Assert.Equal(4, reads.Count);
        Assert.Equal(3, reads.Count(read => read.Contains("-fn", StringComparison.Ordinal)));
        Assert.Single(reads, read => read.Contains("-fy", StringComparison.Ordinal));
        Assert.True(outcome.Rehearsed, outcome.Failure?.Detail ?? "the readings did not line up");
    }

    [Fact]
    public void A_drill_whose_damage_never_landed_is_a_failure_and_not_a_success()
    {
        // The one outcome that must not be mistakable for a pass. A clean disk read three times
        // looks identical to a healthy run in every step, and reports having rehearsed nothing.
        var wsl = Machine(checkExit: 0, repairExit: 0, afterExit: 0);

        var outcome = new RepairDrill(wsl, Paths()).Run(@"C:\downloads\rootfs.tar.gz");

        Assert.True(outcome.Succeeded, outcome.Failure?.Detail);
        Assert.False(outcome.Rehearsed, "a drill that found nothing reported having rehearsed the "
            + "write path, which is the failure that looks most like a pass");
    }

    [Fact]
    public void A_repair_that_left_the_disk_dirty_is_not_a_rehearsal_either()
    {
        // The other half of the same guard: the tool wrote, and the second reading says it did not
        // finish the job.
        var wsl = Machine(checkExit: 4, repairExit: 1, afterExit: 4);

        var outcome = new RepairDrill(wsl, Paths()).Run(@"C:\downloads\rootfs.tar.gz");

        Assert.False(outcome.Rehearsed);
        Assert.False(outcome.After!.Clean);
    }

    [Fact]
    public void The_drill_is_terminated_before_it_is_unregistered_even_where_the_run_failed()
    {
        // DD209, and the worst thing this project has done to a machine: unregistering a running
        // distribution is accepted, moves it to state 4 and blocks the WSL service on something
        // that never stops. Every distribution on the machine queues behind that.
        var wsl = new FakeWsl()
            .Answer(0)                                  // --terminate any leftover
            .Answer(1, "no distribution with that name") // --unregister it: none
            .Answer(0)                                  // --import
            .Answer(1, "apk: could not reach a mirror"); // the tools never arrived

        new RepairDrill(wsl, Paths()).Run(@"C:\downloads\rootfs.tar.gz");

        var terminated = wsl.Invocations.FindIndex(
            argv => argv.Length > 1 && argv[0] == "--terminate");
        var unregistered = wsl.Invocations.FindIndex(
            argv => argv.Length > 1 && argv[0] == "--unregister");

        Assert.True(terminated >= 0, "the drill was unregistered without being terminated first");
        Assert.True(unregistered >= 0, "the drill was left registered after a failed run");
        Assert.True(terminated < unregistered, "the terminate is too late to stop what the "
            + "unregister blocks on");
    }

    [Fact]
    public void Both_surfaces_read_an_exit_code_through_the_same_reading()
    {
        // DD215's refactor, and the reason for it: a drill with its own idea of what 4 means would
        // agree with itself and with nothing that ships.
        var found = FsckReading.Of(new WslResult(4, "Inode 11 ref count is 7", null), write: false);
        Assert.True(found.Step.Ok);
        Assert.False(found.Clean);
        Assert.Contains("a repair would mend them", found.Step.Detail, StringComparison.Ordinal);

        // 1 and 2 are both the repair working: errors corrected, and corrected with a reboot wanted.
        foreach (var corrected in new[] { 1, 2 })
        {
            var mended = FsckReading.Of(new WslResult(corrected, "Fix? yes", null), write: true);
            Assert.True(mended.Step.Ok);
            Assert.False(mended.Clean);
        }

        // And a code neither mode has a meaning for is a failed run rather than a finding.
        var broken = FsckReading.Of(new WslResult(8, "", null), write: false);
        Assert.False(broken.Step.Ok);
        Assert.Contains("exited 8", broken.Step.Detail, StringComparison.Ordinal);
    }
}
