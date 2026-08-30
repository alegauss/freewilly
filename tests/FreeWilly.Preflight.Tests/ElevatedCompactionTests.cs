using FreeWilly.Core.Engine;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// The compaction that asks for administrator rights, and what it will not do to get them (DD237).
/// </summary>
/// <remarks>
/// The elevation itself is a seam, so what is tested here is everything around it: that the disk is
/// out of use before diskpart is handed it, that a declined prompt is an ending and not a fault, and
/// that the command is the one the dialog told the user it would run.
/// </remarks>
public sealed class ElevatedCompactionTests
{
    private static EnginePaths Paths() =>
        new(Path.Combine(Path.GetTempPath(), $"fw-{Guid.NewGuid():N}"));

    /// <summary>What <c>df -k /</c> answers through the state script, as one reading.</summary>
    private static string Reading(long usedKb) =>
        "device=/dev/sdd\noptions=rw,relatime\nerrors=0\nfirst=\nlast=\n"
        + $"blocks=67108864\nused={usedKb}\n";

    /// <summary>An elevation that records what it was asked and answers however it was told to.</summary>
    /// <param name="answer">What to hand back.</param>
    /// <param name="watching">
    /// The WSL calls made so far, sampled at the moment the prompt would be raised. That sample is
    /// the only way to assert an order between two different fakes.
    /// </param>
    private sealed class FakeElevation(ElevatedRun answer, FakeWsl? watching = null) : IElevated
    {
        internal string? FileName { get; private set; }

        internal string? Arguments { get; private set; }

        internal int Asked { get; private set; }

        /// <summary>What WSL had been asked by the time this was called.</summary>
        internal List<string[]> WslCallsBefore { get; } = [];

        public ElevatedRun Run(string fileName, string arguments)
        {
            FileName = fileName;
            Arguments = arguments;
            Asked++;
            if (watching is not null)
            {
                WslCallsBefore.AddRange(watching.Invocations);
            }

            return answer;
        }
    }

    /// <summary>A machine that answers the reading, the terminate and the reading after.</summary>
    private static FakeWsl Machine() =>
        new FakeWsl()
            .Answer(0, Reading(16_000_000)) // the reading before
            .Answer(0)                      // --terminate
            .Answer(0, Reading(16_000_000)); // the reading after

    private static ElevatedCompaction Compaction(FakeWsl wsl, IElevated elevated) =>
        new(
            wsl,
            Paths(),
            elevated,
            () => new RepairStep(FilesystemRepair.StopStep, true, "the host was told"));

    [Fact]
    public void Diskpart_is_never_asked_until_the_distribution_has_been_terminated()
    {
        // compact vdisk refuses a disk something has open, and WSL holds this one for as long as
        // the distribution is up. Asserted as an order rather than a presence, because the failure
        // it prevents is a diskpart that runs and reclaims nothing.
        var wsl = Machine();
        var elevated = new FakeElevation(new ElevatedRun(Ran: true, ExitCode: 0), wsl);

        Compaction(wsl, elevated).Run();

        Assert.Equal(1, elevated.Asked);
        Assert.Contains(
            elevated.WslCallsBefore, argv => argv is ["--terminate", ..]);
    }

    [Fact]
    public void A_terminate_that_fails_stops_the_run_before_any_rights_are_asked_for()
    {
        // The one ordering that matters most. A UAC prompt raised for a command that cannot work is
        // this tool spending somebody's administrator rights on nothing.
        var wsl = new FakeWsl()
            .Answer(0, Reading(16_000_000))
            .Answer(1, "", "the distribution is busy");
        var elevated = new FakeElevation(new ElevatedRun(Ran: true, ExitCode: 0));

        var outcome = Compaction(wsl, elevated).Run();

        Assert.Equal(0, elevated.Asked);
        Assert.False(outcome.Succeeded);
        Assert.Equal(ElevatedCompaction.TakeItDownStep, outcome.Failure?.What);
    }

    [Fact]
    public void A_declined_prompt_is_an_ending_and_not_a_fault()
    {
        // "Refused without consequence" is the bargain this whole task is built on, so the sentence
        // a decline produces must not read like something went wrong or invite a second attempt.
        var elevated = new FakeElevation(new ElevatedRun(Ran: false, Refused: true));

        var outcome = Compaction(Machine(), elevated).Run();

        Assert.False(outcome.Succeeded);
        Assert.Equal(ElevatedCompaction.CompactStep, outcome.Failure?.What);
        Assert.Contains("declined", outcome.Failure?.Detail!, StringComparison.Ordinal);
        Assert.Contains("nothing was changed", outcome.Failure?.Detail!, StringComparison.Ordinal);
        Assert.DoesNotContain("failed", outcome.Failure?.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_command_is_diskpart_by_full_path_and_its_log_is_kept()
    {
        // Under System32 by name rather than trusted to the PATH: what is about to run with
        // administrator rights is not a name a directory earlier in somebody's PATH answers for.
        //
        // The redirect is the other half. An elevated child's handles cannot be read from here, so
        // a failure with no log would be an exit code and no way to say which line produced it.
        var elevated = new FakeElevation(new ElevatedRun(Ran: true, ExitCode: 0));

        Compaction(Machine(), elevated).Run();

        Assert.Equal("cmd.exe", elevated.FileName);
        Assert.Contains(@"\diskpart.exe", elevated.Arguments!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("System32", elevated.Arguments!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("diskpart.log", elevated.Arguments!, StringComparison.Ordinal);
        Assert.Contains("2>&1", elevated.Arguments!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_script_is_the_four_verbs_the_dialog_promised_and_the_path_is_quoted()
    {
        // The confirmation names these four, so a script that did something else would make that
        // dialog a lie. Quoted because {localappdata} is under a profile, and a profile name with a
        // space in it is ordinary.
        var script = ElevatedCompaction.Script(@"C:\Users\Ana Paula\AppData\ext4.vhdx");

        Assert.Contains(@"select vdisk file=""C:\Users\Ana Paula\AppData\ext4.vhdx""", script,
            StringComparison.Ordinal);
        Assert.Contains("attach vdisk readonly", script, StringComparison.Ordinal);
        Assert.Contains("compact vdisk", script, StringComparison.Ordinal);
        Assert.Contains("detach vdisk", script, StringComparison.Ordinal);

        // Read-only before compact, and detach after it. diskpart runs the file top to bottom, so
        // the order in the string is the order of operations.
        var attach = script.IndexOf("attach vdisk readonly", StringComparison.Ordinal);
        var compact = script.IndexOf("compact vdisk", StringComparison.Ordinal);
        var detach = script.IndexOf("detach vdisk", StringComparison.Ordinal);
        Assert.True(attach < compact, "compact runs against a disk it cannot read");
        Assert.True(compact < detach, "the disk is detached before it has been compacted");
    }

    [Fact]
    public void A_run_that_worked_counts_as_the_compaction_having_happened()
    {
        // CompactionOutcome.Succeeded is read by the page to decide what it says, and it used to
        // know one step name. Two routes to one result, and a reader of the outcome should not have
        // to know which one this machine was able to take.
        var elevated = new FakeElevation(new ElevatedRun(Ran: true, ExitCode: 0));

        var outcome = Compaction(Machine(), elevated).Run();

        Assert.True(outcome.Succeeded);
        Assert.True(outcome.EngineWentDown);
    }

    [Fact]
    public void Diskpart_exiting_non_zero_is_a_failure_that_says_so()
    {
        var elevated = new FakeElevation(new ElevatedRun(Ran: true, ExitCode: 1));

        var outcome = Compaction(Machine(), elevated).Run();

        Assert.False(outcome.Succeeded);
        Assert.Contains("diskpart exited 1", outcome.Failure?.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_disk_is_measured_after_a_diskpart_that_refused_rather_than_assumed_unmoved()
    {
        // DD225's rule survives the second route: the figure is two readings this run took itself,
        // and "nothing moved" is a measurement rather than an inference from an exit code.
        var elevated = new FakeElevation(new ElevatedRun(Ran: true, ExitCode: 1));

        var outcome = Compaction(Machine(), elevated).Run();

        Assert.NotNull(outcome.Before);
        Assert.NotNull(outcome.After);
    }

    [Fact]
    public void Nothing_here_reaches_for_the_unsafe_flag_the_other_route_refuses()
    {
        // The elevated path exists because DD211 would not pass --allow-unsafe, so a route that
        // quietly picked it up on the way to administrator rights would undo the whole argument.
        var elevated = new FakeElevation(new ElevatedRun(Ran: true, ExitCode: 0));
        var wsl = Machine();

        Compaction(wsl, elevated).Run();

        Assert.DoesNotContain(
            wsl.Invocations,
            argv => argv.Contains(DiskCompaction.UnsafeFlag, StringComparer.Ordinal));
        Assert.DoesNotContain(
            DiskCompaction.UnsafeFlag, elevated.Arguments!, StringComparison.Ordinal);
    }
}
