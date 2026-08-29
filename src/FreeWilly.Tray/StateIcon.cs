using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using FreeWilly.Core.Engine;

namespace FreeWilly.Tray;

/// <summary>
/// The product mark, badged with what the engine is doing (DD85).
/// </summary>
/// <remarks>
/// The one surface that is always on screen used to be three abstract rings — a disc, a gapped ring
/// and a ring — which said what the engine was doing and nothing at all about which product was
/// doing it. A ring is what a dozen other tools put in the same overflow.
///
/// <para><b>L8 is untouched, and that took a third design.</b> The task proposed replacing shape
/// with luminance — the mark full-colour for running, amber for starting, grey and dimmed for
/// stopped — because one mark is one silhouette and <see cref="InkedPixels"/> could no longer tell
/// the three apart. Measured, that does not survive this artwork: the mark is a mostly-dark disc
/// whose own mean luminance is 102 of 255, so there is no room under it for two more states. At
/// gaps wide enough to read (56 and 40) the stopped icon disappeared into a dark taskbar; raising
/// its floor until it was legible again collapsed the three means to 102, 100 and 88. The two
/// requirements are directly opposed, so the amendment was abandoned rather than tuned.</para>
///
/// <para><b>What ships instead is the mark plus a badge</b>, and the badge is the same three shapes
/// this file already drew: a filled disc, a ring with a bite out of it, a plain ring. Shape still
/// carries the state, colour still only reinforces it, and the mark carries the identity — so the
/// task is answered without the law moving. The badge sits in a punched hole so it reads against
/// the artwork rather than on it.</para>
///
/// <para><b>The mark is read, never drawn.</b> It is a traced path of some hundreds of segments;
/// nothing here could draw it. <c>FreeWilly.ico</c> already carries it at every size Windows asks
/// for, and its entries below 48 already come from <c>build/icon.svg</c> — the same artwork with the
/// wave as one tone and the eye grown to survive sixteen pixels. Every tray size is below 48, so
/// every one of them is the drawing made for exactly this.</para>
/// </remarks>
public static class StateIcon
{
    /// <summary>How much of the icon's edge the state badge takes.</summary>
    /// <remarks>
    /// Measured against the alternative at 0.52, which reads as a badge with a mark behind it rather
    /// than a mark with a badge on it. At 0.44 the orca and the wave both survive 16 pixels and the
    /// three badges are still unmistakable on a light and a dark taskbar.
    /// </remarks>
    public const double BadgeFraction = 0.44;

    /// <summary>Where the badge is, in a drawing of <paramref name="size"/> pixels.</summary>
    /// <remarks>
    /// Public because it is what a test has to know: the discriminating shape is no longer the whole
    /// icon, so "is the middle painted" is a question about this rectangle and not about the bitmap.
    /// </remarks>
    /// <param name="size">The edge of the drawing, in pixels.</param>
    /// <returns>The badge's box.</returns>
    public static RectangleF BadgeAt(int size)
    {
        var badge = (float)(size * BadgeFraction);
        return new RectangleF(size - badge - 0.5f, size - badge - 0.5f, badge, badge);
    }

    /// <summary>The colour each state is drawn in.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The colour.</returns>
    /// <remarks>
    /// The value lives in <see cref="Ui.Palette"/> and this is the GDI+ edge of it (DD34). It used to
    /// be the other way round — the bytes here, converted to a WPF brush by hand at one call site —
    /// which meant the engine's colour had two homes and only one of them was ever looked at.
    /// </remarks>
    public static Color ColourFor(EngineState state) => Ui.Palette.EngineGdi(state);

    /// <summary>The words the tooltip says, for when the shape is not enough.</summary>
    /// <param name="state">The state.</param>
    /// <returns>One line.</returns>
    public static string TooltipFor(EngineState state) => state switch
    {
        EngineState.Running => "FreeWilly: engine running",
        EngineState.Starting => "FreeWilly: engine starting",
        EngineState.Stopped => "FreeWilly: engine stopped",
        _ => "FreeWilly",
    };

    /// <summary>Draw the state at <paramref name="size"/> pixels square.</summary>
    /// <param name="state">The state.</param>
    /// <param name="size">The edge, in pixels.</param>
    /// <returns>The bitmap, owned by the caller.</returns>
    public static Bitmap Draw(EngineState state, int size = 16)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 8);

        using var mark = MarkAt(size);
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);
        graphics.DrawImage(mark, new Rectangle(0, 0, size, size));

        Badge(graphics, state, size);
        return bitmap;
    }

    /// <summary>Draw the state as an icon a <c>NotifyIcon</c> can wear.</summary>
    /// <param name="state">The state.</param>
    /// <param name="size">The edge in pixels, or nothing to ask Windows (DD99).</param>
    /// <returns>The icon, owned by the caller.</returns>
    public static Icon Icon(EngineState state, int? size = null)
    {
        using var bitmap = Draw(state, size ?? NotificationAreaSize());
        return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
    }

    /// <summary>
    /// The edge Windows wants a notification-area icon drawn at, on this display (DD99).
    /// </summary>
    /// <remarks>
    /// <c>app.manifest</c> opts this process into <c>PerMonitorV2</c>, and its comment says exactly
    /// why: "a tray icon drawn for 96 DPI and scaled up by Windows is the blurry square every
    /// WinForms tray app is recognised by". Under that awareness Windows does not scale for the
    /// app — it expects the app to hand over the size the display asks for — and this was drawing 16
    /// regardless, so the manifest bought the right to be sharp and nothing spent it. It matters
    /// more since DD85: three abstract rings survive a bad resample and a traced orca does not.
    ///
    /// <para><b>Windows is asked rather than a scale factor applied.</b> The metric is what the shell
    /// itself uses, so a future Windows that spaces the notification area differently is answered
    /// without a constant here being wrong.</para>
    ///
    /// <para><b>The system DPI, not the monitor's.</b> The icon lives on the taskbar and there is no
    /// window here whose monitor could be asked. This is right wherever the taskbar is on the display
    /// Windows scaled for, and it is a better answer than 16 everywhere else.</para>
    ///
    /// <para><b>Measured on the development machine, at 200%:</b> a DPI-unaware process is told 96
    /// and 16, an aware one 192 and 32. The manifest makes this process the second, so the tray had
    /// been handing the shell a sixteen-pixel picture for a thirty-two-pixel slot on the very machine
    /// it was written on. It also means a test cannot assert the number — the test host carries its
    /// own manifest and is unaware, so it is told 16 whatever this returns to the tray.</para>
    /// </remarks>
    /// <returns>The edge in pixels, never below 16.</returns>
    public static int NotificationAreaSize()
    {
        try
        {
            return Bounded(GetSystemMetricsForDpi(SmallIconWidth, GetDpiForSystem()));
        }
        catch (Exception exception) when (exception is EntryPointNotFoundException
            or DllNotFoundException)
        {
            // Both arrived in Windows 10 1607 and the manifest names no older one, so this is the
            // impossible branch — and an icon is not worth a crash, so it takes the old constant.
            return Fallback;
        }
    }

    /// <summary>Keep an answer from Windows inside what this can actually draw.</summary>
    /// <param name="metric">What the metric said.</param>
    /// <returns>The edge to draw at.</returns>
    /// <remarks>
    /// Zero is what a failed metric returns, and <see cref="Draw"/> refuses anything under 8 — so
    /// without a floor a machine that answered badly would take the tray down rather than look
    /// wrong. The ceiling is the largest frame the mark carries; past it there is nothing to scale
    /// from and the result would be the blur this exists to remove.
    /// </remarks>
    internal static int Bounded(int metric) => Math.Clamp(metric, Fallback, 256);

    /// <summary>What a tray icon was before anybody asked Windows.</summary>
    private const int Fallback = 16;

    /// <summary>SM_CXSMICON — the width of a small icon, which is what the tray draws.</summary>
    private const int SmallIconWidth = 49;

    // DllImport rather than the source-generated LibraryImport: that one requires unsafe blocks to
    // be enabled for the whole project, which is a large permission to buy for two integers.
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    /// <summary>
    /// How many pixels of the drawing are painted at all, ignoring colour.
    /// </summary>
    /// <remarks>
    /// The measurement that makes "shape, not colour" testable: a filled disc covers far more of the
    /// box than a ring, and a ring with a gap covers less than a closed one. Asserting on this is
    /// asserting that the three are told apart by a reader who sees no colour.
    ///
    /// <para>Since DD85 the question is asked of <see cref="BadgeAt"/> rather than of the whole
    /// bitmap: the mark is the same drawing in all three states, so counting it in would swamp the
    /// difference the states actually have. Over the whole bitmap this still answers the other
    /// question worth asking — whether anything was drawn at all.</para>
    /// </remarks>
    /// <param name="bitmap">The drawing.</param>
    /// <returns>The count of pixels with any opacity.</returns>
    public static int InkedPixels(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var inked = 0;
        for (var x = 0; x < bitmap.Width; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                // Half opacity, so the anti-aliased fringe is not counted as ink on both sides.
                if (bitmap.GetPixel(x, y).A > 128)
                {
                    inked++;
                }
            }
        }

        return inked;
    }

    /// <summary>
    /// Draw the state over the corner of the mark.
    /// </summary>
    /// <remarks>
    /// The three shapes are the ones this file drew before the mark arrived, and they are kept
    /// deliberately: they are what L8 is about, and the test that holds them records two near-misses
    /// already — a 360-degree arc one pixel smaller than the ring, and an ink threshold that would
    /// have survived a pen change. A new vocabulary would have thrown both lessons away.
    ///
    /// <para>The hole is punched with <c>SourceCopy</c>, which writes transparency rather than
    /// compositing over it. Without it the badge sits on the mark's own dark blue and the ring's
    /// hollow centre is not hollow — it is the orca, and the one thing that separates two of the
    /// states stops being visible.</para>
    /// </remarks>
    private static void Badge(Graphics graphics, EngineState state, int size)
    {
        var box = BadgeAt(size);
        var halo = Math.Max(1f, size * 0.09f);

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.CompositingMode = CompositingMode.SourceCopy;
        using (var clear = new SolidBrush(Color.Transparent))
        {
            graphics.FillEllipse(
                clear, box.X - halo, box.Y - halo, box.Width + (halo * 2), box.Height + (halo * 2));
        }

        graphics.CompositingMode = CompositingMode.SourceOver;
        var colour = ColourFor(state);
        var pen = Math.Max(1.5f, box.Width / 4f);
        var inner = new RectangleF(
            box.X + (pen / 2), box.Y + (pen / 2), box.Width - pen, box.Height - pen);

        switch (state)
        {
            case EngineState.Running:
                using (var brush = new SolidBrush(colour))
                {
                    graphics.FillEllipse(brush, box);
                }

                break;

            case EngineState.Starting:
                // A ring with a bite out of it: the same outline as Stopped and unmistakably not it,
                // which is what tells "on its way up" from "not running" without relying on hue.
                using (var stroke = new Pen(colour, pen))
                {
                    graphics.DrawArc(stroke, inner, startAngle: -45, sweepAngle: 270);
                }

                break;

            default:
                using (var stroke = new Pen(colour, pen))
                {
                    graphics.DrawEllipse(stroke, inner);
                }

                break;
        }
    }

    /// <summary>The committed mark, at the frame nearest <paramref name="size"/>.</summary>
    /// <remarks>
    /// An <c>.ico</c> is a file of separate pictures rather than one resampled, so asking for a size
    /// is asking which drawing — and below 48 the answer is <c>build/icon.svg</c>, the trace made to
    /// survive a tray. Every size this is called with is below 48.
    /// </remarks>
    private static Bitmap MarkAt(int size)
    {
        using var stream = typeof(StateIcon).GetTypeInfo().Assembly
            .GetManifestResourceStream(MarkResource)
            ?? throw new InvalidOperationException(
                $"{MarkResource} is not in this assembly, so the tray has no mark to wear");

        using var icon = new Icon(stream, new Size(size, size));
        return icon.ToBitmap();
    }

    /// <summary>What the mark is called inside the assembly.</summary>
    private const string MarkResource = "FreeWilly.ico";
}
