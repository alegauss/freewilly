using FreeWilly.Core.Engine;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// The rescue that carries its tools rather than fetching them (DD216).
/// </summary>
/// <remarks>
/// <para><b>Measured, twice, on 29 August 2026.</b> The first drill on this machine imported the
/// pinned Alpine rootfs, ran <c>apk add</c>, and exported a 10.8 MB image on the way out. The next
/// one imported that image and reported <c>already in freewilly-drill, so nothing is fetched</c>: no
/// network call at any point. That is the machine a check is most likely to be wanted on, since the
/// moment <c>e2fsck</c> is needed is a moment when something is already wrong and the network may be
/// part of it.</para>
///
/// <para>What is asserted here is the decision rather than the effect: which file is imported, when
/// the fetch is skipped, when an image is kept, and that a bad one costs a network call rather than
/// the run.</para>
/// </remarks>
/// <summary>
/// A machine whose <c>--export</c> writes a file, which is what the keeping depends on.
/// </summary>
/// <remarks>
/// <see cref="FakeWsl"/> answers an export with success and no file, so the move that follows finds
/// nothing and the image is never kept. That is fine for asserting the order of calls and useless
/// for asserting what happens to the files afterwards, which is what DD223 is about.
/// </remarks>
internal sealed class ExportingWsl : IWsl
{
    private readonly FakeWsl _inner = new();

    /// <summary>Every argument list this was called with, in order.</summary>
    internal List<string[]> Invocations => _inner.Invocations;

    /// <inheritdoc/>
    public WslResult Run(TimeSpan budget, params string[] arguments)
    {
        var result = _inner.Run(budget, arguments);
        if (arguments is ["--export", _, var into, ..])
        {
            File.WriteAllText(into, "a tarball, as far as anything here is concerned");
        }

        return result;
    }
}

public sealed class RescueImageTests
{
    private const string Name = "freewilly-rescue";
    private const string Rootfs = @"C:\downloads\rootfs.tar.gz";

    private static EnginePaths Paths() =>
        new(Path.Combine(Path.GetTempPath(), $"fw-{Guid.NewGuid():N}"));

    /// <summary>An install directory with a prepared image already in it.</summary>
    private static EnginePaths Prepared(out string image)
    {
        var paths = Paths();
        Directory.CreateDirectory(paths.Root);
        image = new RescueImage(new FakeWsl(), paths).PreparedPath;
        File.WriteAllText(image, "not really a tarball, and nothing here opens it");
        return paths;
    }

    [Fact]
    public void A_machine_with_no_image_imports_the_pinned_rootfs_and_fetches_the_tools()
    {
        // The first check on a machine, unchanged from DD199. It still costs what it always cost,
        // which is the point: the saving is for every run after it.
        var paths = Paths();
        var wsl = new FakeWsl();
        var image = new RescueImage(wsl, paths);

        Assert.False(image.IsPrepared);

        var brought = image.Import(Name, image.PreparedPath + ".root", Rootfs);

        Assert.True(brought.Step.Ok);
        Assert.False(brought.FromPrepared);
        Assert.Contains(
            wsl.Invocations,
            argv => argv.Length > 0 && argv[0] == "--import" && argv.Contains(Rootfs));
    }

    [Fact]
    public void A_prepared_image_is_imported_instead_of_the_rootfs()
    {
        var paths = Prepared(out var kept);
        var wsl = new FakeWsl();

        var brought = new RescueImage(wsl, paths).Import(Name, Path.Combine(paths.Root, "r"), Rootfs);

        Assert.True(brought.Step.Ok);
        Assert.True(brought.FromPrepared);
        Assert.Contains(
            wsl.Invocations,
            argv => argv.Length > 0 && argv[0] == "--import" && argv.Contains(kept));
        Assert.DoesNotContain(
            wsl.Invocations, argv => argv.Contains(Rootfs, StringComparer.Ordinal));
    }

    [Fact]
    public void Tools_that_are_already_there_are_never_fetched()
    {
        // The whole saving, and the reason the moment e2fsck is wanted is the moment a network call
        // is worst: something is already wrong, and the network may be part of what is wrong.
        var wsl = new FakeWsl().Answer(0, "/sbin/e2fsck\n/sbin/debugfs");

        var step = new RescueImage(wsl, Paths()).Tools(Name);

        Assert.True(step.Ok);
        Assert.Contains("nothing is fetched", step.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(
            wsl.Invocations,
            argv => argv.Any(word => word.Contains("apk add", StringComparison.Ordinal)));
    }

    [Fact]
    public void Tools_that_are_missing_are_fetched_and_then_asked_for_by_name()
    {
        // DD196's rule, kept: a mirror can succeed and install nothing useful, so apk's exit code
        // is not the answer and `command -v` is.
        var wsl = new FakeWsl()
            .Answer(1, "sh: e2fsck: not found")
            .Answer(0, "/sbin/e2fsck\n/sbin/debugfs");

        var step = new RescueImage(wsl, Paths()).Tools(Name);

        Assert.True(step.Ok);
        Assert.Contains(
            wsl.Invocations,
            argv => argv.Any(word => word.Contains("apk add", StringComparison.Ordinal)
                && word.Contains("command -v e2fsck", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_prepared_image_that_will_not_import_is_thrown_away_and_the_rootfs_used()
    {
        // A machine that lost power mid-export leaves a file that exists, imports as a distribution
        // and carries nothing. Refusing on that would be this mechanism having broken the check it
        // exists to make possible: the pinned rootfs is still on disk and still works.
        var paths = Prepared(out var kept);
        var wsl = new FakeWsl()
            .Answer(1, "the tarball is not a tarball") // the prepared image
            .Answer(0);                                // the pinned rootfs

        var brought = new RescueImage(wsl, paths).Import(Name, Path.Combine(paths.Root, "r"), Rootfs);

        Assert.True(brought.Step.Ok);
        Assert.False(brought.FromPrepared);
        Assert.False(File.Exists(kept), "a broken image was left for the next run to trip over");
    }

    [Fact]
    public void The_image_is_exported_after_the_terminate_and_before_the_unregister()
    {
        // Both halves matter. A distribution with a process still inside it is the case nothing here
        // wants to be the first to find out about, and after the unregister there is nothing left to
        // export.
        var paths = Paths();
        Directory.CreateDirectory(paths.Root);
        var wsl = new FakeWsl();

        new RescueImage(wsl, paths).PutAway(Name, keep: true);

        var terminated = wsl.Invocations.FindIndex(argv => argv.Length > 0 && argv[0] == "--terminate");
        var exported = wsl.Invocations.FindIndex(argv => argv.Length > 0 && argv[0] == "--export");
        var unregistered = wsl.Invocations.FindIndex(
            argv => argv.Length > 0 && argv[0] == "--unregister");

        Assert.True(terminated >= 0, "the distribution was never terminated");
        Assert.True(exported >= 0, "nothing was kept, so the next check still needs a network");
        Assert.True(unregistered >= 0, "the distribution was left registered");
        Assert.True(terminated < exported, "a running distribution was exported");
        Assert.True(exported < unregistered, "there was nothing left to export by then");
    }

    [Fact]
    public void A_run_that_never_got_the_tools_keeps_nothing()
    {
        // An image of a distribution with no e2fsck in it is worse than none: the next check would
        // import it, find nothing, and fetch anyway, having paid for the import twice.
        var wsl = new FakeWsl();

        new RescueImage(wsl, Paths()).PutAway(Name, keep: false);

        Assert.DoesNotContain(wsl.Invocations, argv => argv.Length > 0 && argv[0] == "--export");
    }

    [Fact]
    public void The_image_is_written_to_a_temporary_name_and_moved_into_place()
    {
        // A machine that goes down mid-export must not leave a half-written image where the next
        // check looks for a whole one.
        var paths = Paths();
        Directory.CreateDirectory(paths.Root);
        var image = new RescueImage(new FakeWsl(), paths);

        // The fake writes no file, so the export "succeeds" and the move finds nothing to move.
        // What is asserted is the name it exported to, which is the half a fake can see.
        var wsl = new FakeWsl();
        new RescueImage(wsl, paths).PutAway(Name, keep: true);

        var exported = wsl.Invocations.First(argv => argv.Length > 0 && argv[0] == "--export");
        Assert.EndsWith(".part", exported[^1], StringComparison.Ordinal);
        Assert.StartsWith(image.PreparedPath, exported[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Keeping_a_new_image_drops_the_ones_this_build_no_longer_names()
    {
        // DD223. The name carries the pinned rootfs digest, so a manifest that bumps Alpine stops
        // matching the old file rather than replacing it — correct, and left alone it keeps eleven
        // megabytes on disk forever with nothing that will ever open it again.
        var paths = Paths();
        Directory.CreateDirectory(paths.Root);
        var stale = Path.Combine(paths.Root, "rescue-000000000000.tar");
        var older = Path.Combine(paths.Root, "rescue-ffffffffffff.tar");
        File.WriteAllText(stale, "last year's Alpine");
        File.WriteAllText(older, "the one before that");

        var wsl = new ExportingWsl();
        var image = new RescueImage(wsl, paths);

        var step = image.PutAway(Name, keep: true);

        Assert.True(step.Ok, step.Detail);
        Assert.True(File.Exists(image.PreparedPath), "the new image was not kept");
        Assert.False(File.Exists(stale), "an image this build no longer names was left on disk");
        Assert.False(File.Exists(older), "only one of the older images went");
        Assert.Contains("2 older one(s) went", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_half_written_image_belonging_to_a_run_still_going_is_left_alone()
    {
        // Windows file globbing carries its 8.3 inheritance, where a three-character extension also
        // matches longer ones — so `rescue-*.tar` finds a `.tar.part` too. That file belongs to an
        // export still running, and taking it would be the sweep breaking the thing it tidies up
        // after.
        var paths = Paths();
        Directory.CreateDirectory(paths.Root);
        var inFlight = Path.Combine(paths.Root, "rescue-abcabcabcabc.tar.part");
        File.WriteAllText(inFlight, "half of an export somebody else is still writing");

        var wsl = new ExportingWsl();
        var image = new RescueImage(wsl, paths);

        image.PutAway(Name, keep: true);

        Assert.True(File.Exists(inFlight), "the sweep took a file another run was still writing");
    }

    [Fact]
    public void A_run_that_kept_nothing_sweeps_nothing()
    {
        // The sweep runs where a replacement has just landed and nowhere else. Dropping the images
        // on a run that failed to make a new one would leave a machine with none at all, which is
        // the network call this whole mechanism exists to avoid.
        var paths = Paths();
        Directory.CreateDirectory(paths.Root);
        var stale = Path.Combine(paths.Root, "rescue-000000000000.tar");
        File.WriteAllText(stale, "last year's Alpine");

        new RescueImage(new ExportingWsl(), paths).PutAway(Name, keep: false);

        Assert.True(File.Exists(stale), "an image was dropped by a run that kept no replacement");
    }

    [Fact]
    public void The_sweep_and_the_write_agree_about_the_name()
    {
        // Two spellings of one name is a sweep that quietly stops finding anything, and an install
        // directory that grows by eleven megabytes per Alpine bump with nothing reporting it.
        var image = new RescueImage(new FakeWsl(), Paths());

        Assert.Matches(
            "^" + RescueImage.Pattern.Replace("*", "[0-9a-f]+") + "$",
            Path.GetFileName(image.PreparedPath));
    }

    [Fact]
    public void The_image_is_named_after_the_rootfs_it_was_built_from()
    {
        // So a manifest that bumps Alpine invalidates the image by not matching its name, rather
        // than by somebody remembering to delete it.
        var name = Path.GetFileName(new RescueImage(new FakeWsl(), Paths()).PreparedPath);

        Assert.Contains(
            EngineManifest.Current.Rootfs.Sha256[..12], name, StringComparison.Ordinal);
    }
}
