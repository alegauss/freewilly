using System.IO.Compression;

namespace FreeWilly.Core.Engine;

/// <summary>The steps provisioning goes through, in order.</summary>
/// <remarks>
/// This runs from an installer where there is no terminal to answer a prompt in, so a failure has
/// only one way to be useful: name the step it happened at. That is what this enum is for.
/// </remarks>
public enum ProvisioningStep
{
    /// <summary>Download and verify the Linux root filesystem.</summary>
    AcquireRootfs,

    /// <summary>Download and verify the static Linux engine binaries.</summary>
    AcquireEngine,

    /// <summary>
    /// Read the engine tarball's member list and check the binaries it must carry are there.
    /// </summary>
    InspectEngine,

    /// <summary>Download and verify the archive holding the Windows CLI.</summary>
    AcquireCli,

    /// <summary>Download and verify the Compose CLI plugin.</summary>
    AcquireCompose,

    /// <summary>Download and verify the Buildx CLI plugin.</summary>
    AcquireBuildx,

    /// <summary>Import the owned WSL2 distribution from the root filesystem.</summary>
    ImportDistribution,

    /// <summary>Unpack the engine binaries inside the distribution and configure it.</summary>
    InstallEngine,

    /// <summary>Put <c>docker.exe</c> where an installer can add it to PATH.</summary>
    PlaceCli,

    /// <summary>Put the Compose plugin where the CLI looks for one.</summary>
    PlaceCompose,

    /// <summary>Put the Buildx plugin where the CLI looks for one.</summary>
    PlaceBuildx,
}

/// <summary>What one step did.</summary>
/// <param name="Step">Which step.</param>
/// <param name="Ok">Whether it succeeded.</param>
/// <param name="Detail">What happened, in one line — the path, the version, or the error.</param>
public sealed record StepResult(ProvisioningStep Step, bool Ok, string Detail);

/// <summary>Every step attempted, and whether the whole thing worked.</summary>
/// <param name="Steps">The steps, in the order they ran.</param>
public sealed record ProvisioningOutcome(IReadOnlyList<StepResult> Steps)
{
    /// <summary>Whether every attempted step succeeded and none was skipped by a failure.</summary>
    public bool Succeeded => Steps.Count > 0 && Steps.All(step => step.Ok);

    /// <summary>The step that failed, or <see langword="null"/>.</summary>
    public StepResult? Failure => Steps.FirstOrDefault(step => !step.Ok);
}

/// <summary>
/// Puts upstream Moby into a WSL2 distribution this tool owns, unattended.
/// </summary>
/// <remarks>
/// Stops at the first failing step rather than pressing on: every step after an import that did not
/// happen would fail for a reason that is not its own, and a report naming six failures where there
/// was one is a report nobody can act on.
/// </remarks>
public sealed class EngineProvisioner
{
    private readonly EngineManifest _manifest;
    private readonly ArtefactStore _store;
    private readonly IWsl _wsl;
    private readonly EnginePaths _paths;

    /// <summary>Construct a provisioner.</summary>
    /// <param name="manifest">The pinned artefacts.</param>
    /// <param name="store">Where artefacts are acquired and verified.</param>
    /// <param name="wsl">The WSL command.</param>
    /// <param name="paths">Where things are installed.</param>
    public EngineProvisioner(
        EngineManifest manifest, ArtefactStore store, IWsl wsl, EnginePaths paths)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(wsl);
        ArgumentNullException.ThrowIfNull(paths);
        _manifest = manifest;
        _store = store;
        _wsl = wsl;
        _paths = paths;
    }

    /// <summary>
    /// Download and verify every artefact, and stop. The half that needs no WSL2 and changes
    /// nothing outside this tool's own directory.
    /// </summary>
    /// <param name="report">Called with each step as it lands, or <see langword="null"/>.</param>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>The three acquisition steps.</returns>
    public Task<ProvisioningOutcome> AcquireAsync(
        Action<StepResult>? report = null, CancellationToken cancellation = default) =>
        RunAsync(installing: false, report, cancellation);

    /// <summary>Acquire, import the distribution, install the engine and place the CLI.</summary>
    /// <param name="report">Called with each step as it lands, or <see langword="null"/>.</param>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>Every step attempted.</returns>
    /// <remarks>
    /// <paramref name="report"/> hands over the same records the outcome carries, at the moment each
    /// one is decided rather than at the end (DD119). Nothing here needs it — the outcome is still
    /// the whole story — but a caller drawing a window does: the download alone is a quarter of a
    /// gigabyte, and a wizard page with nothing on it for minutes reads as a hang.
    ///
    /// <para>A delegate and not <see cref="IProgress{T}"/>, which is the type this looks like it
    /// wants. <c>Progress&lt;T&gt;</c> posts to the synchronization context it was constructed on and
    /// falls back to the thread pool where there is none — so from a console verb, which has none,
    /// the reports arrive in whatever order the pool runs them. An ordered list of steps that can be
    /// printed out of order is worse than no reporting at all. This is called inline, on the thread
    /// the step ran on, and the order is therefore the step order.</para>
    /// </remarks>
    public Task<ProvisioningOutcome> ProvisionAsync(
        Action<StepResult>? report = null, CancellationToken cancellation = default) =>
        RunAsync(installing: true, report, cancellation);

    private async Task<ProvisioningOutcome> RunAsync(
        bool installing, Action<StepResult>? report, CancellationToken cancellation)
    {
        var steps = new List<StepResult>();
        _paths.Create();

        var rootfs = await Acquire(
            steps, report, ProvisioningStep.AcquireRootfs, _manifest.Rootfs, cancellation)
            .ConfigureAwait(false);
        if (rootfs is null)
        {
            return new ProvisioningOutcome(steps);
        }

        var engine = await Acquire(
            steps, report, ProvisioningStep.AcquireEngine, _manifest.Engine, cancellation)
            .ConfigureAwait(false);
        if (engine is null)
        {
            return new ProvisioningOutcome(steps);
        }

        if (!Record(steps, report, InspectEngine(engine)))
        {
            return new ProvisioningOutcome(steps);
        }

        var cli = await Acquire(
            steps, report, ProvisioningStep.AcquireCli, _manifest.Cli, cancellation)
            .ConfigureAwait(false);
        if (cli is null)
        {
            return new ProvisioningOutcome(steps);
        }

        var compose = await Acquire(
            steps, report, ProvisioningStep.AcquireCompose, _manifest.Compose, cancellation)
            .ConfigureAwait(false);
        if (compose is null)
        {
            return new ProvisioningOutcome(steps);
        }

        var buildx = await Acquire(
            steps, report, ProvisioningStep.AcquireBuildx, _manifest.Buildx, cancellation)
            .ConfigureAwait(false);
        if (buildx is null || !installing)
        {
            return new ProvisioningOutcome(steps);
        }

        // DD269. Everything above this line reads the network and writes under the Windows root, and
        // a start running alongside it is unaffected. These two are the ones that touch the
        // distribution: the import creates it, and the install rewrites every binary a daemon is
        // about to exec. Held across both rather than around the install alone, because a start
        // launched against a half-imported distribution is the same defect one step earlier.
        //
        // Not a using on the whole method, so the lock is released the moment the last write lands
        // rather than at the end of the placements that follow, which are Windows-side files.
        using (EngineUnpack.Hold(_paths.UnpackLock))
        {
            if (!Record(steps, report, ImportDistribution(rootfs)))
            {
                return new ProvisioningOutcome(steps);
            }

            if (!Record(steps, report, InstallEngine(engine)))
            {
                return new ProvisioningOutcome(steps);
            }
        }

        if (!Record(steps, report, PlaceCli(cli)))
        {
            return new ProvisioningOutcome(steps);
        }

        if (!Record(steps, report, PlacePlugin(
            ProvisioningStep.PlaceCompose, _manifest.Compose, compose, _paths.ComposePlugin)))
        {
            return new ProvisioningOutcome(steps);
        }

        Record(steps, report, PlacePlugin(
            ProvisioningStep.PlaceBuildx, _manifest.Buildx, buildx, _paths.BuildxPlugin));
        return new ProvisioningOutcome(steps);
    }

    /// <summary>Append a step, report it, and say whether the run continues.</summary>
    /// <remarks>
    /// One place, so a step cannot reach the outcome without also reaching whoever is watching. The
    /// alternative — reporting at each call site — is a step that lands silently the first time
    /// somebody adds one.
    /// </remarks>
    private static bool Record(
        List<StepResult> steps, Action<StepResult>? report, StepResult result)
    {
        steps.Add(result);
        report?.Invoke(result);
        return result.Ok;
    }

    private async Task<string?> Acquire(
        List<StepResult> steps,
        Action<StepResult>? report,
        ProvisioningStep step,
        Artefact artefact,
        CancellationToken cancellation)
    {
        var acquired = await _store.AcquireAsync(artefact, cancellation).ConfigureAwait(false);
        if (!acquired.Verified)
        {
            Record(steps, report,
                new StepResult(step, false, acquired.Failure ?? "no file and no reason"));
            return null;
        }

        var how = acquired.Cached ? "already verified on disk" : "downloaded and verified";
        Record(steps, report, new StepResult(
            step, true, $"{artefact.Id} {artefact.Version}, {how}: {acquired.Path}"));
        return acquired.Path;
    }

    /// <summary>The arguments that import the owned distribution. Non-interactive by construction.</summary>
    /// <param name="rootfsPath">The verified root filesystem tarball.</param>
    /// <returns>The argument list handed to wsl.exe.</returns>
    public string[] ImportArguments(string rootfsPath) =>
    [
        "--import",
        _paths.DistributionName,
        _paths.Distribution,
        rootfsPath,
        "--version",
        "2",
    ];

    /// <summary>The shell script run inside the distribution to install the engine.</summary>
    /// <param name="engineTarballPath">The verified engine tarball, as a Windows path.</param>
    /// <returns>The script.</returns>
    public static string InstallScript(string engineTarballPath)
    {
        var inside = Wsl.ToDistributionPath(engineTarballPath);

        // One script, every command non-interactive, and `set -e` so it stops where it broke rather
        // than reporting the last command's status. dockerd needs iptables, which a minimal root
        // filesystem has no copy of, so apk fetches it — which is also why wsl.conf leaves
        // generateResolvConf on: WSL's own DNS is what makes that fetch resolve. systemd stays off
        // because nothing here is a service yet; starting the engine is its own task.
        return string.Join(" && ",
        [
            "set -e",
            // socat is what carries the Engine API out to Windows: a Linux daemon cannot create a
            // Windows named pipe, and every IP route to it is reachable by any local process, so the
            // hop is over wsl.exe's stdio and needs a tool that speaks unix sockets. BusyBox's nc
            // does not — measured: its usage line offers HOST PORT and no -U.
            //
            // e2fsprogs is here for a failure rather than for a feature (DD196), and the timing is
            // the whole argument. apk needs a writable root and a network, and the moment e2fsck is
            // wanted is precisely the moment the root has gone read-only — so a package fetched on
            // demand is a download onto a filesystem that cannot accept it. On 29 August 2026 this
            // distribution held a corrupt ext4 and no program able to say so, and the check had to
            // come from an unrelated Ubuntu that happened to be registered on the same machine.
            //
            // Both packages, because the split is not where it reads: e2fsck is in e2fsprogs, while
            // dumpe2fs and resize2fs are in e2fsprogs-extra (checked against Alpine's own contents
            // index). A few megabytes once, and the remedy DD190 prints has something to run.
            "apk add --no-cache --no-progress iptables ip6tables ca-certificates socat "
            + "e2fsprogs e2fsprogs-extra",
            "mkdir -p /usr/local/bin",
            // --no-same-owner, because tar as root otherwise restores the uid the archive was built
            // with: measured on a real install, every binary landed owned by 1001:1001, a user this
            // distribution does not have. Harmless today and a trap the first time one exists.
            $"tar -xzf '{inside}' -C /usr/local/bin --strip-components=1 --no-same-owner",
            // A glob, not eight names: naming them makes `set -e` abort the whole install the day
            // upstream renames or drops one, and which binaries have to be there is already decided
            // locally by InspectEngine, before the distribution is touched.
            "chmod 0755 /usr/local/bin/*",
            "printf '[boot]\\nsystemd=false\\n[network]\\ngenerateResolvConf=true\\n' > /etc/wsl.conf",
            "/usr/local/bin/dockerd --version",
            // Asked with the command that reported it missing (DD196). `apk add` succeeding is not
            // the same claim: a mirror can serve a package that installs nothing useful, and this is
            // a tool whose absence is only discovered on the day nothing else works.
            "command -v e2fsck",
        ]);
    }

    private StepResult InspectEngine(string engineTarballPath)
    {
        try
        {
            var contents = EngineArchive.Read(engineTarballPath);
            if (contents.TopDirectory is null)
            {
                return new StepResult(ProvisioningStep.InspectEngine, false,
                    "the tarball's entries do not share one top directory, so unpacking it with "
                    + "--strip-components=1 would scatter them");
            }

            var missing = EngineArchive.Missing(contents);
            if (missing.Count > 0)
            {
                return new StepResult(ProvisioningStep.InspectEngine, false,
                    $"{_manifest.Engine.FileName} is missing {string.Join(", ", missing)}: "
                    + $"it carries {contents.Binaries.Count} file(s) under {contents.TopDirectory}/");
            }

            return new StepResult(ProvisioningStep.InspectEngine, true,
                $"{contents.Binaries.Count} binaries under {contents.TopDirectory}/, "
                + $"including {string.Join(", ", EngineArchive.RequiredBinaries)}");
        }
        catch (Exception exception) when (exception is IOException
            or InvalidDataException or UnauthorizedAccessException)
        {
            return new StepResult(ProvisioningStep.InspectEngine, false,
                $"reading {_manifest.Engine.FileName} failed: {exception.Message}");
        }
    }

    private StepResult ImportDistribution(string rootfsPath)
    {
        if (DistributionExists())
        {
            // Left exactly where WSL registered it. Re-importing a distribution that is already
            // there would copy gigabytes and has a failure mode in the middle that loses every image
            // and volume in it.
            return new StepResult(ProvisioningStep.ImportDistribution, true,
                $"{_paths.DistributionName} is already registered, left as it is");
        }

        // Work, not a question (DD122): this writes a virtual disk from a rootfs tarball, and on a
        // machine where WSL2 has not run yet it starts the subsystem to do it.
        var result = _wsl.Run(WslBudget.Work, ImportArguments(rootfsPath));
        return result.Succeeded
            ? new StepResult(ProvisioningStep.ImportDistribution, true,
                $"{_paths.DistributionName} imported into {_paths.Distribution}")
            : new StepResult(ProvisioningStep.ImportDistribution, false,
                Explain($"importing {_paths.DistributionName}", result));
    }

    private StepResult InstallEngine(string engineTarballPath)
    {
        // The step DD122 was filed for, and the slowest call this product makes. The distribution
        // was imported a moment ago and has never been booted, so this pays for a cold start and
        // then untars 85 MB of engine onto a disk being grown as it is written.
        var result = _wsl.Run(
            WslBudget.Work,
            "-d", _paths.DistributionName, "-u", "root", "--",
            "/bin/sh", "-c", InstallScript(engineTarballPath));

        return result.Succeeded
            ? new StepResult(ProvisioningStep.InstallEngine, true,
                $"engine {_manifest.Engine.Version} unpacked into "
                + $"{_paths.DistributionName}:/usr/local/bin")
            : new StepResult(ProvisioningStep.InstallEngine, false,
                Explain("installing the engine binaries", result));
    }

    private StepResult PlaceCli(string cliZipPath)
    {
        const string entryName = "docker/docker.exe";
        try
        {
            using var archive = ZipFile.OpenRead(cliZipPath);
            var entry = archive.GetEntry(entryName)
                ?? throw new InvalidOperationException(
                    $"{Path.GetFileName(cliZipPath)} has no {entryName}");

            Directory.CreateDirectory(_paths.VendorCliDirectory);
            entry.ExtractToFile(_paths.DockerCli, overwrite: true);

            return new StepResult(ProvisioningStep.PlaceCli, true,
                $"docker {_manifest.Cli.Version} at {_paths.DockerCli}");
        }
        catch (Exception exception) when (exception is IOException
            or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new StepResult(ProvisioningStep.PlaceCli, false,
                $"placing the Windows CLI failed: {exception.Message}");
        }
    }

    /// <summary>
    /// Copy one CLI plugin under this install's own config directory (DD73, DD74).
    /// </summary>
    /// <remarks>
    /// A copy and not an extraction: upstream publishes both plugins as bare executables, so there
    /// is no archive to open and the only thing this does that <see cref="PlaceCli"/> does not is
    /// rename. The name is the contract — the CLI derives the subcommand from it, and placing
    /// <c>docker-buildx.exe</c> is also what makes plain <c>docker build</c> use BuildKit.
    ///
    /// <para>Here rather than in <c>%USERPROFILE%\.docker\cli-plugins</c>, which is the user's own
    /// directory and the one this project has refused to write since DD32. The cost is stated rather
    /// than hidden: a plain <c>docker compose</c> in a shell still finds nothing, and what closes
    /// that is a <c>DOCKER_CONFIG</c> the user sets themselves.</para>
    ///
    /// <para>One method for both, because the second plugin introduced no decision — where a plugin
    /// goes was settled by the first, and a copy of this per plugin is where the two would drift.</para>
    /// </remarks>
    /// <param name="step">Which step this is, so a failure names it.</param>
    /// <param name="artefact">The manifest entry, for its version.</param>
    /// <param name="downloaded">The verified file on disk.</param>
    /// <param name="target">Where it goes, named for the subcommand it becomes.</param>
    private StepResult PlacePlugin(
        ProvisioningStep step, Artefact artefact, string downloaded, string target)
    {
        try
        {
            Directory.CreateDirectory(_paths.PluginsDirectory);
            File.Copy(downloaded, target, overwrite: true);

            return new StepResult(step, true, $"{artefact.Id} {artefact.Version} at {target}");
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or NotSupportedException)
        {
            return new StepResult(
                step, false, $"placing the {artefact.Id} plugin failed: {exception.Message}");
        }
    }

    private bool DistributionExists()
    {
        var listed = _wsl.Run("--list", "--quiet");
        if (!listed.Succeeded)
        {
            return false;
        }

        return listed.Output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Trim().Equals(
                _paths.DistributionName, StringComparison.OrdinalIgnoreCase));
    }

    private static string Explain(string what, WslResult result)
    {
        if (result.Failure is not null)
        {
            return $"{what} failed: {result.Failure}";
        }

        var said = result.Output.Trim();
        var because = said.Length == 0 ? "and said nothing" : $"saying: {Shorten(said)}";
        return $"{what} exited {result.ExitCode} {because}";
    }

    private static string Shorten(string text)
    {
        var oneLine = string.Join(" ", text.Split(
            ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return oneLine.Length <= 300 ? oneLine : oneLine[..300] + "…";
    }
}
