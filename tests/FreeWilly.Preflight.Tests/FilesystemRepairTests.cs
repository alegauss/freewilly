using FreeWilly.Core.Engine;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>A hold that records whether it was open, without starting anything.</summary>
/// <remarks>
/// The one seam here that is not a <c>wsl.exe</c> call, and the one the measurement proved cannot be
/// skipped: the disk stays attached only while the virtual machine does, and the machine goes down
/// as soon as no distribution has a running process in it.
/// </remarks>
internal sealed class FakeHold : IDisposable
{
    /// <summary>Which distribution was asked to hold the machine, or null.</summary>
    internal string? Distribution { get; private set; }

    /// <summary>How many wsl calls had been made when the hold opened.</summary>
    internal int OpenedAfter { get; private set; } = -1;

    /// <summary>How many had been made when it closed.</summary>
    internal int ClosedAfter { get; private set; } = -1;

    /// <summary>The seam handed to the repair.</summary>
    /// <param name="wsl">The machine, so the hold can note where in the sequence it sits.</param>
    /// <returns>The factory.</returns>
    internal static Func<string, IDisposable> Over(FakeWsl wsl, out FakeHold hold)
    {
        var made = new FakeHold();
        hold = made;
        return distribution =>
        {
            made.Distribution = distribution;
            made.OpenedAfter = wsl.Invocations.Count;
            made._wsl = wsl;
            return made;
        };
    }

    private FakeWsl? _wsl;

    /// <inheritdoc/>
    public void Dispose() => ClosedAfter = _wsl?.Invocations.Count ?? -1;
}

/// <summary>
/// The check and the repair, and the sequence that makes the disk reachable at all (DD199).
/// </summary>
public sealed class FilesystemRepairTests
{
    private const string Uuid = "9cb04147-49e5-4515-8e3c-0ecdca70eb3f";

    private static EnginePaths Paths() =>
        new(Path.Combine(Path.GetTempPath(), $"fw-{Guid.NewGuid():N}"));

    /// <summary>Answer the sequence a healthy run makes, in order.</summary>
    /// <param name="fsckExit">What e2fsck exits with.</param>
    /// <param name="fsckSaid">What it prints.</param>
    /// <returns>The machine.</returns>
    /// <summary>
    /// What <c>blkid</c> printed inside the live distribution on 29 August 2026, verbatim.
    /// </summary>
    /// <remarks>
    /// Five devices, two of them with no UUID at all and one of them a swap partition, because that
    /// is what a WSL2 virtual machine holds and a listing tidied to one line would prove nothing
    /// about picking the right row out of it.
    /// </remarks>
    private const string Listing =
        "/dev/sde: UUID=\"f20b734b-5cee-45dd-93cc-accea19eb41f\" TYPE=\"ext4\"\n"
        + "/dev/sdd: UUID=\"" + Uuid + "\" TYPE=\"ext4\"\n"
        + "/dev/sdc: UUID=\"f0720437-ca47-455e-beea-3b2cc0ee2ec0\" TYPE=\"swap\"\n"
        + "/dev/sda: TYPE=\"ext4\"\n"
        + "/dev/sdb: TYPE=\"ext4\"\n";

    private static FakeWsl Machine(int fsckExit, string fsckSaid = "")
    {
        var wsl = new FakeWsl();
        wsl.Answer(0, $"/dev/sdd: UUID=\"{Uuid}\" TYPE=\"ext4\"\n") // the engine's root filesystem
            .Answer(0)                 // --import the rescue
            .Answer(0, "/sbin/e2fsck") // apk add && command -v
            .Answer(0)                 // --terminate the engine's distribution
            .Answer(0, Listing)        // blkid: the disk is still attached
            .Answer(fsckExit, fsckSaid)
            .Answer(0);                // --unregister the rescue
        return wsl;
    }

    [Fact]
    public void The_rescue_is_held_open_before_the_engine_is_terminated()
    {
        // Measured on 29 August 2026 and the reason this class is shaped the way it is: WSL leaves
        // the disk attached after a terminate, but only while the virtual machine is up, and the
        // machine goes down once no distribution has a running process. Holding after the terminate
        // would be holding nothing.
        var wsl = Machine(0);
        var repair = new FilesystemRepair(wsl, Paths(), FakeHold.Over(wsl, out var hold));

        repair.Check(@"C:\downloads\rootfs.tar.gz");

        var terminate = wsl.Invocations.FindIndex(
            argv => argv.Length > 0 && argv[0] == "--terminate");

        Assert.Equal(FilesystemRepair.RescueName, hold.Distribution);
        Assert.True(terminate >= 0, "the engine's distribution was never terminated");
        Assert.True(
            hold.OpenedAfter <= terminate,
            "the hold on the virtual machine opened after the terminate, so the disk it is there to "
            + "keep attached had already gone");
    }

    [Fact]
    public void The_disk_is_found_by_its_filesystem_and_never_by_a_device_name()
    {
        // /dev/sdX is assigned in attach order and moves between boots, so a repair that trusted one
        // would run against whichever distribution happened to be third that day.
        var wsl = Machine(0);
        var repair = new FilesystemRepair(wsl, Paths(), FakeHold.Over(wsl, out _));

        repair.Check(@"C:\downloads\rootfs.tar.gz");

        // The listing is read and the row picked here. `blkid -U` is what this looked like it should
        // ask, and BusyBox accepts that flag, exits zero and prints nothing (DD201).
        Assert.DoesNotContain(
            wsl.Invocations,
            argv => argv.Any(word => word.Contains("blkid -U", StringComparison.Ordinal)));
        Assert.Contains(
            wsl.Invocations,
            argv => argv.Any(word => word.EndsWith("blkid", StringComparison.Ordinal)));
    }

    [Fact]
    public void Nothing_asks_a_minirootfs_for_a_command_it_does_not_have()
    {
        // DD201. Every one of these is util-linux, the root filesystem is an Alpine minirootfs, and
        // the two that shipped made the verb refuse on every machine. Asserted over the whole
        // sequence rather than at one call, because the next one added is the one nobody checks.
        var wsl = Machine(0);
        new FilesystemRepair(wsl, Paths(), FakeHold.Over(wsl, out _))
            .Check(@"C:\downloads\rootfs.tar.gz");

        foreach (var absent in new[] { "findmnt", "lsblk", "dumpe2fs" })
        {
            Assert.DoesNotContain(
                wsl.Invocations,
                argv => argv.Any(word => word.Contains(absent, StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void The_root_filesystem_is_read_out_of_proc_mounts_and_not_asked_for()
    {
        // /proc/mounts is the kernel's, so it answers with no package installed — which every
        // distribution provisioned before DD196 is, including the one this was measured against.
        var wsl = Machine(0);
        new FilesystemRepair(wsl, Paths(), FakeHold.Over(wsl, out _))
            .Check(@"C:\downloads\rootfs.tar.gz");

        Assert.Contains(
            wsl.Invocations,
            argv => argv.Any(word => word.Contains("/proc/mounts", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_right_row_is_picked_out_of_a_listing_of_five_devices()
    {
        // The direction blkid -U would have answered. Two rows carry no UUID and one is a swap
        // partition, which is what the virtual machine actually holds.
        Assert.Equal("/dev/sdd", Minirootfs.DeviceIn(Listing, Uuid));

        // Case is not a difference between filesystems: the two ends of this comparison are printed
        // by two different distributions.
        Assert.Equal("/dev/sdd", Minirootfs.DeviceIn(Listing, Uuid.ToUpperInvariant()));

        // And a filesystem nothing carries is null rather than the first row that parsed.
        Assert.Null(Minirootfs.DeviceIn(Listing, "00000000-0000-0000-0000-000000000000"));
    }

    [Fact]
    public void A_device_with_no_filesystem_on_it_is_an_answer_rather_than_a_parse_failure()
    {
        // /dev/sda prints a line with no UUID at all. It is an unformatted disk, which is not the
        // one being asked about.
        Assert.Null(Minirootfs.UuidIn("/dev/sda: TYPE=\"ext4\""));
        Assert.Equal(Uuid, Minirootfs.UuidIn($"/dev/sdd: UUID=\"{Uuid}\" TYPE=\"ext4\""));
    }

    [Fact]
    public void A_check_answers_no_to_everything_and_a_repair_answers_yes()
    {
        // The asymmetry the design settles: reading cannot make a filesystem worse, and writing is
        // done to the disk holding every image and volume the user has.
        var reading = Machine(0);
        new FilesystemRepair(reading, Paths(), FakeHold.Over(reading, out _))
            .Check(@"C:\downloads\rootfs.tar.gz");

        var writing = Machine(1);
        new FilesystemRepair(writing, Paths(), FakeHold.Over(writing, out _))
            .Fix(@"C:\downloads\rootfs.tar.gz");

        Assert.Contains(
            reading.Invocations,
            argv => argv.Any(word => word.Contains("e2fsck -fn", StringComparison.Ordinal)));
        Assert.DoesNotContain(
            reading.Invocations,
            argv => argv.Any(word => word.Contains("e2fsck -fy", StringComparison.Ordinal)));
        Assert.Contains(
            writing.Invocations,
            argv => argv.Any(word => word.Contains("e2fsck -fy", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_clean_filesystem_is_reported_as_needing_nothing()
    {
        var wsl = Machine(0, "/dev/sdd: 905985/67108864 files (0.2% non-contiguous)");
        var repair = new FilesystemRepair(wsl, Paths(), FakeHold.Over(wsl, out _));

        var outcome = repair.Check(@"C:\downloads\rootfs.tar.gz");

        Assert.True(outcome.Succeeded, outcome.Failure?.Detail);
        Assert.True(outcome.Clean);

        // What e2fsck printed, kept rather than summarised: it is the thing somebody is deciding on.
        Assert.Contains("905985", outcome.Findings, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dirty_filesystem_read_only_is_a_finding_and_not_a_failure()
    {
        // e2fsck -fn exits 4 on a filesystem with errors it was not allowed to touch. Reading that
        // as a failure would report the check as broken on exactly the disks it exists for.
        var wsl = Machine(4, "Block bitmap differences: +(1--2)\n/dev/sdd: ********** WARNING");
        var repair = new FilesystemRepair(wsl, Paths(), FakeHold.Over(wsl, out _));

        var outcome = repair.Check(@"C:\downloads\rootfs.tar.gz");

        Assert.True(outcome.Succeeded, outcome.Failure?.Detail);
        Assert.False(outcome.Clean);
        Assert.Contains("Block bitmap differences", outcome.Findings, StringComparison.Ordinal);
    }

    [Fact]
    public void A_repair_that_corrected_errors_succeeded()
    {
        // 1 is "errors corrected" and 2 is "corrected, reboot wanted". Both are the repair working,
        // which is the ordinary outcome after an unclean shutdown.
        foreach (var exitCode in new[] { 1, 2 })
        {
            var wsl = Machine(exitCode, "Free blocks count wrong. Fix? yes");
            var repair = new FilesystemRepair(wsl, Paths(), FakeHold.Over(wsl, out _));

            var outcome = repair.Fix(@"C:\downloads\rootfs.tar.gz");

            Assert.True(outcome.Succeeded, $"e2fsck {exitCode}: {outcome.Failure?.Detail}");
            Assert.False(outcome.Clean);
        }
    }

    [Fact]
    public void The_rescue_is_unregistered_even_where_the_run_failed()
    {
        // A rescue left registered is this tool having put something in somebody's `wsl --list`
        // after telling them it would not.
        var wsl = new FakeWsl();
        wsl.Answer(0, $"/dev/sdd: UUID=\"{Uuid}\" TYPE=\"ext4\"\n") // the engine's root
            .Answer(0)                 // --import
            .Answer(0, "/sbin/e2fsck") // apk add
            .Answer(1, "there is no distribution named freewilly"); // --terminate fails

        var repair = new FilesystemRepair(wsl, Paths(), FakeHold.Over(wsl, out var hold));

        var outcome = repair.Check(@"C:\downloads\rootfs.tar.gz");

        Assert.False(outcome.Succeeded);
        Assert.Contains(
            wsl.Invocations,
            argv => argv.Length > 1 && argv[0] == "--unregister"
                && argv[1] == FilesystemRepair.RescueName);

        // And the hold is released, rather than pinning the machine up behind a failure.
        Assert.True(hold.ClosedAfter >= 0, "the hold on the virtual machine was never released");
    }

    [Fact]
    public void The_rescue_is_terminated_before_it_is_unregistered()
    {
        // DD209, and the worst thing this class has done to a machine. The hold's dispose kills the
        // Windows-side wsl.exe client and not the sleep inside the rescue, so WSL still counts it as
        // running — and it does not refuse an unregister of a running distribution. It accepts it,
        // moves it to state 4 and blocks the service on something that never stops. Every other
        // distribution queues behind that, so the engine's own start began exiting 1 without a word
        // and only an elevated service restart recovered it.
        var wsl = Machine(0);

        new FilesystemRepair(wsl, Paths(), FakeHold.Over(wsl, out _))
            .Check(@"C:\downloads\rootfs.tar.gz");

        var terminated = wsl.Invocations.FindIndex(
            argv => argv.Length > 1 && argv[0] == "--terminate" && argv[1] == FilesystemRepair.RescueName);
        var unregistered = wsl.Invocations.FindIndex(
            argv => argv.Length > 1 && argv[0] == "--unregister" && argv[1] == FilesystemRepair.RescueName);

        Assert.True(terminated >= 0, "the rescue is unregistered without being terminated first, "
            + "which is the call that wedges the WSL service");
        Assert.True(unregistered >= 0, "the rescue was never unregistered");
        Assert.True(
            terminated < unregistered,
            "the rescue is terminated after the unregister, which is too late to be the thing that "
            + "stops the process the unregister blocks on");
    }

    [Fact]
    public void The_rescue_is_terminated_even_where_the_run_failed()
    {
        // The failing path is the one that leaves a machine wedged overnight: a check that stopped
        // early still has a rescue up with a hold in it, and the teardown that runs anyway must be
        // the safe teardown rather than the short one.
        var wsl = new FakeWsl();
        wsl.Answer(0, $"/dev/sdd: UUID=\"{Uuid}\" TYPE=\"ext4\"\n") // the engine's root
            .Answer(0)                 // --import
            .Answer(0, "/sbin/e2fsck") // apk add
            .Answer(1, "there is no distribution named freewilly"); // --terminate the engine fails

        new FilesystemRepair(wsl, Paths(), FakeHold.Over(wsl, out _))
            .Check(@"C:\downloads\rootfs.tar.gz");

        Assert.Contains(
            wsl.Invocations,
            argv => argv.Length > 1 && argv[0] == "--terminate"
                && argv[1] == FilesystemRepair.RescueName);
    }

    [Fact]
    public void A_distribution_that_cannot_say_what_its_root_is_stops_before_anything_is_imported()
    {
        // After the terminate nothing is left that knows which attached disk was the engine's, so a
        // run that could not read the UUID first would be a repair looking for a disk it cannot
        // identify — with the engine already down for it.
        var wsl = new FakeWsl();
        wsl.Answer(1, "");
        var repair = new FilesystemRepair(wsl, Paths(), FakeHold.Over(wsl, out var hold));

        var outcome = repair.Check(@"C:\downloads\rootfs.tar.gz");

        Assert.False(outcome.Succeeded);
        Assert.Contains("by hand", outcome.Failure?.Detail, StringComparison.Ordinal);
        Assert.Null(hold.Distribution);
        Assert.DoesNotContain(wsl.Invocations, argv => argv.Length > 0 && argv[0] == "--import");
        Assert.DoesNotContain(wsl.Invocations, argv => argv.Length > 0 && argv[0] == "--terminate");
    }

    [Fact]
    public void A_disk_the_terminate_took_away_is_named_rather_than_guessed_at()
    {
        // The measured behaviour not holding is the one outcome this whole mechanism rests on, so it
        // has to be reported as itself rather than as e2fsck failing on some other device.
        var wsl = new FakeWsl();
        wsl.Answer(0, $"/dev/sdd: UUID=\"{Uuid}\" TYPE=\"ext4\"\n")
            .Answer(0)
            .Answer(0, "/sbin/e2fsck")
            .Answer(0)
            // The listing came back without the engine's disk in it, which is the measured
            // behaviour not holding: the terminate took the disk off the virtual machine.
            .Answer(0, "/dev/sde: UUID=\"f20b734b-5cee-45dd-93cc-accea19eb41f\" TYPE=\"ext4\"\n");

        var repair = new FilesystemRepair(wsl, Paths(), FakeHold.Over(wsl, out _));

        var outcome = repair.Check(@"C:\downloads\rootfs.tar.gz");

        Assert.False(outcome.Succeeded);
        Assert.Contains("no attached disk carries", outcome.Failure?.Detail, StringComparison.Ordinal);
    }
}
