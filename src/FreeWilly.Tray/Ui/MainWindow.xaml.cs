using System.IO;
using System.Windows;
using System.Windows.Media;
using FreeWilly.Core.Api;
using FreeWilly.Core.Builds;
using FreeWilly.Core.Engine;
using FreeWilly.Core.Licensing;
using FreeWilly.Tray.Ui.Pages;

namespace FreeWilly.Tray.Ui;

/// <summary>
/// The shell: the engine's state, the About terms, and a strip over one page per list (DD35).
/// </summary>
/// <remarks>
/// It owns the chrome and nothing else. Each list is a page with its own header, its own empty states
/// and its own refresh, built the first time it is navigated to and then kept, collapsed, for the life
/// of the window — so a scroll position and a list already read survive switching away.
///
/// <para>Before this, one window class carried three lists, three header rows, three empty states, two
/// prune confirmations, the log windows and the terminal launch, in 452 lines of XAML and 698 of
/// code-behind. The three lists were not variants of one thing: they were three hand-written copies of
/// the same stanza, and DD12 and networks were each about to add a fourth.</para>
///
/// <para>The structure is claude-tray's — a nav strip and a destination host — and so is the reason
/// for it. A <c>TabControl</c> was what this replaced: it reuses one content presenter, so it tears a
/// page's visual tree down on every switch, which is the opposite of keeping one alive.</para>
/// </remarks>
internal partial class MainWindow : Window
{
    private readonly IEngineClient _api;
    private readonly Func<EngineState> _engineState;
    private readonly Action _startEngine;
    private readonly WindowRecall _recall;
    private readonly IBuildHistory _builds;
    private readonly EngineSeams _engineSeams;

    private ImagesPage? _images;
    private VolumesPage? _volumes;
    private BuildsPage? _buildsPage;
    private EnginePage? _engine;
    private AboutPage? _about;

    /// <summary>The builds destination, built on first use and then kept (DD126).</summary>
    /// <remarks>A property because two reach it: the nav strip, and <see cref="ShowBuild"/>.</remarks>
    private BuildsPage BuildsDestination => _buildsPage ??= Add(new BuildsPage(_builds));

    /// <summary>The containers list. Built with the window: it is the destination it opens on.</summary>
    internal ContainersPage Containers { get; }

    /// <summary>Construct the window.</summary>
    /// <param name="api">The Engine API client.</param>
    /// <param name="engineState">What the engine is doing, asked at render time.</param>
    /// <param name="startEngine">What the empty state's button does.</param>
    /// <param name="builds">
    /// Where build records are read from (DD126). The pinned Buildx by default; <c>--fixture</c>
    /// passes one needing no daemon, which is what makes that page capturable (L6).
    /// </param>
    /// <param name="engine">
    /// Everything the Engine destination reads and can start (DD165, DD197, DD199). This machine's
    /// by default; <c>--fixture</c> passes seams that are the same everywhere and refuse to act, for
    /// the same reason <paramref name="builds"/> takes one — the live ones describe whatever this
    /// laptop did that afternoon, which is not reviewable and not safe to put in a README.
    /// </param>
    internal MainWindow(
        IEngineClient api, Func<EngineState> engineState, Action startEngine,
        IBuildHistory? builds = null, EngineSeams? engine = null)
    {
        InitializeComponent();
        _api = api;
        _engineState = engineState;
        _startEngine = startEngine;
        _builds = builds ?? new BuildHistory();
        _engineSeams = engine ?? EngineDestination.OnThisMachine();

        Containers = new ContainersPage(api, engineState, startEngine);
        DestinationHost.Children.Add(Containers);

        // Where it was and what was being read, last time (DD39). The moves belong to WindowRecall;
        // what is this window's own is the one question it asks — which destination is ticked.
        _recall = new WindowRecall(this, new EnginePaths().WindowState, () => Showing);

        // The destination, now that there is one to show. Not in the markup: ticking a destination
        // navigates, and during InitializeComponent there is nothing built to navigate to. A name this
        // window no longer has — a page removed since the file was written — falls back rather than
        // opening on nothing.
        if (!ShowTab(_recall.Destination))
        {
            NavContainers.IsChecked = true;
        }
    }

    /// <summary>The destination being read now.</summary>
    private string Showing =>
        Strip.Children.OfType<System.Windows.Controls.RadioButton>()
            .FirstOrDefault(nav => nav.IsChecked == true)?.Tag?.ToString()
        ?? WindowMemory.FirstDestination;

    /// <summary>What a log window opened from here should be sized to, and where that is kept (DD39).</summary>
    internal WindowRecall Recall => _recall;

    /// <summary>The destinations, by the name a caller shows one with.</summary>
    /// <remarks>
    /// Read off the strip rather than restated, so a fourth page is capturable without a second edit —
    /// and so a name this window does not have can be refused instead of quietly rendering the first
    /// one into the file the caller asked for.
    /// </remarks>
    internal IReadOnlyList<string> TabNames =>
        [.. Strip.Children.OfType<System.Windows.Controls.RadioButton>()
            .Select(nav => nav.Tag?.ToString() ?? string.Empty)];

    /// <summary>Show one destination, for a capture that wants a list other than the first.</summary>
    /// <param name="header">The destination's name.</param>
    /// <returns><see langword="false"/> where this window has no such destination.</returns>
    internal bool ShowTab(string header)
    {
        var found = Strip.Children.OfType<System.Windows.Controls.RadioButton>()
            .FirstOrDefault(nav =>
                string.Equals(nav.Tag?.ToString(), header, StringComparison.OrdinalIgnoreCase));
        if (found is null)
        {
            return false;
        }

        // The button's own tag and never the caller's word (DD170). The lookup above matches
        // case-insensitively, which is right for a name typed on a command line, and `Show` decides
        // with a switch on an exact string — so passing `header` through drew the default page under
        // a strip that said otherwise. The Checked handler has always passed the tag; this is the
        // path that did not, and it only runs when the window reopened on the destination being
        // asked for, which is why a capture could photograph the wrong list and report success.
        var destination = (string)found.Tag;

        // Already there: Checked does not fire again, so show it directly.
        if (found.IsChecked == true)
        {
            Show(destination);
        }
        else
        {
            found.IsChecked = true;
        }

        UpdateLayout();
        return true;
    }

    private void Destination_Checked(object sender, RoutedEventArgs e) =>
        Show((string)((FrameworkElement)sender).Tag);

    /// <summary>
    /// Show a destination, building it on its first visit and refreshing it as it becomes visible.
    /// </summary>
    /// <remarks>
    /// The refresh is here rather than in the page's constructor for the reason the old
    /// <c>TabChanged</c> gave: reading two or three lists to render a pane nobody is looking at is the
    /// poll this project keeps not writing.
    /// </remarks>
    private void Show(string destination)
    {
        UIElement page;
        switch (destination)
        {
            case "Images":
                _images ??= Add(new ImagesPage(_api, _engineState));
                page = _images;
                _ = _images.RefreshImagesAsync();
                break;
            case "Volumes":
                _volumes ??= Add(new VolumesPage(_api, _engineState));
                page = _volumes;
                _ = _volumes.RefreshVolumesAsync();
                break;
            case "Builds":
                page = BuildsDestination;
                _ = BuildsDestination.RefreshBuildsAsync();
                break;
            case "Engine":
                // No refresh call beside it, unlike every other destination: the page reads its own
                // file on a timer it starts when it becomes visible, because what it shows changes
                // while somebody is looking at it and nothing here would know to ask again (DD165).
                _engine ??= Add(new EnginePage(_engineSeams, _startEngine));
                page = _engine;
                break;
            case "About":
                _about ??= Add(new AboutPage(_api));
                page = _about;
                _ = _about.RefreshAboutAsync();
                break;
            default:
                page = Containers;
                break;
        }

        foreach (UIElement child in DestinationHost.Children)
        {
            child.Visibility = ReferenceEquals(child, page) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private T Add<T>(T page) where T : UIElement
    {
        DestinationHost.Children.Add(page);
        return page;
    }

    /// <summary>Open the builds destination on one record, because a link named it (DD126).</summary>
    /// <param name="reference">The ref, as <see cref="BuildAddress.RefIn"/> read it.</param>
    /// <remarks>
    /// The nav is ticked first, so arriving by link leaves the window in the state arriving by click
    /// does — otherwise the strip disagrees with what is on screen.
    /// </remarks>
    internal void ShowBuild(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        NavBuilds.IsChecked = true;
        _ = BuildsDestination.ShowBuildAsync(reference);
    }

    /// <summary>Re-read the containers, and the engine line above them.</summary>
    /// <param name="settled">
    /// The container an event just arrived for, whose pending state that event confirms.
    /// </param>
    /// <returns>A task that completes when the window has been redrawn.</returns>
    internal Task RefreshAsync(string? settled = null)
    {
        ShowEngine(_engineState());
        return Containers.RefreshAsync(settled);
    }

    /// <summary>Re-read the images, but only when that is the page being looked at.</summary>
    /// <returns>A task that completes when the page has been redrawn, or immediately.</returns>
    internal Task RefreshImagesIfShowingAsync() =>
        _images is { Visibility: Visibility.Visible } images
            ? images.RefreshImagesAsync()
            : Task.CompletedTask;

    /// <summary>
    /// Write this window to a PNG by rendering it, not by photographing the screen (DD22).
    /// </summary>
    /// <param name="path">Where to write.</param>
    /// <param name="scale">Device-independent pixels per pixel.</param>
    /// <returns>The size written, in pixels.</returns>
    /// <remarks>
    /// A <c>RenderTargetBitmap</c> over this window's own content has nothing else in the frame, which
    /// is the whole point: the screen copy it replaces read whatever was actually inside the window's
    /// rectangle, and twice that was somebody else's editor and messaging app. Rendering cannot
    /// photograph a thing that is not in this visual tree.
    ///
    /// The opaque background is not decoration. A Fluent window's backdrop is drawn by the system
    /// behind the window and is not part of the visual tree, so a render of the content alone comes
    /// back as light text on transparency — borrowed from claude-tray, which found the same thing.
    /// </remarks>
    internal (int Width, int Height) SaveSnapshot(string path, double scale = 1.5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(scale, 0);

        // Let layout finish before measuring it. Without this the first capture of a window is its
        // pre-arrange size, which is a picture of nothing at 0x0.
        UpdateLayout();
        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

        var target = (FrameworkElement)Content;

        // The content's own margin is part of its offset inside the window, and rendering the element
        // directly draws it at that offset — so the bitmap loses a strip off the right and bottom equal
        // to the margin, and the About button came back as "Abou". Measured by looking at the first
        // capture this verb produced. Drawing it through a VisualBrush into a rectangle at the origin
        // places it by the rectangle instead of by its layout, which is what makes the frame the whole
        // content and nothing less.
        var margin = target.Margin;
        var wide = target.ActualWidth + margin.Left + margin.Right;
        var tall = target.ActualHeight + margin.Top + margin.Bottom;
        if (wide <= 0 || tall <= 0)
        {
            throw new InvalidOperationException(
                $"the window measured {target.ActualWidth}x{target.ActualHeight}, so there is "
                + "nothing to render: it was captured before it was laid out");
        }

        // Both names below are ambiguous unqualified: enabling WinForms puts System.Drawing in the
        // implicit usings, so Brush and Color resolve to the GDI+ pair as readily as the WPF one.
        var backdrop =
            TryFindResource("SolidBackgroundFillColorBaseBrush") as System.Windows.Media.Brush
            ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x20, 0x20, 0x20));

        var whole = new Rect(0, 0, wide, tall);
        var visual = new DrawingVisual();
        using (var canvas = visual.RenderOpen())
        {
            // A Fluent window's backdrop is drawn by the system behind the window and is not in the
            // visual tree, so without this the render is light text on transparency.
            canvas.DrawRectangle(backdrop, null, whole);
            canvas.DrawRectangle(
                new VisualBrush(target) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null,
                new Rect(margin.Left, margin.Top, target.ActualWidth, target.ActualHeight));
        }

        var width = (int)(wide * scale);
        var height = (int)(tall * scale);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        encoder.Save(file);
        return (width, height);
    }

    private void ShowEngine(EngineState engine)
    {
        EngineDot.Fill = Palette.EngineBrush(engine);
        Breathing.Set(EngineDot, engine is EngineState.Starting);

        // The water is the same fact the dot and the word carry, read a third way (DD69). Running
        // only, not starting: a state that is still resolving is exactly what a moving thing would
        // assert too confidently.
        Water.Running(engine is EngineState.Running);
        EngineLabel.Text = engine switch
        {
            EngineState.Running => "Engine running",
            EngineState.Starting => "Engine starting",
            _ => "Engine stopped",
        };
    }
}
