using System.Text.Json.Serialization;

namespace FreeWilly.Core.Api;

/// <summary>What the daemon says about itself.</summary>
/// <remarks>
/// Five fields out of a response with thirty. The client is typed only where a field is read: a
/// generated model of an API surface this tool will use a tenth of is a maintenance cost with no
/// reader.
/// </remarks>
public sealed record EngineVersion
{
    /// <summary>The engine version, e.g. <c>29.7.2</c>.</summary>
    [JsonPropertyName("Version")]
    public string Version { get; init; } = "";

    /// <summary>The newest API version this daemon speaks, e.g. <c>1.55</c>.</summary>
    [JsonPropertyName("ApiVersion")]
    public string ApiVersion { get; init; } = "";

    /// <summary>The oldest API version it still answers.</summary>
    [JsonPropertyName("MinAPIVersion")]
    public string MinApiVersion { get; init; } = "";

    /// <summary>The daemon's operating system — <c>linux</c> for an engine in WSL2.</summary>
    [JsonPropertyName("Os")]
    public string Os { get; init; } = "";

    /// <summary>Its architecture.</summary>
    [JsonPropertyName("Arch")]
    public string Arch { get; init; } = "";
}

/// <summary>One published port.</summary>
public sealed record PortBinding
{
    /// <summary>The address on the host, when the port is published.</summary>
    [JsonPropertyName("IP")]
    public string? Ip { get; init; }

    /// <summary>The port inside the container.</summary>
    [JsonPropertyName("PrivatePort")]
    public int PrivatePort { get; init; }

    /// <summary>The port on the host, absent when nothing is published.</summary>
    [JsonPropertyName("PublicPort")]
    public int? PublicPort { get; init; }

    /// <summary>tcp or udp.</summary>
    [JsonPropertyName("Type")]
    public string Type { get; init; } = "";

    /// <summary>How a list renders this row — <c>8080->80/tcp</c>, or just the private port.</summary>
    public override string ToString() => PublicPort is { } published
        ? $"{published}->{PrivatePort}/{Type}"
        : $"{PrivatePort}/{Type}";
}

/// <summary>One thing a container has mounted.</summary>
public sealed record MountPoint
{
    /// <summary><c>volume</c>, <c>bind</c> or <c>tmpfs</c>.</summary>
    [JsonPropertyName("Type")]
    public string Type { get; init; } = "";

    /// <summary>The volume's name, for a volume mount. Empty for a bind.</summary>
    [JsonPropertyName("Name")]
    public string Name { get; init; } = "";

    /// <summary>Where it is mounted inside the container.</summary>
    [JsonPropertyName("Destination")]
    public string Destination { get; init; } = "";
}

/// <summary>What a volume costs on disk, when the daemon has been asked to work it out.</summary>
public sealed record VolumeUsage
{
    /// <summary>
    /// Bytes, or -1 when nobody has measured it.
    /// </summary>
    /// <remarks>
    /// The plain volume list always answers -1. Only <c>/system/df</c> walks the filesystem, and it
    /// is slow enough that the two are separate calls rather than one.
    /// </remarks>
    [JsonPropertyName("Size")]
    public long Size { get; init; } = -1;

    /// <summary>How many containers reference it, or -1 when unmeasured.</summary>
    [JsonPropertyName("RefCount")]
    public long RefCount { get; init; } = -1;
}

/// <summary>A volume, as the daemon reports it.</summary>
public sealed record VolumeSummary
{
    /// <summary>Its name, which is also its id.</summary>
    [JsonPropertyName("Name")]
    public string Name { get; init; } = "";

    /// <summary>The driver, almost always <c>local</c>.</summary>
    [JsonPropertyName("Driver")]
    public string Driver { get; init; } = "";

    /// <summary>Where it lives inside the distribution.</summary>
    [JsonPropertyName("Mountpoint")]
    public string Mountpoint { get; init; } = "";

    /// <summary>Its size, when something measured it.</summary>
    [JsonPropertyName("UsageData")]
    public VolumeUsage? UsageData { get; init; }

    /// <summary>
    /// What was stamped on it when it was created.
    /// </summary>
    /// <remarks>
    /// Read for one of them: <see cref="Agent.SessionLabel.Key"/>, which is what makes a scoped reclaim
    /// possible at all. A volume with no labels is somebody else's and is never in a plan.
    /// </remarks>
    [JsonPropertyName("Labels")]
    public IReadOnlyDictionary<string, string>? Labels { get; init; }
}

/// <summary>What <c>/volumes</c> answers.</summary>
public sealed record VolumeList
{
    /// <summary>The volumes.</summary>
    [JsonPropertyName("Volumes")]
    public IReadOnlyList<VolumeSummary>? Volumes { get; init; }
}

/// <summary>The part of <c>/system/df</c> this tool reads.</summary>
public sealed record SystemUsage
{
    /// <summary>The volumes, this time with their sizes filled in.</summary>
    [JsonPropertyName("Volumes")]
    public IReadOnlyList<VolumeSummary>? Volumes { get; init; }
}

/// <summary>What a volume prune reclaimed.</summary>
public sealed record VolumesPruned
{
    /// <summary>The names that went.</summary>
    [JsonPropertyName("VolumesDeleted")]
    public IReadOnlyList<string>? VolumesDeleted { get; init; }

    /// <summary>How many bytes came back.</summary>
    [JsonPropertyName("SpaceReclaimed")]
    public long SpaceReclaimed { get; init; }
}

/// <summary>How an image reference reads when it is a digest rather than a name (DD166).</summary>
public static class ImageReference
{
    private const string Algorithm = "sha256:";

    /// <summary>
    /// Twelve characters of a digest, or a name exactly as the daemon said it.
    /// </summary>
    /// <remarks>
    /// The daemon answers with the reference a container was created with, and falls back to the
    /// raw image id once that reference stops resolving — which is what a rebuilt or removed tag
    /// leaves behind. Every one of the forty characters a column has room for is then shared by
    /// every image on the machine, so the cell draws a constant. Twelve and no algorithm prefix is
    /// what <c>docker ps</c> prints and what the images list here already shows, and one window
    /// spelling the same identifier two ways is the thing this exists to stop.
    ///
    /// A name is returned untouched, digest suffix included: <c>redis@sha256:…</c> is how that
    /// container was addressed, and shortening the half that identifies it would lose the name.
    /// </remarks>
    /// <param name="reference">Whatever the daemon reported.</param>
    /// <returns>The reference, as a person reads it.</returns>
    public static string Short(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var digest = reference.StartsWith(Algorithm, StringComparison.Ordinal)
            ? reference[Algorithm.Length..]
            : reference;

        // Only a bare digest shortens. Anything else is a name somebody chose, and a name is the
        // answer the column wanted in the first place.
        return digest.Length >= 12 && digest.All(char.IsAsciiHexDigit)
            ? digest[..12]
            : reference;
    }
}

/// <summary>An image, as the list endpoint reports it.</summary>
public sealed record ImageSummary
{
    /// <summary>The full id, <c>sha256:…</c>.</summary>
    [JsonPropertyName("Id")]
    public string Id { get; init; } = "";

    /// <summary>
    /// Its tags, or nothing at all.
    /// </summary>
    /// <remarks>
    /// The daemon reports an untagged image either as an empty list or as the single string
    /// <c>&lt;none&gt;:&lt;none&gt;</c> depending on version and how it lost its tag, so both mean
    /// dangling and a reader that only knows one of them under-reports the thing this list exists
    /// to find.
    /// </remarks>
    [JsonPropertyName("RepoTags")]
    public IReadOnlyList<string>? RepoTags { get; init; }

    /// <summary>How many bytes it occupies, layers included.</summary>
    [JsonPropertyName("Size")]
    public long Size { get; init; }

    /// <summary>When it was created, in seconds since the epoch.</summary>
    [JsonPropertyName("Created")]
    public long Created { get; init; }

    /// <summary>The twelve characters every Docker tool shows, without the algorithm prefix.</summary>
    public string ShortId => ImageReference.Short(Id);
}

/// <summary>What a prune reclaimed.</summary>
public sealed record ImagesPruned
{
    /// <summary>What went, one entry per deleted or untagged layer.</summary>
    [JsonPropertyName("ImagesDeleted")]
    public IReadOnlyList<DeletedImage>? ImagesDeleted { get; init; }

    /// <summary>How many bytes came back.</summary>
    [JsonPropertyName("SpaceReclaimed")]
    public long SpaceReclaimed { get; init; }
}

/// <summary>One line of what a delete or a prune did.</summary>
public sealed record DeletedImage
{
    /// <summary>The image that was removed outright.</summary>
    [JsonPropertyName("Deleted")]
    public string? Deleted { get; init; }

    /// <summary>The tag that was dropped, where the image itself survived.</summary>
    [JsonPropertyName("Untagged")]
    public string? Untagged { get; init; }
}

/// <summary>What the daemon answers when an exec is created.</summary>
public sealed record ExecCreated
{
    /// <summary>The exec's id, which is what start and inspect are addressed to.</summary>
    [JsonPropertyName("Id")]
    public string Id { get; init; } = "";
}

/// <summary>How an exec finished.</summary>
public sealed record ExecStatus
{
    /// <summary>Whether it is still going.</summary>
    [JsonPropertyName("Running")]
    public bool Running { get; init; }

    /// <summary>
    /// What it exited with, or <see langword="null"/> while it is still running.
    /// </summary>
    [JsonPropertyName("ExitCode")]
    public int? ExitCode { get; init; }
}

/// <summary>The part of a container's configuration this tool reads.</summary>
public sealed record ContainerConfig
{
    /// <summary>
    /// Whether a TTY was allocated, which is what decides how the log stream is shaped.
    /// </summary>
    /// <remarks>
    /// Without one the daemon multiplexes stdout and stderr into a framed stream. With one there is
    /// only a terminal's worth of bytes and no headers at all, so the same reader would eat eight
    /// characters out of every chunk.
    /// </remarks>
    [JsonPropertyName("Tty")]
    public bool Tty { get; init; }
}

/// <summary>A container as the inspect endpoint reports it — the fields this tool reads.</summary>
public sealed record ContainerInspect
{
    /// <summary>The full id.</summary>
    [JsonPropertyName("Id")]
    public string Id { get; init; } = "";

    /// <summary>How it was configured.</summary>
    [JsonPropertyName("Config")]
    public ContainerConfig Config { get; init; } = new();

    /// <summary>What it is doing, and why it stopped.</summary>
    [JsonPropertyName("State")]
    public ContainerState State { get; init; } = new();

    /// <summary>How many times the daemon has restarted it.</summary>
    /// <remarks>
    /// The field behind the context pack's restart marker. A count on its own is not a story - three
    /// restarts over a month is healthy and three in two minutes is a loop - which is why it is read
    /// beside <see cref="ContainerState.FinishedAt"/> rather than alone.
    /// </remarks>
    [JsonPropertyName("RestartCount")]
    public int RestartCount { get; init; }

    /// <summary>The limits it was created with.</summary>
    [JsonPropertyName("HostConfig")]
    public ContainerHostConfig HostConfig { get; init; } = new();

    /// <summary>What it has mounted, and where from.</summary>
    [JsonPropertyName("Mounts")]
    public IReadOnlyList<ContainerMount> Mounts { get; init; } = [];

    /// <summary>What it is called, as the daemon spells it, with a leading slash.</summary>
    [JsonPropertyName("Name")]
    public string Name { get; init; } = "";
}

/// <summary>A container's state, as inspect reports it.</summary>
/// <remarks>
/// Four leaves out of the whole entity tree, and they are the four the canonical diagnosis reads:
/// whether it is running, what it exited with, whether the kernel killed it for memory, and when it
/// stopped. DD23 measured the cost of paying for the tree to reach them - 1603 estimated tokens for
/// one inspect - which is why the context pack reads them once and states the conclusion.
/// </remarks>
public sealed record ContainerState
{
    /// <summary>One word: running, exited, created, paused, restarting.</summary>
    [JsonPropertyName("Status")]
    public string Status { get; init; } = "";

    /// <summary>What it exited with, when it exited.</summary>
    [JsonPropertyName("ExitCode")]
    public int ExitCode { get; init; }

    /// <summary>Whether the kernel killed it for exceeding its memory limit.</summary>
    /// <remarks>
    /// The single most useful bit in an inspect, and the one a human-readable table never carries: an
    /// exit code of 137 is SIGKILL and says nothing about who sent it, while this says the limit did.
    /// </remarks>
    [JsonPropertyName("OOMKilled")]
    public bool OomKilled { get; init; }

    /// <summary>When it stopped, as the daemon spells it.</summary>
    [JsonPropertyName("FinishedAt")]
    public string FinishedAt { get; init; } = "";

    /// <summary>When it last started, which is the window a restart count is read over.</summary>
    [JsonPropertyName("StartedAt")]
    public string StartedAt { get; init; } = "";

    /// <summary>Its health, where a healthcheck was declared.</summary>
    [JsonPropertyName("Health")]
    public ContainerHealth? Health { get; init; }
}

/// <summary>A container's health, where one was declared.</summary>
public sealed record ContainerHealth
{
    /// <summary>starting, healthy or unhealthy.</summary>
    [JsonPropertyName("Status")]
    public string Status { get; init; } = "";

    /// <summary>How many checks have failed in a row.</summary>
    [JsonPropertyName("FailingStreak")]
    public int FailingStreak { get; init; }

    /// <summary>
    /// The last few runs of the check, oldest first, as the daemon keeps them.
    /// </summary>
    /// <remarks>
    /// Read for the newest entry's output, which is the sentence the check itself printed when it
    /// decided. "unhealthy" names a verdict and nothing else; what the command said is the part
    /// somebody can act on, and it is already on an inspect nobody was reading it from.
    /// </remarks>
    [JsonPropertyName("Log")]
    public IReadOnlyList<HealthRun>? Log { get; init; }
}

/// <summary>One run of a container's health check.</summary>
public sealed record HealthRun
{
    /// <summary>What the command exited with.</summary>
    [JsonPropertyName("ExitCode")]
    public int ExitCode { get; init; }

    /// <summary>What it printed, which the daemon truncates itself.</summary>
    [JsonPropertyName("Output")]
    public string Output { get; init; } = "";
}

/// <summary>One mount a container carries.</summary>
public sealed record ContainerMount
{
    /// <summary>bind, volume or tmpfs.</summary>
    [JsonPropertyName("Type")]
    public string Type { get; init; } = "";

    /// <summary>
    /// Where it comes from: a path inside the distribution for a bind, a name for a volume.
    /// </summary>
    [JsonPropertyName("Source")]
    public string Source { get; init; } = "";

    /// <summary>The volume's name, for a volume mount.</summary>
    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    /// <summary>Where it lands inside the container.</summary>
    [JsonPropertyName("Destination")]
    public string Destination { get; init; } = "";

    /// <summary>Whether the container may write to it.</summary>
    [JsonPropertyName("RW")]
    public bool ReadWrite { get; init; }
}

/// <summary>The part of a container's host configuration this tool reads.</summary>
public sealed record ContainerHostConfig
{
    /// <summary>Its memory limit in bytes, or 0 where it has none.</summary>
    /// <remarks>
    /// Read only to say it beside an OOM kill. A limit on its own is configuration; a limit next to
    /// the kill it caused is a diagnosis.
    /// </remarks>
    [JsonPropertyName("Memory")]
    public long Memory { get; init; }

    /// <summary>
    /// What it asked the host to publish, by container port.
    /// </summary>
    /// <remarks>
    /// The declared side of the port question. Whether anything is listening on the host is a
    /// different fact, read from Windows rather than from the daemon, and the two disagreeing is the
    /// whole reason this row exists.
    /// </remarks>
    [JsonPropertyName("PortBindings")]
    public IReadOnlyDictionary<string, IReadOnlyList<PortPublish>?>? PortBindings { get; init; }
}

/// <summary>One host endpoint a container port was published to.</summary>
public sealed record PortPublish
{
    /// <summary>The host address, empty for every address.</summary>
    [JsonPropertyName("HostIp")]
    public string HostIp { get; init; } = "";

    /// <summary>The host port, as a string, which is how the daemon spells it.</summary>
    [JsonPropertyName("HostPort")]
    public string HostPort { get; init; } = "";
}

/// <summary>A container, as the list endpoint reports it.</summary>
public sealed record ContainerSummary
{
    /// <summary>The full id.</summary>
    [JsonPropertyName("Id")]
    public string Id { get; init; } = "";

    /// <summary>The image it was created from, as a name.</summary>
    [JsonPropertyName("Image")]
    public string Image { get; init; } = "";

    /// <summary>
    /// The id of that image, which is what an image list has to be joined on.
    /// </summary>
    /// <remarks>
    /// <see cref="Image"/> is whatever name the container was created with and a tag moves; the id
    /// is what says this container is holding that row. Joining on the name is how a list reports
    /// an image as free while something is still using it.
    /// </remarks>
    [JsonPropertyName("ImageID")]
    public string ImageId { get; init; } = "";

    /// <summary>One word: running, exited, created, paused.</summary>
    [JsonPropertyName("State")]
    public string State { get; init; } = "";

    /// <summary>
    /// The labels it carries, which is where compose records the project and service.
    /// </summary>
    /// <remarks>
    /// The list endpoint already carries these, so a compose address costs no extra call - which is
    /// what makes name addressing (DD24) free rather than a second round trip per container.
    /// </remarks>
    [JsonPropertyName("Labels")]
    public IReadOnlyDictionary<string, string>? Labels { get; init; }

    /// <summary>The human sentence, e.g. <c>Exited (0) 2 minutes ago</c>.</summary>
    [JsonPropertyName("Status")]
    public string Status { get; init; } = "";

    /// <summary>Its names, each with a leading slash as the API returns them.</summary>
    [JsonPropertyName("Names")]
    public IReadOnlyList<string> Names { get; init; } = [];

    /// <summary>Its ports.</summary>
    [JsonPropertyName("Ports")]
    public IReadOnlyList<PortBinding> Ports { get; init; } = [];

    /// <summary>
    /// What it has mounted, which is the only place the volume list can learn who holds what.
    /// </summary>
    /// <remarks>
    /// The volume endpoint does not report holders, so this is the other half of a join the daemon
    /// will not do — and for a volume, which is the one thing here that does not come back, that
    /// answer is what separates an orphan from a database.
    /// </remarks>
    [JsonPropertyName("Mounts")]
    public IReadOnlyList<MountPoint> Mounts { get; init; } = [];

    /// <summary>The first name without its leading slash, or the short id when it has none.</summary>
    public string DisplayName => Names.Count > 0
        ? Names[0].TrimStart('/')
        : ShortId;

    /// <summary>The twelve characters every Docker tool shows.</summary>
    public string ShortId => Id.Length > 12 ? Id[..12] : Id;

    /// <summary>The image as a column shows it: a name, or twelve characters of a digest (DD166).</summary>
    public string ImageName => ImageReference.Short(Image);

    /// <summary>
    /// The ports as a list renders them, with rows that would read identically collapsed.
    /// </summary>
    /// <remarks>
    /// A published port comes back twice — once for <c>0.0.0.0</c> and once for <c>::</c> — and both
    /// render as <c>8099-&gt;80/tcp</c> once the address is dropped. Measured against a real daemon:
    /// one <c>-p 8099:80</c> produced exactly that, printed twice. <see cref="Ports"/> keeps what the
    /// API said; this is what a person should see.
    /// </remarks>
    public IReadOnlyList<string> PublishedPorts =>
        [.. Ports.Select(port => port.ToString()).Distinct(StringComparer.Ordinal)];
}
