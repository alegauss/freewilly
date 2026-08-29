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
        // Checking needs the root unmounted, so the engine and every container stop for it. That is
        // not something to discover from a button that looked like it only read something.
        Assert.Contains("the engine stops", RepairPrompt.Idle.Detail, StringComparison.Ordinal);
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
