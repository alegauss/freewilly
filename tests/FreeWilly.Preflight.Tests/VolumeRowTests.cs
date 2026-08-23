using FreeWilly.Core.Api;
using FreeWilly.Tray.Ui;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// The volumes list. Its job is not reclaiming space but making an irreversible act legible, so
/// most of what is tested here is what the window says before anything is deleted.
/// </summary>
public sealed class VolumeRowTests
{
    private const string Anonymous =
        "8f3a91c47b2e5d6a0c1f8e39b7d425a6cf90e18b34d7629af5c0813e6b24d7f0";

    private static VolumeSummary Volume(string name, long? size = null) =>
        new()
        {
            Name = name,
            UsageData = size is { } bytes ? new VolumeUsage { Size = bytes } : null,
        };

    private static ContainerSummary Mounting(string name, params string[] volumes) =>
        new()
        {
            Id = $"c-{name}",
            Names = [$"/{name}"],
            Mounts = [.. volumes.Select(v => new MountPoint { Type = "volume", Name = v })],
        };

    // ---- the join the daemon will not do --------------------------------------------------------

    [Fact]
    public void A_volume_a_container_mounts_names_that_container()
    {
        // The volume endpoint reports no holders at all, so this is the whole reason volumes get
        // their own task rather than a column on the images work.
        var rows = VolumeRow.From([Volume("shop_pgdata")], [Mounting("pgdata", "shop_pgdata")]);

        Assert.True(rows[0].IsMounted);
        Assert.Equal("pgdata", rows[0].Holders);
    }

    [Fact]
    public void A_volume_nothing_mounts_says_so_because_that_is_the_row_being_looked_for()
    {
        var rows = VolumeRow.From([Volume("shop_pgdata")], []);

        Assert.False(rows[0].IsMounted);
        Assert.Equal("nothing mounts it", rows[0].Holders);
    }

    [Fact]
    public void A_bind_mount_is_not_a_volume_and_does_not_hold_one()
    {
        // A bind is a directory on the host this list does not own and must never offer to delete.
        // The Name is filled in deliberately: a bind that happens to carry one must still be told
        // from a volume by its type, and an empty-name check alone would only look like it worked.
        var container = new ContainerSummary
        {
            Id = "c1",
            Names = ["/web"],
            Mounts = [new MountPoint { Type = "bind", Name = "shop_pgdata", Destination = "/srv" }],
        };

        var rows = VolumeRow.From([Volume("shop_pgdata")], [container]);

        Assert.False(rows[0].IsMounted);
    }

    [Fact]
    public void One_container_mounting_the_same_volume_twice_is_named_once()
    {
        var rows = VolumeRow.From(
            [Volume("data")], [Mounting("web", "data", "data")]);

        Assert.Equal("web", rows[0].Holders);
    }

    [Fact]
    public void A_stopped_container_still_holds_its_volume()
    {
        var container = new ContainerSummary
        {
            Id = "c1", Names = ["/pgdata"], State = "exited",
            Mounts = [new MountPoint { Type = "volume", Name = "shop_pgdata" }],
        };

        Assert.True(VolumeRow.From([Volume("shop_pgdata")], [container])[0].IsMounted);
    }

    // ---- the storage this list does not carry (DD138) --------------------------------------------

    private static ContainerSummary Binding(string name, params string[] destinations) =>
        new()
        {
            Id = $"c-{name}",
            Names = [$"/{name}"],
            Mounts = [.. destinations.Select(d => new MountPoint { Type = "bind", Destination = d })],
        };

    [Fact]
    public void A_machine_whose_storage_is_all_bind_mounts_is_told_so_rather_than_that_nothing_exists()
    {
        // The compose file this was filed for declares no volume at all, so the list is correctly
        // empty and "nothing has been created yet" reads as lost data.
        var binds = BindMounts.For([Binding("aem-author", "/opt/aem/repository", "/opt/aem/launchpad")]);

        Assert.True(binds.Any);
        Assert.Equal(2, binds.Count);
        Assert.Contains("2 bind mounts are in use", binds.Detail, StringComparison.Ordinal);
        Assert.Contains("held by aem-author", binds.Detail, StringComparison.Ordinal);
        Assert.Contains("nothing has gone missing", binds.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tab_with_no_bind_mounts_has_nothing_to_say_about_them()
    {
        Assert.False(BindMounts.For([Mounting("web", "data")]).Any);
        Assert.False(BindMounts.For([]).Any);
    }

    [Fact]
    public void Bind_mounts_are_counted_per_destination_and_not_per_container()
    {
        // The reader is being told how much storage this list is not showing, so the same host
        // folder mounted by two containers is two mounts.
        var binds = BindMounts.For([Binding("web", "/srv"), Binding("worker", "/srv")]);

        Assert.Equal(2, binds.Count);
        Assert.Equal(["web", "worker"], binds.Containers);
    }

    [Fact]
    public void One_bind_mount_reads_as_a_singular()
    {
        var binds = BindMounts.For([Binding("web", "/srv")]);

        Assert.Contains("1 bind mount is in use", binds.Detail, StringComparison.Ordinal);
        Assert.Contains("held by web", binds.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_container_mounting_both_kinds_is_counted_only_for_its_binds()
    {
        var container = new ContainerSummary
        {
            Id = "c1",
            Names = ["/web"],
            Mounts =
            [
                new MountPoint { Type = "volume", Name = "shop_pgdata" },
                new MountPoint { Type = "bind", Destination = "/app" },
            ],
        };

        Assert.Equal(1, BindMounts.For([container]).Count);
    }

    [Fact]
    public void A_stopped_container_still_holds_its_host_folder()
    {
        var container = Binding("web", "/srv") with { State = "exited" };

        Assert.Equal(1, BindMounts.For([container]).Count);
    }

    // ---- and the same count once the list has rows (DD139) ---------------------------------------

    [Fact]
    public void The_totals_line_says_a_list_with_rows_is_still_partial()
    {
        // One named volume anywhere and the empty state never renders again, which would put the
        // bind mounts back out of sight.
        var note = BindMounts.For([Binding("aem-author", "/opt/aem/repository", "/opt/aem/launchpad")]).Note;

        Assert.Contains("2 bind mounts", note, StringComparison.Ordinal);
        Assert.Contains("host folders not on this list", note, StringComparison.Ordinal);
    }

    [Fact]
    public void The_clause_reads_as_one_more_fact_beside_the_others()
    {
        // It joins "2.3 GB in volumes · 1 anonymous volume nothing mounts", so it carries the same
        // separator rather than opening a sentence of its own.
        Assert.StartsWith(" · ", BindMounts.For([Binding("web", "/srv")]).Note, StringComparison.Ordinal);
        Assert.Contains("1 bind mount, a host folder", BindMounts.For([Binding("web", "/srv")]).Note,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_tab_with_no_binds_adds_nothing_to_the_totals_line()
    {
        // A permanent "0 bind mounts" is noise on every machine that never had the problem.
        Assert.Equal("", BindMounts.For([Mounting("web", "data")]).Note);
    }

    [Fact]
    public void The_empty_state_and_the_totals_line_report_the_same_count()
    {
        // Two counts that could disagree is the defect this avoids: one BindMounts, two renderings.
        var binds = BindMounts.For([Binding("web", "/srv"), Binding("worker", "/srv", "/data")]);

        Assert.Contains("3 bind mounts are", binds.Detail, StringComparison.Ordinal);
        Assert.Contains("3 bind mounts,", binds.Note, StringComparison.Ordinal);
    }

    // ---- anonymous, and the compose convention --------------------------------------------------

    [Fact]
    public void A_sixty_four_character_hex_name_is_one_docker_gave_itself()
    {
        var rows = VolumeRow.From([Volume(Anonymous)], []);

        Assert.True(rows[0].IsAnonymous);
        Assert.Equal("anonymous, nothing mounts it", rows[0].Holders);
        Assert.Null(rows[0].Project);
    }

    [Fact]
    public void A_generated_name_is_drawn_short_and_kept_whole_in_the_tooltip()
    {
        // Twelve and an ellipsis, which is what the delete dialog has always written for this case.
        // The whole digest is what a deletion is addressed to, so shortening the cell has to leave
        // it reachable (DD168).
        var row = VolumeRow.From([Volume(Anonymous)], [])[0];

        Assert.Equal($"{Anonymous[..12]}…", row.NameText);
        Assert.Equal(Anonymous, row.FullName);

        // And the dialog reads the row's rule rather than shortening it a second time.
        Assert.Contains(row.NameText, VolumeRemoval.Question(row), StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_somebody_chose_is_drawn_whole_and_needs_no_tooltip()
    {
        // The column wanted the name, so there is nothing to shorten and nothing to put under the
        // pointer — a tooltip repeating the text it covers is noise.
        var row = VolumeRow.From([Volume("shop_pgdata")], [])[0];

        Assert.Equal("shop_pgdata", row.NameText);
        Assert.Null(row.FullName);
    }

    [Fact]
    public void A_named_volume_that_merely_looks_long_is_not_anonymous()
    {
        var rows = VolumeRow.From([Volume("my_very_long_but_perfectly_named_volume")], []);

        Assert.False(rows[0].IsAnonymous);
    }

    [Fact]
    public void A_name_of_exactly_the_right_length_that_is_not_hex_is_still_a_name()
    {
        // Length alone is not the test, and a rule that only checked it would delete somebody's
        // named volume in a prune. Sixty-four characters, none of them hex digits.
        var rows = VolumeRow.From([Volume(new string('z', 64))], []);

        Assert.False(rows[0].IsAnonymous);
    }

    [Fact]
    public void An_uppercase_digest_shaped_name_is_not_what_docker_generates()
    {
        Assert.False(VolumeRow.From([Volume(Anonymous.ToUpperInvariant())], [])[0].IsAnonymous);
    }

    [Fact]
    public void The_compose_prefix_is_read_off_the_name_because_that_is_all_there_is()
    {
        // `<project>_<volume>` is what tells somebody the orphan belonged to a project they deleted
        // months ago.
        var rows = VolumeRow.From([Volume("shop_pgdata")], []);

        Assert.Equal("shop", rows[0].Project);
    }

    [Fact]
    public void A_name_with_no_underscore_claims_no_project()
    {
        Assert.Null(VolumeRow.From([Volume("pgdata")], [])[0].Project);
    }

    // ---- order ----------------------------------------------------------------------------------

    [Fact]
    public void Rows_are_ordered_by_name_so_a_project_s_volumes_sit_together()
    {
        var rows = VolumeRow.From(
            [Volume("shop_redis"), Volume("blog_pgdata"), Volume("shop_pgdata")], []);

        Assert.Equal(["blog_pgdata", "shop_pgdata", "shop_redis"], rows.Select(row => row.Name));
    }

    // ---- the two-call load ------------------------------------------------------------------------

    [Fact]
    public void A_row_with_no_size_yet_says_it_is_being_measured_rather_than_zero()
    {
        // The plain volume list always answers -1, and rendering that as "0 B" would say a database
        // is empty.
        var rows = VolumeRow.From([Volume("shop_pgdata")], []);

        Assert.Null(rows[0].Size);
        Assert.Equal("measuring…", rows[0].SizeText);
    }

    [Fact]
    public void The_size_call_fills_the_column_without_disturbing_anything_else()
    {
        var rows = VolumeRow.From([Volume("shop_pgdata")], [Mounting("pgdata", "shop_pgdata")]);

        var filled = VolumeRow.WithSizes(rows, [Volume("shop_pgdata", 1_400_000_000)]);

        Assert.Equal("1.4 GB", filled[0].SizeText);
        Assert.Equal("pgdata", filled[0].Holders);
    }

    [Fact]
    public void A_volume_the_size_call_did_not_mention_keeps_saying_it_is_unmeasured()
    {
        // Created between the two calls. Dropping it would make a volume vanish from a list that
        // had just shown it.
        var rows = VolumeRow.From([Volume("a"), Volume("b")], []);

        var filled = VolumeRow.WithSizes(rows, [Volume("a", 500)]);

        Assert.Equal(2, filled.Count);
        Assert.Equal("500 B", filled[0].SizeText);
        Assert.Equal("measuring…", filled[1].SizeText);
    }

    [Fact]
    public void A_daemon_reporting_minus_one_is_not_read_as_a_size()
    {
        var rows = VolumeRow.From([new VolumeSummary { Name = "a", UsageData = new VolumeUsage() }], []);

        Assert.Null(rows[0].Size);
    }

    // ---- the totals and prune ---------------------------------------------------------------------

    [Fact]
    public void The_summary_waits_for_the_measurement_rather_than_stating_a_partial_total()
    {
        var rows = VolumeRow.From([Volume("a", 100), Volume("b")], []);

        var totals = VolumeTotals.For(rows);

        Assert.False(totals.Measured);
        Assert.Contains("measuring volumes", totals.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Prune_counts_only_anonymous_volumes_nothing_mounts()
    {
        var rows = VolumeRow.From(
            [Volume(Anonymous), Volume("shop_pgdata"), Volume("b" + Anonymous[1..])],
            [Mounting("web", "b" + Anonymous[1..])]);

        var totals = VolumeTotals.For(rows);

        Assert.Equal(1, totals.AnonymousUnused);
        Assert.True(totals.CanPrune);
    }

    [Fact]
    public void With_no_loose_anonymous_volumes_prune_is_off()
    {
        var totals = VolumeTotals.For(VolumeRow.From([Volume("shop_pgdata", 10)], []));

        Assert.False(totals.CanPrune);
        Assert.Contains("no loose anonymous volumes", totals.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void The_prune_question_says_what_is_lost_and_that_named_volumes_are_safe()
    {
        var totals = VolumeTotals.For(VolumeRow.From([Volume(Anonymous)], []));

        var question = totals.PruneQuestion;

        Assert.Contains("1 anonymous volume", question, StringComparison.Ordinal);
        Assert.Contains("gone for good", question, StringComparison.Ordinal);
        Assert.Contains("Named volumes are not touched", question, StringComparison.Ordinal);
    }

    [Fact]
    public void The_prune_outcome_reports_the_daemon_s_own_numbers()
    {
        var outcome = VolumeTotals.PruneOutcome(new VolumesPruned
        {
            VolumesDeleted = ["a", "b"],
            SpaceReclaimed = 212_000_000,
        });

        Assert.Contains("Deleted 2 volumes", outcome, StringComparison.Ordinal);
        Assert.Contains("212 MB", outcome, StringComparison.Ordinal);
    }

    // ---- the irreversible question ----------------------------------------------------------------

    [Fact]
    public void Deleting_any_volume_asks_first_and_says_it_does_not_come_back()
    {
        // An image re-pulls and a container rebuilds. This one does not.
        var question = VolumeRemoval.Question(VolumeRow.From([Volume("shop_pgdata")], [])[0]);

        Assert.Contains("shop_pgdata", question, StringComparison.Ordinal);
        Assert.Contains("gone for good", question, StringComparison.Ordinal);
    }

    [Fact]
    public void The_question_names_the_container_holding_it_because_that_is_what_to_deal_with_first()
    {
        var row = VolumeRow.From([Volume("shop_pgdata")], [Mounting("pgdata", "shop_pgdata")])[0];

        var question = VolumeRemoval.Question(row);

        Assert.Contains("pgdata has it", question, StringComparison.Ordinal);
        Assert.Contains("Deal with the container first", question, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_containers_holding_it_read_as_a_plural()
    {
        var row = VolumeRow.From(
            [Volume("data")], [Mounting("web", "data"), Mounting("worker", "data")])[0];

        Assert.Contains("web, worker have it", VolumeRemoval.Question(row), StringComparison.Ordinal);
    }

    [Fact]
    public void The_question_names_the_compose_project_when_the_name_carries_one()
    {
        var question = VolumeRemoval.Question(VolumeRow.From([Volume("shop_pgdata")], [])[0]);

        Assert.Contains("\"shop\"", question, StringComparison.Ordinal);
    }

    [Fact]
    public void The_question_states_the_size_once_it_is_known()
    {
        var row = VolumeRow.From([Volume("shop_pgdata", 1_400_000_000)], [])[0];

        Assert.Contains("holds 1.4 GB", VolumeRemoval.Question(row), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unmeasured_volume_promises_no_size_rather_than_claiming_zero()
    {
        var question = VolumeRemoval.Question(VolumeRow.From([Volume("shop_pgdata")], [])[0]);

        Assert.DoesNotContain("holds", question, StringComparison.Ordinal);
    }
}
