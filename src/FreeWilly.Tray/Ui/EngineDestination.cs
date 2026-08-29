using FreeWilly.Core.Engine;

namespace FreeWilly.Tray.Ui;

/// <summary>
/// Wires the Engine destination's three seams to this machine (DD197, DD199, DD204).
/// </summary>
/// <remarks>
/// <para>One place, and it is not the shell. <c>ShellAndPagesTests</c> holds
/// <c>MainWindow.xaml.cs</c> under a line count and says in as many words that it is not a budget to
/// top up: a window that constructed a journal reader, a machine report and a filesystem repair
/// would be the shell growing a collaborator per seam, which is the shape that test refuses.</para>
///
/// <para>Not in Core either, because two of the three reach things this assembly owns: the hold on
/// the virtual machine is a process, and the check is the same object the <c>--fsck</c> verb calls.
/// Core carries the sequence and the decisions; this carries the wiring.</para>
/// </remarks>
internal static class EngineDestination
{
    /// <summary>The seams, against the machine this is running on.</summary>
    /// <returns>The seams.</returns>
    internal static EngineSeams OnThisMachine() => new(
        new EngineJournalFile(),
        LiveMachineReport.OnThisMachine(),
        Cli.FilesystemWork.OnThisMachine());
}
