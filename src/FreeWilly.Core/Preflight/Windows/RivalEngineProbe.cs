using FreeWilly.Core.Engine;
using Microsoft.Win32;

namespace FreeWilly.Core.Preflight.Windows;

/// <summary>
/// The raw signals a rival engine leaves behind, before anything is concluded from them.
/// </summary>
/// <remarks>
/// Separated from the judging so the judging is testable. A rival engine cannot be installed on
/// demand inside a test, and this is the one row that must never be wrongly green — so what decides
/// it is a pure function of this record, and what reads Windows is the part with no decisions in it.
/// </remarks>
internal sealed record RivalSignals
{
    /// <summary>
    /// Where <c>docker</c> resolves, the way a shell would resolve it, or <see langword="null"/>
    /// when nothing on <c>PATH</c> answers to that name.
    /// </summary>
    internal string? DockerCommand { get; init; }

    /// <summary>The registered WSL distribution names.</summary>
    internal IReadOnlyList<string> Distributions { get; init; } = [];

    /// <summary>Vendor executables found where that vendor is known to install.</summary>
    internal IReadOnlyList<RivalEngine> VendorInstalls { get; init; } = [];

    /// <summary>Whether something is listening on the Engine API pipe.</summary>
    internal bool EnginePipeOpen { get; init; }

    /// <summary>
    /// Whether the thing listening on it is this tool's own engine (DD231).
    /// </summary>
    /// <remarks>
    /// The exclusion the pipe signal never had. A signal rather than something
    /// <see cref="RivalEngineProbe.Judge"/> reads for itself, so the judging stays a pure function
    /// of this record: whether a host of ours is alive is a fact about the machine, and this row is
    /// the one where a fact read inside the decision cannot be tested.
    /// </remarks>
    internal bool OurEngineServing { get; init; }

    /// <summary>
    /// Where this tool puts its own <c>docker.exe</c>, so its own CLI is never read as a rival.
    /// </summary>
    internal string OwnCliDirectory { get; init; } = string.Empty;
}

/// <summary>Finds container engines already installed, by the things this one would take over.</summary>
/// <remarks>
/// This row shipped asking where a vendor installs, and the answer moved: Docker Desktop now
/// installs per user into <c>%LOCALAPPDATA%\Programs\DockerDesktop</c>, and its engine only listens
/// while the app is running. Measured on the development machine, all three original signals — a
/// <c>%ProgramFiles%</c> path, a Rancher path, and an open pipe — said no, while <c>docker</c>
/// resolved to <c>…\Programs\DockerDesktop\resources\bin\docker.exe</c> and a <c>docker-desktop</c>
/// distribution was registered. The report printed <c>[ok] Container engine</c> and exited 0,
/// clearing an install to walk into exactly the collision this row exists to prevent (DD16).
///
/// So the question is no longer where a vendor installs but what owns the <c>docker</c> command,
/// which one <c>PATH</c> resolution answers whatever anybody's installer did this year.
/// The vendor paths and the pipe stay: they are still evidence, and evidence carried into the report
/// is what lets a user argue with it.
/// </remarks>
internal static class RivalEngineProbe
{
    /// <summary>The pipe an Engine API client connects to on Windows.</summary>
    internal const string EnginePipeName = "docker_engine";

    /// <summary>Where WSL records every registered distribution.</summary>
    private const string LxssKey = @"Software\Microsoft\Windows\CurrentVersion\Lxss";

    /// <summary>
    /// The distribution this project owns, and therefore never a rival (DD56).
    /// </summary>
    /// <remarks>
    /// This list was two names. The second was the distribution from before the rename, kept because
    /// DD55 adopted such an install where it stood and a probe reporting it as an unidentified engine
    /// would have told a user to uninstall the thing they were running — on the one row that must
    /// never be wrongly red. DD86 removed the adoption, so there is no such machine and no second
    /// name.
    ///
    /// <para>The list itself stays, and it is one entry rather than none. The rule it answers is
    /// exact-match against <see cref="Known"/>, not a substring scan, so nothing here would match
    /// <c>freewilly</c> today — but a probe that recognises every rival and never states which
    /// distribution is its own is one edit away from reporting itself, and this is where that edit
    /// would be caught.</para>
    /// </remarks>
    private static readonly string[] Ours = [Engine.EnginePaths.CurrentDistribution];

    /// <summary>
    /// What a product is called, and how it is recognised.
    /// </summary>
    /// <remarks>
    /// <c>Distributions</c> is matched exactly rather than by substring. That is still worth doing —
    /// <c>docker-desktop</c> and <c>docker-desktop-data</c> are two names one rule should not blur —
    /// but it is no longer what keeps this product from reporting itself. <see cref="Ours"/> is.
    /// </remarks>
    private static readonly (string Name, string[] PathMarkers, string[] Distributions)[] Known =
    [
        ("Docker Desktop",
            ["DockerDesktop", @"Docker\Docker"],
            ["docker-desktop", "docker-desktop-data"]),
        ("Rancher Desktop",
            ["Rancher Desktop", "rancher-desktop"],
            ["rancher-desktop", "rancher-desktop-data"]),
        ("Podman", ["Podman", "podman"], ["podman-machine-default"]),
        ("minikube", ["minikube"], []),
    ];

    /// <summary>Read what is already here.</summary>
    /// <returns>One entry per engine found, each carrying the evidence it was found by.</returns>
    internal static IReadOnlyList<RivalEngine> Read() => Judge(ReadSignals());

    /// <summary>
    /// Conclude what is installed from the signals. Pure, and the whole of the decision.
    /// </summary>
    /// <param name="signals">What was read off the machine.</param>
    /// <returns>One entry per product, its evidence joined.</returns>
    internal static IReadOnlyList<RivalEngine> Judge(RivalSignals signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        // Ordered, and by product: Docker Desktop found by a path, a distribution and the command
        // is one row a user can act on, not three. The order a signal was added in is the order its
        // evidence reads in, which puts the strongest first.
        var evidence = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var order = new List<string>();

        void Note(string name, string because)
        {
            if (!evidence.TryGetValue(name, out var list))
            {
                evidence[name] = list = [];
                order.Add(name);
            }

            if (!list.Contains(because, StringComparer.Ordinal))
            {
                list.Add(because);
            }
        }

        // 1. What owns the docker command. The signal the shipped probe never asked for, and the
        //    only one that survives a vendor moving where it installs.
        if (signals.DockerCommand is { Length: > 0 } docker && !IsOurs(docker, signals))
        {
            Note(NameFor(docker), $"docker resolves to {docker}");
        }

        // 2. A registered distribution, which survives the app being shut down — the state the
        //    original probe read as an empty machine. This project's own is removed first and by
        //    name (DD56), so no entry added to Known later can make the engine report itself and
        //    tell the user to uninstall the thing they are running.
        var foreign = signals.Distributions.Where(registered => !IsOurDistribution(registered));
        foreach (var (name, _, distributions) in Known)
        {
            foreach (var distribution in distributions)
            {
                if (foreign.Any(registered =>
                        registered.Trim().Equals(distribution, StringComparison.OrdinalIgnoreCase)))
                {
                    Note(name, $"a registered {distribution} WSL distribution");
                }
            }
        }

        // 3. The vendor paths, still evidence.
        foreach (var install in signals.VendorInstalls)
        {
            foreach (var signal in install.Signals)
            {
                Note(install.Name, signal);
            }
        }

        // 4. The pipe. Only ever its own row when nothing above identified anything: an engine
        //    nobody recognises still owns the one endpoint a client can reach.
        //
        //    Not when the engine holding it is ours (DD231). Signals 1 and 2 each drop this
        //    project's own before judging, and this one did not: measured on a machine with the
        //    tool's engine up, the row came back red saying an unidentified engine held the pipe and
        //    telling the user to uninstall it, which is the failure DD56's remark says must never
        //    happen. A host of ours being alive is what makes the pipe explained rather than
        //    evidence, so it is dropped from the rival's row too and not only from its own.
        if (signals.EnginePipeOpen && !signals.OurEngineServing)
        {
            if (order.Count == 0)
            {
                Note("an unidentified engine", $@"\\.\pipe\{EnginePipeName} is open");
            }
            else
            {
                Note(order[0], $@"\\.\pipe\{EnginePipeName} is open");
            }
        }

        // Kept as a list, not joined (DD52): every item here is a path, a pipe or a distribution name,
        // and which of them may share a line is the report's decision to make, not this one's.
        return [.. order.Select(name => new RivalEngine(name, evidence[name]))];
    }

    /// <summary>
    /// Resolve a bare command name the way a Windows shell resolves it.
    /// </summary>
    /// <param name="command">The name, without an extension.</param>
    /// <param name="path">The <c>PATH</c> to search, semicolon separated.</param>
    /// <param name="pathExt">The <c>PATHEXT</c> extensions to try, semicolon separated.</param>
    /// <returns>The full path of the first match, or <see langword="null"/>.</returns>
    /// <remarks>
    /// In process rather than by running <c>where.exe</c>: a subprocess costs more than this read
    /// and its output is one more thing to parse. Extensions are tried in <c>PATHEXT</c> order
    /// within each directory, which is the order cmd uses — and it matters, because a directory can
    /// hold both an extensionless <c>docker</c> and a <c>docker.exe</c>, and the resolved path
    /// should be the one that would actually run.
    /// </remarks>
    internal static string? ResolveOnPath(string command, string? path, string? pathExt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        // The set cmd falls back to when PATHEXT is unset.
        var extensions = (string.IsNullOrWhiteSpace(pathExt) ? ".COM;.EXE;.BAT;.CMD" : pathExt)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var directory in path.Split(
            ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // A PATH entry may be quoted, and cmd strips the quotes before using it.
            var folder = directory.Trim('"');
            if (folder.Length == 0)
            {
                continue;
            }

            foreach (var extension in extensions)
            {
                try
                {
                    var candidate = Path.Combine(folder, command + extension);
                    if (File.Exists(candidate))
                    {
                        // Spelled as the filesystem spells it, not as PATHEXT spells it. PATHEXT is
                        // conventionally uppercase, so building the answer out of it produced
                        // `docker.EXE` for a file called `docker.exe` — and this string is evidence
                        // in a report somebody is meant to check against what `where docker` says.
                        return Path.GetFullPath(OnDiskName(folder, candidate));
                    }
                }
                catch (Exception exception) when (exception is ArgumentException
                    or NotSupportedException or PathTooLongException or IOException
                    or UnauthorizedAccessException)
                {
                    // A malformed PATH entry is one directory that cannot hold the answer, not a
                    // reason to stop looking through the rest.
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The name the filesystem holds for a file that is known to exist, case included.
    /// </summary>
    /// <param name="folder">The directory it is in.</param>
    /// <param name="candidate">The path as it was constructed.</param>
    /// <returns>The real path, or <paramref name="candidate"/> where it cannot be recovered.</returns>
    private static string OnDiskName(string folder, string candidate)
    {
        try
        {
            // The pattern match is case-insensitive on Windows and what comes back is the entry as
            // stored. Only reached on a hit, so it costs one enumeration per rival found.
            return Directory.EnumerateFiles(folder, Path.GetFileName(candidate)).FirstOrDefault()
                ?? candidate;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or ArgumentException)
        {
            return candidate;
        }
    }

    /// <summary>Whether something is listening on the Engine API pipe.</summary>
    /// <returns><see langword="true"/> when the pipe exists.</returns>
    internal static bool EnginePipeExists()
    {
        try
        {
            // Enumeration rather than File.Exists: the pipe filesystem answers a directory listing
            // reliably and answers an existence probe differently depending on the pipe's ACL.
            return Directory
                .EnumerateFileSystemEntries(@"\\.\pipe\")
                .Any(entry => Path.GetFileName(entry)
                    .Equals(EnginePipeName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The registered WSL distributions, read where WSL records them.</summary>
    /// <returns>Their names, or empty where the key is absent or unreadable.</returns>
    /// <remarks>
    /// The read itself moved to <see cref="Wsl.RegisteredDistributions"/> when DD55 gave it a second
    /// caller. Unreadable arriving as empty is not the same thing, but this row cannot report Unknown
    /// for one of four signals, and the other three still answer — which is why there are four.
    /// </remarks>
    internal static IReadOnlyList<string> ReadDistributions() => Wsl.RegisteredDistributions();

    /// <summary>Whether a registered distribution is one this project owns, now or before (DD56).</summary>
    /// <param name="registered">A name as WSL reports it.</param>
    /// <returns><see langword="true"/> where this tool owns it.</returns>
    internal static bool IsOurDistribution(string registered) =>
        Ours.Any(name => registered.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a resolved docker command is this tool's own.</summary>
    private static bool IsOurs(string docker, RivalSignals signals)
    {
        if (signals.OwnCliDirectory.Length == 0)
        {
            return false;
        }

        // DD14 puts this tool's own bin directory on PATH, so after an install `docker` resolves
        // here. Without this, the fix for a wrongly green row would make it wrongly red on every
        // machine where the product is working.
        var ours = Path.GetDirectoryName(docker) ?? string.Empty;
        return ours.TrimEnd(Path.DirectorySeparatorChar).Equals(
            signals.OwnCliDirectory.TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Which product a path belongs to, or a name that admits it is not known.</summary>
    private static string NameFor(string path)
    {
        foreach (var (name, markers, _) in Known)
        {
            if (markers.Any(marker => path.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
        }

        return "another engine";
    }

    private static RivalSignals ReadSignals()
    {
        var vendors = new List<RivalEngine>();
        AddIfPresent(vendors, "Docker Desktop", Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Docker", "Docker", "Docker Desktop.exe"));

        // Where Docker Desktop puts itself now that it installs per user. Kept alongside the
        // %ProgramFiles% path rather than replacing it: a machine can have either.
        AddIfPresent(vendors, "Docker Desktop", Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "DockerDesktop", "Docker Desktop.exe"));

        AddIfPresent(vendors, "Rancher Desktop", Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Rancher Desktop", "Rancher Desktop.exe"));

        return new RivalSignals
        {
            DockerCommand = ResolveOnPath(
                "docker",
                Environment.GetEnvironmentVariable("PATH"),
                Environment.GetEnvironmentVariable("PATHEXT")),
            Distributions = ReadDistributions(),
            VendorInstalls = vendors,
            EnginePipeOpen = EnginePipeExists(),
            OurEngineServing = EngineHostSlot.Held,
            OwnCliDirectory = new EnginePaths().CliDirectory,
        };
    }

    private static void AddIfPresent(List<RivalEngine> found, string name, string path)
    {
        if (File.Exists(path))
        {
            found.Add(new RivalEngine(name, path));
        }
    }
}
