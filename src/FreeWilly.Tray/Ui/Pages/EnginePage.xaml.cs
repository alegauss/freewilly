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
    private readonly JournalView _view = new();
    private readonly DispatcherTimer _timer;

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
    /// <param name="journal">
    /// Where the journal is read from. The fixture is what makes this page capturable (L6) — the
    /// live one is a file describing whatever this machine's engine did that afternoon.
    /// </param>
    internal EnginePage(IEngineJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        InitializeComponent();
        _journal = journal;

        Lines.ItemsSource = _view.Lines;
        Where.Text = journal.Path;
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
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        };

        Reread();
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
}
