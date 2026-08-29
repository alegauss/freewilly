using System.Windows;
using System.Windows.Threading;
using FreeWilly.Core.Engine;

namespace FreeWilly.Tray.Ui.Pages;

/// <summary>
/// The engine's own journal, followed, on the same footing as a container's log (DD165).
/// </summary>
/// <remarks>
/// A container that exits two seconds in leaves an artefact this window will show. The engine those
/// containers run on leaves the same artefact and the window showed none of it — the file was
/// <c>engine.log</c> beside the install, which is a path a user learns by being told, and a file
/// they open in Notepad while the thing it describes carries on without them.
///
/// <para><b>It reads on a timer while it is on screen, and that is not the poll this project keeps
/// refusing to write.</b> What DD137 refuses is asking the <em>engine</em> a question on a clock —
/// every one of those is a real request over a pipe, and on a loaded machine they are part of the
/// load. This asks the filesystem for the length of a 64 KB file that is already in the cache, only
/// while somebody is looking at it. A <see cref="System.IO.FileSystemWatcher"/> was the obvious
/// alternative and is worse here: it is an OS handle per page for a file this small, and it misses
/// changes on volumes this tool does not get to choose.</para>
/// </remarks>
internal sealed partial class EnginePage : System.Windows.Controls.UserControl
{
    /// <summary>How often the file is asked whether it has moved.</summary>
    /// <remarks>
    /// A second, which is the interval at which a person watching a log notices it is stuck. The
    /// supervisor's own beat is two seconds, so nothing this page can show arrives faster than that
    /// anyway — reading twice as often as the writer writes is what keeps the page from being a
    /// second behind for reasons of its own.
    /// </remarks>
    private static readonly TimeSpan ReadEvery = TimeSpan.FromSeconds(1);

    private readonly IEngineJournal _journal;
    private readonly IMachineReport _machine;
    private readonly JournalView _view = new();
    private readonly DispatcherTimer _timer;

    private readonly IFilesystemWork _work;
    private readonly IEngineInterlude _interlude;

    /// <summary>What the readings said last, for the copy button.</summary>
    private MachineHealth? _readings;

    /// <summary>
    /// Whether the tree is built. Nothing may draw before it is.
    /// </summary>
    /// <remarks>
    /// <c>IsChecked="True"</c> on the Follow box raises <c>Checked</c> from inside
    /// <c>InitializeComponent</c>, while the parser is still walking the tree — so the handler runs
    /// before the named elements declared after it exist. The same trap <see cref="LogWindow"/>
    /// documents, and it took the whole tray down there the first time the button was pressed.
    /// </remarks>
    private readonly bool _ready;

    /// <summary>Construct the page.</summary>
    /// <param name="seams">
    /// The journal, the readings, the filesystem work and the interlude around it, which is what
    /// makes this page capturable (L6): the live ones describe whatever this machine did that
    /// afternoon, and one of them terminates a distribution.
    /// </param>
    internal EnginePage(EngineSeams seams)
    {
        ArgumentNullException.ThrowIfNull(seams);
        InitializeComponent();
        _journal = seams.Journal;
        _machine = seams.Machine;
        _work = seams.Work;
        _interlude = seams.Interlude;
        Show(RepairPrompt.Idle, steps: null);

        Lines.ItemsSource = _view.Lines;
        Where.Text = _journal.Path;
        MachineHeading.Text = Reading;
        _ready = true;

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = ReadEvery };
        _timer.Tick += (_, _) => Reread();

        // Started and stopped by visibility rather than left running for the life of the window. A
        // page kept alive collapsed is the shape every destination here has (L2), and one that goes
        // on reading a file nobody is looking at is the cost of that shape paid for nothing.
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Reread();
                _ = RereadTheMachine();
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        };

        Reread();
        _ = RereadTheMachine();
    }

    /// <summary>What the heading says while the readings are being taken.</summary>
    /// <remarks>
    /// Said rather than left blank, because taking them is several <c>wsl.exe</c> children and a
    /// pipe request: an empty panel for two seconds reads as a panel with nothing to report, which
    /// is the opposite of what it means.
    /// </remarks>
    internal const string Reading = "Reading the machine…";

    /// <summary>
    /// Take the readings, off the thread that draws them (DD197).
    /// </summary>
    /// <returns>The work.</returns>
    /// <remarks>
    /// Internal so the capture and a test can await one read rather than racing the page's own. The
    /// fixture completes without yielding, so a capture is drawn from the readings rather than from
    /// the placeholder.
    /// </remarks>
    internal async Task RereadTheMachine()
    {
        try
        {
            _readings = await _machine.ReadAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A panel describing a machine that is misbehaving is the last place an exception should
            // escape from. The page is still worth having with the journal alone.
            _readings = null;
        }

        Machine.ItemsSource = _readings?.Groups;

        // The verdict rather than a caption, and it is the same sentence `read health` prints
        // (DD198). A heading that always said the same thing was a row of numbers with no reading
        // of them, which is the work this page exists to do for somebody.
        MachineHeading.Text = _readings?.Summary ?? "Nothing could be read about this machine";
    }

    /// <summary>Read the journal and redraw where it has moved.</summary>
    /// <returns>Whether anything changed.</returns>
    /// <remarks>
    /// Internal so a test and the capture can drive one read without waiting a second for the timer.
    /// </remarks>
    internal bool Reread()
    {
        var lines = _journal.Read();
        var moved = _view.Update(lines);

        // The digest is redrawn either way. It carries a restart count, and a page that had been
        // open since before the first read would otherwise show the sentence for an empty file.
        Digest.Text = JournalDigest.Of(lines).Summary();

        var empty = EmptyState(lines.Count);
        LogPane.Visibility = empty is null ? Visibility.Visible : Visibility.Collapsed;
        Empty.Visibility = empty is null ? Visibility.Collapsed : Visibility.Visible;
        if (empty is not null)
        {
            EmptyHeadline.Text = empty.Headline;
            EmptyDetail.Text = empty.Detail;
        }

        if (moved)
        {
            ScrollToEndIfFollowing();
        }

        return moved;
    }

    /// <summary>
    /// What to show instead of the log, or <see langword="null"/> where the log is the view.
    /// </summary>
    /// <param name="held">How many lines the journal holds.</param>
    /// <returns>The empty state, or nothing.</returns>
    /// <remarks>
    /// One silence here rather than the log window's three, and it is the good one: DD137 made an
    /// absent file the deliberate answer for a machine whose engine has never been troubled. Saying
    /// so is the difference between a page that reports a healthy machine and a blank box that reads
    /// as a page which failed to load.
    /// </remarks>
    internal static LogEmptyState? EmptyState(int held) => held > 0
        ? null
        : new LogEmptyState(
            "Nothing has happened to the engine",
            "This file is written only when something does: a restart, a suspend, a stop. A "
            + "machine whose engine has simply been up leaves nothing here, and that is the "
            + "healthy answer.");

    private void ScrollToEndIfFollowing()
    {
        if (Follow.IsChecked is not true || _view.Lines.Count == 0)
        {
            return;
        }

        Lines.ScrollIntoView(_view.Lines[^1]);
    }

    private void FollowChanged(object sender, RoutedEventArgs e)
    {
        if (_ready)
        {
            ScrollToEndIfFollowing();
        }
    }

    private void CopyEverything(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(_view.ToText());
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another process is holding the clipboard open. Saying so beats a page that took the
            // click and did nothing, and it is not worth a dialog. The digest is where it is said
            // because it is the line the eye is already on.
            Digest.Text = "Windows would not hand over the clipboard. Try that again.";
        }
    }

    /// <summary>Read the filesystem and change nothing (DD199, DD210).</summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Unused.</param>
    /// <remarks>
    /// It asks first, and what it asks about is not what the repair asks about. Reading cannot make
    /// a filesystem worse, so nothing here needs consent to touch the disk; what needs consent is
    /// stopping the engine and every container on it, which a check costs whatever it finds. That
    /// was written in the panel beside the button and discovered by pressing it anyway.
    /// </remarks>
    private async void CheckTheFilesystem(object sender, RoutedEventArgs e)
    {
        var answer = System.Windows.MessageBox.Show(
            RepairPrompt.CheckConfirmation,
            "Check the filesystem",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.No);

        if (answer is System.Windows.MessageBoxResult.Yes)
        {
            await Run(wrote: false).ConfigureAwait(true);
        }
    }

    /// <summary>Mend what the check found, once somebody has said so (DD199).</summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Unused.</param>
    /// <remarks>
    /// The one place this window writes to the filesystem holding every image and volume on the
    /// machine, so it is the one place it asks. A modal here rather than a second click on a
    /// differently worded button: what is being consented to takes a paragraph to state, and a
    /// button caption is not a paragraph.
    /// </remarks>
    private async void RepairTheFilesystem(object sender, RoutedEventArgs e)
    {
        var answer = System.Windows.MessageBox.Show(
            RepairPrompt.Confirmation,
            "Repair the filesystem",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.No);

        if (answer is System.Windows.MessageBoxResult.Yes)
        {
            await Run(wrote: true).ConfigureAwait(true);
        }
    }

    /// <summary>Run one of the two, off the thread that draws the result.</summary>
    /// <param name="wrote">Whether this is the repair.</param>
    /// <returns>The work.</returns>
    /// <remarks>
    /// <para>Both buttons go dead for the duration, because both take the engine down and a second
    /// run started on top of the first would be two processes terminating one distribution. The
    /// steps are collected rather than streamed: they arrive off the UI thread, and a page that
    /// marshalled each one would be doing dispatcher work in the middle of a minutes-long
    /// <c>e2fsck</c>.</para>
    ///
    /// <para><b>The interlude brackets the whole of it (DD210).</b> Said before the work starts and
    /// not after the engine has gone, because the tray decides what an engine that stopped answering
    /// means at the moment it notices, and a claim arriving afterwards is a balloon already on its
    /// way. The start at the other end is this page finishing its own work: it took the engine down
    /// without being asked to leave it down.</para>
    /// </remarks>
    internal async Task Run(bool wrote)
    {
        Check.IsEnabled = false;
        Repair.IsEnabled = false;
        _interlude.Expected();
        Show(RepairPrompt.Working, steps: null);

        var steps = new System.Collections.Concurrent.ConcurrentQueue<RepairStep>();
        RepairOutcome outcome;
        try
        {
            outcome = await Task.Run(() => wrote
                ? _work.Fix(steps.Enqueue)
                : _work.Check(steps.Enqueue)).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            outcome = new RepairOutcome(
                [new RepairStep("run the check", false, exception.Message)]);
        }

        var prompt = RepairPrompt.Of(outcome, wrote);
        if (prompt.StartsAgain)
        {
            _interlude.StartAgain();
            prompt = prompt.AndStarting(TrayState.StartBudget);
        }

        Show(prompt, Transcript(outcome, steps));
        Check.IsEnabled = true;
        Repair.IsEnabled = true;
    }

    /// <summary>Everything the run said, as one block.</summary>
    /// <param name="outcome">What it did.</param>
    /// <param name="steps">The steps as they landed.</param>
    /// <returns>The transcript.</returns>
    private static string Transcript(
        RepairOutcome outcome, System.Collections.Concurrent.ConcurrentQueue<RepairStep> steps)
    {
        var text = new System.Text.StringBuilder();
        foreach (var step in steps)
        {
            text.Append(step.Ok ? "[ok  ]  " : "[FAIL]  ")
                .Append(step.What.PadRight(22)).Append("  ").Append(step.Detail).Append('\n');
        }

        if (outcome.Findings is { Length: > 0 } said)
        {
            text.Append('\n').Append(said);
        }

        return text.ToString();
    }

    /// <summary>Draw one prompt, and the transcript under it where there is one.</summary>
    /// <param name="prompt">What to say.</param>
    /// <param name="steps">What the run printed, or null before there was a run.</param>
    private void Show(RepairPrompt prompt, string? steps)
    {
        FoundHeadline.Text = prompt.Headline;
        FoundDetail.Text = prompt.Detail;
        FoundSteps.Text = steps ?? "";
        FoundSteps.Visibility = string.IsNullOrEmpty(steps)
            ? Visibility.Collapsed
            : Visibility.Visible;

        Found.Visibility = Visibility.Visible;
        Repair.Visibility = prompt.OfferRepair ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Hand the readings to whoever is being asked about this machine (DD197).</summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Unused.</param>
    /// <remarks>
    /// The panel's own copy and not the journal's, because they answer different questions: this is
    /// the state and that is the history, and a reader asked for one does not want the other pasted
    /// underneath it.
    /// </remarks>
    private void CopyTheMachine(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(MachineReport.AsText(_readings?.Groups ?? []));
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            MachineHeading.Text = "Windows would not hand over the clipboard. Try that again.";
        }
    }
}
