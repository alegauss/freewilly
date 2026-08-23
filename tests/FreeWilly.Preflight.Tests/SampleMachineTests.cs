using FreeWilly.Core.Api;
using FreeWilly.Core.Fixtures;
using FreeWilly.Tray.Ui;
using FreeWilly.Tray.Ui.Pages;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// A window drawn from a fixture, and the row template that could not draw at all (DD38, DD66).
/// </summary>
public sealed class SampleMachineTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeWilly.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "the repository root was not found above the test binaries");
        return directory!.FullName;
    }

    // ---- the defect the fixture found -----------------------------------------------------------

    [Fact]
    public void No_markup_puts_a_DynamicResource_on_BasedOn()
    {
        // BasedOn is a CLR property and not a DependencyProperty, so a DynamicResource on it is
        // illegal — and WPF says so at RENDER time, on the first row measured, not at parse time.
        // The window opened, the header drew, and every container row threw: shipped in DD35 and
        // invisible until a fixture produced a row to draw.
        foreach (var markup in Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot(), "src"), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain(
                "BasedOn=\"{DynamicResource",
                File.ReadAllText(markup),
                StringComparison.Ordinal);
        }
    }

    // ---- the machine covers the states it exists to cover ---------------------------------------

    [Fact]
    public async Task Every_row_is_here_because_something_renders_differently_for_it()
    {
        var machine = new SampleMachine();
        var rows = (await machine.ContainersAsync()).Select(ContainerRow.From).ToList();

        // Running and exited, so the verbs on a row differ between rows in one picture.
        Assert.Contains(rows, r => r.CanStop);
        Assert.Contains(rows, r => r.CanStart);

        // A published port is a link and an exposed one is text. One of each, or the difference the
        // template exists for cannot be seen.
        Assert.Contains(rows.SelectMany(r => r.Ports), p => p.IsLink);
        Assert.Contains(rows.SelectMany(r => r.Ports), p => !p.IsLink);

        // A kill beside a clean exit: 137 is what the diagnostic half of this product is about.
        Assert.Contains(rows, r => r.Status.Contains("(137)", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Status.Contains("(0)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_images_carry_something_dangling_and_something_in_use()
    {
        var machine = new SampleMachine();
        var rows = ImageRow.From(
            await machine.ImagesAsync(), await machine.ContainersAsync()).ToList();

        // Or the prune button has nothing to confirm and its dialog cannot be looked at.
        Assert.True(ImageTotals.For(rows).CanPrune);

        // The other half this test is named for, which nothing asserted and which was false: no
        // container here carried an image id, so the join found no holder for anything and USED BY
        // was blank in every capture of the page it is the point of (DD167).
        Assert.Contains(rows, row => row.IsInUse);
    }

    [Fact]
    public async Task One_container_runs_on_an_image_the_machine_no_longer_has()
    {
        // The DD167 state, and the fixture is the only place it can be photographed: a rebuilt tag
        // leaves the daemon with a digest to report and the images page with nothing to hold.
        var machine = new SampleMachine();
        var rows = (await machine.ContainersAsync()).Select(ContainerRow.From).ToList();

        // Exactly one, or the note is the fixture's normal state rather than its exception.
        var orphan = Assert.Single(rows, row => row.ImageIsGone);
        Assert.Equal("image gone", orphan.ImageNote);
        Assert.Equal("666666666666", orphan.Image);
    }

    [Fact]
    public async Task The_volumes_carry_an_anonymous_one_and_a_measured_size()
    {
        var machine = new SampleMachine();
        var rows = VolumeRow.From(await machine.VolumesAsync(), await machine.ContainersAsync()).ToList();
        var measured = VolumeRow.WithSizes(rows, await machine.VolumeSizesAsync()).ToList();

        Assert.Contains(rows, r => r.IsAnonymous);
        Assert.True(VolumeTotals.For(measured).CanPrune);

        // Measured rather than "measuring…", so the size column says something in a capture.
        Assert.All(measured, r => Assert.True(r.Size is not null));
    }

    [Fact]
    public async Task Nothing_here_is_named_as_though_it_were_real()
    {
        var machine = new SampleMachine();

        // A screenshot of this in a README should be obviously a fixture. The alternative is
        // somebody's real project ending up in documentation.
        Assert.All(
            await machine.ContainersAsync(),
            c => Assert.StartsWith(SampleMachine.Prefix, c.DisplayName, StringComparison.Ordinal));
    }

    // ---- and it cannot break anything ------------------------------------------------------------

    [Theory]
    [InlineData("start")]
    [InlineData("stop")]
    [InlineData("remove-container")]
    [InlineData("remove-image")]
    [InlineData("remove-volume")]
    [InlineData("prune-images")]
    [InlineData("prune-volumes")]
    public async Task Every_write_refuses_in_the_shape_a_refusal_arrives_in(string verb)
    {
        var machine = new SampleMachine();

        // A preview whose buttons deleted things would be a preview to be careful with — and the
        // refusal line under a row is one of the states worth seeing anyway, so this is not merely
        // safe, it is the point.
        var refusal = await Assert.ThrowsAsync<DockerApiException>(() => verb switch
        {
            "start" => machine.StartContainerAsync("c1aaaaaaaaaa0000"),
            "stop" => machine.StopContainerAsync("c1aaaaaaaaaa0000"),
            "remove-container" => machine.RemoveContainerAsync("c1aaaaaaaaaa0000"),
            "remove-image" => machine.RemoveImageAsync("sha256:1111111111111111"),
            "remove-volume" => machine.RemoveVolumeAsync("sample_uploads"),
            "prune-images" => machine.PruneDanglingImagesAsync(),
            _ => machine.PruneAnonymousVolumesAsync(),
        });

        Assert.Equal(SampleMachine.ReadOnly, refusal.Detail);
    }

    [Fact]
    public async Task The_log_is_framed_the_way_the_daemon_frames_one()
    {
        var machine = new SampleMachine();
        await using var stream = await machine.LogsAsync("c1aaaaaaaaaa0000", follow: false);

        var frames = new LogFrames(stream, framed: true);
        var chunks = new List<LogChunk>();
        while (await frames.ReadAsync() is { } chunk)
        {
            chunks.Add(chunk);
        }

        // Faking the text without the frames would make the preview prove the opposite of what it is
        // for: de-framing is part of what the log window is.
        Assert.NotEmpty(chunks);
        Assert.Contains(chunks, c => c.Stream == LogStream.StdErr);
        Assert.Contains(chunks, c => c.Stream == LogStream.StdOut);
    }

    [Fact]
    public void The_window_takes_the_seam_and_not_the_client()
    {
        // The whole of DD38 in one assertion: if a window took a concrete DockerApi again, no fixture
        // could stand in for it and the pages would only ever be reviewable against a live daemon.
        foreach (var type in new[]
        {
            typeof(MainWindow), typeof(LogWindow),
            typeof(ContainersPage), typeof(ImagesPage), typeof(VolumesPage),
        })
        {
            var takes = type.GetConstructors(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public)
                .SelectMany(c => c.GetParameters())
                .Select(p => p.ParameterType);

            Assert.DoesNotContain(typeof(DockerApi), takes);
        }
    }

    // ---- the fixture exercises the ordering rather than its fallback (DD113) --------------------

    [Fact]
    public async Task The_sample_project_orders_differently_from_the_list_it_is_drawn_in()
    {
        // The whole of DD113. Before this the fixture's compose project declared no dependencies, so
        // `ComposeOrder` saw a project with no edges every single time the fixture was used, took
        // its fallback and looked exactly as it would if it were not there — which is the one thing
        // a fixture exists to make showable.
        //
        // Asserted as a difference and not as a literal order: the claim is that the fixture
        // exercises the ordering, and a hard-coded sequence would still pass on the day the labels
        // were removed and the fallback happened to agree.
        var rows = (await new SampleMachine().ContainersAsync())
            .Select(ContainerRow.From)
            .Where(row => row.Project is not null)
            .ToList();

        Assert.NotEmpty(rows);
        Assert.Contains(rows, row => row.DependsOn.Count > 0);

        var starting = ComposeOrder.ToStart(rows).Select(row => row.Service).ToList();
        var stopping = ComposeOrder.ToStop(rows).Select(row => row.Service).ToList();

        Assert.NotEqual(rows.Select(row => row.Service), starting);
        Assert.NotEqual(starting, stopping);

        // And it is the right difference: what is depended on comes up first and goes down last.
        Assert.Equal("db", starting[0]);
        Assert.Equal("db", stopping[^1]);
    }

    [Fact]
    public async Task Every_label_the_fixture_carries_is_spelled_the_way_compose_spells_it()
    {
        // A fixture that simplified the format would exercise a parser this project does not ship.
        // `<service>:<condition>:<restart>` is what compose writes, and `DependenciesIn` reading a
        // service out of it is the thing being stood in for.
        var containers = await new SampleMachine().ContainersAsync();
        var labels = containers
            .Select(container => container.Labels)
            .Where(carried => carried is not null)
            .Select(carried => carried!.TryGetValue(ComposeOrder.DependsOnLabel, out var value)
                ? value
                : null)
            .Where(value => value is not null)
            .ToList();

        Assert.NotEmpty(labels);
        foreach (var label in labels)
        {
            Assert.Equal(3, label!.Split(',')[0].Split(':').Length);
            Assert.NotEmpty(ComposeOrder.DependenciesIn(label));
        }
    }
}
