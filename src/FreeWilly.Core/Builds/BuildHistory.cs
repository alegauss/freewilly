using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FreeWilly.Core.Engine;

namespace FreeWilly.Core.Builds;

/// <summary>One build, as the list shows it.</summary>
/// <remarks>
/// The field names are buildx's own, from <c>history ls --format json</c>, which spells them in
/// snake case — <see cref="BuildRecord"/> is the same daemon's answer to a different verb and spells
/// them in Pascal case. Two shapes because upstream has two, not because this wanted them.
/// </remarks>
public sealed record BuildSummary
{
    /// <summary>What was built, usually the context's tail.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>The address, <c>&lt;builder&gt;/&lt;node&gt;/&lt;id&gt;</c>.</summary>
    [JsonPropertyName("ref")]
    public string Reference { get; init; } = "";

    /// <summary>Completed, running or failed, in the daemon's own word (L8).</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    /// <summary>When it started.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>When it stopped, or nothing while it is still going.</summary>
    [JsonPropertyName("completed_at")]
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>How many steps there were.</summary>
    [JsonPropertyName("total_steps")]
    public int TotalSteps { get; init; }

    /// <summary>How many finished.</summary>
    [JsonPropertyName("completed_steps")]
    public int CompletedSteps { get; init; }

    /// <summary>How many came from cache.</summary>
    [JsonPropertyName("cached_steps")]
    public int CachedSteps { get; init; }

    /// <summary>The id alone, which is what a person reads and compares.</summary>
    public string Id => Reference[(Reference.LastIndexOf('/') + 1)..];

    /// <summary>How long it took, or nothing where it has not finished.</summary>
    public TimeSpan? Duration =>
        CreatedAt is { } started && CompletedAt is { } finished && finished >= started
            ? finished - started
            : null;
}

/// <summary>One material a build consumed.</summary>
/// <param name="URI">What it was, in package-url form.</param>
/// <param name="Digests">What it hashed to.</param>
public sealed record BuildMaterial(
    [property: JsonPropertyName("URI")] string URI,
    [property: JsonPropertyName("Digests")] IReadOnlyList<string>? Digests);

/// <summary>What a build was configured with.</summary>
public sealed record BuildConfig
{
    /// <summary>How image references were resolved.</summary>
    [JsonPropertyName("ImageResolveMode")]
    public string? ImageResolveMode { get; init; }

    /// <summary>Whether the cache was refused.</summary>
    [JsonPropertyName("NoCache")]
    public bool NoCache { get; init; }
}

/// <summary>One build in full, as <c>history inspect</c> answers.</summary>
public sealed record BuildRecord
{
    /// <summary>What was built.</summary>
    [JsonPropertyName("Name")]
    public string Name { get; init; } = "";

    /// <summary>The id this was looked up by.</summary>
    [JsonPropertyName("Ref")]
    public string Reference { get; init; } = "";

    /// <summary>Where it was built from, in the caller's own spelling.</summary>
    [JsonPropertyName("Context")]
    public string? Context { get; init; }

    /// <summary>Which Dockerfile.</summary>
    [JsonPropertyName("Dockerfile")]
    public string? Dockerfile { get; init; }

    /// <summary>The repository the context sat in, where it was a checkout.</summary>
    [JsonPropertyName("VCSRepository")]
    public string? VcsRepository { get; init; }

    /// <summary>The revision, which is what makes a build reproducible.</summary>
    [JsonPropertyName("VCSRevision")]
    public string? VcsRevision { get; init; }

    /// <summary>The daemon's own word for how it ended (L8).</summary>
    [JsonPropertyName("Status")]
    public string Status { get; init; } = "";

    /// <summary>When it started.</summary>
    [JsonPropertyName("StartedAt")]
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>When it stopped.</summary>
    [JsonPropertyName("CompletedAt")]
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>How long it took, in nanoseconds, which is how buildx reports it.</summary>
    [JsonPropertyName("Duration")]
    public long DurationNanoseconds { get; init; }

    /// <summary>How many steps finished.</summary>
    [JsonPropertyName("NumCompletedSteps")]
    public int CompletedSteps { get; init; }

    /// <summary>How many there were.</summary>
    [JsonPropertyName("NumTotalSteps")]
    public int TotalSteps { get; init; }

    /// <summary>How many came from cache.</summary>
    [JsonPropertyName("NumCachedSteps")]
    public int CachedSteps { get; init; }

    /// <summary>What it was configured with.</summary>
    [JsonPropertyName("Config")]
    public BuildConfig? Config { get; init; }

    /// <summary>What it consumed.</summary>
    [JsonPropertyName("Materials")]
    public IReadOnlyList<BuildMaterial>? Materials { get; init; }

    /// <summary>The id alone.</summary>
    public string Id => Reference[(Reference.LastIndexOf('/') + 1)..];

    /// <summary>How long it took.</summary>
    public TimeSpan Duration => TimeSpan.FromTicks(DurationNanoseconds / 100);
}

/// <summary>
/// Where the build history is read from, so a page can be drawn without one (L6).
/// </summary>
/// <remarks>
/// A seam and not a class, for the same reason <see cref="Api.IEngineClient"/> is one: the designed
/// empty state — a machine that has never built anything — is the hardest to reach deliberately, and
/// a page that can only be looked at by first producing the right build history is a page nobody
/// reviews.
/// </remarks>
public interface IBuildHistory
{
    /// <summary>The builds this machine remembers, newest first.</summary>
    /// <returns>The list, empty where there are none or where nothing could be asked.</returns>
    IReadOnlyList<BuildSummary> Recent();

    /// <summary>One build in full.</summary>
    /// <param name="reference">The ref, as <see cref="BuildAddress.RefIn"/> read it.</param>
    /// <returns>The record, or <see langword="null"/> where there is none.</returns>
    BuildRecord? Inspect(string reference);
}

/// <summary>
/// The build history, read through the <c>buildx</c> this install placed (DD126).
/// </summary>
/// <remarks>
/// <b>Not the Engine API.</b> The history lives behind BuildKit's own service and the only client
/// for it on this machine is the Buildx plugin DD74 pinned — so this shells out, the same way
/// <see cref="Agent.BundledComposeCli"/> does and for the same reason: the executable beside our own
/// install is the one that talks to our own pipe, and <c>DOCKER_CONFIG</c> is what makes
/// <c>buildx</c> a subcommand of it at all.
///
/// <para><b>Read-only, and short.</b> Both verbs answer from a local record, so the deadline is
/// seconds rather than the minutes a build needs. A window redraw must not be able to hang on this.
/// </para>
/// </remarks>
public sealed class BuildHistory : IBuildHistory
{
    /// <summary>How long either read may take before it is given up on.</summary>
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);

    /// <summary>How many builds the list keeps.</summary>
    /// <remarks>
    /// Applied here and not by the CLI, because <c>history ls</c> has no limit flag — measured
    /// against the pinned Buildx, whose only narrowing options are <c>--filter</c> and
    /// <c>--local</c>. So the cap is this side's, which also means it is a number that cannot change
    /// under the page when the plugin is bumped.
    /// </remarks>
    public const int Limit = 100;

    private readonly string _docker;
    private readonly string _config;

    /// <summary>Construct against this install's own CLI.</summary>
    public BuildHistory()
        : this(new EnginePaths())
    {
    }

    /// <summary>Construct against a layout.</summary>
    /// <param name="paths">The install this runs out of.</param>
    public BuildHistory(EnginePaths paths)
        : this(
            (paths ?? throw new ArgumentNullException(nameof(paths))).DockerCli,
            paths.ConfigDirectory)
    {
    }

    /// <summary>Construct against an explicit executable and config directory.</summary>
    /// <param name="dockerCli">The <c>docker.exe</c> to run.</param>
    /// <param name="configDirectory">What <c>DOCKER_CONFIG</c> is set to for the child.</param>
    public BuildHistory(string dockerCli, string configDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dockerCli);
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        _docker = dockerCli;
        _config = configDirectory;
    }

    /// <inheritdoc/>
    public IReadOnlyList<BuildSummary> Recent()
    {
        var output = Run("buildx", "history", "ls", "--format", "json");
        return output is null ? [] : [.. ReadList(output).Take(Limit)];
    }

    /// <inheritdoc/>
    public BuildRecord? Inspect(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        var output = Run("buildx", "history", "inspect", reference, "--format", "json");
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<BuildRecord>(output);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Read the list, which arrives as one JSON object per line rather than as an array.
    /// </summary>
    /// <remarks>
    /// Buildx streams it, so there is no enclosing bracket and a whole-document parse fails on the
    /// second record. A line that does not parse is skipped rather than failing the list: one
    /// unreadable entry must not be able to empty a page.
    /// </remarks>
    /// <param name="output">What the CLI printed.</param>
    /// <returns>The builds it named.</returns>
    internal static IReadOnlyList<BuildSummary> ReadList(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var builds = new List<BuildSummary>();
        foreach (var line in output.Split('\n'))
        {
            var text = line.Trim();
            if (text.Length == 0 || text[0] != '{')
            {
                continue;
            }

            try
            {
                if (JsonSerializer.Deserialize<BuildSummary>(text) is { } build
                    && build.Reference.Length > 0)
                {
                    builds.Add(build);
                }
            }
            catch (JsonException)
            {
                // One malformed line is one missing row, not an empty page.
            }
        }

        return builds;
    }

    /// <summary>Run the CLI and answer its stdout, or null where it did not succeed.</summary>
    private string? Run(params string[] arguments)
    {
        if (!File.Exists(_docker))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo(_docker)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,

            // Named and not inherited (DD261). Every argument this passes is absolute, so there is
            // nothing for a caller's directory to resolve and nothing gained by locking it.
            WorkingDirectory = Environment.SystemDirectory,
        };

        // The plugin sits under this install's own config directory and the CLI finds one nowhere
        // else this project may write (DD73), so without this `buildx` is not a subcommand at all.
        startInfo.Environment[DockerConfigEntry.Variable] = _config;

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)Deadline.TotalMilliseconds))
            {
                Kill(process);
                return null;
            }

            Task.WaitAll(stdout, stderr);

            // stdout only. A failure's words are on stderr and belong to the empty state, not to a
            // JSON parse that would then be reading an error message as a build.
            return process.ExitCode == 0 ? stdout.Result : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // It exited between the wait and the kill.
        }
    }
}
