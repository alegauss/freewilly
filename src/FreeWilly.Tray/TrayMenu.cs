using FreeWilly.Core.Engine;
using FreeWilly.Core.Releases;
using FreeWilly.Core.Settings;

namespace FreeWilly.Tray;

/// <summary>
/// The tray's context menu, built in one place so the thing photographed is the thing shipped.
/// </summary>
/// <remarks>
/// DD67. This was six lines inside <c>TrayApplication</c>'s constructor, which is fine until
/// something other than the tray needs the same menu — and something does: no popup this product
/// draws had ever been photographed, because a menu exists only while it is open and nothing opened
/// one. A second menu built for the camera would photograph a menu nobody ships.
///
/// <para><b>It takes actions and knows the engine only as a state</b> (L6). Handed no-ops it still
/// builds, which is what lets <c>--show-menu</c> put one on the screen with no engine, no icon and
/// no window behind it — the same law that lets the window draw without a daemon. The setting added
/// in DD135 keeps that law by being optional: no delegate means a box that ticks and tells
/// nobody.</para>
///
/// <para><b>Short on purpose.</b> A context menu that grows into a second UI is how a tray app stops
/// being glanceable; everything else belongs in the window. DD135 spent one item of that budget, and
/// the reason it is affordable is that it qualifies a verb already here rather than introducing a
/// place to go: the menu is still the two things you can do to the engine, plus how it behaves when
/// you are not asking, plus the window and the way out.</para>
///
/// <para><b>The window leads it</b> (DD140). It sat fourth while the menu was the only way in, on the
/// reasoning that a tray menu is about the engine. A left click on the icon now carries that ordinary
/// case, so what is left of this menu's job is to name what the click does — first, where a reader
/// looks — and then the engine, which the click says nothing about.</para>
///
/// <para><b>DD154 spent the budget's other item, and DD171 gave it back.</b> The release check was a
/// tick beside the engine's, and a check nobody turns on is a check nobody has — so the tray asks on
/// every launch the way claude-tray does and the tick is gone. What is left is the install item,
/// hidden until there is something to install, so the resting menu is exactly the length it was
/// before DD154. It is here rather than in the window because an update that could only be applied
/// from a page is one a user has to go and find.</para>
/// </remarks>
internal sealed class TrayMenu
{
    /// <summary>What each item says, in one place, so a test can hold the shipped menu to it.</summary>
    internal const string StartText = "&Start engine";

    /// <inheritdoc cref="StartText"/>
    internal const string StopText = "Sto&p engine";

    /// <inheritdoc cref="StartText"/>
    internal const string OnLaunchText = "Start engine &with FreeWilly";

    /// <summary>What the hidden install item says before a release has named itself (DD154).</summary>
    /// <remarks>
    /// It is never read by a user — the item is invisible until <see cref="Offer"/> replaces this with
    /// the version — but it is what a test asserts the resting menu holds, and an empty caption would
    /// make the hidden item indistinguishable from a separator in a dump of the strip.
    /// </remarks>
    internal const string InstallText = "&Install the update";

    /// <summary>How the install item names a release once there is one.</summary>
    internal const string InstallFormat = "&Install FreeWilly {0}";

    /// <inheritdoc cref="StartText"/>
    internal const string WindowText = "&Open window";

    /// <inheritdoc cref="StartText"/>
    internal const string QuitText = "&Quit";

    private readonly ToolStripMenuItem _start = new(StartText);
    private readonly ToolStripMenuItem _stop = new(StopText);
    private readonly ToolStripMenuItem _onLaunch = new(OnLaunchText) { CheckOnClick = true };
    private readonly ToolStripMenuItem _install = new(InstallText) { Visible = false };
    private readonly ToolStripMenuItem _window = new(WindowText);

    /// <summary>Build the menu.</summary>
    /// <param name="startEngine">What the second item does.</param>
    /// <param name="stopEngine">What the third does.</param>
    /// <param name="openWindow">What the first does.</param>
    /// <param name="quit">What the last does.</param>
    /// <param name="settings">
    /// What the boxes open showing, defaulting to what an install nobody has changed anything on has.
    /// </param>
    /// <param name="save">
    /// What a tick does, given every setting as it now stands — or <see langword="null"/> where
    /// nothing is behind it, which is how <c>--show-menu</c> photographs a menu with no tray under it
    /// (DD135).
    /// </param>
    /// <param name="installUpdate">
    /// What the hidden item does once <see cref="Offer"/> has revealed it, or <see langword="null"/>
    /// for the same reason <paramref name="save"/> takes one.
    /// </param>
    /// <remarks>
    /// The settings arrive as the record rather than as a flag each, and the tick hands the whole
    /// record back. Two settings could have been two parameters and two callbacks; the third would
    /// have been the point at which this constructor stopped being readable, and the file the tray
    /// writes holds all of them at once anyway — so a saver that was given one flag would have to go
    /// and read the others back before it could write.
    /// </remarks>
    internal TrayMenu(
        Action startEngine,
        Action stopEngine,
        Action openWindow,
        Action quit,
        TraySettings? settings = null,
        Action<TraySettings>? save = null,
        Action? installUpdate = null)
    {
        ArgumentNullException.ThrowIfNull(startEngine);
        ArgumentNullException.ThrowIfNull(stopEngine);
        ArgumentNullException.ThrowIfNull(openWindow);
        ArgumentNullException.ThrowIfNull(quit);

        _start.Click += (_, _) => startEngine();
        _stop.Click += (_, _) => stopEngine();
        _window.Click += (_, _) => openWindow();
        _install.Click += (_, _) => installUpdate?.Invoke();

        // CheckOnClick flips the tick before this runs, so the items' own state is the new answer and
        // nothing here has to negate anything. Settings written from what the user is looking at
        // cannot disagree with it.
        var opened = settings ?? new TraySettings();
        _onLaunch.Checked = opened.StartWithTheTray;

        _onLaunch.CheckedChanged += (_, _) =>
            save?.Invoke(new TraySettings { StartWithTheTray = _onLaunch.Checked });

        Strip = new ContextMenuStrip();

        // First, and alone above the rule: it is what the icon's own click does, so the menu opens
        // by naming the thing a reader is most likely here for rather than burying it fourth (DD140).
        Strip.Items.Add(_window);
        Strip.Items.Add(new ToolStripSeparator());

        Strip.Items.Add(_start);
        Strip.Items.Add(_stop);

        // With the two verbs it qualifies rather than off with the window, because it is a third
        // thing to say about the engine and not a third place to go.
        Strip.Items.Add(_onLaunch);

        // Where the release check's tick used to sit (DD171). Hidden until a release exists, so this
        // is the line a resting menu does not have — and when it appears it appears under the engine
        // group rather than beside the window, because installing it stops the engine.
        Strip.Items.Add(_install);
        Strip.Items.Add(new ToolStripSeparator());
        Strip.Items.Add(new ToolStripMenuItem(QuitText, null, (_, _) => quit()));
    }

    /// <summary>The menu itself, for whatever is going to show it.</summary>
    internal ContextMenuStrip Strip { get; }

    /// <summary>Reveal the install item, naming the release it would install (DD154).</summary>
    /// <param name="release">What was found.</param>
    /// <remarks>
    /// The version in the caption and not just "an update is available", because the one thing a user
    /// deciding whether to interrupt what they are doing needs is which version they would be moving
    /// to — and it is the same string the balloon says, so the two cannot disagree about what is on
    /// offer.
    /// </remarks>
    internal void Offer(AvailableRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        _install.Text = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            InstallFormat,
            release.Version.ToString(3));
        _install.Visible = true;
    }

    /// <summary>Say what the engine is doing, by what can be asked of it.</summary>
    /// <param name="state">What the engine is doing.</param>
    internal void Reflect(EngineState state)
    {
        _start.Enabled = state is not EngineState.Running;
        _stop.Enabled = state is not EngineState.Stopped;
    }
}
