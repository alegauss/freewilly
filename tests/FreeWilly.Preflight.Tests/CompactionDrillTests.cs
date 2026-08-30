using FreeWilly.Core.Engine;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// The rehearsal of the path that hands blocks back, and what it found (DD221).
/// </summary>
/// <remarks>
/// <para><b>It was run, and it failed on the first attempt for a reason nobody had.</b> On 29 August
/// 2026 it imported a scratch distribution, wrote and deleted 512 MB inside it, and drove the
/// shipped <see cref="DiskCompaction"/> against it. Every step landed until the last one, where WSL
/// refused: <c>sparse VHD support is currently disabled due to possible data corruption</c>, and the
/// only way past it is the <c>--allow-unsafe</c> flag DD211 deliberately refuses to pass.</para>
///
/// <para>So the Compact button cannot succeed on a current WSL, which no fake was ever going to say
/// and which the window would have told a user in a sentence claiming their WSL was too old. That
/// sentence is corrected here; the button itself is a task of its own.</para>
///
/// <para>What is asserted below is the part that is pure: that the rehearsal points every call at a
/// scratch distribution and never at the engine's, that it drives the shipped sequence rather than a
/// copy, and that a compaction reporting success over a disk that did not move is not a pass.</para>
/// </remarks>
public sealed class CompactionDrillTests
{
    private const string Rootfs = @"C:\downloads\rootfs.tar.gz";

    private static EnginePaths Install() =>
        new(Path.Combine(Path.GetTempPath(), $"fw-{Guid.NewGuid():N}"));

    [Fact]
    public void Every_call_names_the_scratch_machine_and_never_the_engine()
    {
        // The one thing this class must not get wrong. The compaction terminates whatever its paths
        // say the distribution is, so a rehearsal handed the wrong paths would take somebody's
        // engine down and convert their virtual disk.
        var install = Install();
        var wsl = new FakeWsl();

        new CompactionDrill(wsl, install).Run(Rootfs);

        Assert.DoesNotContain(
            wsl.Invocations,
            argv => argv.Contains(install.DistributionName, StringComparer.Ordinal));
        Assert.All(
            wsl.Invocations.Where(argv => argv.Length > 1
                && argv[0] is "--terminate" or "--unregister" or "--import" or "--manage"),
            argv => Assert.Equal(CompactionDrill.DrillName, argv[1]));
    }

    [Fact]
    public void The_scratch_machine_has_a_name_of_its_own()
    {
        // Three temporary distributions now, and each of them is torn down by whatever created it.
        // Two sharing a name would have either teardown take the other out mid-run.
        Assert.NotEqual(FilesystemRepair.RescueName, CompactionDrill.DrillName);
        Assert.NotEqual(RepairDrill.DrillName, CompactionDrill.DrillName);
        Assert.NotEqual(EnginePaths.CurrentDistribution, CompactionDrill.DrillName);
    }

    [Fact]
    public void The_scratch_disk_is_where_the_compaction_will_look_for_it()
    {
        // The compaction reads `<root>\distro\ext4.vhdx`, so the scratch distribution has to be
        // imported into a root whose distro directory is its own. Getting this wrong would measure a
        // file that is not there and report every reading as unread.
        var install = Install();
        var drill = new CompactionDrill(new FakeWsl(), install);

        Assert.Equal(
            new DiskCompaction(
                new FakeWsl(),
                drill.Scratch,
                () => new RepairStep("x", true, "x"),
                () => new RepairStep("y", true, "y")).VirtualDiskPath,
            Path.Combine(drill.Scratch.Distribution, "ext4.vhdx"));

        Assert.StartsWith(install.Root, drill.Scratch.Root, StringComparison.Ordinal);
        Assert.Equal(CompactionDrill.DrillName, drill.Scratch.DistributionName);
    }

    [Fact]
    public void The_disk_is_grown_and_emptied_before_anything_is_measured()
    {
        // A virtual disk that never held anything has nothing to hand back, so a compaction against
        // one reports success and no bytes — which is indistinguishable from the mechanism not
        // working, and is the answer this rehearsal exists to be unable to give.
        var wsl = new FakeWsl();

        new CompactionDrill(wsl, Install()).Run(Rootfs);

        var filled = wsl.Invocations.FindIndex(
            argv => argv.Any(word => word.Contains("of=/fill", StringComparison.Ordinal)));
        var emptied = wsl.Invocations.FindIndex(
            argv => argv.Any(word => word.Contains("rm -f /fill", StringComparison.Ordinal)));
        var compacted = wsl.Invocations.FindIndex(argv => argv.Contains("--set-sparse"));

        Assert.True(filled >= 0, "nothing was written into the scratch disk");
        Assert.Equal(filled, emptied);
        Assert.True(filled < compacted, "the compaction ran before there was anything to reclaim");
    }

    [Fact]
    public void It_drives_the_shipped_sequence_and_never_reaches_for_the_unsafe_flag()
    {
        // The constraint the task states: a rehearsal with its own idea of the order or the flags
        // would rehearse something that ships nowhere. And --allow-unsafe is the flag WSL names in
        // its own refusal, which is exactly why nothing here may quietly start passing it.
        var wsl = new FakeWsl();

        new CompactionDrill(wsl, Install()).Run(Rootfs);

        var trimmed = wsl.Invocations.FindIndex(
            argv => argv.Any(word => word.Contains("fstrim", StringComparison.Ordinal)));
        var handed = wsl.Invocations.FindIndex(argv => argv.Contains("--set-sparse"));

        // The last terminate before the hand-back, not the first: this rehearsal takes the scratch
        // machine down once of its own accord, to measure a disk nothing is still writing to, and
        // the compaction's own terminate is the one the hand-back depends on.
        var terminated = wsl.Invocations.FindLastIndex(
            handed - 1,
            argv => argv.Length > 1 && argv[0] == "--terminate"
                && argv[1] == CompactionDrill.DrillName);

        Assert.True(trimmed >= 0 && handed >= 0, "the shipped sequence did not run");
        Assert.True(terminated >= 0, "the disk was handed back without being taken down");
        Assert.True(trimmed < terminated, "the trim ran against a distribution already down");
        Assert.True(terminated < handed, "the disk was handed back while it was still in use");
        Assert.DoesNotContain(
            wsl.Invocations, argv => argv.Contains("--allow-unsafe", StringComparer.Ordinal));
    }

    [Fact]
    public void The_elevated_route_is_rehearsed_against_the_scratch_disk_and_no_other()
    {
        // DD247. The route that shipped broken twice is the one that had no rehearsal, and both
        // times it was found by somebody pressing a button on the disk holding every image they
        // own. What the drill substitutes is only the machine.
        var install = Install();
        var drill = new CompactionDrill(new FakeWsl(), install);
        var elevated = new FakeElevation();

        drill.Run(Rootfs, elevated: elevated);

        Assert.Equal(1, elevated.Asked);

        // Asserted over the script rather than the command line, because the command line carries
        // the script's path and the log's, and the disk being compacted is named inside the script.
        // That is the one thing this class must never get wrong: the compaction acts on whatever
        // its EnginePaths names, and the engine's is one directory away from the scratch one.
        var script = File.ReadAllText(Path.Combine(drill.Scratch.Root, "compact.diskpart"));

        Assert.Contains(
            Path.Combine(drill.Scratch.Distribution, "ext4.vhdx"),
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            Path.Combine(install.Distribution, "ext4.vhdx"), script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_plain_rehearsal_raises_no_prompt_at_all()
    {
        // The flag is what asks for the elevated route, and a drill somebody ran to watch a
        // hand-back must not put a UAC prompt on their screen.
        var elevated = new FakeElevation();

        new CompactionDrill(new FakeWsl(), Install()).Run(Rootfs);

        Assert.Equal(0, elevated.Asked);
    }

    /// <summary>An elevation that records what it was asked and says it worked.</summary>
    private sealed class FakeElevation : IElevated
    {
        internal string? Arguments { get; private set; }

        internal int Asked { get; private set; }

        public ElevatedRun Run(string fileName, string arguments)
        {
            Arguments = arguments;
            Asked++;
            return new ElevatedRun(Ran: true, ExitCode: 0);
        }
    }

    [Fact]
    public void The_scratch_machine_is_put_away_and_never_kept()
    {
        // Half a gigabyte of deliberately wasted disk. It is of no use to anything after the
        // reading, so unlike the rescue it is never exported.
        var wsl = new FakeWsl();

        new CompactionDrill(wsl, Install()).Run(Rootfs);

        var terminated = wsl.Invocations.FindIndex(
            argv => argv.Length > 1 && argv[0] == "--terminate");
        var unregistered = wsl.Invocations.FindIndex(
            argv => argv.Length > 1 && argv[0] == "--unregister");

        Assert.True(unregistered >= 0, "the scratch machine was left registered");
        Assert.True(terminated < unregistered, "it was unregistered while still running (DD209)");
        Assert.DoesNotContain(wsl.Invocations, argv => argv.Length > 0 && argv[0] == "--export");
    }

    [Fact]
    public void A_compaction_that_reported_success_over_a_disk_that_did_not_move_is_not_a_pass()
    {
        // The claim nobody had ever checked, and the one outcome that must not read as a success.
        var still = new CompactionDrillOutcome([new RepairStep("read it again", true, "76 MB")])
        {
            Before = new VirtualDiskSize(80_000_000, 80_000_000),
            After = new VirtualDiskSize(80_000_000, 80_000_000),
            Compaction = new CompactionOutcome(
                [new RepairStep(DiskCompaction.HandBackStep, true, "it is sparse now")]),
        };

        Assert.Null(still.Reclaimed);
        Assert.False(still.Rehearsed);

        // And one where the volume really gave the space back is.
        var moved = still with { After = new VirtualDiskSize(80_000_000, 20_000_000) };
        Assert.Equal(60_000_000, moved.Reclaimed);
        Assert.True(moved.Rehearsed);
    }

    [Fact]
    public void The_length_of_a_sparse_file_is_not_what_the_volume_is_charging_for()
    {
        // Why there are two readings. A virtual disk handed back to Windows keeps its length while
        // NTFS stops charging for the ranges nothing wrote to, so a rehearsal watching the length
        // alone would report the mechanism as having done nothing.
        var file = Path.Combine(Path.GetTempPath(), $"fw-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(file, new byte[64 * 1024]);

            var onDisk = FileOnDisk.Bytes(file);

            Assert.NotNull(onDisk);
            Assert.True(
                onDisk >= 64 * 1024,
                $"an ordinary file is charged for at least its length, and this said {onDisk}");
        }
        finally
        {
            File.Delete(file);
        }

        // A file that is not there is unread rather than zero: zero would read as a disk that has
        // given everything back.
        Assert.Null(FileOnDisk.Bytes(file));
    }
}
