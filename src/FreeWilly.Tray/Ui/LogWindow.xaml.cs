using System.IO;
using System.Windows;
using FreeWilly.Core.Api;

namespace FreeWilly.Tray.Ui;

/// <summary>
/// One container's log, followed. The artefact a container that exited two seconds in leaves.
/// </summary>
internal partial class LogWindow : Window
{
    /// <summary>
    /// How often the status line and the scroll position are allowed to move.
    /// </summary>
    /// <remarks>
    /// A container printing a thousand chunks a second would otherwise scroll and re-render a
    /// thousand times a second, and the window that is meant to show the output would spend its
    /// time laying it out instead. The buffer still takes every chunk; only the view is throttled.
    /// </remarks>
    private const long RedrawEveryMs = 60;

    private readonly IEngineClient _api;
    private readonly string _id;
    private readonly LogBuffer _buffer = new();
    private readonly CancellationTokenSource _closing = new();
    private long _drawnAt;
    private string? _failure;
    private bool _ended;

    /// <summary>
    /// Whether the tree is built. Nothing may draw before it is.
    /// </summary>
    /// <remarks>
    /// <c>IsChecked="True"</c> on the Follow box raises <c>Checked</c> from inside
    /// <c>InitializeComponent</c>, while the parser is still walking the tree — so the handler runs
    /// before the named elements declared after it exist. Unguarded, that was a
    /// <see cref="NullReferenceException"/> in a routed-event handler, which is not caught anywhere
    /// and took the whole tray down with it the first time the button was pressed.
    /// </remarks>
    private readonly bool _ready;

    /// <summary>Construct the window and start reading.</summary>
    /// <param name="api">The Engine API client.</param>
    /// <param name="id">The container's full id.</param>
    /// <param name="name">What the container is called, for the title.</param>
    internal LogWindow(IEngineClient api, string id, string name)
    {
        InitializeComponent();
        _api = api;
        _id = id;

        Title = $"{name} logs";
        Heading.Text = name;
        Lines.ItemsSource = _buffer.Lines;
        _ready = true;
        Draw();

        Closed += (_, _) =>
        {
            // Closing the window is what ends a followed stream. Without this the read loop holds a
            // pipe connection open against a window nobody can see.
            _closing.Cancel();
            _closing.Dispose();
        };

        _ = ReadAsync();
    }

    private async Task ReadAsync()
    {
        try
        {
            // Asked, not guessed: whether the daemon frames the stream depends on whether a TTY was
            // allocated, and nothing in the bytes says so reliably.
            var container = await _api.InspectAsync(_id, _closing.Token).ConfigureAwait(true);

            await using var stream = await _api
                .LogsAsync(_id, cancellation: _closing.Token).ConfigureAwait(true);

            var frames = new LogFrames(stream, framed: !container.Config.Tty);
            while (await frames.ReadAsync(_closing.Token).ConfigureAwait(true) is { } chunk)
            {
                _buffer.Append(chunk);
                DrawThrottled();
            }

            _buffer.Close();
            _ended = true;
        }
        catch (OperationCanceledException)
        {
            // The window closed. Nothing to report to a window that is gone.
            return;
        }
        catch (Exception exception) when (exception is DockerApiException or IOException
            or ObjectDisposedException)
        {
            _failure = exception is DockerApiException docker
                ? docker.Detail ?? docker.Message
                : exception.Message;
            _ended = true;
        }

        Draw();
    }

    private void DrawThrottled()
    {
        var now = Environment.TickCount64;
        if (now - _drawnAt < RedrawEveryMs)
        {
            return;
        }

        _drawnAt = now;
        Draw();
    }

    private void Draw()
    {
        var empty = LogEmptyState.For(_buffer.IsEmpty, _ended, _failure);
        LogPane.Visibility = empty is null ? Visibility.Visible : Visibility.Collapsed;
        Empty.Visibility = empty is null ? Visibility.Collapsed : Visibility.Visible;
        if (empty is not null)
        {
            EmptyHeadline.Text = empty.Headline;
            EmptyDetail.Text = empty.Detail;
        }

        Status.Text = _buffer.Status(Follow.IsChecked is true, _ended);
        ScrollToEndIfFollowing();
    }

    private void ScrollToEndIfFollowing()
    {
        if (Follow.IsChecked is not true || _buffer.Lines.Count == 0)
        {
            return;
        }

        Lines.ScrollIntoView(_buffer.Lines[^1]);
    }

    private void FollowChanged(object sender, RoutedEventArgs e)
    {
        if (_ready)
        {
            Draw();
        }
    }

    private void CopyEverything(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(_buffer.ToText());
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another process is holding the clipboard open. Saying so beats a window that took the
            // click and did nothing, and it is not worth a dialog.
            Status.Text = "Windows would not hand over the clipboard. Try that again.";
        }
    }
}
