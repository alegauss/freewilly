using FreeWilly.Core.Builds;

namespace FreeWilly.Core.Fixtures;

/// <summary>
/// A build history that is always there, so the builds page can be looked at without one (DD126, L6).
/// </summary>
/// <remarks>
/// <b>Chosen to cover the states, not to look plausible.</b> A completed build beside a failed one and
/// one still running; a build wholly from cache beside one that refused it; a build from a checkout
/// with a revision beside one from a directory with none; and a name long enough that the column has
/// to cope. Each row is here because something renders differently for it.
///
/// <para><b>Every name begins with <see cref="SampleMachine.Prefix"/></b>, for the reason that fixture
/// gives: a capture of this in a README must be obviously a fixture, and the alternative is somebody's
/// real repository in documentation.</para>
///
/// <para><b>The times are fixed, not relative to now.</b> A capture is compared byte for byte, and a
/// duration computed against the clock would make every picture differ from the last one. Fixed as a
/// wall clock rather than as an instant since DD194, so that stays true once the window renders a
/// time in the machine's own zone.</para>
/// </remarks>
public sealed class SampleBuilds : IBuildHistory
{
    /// <summary>
    /// The wall clock this history is anchored to, which is what a capture of it shows (DD194).
    /// </summary>
    /// <remarks>
    /// A wall clock and not an instant, and that is the whole of DD194. The anchor was an instant at
    /// offset zero, which drew the same digits everywhere only for as long as the window rendered a
    /// timestamp in its own offset. DD193 made the window follow the machine's clock, for the good
    /// reason that a time is read against the one in the corner of the same screen, and this fixture
    /// then moved with the operator's zone: four rows and the <c>Started</c> field beside them, so
    /// two people documenting the same history produced two different pictures.
    /// </remarks>
    private static readonly DateTime AnchorWall = new(2026, 3, 14, 9, 30, 0, DateTimeKind.Unspecified);

    /// <summary>The moment this history is anchored to, so a capture is the same picture every run.</summary>
    /// <remarks>
    /// <para><b>Stamped with the drawing machine's own offset, which is the choice DD194 left
    /// open.</b> The alternative was pinning a zone for the capture process, and this is better on
    /// both counts the brief weighed. It keeps the byte-identical picture rather than trading it
    /// away: a wall clock carrying its machine's offset converts back to the same digits in every
    /// zone, so a committed capture still shows 09:30 whoever drew it. And it needs no second code
    /// path and no process-wide mutable zone — the render is untouched, which is what the seam was
    /// introduced to protect.</para>
    ///
    /// <para>It is also the honest reading. The fixture states a wall clock and the window shows
    /// that wall clock, which is exactly what it does with a real build.</para>
    /// </remarks>
    public static readonly DateTimeOffset Anchor = At(AnchorWall);

    /// <summary>Stamp a wall-clock moment with this machine's offset for it.</summary>
    /// <param name="wall">The moment, as a clock on a wall reads it.</param>
    /// <returns>The instant that reads as <paramref name="wall"/> here.</returns>
    /// <remarks>
    /// Each row is stamped from its own wall time rather than shifted off the anchor's offset. The
    /// difference only shows across a daylight-saving boundary, where a fixed offset carried
    /// backwards would render an hour off — a picture that differs on some machines in some months
    /// is the defect this task is about, arrived at by a subtler route.
    /// </remarks>
    private static DateTimeOffset At(DateTime wall) =>
        new(wall, TimeZoneInfo.Local.GetUtcOffset(wall));

    private static BuildSummary Summary(
        string name,
        string id,
        string status,
        int minutesAgo,
        double seconds,
        int total,
        int cached)
    {
        var started = At(AnchorWall.AddMinutes(-minutesAgo));
        return new BuildSummary
        {
            Name = SampleMachine.Prefix + name,
            Reference = $"default/default/{id}",
            Status = status,
            CreatedAt = started,
            // A running build has no completion, which is the state whose duration column must not
            // invent a number.
            CompletedAt = status == "Running" ? null : started.AddSeconds(seconds),
            TotalSteps = total,
            CompletedSteps = status == "Running" ? total / 2 : total,
            CachedSteps = cached,
        };
    }

    private static readonly IReadOnlyList<BuildSummary> Builds =
    [
        Summary("site/deployment/author", "aaaaaaaaaaaaaaaaaaaaaaaaa", "Running", 1, 0, 12, 3),
        Summary("api", "bbbbbbbbbbbbbbbbbbbbbbbbb", "Completed", 6, 1.4, 5, 5),
        Summary("worker", "ccccccccccccccccccccccccc", "Error", 22, 41.7, 9, 2),
        Summary("site/deployment/base", "ddddddddddddddddddddddddd", "Completed", 74, 167.3, 11, 0),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<BuildSummary> Recent() => Builds;

    /// <inheritdoc/>
    /// <remarks>
    /// The detail is built from the summary, so the two halves of the page cannot disagree about a
    /// build the way two hand-written fixtures would. Only the fields the list does not carry are
    /// added here, and they differ per row on purpose: the second build has no repository, which is
    /// the state where the revision rows are absent rather than blank.
    /// </remarks>
    public BuildRecord? Inspect(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        var build = Builds.FirstOrDefault(candidate =>
            string.Equals(candidate.Reference, reference, StringComparison.Ordinal)
            || string.Equals(candidate.Id, reference, StringComparison.Ordinal));

        if (build is null)
        {
            return null;
        }

        var checkout = build.Id[0] != 'b';

        return new BuildRecord
        {
            Name = build.Name,
            Reference = build.Reference,
            Context = $@"D:\{SampleMachine.Prefix}work\{build.Name}",
            Dockerfile = $@"{build.Name}\Dockerfile",
            VcsRepository = checkout ? $"https://example.invalid/{SampleMachine.Prefix}repo.git" : null,
            VcsRevision = checkout ? "0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c" : null,
            Status = build.Status,
            StartedAt = build.CreatedAt,
            CompletedAt = build.CompletedAt,
            DurationNanoseconds = (long)((build.Duration ?? TimeSpan.Zero).TotalMilliseconds
                * 1_000_000),
            CompletedSteps = build.CompletedSteps,
            TotalSteps = build.TotalSteps,
            CachedSteps = build.CachedSteps,
            Config = new BuildConfig
            {
                ImageResolveMode = "local",
                NoCache = build.CachedSteps == 0,
            },
            Materials =
            [
                new BuildMaterial(
                    "pkg:docker/ubuntu@24.04?platform=linux%2Famd64",
                    ["sha256:0000000000000000000000000000000000000000000000000000000000000001"]),
            ],
        };
    }
}
