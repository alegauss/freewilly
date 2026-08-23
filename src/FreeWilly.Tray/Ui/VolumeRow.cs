using FreeWilly.Core.Api;

namespace FreeWilly.Tray.Ui;

/// <summary>A volume, as the window shows it.</summary>
/// <param name="Name">Its name, which is also what a deletion is addressed to.</param>
/// <param name="Size">Bytes, or <see langword="null"/> while nothing has measured it yet.</param>
/// <param name="MountedBy">The containers mounting it, by name.</param>
/// <param name="IsAnonymous">Whether Docker named it, which means nobody did.</param>
public sealed record VolumeRow(
    string Name,
    long? Size,
    IReadOnlyList<string> MountedBy,
    bool IsAnonymous)
{
    /// <summary>How long the name Docker generates for an unnamed volume is.</summary>
    private const int AnonymousNameLength = 64;

    /// <summary>What this row is waiting for, or nothing.</summary>
    public string? Pending { get; init; }

    /// <summary>The engine's own sentence about why the last deletion failed, or nothing.</summary>
    public string? Failure { get; init; }

    /// <summary>Whether a deletion is in flight.</summary>
    public bool IsPending => Pending is not null;

    /// <summary>Whether the row offers its button.</summary>
    public bool IsIdle => Pending is null;

    /// <summary>Whether the last deletion failed.</summary>
    public bool HasFailure => Failure is not null;

    /// <summary>Whether any container mounts it.</summary>
    public bool IsMounted => MountedBy.Count > 0;

    /// <summary>
    /// The compose project this belongs to, read off the name, or nothing.
    /// </summary>
    /// <remarks>
    /// <c>docker compose</c> names a volume <c>&lt;project&gt;_&lt;volume&gt;</c>, and that
    /// convention is the only thing that tells somebody the orphan in front of them belonged to a
    /// project they deleted months ago. It is a convention and not a guarantee, which is why it
    /// appears in the sentence before a deletion rather than as a column claiming to be a fact.
    /// </remarks>
    public string? Project
    {
        get
        {
            if (IsAnonymous)
            {
                return null;
            }

            var split = Name.IndexOf('_', StringComparison.Ordinal);
            return split > 0 ? Name[..split] : null;
        }
    }

    /// <summary>How much of a generated name is worth drawing.</summary>
    private const int ShownCharacters = 12;

    /// <summary>
    /// What the name column draws (DD168).
    /// </summary>
    /// <remarks>
    /// A name somebody chose is the answer the column wanted and is left exactly as it is. A name
    /// the daemon generated is sixty-four characters of digest, and every one past the twelfth
    /// distinguishes nothing a reader of this tab is deciding between — the size, what mounts it and
    /// the compose prefix are what that decision is made on.
    ///
    /// <para>Twelve and an ellipsis because that is what this window's own delete dialog has always
    /// written for this case, and one identifier spelled two ways in one window is the fault DD166
    /// found in the containers list. The rule is here now and the dialog reads it, rather than each
    /// surface carrying its own copy.</para>
    /// </remarks>
    public string NameText =>
        IsAnonymous ? $"{Name[..Math.Min(Name.Length, ShownCharacters)]}…" : Name;

    /// <summary>
    /// The whole name where the column is not showing it, and nothing where it is.
    /// </summary>
    /// <remarks>
    /// A deletion is addressed to the full string and somebody copying one out of the window needs
    /// all of it, so shortening the cell has to leave it reachable. Null rather than empty on a
    /// named row: a tooltip repeating the text under the pointer is noise, and WPF draws an empty
    /// box for an empty one.
    /// </remarks>
    public string? FullName => IsAnonymous ? Name : null;

    /// <summary>What the size column reads, including while the answer is still being worked out.</summary>
    public string SizeText => Size is { } bytes ? Bytes.Human(bytes) : "measuring…";

    /// <summary>
    /// What the third column says: who holds it, or that nothing does.
    /// </summary>
    /// <remarks>
    /// "nothing" is the row a user is looking for. Saying it plainly is the difference between a
    /// list of volumes and a list of volumes you can act on.
    /// </remarks>
    public string Holders => this switch
    {
        { IsMounted: true } => string.Join(", ", MountedBy),
        { IsAnonymous: true } => "anonymous, nothing mounts it",
        _ => "nothing mounts it",
    };


    /// <summary>The headings this list sorts on.</summary>
    public static class Columns
    {
        /// <summary>The volume's name, which is also its id.</summary>
        public const string Name = "NAME";

        /// <summary>What it costs on disk, once measured.</summary>
        public const string Size = "SIZE";

        /// <summary>Which containers mount it.</summary>
        public const string MountedBy = "MOUNTED BY";
    }

    /// <summary>
    /// The order a volume list opens in: by name.
    /// </summary>
    /// <remarks>
    /// Not by size, and the reason is the compose convention: a project's volumes share a prefix, so
    /// alphabetical groups them and size scatters them.
    /// </remarks>
    public const string DefaultColumn = Columns.Name;

    /// <summary>Shape a list of rows: narrowed, then ordered.</summary>
    /// <param name="rows">What the join produced.</param>
    /// <param name="shape">The sort and filter the page is holding.</param>
    /// <returns>The rows to draw.</returns>
    public static IReadOnlyList<VolumeRow> Shaped(IEnumerable<VolumeRow> rows, ListShape shape)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(shape);

        var kept = rows.Where(row => shape.Keeps(row.Name, string.Join(" ", row.MountedBy)));

        IOrderedEnumerable<VolumeRow> ordered = shape.Column switch
        {
            // An unmeasured volume sorts as if it were empty rather than throwing the order: the
            // sizes arrive after the list, and a row jumping when its size lands is worse than a
            // row that starts at the bottom.
            Columns.Size => By(kept, r => r.Size ?? 0, shape.Descending),
            Columns.MountedBy => By(kept, r => r.MountedBy.Count, shape.Descending),
            _ => By(kept, r => r.Name, shape.Descending, StringComparer.OrdinalIgnoreCase),
        };

        return [.. ordered.ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static IOrderedEnumerable<VolumeRow> By<TKey>(
        IEnumerable<VolumeRow> rows,
        Func<VolumeRow, TKey> key,
        bool descending,
        IComparer<TKey>? comparer = null) =>
        descending ? rows.OrderByDescending(key, comparer) : rows.OrderBy(key, comparer);

    /// <summary>
    /// Join the volume list against the containers, since the daemon will not.
    /// </summary>
    /// <remarks>
    /// Sorted by name rather than by size. The compose convention groups a project's volumes
    /// together under that order, which is the thing the list is read for — and sizes arrive after
    /// the rows do, so sorting by size would reshuffle the list under the reader's hand.
    /// </remarks>
    /// <param name="volumes">What the volume endpoint returned.</param>
    /// <param name="containers">What the container endpoint returned, stopped ones included.</param>
    /// <returns>The rows, by name.</returns>
    public static IReadOnlyList<VolumeRow> From(
        IEnumerable<VolumeSummary> volumes, IEnumerable<ContainerSummary> containers)
    {
        ArgumentNullException.ThrowIfNull(volumes);
        ArgumentNullException.ThrowIfNull(containers);

        var holders = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var container in containers)
        {
            foreach (var mount in container.Mounts)
            {
                // Only volume mounts. A bind mount is a directory on the host that this list does
                // not own and must not offer to delete.
                if (!mount.Type.Equals("volume", StringComparison.Ordinal) || mount.Name.Length == 0)
                {
                    continue;
                }

                if (!holders.TryGetValue(mount.Name, out var names))
                {
                    holders[mount.Name] = names = [];
                }

                if (!names.Contains(container.DisplayName, StringComparer.Ordinal))
                {
                    names.Add(container.DisplayName);
                }
            }
        }

        // Enumerated once: the answer below is about the list as a whole, and `volumes` is an
        // IEnumerable a caller is free to make lazily.
        var all = volumes.ToList();
        var stamped = StampsAnonymous(all);

        return
        [
            .. all
                .Select(volume => new VolumeRow(
                    volume.Name,
                    volume.UsageData is { Size: >= 0 } usage ? usage.Size : null,
                    holders.TryGetValue(volume.Name, out var names) ? names : [],
                    NobodyNamed(volume, stamped)))
                .OrderBy(row => row.Name, StringComparer.Ordinal),
        ];
    }

    /// <summary>The label a daemon puts on a volume it named itself.</summary>
    private const string AnonymousLabel = "com.docker.volume.anonymous";

    /// <summary>
    /// Whether this daemon says so itself, rather than leaving it to be inferred (DD169).
    /// </summary>
    /// <remarks>
    /// The label's presence is a fact and its absence is not: a daemon old enough never to stamp one
    /// leaves every volume looking named. So the question is asked of the list rather than of the
    /// row — one volume carrying it proves this daemon stamps them, and every other volume's silence
    /// then means what it says.
    ///
    /// <para>A machine whose volumes are all named answers no here and keeps the shape test, which
    /// is the honest limit of this: there is nothing in the list to learn from.</para>
    /// </remarks>
    /// <param name="volumes">Every volume the endpoint returned.</param>
    /// <returns>Whether the label can be trusted to be absent.</returns>
    private static bool StampsAnonymous(IEnumerable<VolumeSummary> volumes) =>
        volumes.Any(volume => volume.Labels?.ContainsKey(AnonymousLabel) == true);

    /// <summary>
    /// Whether nobody named this volume.
    /// </summary>
    /// <remarks>
    /// The label where the daemon writes one, and the shape of the generated name where it does not.
    /// What this decides is what the tab says about the row — the count in the totals line, the
    /// number the prune dialog promises, and since DD168 how much of the name is drawn. It does not
    /// decide what a prune deletes: that call carries no <c>all=true</c> and the daemon takes only
    /// the volumes it knows to be anonymous, whatever this list believes.
    /// </remarks>
    /// <param name="volume">The volume.</param>
    /// <param name="stamped">Whether this daemon labels the ones it named.</param>
    /// <returns>Whether it is anonymous.</returns>
    private static bool NobodyNamed(VolumeSummary volume, bool stamped) =>
        stamped
            ? volume.Labels?.ContainsKey(AnonymousLabel) == true
            : LooksAnonymous(volume.Name);

    /// <summary>
    /// Put sizes onto rows that already exist, leaving everything else alone.
    /// </summary>
    /// <remarks>
    /// The second half of the two-call load. Rows the size call does not mention keep the size they
    /// had, so a volume created between the two calls stays on the list saying it is unmeasured
    /// rather than vanishing.
    /// </remarks>
    /// <param name="rows">The rows already on screen.</param>
    /// <param name="measured">What <c>/system/df</c> returned.</param>
    /// <returns>The same rows, with sizes where there are sizes.</returns>
    public static IReadOnlyList<VolumeRow> WithSizes(
        IEnumerable<VolumeRow> rows, IEnumerable<VolumeSummary> measured)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(measured);

        var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var volume in measured)
        {
            if (volume.UsageData is { Size: >= 0 } usage)
            {
                sizes[volume.Name] = usage.Size;
            }
        }

        return
        [
            .. rows.Select(row =>
                sizes.TryGetValue(row.Name, out var size) ? row with { Size = size } : row),
        ];
    }

    /// <summary>
    /// The fallback, for a daemon that stamps nothing (DD169).
    /// </summary>
    /// <remarks>
    /// This was the whole rule until the label was read, and it is kept only for the daemon that has
    /// no label to read. It cannot tell a generated name from a name somebody chose that happens to
    /// be a digest — a script naming volumes after content produces exactly that — which is why it
    /// is no longer asked first.
    /// </remarks>
    /// <param name="name">The volume's name.</param>
    /// <returns>Whether it has the shape of a name Docker generates.</returns>
    private static bool LooksAnonymous(string name) =>
        name.Length == AnonymousNameLength
        && name.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}

/// <summary>
/// The bind mounts in play, which are the storage this list does not carry (DD138).
/// </summary>
/// <remarks>
/// A bind mount is a folder on this machine and not an object the daemon owns, so it appears in no
/// <c>/volumes</c> answer and <see cref="VolumeRow.From"/> drops it on purpose: the only verb on
/// that list is delete, and a directory somebody chose is not ours to offer.
///
/// <para>The exclusion is right and the silence about it is not. A compose file written the way
/// most of them are — <c>./volumes/data:/var/lib/…</c> — declares no volume at all, so a project
/// with gigabytes on disk reaches a tab saying nothing has been created, and the conclusion a
/// reader draws is that the tool lost the data.</para>
/// </remarks>
/// <param name="Count">How many bind mounts, counted per destination.</param>
/// <param name="Containers">The containers holding them, by name.</param>
public sealed record BindMounts(int Count, IReadOnlyList<string> Containers)
{
    /// <summary>No binds anywhere, which is what a tab says nothing about.</summary>
    public static BindMounts None { get; } = new(0, []);

    /// <summary>Whether there is anything to say.</summary>
    public bool Any => Count > 0;

    /// <summary>
    /// Count the bind mounts across the containers already in hand.
    /// </summary>
    /// <remarks>
    /// Per destination rather than per container: two containers mounting the same folder are two
    /// mounts, because the reader is being told how much storage this list is not showing and not
    /// how many distinct directories exist.
    /// </remarks>
    /// <param name="containers">What the container endpoint returned, stopped ones included.</param>
    /// <returns>The count and who holds it.</returns>
    public static BindMounts For(IEnumerable<ContainerSummary> containers)
    {
        ArgumentNullException.ThrowIfNull(containers);

        var count = 0;
        var holders = new List<string>();
        foreach (var container in containers)
        {
            var mine = container.Mounts.Count(
                mount => mount.Type.Equals("bind", StringComparison.Ordinal));
            if (mine == 0)
            {
                continue;
            }

            count += mine;
            if (!holders.Contains(container.DisplayName, StringComparer.Ordinal))
            {
                holders.Add(container.DisplayName);
            }
        }

        holders.Sort(StringComparer.OrdinalIgnoreCase);
        return count == 0 ? None : new BindMounts(count, holders);
    }

    /// <summary>
    /// What the empty state says instead of claiming nothing was ever created.
    /// </summary>
    /// <remarks>
    /// The containers are named because that is what turns a number into the reader's own machine,
    /// and the last clause is the one the sentence exists for: an empty list here is not a loss.
    /// </remarks>
    public string Detail =>
        $"{Mounts} in use, {Held}. A bind mount is a folder on this machine rather than storage "
        + "the engine owns, so it is not on this list and nothing has gone missing.";

    /// <summary>
    /// The clause the totals line carries once the list has rows of its own (DD139).
    /// </summary>
    /// <remarks>
    /// <see cref="Detail"/> only ever renders on an empty tab, so one named volume anywhere on the
    /// machine would hide the binds again. The totals line already states what the rows do not —
    /// the space on disk, the loose anonymous ones — and this is a third fact of the same kind.
    ///
    /// <para>Terse on purpose. This sits in a 13px secondary line beside a filter and a button; the
    /// sentence that explains what a bind mount is belongs in the empty state, which has the room
    /// for it. Empty where there are none: a permanent "0 bind mounts" is noise on every machine
    /// that never had the problem.</para>
    /// </remarks>
    public string Note =>
        Any ? $" · {Counted}, {(Count == 1 ? "a host folder" : "host folders")} not on this list" : "";

    private string Counted => Count == 1 ? "1 bind mount" : $"{Count} bind mounts";

    private string Mounts => Count == 1 ? $"{Counted} is" : $"{Counted} are";

    private string Held =>
        Containers.Count == 1
            ? $"held by {Containers[0]}"
            : $"held by {string.Join(", ", Containers)}";
}

/// <summary>What the volumes tab says above the list, and what a deletion asks first.</summary>
/// <param name="Total">Every volume that has been measured, added up.</param>
/// <param name="AnonymousUnused">What a prune would take.</param>
/// <param name="Measured">Whether every row has a size yet.</param>
public sealed record VolumeTotals(long Total, int AnonymousUnused, bool Measured)
{
    /// <summary>Add up a list.</summary>
    /// <param name="rows">The rows.</param>
    /// <returns>The totals.</returns>
    public static VolumeTotals For(IReadOnlyList<VolumeRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return new VolumeTotals(
            rows.Sum(row => row.Size ?? 0),
            rows.Count(row => row is { IsAnonymous: true, IsMounted: false }),
            rows.All(row => row.Size is not null));
    }

    /// <summary>The line above the list.</summary>
    public string Summary =>
        (Measured ? $"{Bytes.Human(Total)} in volumes" : "measuring volumes…")
        + (AnonymousUnused > 0
            ? $" · {(AnonymousUnused == 1 ? "1 anonymous volume" : $"{AnonymousUnused} anonymous volumes")}"
              + " nothing mounts"
            : " · no loose anonymous volumes");

    /// <summary>Whether the prune button does anything.</summary>
    public bool CanPrune => AnonymousUnused > 0;

    /// <summary>
    /// What prune asks first.
    /// </summary>
    /// <remarks>
    /// The size is deliberately not promised here. Prune takes anonymous volumes only, and whether
    /// every one of them has been measured yet depends on a call that may still be running — so
    /// the count is stated and the space is reported afterwards, from the daemon.
    /// </remarks>
    public string PruneQuestion =>
        $"Delete {(AnonymousUnused == 1 ? "1 anonymous volume" : $"{AnonymousUnused} anonymous volumes")} "
        + "that no container mounts?\n\n"
        + "Anonymous volumes are the ones Docker named itself, left behind by `docker run -v`. "
        + "Whatever is inside them is gone for good. Named volumes are not touched.";

    /// <summary>What the window says once the daemon has answered.</summary>
    /// <param name="pruned">What came back.</param>
    /// <returns>One sentence.</returns>
    public static string PruneOutcome(VolumesPruned pruned)
    {
        ArgumentNullException.ThrowIfNull(pruned);

        var count = pruned.VolumesDeleted?.Count ?? 0;
        return count == 0
            ? "Nothing was loose by the time the engine looked."
            : $"Deleted {(count == 1 ? "1 volume" : $"{count} volumes")}, "
              + $"reclaimed {Bytes.Human(pruned.SpaceReclaimed)}.";
    }
}

/// <summary>The question asked before a volume goes.</summary>
public static class VolumeRemoval
{
    /// <summary>
    /// What the dialog says.
    /// </summary>
    /// <remarks>
    /// Always asked, because this is the one thing in this tool that does not come back: an image
    /// re-pulls and a container rebuilds, and a deleted volume is a database that is gone.
    ///
    /// When something mounts it the sentence names that container. The engine will refuse the call
    /// anyway and that refusal is correct — but "deal with the container first" is more use before
    /// the click than after it, and the container's name is the part the user needs.
    /// </remarks>
    /// <param name="row">The volume.</param>
    /// <returns>The dialog's body.</returns>
    public static string Question(VolumeRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        // Through the row's own rule rather than shortening it a second time here (DD168). This is
        // where the twelve-character form was first written, and the list drawing the whole digest
        // beside a dialog that did not is what the task is about.
        var what = row.IsAnonymous
            ? $"Delete the anonymous volume {row.NameText}?"
            : $"Delete the volume {row.NameText}?";

        var whose = row.Project is { } project
            ? $" It is named the way `docker compose` names a volume in the project \"{project}\"."
            : "";

        var held = row.IsMounted
            ? $"\n\n{Held(row)} still mounted, and the engine will refuse while that is true. "
              + "Deal with the container first."
            : "";

        var size = row.Size is { } bytes ? $" It holds {Bytes.Human(bytes)}." : "";

        return $"{what}\n\nWhatever is inside it is gone for good.{size}{whose}{held}";
    }

    private static string Held(VolumeRow row) =>
        row.MountedBy.Count == 1
            ? $"{row.MountedBy[0]} has it"
            : $"{string.Join(", ", row.MountedBy)} have it";
}
