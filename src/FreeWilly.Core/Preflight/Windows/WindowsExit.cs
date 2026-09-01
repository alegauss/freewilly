namespace FreeWilly.Core.Preflight.Windows;

/// <summary>
/// How an exit code reads in the journal, where Windows chose it rather than the tool (DD274).
/// </summary>
/// <remarks>
/// On 29 August 2026 the journal recorded "the daemon exited: wsl.exe exited 1073807364 without a
/// word". That number is 0x40010004, DBG_TERMINATE_PROCESS, and it means Windows killed the process
/// during the shutdown. Nothing in the line said so, and the evidence that it was happening at all
/// came from the Windows System log rather than from the file this tool keeps for exactly that
/// question.
///
/// <para>The 0xC0000142 case is worse. A child that fails DLL initialisation exits with empty stdout
/// and empty stderr, so what gets reported is a tool that ran and said nothing, which is the same
/// shape as a tool that ran and had nothing to say. Two different failures, both invisible, both
/// specific to a session ending (DD270 is the other half of that story).</para>
///
/// <para>Ordinary exit codes are left exactly as they were. 126 is a shell that could not exec, and
/// dressing it up would be this class claiming to know something about a number it does not.</para>
/// </remarks>
internal static class WindowsExit
{
    /// <summary>STATUS_DLL_INIT_FAILED: the process was created and could not be initialised.</summary>
    internal const uint DllInitFailed = 0xC0000142;

    /// <summary>DBG_TERMINATE_PROCESS: Windows killed it, which during a shutdown is the session.</summary>
    internal const uint TerminatedByWindows = 0x40010004;

    /// <summary>Spell an exit code the way a reader needs it.</summary>
    /// <param name="code">What the process exited with.</param>
    /// <returns>The number, and what it means where Windows is the one that chose it.</returns>
    /// <remarks>
    /// The hex is not decoration. A reader who has to convert 1073807364 by hand before they can
    /// search for it will not, and that is the whole distance between a journal that answers the
    /// question and one that sends somebody to Event Viewer.
    /// </remarks>
    internal static string Spell(int code) => (uint)code switch
    {
        DllInitFailed => "0xC0000142 (Windows would not start the process)",
        TerminatedByWindows => "0x40010004 (Windows killed it)",
        _ when NtStatusShaped(code) => $"{code} (0x{(uint)code:X8})",
        _ => $"{code}",
    };

    /// <summary>Whether Windows chose this code rather than the tool.</summary>
    /// <param name="code">The exit code.</param>
    /// <returns><see langword="true"/> where it is shaped like an NTSTATUS.</returns>
    /// <remarks>
    /// The severity and facility live in the top byte, and the two values that reach a process exit
    /// code are 0x40 (informational, which is where DBG_TERMINATE_PROCESS sits) and 0xC0 (error,
    /// which is where the loader's failures sit).
    ///
    /// <para><b>Deliberately not "anything with the top bits set".</b> <c>wsl.exe</c> answers a
    /// missing distribution with -1, which is 0xFFFFFFFF and is in that range without being an
    /// NTSTATUS at all — it is the ordinary convention for "it did not work". Rendering it as hex
    /// would add noise to the one failure a reader already understands.</para>
    /// </remarks>
    private static bool NtStatusShaped(int code) => ((uint)code >> 24) is 0x40 or 0xC0;
}
