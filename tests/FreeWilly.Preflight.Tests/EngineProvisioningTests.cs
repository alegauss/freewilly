using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FreeWilly.Core.Engine;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// The manifest, the digest gate, and the invocations provisioning builds. The digest gate is the
/// one that matters: it is the only check that stops a mirror, a proxy or a truncated download from
/// being unpacked, so it is exercised failing before it is trusted passing.
/// </summary>
public sealed class EngineProvisioningTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("freewilly-test").FullName;

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private static string Sha256Of(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static Artefact Pinned(string content, string id = "thing") =>
        new(id, "1.2.3", $"https://example.invalid/{id}.bin", $"{id}.bin", Sha256Of(content));

    // ---- the manifest ------------------------------------------------------------------------

    [Fact]
    public void The_manifest_is_embedded_and_pins_five_artefacts()
    {
        var manifest = EngineManifest.Current;

        Assert.Equal(5, manifest.Artefacts.Count);
        Assert.Equal(
            ["rootfs", "engine", "cli", "compose", "buildx"],
            manifest.Artefacts.Select(a => a.Id));
    }

    [Fact]
    public void The_plugins_are_pinned_to_their_own_versions_and_not_the_engine_s()
    {
        // The two artefacts whose versions are deliberately free of the others (DD73, DD74): each
        // plugin is a separate upstream release with its own cadence, so tying either to the
        // engine's number would be a rule with no upstream behind it — unlike the CLI, which ships
        // from the same release and is asserted equal above.
        var manifest = EngineManifest.Current;

        Assert.NotEqual(manifest.Engine.Version, manifest.Compose.Version);
        Assert.NotEqual(manifest.Engine.Version, manifest.Buildx.Version);
        Assert.NotEqual(manifest.Compose.Version, manifest.Buildx.Version);

        // A bare executable each, which is why the place step is a copy rather than an extraction.
        Assert.EndsWith(".exe", manifest.Compose.Url, StringComparison.Ordinal);
        Assert.EndsWith(".exe", manifest.Buildx.Url, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_pinned_artefact_carries_a_version_an_https_url_and_a_sha256()
    {
        Assert.All(EngineManifest.Current.Artefacts, artefact =>
        {
            Assert.False(string.IsNullOrWhiteSpace(artefact.Version));
            Assert.StartsWith("https://", artefact.Url, StringComparison.Ordinal);
            Assert.Equal(64, artefact.Sha256.Length);
            Assert.True(
                artefact.Sha256.All(char.IsAsciiHexDigitLower),
                $"{artefact.Id} digest is not lower-case hex: {artefact.Sha256}");
            Assert.DoesNotContain("latest", artefact.Url, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void The_engine_and_the_cli_are_pinned_to_the_same_version()
    {
        // A dockerd and a docker CLI from different releases is a combination nobody upstream tests.
        var manifest = EngineManifest.Current;

        Assert.Equal(manifest.Engine.Version, manifest.Cli.Version);
    }

    // ---- the digest gate --------------------------------------------------------------------

    [Fact]
    public async Task An_artefact_whose_digest_matches_is_verified()
    {
        var store = new ArtefactStore(FakeFetcher.Writing("engine bytes"), _temp);

        var acquired = await store.AcquireAsync(Pinned("engine bytes"));

        Assert.True(acquired.Verified);
        Assert.False(acquired.Cached);
        Assert.True(File.Exists(acquired.Path));
    }

    [Fact]
    public async Task An_artefact_whose_digest_is_wrong_is_refused_and_deleted()
    {
        // The injected failure: the bytes arrive, and they are not the bytes this build pins.
        var artefact = Pinned("what we pinned");
        var store = new ArtefactStore(FakeFetcher.Writing("what actually arrived"), _temp);

        var acquired = await store.AcquireAsync(artefact);

        Assert.False(acquired.Verified);
        Assert.Null(acquired.Path);
        Assert.Contains(artefact.Sha256, acquired.Failure!, StringComparison.Ordinal);
        Assert.Contains(Sha256Of("what actually arrived"), acquired.Failure!, StringComparison.Ordinal);
        Assert.False(
            File.Exists(Path.Combine(_temp, artefact.FileName)),
            "a file that failed its digest must not be left where a retry can find it");
    }

    [Fact]
    public async Task A_download_that_failed_is_reported_as_a_download_and_not_as_a_digest()
    {
        var store = new ArtefactStore(new FakeFetcher(_ => null), _temp);

        var acquired = await store.AcquireAsync(Pinned("never arrives"));

        Assert.False(acquired.Verified);
        Assert.Contains("downloading", acquired.Failure!, StringComparison.Ordinal);
        Assert.DoesNotContain("pins", acquired.Failure!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_verified_file_already_on_disk_is_not_downloaded_again()
    {
        var artefact = Pinned("cached bytes");
        await File.WriteAllTextAsync(Path.Combine(_temp, artefact.FileName), "cached bytes");
        var fetcher = FakeFetcher.Writing("cached bytes");

        var acquired = await new ArtefactStore(fetcher, _temp).AcquireAsync(artefact);

        Assert.True(acquired.Verified);
        Assert.True(acquired.Cached);
        Assert.Empty(fetcher.Requested);
    }

    [Fact]
    public async Task A_corrupt_file_on_disk_is_replaced_rather_than_trusted_or_refused()
    {
        // A half-written file from an interrupted run is the common case, and re-fetching is the
        // answer to it. Trusting it would install a truncated engine.
        var artefact = Pinned("good bytes");
        await File.WriteAllTextAsync(Path.Combine(_temp, artefact.FileName), "truncat");
        var fetcher = FakeFetcher.Writing("good bytes");

        var acquired = await new ArtefactStore(fetcher, _temp).AcquireAsync(artefact);

        Assert.True(acquired.Verified);
        Assert.False(acquired.Cached);
        Assert.Single(fetcher.Requested);
    }

    // ---- what is inside the engine tarball --------------------------------------------------

    [Fact]
    public void A_tarball_shaped_like_upstreams_reads_as_one_top_directory()
    {
        var path = Write("engine.tgz", TarballBytes());

        var contents = EngineArchive.Read(path);

        Assert.Equal("docker", contents.TopDirectory);
        Assert.Empty(EngineArchive.Missing(contents));
        Assert.Contains("dockerd", contents.Binaries);
    }

    [Fact]
    public async Task A_tarball_missing_a_required_binary_stops_before_any_wsl_command()
    {
        // The injection this check exists for. Unread, the same archive fails inside the
        // distribution, after the import, with a message about tar.
        var wsl = new FakeWsl();
        var provisioner = Provisioner(wsl, out _, WithRealArtefacts(engineDropping: "dockerd"));

        var outcome = await provisioner.ProvisionAsync();

        Assert.False(outcome.Succeeded);
        Assert.Equal(ProvisioningStep.InspectEngine, outcome.Failure!.Step);
        Assert.Contains("missing dockerd", outcome.Failure.Detail, StringComparison.Ordinal);
        Assert.Empty(wsl.Invocations);
    }

    [Theory]
    [InlineData("docker/dockerd", "docker/dockerd")]
    [InlineData("./docker/dockerd", "docker/dockerd")]
    [InlineData("/docker/dockerd", "docker/dockerd")]
    [InlineData(".//docker/dockerd", "docker/dockerd")]
    [InlineData(@"docker\dockerd", "docker/dockerd")]
    [InlineData("./", "")]
    [InlineData(".", "")]
    public void An_entry_name_is_read_past_the_prefixes_that_mean_nothing(
        string written, string expected) =>
        Assert.Equal(expected, EngineArchive.Normalize(written));

    [Fact]
    public void A_tarball_written_with_the_dot_slash_prefix_still_reads_as_one_top_directory()
    {
        // The Alpine root filesystem this project downloads is written exactly this way, so the
        // convention is in use by an artefact already in the manifest — not a hypothetical.
        var path = Write("dotslash.tgz", TarballBytes(top: "./docker"));

        var contents = EngineArchive.Read(path);

        Assert.Equal("docker", contents.TopDirectory);
        Assert.Empty(EngineArchive.Missing(contents));
    }

    [Fact]
    public void A_tarball_with_no_shared_top_directory_is_refused()
    {
        // --strip-components=1 on this would scatter the binaries across /usr/local/bin's parent.
        var path = Write("flat.tgz", TarballBytes(top: "docker", loose: "README"));

        var contents = EngineArchive.Read(path);

        Assert.Null(contents.TopDirectory);
    }

    [Fact]
    public void A_file_that_is_not_a_tarball_raises_rather_than_reporting_nothing()
    {
        var path = Write("junk.tgz", Encoding.UTF8.GetBytes("not gzip at all"));

        Assert.Throws<InvalidDataException>(() => EngineArchive.Read(path));
    }

    // The pinned artefact itself is not asserted here: a test that needs a 86 MB download to mean
    // anything is a test that passes vacuously wherever the download is absent, which is every
    // clean checkout and every CI run. `dockerdesk-engine --acquire` runs this same check on the
    // real file, and its exit code is the assertion.

    // ---- path translation -------------------------------------------------------------------

    [Theory]
    [InlineData(@"C:\Users\x\docker.tgz", "/mnt/c/Users/x/docker.tgz")]
    [InlineData(@"D:\a\b\c.tar.gz", "/mnt/d/a/b/c.tar.gz")]
    public void A_windows_path_is_translated_to_the_automatic_drive_mount(
        string windows, string expected) =>
        Assert.Equal(expected, Wsl.ToDistributionPath(windows));

    [Fact]
    public void A_unc_path_has_no_drive_mount_and_is_refused() =>
        Assert.Throws<ArgumentException>(
            () => Wsl.ToDistributionPath(@"\\server\share\docker.tgz"));

    // ---- the invocations --------------------------------------------------------------------

    [Fact]
    public void The_import_names_the_owned_distribution_and_pins_wsl_version_2()
    {
        var provisioner = Provisioner(new FakeWsl(), out var paths);

        var argv = provisioner.ImportArguments(@"C:\downloads\rootfs.tar.gz");

        Assert.Equal(
            ["--import", "freewilly", paths.Distribution, @"C:\downloads\rootfs.tar.gz",
             "--version", "2"],
            argv);
    }

    [Fact]
    public void The_install_script_is_non_interactive_and_stops_at_the_first_error()
    {
        var script = EngineProvisioner.InstallScript(@"C:\downloads\docker.tgz");

        Assert.StartsWith("set -e", script, StringComparison.Ordinal);
        Assert.Contains("--no-cache", script, StringComparison.Ordinal);
        Assert.Contains("iptables", script, StringComparison.Ordinal);
        Assert.Contains("/mnt/c/downloads/docker.tgz", script, StringComparison.Ordinal);
        Assert.Contains("dockerd --version", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_filesystem_tools_are_installed_before_anything_needs_them()
    {
        // DD196. The timing is the whole argument: apk needs a writable root and a network, and the
        // moment e2fsck is wanted is precisely the moment the root has gone read-only. On
        // 29 August 2026 this distribution held a corrupt ext4 and no program able to say so, and
        // the check had to come from an unrelated Ubuntu registered on the same machine.
        var script = EngineProvisioner.InstallScript(@"C:\downloads\docker.tgz");

        // Both packages, because the split is not where it reads: e2fsck is in e2fsprogs, while
        // dumpe2fs and resize2fs are in e2fsprogs-extra.
        Assert.Contains("e2fsprogs", script, StringComparison.Ordinal);
        Assert.Contains("e2fsprogs-extra", script, StringComparison.Ordinal);

        // Asked with the command that reported it missing. `apk add` returning zero is a weaker
        // claim than the tool being on PATH, and this is a tool whose absence is only discovered on
        // the day nothing else works.
        Assert.Contains("command -v e2fsck", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_filesystem_tools_are_proved_present_after_they_are_installed()
    {
        // An ordering rather than a presence: `set -e` makes the script stop where it broke, so a
        // check placed above the install would pass on a distribution that never got the package.
        var script = EngineProvisioner.InstallScript(@"C:\downloads\docker.tgz");

        Assert.True(
            script.IndexOf("command -v e2fsck", StringComparison.Ordinal)
                > script.IndexOf("e2fsprogs-extra", StringComparison.Ordinal),
            "the proof that e2fsck is there runs before the install that puts it there");
    }

    [Fact]
    public async Task Provisioning_imports_then_installs_then_places_the_cli()
    {
        var wsl = new FakeWsl();
        wsl.Answer(0, "Ubuntu\n");            // --list --quiet: ours is not registered
        var provisioner = Provisioner(wsl, out var paths, WithRealArtefacts());

        var outcome = await provisioner.ProvisionAsync();

        Assert.True(outcome.Succeeded, outcome.Failure?.Detail);
        Assert.Equal(
            [ProvisioningStep.AcquireRootfs, ProvisioningStep.AcquireEngine,
             ProvisioningStep.InspectEngine, ProvisioningStep.AcquireCli,
             ProvisioningStep.AcquireCompose, ProvisioningStep.AcquireBuildx,
             ProvisioningStep.ImportDistribution, ProvisioningStep.InstallEngine,
             ProvisioningStep.PlaceCli, ProvisioningStep.PlaceCompose,
             ProvisioningStep.PlaceBuildx],
            outcome.Steps.Select(step => step.Step));
        Assert.NotNull(wsl.WithVerb("--import"));
        Assert.True(File.Exists(paths.DockerCli));
    }

    [Fact]
    public async Task The_two_steps_that_do_work_are_not_held_to_the_budget_written_for_a_question()
    {
        // DD122. Every wsl.exe call shared the preflight's fifteen seconds, and the provision
        // inherited it. Measured on a clean Windows 11 machine: every artefact downloaded and
        // verified, the import succeeded, and InstallEngine was killed at fifteen seconds — leaving
        // a registered distribution with no engine in it and a machine on which `docker` is not a
        // command. The message named a timeout, so it read as a hang; the remedy it printed was to
        // run the same thing again against the same budget.
        var wsl = new FakeWsl();
        wsl.Answer(0, "Ubuntu\n");            // --list --quiet: a question, and still one
        var provisioner = Provisioner(wsl, out _, WithRealArtefacts());

        var outcome = await provisioner.ProvisionAsync();
        Assert.True(outcome.Succeeded, outcome.Failure?.Detail);

        // The import writes a virtual disk; the install cold-boots what it just wrote and untars
        // 85 MB inside it. Neither is a question, and asserting the budget rather than a number
        // keeps this about the distinction rather than about five minutes.
        Assert.Equal(WslBudget.Work, wsl.BudgetForVerb("--import"));
        Assert.Equal(WslBudget.Work, wsl.BudgetForVerb("-d"));

        // And the reads keep the short one, which is the half that makes it a fix rather than a
        // larger constant: a preflight that waits minutes to report a stuck machine is not one.
        Assert.Equal(WslBudget.Probe, wsl.BudgetForVerb("--list"));
        Assert.True(WslBudget.Probe < WslBudget.Work, "the two budgets are the same number again");
    }

    [Fact]
    public void The_sentence_about_a_budget_names_the_budget_that_was_exceeded()
    {
        // With one constant it could not: "did not finish within 15 seconds" read the same whether
        // it was a question nothing answered or an unpack that needed a minute, so a log could not
        // tell a slow machine from a stuck one. The unit follows the budget, because "300 seconds"
        // is a number a reader converts before it means anything.
        Assert.Equal("15 seconds", Core.Preflight.Windows.ConsoleTool.Spell(WslBudget.Probe));
        Assert.Equal("5 minutes", Core.Preflight.Windows.ConsoleTool.Spell(WslBudget.Work));
    }

    [Fact]
    public async Task Both_plugins_land_where_the_cli_looks_for_one()
    {
        // DD73 and DD74. A subcommand exists only where a plugin named for it is under a config
        // directory the CLI was pointed at, so the name and the directory are both the contract and
        // neither is this project's to choose. Measured against the real CLI 29.7.2: with these two
        // files in place, `docker --help` lists `buildx*` and `compose*`, and a Dockerfile carrying
        // `RUN --mount=type=cache` builds instead of failing on the mount option.
        var wsl = new FakeWsl();
        wsl.Answer(0, "Ubuntu\n");
        var provisioner = Provisioner(wsl, out var paths, WithRealArtefacts());

        var outcome = await provisioner.ProvisionAsync();

        Assert.True(outcome.Succeeded, outcome.Failure?.Detail);
        Assert.True(File.Exists(paths.ComposePlugin), $"{paths.ComposePlugin} is not there");
        Assert.True(File.Exists(paths.BuildxPlugin), $"{paths.BuildxPlugin} is not there");

        // `docker-<name>.exe` is what makes the subcommand `<name>`. Renaming either file renames
        // the verb, which is why the names are asserted and not just the presence.
        Assert.Equal("docker-compose.exe", System.IO.Path.GetFileName(paths.ComposePlugin));
        Assert.Equal("docker-buildx.exe", System.IO.Path.GetFileName(paths.BuildxPlugin));
        Assert.Equal(
            System.IO.Path.Combine(paths.ConfigDirectory, "cli-plugins"), paths.PluginsDirectory);

        // Not the user's own %USERPROFILE%\.docker, which is the directory this project has
        // refused to write since DD32.
        Assert.StartsWith(paths.Root, paths.ComposePlugin, StringComparison.Ordinal);
        Assert.StartsWith(paths.Root, paths.BuildxPlugin, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_already_registered_distribution_is_left_alone()
    {
        var wsl = new FakeWsl();
        wsl.Answer(0, "Ubuntu\r\nfreewilly\r\n");
        var provisioner = Provisioner(wsl, out _, WithRealArtefacts());

        var outcome = await provisioner.ProvisionAsync();

        Assert.True(outcome.Succeeded, outcome.Failure?.Detail);
        Assert.Null(wsl.WithVerb("--import"));
        Assert.Contains("already registered",
            outcome.Steps.Single(s => s.Step == ProvisioningStep.ImportDistribution).Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_digest_stops_before_any_wsl_command_runs()
    {
        var wsl = new FakeWsl();
        var provisioner = Provisioner(wsl, out _, _ => Encoding.UTF8.GetBytes("wrong bytes"));

        var outcome = await provisioner.ProvisionAsync();

        Assert.False(outcome.Succeeded);
        Assert.Equal(ProvisioningStep.AcquireRootfs, outcome.Failure!.Step);
        Assert.Single(outcome.Steps);
        Assert.Empty(wsl.Invocations);
    }

    [Fact]
    public async Task A_failed_import_stops_before_the_engine_is_unpacked()
    {
        var wsl = new FakeWsl();
        wsl.Answer(0, "Ubuntu\n");
        wsl.Answer(1, "There is not enough space on the disk.");
        var provisioner = Provisioner(wsl, out _, WithRealArtefacts());

        var outcome = await provisioner.ProvisionAsync();

        Assert.False(outcome.Succeeded);
        Assert.Equal(ProvisioningStep.ImportDistribution, outcome.Failure!.Step);
        Assert.Contains("exited 1", outcome.Failure.Detail, StringComparison.Ordinal);
        Assert.Contains("not enough space", outcome.Failure.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(ProvisioningStep.InstallEngine, outcome.Steps.Select(s => s.Step));
    }

    [Fact]
    public async Task A_wsl_that_never_ran_is_reported_as_that_and_not_as_an_exit_code()
    {
        var wsl = new FakeWsl();
        wsl.Answer(0, "Ubuntu\n");
        wsl.Answer(null, "", "wsl.exe did not finish within 15 seconds");
        var provisioner = Provisioner(wsl, out _, WithRealArtefacts());

        var outcome = await provisioner.ProvisionAsync();

        Assert.Equal(ProvisioningStep.ImportDistribution, outcome.Failure!.Step);
        Assert.Contains("did not finish", outcome.Failure.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("exited", outcome.Failure.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Acquire_stops_after_verifying_and_touches_no_wsl_and_no_path()
    {
        var wsl = new FakeWsl();
        var provisioner = Provisioner(wsl, out var paths, WithRealArtefacts());

        var outcome = await provisioner.AcquireAsync();

        Assert.True(outcome.Succeeded, outcome.Failure?.Detail);
        Assert.Equal(
            [ProvisioningStep.AcquireRootfs, ProvisioningStep.AcquireEngine,
             ProvisioningStep.InspectEngine, ProvisioningStep.AcquireCli,
             ProvisioningStep.AcquireCompose, ProvisioningStep.AcquireBuildx],
            outcome.Steps.Select(step => step.Step));
        Assert.Empty(wsl.Invocations);
        Assert.False(File.Exists(paths.DockerCli));
        Assert.False(File.Exists(paths.ComposePlugin));
        Assert.False(File.Exists(paths.BuildxPlugin));
    }

    [Fact]
    public async Task A_cli_archive_without_docker_exe_fails_at_the_place_step()
    {
        var wsl = new FakeWsl();
        wsl.Answer(0, "Ubuntu\n");
        var provisioner = Provisioner(wsl, out _, WithRealArtefacts(cliHasDockerExe: false));

        var outcome = await provisioner.ProvisionAsync();

        Assert.False(outcome.Succeeded);
        Assert.Equal(ProvisioningStep.PlaceCli, outcome.Failure!.Step);
        Assert.Contains("docker/docker.exe", outcome.Failure.Detail, StringComparison.Ordinal);
    }

    // ---- what a watcher is told, and when (DD119) ---------------------------------------------

    [Fact]
    public async Task Every_step_reaches_a_watcher_in_the_order_the_outcome_records_it()
    {
        // The installer draws a page off these, so a step that reaches the outcome without reaching
        // the watcher is a bar that stops short of the end on a run that worked. Asserted against
        // the outcome rather than against a written-out list: the two cannot drift apart if the
        // claim is that they are the same steps in the same order.
        var wsl = new FakeWsl();
        wsl.Answer(0, "Ubuntu\n");
        var provisioner = Provisioner(wsl, out _, WithRealArtefacts());
        var watched = new List<StepResult>();

        var outcome = await provisioner.ProvisionAsync(watched.Add);

        Assert.True(outcome.Succeeded, outcome.Failure?.Detail);
        Assert.Equal(outcome.Steps, watched);
    }

    [Fact]
    public async Task A_run_that_stops_hands_the_failing_step_over_before_it_returns()
    {
        // The line the installer leaves on screen. Reported at the moment it is decided, so the page
        // is showing the step that stopped rather than the last one that worked.
        var wsl = new FakeWsl();
        var provisioner = Provisioner(wsl, out _, WithRealArtefacts(engineDropping: "dockerd"));
        var watched = new List<StepResult>();

        var outcome = await provisioner.ProvisionAsync(watched.Add);

        Assert.False(outcome.Succeeded);
        Assert.Equal(outcome.Steps, watched);
        Assert.Equal(ProvisioningStep.InspectEngine, watched[^1].Step);
        Assert.False(watched[^1].Ok);
    }

    [Fact]
    public async Task An_artefact_that_never_verifies_is_reported_and_not_only_returned()
    {
        // The acquisition steps record themselves through a second path, and it used to be the one
        // that appended straight to the list. A watcher told about nine steps out of ten is the
        // failure this covers, and a download that cannot be verified is the likeliest tenth.
        var provisioner = Provisioner(new FakeWsl(), out _);
        var watched = new List<StepResult>();

        var outcome = await provisioner.AcquireAsync(watched.Add);

        Assert.False(outcome.Succeeded);
        Assert.Equal(outcome.Steps, watched);
        Assert.Equal(ProvisioningStep.AcquireRootfs, Assert.Single(watched).Step);
    }

    // ---- wiring -----------------------------------------------------------------------------

    /// <summary>
    /// A manifest whose digests are of bytes this test produces, so the whole pipeline runs without
    /// the network and the digest gate is still the real one.
    /// </summary>
    private EngineManifest _manifest = null!;

    private Func<string, byte[]?> WithRealArtefacts(
        bool cliHasDockerExe = true, string? engineDropping = null)
    {
        var rootfs = Encoding.UTF8.GetBytes("pretend rootfs tarball");
        var engine = TarballBytes(engineDropping);
        var cli = ZipBytes(cliHasDockerExe ? "docker/docker.exe" : "docker/dockerd.exe");

        // A bare executable, because that is how upstream publishes the plugin: no archive to open,
        // so what the place step does is a copy and a rename (DD73).
        var compose = Encoding.UTF8.GetBytes("pretend compose plugin");
        var buildx = Encoding.UTF8.GetBytes("pretend buildx plugin");

        _manifest = new EngineManifest
        {
            Rootfs = new Artefact("rootfs", "3.24.1",
                "https://example.invalid/rootfs.tar.gz", "rootfs.tar.gz", Digest(rootfs)),
            Engine = new Artefact("engine", "29.7.2",
                "https://example.invalid/docker.tgz", "docker.tgz", Digest(engine)),
            Cli = new Artefact("cli", "29.7.2",
                "https://example.invalid/docker.zip", "docker.zip", Digest(cli)),
            Compose = new Artefact("compose", "5.4.0",
                "https://example.invalid/compose.exe", "compose.exe", Digest(compose)),
            Buildx = new Artefact("buildx", "0.36.1",
                "https://example.invalid/buildx.exe", "buildx.exe", Digest(buildx)),
        };

        return url => url switch
        {
            var u when u.EndsWith("rootfs.tar.gz", StringComparison.Ordinal) => rootfs,
            var u when u.EndsWith("docker.tgz", StringComparison.Ordinal) => engine,
            var u when u.EndsWith("docker.zip", StringComparison.Ordinal) => cli,
            var u when u.EndsWith("compose.exe", StringComparison.Ordinal) => compose,
            var u when u.EndsWith("buildx.exe", StringComparison.Ordinal) => buildx,
            _ => null,
        };
    }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>
    /// A real gzip tar shaped like upstream's: every entry under one `docker/` directory, which is
    /// what --strip-components=1 removes. Real rather than a placeholder, because the archive is now
    /// read before it is unpacked and a placeholder would only ever exercise the failure path.
    /// </summary>
    private static byte[] TarballBytes(
        string? dropping = null, string top = "docker", string? loose = null)
    {
        var names = EngineArchive.RequiredBinaries
            .Concat(["ctr", "docker", "docker-init"])
            .Where(name => name != dropping)
            .Select(name => $"{top}/{name}")
            .ToList();
        if (loose is not null)
        {
            names.Add(loose);
        }

        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, leaveOpen: true))
        {
            foreach (var name in names)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes($"ELF {name}")),
                };
                tar.WriteEntry(entry);
            }
        }

        return buffer.ToArray();
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_temp, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] ZipBytes(string entryName)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry(entryName).Open();
            entry.Write(Encoding.UTF8.GetBytes("MZ pretend windows binary"));
        }

        return buffer.ToArray();
    }

    private EngineProvisioner Provisioner(FakeWsl wsl, out EnginePaths paths) =>
        Provisioner(wsl, out paths, _ => null);

    private EngineProvisioner Provisioner(
        FakeWsl wsl, out EnginePaths paths, Func<string, byte[]?> bytes)
    {
        _manifest ??= EngineManifest.Current;
        paths = new EnginePaths(Path.Combine(_temp, "install"));
        return new EngineProvisioner(
            _manifest,
            new ArtefactStore(new FakeFetcher(bytes), paths.Downloads),
            wsl,
            paths);
    }
}
