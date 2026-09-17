using Winwright.Capturing;
using Winwright.Locating;
using Winwright.Processes;
using Winwright.Projects;
using Winwright.Windowing;

using Xunit;

namespace FreeWilly.Cases;

/// <summary>
/// DD7, DD61 and DD67, migrated under WW87: the menu photographed, and the picture held to what it
/// is a picture of.
/// <para>
/// <c>--capture-window</c> renders the window's own visual tree, which is why it is the preferred
/// way to photograph anything here: a render has no foreground, no z order and no second instance to
/// be confused with. The one surface this product ships that it cannot reach is the tray's menu — a
/// <c>ContextMenuStrip</c> is its own top-level window and is in no tree the application can hand
/// over — so the only route to it is a copy of the screen, which is a copy of whatever is in the
/// rectangle.
/// </para>
/// <para>
/// That is what <c>scripts\Capture-Window.ps1</c> was for, and the 382 lines were not the copy. They
/// were five assertions standing between the copy and a green run, because shipping DD7 a copy like
/// this twice photographed something else: an editor holding the guest's credentials, and a
/// messaging app holding a medical appointment. Both reached a transcript, which deleting the file
/// afterwards does not undo.
/// </para>
/// <para>
/// All five are readings this engine takes, each against the same fault the script met:
/// the window belongs to the process this run launched, which the receipt refuses without;
/// no other instance is showing a window, which is <see cref="InstanceCheck" />;
/// the window transmits nothing through its own glass, which is DD61's <c>DWMWA_SYSTEMBACKDROP_TYPE</c>
/// asked as <see cref="Glass" />;
/// nothing stood over the region, which is <see cref="RegionThroughout" /> — read either side of the
/// take rather than before it, since a window arriving during the copy is in the file and in nothing
/// else;
/// and the file is not one flat colour, which is <see cref="ColourCheck" />.
/// </para>
/// <para>
/// Two things it gains by not being the script. The route is <em>derived</em> and not assumed: the
/// script hard-coded <c>#32768</c> and <c>tooltips_class32</c> and this menu is neither — a WinForms
/// drop-down carries a per-thread number in its class name and, shown with no form behind it, is
/// owned by nothing — so <see cref="CaptureRoute" /> reading it as a popup a framework drew is the
/// half WW87 taught the engine and the half the script could not have written. And the copy is
/// bounded by the painted frame rather than by <c>GetWindowRect</c>, which spans the resize border
/// and the shadow: the script arrived at that too, and here it is the engine's answer rather than
/// this repository's.
/// </para>
/// <para>
/// What it does not do is assert the menu's entries. Those are read from the accessibility tree by
/// the preflight the installer ships and by the engine's own locator cases; a picture proves the
/// menu was drawn, and a claim about what is in it is a claim a picture cannot make.
/// </para>
/// </summary>
public sealed class TrayMenuPicture
{
    /// <summary>
    /// What holds the menu up with no icon and no window behind it (DD67, DD135). The driving stays
    /// inside the process that owns the menu, so no synthesised click reaches into another
    /// process's UI — and there is nothing else of this application's on the screen to photograph by
    /// accident.
    /// </summary>
    private const string ShowsTheMenu = "--show-menu";

    /// <summary>
    /// How long the menu has to be on the screen. The script measured 8000 ms and said why: a cold
    /// start of the single-file self-contained .exe took longer than 2500 ms on that machine and the
    /// run refused with "no FreeWilly window" on an application that was fine. The project declares
    /// its own launch deadline and this reads it rather than repeating a number.
    /// </summary>
    private static int Appears(ProjectDeclaration declared) => declared.Timeouts.For("launch");

    [Fact]
    public void The_menu_a_render_cannot_reach_is_copied_and_the_copy_says_what_it_is_of()
    {
        var declared = ProjectDeclaration.Load(Path.Combine(Tree.Root(), ProjectDeclaration.FileName));
        var exe = declared.Executable;

        // Named rather than left to fail as a launch that produced nothing: winwright.json points at
        // the published single file on purpose, because the capture is of the menu this product
        // ships and a preview of a Debug build is a picture of something nobody installs.
        Assert.True(
            File.Exists(exe),
            $"there is nothing to photograph: {exe} has not been published. `run-cases.cmd` publishes "
                + "it before it runs this, and `build\\build.cmd` is the same publish by hand.");

        // Before any rectangle is read, and asserted rather than assumed. The script called
        // SetProcessDPIAware itself and said what happens without it: GetWindowRect answers
        // virtualised while the painted frame answers in physical pixels, and the run reports a
        // frame LARGER than the window it is a subset of. The engine takes per-monitor awareness as
        // its module loads; this is the claim that it did.
        Assert.True(
            DisplayAwareness.Current() == DpiAwareness.PerMonitor,
            $"a rectangle read now is not where the window is: {DisplayAwareness.Ensure()}");

        using var register = ProcessRegister.For(declared);
        var launched = register.Launch(exe, ShowsTheMenu);
        var target = AppTarget.FromLaunch(launched, ShowsTheMenu);

        // The second assertion, and the one the engine cannot infer: another instance showing a
        // window is another window that could be photographed. A resident one showing nothing is the
        // ordinary case on a machine where somebody has the tray running, and is never a reason to
        // stop.
        var instances = InstanceCheck.Of(exe, ours: [launched.Pid]);
        Assert.False(
            instances.Refuses,
            $"another FreeWilly is showing a window, so a copy of the screen may be of its menu: "
                + string.Join("; ", instances.Windowed.Select(one => $"pid {one.Pid}")));

        // The menu, waited for rather than read once. A popup has no title, so nothing here matches
        // on one — what is asked for is a window of the process this run launched, which is the
        // only thing `--show-menu` puts on the screen.
        var seen = Attempt.Until(
            () => TopLevelWindows.OfProcess(launched.Pid).FirstOrDefault(),
            Appears(declared),
            declared.Timeouts.For("poll"));

        Assert.True(
            seen.Found,
            $"{Path.GetFileName(exe)} {ShowsTheMenu} drew no window within {seen.WaitedMs}ms over "
                + $"{seen.Polls} look(s), so there is no menu on the screen to copy");

        var menu = seen.Value!;

        // The window is the menu, and it has laid one out. This is the claim the picture cannot make
        // for itself, and the run that proved it is needed wrote a picture of the guest's wallpaper
        // and passed: a window of the right process, of popup style, with nothing over it, through no
        // glass and far from flat — every question a screen copy is asked, answered clean, about a
        // rectangle the menu was not in yet.
        //
        // A window existing is not a menu drawn in it. The script this replaces waited for the window
        // "to appear AND draw" and said so in one breath; what it could check was the border colour
        // of what came back, which is a pixel and this engine asserts none. What can be asked instead
        // is the tree: a drop-down that has laid out reports the entries it holds, and a rectangle
        // that reports none is either not the menu or not drawn yet.
        // The first entry and not "a MenuItem", because this menu holds five and the engine refuses
        // to guess between them — correctly, and the refusal is the reason the spelling says which.
        // Ordered rather than named: the names are this product's own strings, and a case that typed
        // one would be wrong in every language it ships from the moment it was written.
        var entries = Resolve.Until(
            System.Windows.Automation.AutomationElement.FromHandle(menu.Handle),
            Locator.Parse("MenuItem[order=top]"),
            Appears(declared),
            declared.Timeouts.For("poll"));

        Assert.True(
            entries.Found,
            $"the window this would photograph holds no menu entry, so it is either not the menu or "
                + $"has not drawn one: {menu}{Environment.NewLine}{entries.Miss}");

        // Derived, and this is the reading WW87 added to the engine for this menu. A render cannot
        // draw a popup, so a route that answered "render" here would photograph nothing and say it
        // had — and the two facts that make this one a popup are that it is a popup style and that
        // nothing owns it, neither of which is its class name.
        var route = CaptureRoute.For(menu);
        Assert.False(route.Renders, $"the menu routed to a render, which has no tree to draw: {route.Sentence()}");
        Assert.Equal(OutOfReach.OwnedPopup, route.Reach);

        // What the window paints, which is smaller than what it owns by the resize border and the
        // drop shadow. Copying the larger one puts a strip of whatever is behind the window down
        // every edge of the file.
        var frame = PaintedFrame.Of(menu.Handle);
        Assert.NotNull(frame);

        var into = Path.Combine(declared.Captures, "menu.png");
        Directory.CreateDirectory(Path.GetDirectoryName(into)!);

        // The three readings the copy itself needs are taken by the receipt, around the take: the
        // glass before it, the region either side of it, and the colours off the file afterwards. A
        // wrong capture throws here, and the file is written either way — a picture nobody may trust
        // is still evidence about what went wrong, and what the refusal withdraws is the claim that
        // it is a capture.
        var receipt = CaptureReceipt.Taking(
            into,
            menu,
            target,
            path => ScreenCopy.Into(frame!.Painted, path),
            frame,
            route);

        // A receipt existing at all is the claim, and it is the whole of what this case replaces:
        // every question above refuses by throwing, which is what the script's five `exit 1`s were.
        // What is left to say is that the file a reader is pointed at is the file that was written,
        // and that the line naming it names the process this run started — which is the sentence the
        // script printed so that the next wrong capture would report itself.
        Assert.True(File.Exists(into), $"the capture answered clean and wrote no file: {receipt.Sentence()}");
        Assert.Equal(into, receipt.Path);
        Assert.Equal(launched.Pid, receipt.Target.Pid);
    }
}
