using System.Diagnostics;
using System.Windows.Automation;
using FreeWilly.Core.Engine;

namespace FreeWilly.Tray.Cli;

/// <summary>
/// Drives the real window through UI Automation, rather than reading its source (DD214).
/// </summary>
/// <remarks>
/// <para><b>Every other assertion this project makes about the window is a string match over
/// code.</b> DD207 checks that one file mentions the stop signal before the teardown; DD210 checks
/// that another mentions the interlude before the work. Both were right at the time and neither can
/// fail when a handler is left unwired, when an <c>x:Name</c> moves in markup but not in code, or
/// when a button renders disabled. DD212 was found by driving the window and could not have been
/// found any other way.</para>
///
/// <para><b>A verb and not a test, for the same reason <c>--capture-window</c> is one.</b> The path
/// it drives stops Docker and terminates a distribution, so it cannot sit in a suite somebody runs
/// to see whether a rename compiled. It is invoked deliberately, and the half that costs anything is
/// behind a flag on top of that: <see cref="CheckFlag"/>. Without it this reads the window and
/// changes nothing, which is still the half that catches an unwired handler.</para>
///
/// <para><b>It drives whatever window is there.</b> A tray already running is the ordinary case and
/// is the one worth exercising; where there is none, this launches one and leaves it up, because the
/// panel it just read is the thing an operator wants to look at.</para>
/// </remarks>
internal static class WindowDriver
{
    private const int Ok = 0;
    private const int Failed = 1;
    private const int Usage = 2;

    /// <summary>What asks for the half that stops Docker.</summary>
    internal const string CheckFlag = "--check";

    /// <summary>
    /// The button on a Windows message box that means yes, addressed by its control id.
    /// </summary>
    /// <remarks>
    /// <c>IDYES</c>, which WPF's <see cref="System.Windows.MessageBox"/> hands to the Win32 dialog.
    /// Not the caption: this machine renders it as <em>Sim</em>, a German one renders it as
    /// <em>Ja</em>, and a driver that matched the word would pass on exactly one desk.
    /// </remarks>
    private const int YesButton = 6;

    /// <summary><c>GW_ENABLEDPOPUP</c>: the enabled popup a window owns, if it has one.</summary>
    private const uint EnabledPopup = 6;

    /// <summary><c>BM_CLICK</c>.</summary>
    private const uint ClickMessage = 0x00F5;

    /// <summary>How long to wait for a window that is being launched.</summary>
    private static readonly TimeSpan WindowBudget = TimeSpan.FromSeconds(30);

    /// <summary>How long to wait for a control that should already be there.</summary>
    private static readonly TimeSpan ControlBudget = TimeSpan.FromSeconds(10);

    /// <summary>How long a check may take before this gives up watching it.</summary>
    /// <remarks>
    /// Generous, because the first measured run took seventeen minutes and the second disk it runs
    /// against will be somebody else's. Giving up is not the same as stopping it: the run carries on
    /// in the window, and this says so rather than claiming a failure it did not observe.
    /// </remarks>
    private static readonly TimeSpan CheckBudget = TimeSpan.FromMinutes(45);

    /// <summary>How often the window is asked again.</summary>
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(250);

    /// <summary>Drive the window.</summary>
    /// <param name="args">Nothing, or <see cref="CheckFlag"/>.</param>
    /// <returns>The process exit code.</returns>
    internal static int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var checking = args.Contains(CheckFlag, StringComparer.Ordinal);
        var rest = args.Where(a => !string.Equals(a, CheckFlag, StringComparison.Ordinal)).ToArray();
        if (rest.Length > 0)
        {
            Console.Error.WriteLine(
                $"{CommandLine.ExecutableName}: unexpected argument {rest[0]}: "
                + $"{CommandLine.DriveWindowVerb} takes {CheckFlag} or nothing");
            return Usage;
        }

        try
        {
            return Drive(checking);
        }
        catch (ElementNotAvailableException gone)
        {
            // The window closed under the driver. Its own failure, and not the window's: reporting
            // it as a defect in the product would be this verb blaming somebody for pressing a
            // close button.
            Console.Error.WriteLine(
                $"{CommandLine.ExecutableName}: the window went away while this was driving it: "
                + gone.Message);
            return Failed;
        }
        catch (TimeoutException late)
        {
            Console.Error.WriteLine($"{CommandLine.ExecutableName}: {late.Message}");
            return Failed;
        }
    }

    private static int Drive(bool checking)
    {
        var window = TheWindow();
        Say("window", $"{window.Current.Name}, pid {window.Current.ProcessId}");

        // The destination is a radio button in the nav strip, so selecting it is a Select and not a
        // click at a coordinate. A page that never drew is the failure this whole verb is for, so
        // the next lookup is what proves the selection landed.
        var engine = Find(window, "NavEngine", "the Engine destination");
        Pattern<SelectionItemPattern>(
            engine, SelectionItemPattern.Pattern, "the Engine destination").Select();

        var check = Find(window, "Check", "the Check filesystem button");
        var compact = Find(window, "Compact", "the Compact button");
        var headline = Find(window, "FoundHeadline", "the outcome headline");
        var reading = Find(window, "MachineHeading", "the machine verdict");

        Say("engine", "the destination is selected and its controls are on screen");
        Say("check", Describe(check));
        Say("compact", Describe(compact));
        Say("panel", Text(headline));
        Say("machine", Text(reading));

        // Before a check has found anything, and asserted rather than assumed: the design's rule is
        // that nobody is offered a write before seeing what it is for, and a Repair sitting visible
        // on a freshly opened page would be that rule quietly gone.
        if (Look(window, "Repair") is { } repair && !repair.Current.IsOffscreen)
        {
            Console.Error.WriteLine(
                $"{CommandLine.ExecutableName}: Repair is on screen before any check has run, so "
                + "the page is offering a write to the filesystem with nothing behind it");
            return Failed;
        }

        Say("repair", "not offered, which is right before a check has found anything");

        if (!checking)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"  Read only. `{CommandLine.ExecutableName} {CommandLine.DriveWindowVerb} "
                + $"{CheckFlag}` drives the check itself, which stops Docker.");
            return Ok;
        }

        return DriveTheCheck(window, check, headline, reading);
    }

    /// <summary>Press Check filesystem, answer the dialog, and read the ending back.</summary>
    /// <param name="window">The window.</param>
    /// <param name="check">The button.</param>
    /// <param name="headline">Where the outcome is written.</param>
    /// <param name="reading">Where the machine's verdict is written.</param>
    /// <returns>The exit code.</returns>
    /// <remarks>
    /// The whole of the DD210 path, in the order a person walks it. What it is watching for is not
    /// that <c>e2fsck</c> was happy — a dirty filesystem is a finding and this reports it as one —
    /// but that the window moved: it asked, it said it was working, it stopped saying so, and the
    /// buttons came back.
    /// </remarks>
    private static int DriveTheCheck(
        AutomationElement window,
        AutomationElement check,
        AutomationElement headline,
        AutomationElement reading)
    {
        Console.WriteLine();
        Console.WriteLine("  Docker is about to stop. This is the check, driven for real.");
        Console.WriteLine();

        Pattern<InvokePattern>(
            check, InvokePattern.Pattern, "the Check filesystem button").Invoke();

        // Asked of the window and asked in Win32, which is DD227's whole finding. The confirmation
        // is a native message box: UI Automation does not list it among the desktop's children and
        // does not expose its buttons as descendants, so a driver looking there concludes it never
        // appeared. It is there, it is owned by this window, and `GW_ENABLEDPOPUP` names it at once.
        //
        // The complaint says what was observed and stops there. It used to add "so the page took the
        // engine down without asking", which the first real run disproved twice over: the dialog was
        // up and the engine was still serving. A driver that guesses at a consequence it did not
        // watch for is the thing this whole verb exists to replace.
        var dialog = AwaitValue(
            () => OwnedPopup(window),
            ControlBudget,
            "the Check filesystem button was invoked and no confirmation appeared. Whether the "
            + "engine was touched is not something this watched, so it is not being claimed");

        Say("dialog", Caption(dialog));

        var yes = GetDlgItem(dialog, YesButton);
        if (yes == IntPtr.Zero)
        {
            throw new TimeoutException(
                $"the confirmation has no button with control id {YesButton}, so there is no way to "
                + "agree to it that does not depend on what language this machine is in");
        }

        // BM_CLICK on the control itself, which is what a press is. A WM_COMMAND posted to the
        // dialog is the other way and is worse: it is obeyed even where the button is disabled, so
        // it would answer a question the window was not actually asking.
        _ = SendMessage(yes, ClickMessage, IntPtr.Zero, IntPtr.Zero);

        Say("agreed", $"pressed control {YesButton}, whatever this machine calls it");

        // The working sentence has to arrive before the ending, or what follows would read a panel
        // that had not been touched yet and call the previous run's headline an outcome.
        Await(
            () => Text(headline) == RepairPrompt.Working.Headline ? headline : null,
            ControlBudget,
            "the panel never said it was working, so the click reached nothing");

        Say("working", RepairPrompt.Working.Headline);

        var ending = Await(
            () => Text(headline) is { Length: > 0 } said
                && said != RepairPrompt.Working.Headline ? said : null,
            CheckBudget,
            $"the check was still running after {CheckBudget.TotalMinutes:0} minutes. It has not "
            + "failed and this has stopped watching it; the window still has the panel");

        Console.WriteLine();
        Say("ending", ending);
        Say("detail", Text(Find(window, "FoundDetail", "the outcome detail")));
        Say("machine", Text(reading));

        var steps = Text(Find(window, "FoundSteps", "the transcript"));
        if (steps.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine(steps);
        }

        // The buttons coming back is the end of the run as far as the window is concerned, and it is
        // the assertion no source match could make: a page that threw on its way out leaves them
        // dead and looks exactly like a page still working.
        if (!Await(
            () => check.Current.IsEnabled ? check : null,
            ControlBudget,
            "the check ended and its own button never came back, so the page is stuck")
            .Current.IsEnabled)
        {
            return Failed;
        }

        Say("buttons", "enabled again, so the page finished its own work");
        return Ok;
    }

    /// <summary>The window this is driving, launching one where there is none.</summary>
    /// <returns>The window.</returns>
    /// <remarks>
    /// A launch and not a refusal, so this verb works on a machine where nobody has opened the tray.
    /// The window is left up afterwards either way: it is the thing the operator is being asked to
    /// look at, and a driver that closed it would take the evidence with it.
    /// </remarks>
    private static AutomationElement TheWindow()
    {
        if (Window() is { } already)
        {
            return already;
        }

        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("this process has no path");

        // Through the ordinary launch and not a second path: a second instance raises the live one
        // and exits (DD81), so this is also correct on the race where a tray appears in between.
        using (Process.Start(new ProcessStartInfo(exe, CommandLine.WindowVerb)
        {
            UseShellExecute = false,
        }))
        {
            Say("launch", "no window was open, so one was started and is being left up");
        }

        return Await(
            Window,
            WindowBudget,
            $"no FreeWilly window appeared within {WindowBudget.TotalSeconds:0} seconds");
    }

    /// <summary>The product's window on the desktop, or nothing.</summary>
    /// <remarks>
    /// Matched on the name and then on the process, because a window called FreeWilly could be an
    /// editor with this repository open, and driving that would produce a failure about a control it
    /// was never going to have.
    /// </remarks>
    private static AutomationElement? Window() => AutomationElement.RootElement
        .FindAll(
            TreeScope.Children,
            new PropertyCondition(AutomationElement.NameProperty, "FreeWilly"))
        .Cast<AutomationElement>()
        .FirstOrDefault(IsOurs);

    private static bool IsOurs(AutomationElement element)
    {
        try
        {
            using var owner = Process.GetProcessById(element.Current.ProcessId);
            return string.Equals(owner.ProcessName, "FreeWilly", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException or ElementNotAvailableException)
        {
            // The window or the process went away between finding it and asking about it. Not ours
            // as far as anything here can tell.
            return false;
        }
    }

    /// <summary>
    /// The modal dialog a window owns, or nothing (DD227).
    /// </summary>
    /// <param name="window">The window that would own it.</param>
    /// <returns>The dialog's handle, or <see langword="null"/> where there is none.</returns>
    /// <remarks>
    /// <para><b>Win32 and not UI Automation, and that is the finding rather than a preference.</b>
    /// This looked under the desktop for a child of class <c>#32770</c>, which is where a top-level
    /// dialog ought to be and where this one is not: measured on 29 August 2026 with the box plainly
    /// on screen, UI Automation listed twelve desktop children and none of them was it. It does not
    /// expose the dialog's buttons as descendants either, so the second half of the old approach was
    /// no better than the first.</para>
    ///
    /// <para><c>GW_ENABLEDPOPUP</c> answers the question the driver is actually asking: what is this
    /// window's modal, if it has one. The window being disabled is the same fact from the other end,
    /// and a nullable handle is enough to poll on.</para>
    /// </remarks>
    private static IntPtr? OwnedPopup(AutomationElement window)
    {
        var owner = (IntPtr)window.Current.NativeWindowHandle;
        if (owner == IntPtr.Zero)
        {
            return null;
        }

        var popup = GetWindow(owner, EnabledPopup);
        return popup == IntPtr.Zero || popup == owner ? null : popup;
    }

    /// <summary>What a native window's title bar says.</summary>
    private static string Caption(IntPtr window)
    {
        var text = new System.Text.StringBuilder(256);
        return GetWindowText(window, text, text.Capacity) > 0 ? text.ToString() : "(no caption)";
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetDlgItem(IntPtr dialog, int controlId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport(
        "user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode,
        EntryPoint = "GetWindowTextW")]
    private static extern int GetWindowText(
        IntPtr window, System.Text.StringBuilder text, int capacity);

    /// <summary>One control, by the name it carries in markup.</summary>
    /// <param name="window">Where to look.</param>
    /// <param name="id">The <c>x:Name</c>, which WPF publishes as the automation id.</param>
    /// <param name="what">What it is, for the refusal.</param>
    /// <returns>The element.</returns>
    /// <exception cref="TimeoutException">Where nothing in the window carries that id.</exception>
    private static AutomationElement Find(AutomationElement window, string id, string what) =>
        Await(
            () => Look(window, id),
            ControlBudget,
            $"{what} is not in the window under the id {id}, so either the name moved in markup or "
            + "the destination it is on never drew");

    private static AutomationElement? Look(AutomationElement window, string id) =>
        window.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, id));

    /// <summary>One pattern off a control, refused by name where it is not supported.</summary>
    /// <typeparam name="T">The pattern.</typeparam>
    /// <param name="element">The control.</param>
    /// <param name="pattern">Which pattern to ask for.</param>
    /// <param name="what">What the control is, for the refusal.</param>
    /// <returns>The pattern.</returns>
    /// <remarks>
    /// A control that does not support the pattern is the failure this verb exists to catch: a
    /// button rendered as something unclickable looks identical in markup and in a source match.
    /// </remarks>
    private static T Pattern<T>(AutomationElement element, AutomationPattern pattern, string what)
        where T : BasePattern =>
        element.TryGetCurrentPattern(pattern, out var got) && got is T typed
            ? typed
            : throw new TimeoutException(
                $"{what} does not support {pattern.ProgrammaticName}, so there is no way to work it "
                + "that is not a click at a coordinate");

    /// <summary>
    /// The same wait, for something that is a handle rather than an object.
    /// </summary>
    /// <typeparam name="T">What is being waited for.</typeparam>
    /// <param name="look">Asks once.</param>
    /// <param name="budget">How long to keep asking.</param>
    /// <param name="complaint">What to say on giving up.</param>
    /// <returns>The answer.</returns>
    /// <remarks>
    /// A second method rather than one relaxed constraint, because <c>T?</c> means two different
    /// things either side of it and a single signature cannot mean both.
    /// </remarks>
    private static T AwaitValue<T>(Func<T?> look, TimeSpan budget, string complaint)
        where T : struct
    {
        var deadline = DateTime.UtcNow + budget;
        while (true)
        {
            if (look() is { } got)
            {
                return got;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(complaint);
            }

            Thread.Sleep(Poll);
        }
    }

    /// <summary>Ask again until there is an answer, or give up saying what was expected.</summary>
    private static T Await<T>(Func<T?> look, TimeSpan budget, string complaint)
        where T : class
    {
        var deadline = DateTime.UtcNow + budget;
        while (true)
        {
            if (look() is { } got)
            {
                return got;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(complaint);
            }

            Thread.Sleep(Poll);
        }
    }

    /// <summary>What a text element says now.</summary>
    /// <remarks>
    /// A WPF <c>TextBlock</c> publishes its text as the automation name, so this is the same string
    /// a screen reader would be given. Read fresh each time rather than cached: the whole point is
    /// that it changes while this is watching.
    /// </remarks>
    private static string Text(AutomationElement element)
    {
        try
        {
            return element.Current.Name ?? "";
        }
        catch (ElementNotAvailableException)
        {
            return "";
        }
    }

    private static string Describe(AutomationElement button) =>
        $"{button.Current.Name}, {(button.Current.IsEnabled ? "enabled" : "DISABLED")}";

    /// <summary>One line, in the column shape every other verb prints.</summary>
    private static void Say(string what, string detail) =>
        Console.WriteLine($"  {what,-8}  {detail}");
}
