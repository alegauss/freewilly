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

    /// <summary>Hand back what the virtual disk is holding and nothing wants (DD211).</summary>
    /// <param name="report">Called with each step as it lands.</param>
    /// <returns>What was done, and what the disk was either side of it.</returns>
    /// <remarks>
    /// Here rather than behind a seam of its own, because it is the same thing this one exists to
    /// keep out of a capture: a window that constructed it could terminate this machine's
    /// distribution. Two seams for two buttons with one hazard would only be two places to remember
    /// to refuse.
    /// </remarks>
    CompactionOutcome Compact(Action<RepairStep> report);

    /// <summary>
    /// Compact it with administrator rights, where Windows has withdrawn the other way (DD237).
    /// </summary>
    /// <param name="report">Called with each step as it lands.</param>
    /// <param name="saying">
    /// Called as the run says how far into a long step it is, where it can (DD243). Optional, and a
    /// caller that passes nothing gets the steps alone.
    /// </param>
    /// <returns>What was done, and what the disk was either side of it.</returns>
    /// <remarks>
    /// <para>Beside <see cref="Compact"/> rather than folded into it, because the difference is the
    /// whole point: this one raises a UAC prompt and that one never does. A single method with a
    /// flag would be a seam whose most important property was invisible at the call site.</para>
    ///
    /// <para><b>Two channels and not one.</b> A percentage is not a step, and putting it through the
    /// step callback would make it one:
    /// <see cref="CompactionOutcome.Succeeded"/> and <see cref="CompactionOutcome.Failure"/> read
    /// steps by name, and DD244 is what one step meaning something other than what it said already
    /// cost this page.</para>
    /// </remarks>
    CompactionOutcome CompactAsAdministrator(
        Action<RepairStep> report, Action<string>? saying = null);

    /// <summary>
    /// Whether a check on this machine still owes a network call before it can start (DD216).
    /// </summary>
    /// <remarks>
    /// Read by the confirmation, which used to say the wait depends on the size of the disk and was
    /// precise about the wrong thing: warm, the whole sequence is 8.3 seconds and the disk really is
    /// the cost; on a first run it is the fetch, and the person being given that number is the one
    /// with no prior experience to correct it with.
    /// </remarks>
    bool ToolsAreReady { get; }

    /// <summary>
    /// Whether Windows has already refused to hand this machine's blocks back (DD226).
    /// </summary>
    /// <remarks>
    /// Read by the compaction's plan, so a machine that has met the refusal says so before it costs
    /// a second interruption rather than after one. Beside <see cref="ToolsAreReady"/> because it is
    /// the same kind of question one button further along: what this machine can actually do, asked
    /// before anything is pressed.
    /// </remarks>
    bool HandBackWasRefused { get; }

    /// <summary>
    /// Which other WSL distributions are up, and would be stopped by an elevated compaction (DD238).
    /// </summary>
    /// <remarks>
    /// Read by the elevated plan. diskpart needs the virtual disk exclusively and only
    /// <c>wsl --shutdown</c> gives it that, so this one is not like the others on this page: it
    /// reaches past the engine and stops work that has nothing to do with Docker. Naming it before
    /// the UAC prompt is the difference between a cost somebody accepted and one they discovered.
    /// </remarks>
    IReadOnlyList<string> OtherDistributionsRunning { get; }
}

/// <summary>
/// What the window says about work it started on the disk, and what it offers next (DD199, DD211).
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
    /// <summary>
    /// Whether compacting with administrator rights is worth offering from here (DD237).
    /// </summary>
    /// <remarks>
    /// <para>Set on exactly one ending: the hand-back Windows has withdrawn. Not on an ordinary
    /// failure, which is something a second press might get a different answer to, and not on
    /// success, where there is nothing left to offer. Elevation offered anywhere else would be this
    /// tool reaching for administrator rights as a way of getting past problems.</para>
    ///
    /// <para>Beside <see cref="OfferRepair"/> and decided here rather than by the page, for the
    /// reason that one is: a window working out for itself when to raise a UAC prompt would be a
    /// second opinion about the same refusal, and the two surfaces would drift.</para>
    /// </remarks>
    public bool OfferElevated { get; init; }

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
    ///
    /// <para><b>It stopped blaming the disk for a wait the fetch is causing (DD216).</b> Warm, the
    /// whole sequence is 8.3 seconds and the disk really is the cost; on the first check of a machine
    /// the rescue has to download its tools first, and that is the one run where the reader has no
    /// prior experience to correct a wrong number with. So the sentence is chosen by whether this
    /// machine has the tools, rather than being one claim that is true half the time.</para>
    /// </remarks>
    /// <param name="toolsAreReady">Whether a check here still owes a network call.</param>
    /// <returns>The question, as the dialog asks it.</returns>
    public static string CheckConfirmation(bool toolsAreReady) =>
        "Docker stops while this check runs.\n\n"
        + "Containers, builds and every docker command stop with it and stay unavailable until the "
        + "check finishes. "
        + (toolsAreReady
            ? "How long that takes depends on the size of the disk.\n\n"
            : "This is the first check on this machine, so it also downloads the tools it needs "
              + "before it can start.\n\n")
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

    /// <summary>The same sentence, chosen for what this machine still owes (DD216).</summary>
    /// <param name="toolsAreReady">Whether a check here still needs a network.</param>
    /// <returns>The prompt.</returns>
    /// <remarks>
    /// The headline is deliberately the same either way. It is what the page is identified by while
    /// it works — the driving verb waits on it, and a sentence that changed with the machine would
    /// make that wait a guess.
    /// </remarks>
    public static RepairPrompt WorkingOn(bool toolsAreReady) => toolsAreReady
        ? Working
        : Working with
        {
            Detail = "Docker is stopped, and starts again by itself when this finishes. This is "
                + "the first check on this machine, so the tools it needs are being downloaded "
                + "before it can start.",
        };

    /// <summary>
    /// The plan a compaction is asked in, before anything runs (DD211).
    /// </summary>
    /// <remarks>
    /// It names what goes and what stays, in that order, because the fear this dialog has to answer
    /// is not how long it takes: it is whether a button called Compact is about to delete somebody's
    /// images. So the removal is one sentence and the list of what is left alone is the next, and
    /// both are above the question.
    ///
    /// <para>Build cache is named as build cache rather than as reclaimable space. The daemon calls
    /// it reclaimable and that word belongs to the daemon; what the reader has to decide is whether
    /// they mind rebuilding a layer, which is a thing they can answer.</para>
    ///
    /// <para><b>A machine that has already refused says so here (DD226).</b> DD224 fixed the ending
    /// and left the asking: the plan went on describing a result this Windows cannot produce, and
    /// the price of finding out was every container going down. It is still offered rather than
    /// withdrawn, because the flag is disabled and not removed, and a button that stops trying is
    /// one nobody will ever discover has started working again. So the question becomes "try
    /// anyway", which is what it honestly is.</para>
    /// </remarks>
    /// <param name="refusedBefore">Whether Windows has already refused a hand-back here (DD226).</param>
    /// <returns>The question, as the dialog asks it.</returns>
    public static string CompactConfirmation(bool refusedBefore) =>
        "Docker stops while the disk is compacted.\n\n"
        + "The build cache goes first, then the filesystem discards what it has already freed, then "
        + "the virtual disk hands those blocks back to Windows.\n\n"
        + "Images, containers and volumes are left alone. The only thing deleted is build cache, "
        + "which Docker rebuilds the next time it needs it.\n\n"
        + (refusedBefore
            ? "Windows refused the last step on this machine: it has sparse disks turned off, and "
              + "the setting that overrides it is one this tool will not use on a disk holding "
              + "your images. The earlier steps still run, and the last one is likely to be "
              + "refused again. If it is, this page then offers to do it with administrator "
              + "rights instead.\n\n"
              + "Docker starts again by itself at the end.\n\n"
              + "Stop Docker and try anyway?"
            : "Docker starts again by itself at the end.\n\n"
              + "Stop Docker and compact now?");

    /// <summary>What the elevated compaction asks before it raises a prompt (DD237).</summary>
    /// <returns>The question, as the dialog asks it.</returns>
    /// <remarks>
    /// <para><b>It names the command.</b> Asking somebody to approve administrator rights without
    /// saying what will run with them is asking them to approve the rights themselves, which is the
    /// habit that trains people into clicking through UAC. The four verbs are short enough to
    /// print, so they are printed.</para>
    ///
    /// <para><b>It says what declining costs</b>, which is nothing. The prompt is refusable and the
    /// disk is exactly as it was afterwards; a dialog that left that unsaid would make the safe
    /// answer feel like the risky one.</para>
    ///
    /// <para><b>And it names the blast radius, which DD238 widened.</b> diskpart needs the file
    /// exclusively, and only <c>wsl --shutdown</c> gives it that, so every distribution on the
    /// machine goes down and not only this one. Somebody with work open in Ubuntu is entitled to
    /// know that before the UAC prompt rather than after it.</para>
    /// </remarks>
    /// <param name="alsoRunning">
    /// Other WSL distributions that are up, which this will stop too. Empty where there are none.
    /// </param>
    /// <returns>The question, as the dialog asks it.</returns>
    public static string ElevatedConfirmation(IReadOnlyList<string>? alsoRunning = null)
    {
        var others = alsoRunning is { Count: > 0 }
            ? "This also stops other WSL distributions that are running right now: "
              + string.Join(", ", alsoRunning) + ".\n\n"
            : "";

        return "Windows has turned off the way of doing this that needs no administrator rights, "
            + "so the one route left asks for them.\n\n"
            + "Windows will show a prompt. Approving it runs diskpart, which selects the virtual "
            + "disk, attaches it read-only, compacts it and detaches it.\n\n"
            + "That reclaims what the disk is holding and the filesystem has already finished with. "
            + "It does not delete images, containers or volumes.\n\n"
            + "To let diskpart open the disk, all of WSL is shut down first, not just Docker.\n\n"
            + others
            + "Docker starts again afterwards. Declining the prompt costs nothing: the disk is "
            + "left exactly as it is now.\n\n"
            + "Ask for administrator rights?";
    }

    /// <summary>What the panel says while a compaction is running.</summary>
    public static readonly RepairPrompt Compacting = new(
        "Compacting the disk",
        "Docker is stopped, and starts again by itself when this finishes. How long it takes "
        + "depends on how much the virtual disk is holding.",
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
                CleanDetail(outcome.Findings),
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

    /// <summary>Read a compaction for what the page should now say (DD211).</summary>
    /// <param name="outcome">What the compaction did.</param>
    /// <returns>The prompt.</returns>
    /// <remarks>
    /// <para><b>It reports the two readings and not only the difference.</b> The button's claim is
    /// that a gap on this page got smaller, so the sizes it got smaller between are the evidence, and
    /// a headline naming gigabytes with nothing under it is the sentence a user has no way to check.
    /// </para>
    ///
    /// <para><b>The engine goes back on every ending here, including the failing one</b>, which is
    /// where this parts company with <see cref="Of(RepairOutcome, bool)"/>. DD190 keeps the engine
    /// down after a check that could not finish because the disk under it may be unreadable. Nothing
    /// in a compaction reads for damage: a hand-back that failed leaves a filesystem exactly as
    /// sound as it was, and keeping Docker down over a tidy-up that did not work would be this page
    /// punishing somebody for pressing a housekeeping button.</para>
    /// </remarks>
    public static RepairPrompt Of(CompactionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        var sizes = Sizes(outcome);
        if (!outcome.Succeeded)
        {
            // Windows withdrawing the mechanism is not one failure among many (DD224). A headline
            // saying the disk was not compacted invites a second press and a second interruption,
            // and there is nothing on this machine that a second press would find different.
            var withdrawn = DiskCompaction.WindowsWithdrewIt(outcome.Failure?.Detail);

            return new RepairPrompt(
                withdrawn ? "Windows has turned this off" : "The disk was not compacted",
                (outcome.Failure?.Detail ?? "nothing said where it stopped")
                + (withdrawn
                    ? " There is one route left and it needs administrator rights."
                    : "")
                + (outcome.EngineWentDown
                    ? " Nothing on the disk was changed by this, so Docker is being started again."
                    : " Nothing was stopped, so Docker is as it was."),
                OfferRepair: false,
                StartsAgain: outcome.EngineWentDown)
            {
                // The one ending that gets the offer (DD237). An ordinary failure does not: that is
                // something a retry might answer differently, and elevation is not a way past it.
                OfferElevated = withdrawn,
            };
        }

        return outcome.HandedBack is { } bytes
            ? new RepairPrompt(
                $"Windows got {MachineReport.Size(bytes)} back",
                $"The virtual disk {sizes}",
                OfferRepair: false,
                StartsAgain: true)
            : new RepairPrompt(
                "The disk was compacted and gave nothing back",
                $"There was nothing being held that the filesystem had finished with. The virtual "
                + $"disk {sizes}",
                OfferRepair: false,
                StartsAgain: true);
    }

    /// <summary>What a clean reading says, given what the tool printed with it (DD220).</summary>
    /// <param name="findings">Everything <c>e2fsck</c> wrote, or nothing.</param>
    /// <returns>The detail.</returns>
    /// <remarks>
    /// <para><b>Measured, and the reason this is two sentences rather than one.</b> An ext4 image
    /// with its superblock free counters broken was read with <c>e2fsck -fn</c> on 29 August 2026:
    /// it printed <c>Free blocks count wrong (3, counted=25798). Fix? no</c> in full and then exited
    /// zero, because those counts are recomputed rather than trusted. The page reads the exit code,
    /// which is right and which DD199 settled, and it shows the findings whatever the code said. So
    /// it drew "Nothing needed mending" directly above a transcript complaining about the disk.</para>
    ///
    /// <para><b>Neither half was wrong and the fix is not to start parsing <c>e2fsck</c>.</b> A
    /// verdict taken from a tool's prose is a second opinion that drifts from the one the CLI reads.
    /// What the headline and the transcript owe each other is only that they describe the same
    /// reading, and the reading is: the tool declined to change anything, and said something anyway.
    /// That is a third case, and saying so costs a sentence.</para>
    ///
    /// <para>The reassurance stays first, because it is still the answer. What is added is the part
    /// that stops a reader concluding the tool lost track of what it just did.</para>
    /// </remarks>
    private static string CleanDetail(string? findings) => findings is { Length: > 0 }
        ? "Nothing needed mending. What e2fsck printed is below, and it can have something to say "
          + "about a disk it decided not to change."
        : "Nothing needed mending.";

    /// <summary>The two readings the claim rests on, as a clause the detail can end on.</summary>
    /// <param name="outcome">What the compaction did.</param>
    /// <returns>The clause, ending in a full stop.</returns>
    /// <remarks>
    /// What the volume was charging for, and not the file's length (DD225): the hand-back makes the
    /// disk sparse and a sparse file keeps its length, so quoting the length as evidence would be
    /// quoting the one number a successful run does not move. The length is the fallback where
    /// Windows would not answer, which on an ordinary file is the same figure anyway.
    /// </remarks>
    private static string Sizes(CompactionOutcome outcome) =>
        (outcome.Before?.OnDisk ?? outcome.Before?.VirtualDisk,
            outcome.After?.OnDisk ?? outcome.After?.VirtualDisk) switch
        {
            ({ } before, { } after) =>
                $"was costing {MachineReport.Size(before)} and is now costing "
                + $"{MachineReport.Size(after)}.",
            ({ } before, null) => $"was costing {MachineReport.Size(before)} before this ran.",
            _ => "could not be measured, so there is no figure to compare.",
        };

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
