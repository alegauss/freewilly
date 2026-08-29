using FreeWilly.Core.Builds;
using FreeWilly.Core.Fixtures;
using FreeWilly.Tray.Ui;
using FreeWilly.Tray.Ui.Pages;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// The build list's own shaping, and what the detail pane lists (DD126).
/// </summary>
public class BuildRowTests
{
    private static BuildRow Row(
        string name, string id, string status, int minute, double? seconds = 1) =>
        new(
            name,
            $"default/default/{id}",
            status,
            new DateTimeOffset(2026, 3, 14, 9, minute, 0, TimeSpan.Zero),
            seconds is { } taken ? TimeSpan.FromSeconds(taken) : null,
            TotalSteps: 5,
            CachedSteps: 0);

    private static readonly IReadOnlyList<BuildRow> Rows =
    [
        Row("api", "aaa", "Completed", 10, 4),
        Row("worker", "bbb", "Error", 30, 120),
        Row("site", "ccc", "Running", 50, null),
    ];

    [Fact]
    public void The_list_opens_most_recent_first()
    {
        // The only order that answers the question the page is opened with — and the one that puts
        // the build a printed link just named at the top.
        var shown = BuildRow.Shaped(Rows, new ListShape(BuildRow.DefaultColumn, Descending: true));

        Assert.Equal(["site", "worker", "api"], shown.Select(row => row.Name));
    }

    [Fact]
    public void A_time_and_a_duration_start_biggest_first_and_a_name_does_not()
    {
        // Sorting by WHEN and getting the oldest build first is the sort nobody wanted.
        Assert.True(BuildRow.DescendsFirst(BuildRow.Columns.When));
        Assert.True(BuildRow.DescendsFirst(BuildRow.Columns.Duration));
        Assert.False(BuildRow.DescendsFirst(BuildRow.Columns.Name));
        Assert.False(BuildRow.DescendsFirst(BuildRow.Columns.Status));
    }

    [Fact]
    public void A_running_build_sorts_by_duration_without_being_read_as_the_longest()
    {
        // It has no duration. Treating null as "unknown, therefore huge" would park whatever is
        // building at the top of a column about how long things took.
        var shown = BuildRow.Shaped(Rows, new ListShape(BuildRow.Columns.Duration, Descending: true));

        Assert.Equal("worker", shown[0].Name);
    }

    [Fact]
    public void The_filter_matches_the_id_so_a_pasted_ref_finds_its_row()
    {
        var shown = BuildRow.Shaped(Rows, new ListShape(BuildRow.DefaultColumn, true, "bbb"));

        Assert.Equal("worker", Assert.Single(shown).Name);
    }

    [Fact]
    public void The_filter_matches_the_name_and_the_status_too()
    {
        Assert.Single(BuildRow.Shaped(Rows, new ListShape(BuildRow.DefaultColumn, true, "work")));
        Assert.Single(BuildRow.Shaped(Rows, new ListShape(BuildRow.DefaultColumn, true, "error")));
    }

    [Fact]
    public void Two_builds_of_the_same_thing_in_the_same_minute_keep_one_fixed_order()
    {
        // A list that reshuffled between refreshes is unreadable, and the page redraws on its own.
        var same = new[] { Row("api", "zzz", "Completed", 10), Row("api", "aaa", "Completed", 10) };
        var shape = new ListShape(BuildRow.DefaultColumn, Descending: true);

        Assert.Equal(
            BuildRow.Shaped(same, shape).Select(row => row.Id),
            BuildRow.Shaped(same.Reverse(), shape).Select(row => row.Id));
    }

    [Theory]
    [InlineData("Completed", RowTone.Good)]
    [InlineData("Running", RowTone.Warn)]
    [InlineData("Error", RowTone.Bad)]
    [InlineData("Canceled", RowTone.Bad)]
    [InlineData("completed", RowTone.Good)]
    public void The_chip_carries_the_daemons_own_word_in_the_matching_tone(string status, RowTone tone) =>
        Assert.Equal(tone, Row("x", "aaa", status, 10).Tone);

    [Fact]
    public void A_status_this_does_not_know_is_not_evidence_that_it_worked()
    {
        // Muted and never Good. Colouring an unrecognised word green would be this window asserting
        // something upstream never said.
        Assert.Equal(RowTone.Muted, Row("x", "aaa", "SomethingNew", 10).Tone);
        Assert.Equal(RowTone.Muted, Row("x", "aaa", "", 10).Tone);
    }

    [Theory]
    [InlineData(0.43, "0.4s")]
    [InlineData(9.9, "9.9s")]
    [InlineData(41.7, "42s")]
    [InlineData(107.7, "1m 47s")]
    [InlineData(3720, "1h 2m")]
    public void A_duration_reads_the_way_a_build_log_prints_one(double seconds, string expected) =>
        Assert.Equal(expected, BuildRow.Human(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void The_when_column_has_no_clock_in_it()
    {
        // An age would be nicer to read and would make every window capture differ from the last,
        // which is the property DD38 exists for. Computed rather than typed since DD193: the literal
        // that used to be here was the fixture's own offset, which is zero, so it read as correct on
        // exactly one machine and asserted nothing about the conversion.
        var started = new DateTimeOffset(2026, 3, 14, 9, 10, 0, TimeSpan.Zero);

        Assert.Equal(
            started.ToLocalTime().ToString(
                "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            Row("x", "aaa", "Completed", 10).When);
    }

    [Fact]
    public void A_start_is_shown_against_the_clock_beside_the_window()
    {
        // DD193. buildx reports created_at in UTC, and rendering it in its own offset put a build
        // begun at 09:49 on the page as 12:49 for an operator three hours behind. A time is read
        // against the clock in the corner of the same screen, so it has to agree with that one.
        var started = new DateTimeOffset(2026, 3, 14, 12, 49, 0, TimeSpan.Zero);
        var row = new BuildRow(
            "api", "default/default/aaa", "Completed", started, TimeSpan.FromSeconds(1), 5, 0);

        Assert.Equal(started.ToLocalTime().Hour, int.Parse(
            row.When.AsSpan(11, 2), System.Globalization.CultureInfo.InvariantCulture));

        // And the pane the column defers to for the exact moment agrees with it, rather than
        // carrying a second spelling of the zone.
        Assert.StartsWith(row.When, BuildRow.Clock(started, "yyyy-MM-dd HH:mm:ss"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_capture_of_the_fixture_draws_the_same_digits_in_every_zone()
    {
        // DD194, and the literals are the assertion. A committed capture shows these digits, and the
        // property DD38 buys is that they do not move because somebody else drew the picture. The
        // anchor carries the drawing machine's own offset for its own wall clock, so converting it
        // back lands where it started whichever zone that machine is in.
        var shown = BuildRow.From(new SampleBuilds().Recent())
            .Select(row => row.When)
            .ToList();

        // 09:30 less the 1, 6, 22 and 74 minutes the fixture states.
        Assert.Equal(
            ["2026-03-14 09:29", "2026-03-14 09:24", "2026-03-14 09:08", "2026-03-14 08:16"],
            shown);
    }

    [Fact]
    public void The_detail_pane_dates_a_fixture_build_the_same_way_the_row_does()
    {
        // The field the column defers to for the exact moment, and it is in the same capture. A pane
        // that converted differently would be one more thing in the picture moving per machine.
        var history = new SampleBuilds();
        var newest = history.Inspect(history.Recent()[0].Reference)!;

        var started = BuildsPage.Fields(newest).Single(field => field.Name == "Started");

        Assert.Equal("2026-03-14 09:29:00", started.Value);
    }

    [Fact]
    public void The_detail_lists_a_field_only_where_the_record_carries_one()
    {
        // Absent, not blank. A caption over nothing reads as a value that failed to load rather than
        // as one that was never there — a build from a directory simply has no revision.
        var history = new SampleBuilds();
        var fromCheckout = history.Inspect(history.Recent()[0].Reference)!;
        var fromDirectory = history.Recent().Select(b => history.Inspect(b.Reference)!)
            .First(record => record.VcsRepository is null);

        Assert.Contains(BuildsPage.Fields(fromCheckout), field => field.Name == "Revision");
        Assert.DoesNotContain(BuildsPage.Fields(fromDirectory), field => field.Name == "Revision");
        Assert.DoesNotContain(BuildsPage.Fields(fromDirectory), field => field.Name == "Repository");
    }

    [Fact]
    public void The_detail_says_the_cache_was_refused_only_when_it_was()
    {
        // "No cache: false" is a row that says nothing.
        var history = new SampleBuilds();
        var records = history.Recent().Select(b => history.Inspect(b.Reference)!).ToList();

        var refused = records.First(record => record.Config!.NoCache);
        var used = records.First(record => !record.Config!.NoCache);

        Assert.Contains(BuildsPage.Fields(refused), field => field.Name == "Cache");
        Assert.DoesNotContain(BuildsPage.Fields(used), field => field.Name == "Cache");
    }

    [Fact]
    public void A_running_build_gets_no_duration_row_in_the_detail_either()
    {
        var history = new SampleBuilds();
        var running = history.Recent().Select(b => history.Inspect(b.Reference)!)
            .First(record => record.CompletedAt is null);

        Assert.DoesNotContain(BuildsPage.Fields(running), field => field.Name == "Duration");
        Assert.Contains(BuildsPage.Fields(running), field => field.Name == "Status");
    }
}
