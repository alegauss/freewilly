using System.Globalization;
using System.Windows;
using FreeWilly.Core.Builds;

namespace FreeWilly.Tray.Ui.Pages;

/// <summary>One field of the detail pane.</summary>
/// <param name="Name">What it is.</param>
/// <param name="Value">What it says.</param>
internal sealed record BuildField(string Name, string Value);

/// <summary>
/// The build history, with one build shown in full (DD126).
/// </summary>
/// <remarks>
/// <b>This page is where a printed link lands.</b> Buildx ends every build with a
/// <c>docker-desktop://</c> URL that nothing on this machine can open; the ref inside it is real, so
/// the handler this install registers resolves it here.
///
/// <para><b>The list is not decoration around that.</b> A destination reachable only by a link is a
/// destination nobody finds, and the history the daemon already keeps answers the question the link
/// was a single instance of — what has been built here, and did it use the cache.</para>
///
/// <para><b>It draws with no daemon (L6).</b> The history is a seam, and <c>--fixture</c> passes one
/// that is always there, so the empty states and the four status tones can be photographed without
/// building anything.</para>
///
/// <para><b>Reading is on a background thread.</b> Both reads shell out to the pinned Buildx, and a
/// subprocess on the UI thread is a window that stops repainting while it runs.</para>
/// </remarks>
internal partial class BuildsPage : System.Windows.Controls.UserControl
{
    private readonly IBuildHistory _history;

    /// <summary>Construct the page.</summary>
    /// <param name="history">Where the build records are read from.</param>
    internal BuildsPage(IBuildHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        InitializeComponent();
        _history = history;
    }

    /// <summary>What the history last answered, before the sort and the filter are applied.</summary>
    private IReadOnlyList<BuildRow> _rows = [];

    /// <summary>The ref the detail pane is showing, kept so a refresh does not lose the selection.</summary>
    private string? _selected;

    private LiveRows<BuildRow>? _liveRows;

    private LiveRows<BuildRow> _live =>
        _liveRows ??= new LiveRows<BuildRow>(Builds, row => row.Reference);

    /// <summary>Re-read the history and redraw.</summary>
    /// <returns>A task that completes when the page has been redrawn.</returns>
    internal async Task RefreshBuildsAsync()
    {
        var builds = await Task.Run(_history.Recent).ConfigureAwait(false);

        await OnUi(() =>
        {
            _rows = BuildRow.From(builds);
            Show();

            // The selection survives a refresh, and a build that has gone from the history takes
            // the detail with it rather than leaving a pane describing something not listed.
            if (_selected is not null && _rows.All(row => row.Reference != _selected))
            {
                _selected = null;
            }
        }).ConfigureAwait(false);

        await ShowDetailAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Run the drawing half on the thread the window belongs to.
    /// </summary>
    /// <remarks>
    /// <b>Explicit, rather than trusting a captured context.</b> <c>ConfigureAwait(true)</c> only
    /// resumes on a <see cref="SynchronizationContext"/> where one exists, and the first navigation
    /// to this page happens inside <c>MainWindow</c>'s constructor — before <c>Application.Run</c>
    /// has installed the dispatcher's. The continuation then landed on a thread-pool thread, where
    /// <see cref="RowStyle.For"/> built the chip brushes; binding one of those threw
    /// <c>DependencySource must be created on the same thread as the DependencyObject</c> and took
    /// the process with it.
    ///
    /// <para>Found by <c>--capture-window</c> and by nothing else — the suite never touches a
    /// dispatcher, which is the case DD34's "capture before and after" is written for.</para>
    /// </remarks>
    /// <param name="draw">What touches the window.</param>
    /// <returns>A task that completes once it has run.</returns>
    private Task OnUi(Action draw) =>
        Dispatcher.CheckAccess()
            ? RunNow(draw)
            : Dispatcher.InvokeAsync(draw).Task;

    private static Task RunNow(Action draw)
    {
        draw();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Show one build, named by the link that was opened (DD126).
    /// </summary>
    /// <param name="reference">The ref, as <see cref="BuildAddress.RefIn"/> read it.</param>
    /// <returns>A task that completes when the page has been redrawn.</returns>
    /// <remarks>
    /// The list is re-read first, so the row the link names is selected in it rather than the detail
    /// appearing under a list that does not contain it.
    /// </remarks>
    internal async Task ShowBuildAsync(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        _selected = reference;
        var builds = await Task.Run(_history.Recent).ConfigureAwait(false);

        await OnUi(() =>
        {
            _rows = BuildRow.From(builds);
            Show();
        }).ConfigureAwait(false);

        await ShowDetailAsync().ConfigureAwait(false);
    }

    /// <summary>Draw the rows in hand, shaped.</summary>
    private void Show()
    {
        var style = RowStyle.For(this);
        var shown = BuildRow
            .Shaped(_rows, _shape)
            .Select(row => (row with { IsSelected = row.Reference == _selected }).WithChip(style))
            .ToList();

        _live.Show(shown);

        BuildTotalsLine.Text = _rows.Count switch
        {
            0 => "",
            1 => "1 build",
            var many => string.Create(CultureInfo.InvariantCulture, $"{many} builds"),
        };

        NameHeading.Content = BuildRow.Columns.Name + _shape.GlyphFor(BuildRow.Columns.Name);
        StatusHeading.Content = BuildRow.Columns.Status + _shape.GlyphFor(BuildRow.Columns.Status);
        WhenHeading.Content = BuildRow.Columns.When + _shape.GlyphFor(BuildRow.Columns.When);
        DurationHeading.Content =
            BuildRow.Columns.Duration + _shape.GlyphFor(BuildRow.Columns.Duration);

        var empty = shown.Count == 0;
        Builds.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        BuildHeaderRow.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        BuildsEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (!empty)
        {
            return;
        }

        if (_shape.EmptyBecauseFiltered("builds") is var (headline, detail) && _rows.Count > 0)
        {
            (BuildsEmptyHeadline.Text, BuildsEmptyDetail.Text) = (headline, detail);
            return;
        }

        // One empty state, and it does not blame the engine. The history is BuildKit's and survives
        // the daemon being down, so "nothing has been built" is the honest sentence either way.
        (BuildsEmptyHeadline.Text, BuildsEmptyDetail.Text) = (
            "No builds yet",
            "A build appears here as soon as one runs. The link buildx prints at the end of a "
            + "build opens the record on this page.");
    }

    /// <summary>Read and draw the detail for whatever is selected.</summary>
    private async Task ShowDetailAsync()
    {
        if (_selected is not { } reference)
        {
            await OnUi(() => Detail.Visibility = Visibility.Collapsed).ConfigureAwait(false);
            return;
        }

        var record = await Task.Run(() => _history.Inspect(reference)).ConfigureAwait(false);

        await OnUi(() => DrawDetail(reference, record)).ConfigureAwait(false);
    }

    private void DrawDetail(string reference, BuildRecord? record)
    {
        // Another selection may have landed while the read ran. Drawing this one now would put the
        // wrong build under the highlighted row.
        if (_selected != reference)
        {
            return;
        }

        Detail.Visibility = Visibility.Visible;

        if (record is null)
        {
            DetailName.Text = "That build is not in the history";
            DetailId.Text = reference[(reference.LastIndexOf('/') + 1)..];
            DetailFields.ItemsSource = Array.Empty<BuildField>();
            DetailFailure.Text =
                "The record has expired or was pruned. A build id outlives nothing but the history "
                + "that holds it.";
            DetailFailure.Visibility = Visibility.Visible;
            return;
        }

        DetailName.Text = record.Name;
        DetailId.Text = record.Id;
        DetailFailure.Visibility = Visibility.Collapsed;
        DetailFields.ItemsSource = Fields(record);
    }

    /// <summary>
    /// The detail rows, with a field absent rather than blank where the record carries none.
    /// </summary>
    /// <remarks>
    /// Absent and not empty, which is the About page's rule for the same reason: a caption over
    /// nothing reads as a value that failed to load rather than as one that was never there. A build
    /// from a directory has no revision, and that is not a defect to display.
    /// </remarks>
    /// <param name="record">The build.</param>
    /// <returns>What the pane lists.</returns>
    internal static IReadOnlyList<BuildField> Fields(BuildRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var fields = new List<BuildField> { new("Status", record.Status) };

        if (record.StartedAt is { } started)
        {
            // Through the column's own conversion (DD193). This is the field the column defers to
            // for the exact moment, so a second spelling of the zone here is the pane and the row
            // disagreeing about when one build started.
            fields.Add(new BuildField("Started", BuildRow.Clock(started, "yyyy-MM-dd HH:mm:ss")));
        }

        if (record.CompletedAt is not null)
        {
            fields.Add(new BuildField("Duration", BuildRow.Human(record.Duration)));
        }

        fields.Add(new BuildField(
            "Steps",
            record.CachedSteps > 0
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{record.CompletedSteps}/{record.TotalSteps} · {record.CachedSteps} from cache")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{record.CompletedSteps}/{record.TotalSteps}")));

        Add(fields, "Context", record.Context);
        Add(fields, "Dockerfile", record.Dockerfile);
        Add(fields, "Repository", record.VcsRepository);
        Add(fields, "Revision", record.VcsRevision);

        if (record.Config is { } config)
        {
            Add(fields, "Resolve mode", config.ImageResolveMode);
            if (config.NoCache)
            {
                // Only when true. "No cache: false" is a row that says nothing.
                fields.Add(new BuildField("Cache", "refused for this build"));
            }
        }

        foreach (var material in record.Materials ?? [])
        {
            fields.Add(new BuildField(
                "Material",
                material.Digests is { Count: > 0 } digests
                    ? $"{material.URI}\n{digests[0]}"
                    : material.URI));
        }

        return fields;
    }

    private static void Add(List<BuildField> fields, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields.Add(new BuildField(name, value));
        }
    }

    /// <summary>The sort and the filter, held by the page rather than by the controls (DD37).</summary>
    private ListShape _shape = new(BuildRow.DefaultColumn, Descending: true);

    private void SortBy(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string column })
        {
            return;
        }

        _shape = _shape.Toggled(column, BuildRow.DescendsFirst(column));
        Show();
    }

    private void FilterChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _shape = _shape.Narrowed((sender as System.Windows.Controls.TextBox)?.Text);
        Show();
    }

    private async void BuildSelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (Builds.SelectedItem is not BuildRow row || row.Reference == _selected)
        {
            return;
        }

        _selected = row.Reference;
        await ShowDetailAsync().ConfigureAwait(true);
    }
}
