using System.Globalization;
using FreeWilly.Core.Builds;

namespace FreeWilly.Tray.Ui;

/// <summary>A build, as the list shows it (DD126).</summary>
/// <param name="Name">What was built.</param>
/// <param name="Reference">The address, which is what the detail is looked up by.</param>
/// <param name="Status">The daemon's own word (L8).</param>
/// <param name="StartedAt">When it began, or nothing where the record carries none.</param>
/// <param name="Duration">How long it took, or nothing while it is still going.</param>
/// <param name="TotalSteps">How many steps there were.</param>
/// <param name="CachedSteps">How many came from cache.</param>
public sealed record BuildRow(
    string Name,
    string Reference,
    string Status,
    DateTimeOffset? StartedAt,
    TimeSpan? Duration,
    int TotalSteps,
    int CachedSteps)
{
    /// <summary>The id alone, which is what a link names and a person compares.</summary>
    public string Id => Reference[(Reference.LastIndexOf('/') + 1)..];

    /// <summary>The fill the chip is drawn with, set once per render from <see cref="RowStyle"/>.</summary>
    public System.Windows.Media.Brush? ChipFill { get; init; }

    /// <summary>What the chip's word is written in.</summary>
    public System.Windows.Media.Brush? ChipText { get; init; }

    /// <summary>Whether this row is the one the detail pane is showing.</summary>
    public bool IsSelected { get; init; }

    /// <summary>
    /// What the chip is asserting, in the three tones a glance tells apart.
    /// </summary>
    /// <remarks>
    /// Buildx's own words, matched case-insensitively and with anything unrecognised falling to
    /// muted rather than to good. A status this does not know is not evidence that a build
    /// succeeded, and colouring it green would be this window asserting something upstream did not.
    /// </remarks>
    public RowTone Tone => Status.ToLowerInvariant() switch
    {
        "completed" => RowTone.Good,
        "running" => RowTone.Warn,
        "error" or "failed" or "canceled" or "cancelled" => RowTone.Bad,
        _ => RowTone.Muted,
    };

    /// <summary>
    /// When it ran, absolutely rather than as an age.
    /// </summary>
    /// <remarks>
    /// No clock, deliberately. An age is nicer to read and would make every window capture differ
    /// from the last one, which is the whole thing DD38's determinism buys. The exact start and
    /// finish are on the detail pane.
    ///
    /// <para><b>In this machine's zone since DD193, and it used to be the timestamp's own.</b> That
    /// was argued for the picture: a capture drawn in the value's offset is the same picture on
    /// every machine. It is the wrong trade for the reader. buildx reports <c>created_at</c> in UTC,
    /// so a build started at 09:49 on a machine three hours behind was printed as 12:49, and a time
    /// read against the clock in the corner of the same screen has to agree with it. A capture whose
    /// only varying field is a time costs less than a column that is wrong everywhere outside
    /// UTC.</para>
    /// </remarks>
    public string When => StartedAt is { } started
        ? Clock(started, "yyyy-MM-dd HH:mm")
        : "—";

    /// <summary>
    /// A moment as the clock beside this window reads it (DD193).
    /// </summary>
    /// <param name="at">The moment, in whatever offset it arrived with.</param>
    /// <param name="format">How much of it to show.</param>
    /// <returns>The text.</returns>
    /// <remarks>
    /// One conversion for the column and for the detail pane, because they are two renderings of
    /// one instant and the defect this fixes was both of them being wrong together. The invariant
    /// culture stays: it decides digit shapes and separators, which is not what the zone was
    /// deciding.
    /// </remarks>
    internal static string Clock(DateTimeOffset at, string format) =>
        at.ToLocalTime().ToString(format, CultureInfo.InvariantCulture);

    /// <summary>How long it took, or an em dash while it is still going.</summary>
    public string DurationText => Duration is { } taken ? Human(taken) : "—";

    /// <summary>
    /// The steps, with what the cache saved.
    /// </summary>
    /// <remarks>
    /// Two facts in one column because they are read as one question — "did this actually do work"
    /// — and a build wholly from cache is the answer somebody is usually looking for.
    /// </remarks>
    public string Steps => CachedSteps > 0
        ? $"{TotalSteps} · {CachedSteps} cached"
        : $"{TotalSteps}";

    /// <summary>Render a duration the way a build log does.</summary>
    /// <param name="taken">How long.</param>
    /// <returns>Something like <c>1m 47s</c> or <c>0.4s</c>.</returns>
    public static string Human(TimeSpan taken)
    {
        if (taken.TotalSeconds < 10)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{taken.TotalSeconds:0.0}s");
        }

        if (taken.TotalSeconds < 60)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{taken.TotalSeconds:0}s");
        }

        return taken.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)taken.TotalHours}h {taken.Minutes}m")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)taken.TotalMinutes}m {taken.Seconds}s");
    }

    /// <summary>Dress this row in the theme's brushes.</summary>
    /// <param name="style">The brushes, resolved once for the whole render.</param>
    /// <returns>The row, with its chip filled in.</returns>
    public BuildRow WithChip(RowStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return this with { ChipFill = style.Fill(Tone), ChipText = style.Text(Tone) };
    }

    /// <summary>The headings this list sorts on.</summary>
    public static class Columns
    {
        /// <summary>What was built.</summary>
        public const string Name = "NAME";

        /// <summary>How it ended.</summary>
        public const string Status = "STATUS";

        /// <summary>When it ran.</summary>
        public const string When = "WHEN";

        /// <summary>How long it took.</summary>
        public const string Duration = "DURATION";
    }

    /// <summary>
    /// The order a build list opens in: most recent first.
    /// </summary>
    /// <remarks>
    /// The only order that answers the question the page is opened with. A build history is read
    /// backwards from the thing that just happened — which is also what the printed link names.
    /// </remarks>
    public const string DefaultColumn = Columns.When;

    /// <summary>Whether a column reads best biggest-first.</summary>
    /// <param name="column">The heading clicked.</param>
    /// <returns><see langword="true"/> where descending is its natural direction.</returns>
    /// <remarks>
    /// A time and a duration do; a name does not. Sorting by WHEN and getting the oldest build first
    /// is the sort nobody wanted, which is the reasoning <see cref="ListShape.Toggled"/> exists for.
    /// </remarks>
    public static bool DescendsFirst(string column) =>
        column is Columns.When or Columns.Duration;

    /// <summary>Shape a list of rows: narrowed, then ordered.</summary>
    /// <param name="rows">Everything the history answered.</param>
    /// <param name="shape">The sort and filter the page is holding.</param>
    /// <returns>The rows to draw.</returns>
    public static IReadOnlyList<BuildRow> Shaped(IEnumerable<BuildRow> rows, ListShape shape)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(shape);

        // The id is matched too, so a ref pasted out of a printed link finds its row.
        var kept = rows.Where(row => shape.Keeps(row.Name, row.Status, row.Id));

        IOrderedEnumerable<BuildRow> ordered = shape.Column switch
        {
            Columns.Name => By(kept, r => r.Name, shape.Descending, StringComparer.OrdinalIgnoreCase),
            Columns.Status => By(kept, r => r.Status, shape.Descending, StringComparer.OrdinalIgnoreCase),
            Columns.Duration => By(kept, r => r.Duration ?? TimeSpan.Zero, shape.Descending),
            _ => By(kept, r => r.StartedAt ?? DateTimeOffset.MinValue, shape.Descending),
        };

        // The id last, so two builds of the same thing in the same minute still have one fixed
        // order — a list that reshuffled between refreshes is unreadable.
        return [.. ordered.ThenBy(row => row.Id, StringComparer.Ordinal)];
    }

    private static IOrderedEnumerable<BuildRow> By<TKey>(
        IEnumerable<BuildRow> rows,
        Func<BuildRow, TKey> key,
        bool descending,
        IComparer<TKey>? comparer = null) =>
        descending ? rows.OrderByDescending(key, comparer) : rows.OrderBy(key, comparer);

    /// <summary>Project what the history answered into rows.</summary>
    /// <param name="builds">What <see cref="IBuildHistory.Recent"/> returned.</param>
    /// <returns>The rows, in the order they arrived.</returns>
    public static IReadOnlyList<BuildRow> From(IEnumerable<BuildSummary> builds)
    {
        ArgumentNullException.ThrowIfNull(builds);

        return
        [
            .. builds.Select(build => new BuildRow(
                build.Name.Length > 0 ? build.Name : build.Id,
                build.Reference,
                build.Status,
                build.CreatedAt,
                build.Duration,
                build.TotalSteps,
                build.CachedSteps)),
        ];
    }
}
