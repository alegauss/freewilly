namespace FreeWilly.Core.Engine;

/// <summary>
/// Everything the Engine destination reads, and the one thing it can start (DD199).
/// </summary>
/// <param name="Journal">Where the engine's own account of itself is read from (DD165).</param>
/// <param name="Machine">What state WSL, the distribution and the engine are in (DD197).</param>
/// <param name="Work">The filesystem check and repair the page's buttons start.</param>
/// <remarks>
/// <para><b>One parameter and not three, and the shell's own budget is why.</b>
/// <c>ShellAndPagesTests</c> holds <c>MainWindow.xaml.cs</c> under a line count and says in as many
/// words that it is not a budget to top up: a destination needing more shell is a destination whose
/// shape should be argued about. Three seams for one page was that argument arriving, and the answer
/// is that the shell should hold one thing per destination rather than one per collaborator.</para>
///
/// <para>Every one of them exists for the same reason (L6): a window in this project has to be
/// renderable without the thing it is about, and for this page that thing is a machine. A capture
/// taken against the real one photographs whatever that laptop's disk looked like that afternoon,
/// and its buttons would be one click from terminating a distribution.</para>
/// </remarks>
public sealed record EngineSeams(
    IEngineJournal Journal, IMachineReport Machine, IFilesystemWork Work);
