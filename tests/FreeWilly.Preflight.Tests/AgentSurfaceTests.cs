using System.Text.Json;
using FreeWilly.Core.Agent;
using FreeWilly.Core.Api;
using FreeWilly.Tray.Cli;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// The read/do split, and the promise that a read mutates nothing (DD24).
/// </summary>
/// <remarks>
/// The section is explicit that <c>read</c> is a promise rather than a naming convention, and that the
/// guard belongs in a test rather than in review. There are two here because they fail differently: the
/// type stops a read verb being written against a mutating client at all, and this drives every
/// registered read verb against a fake daemon and requires every request it made to be a GET.
/// </remarks>
[Collection(ConsoleCollection.Name)]
public sealed class AgentSurfaceTests
{
    private static string Path(string endpoint) => $"/{DockerApi.ApiVersion}/{endpoint}";

    /// <summary>One verb, by name. Never by index: adding a verb ahead of it would move it.</summary>
    private static AgentVerb Verb(string half, string name) =>
        AgentSurface.Find([half, name]) ?? throw new InvalidOperationException($"no {half} {name}");

    private const string TwoContainers = """
        [{"Id":"aaaaaaaaaaaa0000","Names":["/shop-api-1"],"Image":"shop/api:latest","State":"exited",
          "Status":"Exited (137) 12 seconds ago","Ports":[{"IP":"0.0.0.0","PrivatePort":8080,"PublicPort":8080,"Type":"tcp"}]},
         {"Id":"bbbbbbbbbbbb0000","Names":["/shop-db-1"],"Image":"postgres:16-alpine","State":"running",
          "Status":"Up 4 minutes","Ports":[]}]
        """;

    // ---- the promise ---------------------------------------------------------------------------

    /// <summary>
    /// Arguments that actually make each read verb do its work.
    /// </summary>
    /// <remarks>
    /// A verb driven with no arguments refuses before it reaches the daemon, and a guard that asserts
    /// "every request was a GET" over zero requests asserts nothing. This was found the moment
    /// `read doctor` was registered: it needs a name, refused without one, and the guard went green
    /// having proved nothing about it. So the table is here, and a read verb added without an entry
    /// fails rather than being silently skipped.
    /// </remarks>
    private static readonly Dictionary<string, string[]> DrivenWith = new(StringComparer.Ordinal)
    {
        ["changes"] = [],
        ["context"] = [],
        ["ps"] = [],
        ["doctor"] = ["shop-api-1"],

        // Reaches the daemon for the one reading only it can give: whether the pipe answers, and
        // what version said so. Everything else it prints comes off Windows and out of the
        // distribution, which is why the report is behind MachineReads like every other such read.
        ["health"] = [],
        ["logs"] = ["shop-api-1"],
        ["ports"] = [],

        // Driven against the exited container on purpose. A verify of a running one would open a real
        // socket from a unit test, and this guard is about the requests that reached the daemon.
        ["verify"] = ["shop-api-1"],
    };

    /// <summary>The daemon every read verb in the registry can be driven against.</summary>
    private static FakeDockerDaemon Daemon() => new FakeDockerDaemon()
        .Fails(Path("_ping"), "200 OK", "OK")
        .Json(Path("version"), """{"Version":"29.7.2","ApiVersion":"1.55","MinAPIVersion":"1.24","Os":"linux","Arch":"amd64"}""")
        .Json(Path("containers/json?all=1"), TwoContainers)
        .Json(Path("containers/aaaaaaaaaaaa0000/json"), """{"Id":"aaaaaaaaaaaa0000","State":{"Status":"exited","ExitCode":137}}""")
        .Json(Path("images/json?all=0"), "[]")
        .Json(Path("volumes"), """{"Volumes":[]}""")
        .JsonPrefix(Path("events?"), "")
        .Fails(Path("containers/aaaaaaaaaaaa0000/logs?stdout=1&stderr=1&tail=200&follow=0&timestamps=0"), "200 OK", "")
        .Fails(Path("containers/aaaaaaaaaaaa0000/logs?stdout=1&stderr=1&tail=2000&follow=0&timestamps=1"), "200 OK", "");

    [Fact]
    public async Task Every_read_verb_issues_only_GET_requests()
    {
        // The guard the section asks for, and it enumerates the registry rather than a list written
        // here: a read verb added to AgentSurface.All is driven by this without a second edit, and one
        // that mutates fails the moment it is registered.
        foreach (var verb in AgentSurface.All.Where(v => v.Namespace == AgentNamespace.Read))
        {
            Assert.True(
                DrivenWith.TryGetValue(verb.Name, out var arguments),
                $"{verb} has no entry in DrivenWith, so this guard would skip it. Add arguments that "
                + "make it reach the daemon.");

            await using var daemon = new FakeDockerDaemon()
                .Fails(Path("_ping"), "200 OK", "OK")
                .Json(Path("version"), """{"Version":"29.7.2","ApiVersion":"1.55","MinAPIVersion":"1.24","Os":"linux","Arch":"amd64"}""")
                .Json(Path("containers/json?all=1"), TwoContainers)
                .Json(Path("containers/aaaaaaaaaaaa0000/json"), """{"Id":"aaaaaaaaaaaa0000","State":{"Status":"exited","ExitCode":137}}""")
                .Json(Path("images/json?all=0"), "[]")
                .Json(Path("volumes"), """{"Volumes":[]}""")
                .JsonPrefix(Path("events?"), "")
                .Fails(Path("containers/aaaaaaaaaaaa0000/logs?stdout=1&stderr=1&tail=200&follow=0&timestamps=0"), "200 OK", "")
                .Fails(Path("containers/aaaaaaaaaaaa0000/logs?stdout=1&stderr=1&tail=2000&follow=0&timestamps=1"), "200 OK", "");
            using var api = new DockerApi(daemon.PipeName);
            var output = new StringWriter();

            AgentSurface.Read(verb, api, arguments, output);

            Assert.NotEmpty(daemon.Requested);
            Assert.All(daemon.Requested, line => Assert.StartsWith("GET ", line, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task No_read_verb_writes_a_file_it_was_not_given_a_path_for()
    {
        // The second half of the promise, added with DD27. `read` promises not to mutate the engine, and
        // `read logs --out` writes a file on purpose - so the guard is not "writes nothing" but "writes
        // nothing the caller did not name". The GET-only guard cannot see a filesystem write at all,
        // which is why this one exists beside it.
        var scratch = Directory.CreateTempSubdirectory("freewilly-read-guard");
        var was = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(scratch.FullName);

            foreach (var verb in AgentSurface.All.Where(v => v.Namespace == AgentNamespace.Read))
            {
                await using var daemon = Daemon();
                using var api = new DockerApi(daemon.PipeName);

                AgentSurface.Read(verb, api, DrivenWith[verb.Name], new StringWriter());

                var written = scratch.GetFileSystemInfos();
                Assert.True(
                    written.Length == 0,
                    $"{verb} wrote {string.Join(", ", written.Select(f => f.Name))} without being given "
                    + "a path. A read may write where it was told to and nowhere else.");
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(was);
            scratch.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task The_one_verb_that_writes_writes_only_where_it_was_told()
    {
        var scratch = Directory.CreateTempSubdirectory("freewilly-out-guard");
        try
        {
            var target = System.IO.Path.Combine(scratch.FullName, "nested", "api.log");
            await using var daemon = Daemon();
            using var api = new DockerApi(daemon.PipeName);
            var output = new StringWriter();

            var code = AgentSurface.Read(
                Verb("read", "logs"), api, ["shop-api-1", "--out", target], output);

            Assert.Equal(0, code);
            Assert.True(File.Exists(target));
            // And the payload is the path rather than the log, which is the whole inversion.
            Assert.Contains("wrote ", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Grep it", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_read_verb_is_handed_an_engine_it_cannot_mutate()
    {
        // The compile-time half, asserted as a fact about the type rather than as a comment: the handle
        // a read verb receives exposes three reads and nothing else, so there is no start, no remove
        // and no prune to reach for.
        var reachable = typeof(IEngineReads).GetMethods().Select(m => m.Name).ToArray();

        // Listed exactly, so growing the handle is a deliberate act and not a drift: this assertion
        // failed the moment the context pack needed three more reads, which is the point of writing it
        // this way rather than as a count.
        Assert.Equal(
            ["ContainersAsync", "EventsAsync", "ImagesAsync", "InspectAsync", "LogsAsync",
             "PingAsync", "VersionAsync", "VolumesAsync"],
            reachable.OrderBy(n => n, StringComparer.Ordinal));
        foreach (var forbidden in new[] { "Start", "Stop", "Remove", "Prune", "Restart", "Run" })
        {
            Assert.DoesNotContain(reachable, name => name.Contains(forbidden, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void The_split_puts_the_two_halves_in_different_allowlist_strings()
    {
        // The whole point: `freewilly read` and `freewilly do` are different literal prefixes, so a
        // rule can grant one without the other. `docker ps` and `docker rm -f -v` cannot be told apart
        // that way, which is the defect DD24 answers.
        Assert.NotEqual(AgentSurface.ReadVerb, AgentSurface.DoVerb);
        Assert.All(AgentSurface.All, verb =>
            Assert.StartsWith(
                verb.Namespace == AgentNamespace.Read ? AgentSurface.ReadVerb : AgentSurface.DoVerb,
                verb.ToString(),
                StringComparison.Ordinal));
    }

    // ---- the routing ---------------------------------------------------------------------------

    [Theory]
    [InlineData("read", "ps")]
    [InlineData("do", "engine")]
    public void A_registered_verb_is_found(string half, string name)
    {
        var verb = AgentSurface.Find([half, name]);

        Assert.NotNull(verb);
        Assert.Equal(name, verb.Name);
    }

    [Theory]
    [InlineData("read", "engine")]
    [InlineData("do", "ps")]
    [InlineData("read", "nonsense")]
    [InlineData("Read", "ps")]
    [InlineData("write", "ps")]
    public void Anything_else_is_not(string half, string name) =>
        // `read engine` and `do ps` among them: the halves are not interchangeable, and a verb found in
        // the wrong one would be a read that writes or a write nobody was asked about.
        Assert.Null(AgentSurface.Find([half, name]));

    [Fact]
    public void The_router_sends_both_halves_to_the_agent_surface()
    {
        Assert.Equal(Surface.Agent, CommandLine.Of(["read", "ps"]).Surface);
        Assert.Equal(Surface.Agent, CommandLine.Of(["do", "engine", "start"]).Surface);
        // The whole line travels, because the surface dispatches on the first two words itself.
        Assert.Equal(["read", "ps"], CommandLine.Of(["read", "ps"]).Arguments);
    }

    [Theory]
    [InlineData("read")]
    [InlineData("do")]
    public void A_half_with_no_verb_after_it_is_refused(string half) =>
        Assert.Equal(2, AgentSurface.Run([half]));

    [Fact]
    public void An_unknown_verb_is_refused_and_named()
    {
        var captured = new StringWriter();
        var was = Console.Error;
        try
        {
            Console.SetError(captured);
            Assert.Equal(2, AgentSurface.Run(["read", "nonsense"]));
        }
        finally
        {
            Console.SetError(was);
        }

        Assert.Contains("nonsense", captured.ToString(), StringComparison.Ordinal);
    }

    // ---- what read ps answers ------------------------------------------------------------------

    [Fact]
    public async Task Containers_come_back_one_line_each_in_a_deterministic_order()
    {
        await using var daemon = new FakeDockerDaemon()
            .Fails(Path("_ping"), "200 OK", "OK")
            .Json(Path("containers/json?all=1"), TwoContainers);
        using var api = new DockerApi(daemon.PipeName);
        var output = new StringWriter();

        var code = AgentSurface.Read(Verb("read", "ps"), api, [], output);

        Assert.Equal(0, code);
        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        // Sorted by name, not by the daemon's creation order, because a deterministic payload is what
        // caches and diffs.
        Assert.StartsWith("shop-api-1", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("shop-db-1", lines[1], StringComparison.Ordinal);
        Assert.Contains("8080->8080/tcp", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_engine_that_is_not_answering_says_so_rather_than_returning_an_empty_list()
    {
        // Self-describing state, so the agent never probes for a capability. An empty list would read
        // as "no containers", which is a wrong answer rather than a missing one.
        await using var daemon = new FakeDockerDaemon()
            .Fails(Path("_ping"), "500 Internal Server Error", "");
        using var api = new DockerApi(daemon.PipeName);
        var output = new StringWriter();

        var code = AgentSurface.Read(Verb("read", "ps"), api, [], output);

        Assert.NotEqual(0, code);
        Assert.Contains("engine", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("no containers", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_argument_read_ps_does_not_take_is_refused()
    {
        await using var daemon = new FakeDockerDaemon();
        using var api = new DockerApi(daemon.PipeName);

        var was = Console.Error;
        try
        {
            Console.SetError(new StringWriter());
            Assert.Equal(2, AgentSurface.Read(Verb("read", "ps"), api, ["--nonsense"], new StringWriter()));
        }
        finally
        {
            Console.SetError(was);
        }
    }

    // ---- every shape has a ceiling before it has a payload -------------------------------------

    [Fact]
    public void Every_registered_verb_has_a_ceiling_in_the_budget()
    {
        // The second constraint the section lands here rather than later: the limit exists before the
        // first payload does. A verb registered without a ceiling fails this, which is the only moment
        // anybody is thinking about what it should cost.
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        string? found = null;
        while (here is not null && found is null)
        {
            var candidate = System.IO.Path.Combine(here.FullName, "agent-budget.json");
            found = File.Exists(candidate) ? candidate : null;
            here = here.Parent;
        }

        Assert.NotNull(found);
        using var budget = JsonDocument.Parse(File.ReadAllBytes(found));
        var shapes = budget.RootElement.GetProperty("surface").GetProperty("shapes");

        foreach (var verb in AgentSurface.All)
        {
            Assert.True(
                shapes.TryGetProperty(verb.Shape, out var ceiling),
                $"{verb} registers the shape '{verb.Shape}' and agent-budget.json has no ceiling for "
                + "it. Add one, and say in the commit what the tokens buy.");
            Assert.True(ceiling.GetInt32() > 0);
        }
    }

    [Fact]
    public void Every_verb_is_in_the_help_text()
    {
        foreach (var verb in AgentSurface.All)
        {
            Assert.Contains(verb.ToString(), AgentSurface.HelpText, StringComparison.Ordinal);
        }

        // The line a user actually has to write, spelled the way it has to be spelled.
        Assert.Contains("Bash(freewilly read:*)", AgentSurface.HelpText, StringComparison.Ordinal);
    }

    // ---- addresses are names -------------------------------------------------------------------

    [Fact]
    public void A_container_is_addressed_by_its_name()
    {
        Assert.True(Address.TryParse("shop-api-1", out var address, out _));
        Assert.Equal(AddressKind.Container, address.Kind);
        Assert.Equal("shop-api-1", address.Name);
        Assert.Equal("shop-api-1", address.ToString());
    }

    [Fact]
    public void A_compose_service_is_addressed_by_project_and_service()
    {
        Assert.True(Address.TryParse("svc:shop/api", out var address, out _));
        Assert.Equal(AddressKind.Service, address.Kind);
        Assert.Equal("shop", address.Project);
        Assert.Equal("api", address.Name);
        Assert.Equal("svc:shop/api", address.ToString());
    }

    [Fact]
    public void A_full_container_id_is_refused_and_told_why()
    {
        // The constraint that is a rewrite to retrofit: an id changes on recreate, so an agent that
        // learned one has to thread it through every later call and re-learn it when the container
        // comes back.
        var id = new string('a', 64);

        Assert.False(Address.TryParse(id, out _, out var refusal));
        Assert.Contains("name", refusal, StringComparison.Ordinal);
        Assert.Contains("recreated", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_short_id_is_not_refused_because_it_cannot_be_told_from_a_name() =>
        // Checking it would need a round trip, and a check that costs a call is worse than the thing
        // it prevents.
        Assert.True(Address.TryParse("aaaaaaaaaaaa", out _, out _));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("svc:")]
    [InlineData("svc:shop")]
    [InlineData("svc:shop/")]
    [InlineData("svc:/api")]
    [InlineData("svc:shop/api/extra")]
    public void A_malformed_address_is_refused_with_the_form_it_should_have(string? text)
    {
        Assert.False(Address.TryParse(text, out _, out var refusal));
        Assert.False(string.IsNullOrWhiteSpace(refusal));
    }

    [Fact]
    public void Parse_throws_where_TryParse_refuses() =>
        Assert.Throws<ArgumentException>(() => Address.Parse(new string('f', 64)));

    // ---- the seam DD78 opened, held by something other than memory (DD98) ----------------------

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(System.IO.Path.Combine(directory.FullName, "FreeWilly.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "the repository root was not found above the test binaries");
        return directory!.FullName;
    }

    [Fact]
    public void No_verb_on_this_surface_builds_its_own_machine_read()
    {
        // DD78 made the shaped token figure exact by routing what the verbs read off Windows through
        // `MachineReads`. Exact is a property of the code rather than of the number: a verb that
        // writes `new HostPorts()` in its own body compiles, passes, and quietly makes the measured
        // figure this machine's again — invisibly, because the number still looks precise. That is
        // worse than the 15% band it replaced, which at least said what it was.
        //
        // Asserted on the source, like the hex-colour guard over the markup and the size guard over
        // the shell, and for the same reason: the rule is one a reviewer forgets.
        var source = File.ReadAllText(
            System.IO.Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Cli/AgentSurface.cs"));

        // Derived and never listed. A seam added to `MachineReads` is guarded without this test
        // being edited — which matters because the hand-written list is the failure mode DD100
        // describes in the help-text test, where two verbs had already gone missing unnoticed.
        var seams = typeof(MachineReads)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(property => property.PropertyType)
            .Where(type => type.IsInterface)
            .ToList();

        var readers = typeof(MachineReads).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(type => seams.Exists(seam => seam.IsAssignableFrom(type)))
            .Select(type => type.Name)
            .ToList();

        // Both, or the loop below asserts nothing at all — which is how a guard becomes a comment.
        Assert.NotEmpty(seams);
        Assert.NotEmpty(readers);

        foreach (var reader in readers)
        {
            Assert.DoesNotContain($"new {reader}(", source, StringComparison.Ordinal);
        }

        // The two the interfaces do not cover, because nothing on this surface should reach them at
        // all: `WindowsMachineFacts` walks the machine for rival engines, and `DockerContextProbe`
        // opens this user's config. `ReachesThisEngine` is a pure function of a host string and is
        // deliberately still allowed — the name below is the read, not the whole class.
        Assert.DoesNotContain("new WindowsMachineFacts(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DockerContextProbe.Read(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_machine_a_measurement_hands_over_is_the_one_the_verbs_use()
    {
        // The other half of the guard. Forbidding construction is worth nothing if the seam is not
        // reachable: every property has to be settable by a caller, or a measurement cannot
        // substitute for it and the verbs would have to build their own after all.
        var settable = typeof(MachineReads)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .ToList();

        Assert.NotEmpty(settable);
        foreach (var property in settable)
        {
            Assert.True(
                property.CanWrite,
                $"MachineReads.{property.Name} cannot be set, so nothing can stand in for it");
            Assert.True(
                property.PropertyType.IsInterface,
                $"MachineReads.{property.Name} is a concrete {property.PropertyType.Name}, "
                + "so a measurement gets the real machine whatever it passes");
        }
    }
}
