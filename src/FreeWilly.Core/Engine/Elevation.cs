namespace FreeWilly.Core.Engine;

/// <summary>What an elevated run did, as much of it as can be known (DD237).</summary>
/// <param name="Ran">Whether a process actually started with the rights it asked for.</param>
/// <param name="ExitCode">What it exited with, or <see langword="null"/> where it never ran.</param>
/// <param name="Refused">Whether the prompt was declined, or this account cannot elevate at all.</param>
/// <param name="Failure">Why it did not run, where that is worth a sentence.</param>
/// <remarks>
/// <para><see cref="Refused"/> is separate from <see cref="Failure"/> on purpose. Declining a UAC
/// prompt is a decision somebody made, and reporting it as an error would be this tool telling a
/// user off for answering the question it asked. It is the one ending here that needs no apology
/// and no bug report.</para>
///
/// <para>There is no output. An elevated child's standard handles belong to the elevated process
/// and cannot be redirected into this one, which is why <see cref="ElevatedCompaction"/> spends a
/// <c>cmd</c> on writing a log to a file it can read afterwards instead.</para>
/// </remarks>
public sealed record ElevatedRun(
    bool Ran, int? ExitCode = null, bool Refused = false, string? Failure = null)
{
    /// <summary>Whether it ran and the command was happy.</summary>
    public bool Succeeded => Ran && ExitCode is 0;
}

/// <summary>
/// Running one command with administrator rights, behind a seam (DD237).
/// </summary>
/// <remarks>
/// A seam because the real one puts a UAC prompt on the screen and waits for a person, which is not
/// something a test suite or a screenshot capture should be one call away from. It is also the
/// boundary this product is careful about: everything above this interface runs as the user, and
/// the only thing below it is a single named command somebody has just pressed a button to ask for.
/// </remarks>
public interface IElevated
{
    /// <summary>Run one command elevated and wait for it.</summary>
    /// <param name="fileName">The executable.</param>
    /// <param name="arguments">Its arguments, already quoted.</param>
    /// <returns>What happened, as far as an elevated child lets a caller know.</returns>
    ElevatedRun Run(string fileName, string arguments);
}
