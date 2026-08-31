using System.Globalization;
using System.Text;
using FreeWilly.Core.Api;

namespace FreeWilly.Core.Agent;

/// <summary>How severe a line says it is.</summary>
public enum LogLevel
{
    /// <summary>The line does not say. Never filtered out — see <see cref="LogDigest"/>.</summary>
    Unknown = 0,

    /// <summary>trace.</summary>
    Trace = 1,

    /// <summary>debug.</summary>
    Debug = 2,

    /// <summary>info.</summary>
    Info = 3,

    /// <summary>warn or warning.</summary>
    Warn = 4,

    /// <summary>error.</summary>
    Error = 5,

    /// <summary>fatal, panic or critical.</summary>
    Fatal = 6,
}

/// <summary>One line of a container's output.</summary>
/// <param name="Stream">Which of the two it came from.</param>
/// <param name="Timestamp">
/// When the daemon says it was written, or <see langword="null"/> where the read did not ask for
/// timestamps. This is what a log cursor is made of.
/// </param>
/// <param name="Text">The line, without its timestamp.</param>
public sealed record LogLine(LogStream Stream, DateTimeOffset? Timestamp, string Text)
{
    /// <summary>What this line says about its own severity.</summary>
    public LogLevel Level => LogDigest.LevelOf(Text);
}

/// <summary>What a caller asked of a log read.</summary>
/// <param name="Since">Only lines strictly after this, where given.</param>
/// <param name="MinimumLevel">Only lines at least this severe, plus every line whose level is unknown.</param>
/// <param name="Dedup">Collapse identical lines to one and a count.</param>
/// <param name="BudgetTokens">Stop at this many estimated tokens, saying what was dropped.</param>
public sealed record LogQuery(
    DateTimeOffset? Since = null,
    LogLevel MinimumLevel = LogLevel.Unknown,
    bool Dedup = false,
    int? BudgetTokens = null);

/// <summary>What a log read produced.</summary>
/// <param name="Text">The payload, ending in a newline.</param>
/// <param name="Cursor">
/// The position to pass back as <c>--since</c>, or <see langword="null"/> where nothing was read.
/// </param>
/// <param name="Lines">How many lines came back, after filtering and dedup.</param>
/// <param name="Dropped">How many lines the budget cut, which is never zero silently.</param>
public sealed record LogResult(string Text, string? Cursor, int Lines, int Dropped);

/// <summary>
/// A log read with a cursor, a level, a dedup and a ceiling.
/// </summary>
/// <remarks>
/// DD27. Logs are the largest token sink in this domain and the one with no analogue anywhere else: a
/// container that restarts eight times writes the same stack trace eight times, and <c>--tail</c> is the
/// only instrument, so a caller either truncates blind or pays for all of it. DD23 measured one 200-line
/// tail at 4170 estimated tokens, second only to re-discovery.
///
/// <para><b>The cursor here is a position, and DD25's is not.</b> That one is a fingerprint of machine
/// state, deliberately stable across truncation, so it cannot resume a read. This one is the timestamp
/// of the last line returned, which is what <c>/logs?since=</c> accepts. Both are called cursors and they
/// are different things, so this one is prefixed <c>t:</c> and that one <c>c:</c>.</para>
///
/// <para><b>A line whose level cannot be read is kept.</b> Level is inferred from the text because a
/// container log has no structure to ask, and dropping what could not be classified would silently lose
/// the one line that held the answer. So <c>--level error</c> means "errors, and anything that did not
/// say", which is the only version of the filter that cannot hide the answer.</para>
/// </remarks>
public static class LogDigest
{
    /// <summary>The prefix that marks a log position, so it is never confused with a state cursor.</summary>
    public const string CursorPrefix = "t:";

    /// <summary>
    /// The ceiling a log read has when the caller did not name one.
    /// </summary>
    /// <remarks>
    /// Bounded by default, and that is the difference between a registered ceiling and a decorative one:
    /// with no default, <c>read logs</c> would return a ten-megabyte file and the number in
    /// <c>agent-budget.json</c> would describe nothing. A caller who wants more says <c>--budget</c>, and
    /// a caller who wants all of it says <c>--out</c> — where a ceiling is meaningless because a file is
    /// not read by something paying per token. A test holds this to the ceiling the budget records.
    /// </remarks>
    public const int DefaultBudgetTokens = 400;

    /// <summary>Read a level out of a line, or <see cref="LogLevel.Unknown"/>.</summary>
    /// <param name="text">The line.</param>
    /// <returns>The level.</returns>
    /// <remarks>
    /// Only the first forty characters are looked at. A level appears in a line's prefix, and a line
    /// mentioning the word "error" in its message is not an error line — matching anywhere would make
    /// <c>--level error</c> keep prose about errors and drop the stack trace under it.
    /// </remarks>
    public static LogLevel LevelOf(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return LogLevel.Unknown;
        }

        var head = text.Length <= 40 ? text : text[..40];

        // Ordered most severe first: a line saying "FATAL: error while" is fatal, not an error.
        if (Mentions(head, "fatal") || Mentions(head, "panic") || Mentions(head, "critical")
            || Mentions(head, "crit"))
        {
            return LogLevel.Fatal;
        }

        if (Mentions(head, "error") || Mentions(head, "err") || Mentions(head, "severe"))
        {
            return LogLevel.Error;
        }

        if (Mentions(head, "warning") || Mentions(head, "warn"))
        {
            return LogLevel.Warn;
        }

        if (Mentions(head, "info") || Mentions(head, "notice"))
        {
            return LogLevel.Info;
        }

        if (Mentions(head, "debug"))
        {
            return LogLevel.Debug;
        }

        return Mentions(head, "trace") || Mentions(head, "verbose")
            ? LogLevel.Trace
            : LogLevel.Unknown;
    }

    private static bool Mentions(string head, string word) =>
        head.Contains(word, StringComparison.OrdinalIgnoreCase);

    /// <summary>Read a level as a caller typed it.</summary>
    /// <param name="text">The word.</param>
    /// <param name="level">The level.</param>
    /// <returns><see langword="true"/> where it is a level.</returns>
    public static bool TryParseLevel(string? text, out LogLevel level)
    {
        level = LogLevel.Unknown;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        switch (text.Trim().ToLowerInvariant())
        {
            case "trace" or "verbose": level = LogLevel.Trace; return true;
            case "debug": level = LogLevel.Debug; return true;
            case "info": level = LogLevel.Info; return true;
            case "warn" or "warning": level = LogLevel.Warn; return true;
            case "error" or "err": level = LogLevel.Error; return true;
            case "fatal" or "panic" or "critical": level = LogLevel.Fatal; return true;
            default: return false;
        }
    }

    /// <summary>Split a stream of frames into lines, taking each line's timestamp off the front.</summary>
    /// <param name="chunks">The frames, in order.</param>
    /// <returns>The lines.</returns>
    /// <remarks>
    /// A frame can end mid-line, so text is carried across frames per stream rather than per frame —
    /// splitting each frame on its own would cut a stack trace wherever the daemon happened to flush.
    /// </remarks>
    public static IReadOnlyList<LogLine> Split(IEnumerable<LogChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        var tally = new LogTally(new LogQuery());
        foreach (var chunk in chunks)
        {
            tally.Add(chunk);
        }

        tally.Flush();
        return tally.Lines;
    }

    /// <summary>One line, or nothing where the raw text was only a line ending.</summary>
    internal static LogLine? Line(LogStream stream, string raw)
    {
        var text = raw.TrimEnd('\r');
        if (text.Length == 0)
        {
            return null;
        }

        // With timestamps=1 the daemon puts an RFC3339Nano stamp and a space in front of every line.
        var space = text.IndexOf(' ', StringComparison.Ordinal);
        return space > 0
            && DateTimeOffset.TryParse(
                text[..space], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? new LogLine(stream, at, text[(space + 1)..])
            : new LogLine(stream, null, text);
    }

    /// <summary>Whether this line survives the query's filters.</summary>
    internal static bool Keeps(LogLine line, LogQuery query)
    {
        if (query.Since is { } since && line.Timestamp is { } at && at <= since)
        {
            return false;
        }

        // A line whose level is unknown is always kept: it could not say what it was, and dropping it
        // would be the filter hiding the answer rather than narrowing it.
        return query.MinimumLevel == LogLevel.Unknown
            || line.Level == LogLevel.Unknown
            || line.Level >= query.MinimumLevel;
    }

    /// <summary>What one kept line adds to the rendered payload.</summary>
    internal static int Weight(LogLine line) => Format(line, 1).Length + Environment.NewLine.Length;

    /// <summary>Apply a query to a set of lines.</summary>
    /// <param name="lines">The lines, in order.</param>
    /// <param name="query">What was asked.</param>
    /// <returns>The payload, the cursor, and what was dropped.</returns>
    public static LogResult Render(IReadOnlyList<LogLine> lines, LogQuery query)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(query);

        var kept = Filter(lines, query);
        var cursor = Cursor(lines);

        if (kept.Count == 0)
        {
            return new LogResult(
                "(nothing" + (query.Since is null ? "" : " since that cursor") + ")"
                    + Environment.NewLine
                    + (cursor is null ? "" : "cursor  " + cursor + Environment.NewLine),
                cursor,
                0,
                0);
        }

        var rendered = query.Dedup ? Collapse(kept) : [.. kept.Select(l => Format(l, 1))];

        // The budget cuts from the end, and says how much it cut. A payload that quietly drops the end
        // reads exactly like a log that ended, which is the one failure this must not have.
        var take = rendered.Count;
        var text = Assemble(rendered, take, 0, cursor);
        while (take > 0 && query.BudgetTokens is { } ceiling && TokenEstimate.Of(text) > ceiling)
        {
            take--;
            text = Assemble(rendered, take, rendered.Count - take, cursor);
        }

        return new LogResult(text, cursor, take, rendered.Count - take);
    }

    private static List<LogLine> Filter(IReadOnlyList<LogLine> lines, LogQuery query) =>
        [.. lines.Where(line => Keeps(line, query))];

    /// <summary>
    /// Collapse identical lines to one and a count.
    /// </summary>
    /// <remarks>
    /// By content and across the whole read, not only adjacent: a restart loop writes the same trace
    /// once per restart, and the copies are separated by everything else each run printed. The first
    /// occurrence keeps its place, so the order still reads as the log's own.
    /// </remarks>
    private static List<string> Collapse(List<LogLine> kept)
    {
        var counts = new Dictionary<(LogStream, string), int>();
        var order = new List<LogLine>();
        foreach (var line in kept)
        {
            var key = (line.Stream, line.Text);
            if (counts.TryGetValue(key, out var seen))
            {
                counts[key] = seen + 1;
                continue;
            }

            counts[key] = 1;
            order.Add(line);
        }

        return [.. order.Select(line => Format(line, counts[(line.Stream, line.Text)]))];
    }

    private static string Format(LogLine line, int count)
    {
        var marker = line.Stream == LogStream.StdErr ? "E" : "O";
        var repeat = count > 1
            ? " × " + count.ToString(CultureInfo.InvariantCulture)
            : "";
        return marker + "  " + line.Text + repeat;
    }

    private static string Assemble(List<string> rendered, int take, int dropped, string? cursor)
    {
        var text = new StringBuilder();
        for (var i = 0; i < take; i++)
        {
            text.AppendLine(rendered[i]);
        }

        if (dropped > 0)
        {
            text.Append("truncated ").Append(dropped.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" more line(s): budget reached, read on from the cursor");
        }

        if (cursor is not null)
        {
            text.Append("cursor  ").AppendLine(cursor);
        }

        return text.ToString();
    }

    /// <summary>
    /// The position to read on from: the last timestamp seen, before any filtering.
    /// </summary>
    /// <remarks>
    /// Before filtering on purpose. A cursor taken after a level filter would resume from the last
    /// <em>error</em> and silently skip everything quieter written since, so the next read would be
    /// missing lines nobody asked it to drop.
    /// </remarks>
    private static string? Cursor(IReadOnlyList<LogLine> lines)
    {
        DateTimeOffset? last = null;
        foreach (var line in lines)
        {
            if (line.Timestamp is { } at && (last is null || at > last))
            {
                last = at;
            }
        }

        return last is null
            ? null
            : CursorPrefix + last.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    /// <summary>Read a cursor a caller passed back.</summary>
    /// <param name="text">The cursor, with or without its prefix.</param>
    /// <param name="since">The position.</param>
    /// <param name="refusal">Why it is not one.</param>
    /// <returns><see langword="true"/> where it is a log cursor.</returns>
    public static bool TryParseCursor(
        string? text, out DateTimeOffset since, out string? refusal)
    {
        since = default;
        refusal = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            refusal = "a cursor is required, as read logs printed it";
            return false;
        }

        var value = text.Trim();
        if (value.StartsWith(ContextPack.CursorPrefixForRefusal, StringComparison.Ordinal))
        {
            // The one mistake worth naming rather than refusing generically: the two cursors on this
            // surface look alike and mean different things.
            refusal = $"{value} is a state cursor from `read context`, which fingerprints the machine "
                + $"rather than a position in a log. A log cursor starts {CursorPrefix} and is printed "
                + "by this command.";
            return false;
        }

        if (value.StartsWith(CursorPrefix, StringComparison.Ordinal))
        {
            value = value[CursorPrefix.Length..];
        }

        if (DateTimeOffset.TryParse(
            value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out since))
        {
            return true;
        }

        refusal = $"{text} is not a log cursor: it is {CursorPrefix} and a timestamp, as this command "
            + "printed it.";
        return false;
    }
}

/// <summary>
/// A digest built as the chunks arrive, for a read that follows rather than one that ends (DD253).
/// </summary>
/// <remarks>
/// <see cref="LogDigest.Split"/> and <see cref="LogDigest.Render"/> are whole-buffer functions, which is
/// right for a read that fetches once. A follow calls them per chunk, and re-splitting from byte zero
/// each time is quadratic in what it reads: under the token ceiling the buffer stays small enough to
/// hide it, and <c>--out</c> lifts that ceiling on purpose.
///
/// <para>So the carry that <see cref="LogDigest.Split"/> builds internally and throws away is kept here
/// instead, and each chunk costs its own length. <see cref="Tokens"/> is the same idea for the ceiling:
/// counting what a kept line adds is O(1), where rendering the payload to measure it is O(n).</para>
///
/// <para><b>It is an estimate, and only a stop condition.</b> Under <c>Dedup</c> a repeat is counted as
/// nothing, though the <c>× N</c> it puts on the first occurrence costs a few characters. The exact
/// truncation is still <see cref="LogDigest.Render"/>'s, which runs once at the end over these same
/// lines.</para>
/// </remarks>
public sealed class LogTally
{
    private readonly LogQuery _query;
    private readonly Dictionary<LogStream, StringBuilder> _pending = [];
    private readonly HashSet<(LogStream, string)> _seen = [];
    private readonly List<LogLine> _lines = [];
    private int _characters;

    /// <summary>Start a tally.</summary>
    /// <param name="query">What was asked, whose filters decide what counts toward the ceiling.</param>
    public LogTally(LogQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        _query = query;
    }

    /// <summary>Every line read so far, in order and unfiltered, as <c>Split</c> would return them.</summary>
    public IReadOnlyList<LogLine> Lines => _lines;

    /// <summary>What the payload would cost, estimated as it grows.</summary>
    public int Tokens => TokenEstimate.OfCharacters(_characters);

    /// <summary>Take one chunk.</summary>
    /// <param name="chunk">What just arrived.</param>
    /// <returns>Only the lines this chunk completed, for a caller matching as it reads.</returns>
    public IReadOnlyList<LogLine> Add(LogChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        if (!_pending.TryGetValue(chunk.Stream, out var buffer))
        {
            _pending[chunk.Stream] = buffer = new StringBuilder();
        }

        buffer.Append(chunk.Text);
        var text = buffer.ToString();
        var start = 0;
        List<LogLine> fresh = [];
        int newline;
        while ((newline = text.IndexOf('\n', start)) >= 0)
        {
            Count(fresh, chunk.Stream, text[start..newline]);
            start = newline + 1;
        }

        buffer.Clear();
        buffer.Append(text[start..]);
        return fresh;
    }

    /// <summary>End the tally, so a last line with no newline after it is still a line.</summary>
    /// <returns>Whatever was still carried, now counted.</returns>
    public IReadOnlyList<LogLine> Flush()
    {
        List<LogLine> fresh = [];
        foreach (var (stream, buffer) in _pending)
        {
            if (buffer.Length > 0)
            {
                Count(fresh, stream, buffer.ToString());
                buffer.Clear();
            }
        }

        return fresh;
    }

    private void Count(List<LogLine> fresh, LogStream stream, string raw)
    {
        if (LogDigest.Line(stream, raw) is not { } line)
        {
            return;
        }

        _lines.Add(line);
        fresh.Add(line);

        if (!LogDigest.Keeps(line, _query))
        {
            return;
        }

        // Under Dedup a repeat collapses into the first occurrence's count, so it adds nothing to
        // the payload. Without it, every kept line is its own row.
        if (!_query.Dedup || _seen.Add((line.Stream, line.Text)))
        {
            _characters += LogDigest.Weight(line);
        }
    }
}
