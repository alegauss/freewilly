using FreeWilly.Core.Engine;
using FreeWilly.Core.Releases;
using FreeWilly.Core.Settings;
using FreeWilly.Tray;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// The tray's context menu, which is now photographable and therefore assertable (DD67).
/// </summary>
/// <remarks>
/// These exist because the menu is built in one place for one reason: <c>--show-menu</c> shows the
/// same menu the tray wears, and a second one built for the camera would be a picture of a menu
/// nobody ships. So what is asserted here is the shape a capture is a picture of.
///
/// <para>On an STA thread, because a <c>ContextMenuStrip</c> is a control and WinForms refuses one
/// off an MTA thread — xUnit's are MTA.</para>
/// </remarks>
public sealed class TrayMenuTests
{
    /// <summary>Run a body on a thread WinForms will talk to, and surface whatever it threw.</summary>
    private static void OnUiThread(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException("the menu failed on its own thread", failure);
        }
    }

    private static TrayMenu Menu() => new(Nothing, Nothing, Nothing, Nothing);

    /// <summary>Where each item sits, so a renumbering is one edit rather than thirty.</summary>
    private const int Window = 0;
    private const int Start = 2;
    private const int Stop = 3;
    private const int OnLaunch = 4;
    private const int Install = 5;

    private static void Nothing()
    {
    }

    [Fact]
    public void The_menu_is_six_items_and_two_rules_in_the_order_a_photograph_shows_them() =>
        OnUiThread(() =>
        {
            // Short on purpose, and asserted so it stays short: a context menu that grows into a
            // second UI is how a tray app stops being glanceable. The order is part of the claim
            // because a photograph is a picture of the order.
            //
            // DD135 spent one item here, and it is placed with the two engine verbs rather than off
            // with the window because it qualifies them: what the engine does when nobody is asking.
            // DD154 spent the next one beside it and DD171 gave it back — the check needs no switch —
            // so what follows is the install item, hidden until there is something to install.
            //
            // The window is first since DD140, alone above its rule: the icon's own click opens it,
            // so the menu's first line names what the click does and the engine follows.
            using var menu = Menu().Strip;

            Assert.Equal(8, menu.Items.Count);
            Assert.Equal(TrayMenu.WindowText, menu.Items[Window].Text);
            Assert.IsType<ToolStripSeparator>(menu.Items[1]);
            Assert.Equal(TrayMenu.StartText, menu.Items[Start].Text);
            Assert.Equal(TrayMenu.StopText, menu.Items[Stop].Text);
            Assert.Equal(TrayMenu.OnLaunchText, menu.Items[OnLaunch].Text);
            Assert.Equal(TrayMenu.InstallText, menu.Items[Install].Text);
            Assert.IsType<ToolStripSeparator>(menu.Items[6]);
            Assert.Equal(TrayMenu.QuitText, menu.Items[7].Text);
        });

    [Fact]
    public void No_item_offers_to_turn_the_release_check_on_or_off() =>
        OnUiThread(() =>
        {
            // DD171. The check is not a setting any more, so the menu must not carry a tick that
            // implies it is one — asserted over the whole strip rather than at an index, because the
            // defect this guards is the item coming back somewhere else.
            using var strip = Menu().Strip;

            Assert.DoesNotContain(
                strip.Items.OfType<ToolStripMenuItem>(),
                item => item.Text?.Contains("update", StringComparison.OrdinalIgnoreCase) == true
                    && item.CheckOnClick);
        });

    [Fact]
    public void Nothing_offers_to_install_anything_until_something_has_been_found() =>
        OnUiThread(() =>
        {
            // The item exists so the strip's shape is fixed, and it is invisible so the menu a user
            // opens is the menu they had before (DD154). An install item that was always there would
            // be a verb that usually does nothing.
            using var strip = Menu().Strip;

            Assert.False(strip.Items[Install].Available);
        });

    [Fact]
    public void A_release_that_was_found_is_named_on_the_item_that_installs_it() =>
        OnUiThread(() =>
        {
            // The version and not "an update is available": what a user deciding whether to interrupt
            // themselves needs is which version they would be moving to.
            var menu = Menu();
            using var strip = menu.Strip;

            menu.Offer(new AvailableRelease(
                new Version(9, 8, 7), "v9.8.7", "FreeWilly-Setup-9.8.7.exe", "https://x/i", "https://x/s"));

            Assert.True(strip.Items[Install].Available);
            Assert.Contains("9.8.7", strip.Items[Install].Text, StringComparison.Ordinal);
        });

    [Fact]
    public void The_settings_open_showing_what_is_actually_true() =>
        OnUiThread(() =>
        {
            // Boxes that always opened at the default would be a picture of the default rather than
            // of the user's answer, and the one thing a setting has to do is survive being changed.
            using var flipped = new TrayMenu(
                Nothing, Nothing, Nothing, Nothing,
                new TraySettings { StartWithTheTray = false }).Strip;
            using var shipped = new TrayMenu(Nothing, Nothing, Nothing, Nothing).Strip;

            Assert.False(((ToolStripMenuItem)flipped.Items[OnLaunch]).Checked);
            Assert.Equal(
                TraySettings.EngineShipsOn, ((ToolStripMenuItem)shipped.Items[OnLaunch]).Checked);
        });

    [Fact]
    public void Ticking_the_setting_reports_it_as_the_user_is_looking_at_it() =>
        OnUiThread(() =>
        {
            // CheckOnClick flips the tick before the handler runs, so what is reported is the new
            // answer and nothing has to negate anything — a setting written from what is on screen
            // cannot disagree with what is on screen.
            var told = new List<TraySettings>();
            var menu = new TrayMenu(Nothing, Nothing, Nothing, Nothing, new TraySettings(), told.Add);
            using var strip = menu.Strip;

            ((ToolStripMenuItem)strip.Items[OnLaunch]).PerformClick();
            ((ToolStripMenuItem)strip.Items[OnLaunch]).PerformClick();

            Assert.Equal(2, told.Count);
            Assert.Equal(new TraySettings { StartWithTheTray = false }, told[0]);
            Assert.Equal(new TraySettings { StartWithTheTray = true }, told[1]);
        });

    [Fact]
    public void The_settings_tick_with_nothing_behind_them_so_the_camera_still_reaches_the_menu() =>
        OnUiThread(() =>
        {
            // L6 again. `--show-menu` builds this with no tray under it, and an item that needed a
            // live setting to exist would be an item no capture could photograph.
            using var strip = Menu().Strip;

            ((ToolStripMenuItem)strip.Items[OnLaunch]).PerformClick();
            strip.Items[Install].PerformClick();

            Assert.False(((ToolStripMenuItem)strip.Items[OnLaunch]).Checked);
        });

    [Theory]
    [InlineData(EngineState.Stopped, true, false)]
    [InlineData(EngineState.Starting, true, true)]
    [InlineData(EngineState.Running, false, true)]
    public void What_can_be_asked_of_the_engine_is_what_the_menu_offers(
        EngineState state, bool canStart, bool canStop) =>
        OnUiThread(() =>
        {
            // Two of the three states differ here by one item's enabled flag and by nothing else,
            // which is exactly the difference a capture of each state is for.
            var menu = Menu();
            menu.Reflect(state);

            using var strip = menu.Strip;
            Assert.Equal(canStart, strip.Items[2].Enabled);
            Assert.Equal(canStop, strip.Items[3].Enabled);
        });

    [Fact]
    public void It_builds_with_nothing_to_do_which_is_what_lets_a_capture_reach_it() =>
        OnUiThread(() =>
        {
            // L6, and the whole of why the popup is reachable at all: the preview shows this menu
            // with no engine, no icon and no window behind it. A menu that needed a live tray to
            // exist could only be photographed on a machine that already had one.
            using var strip = Menu().Strip;
            Assert.NotNull(strip);
        });

    [Fact]
    public void An_item_with_nothing_behind_it_is_a_defect_here_rather_than_a_dead_click() =>
        OnUiThread(() =>
        {
            // A null passed for one of these used to be a menu entry that silently did nothing,
            // which is indistinguishable from a broken engine. The setting added in DD135 is the
            // deliberate exception and is checked nowhere here: a box that ticks and tells nobody
            // is a photograph, not a dead click, and it is what `--show-menu` builds.
            Assert.Throws<ArgumentNullException>(() => new TrayMenu(null!, Nothing, Nothing, Nothing));
            Assert.Throws<ArgumentNullException>(() => new TrayMenu(Nothing, null!, Nothing, Nothing));
            Assert.Throws<ArgumentNullException>(() => new TrayMenu(Nothing, Nothing, null!, Nothing));
            Assert.Throws<ArgumentNullException>(() => new TrayMenu(Nothing, Nothing, Nothing, null!));
        });
}
