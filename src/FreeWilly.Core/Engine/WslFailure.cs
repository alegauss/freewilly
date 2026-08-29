namespace FreeWilly.Core.Engine;

/// <summary>
/// A <c>wsl.exe</c> failure whose own words name a WSL internal, read back as what happened (DD190).
/// </summary>
/// <remarks>
/// <para>What the host wrote on 29 August 2026 was "the daemon exited while starting", followed by
/// "wsl.exe exited -1: getpwnam(root) failed 5". Nothing in that says what happened, and the obvious
/// reading of it is wrong: errno 5 is <c>EIO</c>, so root was not missing. The file holding it could
/// not be read, because the distribution's root filesystem had remounted read-only after an unclean
/// shutdown. A reader who takes the message at face value goes looking for a user that is there.</para>
///
/// <para><b>The remedy is named because there is one, and it is not guessable.</b> A root filesystem
/// cannot check itself, so repairing this needs the distribution down, its disk attached to Windows,
/// and <c>e2fsck</c> run against it from a different distribution. That is four commands nobody
/// derives from "getpwnam failed", which is what makes printing them the whole value here.</para>
///
/// <para>Deliberately not the repair itself. Running <c>e2fsck</c> unattended means this tool
/// registering something on the machine to run it from, which is a larger question than the sentence
/// this task exists to fix.</para>
/// </remarks>
/// <param name="Meaning">What the failure was, in one clause a status detail can carry.</param>
/// <param name="Remedy">The commands that repair it, one per line, ready to be printed.</param>
public sealed record WslFailure(string Meaning, IReadOnlyList<string> Remedy)
{
    /// <summary>The errno a read that failed on the medium reports.</summary>
    /// <remarks>
    /// The whole diagnosis turns on this number. <c>getpwnam</c> failing with 2 is <c>ENOENT</c> and
    /// really is a missing user; failing with 5 is <c>EIO</c> and is the filesystem underneath.
    /// </remarks>
    private const string Eio = "failed 5";

    /// <summary>
    /// Read a launcher's words, or a status detail carrying them, for a failure worth explaining.
    /// </summary>
    /// <param name="said">What was said, or <see langword="null"/>.</param>
    /// <param name="distribution">The distribution it was said about.</param>
    /// <param name="basePath">Where WSL registered that distribution, which is where its disk is.</param>
    /// <returns>The reading, or <see langword="null"/> where this is not that failure.</returns>
    /// <remarks>
    /// Asked of a distribution the caller knows is registered. <c>WSL_E_USER_NOT_FOUND</c> out of one
    /// that is <em>not</em> registered means what it says, and this would turn a true message into a
    /// misleading one.
    /// </remarks>
    public static WslFailure? Of(string? said, string distribution, string basePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(distribution);
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);

        if (said is null || !LooksLikeAnUnreadableRoot(said))
        {
            return null;
        }

        return new WslFailure(
            $"that is EIO rather than a missing user, so {distribution}'s filesystem could not be "
            + "read: its root has remounted read-only, which is what an unclean shutdown leaves",
            Repair(distribution, basePath));
    }

    /// <summary>
    /// The same disk, noticed a boot earlier: what the distribution's own kernel log said (DD191).
    /// </summary>
    /// <param name="said">The kernel lines that complained.</param>
    /// <param name="distribution">The distribution they were about.</param>
    /// <param name="basePath">Where WSL registered it.</param>
    /// <returns>The reading, carrying the same remedy.</returns>
    /// <remarks>
    /// One repair, two ways of arriving at it. <see cref="Of"/> is the failure being found by the
    /// start that dies on it, and this is the warning WSL wrote while mounting the same filesystem a
    /// boot earlier, which nothing was listening for. They differ in when they are noticed and in
    /// nothing else, so they must not differ in what they tell the user to do.
    /// </remarks>
    public static WslFailure OfDirtyFilesystem(string said, string distribution, string basePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(said);
        ArgumentException.ThrowIfNullOrWhiteSpace(distribution);
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);

        return new WslFailure(said, Repair(distribution, basePath));
    }

    /// <summary>The commands that repair the distribution's disk.</summary>
    /// <param name="distribution">Which distribution.</param>
    /// <param name="basePath">Where WSL registered it.</param>
    /// <returns>The lines, ready to be printed.</returns>
    private static IReadOnlyList<string> Repair(string distribution, string basePath)
    {
        var disk = Path.Combine(basePath, "ext4.vhdx");
        return
        [
            $"{distribution} cannot check its own root, so the disk is checked from elsewhere:",
            $"  wsl --terminate {distribution}",
            $"  wsl --mount --vhd \"{disk}\" --bare",
            "  wsl -d <another distribution> -u root --exec lsblk",
            "  wsl -d <another distribution> -u root --exec e2fsck -fy /dev/sdX",
            $"  wsl --unmount \"{disk}\"",
            "lsblk names the disk the mount attached; e2fsck goes against that one.",
        ];
    }

    /// <summary>Whether these words are the signature of a root that cannot be read.</summary>
    /// <param name="said">What was said.</param>
    /// <returns><see langword="true"/> where they are.</returns>
    /// <remarks>
    /// Two spellings of one failure. WSL resolves the user before it execs anything, so a root it
    /// cannot read surfaces either as the C call by name or as the error code the launcher maps that
    /// to — and DD192 found the second one arriving as mojibake, which is why the match is on the
    /// code rather than on a sentence around it.
    /// </remarks>
    private static bool LooksLikeAnUnreadableRoot(string said) =>
        (said.Contains("getpwnam", StringComparison.OrdinalIgnoreCase)
            || said.Contains("getpwuid", StringComparison.OrdinalIgnoreCase))
        && said.Contains(Eio, StringComparison.OrdinalIgnoreCase)
        || said.Contains("WSL_E_USER_NOT_FOUND", StringComparison.OrdinalIgnoreCase);
}
