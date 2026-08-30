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
    public void A_clean_verdict_over_a_transcript_full_of_complaints_says_so()
    {
        // DD220, and it was measured rather than imagined. An ext4 image with its superblock free
        // counters broken printed `Free blocks count wrong (3, counted=25798). Fix? no` in full and
        // exited zero, because those counts are recomputed rather than trusted. The panel drew
        // "Nothing needed mending" directly above that, which reads as the tool having lost track of
        // what it just did.
        var noisy = Outcome(
            ok: true,
            clean: true,
            findings: "Free blocks count wrong (3, counted=25798).\nFix? no\n");

        var prompt = RepairPrompt.Of(noisy, wrote: false);

        // The reassurance stays first, because it is still the answer the exit code gave.
        Assert.StartsWith("Nothing needed mending.", prompt.Detail, StringComparison.Ordinal);
        Assert.Contains("decided not to change", prompt.Detail, StringComparison.Ordinal);

        // And a run that printed nothing keeps the short sentence: there is no transcript to
        // prepare anybody for, and the longer one would be answering a question nobody asked.
        Assert.Equal(
            "Nothing needed mending.",
            RepairPrompt.Of(Outcome(ok: true, clean: true), wrote: false).Detail);
    }

    [Fact]
    public void The_verdict_is_still_the_exit_code_and_never_the_prose()
    {
        // What DD220 must not have changed. Reading e2fsck's text for a verdict would be a second
        // opinion on the same run the CLI reads an exit code for, and the two would drift — which
        // for this pair means one surface offering to write to the filesystem holding every image
        // on the machine while the other says there is nothing to mend.
        var noisy = Outcome(ok: true, clean: true, findings: "Free blocks count wrong. Fix? no");

        var prompt = RepairPrompt.Of(noisy, wrote: false);

        Assert.Contains("clean", prompt.Headline, StringComparison.Ordinal);
        Assert.False(prompt.OfferRepair);
        Assert.True(prompt.StartsAgain);
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
        foreach (var ready in new[] { true, false })
        {
            var asked = RepairPrompt.CheckConfirmation(ready);

            Assert.Contains("Docker stops", asked, StringComparison.Ordinal);
            Assert.Contains("unavailable", asked, StringComparison.Ordinal);
            Assert.Contains("starts again by itself", asked, StringComparison.Ordinal);

            // And none of the machinery a reader has no reason to know about.
            foreach (var jargon in new[] { "unmount", "root", "distribution", "e2fsck" })
            {
                Assert.DoesNotContain(jargon, asked, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void The_dialog_stops_blaming_the_disk_for_a_wait_the_fetch_is_causing()
    {
        // DD216. Warm, the whole sequence is 8.3 seconds and the disk really is the cost. On the
        // first check of a machine the rescue downloads its tools first, and that is the one run
        // where the reader has no prior experience to correct a wrong number with — so the sentence
        // follows the machine rather than being one claim that is true half the time.
        var warm = RepairPrompt.CheckConfirmation(toolsAreReady: true);
        var cold = RepairPrompt.CheckConfirmation(toolsAreReady: false);

        Assert.Contains("depends on the size of the disk", warm, StringComparison.Ordinal);
        Assert.DoesNotContain("depends on the size of the disk", cold, StringComparison.Ordinal);
        Assert.Contains("downloads the tools", cold, StringComparison.Ordinal);

        // The panel says the same thing for the whole wait, which is the sentence somebody actually
        // sits and reads. A dialog that was honest and a panel that was not would be worse than
        // neither, because the panel is the one still on screen when the wait gets long.
        Assert.Contains(
            "depends on the size of the disk",
            RepairPrompt.WorkingOn(toolsAreReady: true).Detail,
            StringComparison.Ordinal);
        Assert.Contains(
            "being downloaded",
            RepairPrompt.WorkingOn(toolsAreReady: false).Detail,
            StringComparison.Ordinal);

        // And the headline does not move with it. The driving verb waits on that sentence (DD214),
        // so one that changed with the machine would make the wait a guess.
        Assert.Equal(
            RepairPrompt.WorkingOn(true).Headline, RepairPrompt.WorkingOn(false).Headline);
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
        //
        // Through AskedStop since DD213, which announces to the host and the tray together. The
        // ordering argument is unchanged and now covers both listeners.
        var work = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Cli/FilesystemWork.cs"));

        var told = work.IndexOf("AskedStop.Announce()", StringComparison.Ordinal);
        var stopped = work.IndexOf("StopAsync(", StringComparison.Ordinal);

        Assert.True(told >= 0, "the check tears the engine down without telling the host, so it "
            + "will be put back under the disk being read");
        Assert.True(
            told < stopped,
            "the host is told after the teardown has begun, which is the window the revival fits in");
    }

    [Fact]
    public void Every_stop_this_tool_asks_for_is_announced_to_the_tray_as_well_as_the_host()
    {
        // DD213. The two signals are always sent together and always before the teardown, and the
        // way that stops being true is a third caller copying one of them. Source-asserted for the
        // reason DD207's neighbour is: the mistake only happens on a machine with a tray up, and
        // what it produces there is a balloon eight seconds long.
        var root = RepositoryRoot();
        var announcement = File.ReadAllText(
            Path.Combine(root, "src/FreeWilly.Tray/Cli/AskedStop.cs"));

        Assert.Contains(
            "SingleEngine.TellTheLiveOneToStop()", announcement, StringComparison.Ordinal);
        Assert.Contains(
            "SingleTray.AskTheLiveOneToExpectAStop()", announcement, StringComparison.Ordinal);

        // And nothing else reaches past it for one half of the pair. The tray's own in-process path
        // is the exception the window has and a verb does not.
        foreach (var verb in new[]
        {
            "src/FreeWilly.Tray/Cli/FilesystemWork.cs",
            "src/FreeWilly.Tray/Cli/EngineCommand.cs",
        })
        {
            var source = File.ReadAllText(Path.Combine(root, verb));

            Assert.Contains("AskedStop.Announce()", source, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "SingleEngine.TellTheLiveOneToStop()", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_verb_states_the_same_third_case_the_window_does()
    {
        // DD220 on the other surface. The words differ because the positions do — the console has
        // the transcript above and the panel has it below — but the case is one case, and a verb
        // that only printed "Nothing to mend" under a page of complaints would be the same defect
        // wearing a different font. Source-asserted for the reason DD204's neighbour is: what is
        // guarded against is one surface being fixed and the other left.
        var verb = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Cli/EngineCommand.cs"));

        Assert.Contains(
            "Clean: true, Findings: { Length: > 0 }", verb, StringComparison.Ordinal);
        Assert.Contains("decided not to change", verb, StringComparison.Ordinal);
    }

    [Fact]
    public void A_verb_leaves_the_engine_where_the_caller_asked_and_says_which_command_starts_it()
    {
        // The half DD213 settles rather than implements. The window starts the engine back because
        // it interrupted somebody who had not asked for an interruption; a command is a scripting
        // surface, and a verb that quietly brought the engine back would overrule the next line of
        // the script. So the verb ends by naming the command instead (DD205).
        var verb = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Cli/EngineCommand.cs"));

        Assert.Contains("--run` starts the engine again", verb, StringComparison.Ordinal);
        Assert.DoesNotContain("_interlude", verb, StringComparison.Ordinal);
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
    public void The_administrator_button_is_collapsed_in_the_markup_and_shown_only_by_the_prompt()
    {
        // DD237, asserted in the markup because the window driver cannot reach it: that verb
        // attaches to whatever FreeWilly is already running, which on a developer's machine is the
        // installed build rather than the one just compiled.
        //
        // What must never regress is the starting state. A button asking for administrator rights
        // that is visible on a freshly opened page is a tool asking for them because it can, and
        // the page has to have been refused the other route before it may offer this one.
        var markup = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Ui/Pages/EnginePage.xaml"));
        var page = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Ui/Pages/EnginePage.xaml.cs"));

        var button = markup.IndexOf("x:Name=\"Elevate\"", StringComparison.Ordinal);
        Assert.True(button > 0, "the page carries no administrator button");

        var declared = markup.IndexOf("/>", button, StringComparison.Ordinal);
        Assert.Contains(
            "Visibility=\"Collapsed\"", markup[button..declared], StringComparison.Ordinal);

        // And it is the prompt that decides, not the page: a window working out for itself when to
        // raise a UAC prompt would be a second opinion about the same refusal.
        Assert.Contains("prompt.OfferElevated", page, StringComparison.Ordinal);

        // Words rather than a colour emoji, which is what the reference does and what every other
        // button on this page is.
        var content = markup.IndexOf("Content=\"Compact as administrator\"", StringComparison.Ordinal);
        Assert.True(content > 0, "the administrator button was relabelled");
    }

    [Fact]
    public void Both_runs_draw_their_steps_while_they_are_still_running()
    {
        // DD239, reported on the first successful elevated compaction: diskpart took two and a half
        // minutes and the page said "Compacting the disk" for all of it. The run worked; the person
        // watching had no way to tell that from a hang.
        //
        // Asserted as a placement, because that is what the defect was. The steps already arrived on
        // time and the page already had them: what it did was hold them until the run returned.
        var page = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Ui/Pages/EnginePage.xaml.cs"));

        Assert.Contains(
            "private DispatcherTimer DrawAsTheyLand(", page, StringComparison.Ordinal);

        foreach (var run in new[] { "internal async Task Compacting(", "internal async Task Run(" })
        {
            var at = page.IndexOf(run, StringComparison.Ordinal);
            Assert.True(at > 0, $"{run} is gone, so this guard reads nothing");

            var body = page[at..];
            var started = body.IndexOf("DrawAsTheyLand(", StringComparison.Ordinal);
            var awaited = body.IndexOf("await Task.Run(", StringComparison.Ordinal);
            var stopped = body.IndexOf("ticker.Stop();", StringComparison.Ordinal);

            Assert.True(started >= 0, $"{run} does not draw its steps as they land");
            Assert.True(
                started < awaited,
                $"{run} starts drawing after the work it is drawing, so the first steps are missed");
            Assert.True(
                stopped > awaited,
                $"{run} never stops the ticker, so it redraws a working panel over the outcome");
        }

        // In a finally on both, because the thrown ending is the one where a ticker left running
        // would paint over the exception the reader needs.
        Assert.Equal(
            2,
            page.Split("            ticker.Stop();", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Nothing_marshals_a_step_off_the_thread_the_run_is_on()
    {
        // The objection DD239 had to answer rather than ignore, and it was written down where the
        // old behaviour was: a page that marshalled each step would be doing dispatcher work in the
        // middle of a minutes-long e2fsck. So the queue is read by the UI thread on a timer, and
        // the run goes on enqueueing exactly as it did.
        var page = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Ui/Pages/EnginePage.xaml.cs"));

        var drawing = page.IndexOf(
            "private DispatcherTimer DrawAsTheyLand(", StringComparison.Ordinal);
        var body = page[drawing..];

        Assert.Contains("ticker.Tick +=", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Dispatcher.Invoke", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Dispatcher.BeginInvoke", page, StringComparison.Ordinal);

        // And a tick that found nothing new draws nothing, so a four-minute step costs a comparison
        // every half second and no layout. Asserted on the comparison rather than on the whole
        // condition, which grew a second half when DD243 gave a long step something to say.
        Assert.Contains("landed == drawn", body, StringComparison.Ordinal);
        Assert.Contains("said == shown", body, StringComparison.Ordinal);
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

    [Fact]
    public void The_compaction_plan_says_what_goes_and_what_stays_before_it_asks()
    {
        // DD211. The fear a button called Compact has to answer is not how long it takes: it is
        // whether somebody's images are about to be deleted. So the removal and the list of what is
        // left alone are both above the question.
        foreach (var refused in new[] { true, false })
        {
            var plan = RepairPrompt.CompactConfirmation(refused);

            Assert.Contains("build cache", plan, StringComparison.Ordinal);
            Assert.Contains(
                "Images, containers and volumes are left alone", plan, StringComparison.Ordinal);
            Assert.Contains("Docker stops", plan, StringComparison.Ordinal);
            Assert.Contains("starts again by itself", plan, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_machine_that_has_already_refused_says_so_before_it_costs_a_second_interruption()
    {
        // DD226. DD224 fixed the ending and left the asking: the plan went on describing a result
        // this Windows cannot produce, somebody agreed, every container went down, and only then
        // were they told. The refusal is a fact about the machine, so the machine's plan carries it.
        var first = RepairPrompt.CompactConfirmation(refusedBefore: false);
        var again = RepairPrompt.CompactConfirmation(refusedBefore: true);

        Assert.Contains("Stop Docker and compact now?", first, StringComparison.Ordinal);
        Assert.DoesNotContain("refused", first, StringComparison.Ordinal);

        Assert.Contains("Windows refused the last step", again, StringComparison.Ordinal);
        Assert.Contains("likely to be refused again", again, StringComparison.Ordinal);

        // Offered rather than withdrawn: the flag is disabled and not removed, and a button that
        // stopped trying is one nobody would ever discover had started working again. So the
        // question becomes what it honestly is.
        Assert.Contains("try anyway?", again, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_compaction_that_gave_bytes_back_names_both_readings_and_not_only_the_difference()
    {
        // The claim is that a gap three rows up this page got smaller, so the sizes it got smaller
        // between are the evidence. A headline naming gigabytes with nothing under it is a sentence
        // the reader has no way to check.
        var outcome = new CompactionOutcome(
            [new RepairStep(DiskCompaction.HandBackStep, true, "freewilly is sparse")])
        {
            Before = new DiskSizes(50L * 1024 * 1024 * 1024, null),
            After = new DiskSizes(30L * 1024 * 1024 * 1024, null),
        };

        var prompt = RepairPrompt.Of(outcome);

        Assert.Contains("20 GB", prompt.Headline, StringComparison.Ordinal);
        Assert.Contains("50 GB", prompt.Detail, StringComparison.Ordinal);
        Assert.Contains("30 GB", prompt.Detail, StringComparison.Ordinal);
        Assert.True(prompt.StartsAgain);
    }

    [Fact]
    public void A_compaction_that_freed_nothing_says_so_rather_than_claiming_a_reclaim()
    {
        // The ordinary answer on a machine that was already tidy, and the one a button like this is
        // most tempted to dress up.
        var outcome = new CompactionOutcome(
            [new RepairStep(DiskCompaction.HandBackStep, true, "freewilly is sparse")])
        {
            Before = new DiskSizes(30L * 1024 * 1024 * 1024, null),
            After = new DiskSizes(30L * 1024 * 1024 * 1024, null),
        };

        var prompt = RepairPrompt.Of(outcome);

        Assert.Contains("gave nothing back", prompt.Headline, StringComparison.Ordinal);
        Assert.True(prompt.StartsAgain);
    }

    [Fact]
    public void Windows_withdrawing_the_mechanism_is_said_as_that_and_not_as_a_failed_run()
    {
        // DD224, from the refusal DD221's rehearsal met on its first run. Sparse VHD support is
        // disabled over possible data corruption and the only way past it is a flag DD211 declined
        // to pass, so a second press finds nothing different — and "The disk was not compacted" is
        // an invitation to make one, at the cost of a second interruption.
        var refused = new CompactionOutcome(
        [
            new RepairStep(FilesystemRepair.StopStep, true, "told the host to stop"),
            new RepairStep(
                DiskCompaction.HandBackStep,
                false,
                "Windows has turned off the only way of handing these blocks back that needs no "
                + $"administrator rights, and offers {DiskCompaction.UnsafeFlag} instead."),
        ]);

        var prompt = RepairPrompt.Of(refused);

        Assert.Equal("Windows has turned this off", prompt.Headline);
        Assert.True(prompt.StartsAgain, "the engine went down for this and was left there");

        // And any other refusal keeps the sentence that invites a retry, because a retry is what
        // those are for.
        var ordinary = new CompactionOutcome(
        [
            new RepairStep(FilesystemRepair.StopStep, true, "told the host to stop"),
            new RepairStep(DiskCompaction.HandBackStep, false, "the disk is still in use"),
        ]);

        Assert.Equal("The disk was not compacted", RepairPrompt.Of(ordinary).Headline);
    }

    [Fact]
    public void Only_the_withdrawn_hand_back_is_offered_administrator_rights()
    {
        // DD237. The offer exists because that one ending has no unelevated route left, and it must
        // not spread: an ordinary failure is something a retry might answer differently, and a tool
        // that reached for administrator rights whenever something went wrong would be teaching the
        // habit this whole design avoids.
        var withdrawn = new CompactionOutcome(
        [
            new RepairStep(FilesystemRepair.StopStep, true, "told the host to stop"),
            new RepairStep(
                DiskCompaction.HandBackStep,
                false,
                "Windows has turned off the only way of handing these blocks back that needs no "
                + $"administrator rights, and offers {DiskCompaction.UnsafeFlag} instead."),
        ]);

        var offered = RepairPrompt.Of(withdrawn);
        Assert.True(offered.OfferElevated);
        Assert.Contains("administrator rights", offered.Detail, StringComparison.Ordinal);

        var ordinary = new CompactionOutcome(
        [
            new RepairStep(FilesystemRepair.StopStep, true, "told the host to stop"),
            new RepairStep(DiskCompaction.HandBackStep, false, "the disk is still in use"),
        ]);
        Assert.False(RepairPrompt.Of(ordinary).OfferElevated);

        // Nor on the ending where it worked, which has nothing left to offer.
        var worked = new CompactionOutcome(
            [new RepairStep(DiskCompaction.HandBackStep, true, "freewilly is sparse")]);
        Assert.False(RepairPrompt.Of(worked).OfferElevated);
    }

    [Fact]
    public void The_elevated_plan_names_the_command_and_says_declining_costs_nothing()
    {
        // DD237. Approving administrator rights without being told what runs with them is approving
        // the rights themselves, which is the habit that trains people into clicking through UAC.
        // And a dialog that left the cost of "No" unsaid would make the safe answer feel risky.
        var plan = RepairPrompt.ElevatedConfirmation();

        Assert.Contains("diskpart", plan, StringComparison.Ordinal);
        Assert.Contains("read-only", plan, StringComparison.Ordinal);
        Assert.Contains(
            "does not delete images, containers or volumes", plan, StringComparison.Ordinal);
        Assert.Contains("Declining the prompt costs nothing", plan, StringComparison.Ordinal);

        // The unsafe flag is what the other route refuses, and no dialog here offers it as a way on.
        Assert.DoesNotContain(DiskCompaction.UnsafeFlag, plan, StringComparison.Ordinal);
    }

    [Fact]
    public void The_elevated_plan_says_all_of_WSL_stops_and_names_who_else_that_is()
    {
        // DD238. diskpart needs the disk exclusively and only `wsl --shutdown` gives it that, so
        // this button reaches past the engine and stops work that has nothing to do with Docker.
        // A cost of that size discovered afterwards is one nobody agreed to.
        var alone = RepairPrompt.ElevatedConfirmation();
        Assert.Contains("all of WSL is shut down first, not just Docker", alone,
            StringComparison.Ordinal);

        // Named, not counted: "two other distributions" is not something a reader can act on, and
        // deciding whether to lose what is open in Ubuntu means being told it is Ubuntu.
        var busy = RepairPrompt.ElevatedConfirmation(["Ubuntu", "Debian"]);
        Assert.Contains("Ubuntu, Debian", busy, StringComparison.Ordinal);

        // And silent where there is nothing to warn about, rather than a sentence about an empty
        // list that every reader has to parse before discarding.
        Assert.DoesNotContain("also stops other WSL distributions", alone, StringComparison.Ordinal);
    }

    [Fact]
    public void The_plan_for_a_machine_that_was_refused_points_at_the_route_that_is_left()
    {
        // DD226 told a refused machine it was likely to be refused again and stopped there, which
        // was true when there was nothing after it. DD237 put something after it, so leaving that
        // sentence alone would have made the plan the one thing on this page that was out of date.
        var again = RepairPrompt.CompactConfirmation(refusedBefore: true);

        Assert.Contains("administrator rights", again, StringComparison.Ordinal);
    }

    [Fact]
    public void The_refusal_is_recognised_by_the_flag_and_never_by_the_prose()
    {
        // Measured in Portuguese on the machine this was written on: WSL translates its
        // explanation and does not translate the flag it points at. A check on the words would
        // work on one desk and report every other machine as having an ordinary failure.
        Assert.True(DiskCompaction.WindowsWithdrewIt(
            "O suporte ao VHD esparso está desabilitado no momento devido à possível corrupção de "
            + "dados. Para forçar uma distribuição a usar um VHD esparso, execute: wsl.exe --manage "
            + "<DistributionName> --set-sparse --allow-unsafe"));

        Assert.False(DiskCompaction.WindowsWithdrewIt("the disk is still in use"));
        Assert.False(DiskCompaction.WindowsWithdrewIt(null));
    }

    [Fact]
    public void A_compaction_that_failed_still_starts_the_engine_it_took_down()
    {
        // Where this parts company with the check, and deliberately. DD190 keeps the engine down
        // after a check that could not finish, because the disk under it may be unreadable. Nothing
        // in a compaction reads for damage: a hand-back that failed leaves a filesystem exactly as
        // sound as it was, and keeping Docker down over it would punish somebody for tidying up.
        var outcome = new CompactionOutcome(
        [
            new RepairStep(FilesystemRepair.StopStep, true, "told the host to stop"),
            new RepairStep(FilesystemRepair.TerminateStep, false, "stopped here"),
        ]);

        var prompt = RepairPrompt.Of(outcome);

        Assert.False(outcome.Succeeded);
        Assert.True(prompt.StartsAgain);
        Assert.Contains("stopped here", prompt.Detail, StringComparison.Ordinal);

        // And a run that never got that far says the engine is where they left it.
        var early = new CompactionOutcome(
            [new RepairStep("find the distribution", false, "nothing is registered")]);

        Assert.False(RepairPrompt.Of(early).StartsAgain);
        Assert.Contains(
            "Docker is as it was", RepairPrompt.Of(early).Detail, StringComparison.Ordinal);
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
