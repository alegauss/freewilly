using System.Globalization;
using System.Text;
using FreeWilly.Core.Api;

namespace FreeWilly.Core.Agent;

/// <summary>One object, and everything that happened to it since the cursor.</summary>
/// <param name="Kind">container, image, volume or network.</param>
/// <param name="Name">What it is called, or its short id where the daemon attached no name.</param>
/// <param name="What">The whole of its movement, collapsed into one phrase.</param>
public sealed record ChangeRow(string Kind, string Name, string What);

/// <summary>A delta, and where to continue from.</summary>
/// <param name="Rows">One row per object that moved, in name order.</param>
/// <param name="Cursor">What to pass to the next call.</param>
/// <param name="TooOld">
/// Whether the answer may be missing its beginning, in which case the rows are not to be trusted as
/// a complete delta.
/// </param>
public sealed record ChangeDelta(IReadOnlyList<ChangeRow> Rows, string Cursor, bool TooOld);

/// <summary>
/// What moved since last time, which is the only thing here that makes the <i>next</i> session cheaper.
/// </summary>
/// <remarks>
/// DD31. Everything else on this surface makes one session cheaper; a delta makes the second one
/// cheaper, and over a week that is the larger number. A follow-up session syncs with
/// <c>worker restarted ×3, exited 137</c> rather than re-deriving the machine from DD25's pack.
///
/// <para><b>Collapsed per object, not per event.</b> A container that crash-looped four times emits
/// twelve events saying the same thing, and twelve lines is the shape this exists to avoid. One row per
/// object carries the count and the state it ended in, which is what a caller was going to reduce them
/// to anyway.</para>
///
/// <para><b>A bounded history has to say so.</b> The daemon keeps its last <see cref="DaemonRing"/>
/// events and nothing older, so a cursor from long enough ago is answered with a beginning that was
/// silently dropped. The failure mode of a delta that quietly skips is worse than no delta, because
/// nothing downstream can detect it — so a full ring is reported as <c>too old</c> and the caller is
/// sent back to the context pack. It errs towards saying so: exactly a full ring might have lost
/// nothing, and being told to re-read costs one call, where a silent gap costs a wrong conclusion.</para>
/// </remarks>
public static class ChangeFeed
{
    /// <summary>What a change cursor starts with. A time, because the daemon's history is indexed by one.</summary>
    /// <remarks>
    /// The same prefix as a log cursor and deliberately not the <c>c:</c> of a context pack.
    ///
    /// <para>The section expected the pack's own cursor to be what this is given, and it cannot be: that
    /// cursor is a SHA of the machine's state, chosen in DD25 precisely so that an unchanged machine
    /// gives an unchanged cursor, and there is no moment inside it to ask the daemon about. Printing a
    /// second, time-based line on the pack was tried and reverted — it made two renders of an unchanged
    /// machine differ, which is the property DD25 argued for and a test already held. So a session gets
    /// its first cursor from a bare <c>read changes</c>, which it was going to call anyway, and a
    /// <c>c:</c> passed here is refused by name rather than by shape.</para>
    /// </remarks>
    public const string CursorPrefix = "t:";

    /// <summary>How many events the daemon keeps.</summary>
    /// <remarks>
    /// 256, which is moby's own <c>eventsLimit</c>. Hard-coded because the daemon does not report it and
    /// a number that is wrong in the safe direction only ever produces an unnecessary <c>too old</c>.
    /// </remarks>
    public const int DaemonRing = 256;

    /// <summary>How far back a call with no cursor looks.</summary>
    /// <remarks>
    /// Fifteen minutes. Long enough that "what just happened" is in it and short enough that the first
    /// call of a session is not a history lesson — and the answer carries a cursor, so the second call
    /// is exact rather than a window at all.
    /// </remarks>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(15);

    /// <summary>Issue a cursor for a moment.</summary>
    /// <param name="at">The moment.</param>
    /// <returns>The cursor.</returns>
    public static string CursorFor(DateTimeOffset at) =>
        CursorPrefix + at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Read a cursor, refusing the ones that are not one by name.</summary>
    /// <param name="value">What the caller typed.</param>
    /// <param name="at">The moment it names.</param>
    /// <param name="why">Why not, where it is not a cursor.</param>
    /// <returns><see langword="true"/> where it parsed.</returns>
    public static bool TryParseCursor(string value, out DateTimeOffset at, out string? why)
    {
        at = default;
        why = null;

        if (value.StartsWith(ContextPack.CursorPrefixForRefusal, StringComparison.Ordinal))
        {
            // Named rather than rejected as malformed. A context cursor is a hash of the machine's
            // state — there is no moment inside it to ask the daemon about — and the caller is one
            // line away from the right one, which the pack prints beside it.
            why = $"{value} is a context cursor: it fingerprints the machine's state and carries no "
                + $"moment. Run `freewilly read changes` with no cursor, and it answers with a "
                + $"{CursorPrefix} one to continue from.";
            return false;
        }

        if (!value.StartsWith(CursorPrefix, StringComparison.Ordinal)
            || !DateTimeOffset.TryParse(
                value[CursorPrefix.Length..],
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out at))
        {
            why = $"{value} is not a change cursor: they look like "
                + CursorFor(DateTimeOffset.UnixEpoch) + ".";
            return false;
        }

        return true;
    }

    /// <summary>Collapse a window of events into one row per object.</summary>
    /// <param name="events">What the daemon replayed, oldest first.</param>
    /// <param name="until">The moment the window ends, which becomes the next cursor.</param>
    /// <returns>The delta.</returns>
    public static ChangeDelta Collapse(IReadOnlyList<DockerEvent> events, DateTimeOffset until)
    {
        ArgumentNullException.ThrowIfNull(events);

        var byObject = new Dictionary<(string Type, string Id), List<DockerEvent>>();
        foreach (var moved in events.Where(Interesting))
        {
            var key = (moved.Type, moved.Id);
            if (!byObject.TryGetValue(key, out var list))
            {
                byObject[key] = list = [];
            }

            list.Add(moved);
        }

        var rows = byObject
            .Select(entry => new ChangeRow(
                entry.Key.Type,
                Name(entry.Value),
                Phrase(entry.Value)))
            .OrderBy(r => r.Kind, StringComparer.Ordinal)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();

        return new ChangeDelta(rows, CursorFor(until), events.Count >= DaemonRing);
    }

    /// <summary>
    /// What a delta may cost, matching the ceiling <c>agent-budget.json</c> records for this shape.
    /// </summary>
    /// <remarks>
    /// A ring of 256 events can collapse to a hundred objects on a machine that was busy, and a delta
    /// whose whole argument is that it is cheaper than re-reading the pack cannot answer with something
    /// larger than the pack. So rows go from the end and how many went is stated — the same rule DD25
    /// applied for the same reason, because a silent cut is the one thing a ceiling must not become.
    /// </remarks>
    public const int CeilingTokens = 115;

    /// <summary>The delta, as lines.</summary>
    /// <param name="delta">What moved.</param>
    /// <returns>The text, ending in a newline.</returns>
    public static string Render(ChangeDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);

        var kept = delta.Rows.Count;
        var payload = Assemble(delta, kept);
        while (kept > 0 && TokenEstimate.Of(payload) > CeilingTokens)
        {
            kept--;
            payload = Assemble(delta, kept);
        }

        return payload;
    }

    private static string Assemble(ChangeDelta delta, int kept)
    {
        var text = new StringBuilder();
        if (delta.TooOld)
        {
            // First, and before the rows, because a caller that stops reading after one line has to
            // stop on this one. The rows are still printed: they are true, they are simply not all.
            text.AppendLine(
                "too old  the daemon keeps its last "
                + DaemonRing.ToString(CultureInfo.InvariantCulture)
                + " events and this cursor reaches past them");
            text.AppendLine("         Re-read `freewilly read context`. What follows is not a complete delta.");
        }

        if (delta.Rows.Count == 0)
        {
            text.AppendLine("(nothing moved)");
        }
        else
        {
            foreach (var row in delta.Rows.Take(kept))
            {
                text.Append(row.Name.PadRight(24)).AppendLine(row.What);
            }

            if (kept < delta.Rows.Count)
            {
                text.Append("truncated ")
                    .Append((delta.Rows.Count - kept).ToString(CultureInfo.InvariantCulture))
                    .AppendLine(" more object(s) moved: ceiling reached, re-read the context");
            }
        }

        text.Append("cursor  ").AppendLine(delta.Cursor);
        return text.ToString();
    }

    /// <summary>The delta as one object, for a caller that parses.</summary>
    /// <param name="delta">What moved.</param>
    /// <returns>The document, ending in a newline.</returns>
    public static string RenderJson(ChangeDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        return System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["tooOld"] = delta.TooOld,
                ["rows"] = delta.Rows,
                ["cursor"] = delta.Cursor,
            }) + Environment.NewLine;
    }

    /// <summary>
    /// Whether an event says something a caller could act on.
    /// </summary>
    /// <remarks>
    /// The daemon emits a great deal that changes nothing: <c>exec_create</c>, <c>exec_start</c>, every
    /// health probe, every layer of a pull. Including them turns a delta back into a log, which is the
    /// cost this verb exists to avoid.
    /// </remarks>
    private static bool Interesting(DockerEvent moved) =>
        moved.ChangesTheContainerList
        || (moved.Type == "volume" && moved.Action is "create" or "destroy")
        || (moved.Type == "network" && moved.Action is "create" or "destroy")
        || (moved.Type == "image" && moved.Action is "pull" or "delete" or "untag");

    /// <summary>What to call the object, preferring the name the daemon attached.</summary>
    private static string Name(IReadOnlyList<DockerEvent> moved)
    {
        // Newest first: a rename means the last name is the one that will still find it.
        for (var i = moved.Count - 1; i >= 0; i--)
        {
            if (moved[i].Name is { Length: > 0 } name)
            {
                return name;
            }
        }

        return moved[^1].ShortId;
    }

    /// <summary>
    /// Everything that happened to one object, as one phrase.
    /// </summary>
    /// <remarks>
    /// The count and the state it ended in. A crash loop is <c>restarted ×3, exited 137</c> — which is
    /// the sentence a caller would have written after reading twelve event lines, and the exit code is
    /// the part that makes it a diagnosis rather than a notification.
    /// </remarks>
    private static string Phrase(IReadOnlyList<DockerEvent> moved)
    {
        var starts = moved.Count(e => e.Action is "start" or "restart");
        var parts = new List<string>(2);

        // Restarts, and only from the second start: the first is the thing starting.
        if (starts > 1)
        {
            parts.Add("restarted ×" + (starts - 1).ToString(CultureInfo.InvariantCulture));
        }

        var last = moved[^1];
        var ended = last.Action switch
        {
            "create" => "created",
            "start" or "restart" or "unpause" => "running",
            "die" => "exited " + (last.Actor.Attributes.TryGetValue("exitCode", out var code)
                ? code
                : "?"),
            "stop" => "stopped",
            "kill" => "killed",
            "destroy" or "delete" => "removed",
            "pause" => "paused",
            "rename" => "renamed",
            "pull" => "pulled",
            "untag" => "untagged",
            "update" => "updated",
            _ => last.Action,
        };

        parts.Add(ended);
        return string.Join(", ", parts);
    }
}
