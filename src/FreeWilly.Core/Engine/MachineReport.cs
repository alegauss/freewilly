using System.Globalization;
using System.Text;

namespace FreeWilly.Core.Engine;

/// <summary>One fact, as the page prints it.</summary>
/// <param name="Name">What it is.</param>
/// <param name="Value">What it says, already rendered.</param>
public sealed record MachineReading(string Name, string Value);

/// <summary>A column of facts about one thing.</summary>
/// <param name="Title">Which thing.</param>
/// <param name="Readings">Its facts, in the order they are read.</param>
public sealed record MachineGroup(string Title, IReadOnlyList<MachineReading> Readings);

/// <summary>
/// What the machine under the engine is doing, and whether that is well (DD197, DD198).
/// </summary>
/// <param name="Well">Whether nothing is wrong with it.</param>
/// <param name="Summary">The verdict, in one clause.</param>
/// <param name="Groups">The readings the verdict was made of.</param>
/// <remarks>
/// The verdict travels with the readings rather than being derived from them twice. A window that
/// decided health by reading its own rendered strings and an agent surface that decided it again
/// from the same strings would be two answers to one question, and the second one to change would
/// be the one nobody noticed.
/// </remarks>
public sealed record MachineHealth(
    bool Well, string Summary, IReadOnlyList<MachineGroup> Groups);

/// <summary>
/// Where the Engine page's readings come from. The seam that makes the page capturable (DD197, L6).
/// </summary>
/// <remarks>
/// Every window in this project has to be renderable without the thing it is about, and for this
/// panel the thing it is about is a virtual machine. A capture taken against the real one is a
/// picture of whatever that laptop's disk looked like that afternoon, which is neither reviewable
/// nor safe to put in a README.
/// </remarks>
public interface IMachineReport
{
    /// <summary>Take every reading.</summary>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>The groups, in the order they are shown.</returns>
    /// <remarks>
    /// Asynchronous because the live one is several <c>wsl.exe</c> children and a pipe request, and
    /// the page it draws on must not be the thing that goes white for three seconds. A fixture
    /// answers from memory and completes before the caller yields, which is what keeps a capture of
    /// this page deterministic.
    /// </remarks>
    Task<MachineHealth> ReadAsync(CancellationToken cancellation = default);
}

/// <summary>
/// How a caller that already has an engine open gets the readings for it (DD198).
/// </summary>
/// <remarks>
/// A named seam rather than a function, because <c>MachineReads</c> holds interfaces and the guard
/// over it says why: a property a measurement cannot stand in for is a verb that reaches the real
/// machine whatever it was handed. The indirection is the engine — the report asks the pipe one
/// question, and a verb on the agent surface is given the engine it is to use.
/// </remarks>
public interface IMachineReports
{
    /// <summary>The readings, asking <paramref name="engine"/> the one question it owns.</summary>
    /// <param name="engine">The engine the caller already has.</param>
    /// <returns>The report.</returns>
    IMachineReport Through(Agent.IEngineReads engine);
}

/// <summary>
/// The six readings diagnosing the 29 August 2026 failure took by hand (DD197).
/// </summary>
/// <remarks>
/// <para>That diagnosis meant <c>wsl --list --verbose</c> for the state of the distribution,
/// <c>dmesg</c> out of a second distribution for the ext4 errors, <c>blkid</c> for the device, the
/// Lxss registry key for the path of the virtual disk, a PowerShell query for free space on the
/// Windows volume, and the journal for what the host had seen. Every one of them is a reading this
/// tool is better placed to take than a user is, and none of them is a remedy: DD190 owns what to
/// do about what this reports.</para>
///
/// <para><b>The two sizes are here as a pair on purpose.</b> A question about a full disk needs the
/// virtual disk's size on the Windows volume beside the space used inside the distribution, because
/// a sparse file that has grown to fifty gigabytes and a filesystem holding fifty gigabytes are
/// different facts and only one of them is the user's data.</para>
/// </remarks>
public static class MachineReport
{
    /// <summary>What a reading says where the thing it names could not be reached.</summary>
    public const string Unread = "could not be read";

    /// <summary>Render every group as text, which is what the copy button hands over.</summary>
    /// <param name="groups">The groups.</param>
    /// <returns>The report, as one block.</returns>
    /// <remarks>
    /// The point of this panel is handing what it says to somebody else, so the text is the
    /// deliverable rather than a convenience on top of one. Grouped and aligned the way the page
    /// shows it, because a reader who has been sent this and a reader looking at the window should
    /// be talking about the same thing.
    /// </remarks>
    public static string AsText(IReadOnlyList<MachineGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        var width = groups
            .SelectMany(group => group.Readings)
            .Select(reading => reading.Name.Length)
            .DefaultIfEmpty(0)
            .Max();

        var text = new StringBuilder();
        foreach (var group in groups)
        {
            text.Append(group.Title).Append('\n');
            foreach (var reading in group.Readings)
            {
                text.Append("  ").Append(reading.Name.PadRight(width))
                    .Append("  ").Append(reading.Value).Append('\n');
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    /// <summary>A size in bytes, as a reader says it.</summary>
    /// <param name="bytes">How many.</param>
    /// <returns>The text.</returns>
    /// <remarks>
    /// Binary units and one decimal, which is what every other tool a user compares this against
    /// prints for a disk. The invariant culture for the reason the build column uses it: this is a
    /// figure being pasted into a bug report.
    /// </remarks>
    public static string Size(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return string.Create(
            CultureInfo.InvariantCulture, $"{size:0.#} {units[unit]}");
    }
}
