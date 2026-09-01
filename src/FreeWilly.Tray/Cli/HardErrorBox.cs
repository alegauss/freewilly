using System.Runtime.InteropServices;

namespace FreeWilly.Tray.Cli;

/// <summary>
/// Takes this process, and everything it starts, off the critical-error dialog (DD270).
/// </summary>
/// <remarks>
/// Windows logged the same application popup at every shutdown between 29 and 31 August 2026:
/// <c>wsl.exe</c> failing with 0xC0000142, STATUS_DLL_INIT_FAILED, in the same second the host and
/// the tray both took SessionEnding. It is not a WSL fault and not a fault in how this tool spells
/// the command — Adobe's LogTransport2.exe took the same code in the same second on 30 August, so
/// the condition belongs to the session: under Fast Startup, Windows logs the user off before it
/// hibernates, and process creation in that session stops working part way through.
///
/// <para><b>What makes that worse than a failed launch is that the popup is a hard error.</b> The
/// child sits on a modal box nobody can click, so <see cref="Core.Preflight.Windows.ConsoleTool"/>
/// waits out its whole budget on a process that has already failed. Every failing teardown in the
/// journal ends at exactly the four seconds <see cref="EngineCommand.SessionEndingBudget"/> allows,
/// while the one that worked, on 30 August at 10:08, finished in two.</para>
///
/// <para><b>The launch still fails.</b> Nothing here can make Windows start a process it has decided
/// not to start. What changes is that it fails immediately and silently, which returns the budget to
/// the steps that can still run and takes the dialog off the user's screen.</para>
///
/// <para>Set once, for the whole process, because the error mode is what a child inherits: one call
/// in <c>Main</c> covers every <c>wsl.exe</c> any surface of this executable starts — the host's, the
/// tray's session teardown, and <c>--stop</c>.</para>
/// </remarks>
internal static class HardErrorBox
{
    /// <summary>
    /// SEM_FAILCRITICALERRORS: the system does not show the critical-error-handler box, and the
    /// failure is returned to the caller instead.
    /// </summary>
    internal const uint FailCriticalErrors = 0x0001;

    /// <summary>Stop this process and its children opening one.</summary>
    /// <returns>The error mode now in force, which is what a child will inherit.</returns>
    /// <remarks>
    /// Added to whatever is already set rather than assigned. The error mode is one process-wide
    /// word shared with everything else in here — the WPF and WinForms runtimes among them — and
    /// assigning it would silently clear a flag this code never chose and cannot see.
    /// </remarks>
    internal static uint Suppress()
    {
        var mode = GetErrorMode() | FailCriticalErrors;
        _ = SetErrorMode(mode);
        return mode;
    }

    // DllImport rather than LibraryImport, for the reason ParentConsole states: the generated
    // marshalling stubs need AllowUnsafeBlocks, and neither of these marshals anything.
    [DllImport("kernel32.dll")]
    private static extern uint GetErrorMode();

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint mode);
}
