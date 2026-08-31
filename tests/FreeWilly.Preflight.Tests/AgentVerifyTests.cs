using FreeWilly.Core.Agent;
using FreeWilly.Core.Api;
using FreeWilly.Tray.Cli;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// <c>read verify</c> through the surface: what it probes, what it refuses to probe, and the wait
/// that replaces a sleep loop (DD30).
/// </summary>
public sealed class AgentVerifyTests
{
    private static string Path(string endpoint) => $"/{DockerApi.ApiVersion}/{endpoint}";

    /// <summary>A probe that answers from a script and counts what it was asked.</summary>
    private sealed class ScriptedProbe(params bool[] accepts) : IServiceProbe
    {
        private int _attempt;

        internal int Connects { get; private set; }

        internal int Requests { get; private set; }

        internal List<int> Asked { get; } = [];

        internal string? Path { get; private set; }

        /// <summary>What a GET answers, so a test can ask about a path that is meant to be gone.</summary>
        internal int Status { get; init; } = 200;

        public PortAnswer Connect(int hostPort, string containerPort, TimeSpan timeout)
        {
            Connects++;
            Asked.Add(hostPort);

            // The last entry repeats, so a script of one value is a probe with a fixed opinion.
            var accepted = accepts.Length == 0 || accepts[Math.Min(_attempt++, accepts.Length - 1)];
            return new PortAnswer(hostPort, containerPort, accepted, 3,
                accepted ? null : "ConnectionRefused");
        }

        public RequestAnswer Get(int hostPort, string path, TimeSpan timeout)
        {
            Requests++;
            Path = path;
            Asked.Add(hostPort);
            return new RequestAnswer($"http://127.0.0.1:{hostPort}{path}", Status, 7, null);
        }
    }

    private const string RunningWithOnePort = """
        [{"Id":"aaaaaaaaaaaa0000","Names":["/shop-api-1"],"Image":"shop/api:latest","State":"running",
          "Status":"Up 4 minutes","Ports":[]}]
        """;

    private const string InspectOnePort = """
        {"Id":"aaaaaaaaaaaa0000","Name":"/shop-api-1","State":{"Status":"running"},
         "HostConfig":{"PortBindings":{"8080/tcp":[{"HostIp":"0.0.0.0","HostPort":"8080"}]}},
         "Mounts":[]}
        """;

    private const string InspectTwoPorts = """
        {"Id":"aaaaaaaaaaaa0000","Name":"/shop-api-1","State":{"Status":"running"},
         "HostConfig":{"PortBindings":{"8080/tcp":[{"HostIp":"0.0.0.0","HostPort":"8080"}],
                                       "5432/tcp":[{"HostIp":"0.0.0.0","HostPort":"5432"}]}},
         "Mounts":[]}
        """;

    private const string ExitedContainer = """
        [{"Id":"aaaaaaaaaaaa0000","Names":["/shop-api-1"],"Image":"shop/api:latest","State":"exited",
          "Status":"Exited (137) 12 seconds ago","Ports":[]}]
        """;

    private const string InspectExited = """
        {"Id":"aaaaaaaaaaaa0000","Name":"/shop-api-1","State":{"Status":"exited","ExitCode":137},
         "HostConfig":{"PortBindings":{"8080/tcp":[{"HostIp":"0.0.0.0","HostPort":"8080"}]}},
         "Mounts":[]}
        """;

    private static FakeDockerDaemon Daemon(string containers, string inspect) =>
        new FakeDockerDaemon()
            .Fails(Path("_ping"), "200 OK", "OK")
            .Json(Path("containers/json?all=1"), containers)
            .Json(Path("containers/aaaaaaaaaaaa0000/json"), inspect);

    private static int Verify(
        FakeDockerDaemon daemon, IServiceProbe probe, string[] arguments, TextWriter output)
    {
        using var api = new DockerApi(daemon.PipeName);
        return AgentSurface.ReadVerify(
            api, arguments, output, new MachineReads { Service = probe }, gap: TimeSpan.Zero);
    }

    [Fact]
    public async Task A_port_that_accepts_is_a_pass_and_an_exit_code()
    {
        await using var daemon = Daemon(RunningWithOnePort, InspectOnePort);
        var probe = new ScriptedProbe(true);
        var output = new StringWriter();

        var code = Verify(daemon, probe, ["shop-api-1"], output);

        Assert.Equal(0, code);
        Assert.Equal(1, probe.Connects);
        Assert.Equal([8080], probe.Asked);
        Assert.Contains("It answers.", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_port_that_refuses_fails_and_names_the_bind()
    {
        await using var daemon = Daemon(RunningWithOnePort, InspectOnePort);
        var output = new StringWriter();

        var code = Verify(daemon, new ScriptedProbe(false), ["shop-api-1"], output);

        Assert.Equal(1, code);
        Assert.Contains("127.0.0.1 rather than 0.0.0.0", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_is_probed_for_a_container_that_is_not_running()
    {
        await using var daemon = Daemon(ExitedContainer, InspectExited);
        var probe = new ScriptedProbe(true);
        var output = new StringWriter();

        var code = Verify(daemon, probe, ["shop-api-1"], output);

        // The port is still published, and whatever took it would have accepted. Reporting that as
        // this service is the confidently wrong answer the verb exists to prevent.
        Assert.Equal(1, code);
        Assert.Equal(0, probe.Connects);
        Assert.Contains("nothing was probed", output.ToString(), StringComparison.Ordinal);
    }

    // ---- the readiness primitive ---------------------------------------------------------------

    [Fact]
    public async Task Wait_returns_the_moment_it_answers()
    {
        await using var daemon = Daemon(RunningWithOnePort, InspectOnePort);

        // Refused, refused, then accepted: a starting service, which is the whole case for --wait.
        var probe = new ScriptedProbe(false, false, true);
        var output = new StringWriter();

        var code = Verify(daemon, probe, ["shop-api-1", "--wait", "--timeout", "30s"], output);

        Assert.Equal(0, code);
        Assert.Equal(3, probe.Connects);
        Assert.Contains("It answers.", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Waiting_without_asking_for_it_probes_once()
    {
        await using var daemon = Daemon(RunningWithOnePort, InspectOnePort);
        var probe = new ScriptedProbe(false, true);
        var output = new StringWriter();

        // No --wait, so a refusal is the answer rather than the first of several attempts.
        Assert.Equal(1, Verify(daemon, probe, ["shop-api-1"], output));
        Assert.Equal(1, probe.Connects);
    }

    [Fact]
    public async Task A_wait_that_runs_out_says_which_part_did_not_pass()
    {
        await using var daemon = Daemon(RunningWithOnePort, InspectOnePort);
        var probe = new ScriptedProbe(false);
        var output = new StringWriter();

        // Zero seconds: the deadline has passed by the first check, so this is one attempt and a
        // timeout rather than a test that waits.
        var code = Verify(daemon, probe, ["shop-api-1", "--wait", "--timeout", "0"], output);

        Assert.Equal(1, code);

        // "fails saying which part did not" - a sleep loop written by the caller ends knowing only
        // that time ran out.
        Assert.Contains("Still not answering after 0s: port", output.ToString(), StringComparison.Ordinal);
    }

    // ---- the request ---------------------------------------------------------------------------

    [Fact]
    public async Task One_published_port_needs_no_port_in_the_request()
    {
        await using var daemon = Daemon(RunningWithOnePort, InspectOnePort);
        var probe = new ScriptedProbe(true);

        var code = Verify(daemon, probe, ["shop-api-1", "--request", "/healthz"], new StringWriter());

        Assert.Equal(0, code);
        Assert.Equal(1, probe.Requests);
        Assert.Equal("/healthz", probe.Path);
    }

    [Fact]
    public async Task Several_published_ports_are_named_rather_than_guessed()
    {
        await using var daemon = Daemon(RunningWithOnePort, InspectTwoPorts);
        var probe = new ScriptedProbe(true);
        var output = new StringWriter();

        var code = Verify(daemon, probe, ["shop-api-1", "--request", "/healthz"], output);

        // Picking the lowest would answer confidently about the wrong service, which is the failure
        // this verb exists to remove.
        Assert.Equal(2, code);
        Assert.Equal(0, probe.Requests);
    }

    [Fact]
    public async Task A_request_can_name_its_own_port()
    {
        await using var daemon = Daemon(RunningWithOnePort, InspectTwoPorts);
        var probe = new ScriptedProbe(true);

        var code = Verify(
            daemon, probe, ["shop-api-1", "--request", ":5432/healthz"], new StringWriter());

        Assert.Equal(0, code);
        Assert.Equal("/healthz", probe.Path);
        Assert.Contains(5432, probe.Asked);
    }

    // ---- what a refusal says, and not only that it refused (DD252) -------------------------------

    private static readonly IReadOnlyList<(int Host, string Container)> OnePort = [(8080, "8080/tcp")];

    private static string Why(string request, IReadOnlyList<(int Host, string Container)>? published = null)
    {
        Assert.False(AgentSurface.TryTargetRequest(
            request, published ?? OnePort, out _, out _, out var why));
        return why;
    }

    [Fact]
    public void A_value_that_is_neither_form_is_told_what_a_path_looks_like()
    {
        // It used to answer "healthz is not a path: it begins with a slash", which states the false
        // half as a fact about what was typed and sends the reader back to try it again.
        var why = Why("healthz");

        Assert.Contains("a path begins with a slash", why, StringComparison.Ordinal);
        Assert.DoesNotContain("it begins with a slash", why, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("healthz")]
    [InlineData(":x/healthz")]
    [InlineData(":8080")]
    public void Every_request_refusal_says_what_a_correct_value_looks_like(string request)
    {
        // The shape, asserted over all of them: the value that was typed, and an example that would
        // have been accepted. A refusal describing only the wrong value is the defect DD252 fixed.
        var why = Why(request);

        Assert.Contains(request, why, StringComparison.Ordinal);
        Assert.Contains("/healthz", why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_container_publishing_nothing_says_so_rather_than_naming_a_path()
    {
        Assert.Contains(
            "publishes no port", Why("/healthz", []), StringComparison.Ordinal);
    }

    [Fact]
    public void Several_ports_are_refused_with_the_call_that_would_have_worked()
    {
        // Picking the lowest would answer confidently about the wrong service, so the refusal hands
        // back the one thing that saves the round trip: the argument to type instead.
        Assert.Contains(
            "--request :5432/healthz",
            Why("/healthz", [(5432, "5432/tcp"), (8080, "8080/tcp")]),
            StringComparison.Ordinal);
    }

    // ---- the status the caller named (DD250) ----------------------------------------------------

    [Fact]
    public async Task A_path_that_had_to_be_gone_exits_zero_when_it_is()
    {
        await using var daemon = Daemon(RunningWithOnePort, InspectOnePort);
        var probe = new ScriptedProbe(true) { Status = 404 };
        var output = new StringWriter();

        var code = Verify(
            daemon, probe, ["shop-api-1", "--request", "/retired", "--expect", "404"], output);

        // The whole of DD250: proving a removal used to be the run that printed red.
        Assert.Equal(0, code);
        Assert.Contains("[ok  ]", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_path_that_still_answers_fails_the_expectation_and_says_what_it_returned()
    {
        await using var daemon = Daemon(RunningWithOnePort, InspectOnePort);
        var probe = new ScriptedProbe(true) { Status = 200 };
        var output = new StringWriter();

        var code = Verify(
            daemon, probe, ["shop-api-1", "--request", "/retired", "--expect", "404"], output);

        Assert.Equal(1, code);
        Assert.Contains("200", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_expectation_with_no_request_is_refused_before_anything_is_probed()
    {
        await using var daemon = Daemon(RunningWithOnePort, InspectOnePort);
        var probe = new ScriptedProbe(true);

        Assert.Equal(2, Verify(daemon, probe, ["shop-api-1", "--expect", "404"], new StringWriter()));
        Assert.Equal(0, probe.Connects);
        Assert.Empty(daemon.Requested);
    }

    [Fact]
    public async Task A_status_that_is_not_one_is_refused_before_anything_is_probed()
    {
        await using var daemon = Daemon(RunningWithOnePort, InspectOnePort);
        var probe = new ScriptedProbe(true);

        Assert.Equal(
            2,
            Verify(daemon, probe, ["shop-api-1", "--request", "/x", "--expect", "40"], new StringWriter()));
        Assert.Equal(0, probe.Connects);
        Assert.Empty(daemon.Requested);
    }

    [Fact]
    public async Task A_timeout_that_is_not_a_number_is_refused_before_anything_is_probed()
    {
        await using var daemon = Daemon(RunningWithOnePort, InspectOnePort);
        var probe = new ScriptedProbe(true);

        Assert.Equal(2, Verify(daemon, probe, ["shop-api-1", "--timeout", "soon"], new StringWriter()));
        Assert.Equal(0, probe.Connects);
        Assert.Empty(daemon.Requested);
    }
}
