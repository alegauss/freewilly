using FreeWilly.Core.Engine;
using FreeWilly.Core.Fixtures;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// The readings the Engine page shows, and the one reading two callers share (DD197).
/// </summary>
public sealed class MachineReportTests
{
    /// <summary>
    /// What the live distribution answered on 29 August 2026, field for field.
    /// </summary>
    /// <remarks>
    /// Captured rather than invented, including the two fields that come back present and empty:
    /// <c>first</c> and <c>last</c> are files that exist on a healthy filesystem and hold nothing,
    /// which is a case a tidied fixture would not have.
    /// </remarks>
    private const string Live =
        "device=/dev/sdd\n"
        + "options=rw,relatime,discard,errors=remount-ro,data=ordered\n"
        + "errors=0\n"
        + "first=\n"
        + "last=\n"
        + "blocks=1055762868\n"
        + "used=59264744\n";

    [Fact]
    public void The_live_reading_is_read_field_for_field()
    {
        var state = DistributionState.Of(Live);

        Assert.NotNull(state);
        Assert.Equal("/dev/sdd", state.Device);
        Assert.Equal(0, state.Errors);
        Assert.Equal(59264744, state.UsedKb);
        Assert.Equal(1055762868, state.BlocksKb);

        // Present and empty is not a function name. A healthy filesystem has never recorded one.
        Assert.Null(state.FirstError);
        Assert.Null(state.LastError);
    }

    [Fact]
    public void A_healthy_mount_is_writable_despite_saying_remount_ro()
    {
        // `errors=remount-ro` says what the kernel would do if there were an error, not that there
        // was one. Every healthy ext4 mount carries it.
        var state = DistributionState.Of(Live);

        Assert.NotNull(state);
        Assert.True(state.Writable);
        Assert.False(state.Faulted);
        Assert.Contains("errors=remount-ro", state.Options, StringComparison.Ordinal);
    }

    [Fact]
    public void A_root_the_kernel_remounted_is_neither_writable_nor_well()
    {
        var state = DistributionState.Of(
            "device=/dev/sdd\noptions=ro,relatime,errors=remount-ro\nerrors=0\n");

        Assert.NotNull(state);
        Assert.False(state.Writable);
        Assert.True(state.Faulted);
    }

    [Fact]
    public void A_reading_that_named_no_device_is_nothing_rather_than_an_empty_one()
    {
        // The distribution answered, and answered nothing useful. A state with an empty device would
        // be a page reporting a filesystem it never reached.
        Assert.Null(DistributionState.Of("errors=0\n"));
        Assert.Null(DistributionState.Of(""));
    }

    [Fact]
    public void An_unreadable_counter_is_absent_rather_than_zero()
    {
        // The script says `unknown` where the file is not there, and zero errors is a very different
        // claim from not having been able to look.
        var state = DistributionState.Of("device=/dev/sdd\noptions=rw\nerrors=unknown\n");

        Assert.NotNull(state);
        Assert.Null(state.Errors);
        Assert.False(state.Faulted);
    }

    // ---- what the panel hands over ------------------------------------------------------------

    [Fact]
    public async Task The_report_answers_the_six_questions_the_diagnosis_took_by_hand()
    {
        // WSL and the distribution, the filesystem, its errors, the two sizes, and the engine.
        var groups = (await new SampleMachineReport().ReadAsync()).Groups;

        Assert.Equal(
            ["WSL", "Filesystem", "Errors", "Disk", "Engine"],
            groups.Select(group => group.Title));

        // The two sizes as a pair, which is what a question about a full disk actually needs: a
        // sparse file that has grown and a filesystem holding data are different facts.
        var disk = groups.Single(group => group.Title == "Disk");
        Assert.Contains(disk.Readings, r => r.Name == "virtual disk");
        Assert.Contains(disk.Readings, r => r.Name == "used inside");
        Assert.Contains(disk.Readings, r => r.Name == "free on Windows");
    }

    [Fact]
    public async Task The_fixture_answers_without_yielding_so_a_capture_catches_the_readings()
    {
        // A fixture that went through the thread pool would make the picture depend on how busy the
        // machine drawing it was, which is the one property the capture verb exists for.
        var reading = new SampleMachineReport().ReadAsync();

        Assert.True(reading.IsCompleted);
        Assert.NotEmpty((await reading).Groups);
    }

    [Fact]
    public async Task The_verdict_travels_with_the_readings_it_was_made_of()
    {
        // DD198. The window and `read health` print the same sentence, and neither derives it from
        // the other's rendered strings: two answers to one question are one answer that goes stale.
        var health = await new SampleMachineReport().ReadAsync();

        Assert.True(health.Well);
        Assert.NotEmpty(health.Summary);
        Assert.Equal(5, health.Groups.Count);
    }

    [Fact]
    public async Task The_copy_hands_over_every_reading_the_page_shows()
    {
        // The point of the panel is handing what it says to somebody else, so the text is the
        // deliverable rather than a convenience on top of one.
        var groups = (await new SampleMachineReport().ReadAsync()).Groups;

        var text = MachineReport.AsText(groups);

        foreach (var reading in groups.SelectMany(group => group.Readings))
        {
            Assert.Contains(reading.Name, text, StringComparison.Ordinal);
            Assert.Contains(reading.Value, text, StringComparison.Ordinal);
        }

        foreach (var group in groups)
        {
            Assert.Contains(group.Title, text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(62636687360, "58.3 GB")]
    [InlineData(59264744L * 1024, "56.5 GB")]
    public void A_size_reads_the_way_every_other_tool_prints_one(long bytes, string expected) =>
        Assert.Equal(expected, MachineReport.Size(bytes));
}
