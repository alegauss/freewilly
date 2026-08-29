namespace FreeWilly.Core.Engine;

/// <summary>
/// What the window tells whoever watches the engine, around work that takes it down (DD210).
/// </summary>
/// <remarks>
/// <para><b>The tray already holds the right idea and was never told.</b> An engine that stops
/// answering is news, so the tray waits out a blip and then announces it (DD164, DD183) — and it
/// suppresses that for a stop somebody asked for, because reporting its own obedience as a fault is
/// not a thing a tool should do. A filesystem check is somebody asking. It was the one stop the tray
/// never heard about, because the window takes the engine down through the path the CLI uses and
/// nothing carries that back, so pressing Check filesystem produced a failure balloon about the
/// engine the button had just stopped on purpose.</para>
///
/// <para>Two calls rather than one flag, because the interruption has two ends. The check owns the
/// engine from <see cref="Expected"/> until <see cref="StartAgain"/>, and what happens in between is
/// not an outage.</para>
/// </remarks>
public interface IEngineInterlude
{
    /// <summary>The engine is about to go down, because somebody here asked for it.</summary>
    void Expected();

    /// <summary>Put it back.</summary>
    void StartAgain();
}

/// <summary>An interlude for a window with no tray behind it.</summary>
/// <remarks>
/// The capture's. It does nothing rather than something plausible, for the reason
/// <see cref="IFilesystemWork"/> refuses there: a window rendered off-screen has nothing watching
/// the engine and nothing to put back, and a seam that pretended otherwise would make a picture look
/// like it had exercised something.
/// </remarks>
public sealed class NoInterlude : IEngineInterlude
{
    /// <inheritdoc/>
    public void Expected()
    {
    }

    /// <inheritdoc/>
    public void StartAgain()
    {
    }
}

/// <summary>
/// Everything the Engine destination reads, and the one thing it can start (DD199).
/// </summary>
/// <param name="Journal">Where the engine's own account of itself is read from (DD165).</param>
/// <param name="Machine">What state WSL, the distribution and the engine are in (DD197).</param>
/// <param name="Work">The filesystem check and repair the page's buttons start.</param>
/// <param name="Interlude">
/// How the page says it is taking the engine down and puts it back (DD210). The page's own start
/// since that task, rather than an action the shell threads through beside these: the stop and the
/// start are two ends of one interruption, and splitting them across two seams is how one of them
/// gets wired and the other forgotten.
/// </param>
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
    IEngineJournal Journal,
    IMachineReport Machine,
    IFilesystemWork Work,
    IEngineInterlude Interlude);
