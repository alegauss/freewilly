using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FreeWilly.Core.Api;

namespace FreeWilly.Core.Agent;

/// <summary>Everything the pack states, gathered before any of it is rendered.</summary>
/// <param name="EngineState">One word: running, starting, stopped.</param>
/// <param name="Distribution">The WSL2 distribution this tool owns.</param>
/// <param name="ApiVersion">The Engine API version this client asks for.</param>
/// <param name="ContextName">The active docker context, or null where none was readable.</param>
/// <param name="ContextReachesEngine">Whether that context points at this engine's pipe.</param>
/// <param name="Containers">Every container, as the list endpoint reported them.</param>
/// <param name="Diagnoses">
/// The inspect of each container that is not running, by container id. Only those, because a
/// container that is up has nothing for an inspect to add that the list did not already carry.
/// </param>
/// <param name="Images">Every image, for the disk line.</param>
/// <param name="VolumeCount">How many volumes there are.</param>
public sealed record ContextFacts(
    string EngineState,
    string Distribution,
    string ApiVersion,
    string? ContextName,
    bool ContextReachesEngine,
    IReadOnlyList<ContainerSummary> Containers,
    IReadOnlyDictionary<string, ContainerInspect> Diagnoses,
    IReadOnlyList<ImageSummary> Images,
    int VolumeCount);

/// <summary>
/// One deterministic, budgeted payload answering what a session asks first.
/// </summary>
/// <remarks>
/// DD25. The first thing any session does is ask what this machine is doing, and today that is
/// <c>ps -a</c>, <c>compose ps</c>, <c>version</c>, <c>system df</c> and a read of the compose file,
/// repeated three to five times as the state moves because a table carries no cursor. DD23 measured
/// the list half of that at 1906 estimated tokens per read, 5718 for the three reads a diagnosis makes.
///
/// <para>A line format rather than JSON, because entity JSON spends most of its bytes on punctuation,
/// repeated keys and authoring metadata nothing reads.</para>
///
/// <para>Four properties, none cosmetic:</para>
/// <list type="bullet">
/// <item><b>Deterministic order</b> — rows sorted by name, so the payload caches and a diff means
/// something.</item>
/// <item><b>Name addressing</b> — a container by its name and a compose service as
/// <c>svc:project/service</c>, per DD24.</item>
/// <item><b>A hard ceiling with an explicit truncation cursor</b> — never a silent cut. A payload that
/// quietly drops a row is worse than one that refuses, so rows are dropped from the end and the count
/// that went is stated.</item>
/// <item><b>State stated rather than probed</b> — the engine line says what is there, so the caller
/// never spends a call discovering whether a capability exists.</item>
/// </list>
///
/// <para>Two deviations from the constitution's sample, both deliberate. The port column carries the
/// mapping and makes no claim about whether the host port answers: that is DD30, it needs a different
/// mechanism, and a weaker word here would mean less than DD30 will make it mean. And volumes are
/// counted rather than sized, because the only endpoint that sizes them is <c>/system/df</c>, which
/// walks the filesystem and takes seconds — a cost a pack built to replace five cheap calls cannot
/// pay.</para>
/// </remarks>
public static class ContextPack
{
    /// <summary>
    /// The prefix on this pack's cursor, so a log cursor is never confused with it (DD27).
    /// </summary>
    /// <remarks>
    /// Named rather than spelled twice. This one fingerprints the machine's state and is stable across
    /// truncation; <see cref="LogDigest.CursorPrefix"/> marks a position in a log. They look alike, so
    /// the refusal that tells a caller which they pasted reads this constant.
    /// </remarks>
    public const string CursorPrefixForRefusal = "c:";

    /// <summary>The compose label carrying the project.</summary>
    public const string ProjectLabel = "com.docker.compose.project";

    /// <summary>The compose label carrying the service.</summary>
    public const string ServiceLabel = "com.docker.compose.service";

    /// <summary>
    /// The ceiling, in estimated tokens.
    /// </summary>
    /// <remarks>
    /// A constant rather than a read of <c>agent-budget.json</c>: the shipped executable carries no
    /// budget file and should not need one. The file stays authoritative for review, and a test holds
    /// this number to the ceiling recorded there — so raising one without the other fails.
    /// </remarks>
    public const int CeilingTokens = 200;

    /// <summary>
    /// Render the same facts as JSON, for a caller that parses rather than reads.
    /// </summary>
    /// <param name="facts">What was gathered.</param>
    /// <returns>The payload, ending in a newline.</returns>
    /// <remarks>
    /// Deliberately not under <see cref="CeilingTokens"/>. The ceiling exists because a line format is
    /// read by something paying per token and a silent truncation would corrupt that; a parser asking
    /// for JSON has accepted the cost of punctuation and repeated keys, and truncating structured output
    /// would hand it a document that is wrong rather than long. So this is complete or it is nothing.
    /// </remarks>
    public static string RenderJson(ContextFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var rows = facts.Containers
            .OrderBy(c => c.DisplayName, StringComparer.Ordinal)
            .Select(c =>
            {
                facts.Diagnoses.TryGetValue(c.Id, out var inspect);
                return new
                {
                    name = c.DisplayName,
                    state = c.State,
                    status = StateOf(c),
                    address = Address(c),
                    ports = c.PublishedPorts,
                    oomKilled = inspect?.State.OomKilled,
                    exitCode = inspect?.State.ExitCode,
                    restarts = inspect?.RestartCount,
                    memoryLimit = inspect is { HostConfig.Memory: > 0 }
                        ? inspect.HostConfig.Memory
                        : (long?)null,
                };
            })
            .ToList();

        long images = 0;
        long dangling = 0;
        foreach (var image in facts.Images)
        {
            images += image.Size;
            var tags = image.RepoTags;
            if (tags is null || tags.Count == 0
                || tags.All(tag => tag.StartsWith("<none>", StringComparison.Ordinal)))
            {
                dangling += image.Size;
            }
        }

        var document = new
        {
            engine = new
            {
                state = facts.EngineState,
                distribution = facts.Distribution,
                apiVersion = facts.ApiVersion,
                context = facts.ContextName,
                contextReachesEngine = facts.ContextReachesEngine,
            },
            containers = rows,
            disk = new { imageBytes = images, danglingBytes = dangling, volumes = facts.VolumeCount },
            cursor = Cursor(EngineLine(facts), ContainerLines(facts), DiskLine(facts)),
        };

        return System.Text.Json.JsonSerializer.Serialize(
            document,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = false }) + Environment.NewLine;
    }

    /// <summary>Render the pack.</summary>
    /// <param name="facts">What was gathered.</param>
    /// <returns>The payload, ending in a newline.</returns>
    public static string Render(ContextFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var engine = EngineLine(facts);
        var rows = ContainerLines(facts);
        var disk = DiskLine(facts);

        // The cursor is over the state, not over the text: it is the fingerprint of what the machine
        // is, so an unchanged machine gives an unchanged cursor even if a row was truncated this time
        // and not last. DD31 is a delta since one of these.
        var cursor = Cursor(engine, rows, disk);

        var kept = rows.Count;
        var payload = Assemble(engine, rows, disk, cursor, dropped: 0);
        while (kept > 0 && TokenEstimate.Of(payload) > CeilingTokens)
        {
            // Rows go from the end, and how many went is stated. A silent cut is the one thing the
            // ceiling must not become.
            kept--;
            payload = Assemble(engine, rows.Take(kept).ToList(), disk, cursor, rows.Count - kept);
        }

        return payload;
    }

    private static string Assemble(
        string engine, IReadOnlyList<string> rows, string disk, string cursor, int dropped)
    {
        var text = new StringBuilder();
        text.AppendLine(engine);
        foreach (var row in rows)
        {
            text.AppendLine(row);
        }

        if (dropped > 0)
        {
            text.Append("truncated ").Append(dropped.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" more container(s): ceiling reached, ask by name for the rest");
        }

        text.AppendLine(disk);
        text.Append("cursor  ").AppendLine(cursor);
        return text.ToString();
    }

    private static string EngineLine(ContextFacts facts)
    {
        var context = facts.ContextName is null
            ? "ctx=?"
            : $"ctx={facts.ContextName}({(facts.ContextReachesEngine ? "ok" : "elsewhere")})";

        return string.Join(
            "  ",
            "engine",
            facts.EngineState,
            $"wsl:{facts.Distribution}",
            $"api={facts.ApiVersion}",
            context);
    }

    private static List<string> ContainerLines(ContextFacts facts)
    {
        var lines = new List<string>();

        // Sorted by name. The daemon answers in creation order, which moves the moment anything is
        // recreated, and a payload whose order moves cannot be diffed.
        foreach (var container in facts.Containers.OrderBy(c => c.DisplayName, StringComparer.Ordinal))
        {
            var parts = new List<string> { container.DisplayName, StateOf(container) };

            if (Address(container) is { } address)
            {
                parts.Add(address);
            }

            if (container.PublishedPorts.Count > 0)
            {
                parts.Add(string.Join(",", container.PublishedPorts));
            }

            if (facts.Diagnoses.TryGetValue(container.Id, out var inspect))
            {
                parts.AddRange(Diagnosis(inspect));
            }

            lines.Add(string.Join("  ", parts));
        }

        return lines;
    }

    /// <summary>
    /// The state, taken from the list's own sentence rather than from an inspect.
    /// </summary>
    /// <remarks>
    /// <c>Status</c> already carries the exit code and the health — <c>Exited (137) 12 seconds ago</c>,
    /// <c>Up 4 minutes (healthy)</c> — so both come free with the list. Reading them from an inspect
    /// instead would be the projection cost DD23 measured, paid for something already in hand.
    /// </remarks>
    private static string StateOf(ContainerSummary container)
    {
        var status = container.Status;
        if (status.Length == 0)
        {
            return container.State;
        }

        // Kept short: the sentence is for a person and the pack is not. "Up 4 minutes (healthy)"
        // becomes "up 4m (healthy)"; "Exited (137) 12 seconds ago" becomes "exited 137".
        if (status.StartsWith("Exited (", StringComparison.Ordinal))
        {
            var close = status.IndexOf(')', StringComparison.Ordinal);
            return close > 8 ? $"exited {status[8..close]}" : "exited";
        }

        if (status.StartsWith("Up ", StringComparison.Ordinal))
        {
            var health = status.Contains("(healthy)", StringComparison.Ordinal) ? " (healthy)"
                : status.Contains("(unhealthy)", StringComparison.Ordinal) ? " (unhealthy)"
                : status.Contains("(health: starting)", StringComparison.Ordinal) ? " (starting)"
                : "";
            return "up " + Brief(status[3..]) + health;
        }

        return container.State;
    }

    /// <summary>"4 minutes" as "4m", because the pack is read by something paying per token.</summary>
    private static string Brief(string duration)
    {
        var words = duration.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2)
        {
            return duration.Trim();
        }

        var unit = words[1] switch
        {
            var u when u.StartsWith("second", StringComparison.Ordinal) => "s",
            var u when u.StartsWith("minute", StringComparison.Ordinal) => "m",
            var u when u.StartsWith("hour", StringComparison.Ordinal) => "h",
            var u when u.StartsWith("day", StringComparison.Ordinal) => "d",
            var u when u.StartsWith("week", StringComparison.Ordinal) => "w",
            var u when u.StartsWith("month", StringComparison.Ordinal) => "mo",
            _ => "",
        };
        return unit.Length == 0 ? duration.Trim() : words[0] + unit;
    }

    /// <summary>The compose address, when the labels carry one.</summary>
    private static string? Address(ContainerSummary container)
    {
        if (container.Labels is null
            || !container.Labels.TryGetValue(ProjectLabel, out var project)
            || !container.Labels.TryGetValue(ServiceLabel, out var service)
            || project.Length == 0 || service.Length == 0)
        {
            return null;
        }

        return $"{Agent.Address.ServicePrefix}{project}/{service}";
    }

    /// <summary>
    /// What the inspect of a stopped container adds, and nothing more.
    /// </summary>
    /// <remarks>
    /// This is the whole argument for the command. The constitution's sample closes the canonical
    /// task's question — why is the api container not responding — with <c>OOM limit=512m</c>, in the
    /// first call, without an inspect the caller had to ask for and pay 1603 tokens to read.
    /// </remarks>
    private static IEnumerable<string> Diagnosis(ContainerInspect inspect)
    {
        if (inspect.State.OomKilled)
        {
            yield return "OOM";
        }

        if (inspect.RestartCount > 0)
        {
            yield return $"×{inspect.RestartCount.ToString(CultureInfo.InvariantCulture)}";
        }

        if (inspect.HostConfig.Memory > 0)
        {
            yield return $"limit={Bytes(inspect.HostConfig.Memory)}";
        }
    }

    private static string DiskLine(ContextFacts facts)
    {
        long total = 0;
        long dangling = 0;
        foreach (var image in facts.Images)
        {
            total += image.Size;
            // Dangling is an image no tag points at. The daemon spells that either as an empty list
            // or as the literal <none>:<none>, and both mean the same thing.
            var tags = image.RepoTags;
            if (tags is null || tags.Count == 0
                || tags.All(tag => tag.StartsWith("<none>", StringComparison.Ordinal)))
            {
                dangling += image.Size;
            }
        }

        var images = dangling > 0
            ? $"images {Bytes(total)} ({Bytes(dangling)} dangling)"
            : $"images {Bytes(total)}";

        // Counted, not sized: /system/df is the only endpoint that sizes volumes and it walks the
        // filesystem, which is seconds on a machine with data on it.
        return $"disk    {images}  volumes {facts.VolumeCount.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>Bytes as a person and a token budget both want them: two significant figures.</summary>
    internal static string Bytes(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes.ToString(CultureInfo.InvariantCulture) + "B";
        }

        string[] units = ["K", "M", "G", "T"];
        double value = bytes;
        var unit = -1;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value >= 10
            ? Math.Round(value).ToString(CultureInfo.InvariantCulture) + units[unit]
            : value.ToString("0.#", CultureInfo.InvariantCulture) + units[unit];
    }

    /// <summary>
    /// A fingerprint of the state, so an unchanged machine gives an unchanged cursor.
    /// </summary>
    /// <remarks>
    /// Over the facts and not over the rendered payload: truncation changes the text and not the
    /// machine, and a cursor that moved because a ceiling was reached would report a change that did
    /// not happen. Six hex characters, which is enough to notice a change and short enough to be free.
    /// </remarks>
    private static string Cursor(string engine, IReadOnlyList<string> rows, string disk)
    {
        var material = string.Join("\n", [engine, .. rows, disk]);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return CursorPrefixForRefusal + Convert.ToHexStringLower(digest)[..6];
    }
}
