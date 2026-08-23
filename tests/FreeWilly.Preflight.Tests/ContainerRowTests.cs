using FreeWilly.Core.Api;
using FreeWilly.Core.Engine;
using FreeWilly.Tray.Ui;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// What the window puts on screen, tested without a window. The empty states carry most of the
/// weight: the first screen a new user sees is usually one of them.
/// </summary>
public sealed class ContainerRowTests
{
    private static PortBinding Port(int privatePort, int? publicPort, string type = "tcp") =>
        new() { PrivatePort = privatePort, PublicPort = publicPort, Type = type };

    // ---- ports as links -----------------------------------------------------------------------

    [Fact]
    public void A_published_tcp_port_is_a_link_to_localhost()
    {
        var link = PortLink.From(Port(80, 8080));

        Assert.True(link.IsLink);
        Assert.Equal("http://localhost:8080", link.Url);
        Assert.Equal("8080->80/tcp", link.Text);
    }

    [Fact]
    public void A_port_that_is_only_exposed_is_text_because_there_is_nowhere_to_go()
    {
        var link = PortLink.From(Port(443, null));

        Assert.False(link.IsLink);
        Assert.Null(link.Url);
        Assert.Equal("443/tcp", link.Text);
    }

    [Fact]
    public void A_published_udp_port_is_not_a_link()
    {
        // It is published and real, and http://localhost:x is not where it is. A link that opens a
        // browser onto nothing is worse than plain text.
        var link = PortLink.From(Port(53, 5353, "udp"));

        Assert.False(link.IsLink);
        Assert.Equal("5353->53/udp", link.Text);
    }

    [Fact]
    public void A_port_published_on_both_address_families_is_one_cell()
    {
        var container = new ContainerSummary
        {
            Id = "d7c06c1092c59ff1",
            Names = ["/web"],
            Ports = [Port(80, 8099), Port(80, 8099)],
        };

        var row = ContainerRow.From(container);

        Assert.Single(row.Ports);
        Assert.Equal("8099->80/tcp", row.Ports[0].Text);
    }

    // ---- the row ------------------------------------------------------------------------------

    [Fact]
    public void A_row_carries_what_the_columns_show()
    {
        var container = new ContainerSummary
        {
            Id = "a1b2c3d4e5f60718293a",
            Names = ["/hopeful_wozniak"],
            Image = "nginx:alpine",
            State = "running",
            Status = "Up 3 minutes",
            Ports = [Port(80, 8080)],
        };

        var row = ContainerRow.From(container);

        Assert.Equal("hopeful_wozniak", row.Name);
        Assert.Equal("nginx:alpine", row.Image);
        Assert.Equal("running", row.State);
        Assert.Equal("Up 3 minutes", row.Status);
        Assert.True(row.IsRunning);
        Assert.Equal("a1b2c3d4e5f60718293a", row.Id);
    }

    [Fact]
    public void An_image_the_daemon_could_only_name_by_digest_reads_as_twelve_characters()
    {
        // What a rebuilt or removed tag leaves behind: the reference stops resolving and the
        // daemon answers with the raw id. Forty characters of it are shared by every image on the
        // machine, so the untouched field draws a constant (DD166).
        var container = new ContainerSummary
        {
            Id = "b12f355e14c8",
            Names = ["/vigilant_diffie"],
            Image = "sha256:e4af4f3e24fdad8647264213f60f94ba16f67ae71a4b7281e6a0cd55de2627d1",
        };

        Assert.Equal("e4af4f3e24fd", ContainerRow.From(container).Image);
    }

    [Theory]
    [InlineData("redis:7-alpine")]
    [InlineData("registry.local:5000/app:2.1")]
    [InlineData("redis@sha256:e4af4f3e24fdad8647264213f60f94ba16f67ae71a4b7281e6a0cd55de2627d1")]
    [InlineData("deadbeef")]
    public void A_name_is_left_exactly_as_the_daemon_said_it(string reference)
    {
        // Only a bare digest shortens. A name is the answer the column wanted, and a digest that
        // is the suffix of one is the half that identifies it.
        Assert.Equal(reference, ImageReference.Short(reference));
    }

    [Fact]
    public void A_container_with_no_name_shows_its_short_id()
    {
        var row = ContainerRow.From(new ContainerSummary { Id = "ff00112233445566", Names = [] });

        Assert.Equal("ff0011223344", row.Name);
    }

    [Fact]
    public void An_exited_container_is_not_running()
    {
        var row = ContainerRow.From(new ContainerSummary { State = "exited" });

        Assert.False(row.IsRunning);
    }

    // ---- empty is designed --------------------------------------------------------------------

    [Fact]
    public void Empty_with_the_engine_down_offers_to_start_it()
    {
        var empty = EmptyState.For(EngineState.Stopped);

        Assert.True(empty.OffersStart);
        Assert.Contains("not running", empty.Headline, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Empty_with_the_engine_up_says_so_plainly_and_offers_nothing()
    {
        // Two different reasons a list is empty, and only one of them is something a user can act
        // on. Offering "start the engine" to somebody whose engine is running is a wrong answer.
        var empty = EmptyState.For(EngineState.Running);

        Assert.False(empty.OffersStart);
        Assert.Contains("No containers", empty.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_while_starting_asks_for_neither_and_says_why_it_is_waiting()
    {
        var empty = EmptyState.For(EngineState.Starting);

        Assert.False(empty.OffersStart);
        Assert.Contains("WSL2", empty.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(EngineState.Running)]
    [InlineData(EngineState.Starting)]
    [InlineData(EngineState.Stopped)]
    public void Every_empty_state_says_something_in_both_lines(EngineState engine)
    {
        var empty = EmptyState.For(engine);

        Assert.False(string.IsNullOrWhiteSpace(empty.Headline));
        Assert.False(string.IsNullOrWhiteSpace(empty.Detail));
    }

    [Fact]
    public void The_three_empty_states_are_three_different_screens()
    {
        var states = new[] { EngineState.Running, EngineState.Starting, EngineState.Stopped }
            .Select(EmptyState.For)
            .Select(e => e.Headline)
            .ToList();

        Assert.Equal(3, states.Distinct().Count());
    }
}
