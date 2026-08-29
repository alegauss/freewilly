using FreeWilly.Core.Engine;
using FreeWilly.Core.Fixtures;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// What the window says about a check, and when it offers to write (DD199).
/// </summary>
/// <remarks>
/// The page renders this and decides none of it, so this is where the decisions are tested. The
/// button itself cannot be: pressing it terminates a distribution.
/// </remarks>
public sealed class RepairPromptTests
{
    private static RepairOutcome Outcome(bool ok, bool clean, string? findings = null) =>
        new([new RepairStep("check", ok, ok ? "read it" : "stopped here")])
        {
            Clean = clean,
            Findings = findings,
        };

    [Fact]
    public void A_clean_filesystem_is_not_offered_a_repair()
    {
        // The common answer, and offering to write to a healthy disk is how a button that should
        // reassure somebody talks them into a repair instead.
        var prompt = RepairPrompt.Of(Outcome(ok: true, clean: true), wrote: false);

        Assert.False(prompt.OfferRepair);
        Assert.Contains("clean", prompt.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dirty_filesystem_offers_one_and_says_to_read_the_findings_first()
    {
        // The design's rule: the user sees what the check found before being asked to approve
        // anything, so the sentence that offers the repair is also the one that points at the
        // evidence.
        var prompt = RepairPrompt.Of(Outcome(ok: true, clean: false), wrote: false);

        Assert.True(prompt.OfferRepair);
        Assert.Contains("before approving", prompt.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_that_did_not_finish_is_never_offered_a_repair()
    {
        // Not the same as a dirty filesystem. Running a write against a disk this could not finish
        // reading is the one thing worse than leaving it alone, and the failed step is what the page
        // shows instead.
        foreach (var wrote in new[] { false, true })
        {
            var prompt = RepairPrompt.Of(Outcome(ok: false, clean: false), wrote);

            Assert.False(prompt.OfferRepair);
            Assert.Contains("did not finish", prompt.Headline, StringComparison.Ordinal);
            Assert.Contains("stopped here", prompt.Detail, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_repair_that_worked_does_not_offer_itself_again()
    {
        var prompt = RepairPrompt.Of(Outcome(ok: true, clean: false), wrote: true);

        Assert.False(prompt.OfferRepair);
        Assert.Contains("repaired", prompt.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public void The_cost_of_checking_is_stated_before_anything_is_pressed()
    {
        // Docker goes away for it, and that is not something to discover from a button that looked
        // like it only read something. Said in those words since DD210: what the machine does is
        // unmount a root, and what it costs the reader is Docker.
        Assert.Contains("Docker stops", RepairPrompt.Idle.Detail, StringComparison.Ordinal);
        Assert.False(RepairPrompt.Idle.OfferRepair);
    }

    [Fact]
    public void The_confirmation_names_what_is_written_to_rather_than_asking_if_you_are_sure()
    {
        // `e2fsck -fy` answers yes to questions that discard a damaged inode, and the filesystem it
        // answers them about holds every image and volume on the machine. That is the thing being
        // consented to, so that is what the dialog says.
        Assert.Contains("every image", RepairPrompt.Confirmation, StringComparison.Ordinal);
        Assert.Contains("discard", RepairPrompt.Confirmation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_that_finished_starts_the_engine_it_left_down()
    {
        // DD205, and automatic since DD210. Checking needs the root unmounted, so this page is the
        // one that stopped the engine, and it was never asked to leave it stopped. All three endings
        // start it: clean, repaired, and errors somebody has not decided about yet.
        Assert.True(RepairPrompt.Of(Outcome(ok: true, clean: true), wrote: false).StartsAgain);
        Assert.True(RepairPrompt.Of(Outcome(ok: true, clean: false), wrote: true).StartsAgain);
        Assert.True(RepairPrompt.Of(Outcome(ok: true, clean: false), wrote: false).StartsAgain);
    }

    [Fact]
    public void A_run_that_did_not_finish_never_starts_the_engine()
    {
        // The one exclusion the design names, and DD210 does not reopen it. An engine started on a
        // filesystem the check could not finish reading is the state DD190 was filed about.
        foreach (var wrote in new[] { false, true })
        {
            Assert.False(RepairPrompt.Of(Outcome(ok: false, clean: false), wrote).StartsAgain);
        }
    }

    [Fact]
    public void Nothing_starts_before_a_run_or_during_one()
    {
        // The engine is up before a check and going down during one, so a start here is either
        // premature or a race with the thing that is stopping it.
        Assert.False(RepairPrompt.Idle.StartsAgain);
        Assert.False(RepairPrompt.Working.StartsAgain);
    }

    [Fact]
    public void The_start_is_named_beside_what_the_run_found_and_not_instead_of_it()
    {
        // DD210. The findings are what somebody pressed the button for, and the panel that used to
        // replace them with "Starting the engine" was answering a question by discarding the answer.
        // The wait is named because a start is not instant and every other surface says the engine
        // is not answering until it lands.
        var ending = RepairPrompt.Of(Outcome(ok: true, clean: true), wrote: false);
        var starting = ending.AndStarting(TimeSpan.FromSeconds(75));

        Assert.Equal(ending.Headline, starting.Headline);
        Assert.Contains("Nothing needed mending", starting.Detail, StringComparison.Ordinal);
        Assert.Contains("75 seconds", starting.Detail, StringComparison.Ordinal);

        // And it does not ask for a second start on top of the one it just described.
        Assert.False(starting.StartsAgain);
    }

    [Fact]
    public void A_check_asks_before_it_interrupts_anything()
    {
        // DD210, and it is not the confirmation DD199 refused. That asymmetry was about the
        // filesystem: reading cannot make one worse. What is consented to here is the interruption,
        // which a check costs whatever it finds.
        //
        // In the reader's words, which is the correction the first version needed. It said the
        // distribution's root would be unmounted: true, and a sentence somebody has to translate
        // before they can weigh it. What they are agreeing to is Docker going away, so the dialog
        // says that, says what stops with it, and says it comes back.
        var asked = RepairPrompt.CheckConfirmation;

        Assert.Contains("Docker stops", asked, StringComparison.Ordinal);
        Assert.Contains("unavailable", asked, StringComparison.Ordinal);
        Assert.Contains("starts again by itself", asked, StringComparison.Ordinal);

        // And none of the machinery a reader has no reason to know about.
        foreach (var jargon in new[] { "unmount", "root", "distribution", "e2fsck" })
        {
            Assert.DoesNotContain(jargon, asked, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_failed_run_says_whether_the_engine_is_down_rather_than_leaving_it_to_be_guessed()
    {
        // DD210. Two failures that read identically otherwise: one stopped at the registered guard
        // with the engine still serving, one stopped at e2fsck with the distribution terminated.
        // Telling somebody their engine was deliberately left down while it is in fact still up is
        // how they go looking for a second fault.
        var early = new RepairOutcome(
            [new RepairStep("find the distribution", false, "stopped here")]);
        var late = new RepairOutcome(
        [
            new RepairStep(FilesystemRepair.StopStep, true, "told the host to stop"),
            new RepairStep("check", false, "stopped here"),
        ]);

        Assert.False(early.EngineWentDown);
        Assert.True(late.EngineWentDown);

        Assert.Contains(
            "Nothing was stopped",
            RepairPrompt.Of(early, wrote: false).Detail,
            StringComparison.Ordinal);
        Assert.Contains(
            "left stopped on purpose",
            RepairPrompt.Of(late, wrote: false).Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_terminate_inside_the_rescue_sequence_also_counts_as_the_engine_going_down()
    {
        // The two ways it goes down are reported by two different assemblies under two names, and a
        // run that failed between them still left it stopped.
        var terminated = new RepairOutcome(
        [
            new RepairStep(FilesystemRepair.TerminateStep, true, "freewilly terminated"),
            new RepairStep("find the disk", false, "stopped here"),
        ]);

        Assert.True(terminated.EngineWentDown);
    }

    [Fact]
    public void The_verb_and_the_window_reach_one_assembly_of_the_sequence()
    {
        // DD204. Both had built the same five steps — the registered guard, the rootfs acquire, the
        // engine stop, the construction and the call — in their own spelling, and what must not
        // differ between two surfaces is the order the engine comes down in. Source-asserted because
        // the thing being guarded against is a second copy appearing, which no behaviour shows.
        var verb = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Cli/EngineCommand.cs"));

        Assert.Contains("FilesystemWork.OnThisMachine()", verb, StringComparison.Ordinal);

        // The parts it used to assemble for itself, now reached only through that one door.
        Assert.DoesNotContain("new FilesystemRepair(", verb, StringComparison.Ordinal);
        Assert.DoesNotContain("VmHold.On", verb, StringComparison.Ordinal);
    }

    [Fact]
    public void The_check_tells_the_host_before_it_takes_the_engine_down()
    {
        // DD207, and it is asserted on the source because no fake can show it: a fake wsl answers a
        // terminate with no host behind it, and the revival that makes this dangerous only exists on
        // a machine running the product. Measured there instead — the host had the engine back nine
        // seconds into the first real run, with the root mounted read-write under e2fsck.
        var work = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Cli/FilesystemWork.cs"));

        var told = work.IndexOf("SingleEngine.TellTheLiveOneToStop()", StringComparison.Ordinal);
        var stopped = work.IndexOf("StopAsync(", StringComparison.Ordinal);

        Assert.True(told >= 0, "the check tears the engine down without telling the host, so it "
            + "will be put back under the disk being read");
        Assert.True(
            told < stopped,
            "the host is told after the teardown has begun, which is the window the revival fits in");
    }

    [Fact]
    public void The_page_says_the_stop_is_coming_before_it_starts_the_work()
    {
        // DD210, source-asserted for the reason DD207's neighbour is: the page is WPF and the run it
        // brackets terminates a distribution, so there is no instance to drive. The order is the
        // whole of it. The tray decides what an engine that stopped answering means at the moment it
        // notices, so a claim arriving after the teardown is a balloon already on its way.
        var page = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Ui/Pages/EnginePage.xaml.cs"));

        var told = page.IndexOf("_interlude.Expected()", StringComparison.Ordinal);
        var worked = page.IndexOf("Task.Run(", StringComparison.Ordinal);

        Assert.True(told >= 0, "the page takes the engine down without telling the tray, so the "
            + "stop it asked for is announced as a failure");
        Assert.True(told < worked, "the tray is told after the work has begun, which is the window "
            + "the announcement fits in");

        // And the check asks first. The repair already did; this is the one that interrupted a
        // machine full of running containers without a word.
        Assert.Contains("RepairPrompt.CheckConfirmation", page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_page_no_longer_carries_a_button_to_undo_its_own_interruption()
    {
        // DD210 removed it rather than hiding it. A Start engine button here was the page charging a
        // click for its own bookkeeping: it stopped the engine to do the check and was never asked
        // to leave it stopped. The tray menu still has one for somebody who disagrees.
        var markup = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Ui/Pages/EnginePage.xaml"));

        Assert.DoesNotContain("x:Name=\"StartEngine\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"Check\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_readings_follow_the_engine_rather_than_only_the_page_being_opened()
    {
        // DD212, and the picture that filed it: the strip said Engine running while the panel under
        // it said the distribution is not running, seconds after a check had started the engine
        // back. The readings were taken once, when the page opened, which held only while nothing
        // but the reader could change them. DD210 ended that.
        var shell = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Ui/MainWindow.xaml.cs"));

        var refresh = shell.IndexOf("internal Task RefreshAsync(", StringComparison.Ordinal);
        Assert.True(refresh >= 0, "the shell no longer has the refresh the tray calls on a crossing");

        var reread = shell.IndexOf("RereadTheMachine()", refresh, StringComparison.Ordinal);
        Assert.True(
            reread >= 0,
            "the engine state changes without the readings being re-read, so the panel goes on "
            + "describing a machine that has moved");

        // And not for a page nobody is looking at: the readings are several wsl.exe children.
        Assert.Contains(
            "Visibility.Visible", shell[refresh..(reread + 20)], StringComparison.Ordinal);
    }

    /// <summary>Where the repository is, from a test binary under bin/.</summary>
    /// <returns>The root.</returns>
    private static string RepositoryRoot()
    {
        var at = new DirectoryInfo(AppContext.BaseDirectory);
        while (at is not null && !File.Exists(Path.Combine(at.FullName, "FreeWilly.slnx")))
        {
            at = at.Parent;
        }

        Assert.True(at is not null, "the repository root was not found above the test binaries");
        return at!.FullName;
    }

    [Fact]
    public void A_captured_window_cannot_run_the_thing_the_buttons_start()
    {
        // The seam is what makes that true by construction. A fixture that answered plausibly would
        // make the button look exercised in a picture where nothing was.
        var refused = new SampleFilesystemWork().Check(_ => { });

        Assert.False(refused.Succeeded);
        Assert.Contains("fixture", refused.Failure?.Detail, StringComparison.Ordinal);
        Assert.False(RepairPrompt.Of(refused, wrote: false).OfferRepair);
    }
}
