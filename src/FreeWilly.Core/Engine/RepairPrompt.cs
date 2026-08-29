namespace FreeWilly.Core.Engine;

/// <summary>
/// The filesystem work the window can start, behind a seam (DD199).
/// </summary>
/// <remarks>
/// A window that constructed a <see cref="FilesystemRepair"/> of its own would be a page able to
/// terminate this machine's distribution, which is not something a capture or a test should be one
/// mis-click away from. The real one is wired at the window, the fixture refuses.
/// </remarks>
public interface IFilesystemWork
{
    /// <summary>Read the filesystem and change nothing.</summary>
    /// <param name="report">Called with each step as it lands.</param>
    /// <returns>What was found.</returns>
    RepairOutcome Check(Action<RepairStep> report);

    /// <summary>Read it and mend what it finds.</summary>
    /// <param name="report">Called with each step as it lands.</param>
    /// <returns>What was done.</returns>
    RepairOutcome Fix(Action<RepairStep> report);
}

/// <summary>
/// What the window says about a filesystem check, and whether it offers to mend it (DD199).
/// </summary>
/// <param name="Headline">The one line above the detail.</param>
/// <param name="Detail">What was found, or what is about to happen.</param>
/// <param name="OfferRepair">Whether a repair is worth offering from here.</param>
/// <remarks>
/// The page renders this and decides none of it. A window that worked out for itself whether to
/// offer a repair would be a second opinion on the same <c>e2fsck</c> exit code the CLI already
/// reads, and the two would drift — which for this pair means one surface offering to write to the
/// filesystem holding every image the user has while the other says there is nothing to mend.
/// </remarks>
public sealed record RepairPrompt(string Headline, string Detail, bool OfferRepair)
{
    /// <summary>What the panel says before anything has been run.</summary>
    /// <remarks>
    /// The cost is in the sentence rather than discovered by pressing. Checking needs the root
    /// unmounted, so the engine and every container on it stop for the duration — that is not
    /// something to find out from a button that looked like it only read something.
    /// </remarks>
    public static readonly RepairPrompt Idle = new(
        "The filesystem can be checked from here",
        "Checking needs the distribution's root unmounted, so the engine stops and every container "
        + "with it. Nothing is written unless the check finds something and you approve a repair.",
        OfferRepair: false);

    /// <summary>What the panel says while the work is running.</summary>
    public static readonly RepairPrompt Working = new(
        "Checking the filesystem",
        "The engine is down while this runs. A full check of a disk this size is minutes rather "
        + "than seconds.",
        OfferRepair: false);

    /// <summary>
    /// What a repair is asked in, before it is allowed to write.
    /// </summary>
    /// <remarks>
    /// Names what it writes to rather than asking whether the user is sure. <c>e2fsck -fy</c>
    /// answers yes to every question it would otherwise put, and some of those questions are whether
    /// to discard a damaged inode — so the thing being consented to is a write to the filesystem
    /// holding every image, container and volume on this machine.
    /// </remarks>
    public const string Confirmation =
        "Repair writes to the distribution's filesystem, which holds every image, container and "
        + "volume on this machine. e2fsck answers yes to each of its own questions, and a few of "
        + "those discard a damaged inode rather than mending it.\n\n"
        + "The check above says what is wrong. Repair it?";

    /// <summary>Read an outcome for what the page should now say.</summary>
    /// <param name="outcome">What the check or the repair did.</param>
    /// <param name="wrote">Whether that was a repair rather than a check.</param>
    /// <returns>The prompt.</returns>
    /// <remarks>
    /// A failed run is not the same as a dirty filesystem and does not offer a repair: the steps
    /// name where it stopped, and running a write against a disk this could not finish reading is
    /// the one thing worse than leaving it alone.
    /// </remarks>
    public static RepairPrompt Of(RepairOutcome outcome, bool wrote)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (!outcome.Succeeded)
        {
            return new RepairPrompt(
                wrote ? "The repair did not finish" : "The check did not finish",
                outcome.Failure?.Detail ?? "nothing said where it stopped",
                OfferRepair: false);
        }

        if (outcome.Clean)
        {
            return new RepairPrompt(
                "The filesystem is clean",
                "Nothing needed mending. The engine is stopped and can be started again.",
                OfferRepair: false);
        }

        return wrote
            ? new RepairPrompt(
                "The filesystem was repaired",
                "The engine is stopped and can be started again. What e2fsck did is below.",
                OfferRepair: false)
            : new RepairPrompt(
                "The filesystem has errors",
                "A repair would mend them. What the check found is below, and it is worth reading "
                + "before approving one.",
                OfferRepair: true);
    }
}
