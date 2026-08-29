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
/// <param name="StartsAgain">
/// Whether the page starts the engine again at this ending (DD205, DD210). It was a button before,
/// and a button is a thing to notice: the page took the engine down without being asked to leave it
/// down, so putting it back is the page finishing its own work rather than a favour to offer.
///
/// <para>Never where the run failed, which DD190 settled and this does not reopen: an engine started
/// on a filesystem the check could not finish reading is the state that task is about.</para>
/// </param>
/// <remarks>
/// The page renders this and decides none of it. A window that worked out for itself whether to
/// offer a repair would be a second opinion on the same <c>e2fsck</c> exit code the CLI already
/// reads, and the two would drift — which for this pair means one surface offering to write to the
/// filesystem holding every image the user has while the other says there is nothing to mend.
/// </remarks>
public sealed record RepairPrompt(
    string Headline, string Detail, bool OfferRepair, bool StartsAgain = false)
{
    /// <summary>What the panel says before anything has been run.</summary>
    /// <remarks>
    /// The cost is in the sentence rather than discovered by pressing. Checking needs the root
    /// unmounted, so the engine and every container on it stop for the duration, and since DD210 it
    /// also says what happens afterwards: an interruption nobody is told the end of is one they have
    /// to sit and watch.
    /// </remarks>
    public static readonly RepairPrompt Idle = new(
        "The filesystem can be checked from here",
        "Docker stops while the check runs, and starts again by itself at the end. The check only "
        + "looks at the disk: nothing is changed unless it finds a problem and you approve a repair.",
        OfferRepair: false);

    /// <summary>
    /// What a check is asked in, before it interrupts anything (DD210).
    /// </summary>
    /// <remarks>
    /// <b>Not the confirmation DD199 refused.</b> That asymmetry was about the filesystem, and it
    /// still holds: reading cannot make one worse, so a read needs nobody's consent to go ahead.
    /// What is being consented to here is the interruption, which a check costs whatever it finds,
    /// and the containers it stops are not the filesystem's to risk. Somebody with a database up
    /// deserves to hear that before it goes down and not from the sentence beside the button.
    ///
    /// <para>It says the ending too. A dialog that named only the cost would be one people learn to
    /// dismiss without reading, and Docker coming back on its own is the part that makes this worth
    /// agreeing to.</para>
    ///
    /// <para><b>In the reader's words and not this project's.</b> The first version of this said the
    /// distribution's root would be unmounted, which is what happens and not what it costs: somebody
    /// deciding whether to press a button needs to know Docker goes away and when it comes back, and
    /// a sentence they have to translate first is one they agree to without having understood it.
    /// No duration is promised either. The one that was promised, minutes rather than seconds, was a
    /// guess, and the first measured run took seventeen.</para>
    /// </remarks>
    public const string CheckConfirmation =
        "Docker stops while this check runs.\n\n"
        + "Containers, builds and every docker command stop with it and stay unavailable until the "
        + "check finishes. How long that takes depends on the size of the disk.\n\n"
        + "The check only looks at the disk. It changes nothing.\n\n"
        + "Docker starts again by itself at the end.\n\n"
        + "Stop Docker and check now?";

    /// <summary>What the panel says while the work is running.</summary>
    /// <remarks>
    /// It repeats the ending (DD210). This is the sentence somebody reads for the several minutes
    /// the engine is down, and a wait whose end is only stated in a dialog already dismissed is a
    /// wait people spend wondering whether to intervene.
    /// </remarks>
    public static readonly RepairPrompt Working = new(
        "Checking the filesystem",
        "Docker is stopped, and starts again by itself when this finishes. How long it takes "
        + "depends on the size of the disk.",
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
            // What happened to the engine is said rather than left to be worked out, and the two
            // cases are not the same sentence (DD210). A run that stopped at the registered guard
            // never touched it; one that stopped at e2fsck left it down, deliberately, and somebody
            // who is not told that goes looking for a second fault.
            var engine = outcome.EngineWentDown
                ? " Docker was left stopped on purpose: a disk this could not finish reading is not "
                + "one to start Docker on. Start engine in the tray menu starts it anyway."
                : " Nothing was stopped, so Docker is as it was.";

            return new RepairPrompt(
                wrote ? "The repair did not finish" : "The check did not finish",
                (outcome.Failure?.Detail ?? "nothing said where it stopped") + engine,
                OfferRepair: false);
        }

        if (outcome.Clean)
        {
            return new RepairPrompt(
                "The filesystem is clean",
                "Nothing needed mending.",
                OfferRepair: false,
                StartsAgain: true);
        }

        return wrote
            ? new RepairPrompt(
                "The filesystem was repaired",
                "What was mended is below, and Docker starting again is also the check that it "
                + "worked.",
                OfferRepair: false,
                StartsAgain: true)
            : new RepairPrompt(
                "The filesystem has errors",
                "A repair would mend them. What the check found is below, and it is worth reading "
                + "before approving one.",
                OfferRepair: true,
                StartsAgain: true);
    }

    /// <summary>
    /// The same ending, with the start this page has just asked for named (DD205, DD210).
    /// </summary>
    /// <param name="budget">How long the tray gives a start before it gives up on it.</param>
    /// <returns>The prompt, saying what was found and what is now happening.</returns>
    /// <remarks>
    /// Appended rather than replacing the ending, which is the whole difference from the panel this
    /// grew out of. The findings are what somebody pressed the button for, and a page that swapped
    /// them for "Starting the engine" the moment the run landed would be answering a question by
    /// throwing the answer away.
    ///
    /// <para>The wait is named because a start is not instant, and until it lands every other
    /// surface here says the engine is not answering.</para>
    /// </remarks>
    public RepairPrompt AndStarting(TimeSpan budget) => this with
    {
        Detail = $"{Detail} Docker is starting again, and is back within about "
            + $"{budget.TotalSeconds:0} seconds.",
        StartsAgain = false,
    };
}
