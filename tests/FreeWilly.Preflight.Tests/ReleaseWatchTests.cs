using FreeWilly.Core.Releases;
using FreeWilly.Core.Settings;
using FreeWilly.Tray;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// When the tray asks about releases, and what it does with the answer (DD154).
/// </summary>
public sealed class ReleaseWatchTests
{
    private sealed class Feed(string body) : IReleaseFeed
    {
        internal int Asked { get; private set; }

        public Task<string> LatestAsync(CancellationToken cancellation)
        {
            Asked++;
            return Task.FromResult(body);
        }
    }

    private static string Release(string tag) => $$"""
        {
          "tag_name": "{{tag}}",
          "assets": [
            { "name": "FreeWilly-Setup-{{tag.TrimStart('v')}}.exe",
              "browser_download_url": "https://example.invalid/setup.exe" },
            { "name": "SHA256SUMS.txt", "browser_download_url": "https://example.invalid/sums" }
          ]
        }
        """;

    [Fact]
    public async Task It_finds_a_newer_release_and_hands_it_over()
    {
        var found = new List<(AvailableRelease Release, bool Announce)>();
        using var watch = new ReleaseWatch(
            (release, announce) => found.Add((release, announce)),
            () => new Feed(Release("v99.0.0")));

        var answer = await watch.CheckAsync();

        Assert.Equal(new Version(99, 0, 0), answer?.Version);
        Assert.Single(found);
        Assert.True(found[0].Announce);
    }

    [Fact]
    public async Task Nothing_has_to_be_turned_on_first()
    {
        // DD171. The check shipped off behind a menu tick, and a check nobody turns on is a check
        // nobody has — the 1.0.1 notes had to tell readers that upgrading from 1.0.0 was a manual
        // download, and off by default is that same failure by choice. There is now no setting to
        // pass and no way to construct a watch that declines to ask.
        var settings = new TraySettings();
        var feed = new Feed(Release("v99.0.0"));
        using var watch = new ReleaseWatch((_, _) => { }, () => feed);

        Assert.NotNull(await watch.CheckAsync());
        Assert.Equal(1, feed.Asked);

        // And nothing a user can write down changes that: the file holds one setting, about the
        // engine, and it is not consulted here.
        Assert.Equal(TraySettings.EngineShipsOn, settings.StartWithTheTray);
    }

    [Fact]
    public async Task The_same_release_is_offered_every_tick_and_announced_once()
    {
        // A balloon every six hours about a release the user has already been told about is nagging,
        // and this product's whole argument is about not being the tool that does that. The menu item
        // still has to be offered on every tick, because a menu is rebuilt from what it was told.
        var announcements = new List<bool>();
        using var watch = new ReleaseWatch(
            (_, announce) => announcements.Add(announce),
            () => new Feed(Release("v99.0.0")));

        await watch.CheckAsync();
        await watch.CheckAsync();
        await watch.CheckAsync();

        Assert.Equal([true, false, false], announcements);
    }

    [Fact]
    public void Only_the_release_balloon_offers_to_install_anything()
    {
        // DD172. The tray's balloon carries failures as well as news — an engine that went away, an
        // update that did not verify — and clicking one of those must not start an install. The
        // guard is a default-false argument, so what can go wrong is a second call site opting in,
        // and that is what this counts. Read off the source because the surface is a NotifyIcon
        // there is no way to raise a balloon click on in a test.
        var program = File.ReadAllText(RepositoryFile(@"src\FreeWilly.Tray\Program.cs"));

        Assert.Equal(
            1,
            program.Split("offersUpdate: true", StringSplitOptions.None).Length - 1);
    }

    private static string RepositoryFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeWilly.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "the repository root was not found above the test binaries");
        return Path.Combine(directory!.FullName, relative);
    }

    [Fact]
    public void Four_a_day_after_a_launch_it_does_not_compete_with()
    {
        // A release happens a few times a year, and sixty unauthenticated requests an hour is a
        // shared NAT's whole allowance — so the cadence is the point rather than an implementation
        // detail. The first check waits, because the first seconds of a launch may be provisioning a
        // distribution and this is the least urgent thing the process does.
        Assert.Equal(TimeSpan.FromHours(6), ReleaseWatch.Every);
        Assert.True(ReleaseWatch.FirstCheckAfter > TimeSpan.Zero);
        Assert.True(ReleaseWatch.FirstCheckAfter < ReleaseWatch.Every);
    }

    [Fact]
    public void Starting_twice_arms_one_timer()
    {
        // Start is idempotent by construction rather than by the caller remembering. It had a second
        // caller — the menu tick DD171 removed — and the guard stays because a timer left behind
        // would double the traffic this file is otherwise careful about.
        using var watch = new ReleaseWatch((_, _) => { }, () => new Feed("{}"));

        watch.Start();
        watch.Start();
        watch.Dispose();
    }
}
