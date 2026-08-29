using FreeWilly.Core.Api;
using FreeWilly.Core.Engine;

namespace FreeWilly.Tray.Ui;

/// <summary>One published port, and where clicking it goes.</summary>
/// <param name="Text">What the cell reads, e.g. <c>8080-&gt;80/tcp</c>.</param>
/// <param name="Url">
/// Where to open, or <see langword="null"/> when there is nowhere: a port the container exposes and
/// nothing published has no address on this machine, so it is text and not a link.
/// </param>
public sealed record PortLink(string Text, string? Url)
{
    /// <summary>Whether this one can be clicked.</summary>
    public bool IsLink => Url is not null;

    /// <summary>Read a binding.</summary>
    /// <param name="port">The binding.</param>
    /// <returns>The cell.</returns>
    public static PortLink From(PortBinding port)
    {
        ArgumentNullException.ThrowIfNull(port);

        // Only TCP gets a link. A published UDP port is real and http://localhost:x is not where it
        // is, and a link that opens a browser onto nothing is worse than plain text.
        var url = port.PublicPort is { } published
            && port.Type.Equals("tcp", StringComparison.OrdinalIgnoreCase)
                ? $"http://localhost:{published}"
                : null;

        return new PortLink(port.ToString(), url);
    }
}

/// <summary>A container, as the window shows it.</summary>
/// <param name="Name">The name, or the short id when it has none.</param>
/// <param name="Image">The image.</param>
/// <param name="State">One word.</param>
/// <param name="Status">The daemon's own sentence: <c>Up 3 minutes</c>, <c>Exited (0) …</c>.</param>
/// <param name="Ports">The ports, deduplicated, each with its link or without one.</param>
/// <param name="Id">The full id, which is what an action is addressed to.</param>
public sealed record ContainerRow(
    string Name,
    string Image,
    string State,
    string Status,
    IReadOnlyList<PortLink> Ports,
    string Id)
{
    /// <summary>
    /// The compose project this container belongs to, or nothing (DD106).
    /// </summary>
    /// <remarks>
    /// Read off the label the list response already carries, so the hierarchy costs no second call —
    /// the same label DD24 addresses containers by. A container carrying none stays a top-level row
    /// rather than joining an "other" group, because that group would name nothing.
    /// </remarks>
    public string? Project { get; init; }

    /// <summary>The compose service this container is an instance of, or nothing (DD107).</summary>
    public string? Service { get; init; }

    /// <summary>The services this one waits for, as its own label names them (DD107).</summary>
    public IReadOnlyList<string> DependsOn { get; init; } = [];

    /// <summary>
    /// Whether this row is a project's header rather than a container (DD106).
    /// </summary>
    /// <remarks>
    /// One row type and one template with a trigger, rather than two of each. The header fills the
    /// columns it has an answer for — the name, the count, its chevron — and the rest read empty,
    /// which is what the trigger is for. A header with no answer for the image column must read as
    /// blank and never as a container with no image.
    /// </remarks>
    public bool IsProject { get; init; }

    /// <summary>Whether this row is a container. The complement, spelled for markup.</summary>
    public bool IsContainer => !IsProject;

    /// <summary>How many of the project's shown containers are running.</summary>
    public int Running { get; init; }

    /// <summary>How many containers the project is showing.</summary>
    public int Total { get; init; }

    /// <summary>Whether the project's children are hidden.</summary>
    public bool Collapsed { get; init; }

    /// <summary>
    /// The disclosure glyph, in the two codepoints Segoe MDL2 Assets spells a chevron with.
    /// </summary>
    /// <remarks>
    /// The same pair claude-tray's call tree uses, because that window is this project's reference
    /// for interface formatting and a second glyph vocabulary would read as a second application. It
    /// is the affordance and not decoration: a row that opens and one that does not must not look
    /// alike.
    /// </remarks>
    public string Chevron => !IsProject ? "" : Collapsed ? "" : "";

    /// <summary>
    /// What the header says instead of a status: how much of the project is up.
    /// </summary>
    /// <remarks>
    /// Of the containers actually under it rather than of the project as the daemon knows it. The
    /// filter can hide some, and a header describing rows that are not on screen is a count nobody
    /// can check against what they are looking at.
    /// </remarks>
    public string ProjectCount => IsProject
        ? $"{Running.ToString(System.Globalization.CultureInfo.InvariantCulture)} of "
            + $"{Total.ToString(System.Globalization.CultureInfo.InvariantCulture)} running"
        : "";

    /// <summary>How far this row is pushed in — the whole signal that it belongs to the one above.</summary>
    public System.Windows.Thickness Indent =>
        IsProject || Project is null ? default : new System.Windows.Thickness(18, 0, 0, 0);

    /// <summary>The id a project's header is reconciled by (DD70), which is not a container's.</summary>
    /// <param name="project">The project.</param>
    /// <returns>The id.</returns>
    /// <remarks>
    /// Prefixed rather than the bare name, so a header can never collide with a container id — and
    /// so <see cref="LiveRows{T}"/>'s arrive-and-leave fade works on projects with nothing added to
    /// it, a project appearing being exactly the event that fade exists to show.
    /// </remarks>
    public static string ProjectId(string project) => "compose:" + project;

    /// <summary>Build a project's header row.</summary>
    /// <param name="project">The project's name.</param>
    /// <param name="children">The containers shown under it.</param>
    /// <param name="collapsed">Whether its children are hidden.</param>
    /// <returns>The header.</returns>
    public static ContainerRow ProjectHeader(
        string project, IReadOnlyList<ContainerRow> children, bool collapsed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentNullException.ThrowIfNull(children);

        return new ContainerRow(project, "", "", "", [], ProjectId(project))
        {
            IsProject = true,
            Project = project,
            Running = children.Count(child => child.IsRunning),
            Total = children.Count,
            Collapsed = collapsed,
        };
    }

    /// <summary>
    /// What this row is waiting for — <c>Stopping…</c> — or <see langword="null"/> when idle.
    /// </summary>
    /// <remarks>
    /// Set the moment the button is pressed and cleared by the event that confirms it, because a
    /// stop can take the daemon's full ten-second grace period and a row that sits unchanged for
    /// ten seconds reads as a click that did nothing.
    /// </remarks>
    public string? Pending { get; init; }

    /// <summary>The engine's own sentence about why the last action failed, or nothing.</summary>
    public string? Failure { get; init; }

    /// <summary>Whether this container is running now.</summary>
    public bool IsRunning => State.Equals("running", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the container is up in any sense the four verbs care about.
    /// </summary>
    /// <remarks>
    /// Paused and restarting are not <c>running</c> and are not stopped either. Offering "Start" for
    /// them would be a button whose only outcome is a 409 from the daemon.
    /// </remarks>
    public bool IsLive => State.Equals("running", StringComparison.OrdinalIgnoreCase)
        || State.Equals("paused", StringComparison.OrdinalIgnoreCase)
        || State.Equals("restarting", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether an action is in flight on this row.</summary>
    public bool IsPending => Pending is not null;

    /// <summary>Whether the last action on this row failed.</summary>
    public bool HasFailure => Failure is not null;

    /// <summary>
    /// Whether anything this row stands for is up (DD107).
    /// </summary>
    /// <remarks>
    /// The one place a project's verb differs from a container's, and everything below reads it
    /// rather than <see cref="IsLive"/>: a header carries no state of its own, so what decides
    /// whether it offers Stop or Start is how many of its containers are running. One partly-up
    /// project offers Stop, because the button that finishes the job is the useful one.
    /// </remarks>
    public bool AnyUp => IsProject ? Running > 0 : IsLive;

    /// <summary>Whether the row offers Start.</summary>
    public bool CanStart => !IsPending && !AnyUp;

    /// <summary>Whether the row offers Stop.</summary>
    public bool CanStop => !IsPending && AnyUp;

    /// <summary>Whether the row offers Restart.</summary>
    public bool CanRestart => !IsPending && AnyUp;

    /// <summary>Whether the row offers Remove. Anything not already busy can be removed.</summary>
    /// <remarks>
    /// A project too, since DD107 gave the header verbs of its own — the removal is fanned across
    /// its containers and its dialog names the count rather than a container.
    /// </remarks>
    public bool CanRemove => !IsPending;

    /// <summary>
    /// Whether a shell can be opened: running, and nothing else in flight.
    /// </summary>
    /// <remarks>
    /// Running and not merely live. <c>docker exec</c> against a paused container is accepted and
    /// then hangs until somebody unpauses it, with no window and no error — which is worse than a
    /// button that is visibly off.
    /// </remarks>
    public bool CanShell => IsContainer && !IsPending && IsRunning;

    /// <summary>
    /// Why the Shell button is off, or what it does when it is on.
    /// </summary>
    /// <remarks>
    /// Unlike the other four, this one is shown disabled rather than hidden, so it owes an
    /// explanation: a disabled control that says why is a limitation, and one that does not is a
    /// bug the user is left to guess at.
    /// </remarks>
    public string ShellReason => this switch
    {
        // Before the pending case: a project has no shell whether or not something is in flight on
        // it, and "wait for the action" would promise one that never arrives.
        { IsProject: true } => "A project has no shell. Open one on a container under it.",
        { IsPending: true } => "Wait for the action already running on this container.",
        { IsRunning: true } => "Open a shell in this container.",
        { State: "paused" } => "This container is paused. Unpause it before opening a shell.",
        { State: "restarting" } => "This container is restarting. Wait for it to come up.",
        _ => "This container is not running. Start it before opening a shell.",
    };


    /// <summary>
    /// What the state chip asserts, in the three tones a glance tells apart (DD36).
    /// </summary>
    /// <remarks>
    /// A clean exit is muted rather than red: it stopped because it was finished, and a migration
    /// container that did its job is not a problem to draw attention to. Only a non-zero exit is bad,
    /// which is the distinction the tertiary grey it used to be drawn in could not make.
    /// </remarks>
    public RowTone Tone => this switch
    {
        { IsRunning: true } => RowTone.Good,
        { State: "paused" } or { State: "restarting" } or { State: "created" } => RowTone.Warn,
        _ when ExitCode is 0 => RowTone.Muted,
        { IsLive: false } => RowTone.Bad,
        _ => RowTone.Muted,
    };

    /// <summary>
    /// The exit code the daemon put in the status line, where it put one there.
    /// </summary>
    /// <remarks>
    /// Parsed out of <c>Exited (137) 12 seconds ago</c> rather than asked for: it is already in the
    /// list response, and an inspect per row to read one integer is the call this window does not make.
    /// </remarks>
    public int? ExitCode
    {
        get
        {
            var open = Status.IndexOf('(', StringComparison.Ordinal);
            var close = Status.IndexOf(')', StringComparison.Ordinal);
            return open >= 0 && close > open + 1
                   && int.TryParse(
                       Status.AsSpan(open + 1, close - open - 1),
                       System.Globalization.NumberStyles.Integer,
                       System.Globalization.CultureInfo.InvariantCulture,
                       out var code)
                ? code
                : null;
        }
    }

    /// <summary>
    /// Why the chip says what it says.
    /// </summary>
    /// <remarks>
    /// A chip is an assertion, so it owes evidence — and the evidence has to be more than the status
    /// column already shows, or the tooltip is the same sentence twice. 137 is the one worth spelling
    /// out: it is SIGKILL, it is what the kernel's memory limit looks like from here, and it is the
    /// exit code the diagnostic half of this product exists for.
    /// </remarks>
    public string StateEvidence => this switch
    {
        { IsRunning: true } => Status.Length > 0 ? Status : "The daemon reports this container running.",
        { State: "paused" } => "Paused: its processes are frozen, not stopped.",
        { State: "restarting" } => "Restarting: the daemon is bringing it back up.",
        { State: "created" } => "Created and never started.",
        { ExitCode: 0 } => "Exited 0: it finished and meant to.",
        { ExitCode: 137 } => "Exited 137: SIGKILL. Usually the memory limit, sometimes a stop that "
            + "ran out of its grace period.",
        { ExitCode: { } code } => $"Exited {code.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            + ": it stopped and did not mean to. Its log is the next thing to read.",
        _ => Status,
    };

    /// <summary>
    /// Whether the image this container runs on has left the store (DD167).
    /// </summary>
    /// <remarks>
    /// The one fact that explains a digest in this column and an images page holding nothing in
    /// use: those are two correct answers to the same question, and without this they read as two
    /// lists disagreeing.
    /// </remarks>
    public bool ImageIsGone { get; init; }

    /// <summary>What the note beside the image says, or nothing where the image is still there.</summary>
    public string ImageNote => ImageIsGone ? "image gone" : "";

    /// <summary>
    /// Why the note says it, and what to do about it.
    /// </summary>
    /// <remarks>
    /// A note is an assertion and owes its evidence, the same debt <see cref="StateEvidence"/> pays.
    /// The last sentence is the actionable half: a restart reuses the image the container already
    /// holds, so the reflex that would fix almost anything else does nothing here.
    /// </remarks>
    public string ImageEvidence => ImageIsGone
        ? "This container's image is no longer in the image list: it was rebuilt or removed "
          + "while the container kept running, so the daemon has only its digest left to report. "
          + "Recreating the container is what picks up the current image; restarting it will not."
        : "";

    /// <summary>The fill the chip is drawn with, set once per render from <see cref="RowStyle"/>.</summary>
    public System.Windows.Media.Brush? ChipFill { get; init; }

    /// <summary>What the chip's word is written in.</summary>
    public System.Windows.Media.Brush? ChipText { get; init; }

    /// <summary>
    /// The one verb this row is opened for, beside Logs.
    /// </summary>
    /// <remarks>
    /// The other three moved behind the overflow (DD36): six word captions per row is two hundred of
    /// them on a list of forty, and the eye has nothing to skip past. Which one this is depends on the
    /// state, because a running container is opened to be stopped and a stopped one to be started.
    /// </remarks>
    public string PrimaryVerb => CanStop ? "Stop" : "Start";

    /// <summary>Whether the primary verb is offered at all.</summary>
    public bool HasPrimary => CanStop || CanStart;

    /// <summary>
    /// Whether the Logs button is drawn, and how it is not (DD107).
    /// </summary>
    /// <remarks>
    /// Hidden rather than collapsed, which is the whole reason this is a
    /// <see cref="System.Windows.Visibility"/> and not another <c>bool</c> through the converter the
    /// rest of the row uses. A project has no log of its own, but collapsing the button closes the
    /// gap and shifts the header's Stop a button's width left of every Stop below it — and a control
    /// that does not sit under the one it matches is what L3 is about. The space is kept; only the
    /// button goes.
    /// </remarks>
    public System.Windows.Visibility LogsVisibility =>
        IsContainer ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;

    /// <summary>Dress this row in the theme's brushes.</summary>
    /// <param name="style">The brushes, resolved once for the whole render.</param>
    /// <returns>The row, with its chip filled in.</returns>
    public ContainerRow WithChip(RowStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return this with { ChipFill = style.Fill(Tone), ChipText = style.Text(Tone) };
    }


    /// <summary>
    /// Which of this type's members belong to which of the two rows it describes (DD111).
    /// </summary>
    /// <remarks>
    /// DD106 chose one row type and one template with a trigger, and that was right — two templates
    /// would mean two column layouts, and the guard pairing a header's columns with its rows exists
    /// because those drift. What followed by accretion was the type: seven members that mean nothing
    /// on a container sat beside a dozen that mean nothing on a header, with nothing saying which was
    /// which, and three guards written one at a time as each member was found to be wrong.
    ///
    /// <para><b>This is the rule the next member is checked against</b>, which is the part that was
    /// actually missing. Every public member has to be named in exactly one of these three sets, so
    /// adding one without deciding what a header answers for it fails a test instead of being
    /// discovered by looking at the window — which is how <see cref="AnyUp"/> was found.</para>
    /// </remarks>
    public static class Members
    {
        /// <summary>What only a container answers. A header returns each of these empty.</summary>
        public static readonly IReadOnlySet<string> OfAContainer =
            new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(Image), nameof(State), nameof(Status), nameof(Ports), nameof(Service),
                nameof(DependsOn), nameof(ExitCode), nameof(StateEvidence), nameof(IsRunning),
                nameof(IsLive), nameof(ChipFill), nameof(ChipText), nameof(ImageIsGone),
                nameof(ImageNote), nameof(ImageEvidence),
            };

        /// <summary>What only a header answers. A container returns each of these empty.</summary>
        public static readonly IReadOnlySet<string> OfAProject =
            new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(Running), nameof(Total), nameof(Collapsed), nameof(Chevron),
                nameof(ProjectCount),
            };

        /// <summary>
        /// What both answer, each in its own terms.
        /// </summary>
        /// <remarks>
        /// The interesting set, and the one worth reading before adding a member: these are where a
        /// header is not a container with the fields blanked out but a row with an answer of its own.
        /// <see cref="AnyUp"/> is the shape — a container reads its state and a header counts its
        /// children — and <see cref="ShellReason"/>, <see cref="PrimaryVerb"/> and
        /// <see cref="Indent"/> each differ the same way.
        /// </remarks>
        public static readonly IReadOnlySet<string> OfBoth =
            new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(Name), nameof(Id), nameof(Project), nameof(IsProject), nameof(IsContainer),
                nameof(Indent), nameof(Pending), nameof(Failure), nameof(IsPending),
                nameof(HasFailure), nameof(AnyUp), nameof(CanStart), nameof(CanStop),
                nameof(CanRestart), nameof(CanRemove), nameof(CanShell), nameof(ShellReason),
                nameof(Tone), nameof(PrimaryVerb), nameof(HasPrimary), nameof(LogsVisibility),
            };
    }

    /// <summary>The headings this list sorts on, spelled once so markup and code cannot disagree.</summary>
    public static class Columns
    {
        /// <summary>The container's name.</summary>
        public const string Name = "NAME";

        /// <summary>What it runs.</summary>
        public const string Image = "IMAGE";

        /// <summary>Running, exited, paused.</summary>
        public const string State = "STATE";

        /// <summary>The daemon's own sentence, which carries the duration.</summary>
        public const string Status = "STATUS";

        /// <summary>What it publishes.</summary>
        public const string Ports = "PORTS";
    }

    /// <summary>
    /// The order a container list opens in.
    /// </summary>
    /// <remarks>
    /// Running first, then alphabetical inside each group. The window is opened to act on something,
    /// and the things that can be stopped, shelled into or read are the running ones; a stopped
    /// container is usually looked up rather than scanned for. Creation order — what the daemon
    /// returns — answers neither question.
    ///
    /// <para>Stable either way: two rows never swap places unless their state actually changed, which
    /// matters on a list that redraws on every engine event.</para>
    /// </remarks>
    public const string DefaultColumn = Columns.State;

    /// <summary>Shape a list of rows: narrowed, then ordered.</summary>
    /// <param name="rows">What the daemon just returned.</param>
    /// <param name="shape">The sort and filter the page is holding.</param>
    /// <returns>The rows to draw.</returns>
    public static IReadOnlyList<ContainerRow> Shaped(
        IEnumerable<ContainerRow> rows, ListShape shape)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(shape);

        return Ordered(Kept(rows, shape), shape);
    }

    /// <summary>
    /// The same list, with each compose project gathered under a header of its own (DD106).
    /// </summary>
    /// <param name="rows">What the daemon just returned.</param>
    /// <param name="shape">The sort and filter the page is holding.</param>
    /// <param name="collapsed">The projects whose children are hidden.</param>
    /// <returns>Headers and rows, flat and in draw order.</returns>
    /// <remarks>
    /// <b>The filter runs before the grouping</b>, which is what keeps a header honest: a project
    /// whose containers all went produces no group at all, and a header with nothing under it is
    /// worse than no header. The project name is one of the fields the filter matches, so typing it
    /// keeps the whole project rather than emptying it.
    ///
    /// <para><b>The sort runs twice</b>, and on the same key both times: inside a project, and over
    /// the projects. A project is ordered by the child that sorts first under whatever column is
    /// being sorted — so with the default STATE sort a project holding something running sits with
    /// the running rows, which is where a reader looking for it would look. Ordering the projects
    /// alphabetically instead would put a stopped project above a running container and make the one
    /// column everybody scans stop meaning anything.</para>
    ///
    /// <para><b>A collapsed project keeps its header and drops its children</b>, so the count on the
    /// header is what is left saying anything about it. Which projects those are is the page's to
    /// hold, for DD37's reason: this list is rebuilt on every engine event, so a collapse living in
    /// the ListView would spring open while somebody was reading it.</para>
    /// </remarks>
    public static IReadOnlyList<ContainerRow> Grouped(
        IEnumerable<ContainerRow> rows, ListShape shape, IReadOnlySet<string> collapsed)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(collapsed);

        var kept = Kept(rows, shape).ToList();

        // A unit is a whole project or a single loose container, and both are ordered by the row
        // that leads them — which is what makes one comparison serve both.
        var units = new List<(ContainerRow Leads, IReadOnlyList<ContainerRow> Draws)>();

        foreach (var row in kept.Where(row => row.Project is null))
        {
            units.Add((row, [row]));
        }

        foreach (var project in kept
            .Where(row => row.Project is not null)
            .GroupBy(row => row.Project!, StringComparer.Ordinal))
        {
            var children = Ordered(project, shape);
            var header = ProjectHeader(project.Key, children, collapsed.Contains(project.Key));
            units.Add((children[0], collapsed.Contains(project.Key) ? [header] : [header, .. children]));
        }

        // Ordered by the leading row and never by the header: a header carries no state, no status
        // and no ports, so sorting on one would file every project under the empty string.
        var order = Ordered(units.Select(unit => unit.Leads), shape);
        var at = order.Select((row, index) => (row.Id, index))
            .ToDictionary(pair => pair.Id, pair => pair.index, StringComparer.Ordinal);

        return
        [
            .. units
                .OrderBy(unit => at.TryGetValue(unit.Leads.Id, out var index) ? index : int.MaxValue)
                .SelectMany(unit => unit.Draws),
        ];
    }

    /// <summary>
    /// Give a project header the wait its containers are actually under (DD108).
    /// </summary>
    /// <param name="header">The header, already carrying whatever is known about it by id.</param>
    /// <param name="rows">Every container in hand, dressed.</param>
    /// <returns>The header, waiting exactly as long as something under it is.</returns>
    /// <remarks>
    /// DD8's rule is that the event confirms an action — a 204 from <c>/stop</c> means the daemon
    /// took the call, not that the container is down. Every container row obeys it and the header
    /// could not: it has no event of its own, so DD107 ended its wait when the last call was
    /// answered and the header stopped saying <c>Stopping…</c> while all four rows under it went on
    /// saying it for the daemon's grace period.
    ///
    /// <para><b>Derived rather than stored</b>, which is what makes it right without a timer and
    /// without the header needing an event: the children already end their own waits on the events
    /// that end them, so a header that simply reads them is correct whenever they are. It also
    /// cannot hang — a container that will never emit an event is one whose own row is stuck too,
    /// and that is a defect with one cause rather than two symptoms.</para>
    ///
    /// <para><b>One container clicked on its own counts.</b> The header reports on the group, and
    /// something in the group is in flight — so it says so, in that child's own word.</para>
    /// </remarks>
    public static ContainerRow WithProjectWait(ContainerRow header, IEnumerable<ContainerRow> rows)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(rows);

        if (!header.IsProject)
        {
            return header;
        }

        var waits = rows
            .Where(row => row.IsContainer
                && string.Equals(row.Project, header.Project, StringComparison.Ordinal))
            .Select(row => row.Pending)
            .Where(pending => pending is not null)
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToList();

        return waits.Count switch
        {
            0 => header,
            1 => header with { Pending = waits[0] },
            _ => header with { Pending = ContainerAction.MixedLabel },
        };
    }

    /// <summary>
    /// The folded-away projects that the daemon still has (DD112).
    /// </summary>
    /// <param name="folded">The projects somebody folded away.</param>
    /// <param name="rows">Every container the daemon just returned.</param>
    /// <returns>The same set, without the projects that are gone.</returns>
    /// <remarks>
    /// <b>A project's absence forgets its fold, and that was a decision rather than an omission.</b>
    /// The argument for the other answer is real — somebody who folds <c>shop</c> away is saying
    /// they are not working on <c>shop</c>, and L7 says the window remembers what the user set on
    /// purpose. What settles it is that bringing a project up is an explicit act whose whole point is
    /// to run it: a header that appears already folded hides the very thing that was just asked for,
    /// and the fold it is honouring was set on a different instance of the project. L7's subjects —
    /// a window size, a sort column — have no lifecycle, and a project does.
    ///
    /// <para>It also stops the set growing for the life of the window, which is the smaller half.</para>
    ///
    /// <para><b>Only where the daemon actually answered.</b> This is called beside
    /// <c>RowActivity.Prune</c> and for its reason: an engine that went away returns no list, so
    /// nothing on that path is evidence a project is gone, and narrowing against it would forget
    /// every fold in hand on the strength of one refused call.</para>
    /// </remarks>
    public static HashSet<string> FoldedStillThere(
        IReadOnlySet<string> folded, IEnumerable<ContainerRow> rows)
    {
        ArgumentNullException.ThrowIfNull(folded);
        ArgumentNullException.ThrowIfNull(rows);

        var present = rows
            .Select(row => row.Project)
            .Where(project => project is not null)
            .ToHashSet(StringComparer.Ordinal);

        return [.. folded.Where(present.Contains)];
    }

    /// <summary>Narrow a list to what the filter keeps.</summary>
    private static IEnumerable<ContainerRow> Kept(IEnumerable<ContainerRow> rows, ListShape shape) =>
        rows.Where(row => shape.Keeps(
            row.Name, row.Image, row.State, row.Status,
            string.Join(" ", row.Ports.Select(port => port.Text)),

            // DD106: typing a project's name keeps the project. Without it the one word a reader
            // knows the group by is the one word that empties the list.
            row.Project));

    /// <summary>Order a list under the shape's column.</summary>
    private static IReadOnlyList<ContainerRow> Ordered(
        IEnumerable<ContainerRow> kept, ListShape shape)
    {
        // Name is the tie-break under every column, and it is always ascending: a redraw must not
        // reshuffle rows that compare equal on whatever is being sorted, and flipping the direction
        // of the sorted column is not a reason to flip the tie-break under it.
        IOrderedEnumerable<ContainerRow> ordered = shape.Column switch
        {
            Columns.Image => By(kept, r => r.Image, shape.Descending, StringComparer.OrdinalIgnoreCase),
            Columns.Status => By(kept, r => r.Status, shape.Descending, StringComparer.OrdinalIgnoreCase),

            // Published before exposed-only before none, so "which of these can I open" is the
            // question this column answers.
            Columns.Ports => By(kept, r => r.Ports.Count == 0 ? "" : r.Ports[0].Text, shape.Descending,
                StringComparer.OrdinalIgnoreCase),

            // Running before anything else, which is what makes this the default rather than a
            // straight alphabetical sort on the word.
            Columns.State => By(kept, r => r.IsRunning ? 0 : r.IsLive ? 1 : 2, shape.Descending),
            _ => By(kept, r => r.Name, shape.Descending, StringComparer.OrdinalIgnoreCase),
        };

        return [.. ordered.ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static IOrderedEnumerable<ContainerRow> By<TKey>(
        IEnumerable<ContainerRow> rows,
        Func<ContainerRow, TKey> key,
        bool descending,
        IComparer<TKey>? comparer = null) =>
        descending
            ? rows.OrderByDescending(key, comparer)
            : rows.OrderBy(key, comparer);

    /// <summary>Project one summary.</summary>
    /// <param name="container">What the API returned.</param>
    /// <returns>The row.</returns>
    public static ContainerRow From(ContainerSummary container)
    {
        ArgumentNullException.ThrowIfNull(container);

        // Deduplicated the same way the list renders them: a port published on both address
        // families comes back twice and would otherwise be two identical cells.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ports = new List<PortLink>();
        foreach (var port in container.Ports)
        {
            var link = PortLink.From(port);
            if (seen.Add(link.Text))
            {
                ports.Add(link);
            }
        }

        // Already on the list response, which is the whole reason the hierarchy costs no second call
        // (DD106). Blank is treated as absent: a label present and empty names no project, and a
        // group headed by the empty string is a group nobody can read.
        var project = Label(container, Core.Agent.ContextPack.ProjectLabel);

        return new ContainerRow(
            container.DisplayName,
            container.ImageName,
            container.State,
            container.Status,
            ports,
            container.Id)
        {
            Project = project,
            ImageIsGone = container.ImageIsGone,
            Service = Label(container, Core.Agent.ContextPack.ServiceLabel),
            DependsOn = ComposeOrder.DependenciesIn(
                Label(container, ComposeOrder.DependsOnLabel)),
        };
    }

    /// <summary>One label, or nothing where it is absent or blank.</summary>
    private static string? Label(ContainerSummary container, string name) =>
        container.Labels is { } labels
        && labels.TryGetValue(name, out var value)
        && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
}

/// <summary>What the window says when the list is empty.</summary>
/// <param name="Headline">One line, large.</param>
/// <param name="Detail">One sentence under it.</param>
/// <param name="OffersStart">Whether the empty state should offer to start the engine.</param>
public sealed record EmptyState(string Headline, string Detail, bool OffersStart)
{
    /// <summary>
    /// The empty state for an engine in <paramref name="engine"/>.
    /// </summary>
    /// <remarks>
    /// Empty is a designed state and not a blank grid. The first screen a new user sees is usually
    /// this one, and a table with headers and nothing under them is where a free alternative loses
    /// them — so the two reasons a list is empty say different things, and only one of them is
    /// something the user can act on.
    /// </remarks>
    /// <param name="engine">What the engine is doing.</param>
    /// <returns>What to show.</returns>
    public static EmptyState For(EngineState engine) => engine switch
    {
        EngineState.Running => new EmptyState(
            "No containers",
            "The engine is running and has nothing to show. Start one and it appears here.",
            OffersStart: false),

        EngineState.Starting => new EmptyState(
            "Starting the engine",
            "This takes a few seconds while WSL2 boots the distribution.",
            OffersStart: false),

        _ => new EmptyState(
            "The engine is not running",
            "Start it to see your containers.",
            OffersStart: true),
    };
}
