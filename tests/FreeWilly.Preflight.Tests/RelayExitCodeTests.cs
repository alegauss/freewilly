using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// The exit code a real client reports, through this working tree's relay, against a live daemon
/// (DD260).
/// </summary>
/// <remarks>
/// Every other check the relay has drives a request that carries a Content-Length and asserts what
/// came back. That is the one shape DD259 spared: the relay disconnected an upgraded connection
/// instead of closing it, so <c>compose run</c>, <c>start -a</c> and <c>exec</c> all delivered
/// their output in full and then exited 1 over a container that had exited 0. The suite was green
/// throughout, and it shipped.
///
/// <para>So the assertion here is the number the client exits with, and nothing else. Not the bytes
/// the relay forwarded, which were never wrong; not what the daemon said, which was never wrong
/// either. The whole defect lived in the last thing that happened on the connection, and the exit
/// code is the only place it was ever visible.</para>
///
/// <para>These need a live daemon and stand aside without one, which is why the unit tests keep the
/// fakes. On a machine serving the engine they create containers with names of their own and remove
/// them again; on CI they skip, saying so.</para>
/// </remarks>
public sealed class RelayExitCodeTests
{
    private static string Named() => LiveEngine.Named();

    [FactWhenTheEngineAnswers]
    public async Task An_exec_that_succeeds_is_reported_as_a_success()
    {
        // The clearest reading of the defect. An exec's output arrived whole and its exit code was
        // 1 anyway, so a test that looked at stdout would have passed while the caller saw a
        // failure — which is what both halves below are for, in that order.
        await using var engine = new LiveEngine.Served();
        var name = Named();

        var started = engine.Run(
            LiveEngine.Anywhere, "run", "-d", "--name", name, LiveEngine.Image, "sleep", "60");
        Assert.Equal(0, started.ExitCode);

        try
        {
            var exec = engine.Run(LiveEngine.Anywhere, "exec", name, "sh", "-c", "echo hi");

            Assert.Contains("hi", exec.Output, StringComparison.Ordinal);
            Assert.Equal(0, exec.ExitCode);
        }
        finally
        {
            engine.Run(LiveEngine.Anywhere, "rm", "-f", name);
        }
    }

    [FactWhenTheEngineAnswers]
    public async Task A_start_that_attaches_reports_the_success_the_container_exited_with()
    {
        // `docker run` was never affected and `docker start -a` was, over the same container and the
        // same daemon. The difference is the attach, which is the connection the client upgraded.
        await using var engine = new LiveEngine.Served();
        var name = Named();

        var created = engine.Run(
            LiveEngine.Anywhere, "create", "--name", name, LiveEngine.Image, "true");
        Assert.Equal(0, created.ExitCode);

        try
        {
            var attached = engine.Run(LiveEngine.Anywhere, "start", "-a", name);

            Assert.Equal(0, attached.ExitCode);
        }
        finally
        {
            engine.Run(LiveEngine.Anywhere, "rm", "-f", name);
        }
    }

    [FactWhenTheEngineAnswers]
    public async Task A_start_that_attaches_reports_the_failure_the_container_exited_with()
    {
        // The half that keeps the others honest. A teardown that handed every client a clean ending
        // regardless would satisfy them all and would also hide every real failure, which is a worse
        // defect than the one being fixed: the exit code has to be the container's, not the
        // connection's.
        await using var engine = new LiveEngine.Served();
        var name = Named();

        var created = engine.Run(
            LiveEngine.Anywhere, "create", "--name", name, LiveEngine.Image, "sh", "-c", "exit 3");
        Assert.Equal(0, created.ExitCode);

        try
        {
            var attached = engine.Run(LiveEngine.Anywhere, "start", "-a", name);

            Assert.Equal(3, attached.ExitCode);
        }
        finally
        {
            engine.Run(LiveEngine.Anywhere, "rm", "-f", name);
        }
    }

    [FactWhenTheEngineAnswers]
    public async Task A_compose_run_on_a_service_that_succeeds_is_reported_as_a_success()
    {
        // The client somebody actually typed when this was reported. Its own project name, so a run
        // here cannot join or tear down a project on the machine it is running on.
        await using var engine = new LiveEngine.Served();
        var project = Named();
        var scratch = Directory.CreateTempSubdirectory("freewilly-dd260-compose");

        try
        {
            File.WriteAllText(
                Path.Combine(scratch.FullName, "compose.yaml"),
                $"services:\n  probe:\n    image: {LiveEngine.Image}\n    command: [\"true\"]\n");

            // -T for the same reason the report had one: with stdio redirected there is no terminal
            // to allocate, and `compose run -T` returned 1 over a service that succeeded too.
            var ran = engine.Run(
                scratch.FullName, "compose", "-p", project, "run", "--rm", "-T", "probe");

            Assert.Equal(0, ran.ExitCode);
        }
        finally
        {
            engine.Run(scratch.FullName, "compose", "-p", project, "down", "--remove-orphans");
            scratch.Delete(recursive: true);
        }
    }

    [Fact]
    public void The_skip_is_conditional_and_says_what_to_do_about_it()
    {
        // The half a skip cannot show about itself, held the way ProductSlotTests holds its own: a
        // gate wrong in one direction hides these tests forever, which is the state DD260 exists to
        // end, and neither direction is visible in a result that reads "skipped".
        var reason = new FactWhenTheEngineAnswersAttribute().Skip;

        Assert.Equal(LiveEngine.Absent, reason);

        if (reason is not null)
        {
            Assert.Contains("re-run", reason, StringComparison.Ordinal);
        }
    }
}
