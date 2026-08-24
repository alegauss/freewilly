using System.Text;
using System.Text.Json;
using FreeWilly.Core.Agent;
using FreeWilly.Core.Api;
using FreeWilly.Tray.Cli;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// What the canonical task costs an agent, measured rather than argued (DD23).
/// </summary>
/// <remarks>
/// The constitution in <c>docs/specs/DD23-agent-first-freewilly.md</c> argues its whole design from
/// an accounting table whose every figure is an estimate. This is the benchmark that replaces the read
/// half of those figures with a number, and <c>agent-budget.json</c> is the ceiling it reads, so a
/// build that made a response more expensive fails instead of mentioning it.
///
/// <para><b>Where the numbers come from.</b> Fixtures served over a real named pipe through the real
/// client. No engine was answering either pipe on the machine this was written on, and a measurement
/// that needs a running Docker cannot gate a build at all — <c>check.yml</c> has no Docker. So these
/// are honest about being the documented *shape* at a realistic size, and the sizes are banded in both
/// directions: a fixture somebody shrinks makes the baseline look cheaper, which is the same defect as
/// a surface that grew.</para>
///
/// <para><b>Not a performance suite.</b> Nothing here times anything.</para>
/// </remarks>
[Collection(ConsoleCollection.Name)]
public sealed class AgentBudgetTests
{
    private static string Path(string endpoint) => $"/{DockerApi.ApiVersion}/{endpoint}";

    // ---- the budget file ----------------------------------------------------------------------

    /// <summary>The budget, found by walking up from the test binary.</summary>
    private static JsonDocument Budget()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null)
        {
            var candidate = System.IO.Path.Combine(here.FullName, "agent-budget.json");
            if (File.Exists(candidate))
            {
                return JsonDocument.Parse(File.ReadAllBytes(candidate));
            }

            here = here.Parent;
        }

        throw new InvalidOperationException(
            "agent-budget.json was not found above " + AppContext.BaseDirectory
            + " — the ceiling this test enforces is the file, so its absence is a failure and not a skip");
    }

    // ---- the fixtures, and what they are ------------------------------------------------------

    /// <summary>Six containers, the shape <c>/containers/json</c> documents.</summary>
    /// <remarks>
    /// A stack with an api, a database, a cache, a worker and two sidecars — the list the canonical
    /// task re-reads as state moves. Ports and labels included because they are what makes the real
    /// response wide.
    /// </remarks>
    private static readonly (string Name, string Image, string State, string Status, string Port)[]
        Services =
    [
        ("api", "shop/api:latest", "exited", "Exited (137) 12 seconds ago", "8080"),
        ("db", "postgres:16-alpine", "running", "Up 4 minutes (healthy)", "5432"),
        ("cache", "redis:7-alpine", "running", "Up 4 minutes", "6379"),
        ("worker", "shop/worker:latest", "running", "Up 4 minutes", ""),
        ("mailhog", "mailhog/mailhog:v1.0.1", "running", "Up 4 minutes", "8025"),
        ("proxy", "traefik:v3.1", "running", "Up 4 minutes", "80"),
    ];

    /// <summary>The id the list gives the container at <paramref name="index"/>.</summary>
    /// <remarks>
    /// Derived rather than pasted, because the shaped side routes by id: every verb there resolves
    /// the name through the list first, so a fixture whose ids are written twice is one where the
    /// two copies can drift into a 404 that reads like a missing container.
    /// </remarks>
    private static string ContainerId(int index) =>
        new string((char)('a' + index), 8) + index
        + "3f9c1b7e2d4a6c8f0b1d3e5a7c9f1b3d5e7a9c1f3b5d7e9a";

    private static string ContainerListJson()
    {
        var services = Services;

        // A template with markers rather than an interpolated raw string: JSON is mostly braces, and
        // an interpolated literal cannot tell a doubled brace it should print from one it should read.
        const string one = """
            {"Id":"@ID@","Names":["/shop-@NAME@-1"],"Image":"@IMAGE@",
            "ImageID":"sha256:@IMAGEID@","Command":"/entrypoint.sh serve","Created":1755000000,
            "Ports":@PORTS@,
            "Labels":{"com.docker.compose.project":"shop","com.docker.compose.service":"@NAME@","com.docker.compose.config-hash":"@HASH@","com.docker.compose.container-number":"1","com.docker.compose.oneoff":"False","com.docker.compose.version":"2.29.1"},
            "State":"@STATE@","Status":"@STATUS@","HostConfig":{"NetworkMode":"shop_default"},
            "NetworkSettings":{"Networks":{"shop_default":{"IPAMConfig":null,"Links":null,"Aliases":null,"NetworkID":"@NETID@","EndpointID":"@EPID@","Gateway":"172.19.0.1","IPAddress":"172.19.0.@OCTET@","IPPrefixLen":16,"MacAddress":"02:42:ac:13:00:0@INDEX@"}}},
            "Mounts":[{"Type":"volume","Name":"shop_@NAME@_data","Source":"/var/lib/docker/volumes/shop_@NAME@_data/_data","Destination":"/data","Driver":"local","Mode":"z","RW":true,"Propagation":""}]}
            """;

        var json = new StringBuilder("[");
        for (var i = 0; i < services.Length; i++)
        {
            var (name, image, state, status, port) = services[i];
            var ports = port.Length == 0
                ? "[]"
                : "[{\"IP\":\"0.0.0.0\",\"PrivatePort\":" + port + ",\"PublicPort\":" + port
                    + ",\"Type\":\"tcp\"},{\"IP\":\"::\",\"PrivatePort\":" + port
                    + ",\"PublicPort\":" + port + ",\"Type\":\"tcp\"}]";

            json.Append(i == 0 ? "" : ",")
                .Append(one
                    .ReplaceLineEndings("")
                    .Replace("@ID@", ContainerId(i))
                    .Replace("@IMAGEID@", new string((char)('1' + i), 12) + "9f2c4a6e8b0d2f4a6c8e0b2d4f6a8c0e2b4d6f8a0c2e4b6d")
                    .Replace("@HASH@", new string((char)('a' + i), 64))
                    .Replace("@NETID@", "n" + i + "c4a6e8b0d2f4a6c8e0b2d4f6a8c0e2b4d6f8a0c2e4b6d8f")
                    .Replace("@EPID@", "e" + i + "a6c8e0b2d4f6a8c0e2b4d6f8a0c2e4b6d8f0a2c4e6b8d0f")
                    .Replace("@PORTS@", ports)
                    .Replace("@IMAGE@", image)
                    .Replace("@STATE@", state)
                    .Replace("@STATUS@", status)
                    .Replace("@OCTET@", (i + 2).ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Replace("@INDEX@", i.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Replace("@NAME@", name));
        }

        return json.Append(']').ToString();
    }

    /// <summary>
    /// One container's full inspect, of which the canonical task reads four fields.
    /// </summary>
    /// <remarks>
    /// The constitution's table says 300–600 lines of JSON for
    /// <c>State.ExitCode</c>, <c>OOMKilled</c>, <c>PortBindings</c> and <c>Mounts</c>. This carries all
    /// the sections a real inspect carries, because the point being measured is that the whole entity
    /// tree is paid for to read four of its leaves.
    /// </remarks>
    private static string InspectJson()
    {
        var env = string.Join(",", Enumerable.Range(0, 18).Select(i =>
            $"\"SHOP_SETTING_{i:D2}=some-reasonably-long-configuration-value-{i}\""));
        var labels = string.Join(",", Enumerable.Range(0, 8).Select(i =>
            $"\"com.example.label.{i}\":\"value-{i}-with-some-length-to-it\""));

        return """
            {"Id":"a1b2c3d4e5f60000000000000000000000000000000000000000000000000000",
            "Created":"2026-08-13T09:12:44.123456789Z","Path":"/entrypoint.sh","Args":["serve","--port","8080"],
            "State":{"Status":"exited","Running":false,"Paused":false,"Restarting":false,"OOMKilled":true,"Dead":false,"Pid":0,"ExitCode":137,"Error":"","StartedAt":"2026-08-13T09:12:45.000000000Z","FinishedAt":"2026-08-13T09:16:02.000000000Z","Health":{"Status":"unhealthy","FailingStreak":3,"Log":[{"Start":"2026-08-13T09:15:00Z","End":"2026-08-13T09:15:01Z","ExitCode":1,"Output":"curl: (7) Failed to connect to localhost port 8080"},{"Start":"2026-08-13T09:15:30Z","End":"2026-08-13T09:15:31Z","ExitCode":1,"Output":"curl: (7) Failed to connect to localhost port 8080"},{"Start":"2026-08-13T09:16:00Z","End":"2026-08-13T09:16:01Z","ExitCode":1,"Output":"curl: (7) Failed to connect to localhost port 8080"}]}},
            "Image":"sha256:cafebabe00000000000000000000000000000000000000000000000000000000",
            "ResolvConfPath":"/var/lib/docker/containers/a1b2c3d4e5f6/resolv.conf",
            "HostnamePath":"/var/lib/docker/containers/a1b2c3d4e5f6/hostname",
            "HostsPath":"/var/lib/docker/containers/a1b2c3d4e5f6/hosts",
            "LogPath":"/var/lib/docker/containers/a1b2c3d4e5f6/a1b2c3d4e5f6-json.log",
            "Name":"/shop-api-1","RestartCount":3,"Driver":"overlay2","Platform":"linux","MountLabel":"","ProcessLabel":"","AppArmorProfile":"",
            "ExecIDs":null,
            "HostConfig":{"Binds":["/c/Users/dev/shop/api:/app:rw"],"ContainerIDFile":"","LogConfig":{"Type":"json-file","Config":{}},"NetworkMode":"shop_default","PortBindings":{"8080/tcp":[{"HostIp":"0.0.0.0","HostPort":"8080"},{"HostIp":"::","HostPort":"8080"}]},"RestartPolicy":{"Name":"unless-stopped","MaximumRetryCount":0},"AutoRemove":false,"VolumeDriver":"","VolumesFrom":[],"CapAdd":null,"CapDrop":null,"CgroupnsMode":"private","Dns":[],"DnsOptions":[],"DnsSearch":[],"ExtraHosts":[],"GroupAdd":null,"IpcMode":"private","Cgroup":"","Links":null,"OomScoreAdj":0,"PidMode":"","Privileged":false,"PublishAllPorts":false,"ReadonlyRootfs":false,"SecurityOpt":null,"UTSMode":"","UsernsMode":"","ShmSize":67108864,"Runtime":"runc","Isolation":"","CpuShares":0,"Memory":536870912,"NanoCpus":0,"CgroupParent":"","BlkioWeight":0,"BlkioWeightDevice":[],"BlkioDeviceReadBps":[],"BlkioDeviceWriteBps":[],"BlkioDeviceReadIOps":[],"BlkioDeviceWriteIOps":[],"CpuPeriod":0,"CpuQuota":0,"CpuRealtimePeriod":0,"CpuRealtimeRuntime":0,"CpusetCpus":"","CpusetMems":"","Devices":[],"DeviceCgroupRules":null,"DeviceRequests":null,"MemoryReservation":0,"MemorySwap":1073741824,"MemorySwappiness":null,"OomKillDisable":false,"PidsLimit":null,"Ulimits":[],"CpuCount":0,"CpuPercent":0,"IOMaximumIOps":0,"IOMaximumBandwidth":0,"MaskedPaths":["/proc/asound","/proc/acpi","/proc/kcore","/proc/keys","/proc/latency_stats","/proc/timer_list","/proc/timer_stats","/proc/sched_debug","/proc/scsi","/sys/firmware","/sys/devices/virtual/powercap"],"ReadonlyPaths":["/proc/bus","/proc/fs","/proc/irq","/proc/sys","/proc/sysrq-trigger"]},
            "GraphDriver":{"Data":{"LowerDir":"/var/lib/docker/overlay2/l/AAAA:/var/lib/docker/overlay2/l/BBBB:/var/lib/docker/overlay2/l/CCCC","MergedDir":"/var/lib/docker/overlay2/abc/merged","UpperDir":"/var/lib/docker/overlay2/abc/diff","WorkDir":"/var/lib/docker/overlay2/abc/work"},"Name":"overlay2"},
            "Mounts":[{"Type":"bind","Source":"/c/Users/dev/shop/api","Destination":"/app","Mode":"rw","RW":true,"Propagation":"rprivate"},{"Type":"volume","Name":"shop_api_data","Source":"/var/lib/docker/volumes/shop_api_data/_data","Destination":"/data","Driver":"local","Mode":"z","RW":true,"Propagation":""}],
            "Config":{"Hostname":"a1b2c3d4e5f6","Domainname":"","User":"1000:1000","AttachStdin":false,"AttachStdout":true,"AttachStderr":true,"ExposedPorts":{"8080/tcp":{}},"Tty":false,"OpenStdin":false,"StdinOnce":false,"Env":[@ENV@],"Cmd":["serve","--port","8080"],"Healthcheck":{"Test":["CMD-SHELL","curl -fsS http://localhost:8080/health || exit 1"],"Interval":30000000000,"Timeout":5000000000,"Retries":3},"Image":"shop/api:latest","Volumes":null,"WorkingDir":"/app","Entrypoint":["/entrypoint.sh"],"OnBuild":null,"Labels":{@LABELS@},"StopSignal":"SIGTERM","StopTimeout":10},
            "NetworkSettings":{"Bridge":"","SandboxID":"s1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b1c2d3e4","HairpinMode":false,"LinkLocalIPv6Address":"","LinkLocalIPv6PrefixLen":0,"Ports":{},"SandboxKey":"/var/run/docker/netns/s1a2b3c4d5e6","SecondaryIPAddresses":null,"SecondaryIPv6Addresses":null,"EndpointID":"","Gateway":"","GlobalIPv6Address":"","GlobalIPv6PrefixLen":0,"IPAddress":"","IPPrefixLen":0,"IPv6Gateway":"","MacAddress":"","Networks":{"shop_default":{"IPAMConfig":null,"Links":null,"Aliases":["api","shop-api-1"],"NetworkID":"n1c4a6e8b0d2f4a6c8e0b2d4f6a8c0e2b4d6f8a0c2e4b6d8f","EndpointID":"","Gateway":"","IPAddress":"","IPPrefixLen":0,"IPv6Gateway":"","GlobalIPv6Address":"","GlobalIPv6PrefixLen":0,"MacAddress":"","DriverOpts":null}}}}
            """.ReplaceLineEndings("").Replace("@ENV@", env).Replace("@LABELS@", labels);
    }

    /// <summary>
    /// A tail of logs from a restart loop, carrying the same trace forty times.
    /// </summary>
    /// <remarks>
    /// Forty is the constitution's own number for this: no dedup, no cursor, no level filter and no
    /// ceiling, so the cost is the size of the file rather than of the answer. Built by repetition
    /// rather than pasted, so what makes it expensive is visible in the code that makes it.
    /// </remarks>
    private static string LogsPayload()
    {
        const string trace = """
            2026-08-13T09:16:01.884Z ERROR [main] shop.api.Bootstrap - failed to bind :8080
            java.net.BindException: Address already in use
            	at java.base/sun.nio.ch.Net.bind0(Native Method)
            	at java.base/sun.nio.ch.Net.bind(Net.java:565)
            	at java.base/sun.nio.ch.ServerSocketChannelImpl.netBind(ServerSocketChannelImpl.java:344)
            	at shop.api.http.Server.start(Server.java:88)
            	at shop.api.Bootstrap.main(Bootstrap.java:41)

            """;
        return string.Concat(Enumerable.Repeat(trace, 40));
    }

    /// <summary>The network read, the last question before the one Docker cannot answer at all.</summary>
    private static string NetworkJson() => """
        {"Name":"shop_default","Id":"n1c4a6e8b0d2f4a6c8e0b2d4f6a8c0e2b4d6f8a0c2e4b6d8f","Created":"2026-08-13T09:12:40.1Z","Scope":"local","Driver":"bridge","EnableIPv6":false,
        "IPAM":{"Driver":"default","Options":null,"Config":[{"Subnet":"172.19.0.0/16","Gateway":"172.19.0.1"}]},
        "Internal":false,"Attachable":true,"Ingress":false,"ConfigFrom":{"Network":""},"ConfigOnly":false,
        "Containers":{"a1b2c3d4e5f6":{"Name":"shop-api-1","EndpointID":"e1","MacAddress":"02:42:ac:13:00:02","IPv4Address":"172.19.0.2/16","IPv6Address":""},"b2c3d4e5f6a1":{"Name":"shop-db-1","EndpointID":"e2","MacAddress":"02:42:ac:13:00:03","IPv4Address":"172.19.0.3/16","IPv6Address":""}},
        "Options":{"com.docker.network.bridge.default_bridge":"false","com.docker.network.driver.mtu":"1500"},
        "Labels":{"com.docker.compose.network":"default","com.docker.compose.project":"shop","com.docker.compose.version":"2.29.1"}}
        """.Replace("\r\n", "").Replace("\n", "");

    // ---- the measurement ----------------------------------------------------------------------

    /// <summary>Drive the canonical task and return what it cost.</summary>
    private static async Task<(AgentCost Cost, int Served)> MeasureCanonicalTaskAsync()
    {
        var list = ContainerListJson();
        var inspect = InspectJson();
        var logs = LogsPayload();
        var network = NetworkJson();

        await using var daemon = new FakeDockerDaemon()
            .Json(Path("containers/json?all=1"), list)
            .Json(Path("containers/shop-api-1/json"), inspect)
            .Raw(Path("containers/shop-api-1/logs?stdout=1&stderr=1&tail=200"),
                "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: "
                + Encoding.UTF8.GetByteCount(logs) + "\r\n\r\n" + logs)
            .Json(Path("networks/shop_default"), network);

        using var api = new DockerApi(daemon.PipeName);

        var paid = new List<string>();

        // Learn the state, three times, because a truncating table with no cursor is re-read as
        // state moves. The constitution's table says three to five.
        for (var i = 0; i < 3; i++)
        {
            _ = await api.ContainersAsync();
            paid.Add(list);
        }

        // Diagnose: the whole entity tree, for four of its leaves.
        await using (var stream = await api.StreamAsync("containers/shop-api-1/json"))
        {
            paid.Add(await new StreamReader(stream).ReadToEndAsync());
        }

        // The log, unbounded except by --tail.
        await using (var stream = await api.StreamAsync("containers/shop-api-1/logs?stdout=1&stderr=1&tail=200"))
        {
            paid.Add(await new StreamReader(stream).ReadToEndAsync());
        }

        // Confirm the network, which still does not answer whether the host port listens.
        await using (var stream = await api.StreamAsync("networks/shop_default"))
        {
            paid.Add(await new StreamReader(stream).ReadToEndAsync());
        }

        return (TokenEstimate.OfAll(paid), daemon.Requested.Count);
    }

    // ---- the same task, through the surface that was built to be cheaper ------------------------

    /// <summary>The container the whole task is about, as the surface has to name it.</summary>
    private const string Api = "shop-api-1";

    /// <summary>What a caller types, in the order the canonical task is worked through.</summary>
    /// <remarks>
    /// Four verbs against the baseline's six reads, and they are not the same four questions rewritten
    /// — that is the thing the ratio must not be allowed to imply. <c>read doctor</c> joins the list,
    /// the inspect and a tail of stderr into a verdict, so it answers in one call what the baseline
    /// answers by handing over three payloads and leaving the joining to the reader. <c>read verify</c>
    /// stands where the baseline's network read stood and answers a strictly larger question: whether
    /// the host port accepts, which <c>/networks/…</c> cannot say at all.
    /// </remarks>
    private static readonly string[][] ShapedTask =
    [
        ["read", "context"],
        ["read", "doctor", Api],
        ["read", "logs", Api],
        ["read", "verify", Api],
    ];

    /// <summary>
    /// The daemon the shaped side is driven against, carrying the baseline's own fixtures.
    /// </summary>
    /// <remarks>
    /// Same list, same inspect, same log bytes. The routes differ because the verbs ask differently:
    /// they resolve a name through the list and then address the container by id, and they read the
    /// log framed, with timestamps and a deeper tail. The engine also answers <c>_ping</c>, the
    /// version, images and volumes here, because <c>read context</c> states the whole machine — those
    /// requests are the surface's, not the fixtures'.
    /// </remarks>
    private static FakeDockerDaemon ShapedDaemon()
    {
        var id = ContainerId(0);
        return new FakeDockerDaemon()
            .Fails(Path("_ping"), "200 OK", "OK")
            .Json(Path("version"), """{"Version":"29.7.2","ApiVersion":"1.55","MinAPIVersion":"1.24","Os":"linux","Arch":"amd64"}""")
            .Json(Path("containers/json?all=1"), ContainerListJson())
            .Json(Path("images/json?all=0"), "[]")
            .Json(Path("volumes"), """{"Volumes":[]}""")
            .Json(Path($"containers/{id}/json"), InspectJson())
            .Raw(
                Path($"containers/{id}/logs?stdout=1&stderr=1&tail=200&follow=0&timestamps=0"),
                FramedLog())
            .Raw(
                Path($"containers/{id}/logs?stdout=1&stderr=1&tail=2000&follow=0&timestamps=1"),
                FramedLog());
    }

    /// <summary>The log fixture as the daemon actually sends it: one multiplexed stdout frame.</summary>
    /// <remarks>
    /// A header of a stream byte, three zeroes and a big-endian length, which is why this is bytes and
    /// not a string — no encoder round-trips an arbitrary length. Framing it rather than reframing the
    /// fixture keeps both sides paying for the same log.
    /// </remarks>
    private static byte[] FramedLog()
    {
        var body = Encoding.UTF8.GetBytes(LogsPayload());
        var frame = new byte[LogFrames.HeaderSize + body.Length];
        frame[0] = 1;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(4), body.Length);
        body.CopyTo(frame.AsSpan(LogFrames.HeaderSize));

        var head = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: application/vnd.docker.raw-stream\r\n"
            + $"Content-Length: {frame.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}\r\n\r\n");

        return [.. head, .. frame];
    }

    /// <summary>
    /// Everything the shaped task reads off Windows, as fixtures (DD78).
    /// </summary>
    /// <remarks>
    /// The whole reason the shaped figure could not be exact. <c>read context</c> named the CLI's
    /// context from this machine's own <c>.docker/config.json</c> and <c>read doctor</c> asked Windows
    /// whether anything held port 8080, so the same build measured differently on a developer's
    /// machine and on a runner with no Docker on it.
    ///
    /// <para>Chosen to agree with the fixtures rather than to be convenient. The pack states the
    /// engine running, so the CLI points at <c>default</c> and reaches it; the container the task is
    /// about was OOM-killed and exited, so nothing on the host holds its published port.</para>
    ///
    /// <para>The clock is the fourth (DD178), and it was the one left because it is not a read of
    /// Windows: <c>read doctor</c>'s restarts row states how long ago the container last started, so
    /// with <c>UtcNow</c> behind it the width of that string was a function of the day the build ran.
    /// It went from <c>9d</c> to <c>10d</c> eleven days after the fixture was written and took the
    /// exact figure with it, on a tree nobody had touched — which is the same defect as a response
    /// that grew, arriving without a commit to blame.</para>
    /// </remarks>
    private static MachineReads FixedMachine() => new()
    {
        Ports = new NoListeners(),
        Client = new FixedContext(),
        Service = new NeverProbed(),
        Sources = new FixedSource(),
        Clock = new FixedClock(),
    };

    /// <summary>When the shaped task is read, so a span is the fixture's and not the calendar's.</summary>
    /// <remarks>
    /// Three days after the inspect fixture's <c>StartedAt</c>, and both halves of that are chosen.
    /// Days, because the restarts row it feeds turns <c>Fail</c> at three restarts inside ten minutes
    /// and this fixture has exactly three — a pin nearer the crash would rewrite the row, its remedy
    /// and the verdict, which is a different payload rather than a fixed one. Three of them, because
    /// <c>3d</c> is the width the recorded figure was measured at, so this pins the number rather than
    /// moving it: the diff is a seam and not a raise.
    /// </remarks>
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset Now() => new(2026, 8, 16, 9, 12, 45, TimeSpan.Zero);
    }

    private sealed class NoListeners : IHostPorts
    {
        public IReadOnlySet<int> Listening() => new HashSet<int>();
    }

    private sealed class FixedContext : IContextProbe
    {
        public Core.Preflight.DockerClientTarget Read() => new()
        {
            ContextName = Core.Preflight.Windows.DockerContextProbe.DefaultContextName,
            Host = "npipe:////./pipe/" + DockerApi.DefaultPipeName,
            FromEnvironment = false,
        };
    }

    /// <summary>
    /// The probe the shaped task must not reach.
    /// </summary>
    /// <remarks>
    /// An assertion and not a stub. <c>read verify</c> probes nothing for a container that is not
    /// running, and this fixture's is exited — so a build where that stopped being true would start
    /// connecting to whatever holds 8080 on the measuring machine, which is the class of input this
    /// task exists to take out of the figure.
    /// </remarks>
    /// <summary>
    /// What the distribution says about the shaped task's one bind source (DD101).
    /// </summary>
    /// <remarks>
    /// A fixture rather than an assertion, because the shaped task genuinely reaches this: its
    /// container's bind source is <c>/c/Users/dev/shop/api</c>, which is the case DD96 named as the
    /// one nothing about the string can settle, and DD101 is exactly the read that settles it. So the
    /// question is not whether the measured task asks, but that the answer is the fixture's and never
    /// this machine's — a real read would run <c>wsl.exe</c> here and the figure would drift with
    /// whatever distribution the measuring machine has.
    ///
    /// <para><c>Missing</c> because that is what the fixture describes: a Git-Bash spelling of a
    /// Windows folder, typed at a prompt, reaching a daemon that has no such path and creates it.
    /// Choosing the cheapest answer instead would measure a row this task rarely produces.</para>
    /// </remarks>
    private sealed class FixedSource : IBindSources
    {
        public BindSource Look(string source) => BindSource.Missing;
    }

    private sealed class NeverProbed : IServiceProbe
    {
        public PortAnswer Connect(int hostPort, string containerPort, TimeSpan timeout) =>
            throw new InvalidOperationException(
                "the shaped task probed a host port, so its token figure is this machine's again");

        public RequestAnswer Get(int hostPort, string path, TimeSpan timeout) =>
            throw new InvalidOperationException(
                "the shaped task made an HTTP request, so its token figure is this machine's again");
    }

    /// <summary>Drive the same task through the surface and return what it cost.</summary>
    /// <remarks>
    /// A call here is a verb invocation, which is what the caller pays for — not a request the daemon
    /// received. The two coincide on the baseline, where the agent is the one making every request,
    /// and they do not here: the requests behind a verb are made by this process and cost the reader
    /// nothing. <paramref name="Served"/> is reported anyway, so a verb that started re-reading the
    /// machine is visible even while the payload it prints stays the same size.
    /// </remarks>
    private static async Task<(AgentCost Cost, int Served)> MeasureShapedTaskAsync()
    {
        await using var daemon = ShapedDaemon();
        using var engine = new DockerApi(daemon.PipeName);

        var paid = new List<string>();
        foreach (var call in ShapedTask)
        {
            var output = new StringWriter();
            AgentSurface.Read(AgentSurface.Find(call)!, engine, call[2..], output, FixedMachine());
            paid.Add(output.ToString());
        }

        return (TokenEstimate.OfAll(paid), daemon.Requested.Count);
    }

    [Fact]
    public async Task The_canonical_task_costs_what_the_budget_records()
    {
        var (cost, served) = await MeasureCanonicalTaskAsync();

        using var budget = Budget();
        var baseline = budget.RootElement.GetProperty("baseline").GetProperty("measured");
        var expectedCalls = baseline.GetProperty("calls").GetInt32();
        var expectedTokens = baseline.GetProperty("tokens").GetInt32();
        var tolerance = budget.RootElement.GetProperty("fixtures")
            .GetProperty("sizes").GetProperty("tolerance").GetDouble();

        Assert.Equal(expectedCalls, cost.Calls);
        Assert.Equal(cost.Calls, served);

        // Banded in both directions. A response that grew is the defect this file exists for; a
        // fixture that shrank is the same defect wearing the opposite sign, because it makes the
        // baseline the surface is judged against quietly cheaper.
        var low = (int)(expectedTokens * (1 - tolerance));
        var high = (int)(expectedTokens * (1 + tolerance));
        Assert.True(
            cost.Tokens >= low && cost.Tokens <= high,
            $"the canonical task now estimates {cost.Tokens} tokens, outside the recorded "
            + $"{expectedTokens} +/-{tolerance:P0} ({low}..{high}). If this is deliberate, raise it in "
            + "agent-budget.json and say in the commit what the tokens bought.");
    }

    [Fact]
    public async Task Re_discovery_is_the_largest_driver_and_not_the_inspect()
    {
        // Written the other way round first, asserting the constitution's own emphasis, and the
        // measurement falsified it. Recorded rather than quietly corrected, because falsifying an
        // estimate is what DD23 is for.
        //
        // Measured over the Engine API, 11711 estimated tokens for six calls:
        //   three list reads   5718   48.8%   <- the largest driver
        //   the log tail       4170   35.6%
        //   the whole inspect  1603   13.7%
        //   the network         220    1.9%
        // So re-discovery leads, and it is not a majority on its own - stated as the two comparisons
        // that are actually true rather than as "most of it", which it is not.
        //
        // The caveat that keeps this honest: `docker inspect` PRINTS indented JSON - the 300 to 600
        // lines the constitution's table counts - while the API answers compactly, so an agent going
        // through the CLI pays several times this for the same entity. What is measured here is the
        // transport this project's own surface is built on, which makes it a floor for today's cost
        // and not a ceiling.
        var (cost, _) = await MeasureCanonicalTaskAsync();
        var list = TokenEstimate.Of(ContainerListJson());
        var inspect = TokenEstimate.Of(InspectJson());
        var logs = TokenEstimate.Of(LogsPayload());

        Assert.True(
            list * 3 > logs,
            $"three list reads ({list * 3}) are no longer the largest driver against the log tail "
            + $"({logs}) - if the list got cheaper or a read was removed, DD25's cursor is worth "
            + "less than this recorded");
        Assert.True(
            inspect < list,
            $"one inspect ({inspect}) is now larger than one list read ({list}), which is the "
            + "emphasis the constitution assumed and this measurement did not find");
        Assert.Equal(cost.Tokens, (list * 3) + inspect + logs + TokenEstimate.Of(NetworkJson()));
    }

    // ---- the case a well-formed benchmark cannot see -------------------------------------------

    [Theory]
    [InlineData("--nonsense", "--preflight", "--nonsense")]
    [InlineData("--nonsense", "--plan", "--nonsense")]
    [InlineData("--nonsense", "--status", "--nonsense")]
    [InlineData("nonsense", "--autostart", "nonsense")]
    [InlineData("--nonsense", "--capture-window", "out.png", "Containers", "--nonsense")]
    public void An_unknown_argument_is_refused_rather_than_dropped(string offending, params string[] args)
    {
        // The expensive case, and the reason this file does not only measure a well-formed script: a
        // refusal costs one round trip, and an argument silently dropped costs a wrong outcome nobody
        // notices.
        //
        // Asserted on the usage code and on the argument being NAMED, and both are load-bearing. The
        // first version of this asked only for a non-zero exit and some output, and a verb that
        // ignored the flag and did its normal work satisfied it: on a machine with a rival engine
        // installed the preflight exits 1 and prints a full report, so a silent drop passed. Verified
        // by making the preflight drop the argument on purpose - the weak assertion stayed green.
        var (code, output) = RunVerb(args);

        Assert.Equal(2, code);
        Assert.Contains(offending, output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refusal_is_cheap_as_well_as_loud()
    {
        using var budget = Budget();
        var refusal = budget.RootElement.GetProperty("refusal");
        var maxTokens = refusal.GetProperty("maxTokens").GetInt32();

        var (_, output) = RunVerb(["--preflight", "--nonsense"]);
        var cost = TokenEstimate.Of(output);

        Assert.True(
            cost <= maxTokens,
            $"the refusal estimates {cost} tokens against a ceiling of {maxTokens}. A refusal that "
            + "costs as much as an answer is not a cheap round trip.");
    }

    /// <summary>Run one verb in this process and collect what it wrote.</summary>
    private static (int Code, string Output) RunVerb(string[] args)
    {
        var captured = new StringWriter();
        var wasOut = Console.Out;
        var wasError = Console.Error;
        try
        {
            Console.SetOut(captured);
            Console.SetError(captured);
            var route = CommandLine.Of(args);
            var code = route.Surface switch
            {
                Surface.Preflight => PreflightCommand.Run(route.Arguments),
                Surface.Engine => EngineCommand.Run(route.Arguments),
                Surface.CaptureWindow => WindowCapture.Run(route.Arguments),
                // Anything the router itself refuses is already the cheapest possible refusal.
                _ => 2,
            };
            return (code, captured.ToString());
        }
        finally
        {
            Console.SetOut(wasOut);
            Console.SetError(wasError);
        }
    }

    // ---- what the file itself has to say ------------------------------------------------------

    [Fact]
    public async Task The_shaped_surface_costs_what_the_budget_records()
    {
        // The file said `exists: false` for eight tasks, and refusing to invent a number was right
        // for every one of them (DD65). It is measurable now, so the refusal is replaced by the
        // measurement and not by a claim.
        var (cost, served) = await MeasureShapedTaskAsync();

        using var budget = Budget();
        var surface = budget.RootElement.GetProperty("surface");
        Assert.True(surface.GetProperty("exists").GetBoolean());

        var measured = surface.GetProperty("measured");
        Assert.Equal(measured.GetProperty("calls").GetInt32(), cost.Calls);

        // Exact, because the fixtures fully determine it: this is the figure that goes red when a
        // verb quietly starts asking the daemon something it did not ask before.
        Assert.Equal(measured.GetProperty("requests").GetInt32(), served);

        // Exact since DD78, and that is the whole of it. This was banded at the file's own 15% for
        // two inputs read off Windows rather than off a fixture — against a measured variance of
        // about 5%, so a response that grew by 100 tokens landed inside the band and the gate said
        // nothing. The seam removed them, and what is left is fully determined by this file.
        var recorded = measured.GetProperty("tokens").GetInt32();
        Assert.True(
            cost.Tokens == recorded,
            $"the shaped task now estimates {cost.Tokens} tokens against the recorded {recorded}. "
            + "If this is deliberate, record it in agent-budget.json and say in the commit what the "
            + "tokens bought.");
    }

    [Fact]
    public async Task Every_recorded_figure_is_one_the_measurement_produced()
    {
        // DD147, and DD144 is why. A figure here can be typed, and one that was cost this project a
        // gate that was red on a clean checkout for twelve commits — an exact assertion binds the
        // recorded number tightly to the measurement, but only once somebody has written down a
        // number the measurement produced.
        //
        // So the whole block is checked against one run, and the same run can write it:
        //
        //     FREEWILLY_RECORD_BUDGET=1 dotnet test --filter AgentBudgetTests
        //
        // which leaves a diff to read rather than seven integers to transcribe. Asked for and never
        // automatic: a run that rewrote the file whenever it disagreed would not be a gate at all.
        var (canonical, canonicalServed) = await MeasureCanonicalTaskAsync();
        var (shaped, shapedServed) = await MeasureShapedTaskAsync();

        BudgetFile.Figure[] figures =
        [
            new("baseline", "measured", "calls", BudgetFile.Number(canonical.Calls)),
            new("surface", "measured", "calls", BudgetFile.Number(shaped.Calls)),
            new("surface", "measured", "requests", BudgetFile.Number(shapedServed)),
            new("surface", "measured", "tokens", BudgetFile.Number(shaped.Tokens)),

            // Derived rather than measured, and recorded because a reader should not need a
            // calculator — which is exactly what makes it a third number that can drift.
            new("surface", "ratio", "calls",
                BudgetFile.Ratio((double)canonical.Calls / shaped.Calls)),
            new("surface", "ratio", "tokens",
                BudgetFile.Ratio((double)canonical.Tokens / shaped.Tokens)),
        ];

        // The baseline's tokens are deliberately absent: that one is banded, because it moves with
        // the fixtures rather than with the surface, and writing it from a run would silently
        // ratify a fixture somebody had shrunk.
        Assert.Equal(canonical.Calls, canonicalServed);

        if (BudgetFile.Recording)
        {
            var moved = BudgetFile.Record(figures);
            Assert.Fail(
                moved.Count == 0
                    ? $"{BudgetFile.RecordVariable} was set and every figure already agreed, so "
                      + "nothing was written. Unset it and the suite is green."
                    : $"{BudgetFile.RecordVariable} was set, so agent-budget.json was rewritten:"
                      + Environment.NewLine
                      + string.Join(Environment.NewLine, moved.Select(line => "  " + line))
                      + Environment.NewLine
                      + "Read the diff, say in the commit what the tokens bought, and run again "
                      + "without the variable.");
        }

        // Not recording, so the file has to already say what this run measured. Reported together
        // rather than one at a time: a figure fixed while the next was still wrong is how DD144
        // stayed unnoticed through eleven commits.
        using var budget = Budget();
        var wrong = new List<string>();
        foreach (var figure in figures)
        {
            var recorded = budget.RootElement
                .GetProperty(figure.Section)
                .GetProperty(figure.Block)
                .GetProperty(figure.Key)
                .GetRawText();

            if (recorded != figure.Value)
            {
                wrong.Add($"  {figure.Section}.{figure.Block}.{figure.Key}: "
                    + $"recorded {recorded}, measured {figure.Value}");
            }
        }

        Assert.True(
            wrong.Count == 0,
            "agent-budget.json records figures this run did not produce:" + Environment.NewLine
            + string.Join(Environment.NewLine, wrong) + Environment.NewLine
            + $"Set {BudgetFile.RecordVariable}=1 to write them, then say in the commit what "
            + "the tokens bought.");
    }

    [Fact]
    public async Task The_surface_meets_what_it_was_budgeted_for_before_it_existed()
    {
        // The acceptance criteria in the constitution's section 3.1, written into this file before
        // any verb did, and now answerable. Kept as an assertion rather than a note, because a
        // target nothing checks is a target that was met by being forgotten.
        var (cost, _) = await MeasureShapedTaskAsync();

        using var budget = Budget();
        var target = budget.RootElement.GetProperty("surface").GetProperty("target");

        Assert.True(
            cost.Calls <= target.GetProperty("calls").GetInt32(),
            $"the shaped task takes {cost.Calls} calls against a target of "
            + $"{target.GetProperty("calls").GetInt32()}");
        Assert.True(
            cost.Tokens <= target.GetProperty("tokens").GetInt32(),
            $"the shaped task estimates {cost.Tokens} tokens against a target of "
            + $"{target.GetProperty("tokens").GetInt32()}");
    }

    [Fact]
    public void The_recorded_ratio_is_the_two_recorded_measurements_and_not_a_third_number()
    {
        // The ratio is the thing DD23 built this file to produce, so it is written down rather than
        // left to a reader with a calculator - and the moment it is written down it is a third
        // number that can drift. This is what stops it: it is checked against the two it comes from,
        // so a measurement that moves takes the ratio with it or fails.
        using var budget = Budget();
        var baseline = budget.RootElement.GetProperty("baseline").GetProperty("measured");
        var surface = budget.RootElement.GetProperty("surface");
        var measured = surface.GetProperty("measured");
        var ratio = surface.GetProperty("ratio");

        Assert.Equal(
            Math.Round(
                (double)baseline.GetProperty("calls").GetInt32()
                / measured.GetProperty("calls").GetInt32(), 1),
            ratio.GetProperty("calls").GetDouble());
        Assert.Equal(
            Math.Round(
                (double)baseline.GetProperty("tokens").GetInt32()
                / measured.GetProperty("tokens").GetInt32(), 1),
            ratio.GetProperty("tokens").GetDouble());
    }

    [Fact]
    public void The_budget_states_its_method_and_where_its_fixtures_came_from()
    {
        // A ceiling whose unit is not written down is a number nobody can argue with, and these
        // numbers are an approximation that has to say so.
        using var budget = Budget();

        var method = budget.RootElement.GetProperty("method");
        Assert.Contains(
            TokenEstimate.CharactersPerToken.ToString(System.Globalization.CultureInfo.InvariantCulture),
            method.GetProperty("tokens").GetString()!,
            StringComparison.Ordinal);
        Assert.True(method.TryGetProperty("caveat", out _));
        Assert.True(
            budget.RootElement.GetProperty("fixtures").TryGetProperty("provenance", out _),
            "fixtures constructed rather than captured have to say so");
    }

    [Fact]
    public void Nothing_here_measures_time()
    {
        // Stated as an assertion because it is a law, not a preference: wall-clock is a different
        // question with a different answer, and a suite that mixes them is one where neither number
        // is trusted.
        using var budget = Budget();
        var raw = budget.RootElement.GetRawText();

        foreach (var forbidden in new[] { "\"ms\"", "millis", "seconds", "wallClock", "duration" })
        {
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---- the estimator ------------------------------------------------------------------------

    [Fact]
    public void An_estimate_rounds_up_so_a_payload_never_costs_nothing()
    {
        Assert.Equal(0, TokenEstimate.Of(""));
        Assert.Equal(0, TokenEstimate.Of(null));
        Assert.Equal(1, TokenEstimate.Of("a"));
        Assert.Equal(1, TokenEstimate.Of("abcd"));
        Assert.Equal(2, TokenEstimate.Of("abcde"));
    }

    [Fact]
    public void Costs_add_in_both_units()
    {
        var total = new AgentCost(1, 100) + new AgentCost(2, 50);

        Assert.Equal(3, total.Calls);
        Assert.Equal(150, total.Tokens);
    }

    [Fact]
    public void One_call_is_counted_per_payload() =>
        Assert.Equal(new AgentCost(3, 3), TokenEstimate.OfAll(["a", "b", "c"]));
}
