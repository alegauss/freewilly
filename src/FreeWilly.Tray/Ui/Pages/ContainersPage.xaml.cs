using System.Diagnostics;
using System.IO;
using System.Windows;
using FreeWilly.Core.Api;
using FreeWilly.Core.Engine;

namespace FreeWilly.Tray.Ui.Pages;

/// <summary>
/// The containers list, and everything that is only about containers (DD35).
/// </summary>
/// <remarks>
/// It owns its header, its rows, the four verbs, the log windows it opens and the shell it launches.
/// What it does not own is the engine banner or the strip above it: the shell owns the chrome, and a
/// list owns its page. Before this, one window class held three of these, and DD12 and networks were
/// each about to add a fourth copy of the same stanza.
///
/// <para>Built the first time it is navigated to and then kept, collapsed, for the life of the window
/// — so a scroll position and a list already read survive switching away. That is claude-tray's
/// destination host, and it is why this is a <see cref="System.Windows.Controls.UserControl"/> rather
/// than a <c>TabItem</c>, which tears its content down on every switch.</para>
/// </remarks>
internal partial class ContainersPage : System.Windows.Controls.UserControl
{
    private readonly IEngineClient _api;
    private readonly Func<EngineState> _engineState;
    private readonly Action _startEngine;
    private readonly RowActivity _activity = new();
    private readonly Dictionary<string, LogWindow> _logs = new(StringComparer.Ordinal);

    /// <summary>Construct the page.</summary>
    /// <param name="api">The Engine API client.</param>
    /// <param name="engineState">What the engine is doing, asked at render time.</param>
    /// <param name="startEngine">What the empty state's button does.</param>
    internal ContainersPage(IEngineClient api, Func<EngineState> engineState, Action startEngine)
    {
        InitializeComponent();
        _api = api;
        _engineState = engineState;
        _startEngine = startEngine;
    }

    /// <summary>
    /// Re-read the list. Called when an event says the list changed, never on a timer and never
    /// from a refresh button: the stream is what makes this correct.
    /// </summary>
    /// <param name="settled">
    /// The container an event just arrived for, whose pending state that event confirms. This is
    /// what ends a wait — not the HTTP response, which only says the daemon took the call.
    /// </param>
    /// <returns>A task that completes when the window has been redrawn.</returns>
    internal async Task RefreshAsync(string? settled = null)
    {
        if (settled is not null)
        {
            _activity.Settled(settled);
        }

        var engine = _engineState();

        IReadOnlyList<ContainerRow> rows = [];
        if (engine is EngineState.Running)
        {
            try
            {
                var containers = await _api.ContainersAsync().ConfigureAwait(true);

                // Projected once, and the order is the whole of DD110: the prune used to reach for
                // `ContainerRow.From` to read each container's project label and then this line
                // built the same rows again three lines below, so every row was made twice per
                // refresh and half of them discarded after one property was read.
                var projected = containers.Select(ContainerRow.From).ToList();

                // Before the dressing and never after, because `Dress` reads exactly the state this
                // is about to drop. The project headers' ids go in too (DD107): a header carries its
                // own count of refusals and the daemon has never heard of `compose:shop`, so pruning
                // against container ids alone would forget it on the very next event.
                _activity.Prune(
                [
                    .. projected.Select(row => row.Id),
                    .. projected
                        .Select(row => row.Project)
                        .Where(project => project is not null)
                        .Distinct(StringComparer.Ordinal)
                        .Select(project => ContainerRow.ProjectId(project!)),
                ]);

                // The page's other keyed set, narrowed here and nowhere else, for the reason above:
                // this is the one place the daemon has actually answered, so it is the only evidence
                // that a project is gone rather than unreachable (DD112).
                _collapsed = ContainerRow.FoldedStillThere(_collapsed, projected);

                // Resolved once for the whole render rather than per row: this list is rebuilt on
                // every engine event, and a FindResource per row is a dictionary walk per row.
                var style = RowStyle.For(this);
                rows = [.. projected.Select(_activity.Dress).Select(row => row.WithChip(style))];
            }
            catch (DockerApiException)
            {
                // The engine went away between the event and this call. The empty state below says
                // so, which is more use than a dialog about a race the user did not cause.
                //
                // Deliberately no prune on this path: there is no list, so nothing here is evidence
                // that a container went away — and pruning against nothing would forget every wait
                // and every failure in hand on the strength of one refused call.
                rows = [];
            }
        }

        _rows = rows;
        Show();
    }

    /// <summary>What the daemon last returned, before the sort and the filter are applied.</summary>
    /// <remarks>
    /// Kept so a heading click and a keystroke redraw from these rather than asking the engine again:
    /// the answer is already here, and the question is about presentation.
    /// </remarks>
    private IReadOnlyList<ContainerRow> _rows = [];

    /// <summary>The list, reconciled rather than replaced, so a new row can say so (DD70).</summary>
    private LiveRows<ContainerRow>? _liveRows;

    private LiveRows<ContainerRow> _live =>
        _liveRows ??= new LiveRows<ContainerRow>(Containers, row => row.Id);

    /// <summary>Draw the rows in hand, shaped.</summary>
    private void Show()
    {
        // The headers are made by the grouping, so they are dressed after it rather than with the
        // containers: the count that came back refused is state about the header, and there is no
        // header until here (DD107). Its wait is not stored beside that count — it is read off the
        // containers, which end their own waits on the events that end them (DD108).
        var shown = ContainerRow.Grouped(_rows, _shape, _collapsed);
        shown =
        [
            .. shown.Select(row => row.IsProject
                ? ContainerRow.WithProjectWait(_activity.Dress(row), _rows)
                : row),
        ];

        _live.Show(shown);
        NameHeading.Content = ContainerRow.Columns.Name + _shape.GlyphFor(ContainerRow.Columns.Name);
        ImageHeading.Content = ContainerRow.Columns.Image + _shape.GlyphFor(ContainerRow.Columns.Image);
        StateHeading.Content = ContainerRow.Columns.State + _shape.GlyphFor(ContainerRow.Columns.State);
        StatusHeading.Content = ContainerRow.Columns.Status + _shape.GlyphFor(ContainerRow.Columns.Status);
        PortsHeading.Content = ContainerRow.Columns.Ports + _shape.GlyphFor(ContainerRow.Columns.Ports);

        var empty = shown.Count == 0;
        Containers.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        HeaderRow.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        Empty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

        if (!empty)
        {
            return;
        }

        // The third empty state. "No containers" and "nothing matched api" are different answers, and
        // only one of them is fixed by clearing a box.
        if (_shape.EmptyBecauseFiltered("containers") is var (headline, detail) && _rows.Count > 0)
        {
            (EmptyHeadline.Text, EmptyDetail.Text) = (headline, detail);
            EmptyStart.Visibility = Visibility.Collapsed;
            return;
        }

        var state = EmptyState.For(_engineState());
        EmptyHeadline.Text = state.Headline;
        EmptyDetail.Text = state.Detail;
        EmptyStart.Visibility = state.OffersStart ? Visibility.Visible : Visibility.Collapsed;
    }


    /// <summary>
    /// The sort and the filter, held by the page rather than by the controls.
    /// </summary>
    /// <remarks>
    /// This is the part DD37 calls easy to get wrong. The list redraws on every engine event, so a
    /// shape that lived only in the ListView would be thrown away each time and snap back to its
    /// default while somebody was reading it.
    /// </remarks>
    private ListShape _shape = new(ContainerRow.DefaultColumn, Descending: false);

    /// <summary>
    /// The projects the user has folded away (DD106).
    /// </summary>
    /// <remarks>
    /// Beside <see cref="_shape"/> and for exactly its reason: DD37 says presentation cannot live in
    /// the ListView because the list is rebuilt on every engine event, and a collapse held there
    /// would spring open while somebody was reading it. Keyed by the project's name, which is the
    /// only handle a project has — and narrowed on every answered refresh, so a project's absence
    /// forgets its fold rather than holding it against the next one to use that name (DD112).
    /// </remarks>
    private HashSet<string> _collapsed = new(StringComparer.Ordinal);

    /// <summary>Fold a project away, or open it again.</summary>
    /// <remarks>
    /// Redrawn from the rows in hand: which rows are hidden is presentation, and asking the daemon
    /// about it would be a round trip to answer a question this window already knows.
    /// </remarks>
    private void ToggleProject(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ContainerRow { IsProject: true, Project: { } project } })
        {
            return;
        }

        if (!_collapsed.Add(project))
        {
            _ = _collapsed.Remove(project);
        }

        Show();
    }

    /// <summary>Re-sort on a heading click, and redraw from the rows already in hand.</summary>
    private void SortBy(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string column })
        {
            return;
        }

        // A size reads best biggest-first and a name does not, so a column starts at its own natural
        // direction rather than at whatever the last one happened to be.
        _shape = _shape.Toggled(column, descendsFirst: column is ContainerRow.Columns.Ports);
        Show();
    }

    /// <summary>Re-narrow as it is typed, over the rows in hand.</summary>
    private void FilterChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _shape = _shape.Narrowed((sender as System.Windows.Controls.TextBox)?.Text);
        Show();
    }

    private void OpenPort(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && url.Length > 0)
        {
            // The whole reason ports are links: making somebody retype localhost:8080 is a small
            // daily tax a GUI exists to remove.
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
    }

    private void StartEngine(object sender, RoutedEventArgs e) => _startEngine();

    /// <summary>
    /// Open this container's log, or bring the window already open on it to the front.
    /// </summary>
    /// <remarks>
    /// One window per container, keyed by id. Pressing Logs twice on the same row opening a second
    /// window would mean two streams against one container and two buffers filling in parallel.
    /// </remarks>
    private void OpenLogs(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ContainerRow row })
        {
            return;
        }

        if (_logs.TryGetValue(row.Id, out var open))
        {
            open.Activate();
            return;
        }

        var shell = Window.GetWindow(this) as MainWindow;
        var window = new LogWindow(_api, row.Id, row.Name) { Owner = shell };

        // DD39: the child stays centred on its owner, which is right for a child — it is the size that
        // was being thrown away. A log read at half the screen came back at the markup's 900x600 every
        // time, and it is set before Show so there is no resize to watch.
        if (shell?.Recall.LogSize is { } size)
        {
            window.Width = size.Width;
            window.Height = size.Height;
        }

        _logs[row.Id] = window;
        window.Closing += (_, _) =>
            shell?.Recall.RememberLogSize(window.ActualWidth, window.ActualHeight);
        window.Closed += (_, _) => _logs.Remove(row.Id);
        window.Show();
    }

    /// <summary>
    /// Ask the container which shell it has, then hand the terminal the user already has a
    /// <c>docker exec</c> against it.
    /// </summary>
    /// <remarks>
    /// The probe is why the row goes pending first: two round trips to the daemon before any window
    /// appears, and a click that looks like it did nothing for that long is a click people press
    /// again. An image with no shell says so on the row rather than opening a terminal that closes.
    /// </remarks>
    private async void OpenShell(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ContainerRow row })
        {
            return;
        }

        _activity.Began(row.Id, ContainerVerb.Shell);
        Redress();
        try
        {
            var shell = await ContainerShell
                .FindAsync((command, token) => _api.RunInContainerAsync(row.Id, command, token))
                .ConfigureAwait(true);

            if (shell is null)
            {
                _activity.Failed(row.Id, ContainerShell.NoShellMessage(row.Name));
                return;
            }

            var launch = ContainerShell.LaunchFor(
                Terminals.Choose(File.Exists), new EnginePaths().DockerCli, row.Id, shell);

            var start = new ProcessStartInfo(launch.FileName) { UseShellExecute = false };
            foreach (var argument in launch.Arguments)
            {
                start.ArgumentList.Add(argument);
            }

            Process.Start(start)?.Dispose();
            _activity.Settled(row.Id);
        }
        catch (DockerApiException failure)
        {
            _activity.Failed(row.Id, failure.Detail ?? failure.Message);
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception
            or InvalidOperationException or IOException)
        {
            // The terminal is not where it was expected, or Windows refused to start it. The row is
            // where the click was, so the row is where this goes.
            _activity.Failed(row.Id, $"the terminal would not start: {failure.Message}");
        }
        finally
        {
            Redress();
        }
    }

    /// <summary>
    /// The one verb the row shows, which is Stop for a live container and Start for anything else.
    /// </summary>
    /// <remarks>
    /// Decided from the row rather than from two buttons with a visibility binding each: the row
    /// already knows which verb it is offering, and reading it in two places is how the two answers
    /// drift apart.
    /// </remarks>
    private void PrimaryAction(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ContainerRow row })
        {
            Send(row, row.CanStop ? ContainerVerb.Stop : ContainerVerb.Start);
        }
    }

    /// <summary>
    /// Open the row's overflow, on a left click rather than only on a right one.
    /// </summary>
    /// <remarks>
    /// A context menu that only answers the right button is the discovery problem the section warns
    /// about wearing a different hat. Placed under the button so it reads as belonging to that row and
    /// not to wherever the pointer happened to be.
    /// </remarks>
    private void OpenOverflow(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { ContextMenu: { } menu } button)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.DataContext = button.Tag;
        menu.IsOpen = true;
    }

    private void StartContainer(object sender, RoutedEventArgs e) =>
        Act(sender, ContainerVerb.Start);

    private void StopContainer(object sender, RoutedEventArgs e) =>
        Act(sender, ContainerVerb.Stop);

    private void RestartContainer(object sender, RoutedEventArgs e) =>
        Act(sender, ContainerVerb.Restart);

    private void RemoveContainer(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ContainerRow row })
        {
            return;
        }

        if (ContainerAction.AsksBeforeRemoving(row))
        {
            var answer = System.Windows.MessageBox.Show(
                Window.GetWindow(this),
                ContainerAction.RemovalPrompt(row),
                "Remove container",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning,

                // The safe answer is the one Enter and Escape both land on.
                System.Windows.MessageBoxResult.No);

            if (answer is not System.Windows.MessageBoxResult.Yes)
            {
                return;
            }
        }

        // force is what the dialog was about: without it the daemon answers 409 for a running
        // container and the row would say so after the user already agreed to the kill. On a project
        // it is decided per child rather than here — the fan-out asks each container whether it is
        // live, so a stopped one is not removed under a flag it never needed.
        Send(row, ContainerVerb.Remove, force: row.IsProject || row.IsLive);
    }

    private void Act(object sender, ContainerVerb verb)
    {
        if (sender is FrameworkElement { Tag: ContainerRow row })
        {
            Send(row, verb);
        }
    }

    private void Send(ContainerRow row, ContainerVerb verb, bool force = false)
    {
        if (row.IsProject)
        {
            SendProject(row, verb, force);
            return;
        }

        // Pending first, and without asking the daemon anything: this is the half-second between
        // the click and the engine's first word, and it is the half-second the row has to account
        // for.
        _activity.Began(row.Id, verb);
        Redress();
        _ = CallAsync(row.Id, verb, force);
    }

    /// <summary>
    /// Do one verb to every container of a project (DD107).
    /// </summary>
    /// <remarks>
    /// DD8's four verbs fanned across the children, and no new engine surface: <c>docker compose
    /// stop</c> wants the project's files and this window holds a container list, not a working
    /// directory. Every child id is already in hand.
    ///
    /// <para><b>In order</b>, which is the difference between this and a loop: what is depended on
    /// goes first on the way up and on a restart, and last on the way down, and
    /// <see cref="ComposeOrder"/> reads that off the label the containers already carry. Sequential
    /// for the same reason — a fan-out that issued all four calls at once would have computed an
    /// order and then not used it.</para>
    ///
    /// <para><b>The parent's wait ends when the calls do</b>, not when the containers are down. Each
    /// child keeps DD8's own rule and stays pending until its event confirms it; the header has no
    /// event of its own to wait for, and leaving it spinning until the last child settled would hang
    /// it forever on a container that will never emit one.</para>
    /// </remarks>
    private void SendProject(ContainerRow header, ContainerVerb verb, bool force)
    {
        var children = _rows
            .Where(row => string.Equals(row.Project, header.Project, StringComparison.Ordinal))
            .ToList();

        // Which end wants the dependency first is the verb's question, and it is asked where the
        // ordering lives (DD114) — the condition used to be here and split on Start against
        // everything else, which put a restart on the wrong side.
        var ordered = ComposeOrder.For(verb, children);

        // The header's own last refusal count goes, because pressing the button is how somebody
        // says they have read it — but nothing marks the header pending here. Its wait is the
        // children's, and they are about to have one (DD108).
        _activity.Acknowledged(header.Id);
        foreach (var child in ordered)
        {
            _activity.Began(child.Id, verb);
        }

        Redress();
        _ = FanOutAsync(header.Id, ordered, verb, force);
    }

    private async Task FanOutAsync(
        string headerId, IReadOnlyList<ContainerRow> ordered, ContainerVerb verb, bool force)
    {
        var refused = 0;
        foreach (var child in ordered)
        {
            try
            {
                await ContainerAction
                    .InvokeAsync(_api, verb, child.Id, force && child.IsLive)
                    .ConfigureAwait(true);
            }
            catch (DockerApiException failure)
            {
                // On the child, in the engine's words — DD8's line is per row and stays there. Three
                // of four stopped is not the project's failure, it is one container's, and the
                // header repeating that container's sentence would say it twice and name neither.
                _activity.Failed(child.Id, failure.Detail ?? failure.Message);
                refused++;
            }
        }

        if (refused > 0)
        {
            // A count and a pointer, never a sentence borrowed from one child.
            var some = refused.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var all = ordered.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _activity.Failed(
                headerId,
                $"{some} of {all} were refused. The row that failed says why.");
        }

        Redress();
    }

    private async Task CallAsync(string id, ContainerVerb verb, bool force)
    {
        try
        {
            await ContainerAction.InvokeAsync(_api, verb, id, force).ConfigureAwait(true);
        }
        catch (DockerApiException failure)
        {
            // On the row, in the engine's words. Detail is those words alone; Message wraps them in
            // the endpoint, which a log wants and a row does not. A dialog would take the message
            // away from the place the user was looking when they pressed the button.
            _activity.Failed(id, failure.Detail ?? failure.Message);
            Redress();
        }
    }

    /// <summary>
    /// Redraw the rows already on screen against what is now known about them, with no call to the
    /// daemon. The list itself has not changed; what a row is waiting for has.
    /// </summary>
    private void Redress()
    {
        // Through the rows in hand and the same Show(), so a row going pending keeps the order and
        // the filter it was drawn under. Redressing the ListView's own items would drop both.
        _rows = [.. _rows.Select(_activity.Dress)];
        Show();
    }
}
