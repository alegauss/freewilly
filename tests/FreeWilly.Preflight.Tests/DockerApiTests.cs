using FreeWilly.Core.Api;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// The client, over a real named pipe. The happy path is one test; the rest are the answers a
/// daemon actually gives when something is wrong, because those are what a UI has to render.
/// </summary>
public sealed class DockerApiTests
{
    private const string VersionJson = """
        {"Platform":{"Name":"Docker Engine - Community"},"Version":"29.7.2",
         "ApiVersion":"1.55","MinAPIVersion":"1.24","Os":"linux","Arch":"amd64",
         "KernelVersion":"6.6.87.2","GitCommit":"6a43e3d"}
        """;

    private const string ContainersJson = """
        [{"Id":"a1b2c3d4e5f60718293a4b5c6d7e8f90","Names":["/hopeful_wozniak"],
          "Image":"nginx:alpine","State":"running","Status":"Up 3 minutes",
          "Ports":[{"IP":"0.0.0.0","PrivatePort":80,"PublicPort":8080,"Type":"tcp"},
                   {"PrivatePort":443,"Type":"tcp"}]},
         {"Id":"ff00112233445566778899aabbccddee","Names":[],
          "Image":"hello-world","State":"exited","Status":"Exited (0) 2 minutes ago",
          "Ports":[]}]
        """;

    /// <summary>What a real daemon returns for one `-p 8099:80`: the same port, twice.</summary>
    private const string DualStackJson = """
        [{"Id":"d7c06c1092c59ff199799c6e94da3f4273f51cf4d9ac6ef44a8c496df175b562",
          "Names":["/dd4-nginx"],"Image":"nginx:alpine","State":"running","Status":"Up 1 second",
          "Ports":[{"IP":"0.0.0.0","PrivatePort":80,"PublicPort":8099,"Type":"tcp"},
                   {"IP":"::","PrivatePort":80,"PublicPort":8099,"Type":"tcp"}]}]
        """;

    private static string Path(string endpoint) => $"/{DockerApi.ApiVersion}/{endpoint}";

    [Fact]
    public async Task A_port_published_on_both_address_families_is_shown_once()
    {
        // Measured against a real engine: `-p 8099:80` came back as two entries and printed as
        // "8099->80/tcp, 8099->80/tcp". The fixture above is that response.
        await using var daemon = new FakeDockerDaemon()
            .Json(Path("containers/json?all=1"), DualStackJson);
        using var api = new DockerApi(daemon.PipeName);

        var container = (await api.ContainersAsync())[0];

        Assert.Equal(2, container.Ports.Count);
        Assert.Equal(["8099->80/tcp"], container.PublishedPorts);
    }


    // ---- the happy path -----------------------------------------------------------------------

    [Fact]
    public async Task Version_is_read_off_a_real_pipe()
    {
        await using var daemon = new FakeDockerDaemon().Json(Path("version"), VersionJson);
        using var api = new DockerApi(daemon.PipeName);

        var version = await api.VersionAsync();

        Assert.Equal("29.7.2", version.Version);
        Assert.Equal("1.55", version.ApiVersion);
        Assert.Equal("1.24", version.MinApiVersion);
        Assert.Equal("linux", version.Os);
        Assert.Equal("amd64", version.Arch);
    }

    [Fact]
    public async Task Every_request_carries_the_pinned_api_version()
    {
        // Unversioned paths are answered with the daemon's newest, so an engine upgrade could change
        // a response shape under a client that asked for nothing.
        await using var daemon = new FakeDockerDaemon().Json(Path("version"), VersionJson);
        using var api = new DockerApi(daemon.PipeName);

        await api.VersionAsync();

        Assert.Contains(daemon.Requested, line =>
            line.Contains($"/{DockerApi.ApiVersion}/version", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Containers_are_read_with_their_names_state_and_ports()
    {
        await using var daemon = new FakeDockerDaemon()
            .Json(Path("containers/json?all=1"), ContainersJson);
        using var api = new DockerApi(daemon.PipeName);

        var containers = await api.ContainersAsync();

        Assert.Equal(2, containers.Count);
        var running = containers[0];
        Assert.Equal("hopeful_wozniak", running.DisplayName);
        Assert.Equal("a1b2c3d4e5f6", running.ShortId);
        Assert.Equal("nginx:alpine", running.Image);
        Assert.Equal("running", running.State);
        Assert.Equal(["8080->80/tcp", "443/tcp"], running.Ports.Select(p => p.ToString()));
    }

    [Fact]
    public async Task A_container_with_no_name_falls_back_to_its_short_id()
    {
        await using var daemon = new FakeDockerDaemon()
            .Json(Path("containers/json?all=1"), ContainersJson);
        using var api = new DockerApi(daemon.PipeName);

        var containers = await api.ContainersAsync();

        Assert.Equal("ff0011223344", containers[1].DisplayName);
        Assert.Empty(containers[1].Ports);
    }

    [Fact]
    public async Task Asking_for_running_containers_only_leaves_the_all_flag_off()
    {
        await using var daemon = new FakeDockerDaemon()
            .Json(Path("containers/json"), "[]");
        using var api = new DockerApi(daemon.PipeName);

        await api.ContainersAsync(all: false);

        Assert.DoesNotContain(daemon.Requested, line =>
            line.Contains("all=1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ping_is_true_when_the_engine_answers()
    {
        await using var daemon = new FakeDockerDaemon().Json(Path("_ping"), "OK");
        using var api = new DockerApi(daemon.PipeName);

        Assert.True(await api.PingAsync());
    }

    // ---- what a daemon says when something is wrong ------------------------------------------

    [Fact]
    public async Task Ping_is_false_when_no_pipe_is_there_rather_than_throwing()
    {
        // The tray asks this on a timer. An exception per tick for a stopped engine is not an
        // answer, it is noise.
        using var api = new DockerApi($"freewilly-absent-{Guid.NewGuid():N}", TimeSpan.FromSeconds(3));

        Assert.False(await api.PingAsync());
    }

    [Fact]
    public async Task A_call_to_an_absent_engine_names_the_endpoint_it_could_not_reach()
    {
        using var api = new DockerApi($"freewilly-absent-{Guid.NewGuid():N}", TimeSpan.FromSeconds(3));

        var thrown = await Assert.ThrowsAsync<DockerApiException>(() => api.VersionAsync());

        Assert.Contains("did not answer", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("version", thrown.Message, StringComparison.Ordinal);
        Assert.Null(thrown.Status);
    }

    [Fact]
    public async Task A_refusal_carries_the_status_and_the_sentence_the_daemon_put_in_the_body()
    {
        await using var daemon = new FakeDockerDaemon()
            .Fails(Path("version"), "500 Internal Server Error", "something went wrong in the daemon");
        using var api = new DockerApi(daemon.PipeName);

        var thrown = await Assert.ThrowsAsync<DockerApiException>(() => api.VersionAsync());

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, thrown.Status);
        Assert.Contains("something went wrong in the daemon", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_endpoint_this_daemon_does_not_have_is_a_404_and_not_a_crash()
    {
        // An engine older than the pinned API version answers exactly like this.
        await using var daemon = new FakeDockerDaemon();
        using var api = new DockerApi(daemon.PipeName);

        var thrown = await Assert.ThrowsAsync<DockerApiException>(() => api.VersionAsync());

        Assert.Equal(System.Net.HttpStatusCode.NotFound, thrown.Status);
    }

    [Fact]
    public async Task A_body_that_is_not_the_json_expected_is_reported_as_that()
    {
        // A proxy or a captive portal answering on the pipe's behalf is far-fetched; a daemon
        // changing a shape is not. Either way "unexpected character" alone tells a user nothing.
        await using var daemon = new FakeDockerDaemon()
            .Json(Path("version"), "this is not json");
        using var api = new DockerApi(daemon.PipeName);

        var thrown = await Assert.ThrowsAsync<DockerApiException>(() => api.VersionAsync());

        Assert.Contains("not the JSON expected", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_streaming_endpoint_hands_back_a_body_that_is_read_as_it_arrives()
    {
        await using var daemon = new FakeDockerDaemon()
            .Json(Path("events"), "{\"status\":\"start\"}\n{\"status\":\"die\"}\n");
        using var api = new DockerApi(daemon.PipeName);

        await using var stream = await api.StreamAsync("events");
        using var reader = new StreamReader(stream);

        Assert.Equal("{\"status\":\"start\"}", await reader.ReadLineAsync());
        Assert.Equal("{\"status\":\"die\"}", await reader.ReadLineAsync());
    }

    [Fact]
    public async Task A_streaming_endpoint_that_is_refused_throws_before_handing_back_a_stream()
    {
        await using var daemon = new FakeDockerDaemon()
            .Fails(Path("events"), "400 Bad Request", "bad filter");
        using var api = new DockerApi(daemon.PipeName);

        var thrown = await Assert.ThrowsAsync<DockerApiException>(() => api.StreamAsync("events"));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, thrown.Status);
        Assert.Contains("bad filter", thrown.Message, StringComparison.Ordinal);
    }

    // ---- the four verbs -----------------------------------------------------------------------

    private const string Id = "a1b2c3d4e5f60718293a4b5c6d7e8f90";

    private static string NoBody(string status) => $"HTTP/1.1 {status}\r\nConnection: close\r\n\r\n";

    [Theory]
    [InlineData("start")]
    [InlineData("stop")]
    [InlineData("restart")]
    public async Task A_verb_is_a_post_to_the_container_and_a_204_is_the_whole_answer(string verb)
    {
        await using var daemon = new FakeDockerDaemon()
            .Raw(Path($"containers/{Id}/{verb}"), NoBody("204 No Content"));
        using var api = new DockerApi(daemon.PipeName);

        await (verb switch
        {
            "start" => api.StartContainerAsync(Id),
            "stop" => api.StopContainerAsync(Id),
            _ => api.RestartContainerAsync(Id),
        });

        Assert.Contains(daemon.Requested, line =>
            line.StartsWith($"POST /{DockerApi.ApiVersion}/containers/{Id}/{verb} ",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stopping_a_container_that_is_already_down_is_not_a_failure()
    {
        // 304 is the daemon saying the container is already in the state that was asked for. The
        // row already reads that way, so reporting "not modified" would be an error about nothing.
        await using var daemon = new FakeDockerDaemon()
            .Raw(Path($"containers/{Id}/stop"), NoBody("304 Not Modified"));
        using var api = new DockerApi(daemon.PipeName);

        await api.StopContainerAsync(Id);
    }

    [Fact]
    public async Task A_removal_says_whether_it_may_kill_first()
    {
        await using var daemon = new FakeDockerDaemon()
            .Raw(Path($"containers/{Id}?force=1"), NoBody("204 No Content"))
            .Raw(Path($"containers/{Id}?force=0"), NoBody("204 No Content"));
        using var api = new DockerApi(daemon.PipeName);

        await api.RemoveContainerAsync(Id, force: true);
        await api.RemoveContainerAsync(Id);

        Assert.Contains(daemon.Requested, line =>
            line.StartsWith($"DELETE /{DockerApi.ApiVersion}/containers/{Id}?force=1 ",
                StringComparison.Ordinal));
        Assert.Contains(daemon.Requested, line =>
            line.Contains("?force=0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_refused_action_carries_the_daemon_s_own_sentence_for_the_row_to_show()
    {
        // What the engine says here is the useful part: "port is already allocated" is an answer,
        // and "the action failed" is not.
        await using var daemon = new FakeDockerDaemon().Fails(
            Path($"containers/{Id}/start"),
            "500 Internal Server Error",
            "driver failed programming external connectivity: "
            + "Bind for 0.0.0.0:8080 failed: port is already allocated");
        using var api = new DockerApi(daemon.PipeName);

        var thrown = await Assert.ThrowsAsync<DockerApiException>(() => api.StartContainerAsync(Id));

        Assert.Contains("port is already allocated", thrown.Message, StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, thrown.Status);

        // The row shows Detail, and Detail is the daemon's sentence with none of this client's
        // framing: no status, no endpoint, nothing the user did not ask about.
        Assert.StartsWith("driver failed", thrown.Detail!, StringComparison.Ordinal);
        Assert.DoesNotContain("the engine answered", thrown.Detail!, StringComparison.Ordinal);
        Assert.DoesNotContain(DockerApi.ApiVersion, thrown.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_engine_that_never_answered_has_nothing_of_its_own_to_quote()
    {
        // Detail is the daemon's words or nothing. A row that falls back to Message here is showing
        // the only sentence that exists.
        using var api = new DockerApi($"freewilly-absent-{Guid.NewGuid():N}", TimeSpan.FromSeconds(3));

        var thrown = await Assert.ThrowsAsync<DockerApiException>(() => api.StartContainerAsync(Id));

        Assert.Null(thrown.Detail);
    }

    [Fact]
    public async Task Removing_a_running_container_without_force_is_the_409_the_daemon_gives()
    {
        await using var daemon = new FakeDockerDaemon().Fails(
            Path($"containers/{Id}?force=0"), "409 Conflict",
            "You cannot remove a running container. Stop the container before attempting removal or force remove");
        using var api = new DockerApi(daemon.PipeName);

        var thrown = await Assert.ThrowsAsync<DockerApiException>(
            () => api.RemoveContainerAsync(Id));

        Assert.Equal(System.Net.HttpStatusCode.Conflict, thrown.Status);
        Assert.Contains("cannot remove a running container", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_action_against_an_absent_engine_names_the_endpoint_rather_than_timing_out_blankly()
    {
        using var api = new DockerApi($"freewilly-absent-{Guid.NewGuid():N}", TimeSpan.FromSeconds(3));

        var thrown = await Assert.ThrowsAsync<DockerApiException>(() => api.StopContainerAsync(Id));

        Assert.Contains("did not answer", thrown.Message, StringComparison.Ordinal);
        Assert.Contains($"{Id}/stop", thrown.Message, StringComparison.Ordinal);
    }

    // ---- running one command inside a container ------------------------------------------------

    [Fact]
    public async Task A_command_run_in_a_container_comes_back_as_its_exit_code()
    {
        await using var daemon = new FakeDockerDaemon()
            .Json(Path($"containers/{Id}/exec"), """{"Id":"e5f6a7b8"}""")
            .Raw(Path("exec/e5f6a7b8/start"), NoBody("200 OK"))
            .Json(Path("exec/e5f6a7b8/json"), """{"Running":false,"ExitCode":0}""");
        using var api = new DockerApi(daemon.PipeName);

        Assert.Equal(0, await api.RunInContainerAsync(Id, ["/bin/sh", "-c", "exit 0"]));

        Assert.Contains(daemon.Requested, line =>
            line.StartsWith($"POST /{DockerApi.ApiVersion}/containers/{Id}/exec ",
                StringComparison.Ordinal));
        Assert.Contains(daemon.Requested, line =>
            line.StartsWith($"POST /{DockerApi.ApiVersion}/exec/e5f6a7b8/start ",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_non_zero_exit_is_returned_rather_than_thrown()
    {
        // A shell that ran and failed is an answer about the container, not a failed call.
        await using var daemon = new FakeDockerDaemon()
            .Json(Path($"containers/{Id}/exec"), """{"Id":"e5f6a7b8"}""")
            .Raw(Path("exec/e5f6a7b8/start"), NoBody("200 OK"))
            .Json(Path("exec/e5f6a7b8/json"), """{"Running":false,"ExitCode":126}""");
        using var api = new DockerApi(daemon.PipeName);

        Assert.Equal(126, await api.RunInContainerAsync(Id, ["/bin/bash", "-c", "exit 0"]));
    }

    [Fact]
    public async Task A_daemon_reporting_no_exit_code_at_all_is_not_read_as_success()
    {
        // ExitCode is null while an exec is still going, and null read as 0 would say "this shell
        // works" about a probe that never finished.
        await using var daemon = new FakeDockerDaemon()
            .Json(Path($"containers/{Id}/exec"), """{"Id":"e5f6a7b8"}""")
            .Raw(Path("exec/e5f6a7b8/start"), NoBody("200 OK"))
            .Json(Path("exec/e5f6a7b8/json"), """{"Running":true}""");
        using var api = new DockerApi(daemon.PipeName);

        Assert.NotEqual(0, await api.RunInContainerAsync(Id, ["/bin/sh", "-c", "exit 0"]));
    }

    [Fact]
    public async Task A_binary_that_is_not_in_the_image_is_a_refusal_from_start()
    {
        // This is what "no /bin/bash" actually looks like: exec create succeeds and start refuses.
        await using var daemon = new FakeDockerDaemon()
            .Json(Path($"containers/{Id}/exec"), """{"Id":"e5f6a7b8"}""")
            .Fails(
                Path("exec/e5f6a7b8/start"), "500 Internal Server Error",
                "OCI runtime exec failed: exec failed: unable to start container process: "
                + "exec: \"/bin/bash\": stat /bin/bash: no such file or directory");
        using var api = new DockerApi(daemon.PipeName);

        var thrown = await Assert.ThrowsAsync<DockerApiException>(
            () => api.RunInContainerAsync(Id, ["/bin/bash", "-c", "exit 0"]));

        Assert.Contains("no such file or directory", thrown.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Several_calls_in_a_row_work_on_one_client()
    {
        await using var daemon = new FakeDockerDaemon()
            .Json(Path("version"), VersionJson)
            .Json(Path("containers/json?all=1"), ContainersJson)
            .Json(Path("_ping"), "OK");
        using var api = new DockerApi(daemon.PipeName);

        Assert.True(await api.PingAsync());
        Assert.Equal("29.7.2", (await api.VersionAsync()).Version);
        Assert.Equal(2, (await api.ContainersAsync()).Count);
        Assert.Equal("29.7.2", (await api.VersionAsync()).Version);

        Assert.Equal(4, daemon.Requested.Count);
    }

    // ---- how long a call is given, by what kind of call it is (DD234) ------------------------

    [Fact]
    public void Housekeeping_is_given_longer_than_a_question()
    {
        // The two exist to be different. A single constant is what DD234 was: the compaction's
        // prune ran under the budget written for a list, and failed at twenty seconds on the disk
        // the button is for.
        Assert.True(EngineBudget.Housekeeping > EngineBudget.Question);
    }

    [Fact]
    public async Task A_prune_outlives_the_budget_a_question_gets_on_the_same_client()
    {
        // One client, two budgets, which is the whole of DD235: the window's pages and its prune
        // buttons share a client, so raising the one number would have been raising it for every
        // list too. The daemon here is not stuck, it is deleting.
        await using var daemon = new FakeDockerDaemon()
            .Json(Path("build/prune"), """{"CachesDeleted":["k1","k2"],"SpaceReclaimed":4294967296}""")
            .Takes(Path("build/prune"), TimeSpan.FromMilliseconds(600));
        using var api = new DockerApi(
            daemon.PipeName,
            timeout: TimeSpan.FromMilliseconds(250),
            housekeeping: TimeSpan.FromSeconds(10));

        var pruned = await api.PruneBuildCacheAsync();

        Assert.Equal(2, pruned.CachesDeleted!.Count);
        Assert.Equal(4294967296, pruned.SpaceReclaimed);
    }

    [Theory]
    [InlineData("images/prune?filters=%7B%22dangling%22%3A%5B%22true%22%5D%7D")]
    [InlineData("volumes/prune")]
    public async Task The_window_s_prune_buttons_get_the_same_budget_the_compaction_does(string endpoint)
    {
        // DD235's actual report: both of these ran under the list's twenty seconds, on the surface
        // where a user is watching and the machine is large enough to have pressed the button.
        await using var daemon = new FakeDockerDaemon()
            .Json(Path(endpoint), """{"ImagesDeleted":[],"VolumesDeleted":[],"SpaceReclaimed":17}""")
            .Takes(Path(endpoint), TimeSpan.FromMilliseconds(600));
        using var api = new DockerApi(
            daemon.PipeName,
            timeout: TimeSpan.FromMilliseconds(250),
            housekeeping: TimeSpan.FromSeconds(10));

        var reclaimed = endpoint.StartsWith("images", StringComparison.Ordinal)
            ? (await api.PruneDanglingImagesAsync()).SpaceReclaimed
            : (await api.PruneAnonymousVolumesAsync()).SpaceReclaimed;

        Assert.Equal(17, reclaimed);
    }

    [Fact]
    public async Task A_list_keeps_the_short_budget_on_a_client_whose_prune_is_patient()
    {
        // The other half of the same claim, and the one that would break if the fix had been to
        // raise the client's timeout: a hung list still has to give up while somebody is looking.
        await using var daemon = new FakeDockerDaemon()
            .Json(Path("containers/json?all=1"), ContainersJson)
            .Takes(Path("containers/json?all=1"), TimeSpan.FromSeconds(5));
        using var api = new DockerApi(
            daemon.PipeName,
            timeout: TimeSpan.FromMilliseconds(300),
            housekeeping: TimeSpan.FromMinutes(10));

        var thrown = await Assert.ThrowsAsync<DockerApiException>(() => api.ContainersAsync());

        Assert.Contains("containers/json", thrown.Message, StringComparison.Ordinal);
        Assert.Null(thrown.Status);
    }

    [Fact]
    public async Task A_budget_that_runs_out_names_the_endpoint_and_the_wait_and_no_dotnet_class()
    {
        // What DD235 read in a log was ".NET's own sentence": "The request was canceled due to the
        // configured HttpClient.Timeout of 20 seconds elapsing" — a class the reader does not have,
        // and no endpoint.
        await using var daemon = new FakeDockerDaemon()
            .Json(Path("version"), VersionJson)
            .Takes(Path("version"), TimeSpan.FromSeconds(5));
        using var api = new DockerApi(daemon.PipeName, TimeSpan.FromMilliseconds(300));

        var thrown = await Assert.ThrowsAsync<DockerApiException>(() => api.VersionAsync());

        Assert.Contains("did not answer", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("version", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("0.3 seconds", thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cancellation_the_caller_asked_for_is_not_dressed_up_as_a_budget()
    {
        // The linked token is what makes these two tellable apart, and they are different endings:
        // one is a page that navigated away, the other is a daemon that took too long.
        await using var daemon = new FakeDockerDaemon()
            .Json(Path("version"), VersionJson)
            .Takes(Path("version"), TimeSpan.FromSeconds(5));
        using var api = new DockerApi(daemon.PipeName, TimeSpan.FromMinutes(1));
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => api.VersionAsync(caller.Token));
    }
}
