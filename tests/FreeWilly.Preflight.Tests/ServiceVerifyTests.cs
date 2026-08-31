using FreeWilly.Core.Agent;
using FreeWilly.Core.Api;
using FreeWilly.Core.Preflight;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// Cheap textual proof that a service answers, because the agent cannot look (DD30).
/// </summary>
public sealed class ServiceVerifyTests
{
    private static ContainerSummary Summary(string state = "running") => new()
    {
        Id = "aaaaaaaaaaaa0000",
        Names = ["/shop-api-1"],
        Image = "shop/api:latest",
        State = state,
        Status = state == "running" ? "Up 4 minutes" : "Exited (137) 12 seconds ago",
    };

    private static ContainerInspect Inspect(
        string status = "running",
        string? health = null,
        int failingStreak = 0,
        string? healthSaid = null,
        IReadOnlyDictionary<string, IReadOnlyList<PortPublish>?>? ports = null,
        IReadOnlyList<ContainerMount>? mounts = null) => new()
    {
        Id = "aaaaaaaaaaaa0000",
        Name = "/shop-api-1",
        State = new ContainerState
        {
            Status = status,
            Health = health is null
                ? null
                : new ContainerHealth
                {
                    Status = health,
                    FailingStreak = failingStreak,
                    Log = healthSaid is null ? null : [new HealthRun { ExitCode = 1, Output = healthSaid }],
                },
        },
        HostConfig = new ContainerHostConfig { PortBindings = ports },
        Mounts = mounts ?? [],
    };

    private static IReadOnlyDictionary<string, IReadOnlyList<PortPublish>?> Published(
        params (string Container, string Host)[] bindings) =>
        bindings.ToDictionary(
            b => b.Container,
            b => (IReadOnlyList<PortPublish>?)[new PortPublish { HostIp = "0.0.0.0", HostPort = b.Host }],
            StringComparer.Ordinal);

    private static VerifyFacts Facts(
        ContainerSummary? summary = null,
        ContainerInspect? inspect = null,
        IReadOnlyList<PortAnswer>? ports = null,
        RequestAnswer? request = null,
        IReadOnlyList<int>? expect = null) => new(
            Address.Parse("shop-api-1"),
            summary ?? Summary(),
            inspect ?? Inspect(),
            ports ?? [],
            request,
            expect);

    // ---- the fact the daemon cannot supply -----------------------------------------------------

    [Fact]
    public void A_port_that_accepts_from_Windows_is_the_proof()
    {
        var report = ServiceVerify.Verify(Facts(
            ports: [new PortAnswer(8080, "8080/tcp", true, 3, null)]));

        var port = report[ServiceVerify.Rows.Port];
        Assert.NotNull(port);
        Assert.Equal(Verdict.Pass, port.Verdict);
        Assert.Contains("accepts", port.Detail, StringComparison.Ordinal);
        Assert.True(report.CanHostEngine);
    }

    [Fact]
    public void A_published_port_that_answers_nothing_names_the_bind()
    {
        // The case DD26's port row deliberately stopped short of: the socket table says listening and
        // the connection is refused anyway, which is a dead process or a service bound to 127.0.0.1
        // inside the container. "listening" would have called this one green.
        var report = ServiceVerify.Verify(Facts(
            ports: [new PortAnswer(8080, "8080/tcp", false, 1, "ConnectionRefused")]));

        var port = report[ServiceVerify.Rows.Port];
        Assert.NotNull(port);
        Assert.Equal(Verdict.Fail, port.Verdict);
        Assert.Contains("8080", port.Remedy!, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1", port.Remedy!, StringComparison.Ordinal);
        Assert.False(report.CanHostEngine);
    }

    [Fact]
    public void A_container_that_is_not_running_is_the_whole_answer()
    {
        var report = ServiceVerify.Verify(Facts(
            Summary("exited"), Inspect("exited", ports: Published(("8080/tcp", "8080")))));

        // One row, and it points at the verb that answers why rather than repeating it.
        var row = Assert.Single(report.Checks);
        Assert.Equal(ServiceVerify.Rows.State, row.Id);
        Assert.Contains("nothing was probed", row.Detail, StringComparison.Ordinal);
        Assert.Contains("read doctor", row.Remedy!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_container_publishing_nothing_says_so_rather_than_passing()
    {
        var report = ServiceVerify.Verify(Facts());

        var row = Assert.Single(report.Checks);
        Assert.Equal(Verdict.Unknown, row.Verdict);
        Assert.False(row.Blocking);
    }

    // ---- what the health check said ------------------------------------------------------------

    [Fact]
    public void The_health_row_carries_what_the_check_printed()
    {
        // "unhealthy" is a verdict with no content. The command's own last line is what somebody acts
        // on, and it was already on an inspect nobody was reading it from.
        var report = ServiceVerify.Verify(Facts(inspect: Inspect(
            health: "unhealthy",
            failingStreak: 3,
            healthSaid: "connect ECONNREFUSED 127.0.0.1:8080\n",
            ports: Published(("8080/tcp", "8080"))),
            ports: [new PortAnswer(8080, "8080/tcp", true, 3, null)]));

        var health = report[ServiceVerify.Rows.Health];
        Assert.NotNull(health);
        Assert.Equal(Verdict.Fail, health.Verdict);
        Assert.Contains("ECONNREFUSED", health.Detail, StringComparison.Ordinal);

        // One line, whatever the check printed: a row that carried a newline would break the report.
        Assert.DoesNotContain('\n', health.Detail);
    }

    [Fact]
    public void A_health_check_still_starting_does_not_fail_the_verify()
    {
        var report = ServiceVerify.Verify(Facts(
            inspect: Inspect(health: "starting", ports: Published(("8080/tcp", "8080"))),
            ports: [new PortAnswer(8080, "8080/tcp", true, 3, null)]));

        Assert.Equal(Verdict.Warn, report[ServiceVerify.Rows.Health]!.Verdict);
        Assert.True(report.CanHostEngine);
    }

    // ---- the request ---------------------------------------------------------------------------

    [Fact]
    public void A_status_that_came_back_is_an_answer()
    {
        var report = ServiceVerify.Verify(Facts(
            ports: [new PortAnswer(8080, "8080/tcp", true, 3, null)],
            request: new RequestAnswer("http://127.0.0.1:8080/healthz", 204, 7, null)));

        Assert.Equal(Verdict.Pass, report[ServiceVerify.Rows.Request]!.Verdict);
    }

    [Fact]
    public void A_status_nobody_expected_says_the_service_is_up_and_the_path_is_not()
    {
        var report = ServiceVerify.Verify(Facts(
            ports: [new PortAnswer(8080, "8080/tcp", true, 3, null)],
            request: new RequestAnswer("http://127.0.0.1:8080/healthz", 404, 7, null)));

        var row = report[ServiceVerify.Rows.Request];
        Assert.Equal(Verdict.Fail, row!.Verdict);

        // The distinction that saves a call: a 404 is not "unreachable", and saying so stops the caller
        // going back to the port.
        Assert.Contains("the service is up", row.Remedy!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_request_that_never_finished_points_at_the_service_and_not_the_mapping()
    {
        var report = ServiceVerify.Verify(Facts(
            ports: [new PortAnswer(8080, "8080/tcp", true, 3, null)],
            request: new RequestAnswer("http://127.0.0.1:8080/healthz", null, 2000, "timed out after 2s")));

        var row = report[ServiceVerify.Rows.Request];
        Assert.Equal(Verdict.Fail, row!.Verdict);
        Assert.Contains("rather than the mapping", row.Remedy!, StringComparison.Ordinal);
    }

    // ---- the status the caller named (DD250) ----------------------------------------------------

    [Fact]
    public void A_removed_path_passes_when_the_call_named_the_status_it_returns()
    {
        var report = ServiceVerify.Verify(Facts(
            ports: [new PortAnswer(8080, "8080/tcp", true, 3, null)],
            request: new RequestAnswer("http://127.0.0.1:8080/retired", 404, 7, null),
            expect: [404]));

        var row = report[ServiceVerify.Rows.Request];
        Assert.Equal(Verdict.Pass, row!.Verdict);
        Assert.Null(row.Remedy);
        Assert.True(report.CanHostEngine);
    }

    [Fact]
    public void An_expectation_that_missed_says_which_status_arrived()
    {
        var report = ServiceVerify.Verify(Facts(
            ports: [new PortAnswer(8080, "8080/tcp", true, 3, null)],
            request: new RequestAnswer("http://127.0.0.1:8080/retired", 200, 7, null),
            expect: [404]));

        var row = report[ServiceVerify.Rows.Request];
        Assert.Equal(Verdict.Fail, row!.Verdict);

        // The status is in the detail and the expectation is in the remedy, so the caller reads what
        // it asked for beside what it got without going back to the command line it typed.
        Assert.Contains("200", row.Detail, StringComparison.Ordinal);
        Assert.Contains("--expect named 404", row.Remedy!, StringComparison.Ordinal);
    }

    [Fact]
    public void Naming_several_statuses_passes_on_any_one_of_them()
    {
        var report = ServiceVerify.Verify(Facts(
            ports: [new PortAnswer(8080, "8080/tcp", true, 3, null)],
            request: new RequestAnswer("http://127.0.0.1:8080/retired", 410, 7, null),
            expect: [404, 410]));

        Assert.Equal(Verdict.Pass, report[ServiceVerify.Rows.Request]!.Verdict);
    }

    [Fact]
    public void An_expectation_names_a_status_and_not_a_connection()
    {
        // A request that never finished has no status to match, so naming one cannot turn a service
        // that answered nothing into a pass.
        var report = ServiceVerify.Verify(Facts(
            ports: [new PortAnswer(8080, "8080/tcp", true, 3, null)],
            request: new RequestAnswer("http://127.0.0.1:8080/retired", null, 2000, "timed out after 2s"),
            expect: [404]));

        var row = report[ServiceVerify.Rows.Request];
        Assert.Equal(Verdict.Fail, row!.Verdict);
        Assert.Contains("rather than the mapping", row.Remedy!, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_expectation_reads_as_none_at_all()
    {
        // A caller who filtered a list down to nothing gets the default reading rather than a row
        // that no status could ever satisfy.
        var report = ServiceVerify.Verify(Facts(
            ports: [new PortAnswer(8080, "8080/tcp", true, 3, null)],
            request: new RequestAnswer("http://127.0.0.1:8080/healthz", 204, 7, null),
            expect: []));

        Assert.Equal(Verdict.Pass, report[ServiceVerify.Rows.Request]!.Verdict);
    }

    // ---- the mount, as far as this side can see ------------------------------------------------

    [Fact]
    public void An_empty_bind_source_fails_because_the_count_is_the_point()
    {
        var empty = Directory.CreateTempSubdirectory("freewilly-verify-empty");
        try
        {
            var report = ServiceVerify.Verify(Facts(
                inspect: Inspect(
                    ports: Published(("8080/tcp", "8080")),
                    mounts: [Bind("/app", empty.FullName)]),
                ports: [new PortAnswer(8080, "8080/tcp", true, 3, null)]));

            var row = report[ServiceVerify.Rows.Mounts];
            Assert.NotNull(row);

            // "exists" would have called this green, and the container sees an empty directory rather
            // than an error - so the service reads as missing its own code.
            Assert.Equal(Verdict.Fail, row.Verdict);
            Assert.Contains("0 file(s)", row.Detail, StringComparison.Ordinal);
            Assert.False(report.CanHostEngine);
        }
        finally
        {
            empty.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_bind_source_with_something_in_it_passes()
    {
        var full = Directory.CreateTempSubdirectory("freewilly-verify-full");
        try
        {
            File.WriteAllText(System.IO.Path.Combine(full.FullName, "index.js"), "//");

            var report = ServiceVerify.Verify(Facts(
                inspect: Inspect(
                    ports: Published(("8080/tcp", "8080")),
                    mounts: [Bind("/app", full.FullName)]),
                ports: [new PortAnswer(8080, "8080/tcp", true, 3, null)]));

            Assert.Equal(Verdict.Pass, report[ServiceVerify.Rows.Mounts]!.Verdict);
            Assert.Contains("1 file(s)", report[ServiceVerify.Rows.Mounts]!.Detail, StringComparison.Ordinal);
        }
        finally
        {
            full.Delete(recursive: true);
        }
    }

    [Fact]
    public void The_far_side_is_reported_unchecked_rather_than_guessed()
    {
        // A volume lives inside the distribution, and counting it means an exec - a POST that creates
        // an exec object, which a read verb cannot reach. DD26's rule applies: a false "does not
        // resolve" is worse than no answer.
        var report = ServiceVerify.Verify(Facts(
            inspect: Inspect(
                ports: Published(("8080/tcp", "8080")),
                mounts: [new ContainerMount { Type = "volume", Name = "shop_data", Destination = "/data" }]),
            ports: [new PortAnswer(8080, "8080/tcp", true, 3, null)]));

        var row = report[ServiceVerify.Rows.Mounts];
        Assert.Equal(Verdict.Unknown, row!.Verdict);
        Assert.Contains("unchecked", row.Detail, StringComparison.Ordinal);

        // Unknown and not blocking: a row nobody could answer must not fail a verify whose port and
        // health both passed.
        Assert.False(row.Blocking);
        Assert.True(report.CanHostEngine);
    }

    /// <summary>A bind whose source is written the way the daemon reports it, inside the distribution.</summary>
    private static ContainerMount Bind(string destination, string windowsPath)
    {
        // /mnt/d/... is what a mapped Windows drive looks like from inside WSL, and Wsl.ToWindowsPath
        // is what turns it back. Building it from the real temp path keeps the test honest about the
        // conversion rather than hard-coding one side of it.
        var root = System.IO.Path.GetPathRoot(windowsPath)![..1].ToLowerInvariant();
        var rest = windowsPath[3..].Replace('\\', '/');
        return new ContainerMount
        {
            Type = "bind",
            Source = $"/mnt/{root}/{rest}",
            Destination = destination,
        };
    }

    // ---- what it costs -------------------------------------------------------------------------

    [Fact]
    public void A_verify_stays_under_the_ceiling_recorded_for_it()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        string? budgetPath = null;
        while (here is not null && budgetPath is null)
        {
            var candidate = System.IO.Path.Combine(here.FullName, "agent-budget.json");
            budgetPath = File.Exists(candidate) ? candidate : null;
            here = here.Parent;
        }

        Assert.NotNull(budgetPath);
        using var budget = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(budgetPath));
        var ceiling = budget.RootElement.GetProperty("surface").GetProperty("shapes")
            .GetProperty("read verify").GetInt32();

        // Every row present and every one of them failing, which is the most expensive shape there is.
        var worst = ReportText.Render(
            ServiceVerify.Verify(Facts(
                inspect: Inspect(
                    health: "unhealthy",
                    failingStreak: 3,
                    healthSaid: "connect ECONNREFUSED 127.0.0.1:8080",
                    ports: Published(("8080/tcp", "8080"), ("5432/tcp", "5432")),
                    mounts: [new ContainerMount
                    {
                        Type = "bind", Source = "/mnt/c/dev/shop/api", Destination = "/app",
                    }]),
                ports:
                [
                    new PortAnswer(5432, "5432/tcp", false, 1, "ConnectionRefused"),
                    new PortAnswer(8080, "8080/tcp", true, 3, null),
                ],
                request: new RequestAnswer("http://127.0.0.1:8080/healthz", 502, 12, null))),
            heading: "freewilly read verify shop-api-1",
            summary: "Not answering: port, request, health, mounts. The remedy on each row is the action.");

        Assert.True(
            TokenEstimate.Of(worst) <= ceiling,
            $"a verify with every row failing is {TokenEstimate.Of(worst)} estimated tokens against "
            + $"the {ceiling} recorded in agent-budget.json.");
    }
}
