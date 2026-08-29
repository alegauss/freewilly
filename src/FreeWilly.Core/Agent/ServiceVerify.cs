using System.Globalization;
using FreeWilly.Core.Api;
using FreeWilly.Core.Engine;
using FreeWilly.Core.Preflight;

namespace FreeWilly.Core.Agent;

/// <summary>What a verify judges, gathered before any of it is judged.</summary>
/// <param name="Address">What the caller asked about.</param>
/// <param name="Summary">The container's row in the list, where it has one.</param>
/// <param name="Inspect">Its whole entity tree.</param>
/// <param name="Ports">What Windows found when it tried each published port.</param>
/// <param name="Request">What the service answered, where one was asked for.</param>
public sealed record VerifyFacts(
    Address Address,
    ContainerSummary? Summary,
    ContainerInspect? Inspect,
    IReadOnlyList<PortAnswer> Ports,
    RequestAnswer? Request);

/// <summary>
/// Proof that a service answers, in text, because the agent cannot look.
/// </summary>
/// <remarks>
/// DD30. The daemon reporting <c>running</c> and the service actually answering are different facts, and
/// the gap between them is closed today by a person opening a browser and reporting back. That is the
/// most expensive cycle in the system, and two of the three calls in the canonical task exist because of
/// it.
///
/// <para>So this returns cheap textual proof instead: the port accepts a connection from Windows, an
/// optional GET returns a status, the health check's state <i>and what it printed</i>, and each mount
/// resolved as far as this side can see. Rows, verdicts and remedies are the preflight's own — a caller
/// who has read one preflight reads this without learning anything, which is DD26's argument reused
/// rather than a second vocabulary.</para>
///
/// <para><b>The mount row stops at the Windows side, and says so.</b> Counting the far side means an
/// exec, which is a POST that creates an exec object — so a read verb cannot reach it, and buying that
/// row would cost the GET-only guard its meaning. It is not much of a loss: the failure the section
/// names is a Windows path that did not survive the hop into WSL, and a source directory that is empty
/// or missing is visible from here. What is inside the container is reported <c>unchecked</c>, because
/// DD26 already established that a false "does not resolve" is worse than no answer.</para>
/// </remarks>
public static class ServiceVerify
{
    /// <summary>The row ids, so a caller names one without spelling a string twice.</summary>
    public static class Rows
    {
        /// <summary>Whether it is running at all, said only when it is not.</summary>
        public const string State = "service-state";

        /// <summary>Whether each published port accepts from Windows.</summary>
        public const string Port = "service-port";

        /// <summary>What the service answered, where a request was asked for.</summary>
        public const string Request = "service-request";

        /// <summary>What its own health check says, and what it printed.</summary>
        public const string Health = "service-health";

        /// <summary>Each mount, as far as this side can resolve it.</summary>
        public const string Mounts = "service-mounts";
    }

    /// <summary>How long a connect or a request is given before it counts as no answer.</summary>
    /// <remarks>
    /// Two seconds. This is loopback to a port on the same machine, so a service that has not answered
    /// in two seconds is not slow — it is a service whose listener accepted and then did nothing, which
    /// is a failure worth reporting rather than a wait worth extending. <c>--timeout</c> governs the
    /// whole wait, which is the number a caller actually wants to set.
    /// </remarks>
    public static readonly TimeSpan Attempt = TimeSpan.FromSeconds(2);

    /// <summary>Every published host port a verify would try.</summary>
    /// <param name="inspect">The container's entity tree.</param>
    /// <returns>Each host port beside what it maps to inside, lowest first.</returns>
    /// <remarks>
    /// From the bindings rather than from the list's port strings, because a binding says what was
    /// asked for and the list only says what is currently mapped.
    /// </remarks>
    public static IReadOnlyList<(int Host, string Container)> Published(ContainerInspect? inspect)
    {
        var declared = new List<(int Host, string Container)>();
        foreach (var (containerPort, publishes) in inspect?.HostConfig.PortBindings
            ?? new Dictionary<string, IReadOnlyList<PortPublish>?>(StringComparer.Ordinal))
        {
            foreach (var publish in publishes ?? [])
            {
                if (int.TryParse(publish.HostPort, CultureInfo.InvariantCulture, out var host))
                {
                    declared.Add((host, containerPort));
                }
            }
        }

        return [.. declared.DistinctBy(d => d.Host).OrderBy(d => d.Host)];
    }

    /// <summary>Judge one service.</summary>
    /// <param name="facts">What was gathered.</param>
    /// <returns>The report, whose rows carry the verdicts and the remedies.</returns>
    public static PreflightReport Verify(VerifyFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (facts.Summary is null && facts.Inspect is null)
        {
            return new PreflightReport([new PreflightCheck
            {
                Id = Rows.State,
                Title = facts.Address.ToString(),
                Verdict = Verdict.Fail,
                Detail = "no such container on this engine",
                Remedy = "Run `freewilly read context` to see what is there.",
                Blocking = true,
            }]);
        }

        var running = string.Equals(facts.Inspect?.State.Status, "running", StringComparison.Ordinal)
            || string.Equals(facts.Summary?.State, "running", StringComparison.Ordinal);

        if (!running)
        {
            // The whole answer, and nothing is probed: a port belonging to a container that is not
            // running would be answered by whatever took it, and reporting that as this service would
            // be the confidently wrong answer this verb exists to prevent.
            return new PreflightReport([new PreflightCheck
            {
                Id = Rows.State,
                Title = "state",
                Verdict = Verdict.Fail,
                Detail = (facts.Inspect?.State.Status ?? facts.Summary?.State ?? "unknown")
                    + ", so nothing was probed",
                Remedy = $"Run `freewilly read doctor {facts.Address}` for why it is not running.",
                Blocking = true,
            }]);
        }

        var rows = new List<PreflightCheck>();
        if (PortRow(facts) is { } port)
        {
            rows.Add(port);
        }

        if (RequestRow(facts) is { } request)
        {
            rows.Add(request);
        }

        if (HealthRow(facts) is { } health)
        {
            rows.Add(health);
        }

        if (MountRow(facts) is { } mounts)
        {
            rows.Add(mounts);
        }

        return rows.Count == 0
            ? new PreflightReport([new PreflightCheck
            {
                Id = Rows.Port,
                Title = "ports",
                Verdict = Verdict.Unknown,
                Detail = "running, and publishing nothing",
                Remedy = "There is no port to prove. Reach it from another container, or publish one.",
            }])
            : new PreflightReport(rows);
    }

    /// <summary>
    /// The row DD26 deliberately stopped short of.
    /// </summary>
    /// <remarks>
    /// The doctor says <c>listening</c>, read from the socket table. This says <c>accepts</c>, and the
    /// difference is the whole of DD30: a published port with a dead process behind it is listening and
    /// answers nothing, which is exactly the state that sends the work back to a person with a browser.
    /// </remarks>
    private static PreflightCheck? PortRow(VerifyFacts facts)
    {
        if (facts.Ports.Count == 0)
        {
            return null;
        }

        var refused = facts.Ports.Where(p => !p.Accepted).ToList();
        var text = facts.Ports.Select(p =>
            $":{p.HostPort.ToString(CultureInfo.InvariantCulture)}→{p.ContainerPort} "
            + (p.Accepted
                ? $"accepts ({p.Milliseconds.ToString(CultureInfo.InvariantCulture)}ms)"
                : $"no answer ({p.Failure})"));

        return new PreflightCheck
        {
            Id = Rows.Port,
            Title = "port",
            Verdict = refused.Count == 0 ? Verdict.Pass : Verdict.Fail,
            Detail = string.Join(", ", text),
            Remedy = refused.Count == 0
                ? null
                : $"It is running and port {Ports(refused)} refuses from Windows: the process inside "
                    + "never bound, or bound 127.0.0.1 rather than 0.0.0.0.",
            Blocking = refused.Count > 0,
        };
    }

    private static PreflightCheck? RequestRow(VerifyFacts facts)
    {
        if (facts.Request is not { } request)
        {
            return null;
        }

        if (request.Status is not { } status)
        {
            return new PreflightCheck
            {
                Id = Rows.Request,
                Title = "request",
                Verdict = Verdict.Fail,
                Detail = $"{request.Target}: {request.Failure}",
                Remedy = "The port accepts and the request did not finish, so it is the service "
                    + "rather than the mapping. Read its stderr.",
                Blocking = true,
            };
        }

        // 2xx and 3xx are answers. A 4xx or 5xx is also an answer and the caller may have asked for a
        // path that legitimately 404s, so the verdict is what the status says and the remedy names the
        // ambiguity rather than pretending it away.
        var ok = status is >= 200 and < 400;
        return new PreflightCheck
        {
            Id = Rows.Request,
            Title = "request",
            Verdict = ok ? Verdict.Pass : Verdict.Fail,
            Detail = $"{request.Target} → {status.ToString(CultureInfo.InvariantCulture)} "
                + $"({request.Milliseconds.ToString(CultureInfo.InvariantCulture)}ms)",
            Remedy = ok
                ? null
                : "It answered, so the service is up and this path is not what was expected.",
            Blocking = !ok,
        };
    }

    /// <summary>
    /// What the check says, beside what it printed.
    /// </summary>
    /// <remarks>
    /// The output is the addition. <c>unhealthy</c> is a verdict with no content, and the command's own
    /// last line is the sentence somebody acts on — already on an inspect nobody was reading it from.
    /// </remarks>
    private static PreflightCheck? HealthRow(VerifyFacts facts)
    {
        if (facts.Inspect?.State.Health is not { } health || health.Status.Length == 0)
        {
            return null;
        }

        var last = health.Log is { Count: > 0 } log ? log[^1].Output : null;
        var said = string.IsNullOrWhiteSpace(last)
            ? null
            : last.ReplaceLineEndings(" ").Trim();

        var healthy = string.Equals(health.Status, "healthy", StringComparison.Ordinal);
        var starting = string.Equals(health.Status, "starting", StringComparison.Ordinal);

        return new PreflightCheck
        {
            Id = Rows.Health,
            Title = "health",
            Verdict = healthy ? Verdict.Pass : starting ? Verdict.Warn : Verdict.Fail,
            Detail = health.FailingStreak > 0
                ? $"{health.Status}, {health.FailingStreak.ToString(CultureInfo.InvariantCulture)} failing in a row"
                    + (said is null ? "" : $", last said: {said}")
                : health.Status + (said is null ? "" : $", last said: {said}"),
            Remedy = healthy ? null : "Its own health check decided this; that is what it printed.",
            Blocking = !healthy && !starting,
        };
    }

    /// <summary>
    /// Each mount, resolved as far as Windows can see and counted.
    /// </summary>
    /// <remarks>
    /// The count is what makes the row worth its tokens: a bind source that exists but is empty gives
    /// the container an empty directory rather than an error, so it reads as missing code — and
    /// "exists" alone would have called that one green.
    /// </remarks>
    private static PreflightCheck? MountRow(VerifyFacts facts)
    {
        var mounts = facts.Inspect?.Mounts ?? [];
        if (mounts.Count == 0)
        {
            return null;
        }

        var text = new List<string>();
        var broken = 0;
        var unresolved = 0;

        foreach (var mount in mounts.OrderBy(m => m.Destination, StringComparer.Ordinal))
        {
            if (!string.Equals(mount.Type, "bind", StringComparison.Ordinal))
            {
                text.Add($"{mount.Destination} ← {mount.Type}:{mount.Name ?? mount.Source} unchecked");
                unresolved++;
                continue;
            }

            var windows = Wsl.ToWindowsPath(mount.Source);
            if (windows is null)
            {
                text.Add($"{mount.Destination} ← {mount.Source} unchecked");
                unresolved++;
                continue;
            }

            var count = Count(windows);
            if (count is null)
            {
                text.Add($"{mount.Destination} ← {windows} MISSING");
                broken++;
                continue;
            }

            text.Add($"{mount.Destination} ← {windows} "
                + $"{count.Value.ToString(CultureInfo.InvariantCulture)} file(s)");
            if (count.Value == 0)
            {
                broken++;
            }
        }

        return new PreflightCheck
        {
            Id = Rows.Mounts,
            Title = "mounts",
            Verdict = broken > 0
                ? Verdict.Fail
                : unresolved == mounts.Count ? Verdict.Unknown : Verdict.Pass,
            Detail = string.Join(", ", text),
            Remedy = broken > 0
                ? "A missing or empty bind source gives the container an empty directory rather than "
                    + "an error, so this reads as missing code."
                : null,
            Blocking = broken > 0,
        };
    }

    /// <summary>What is on the Windows side of a bind, or null where the source is not there.</summary>
    private static int? Count(string windows)
    {
        try
        {
            if (File.Exists(windows))
            {
                return 1;
            }

            return Directory.Exists(windows)
                ? Directory.EnumerateFileSystemEntries(windows).Take(1000).Count()
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Present and not readable by this process is not missing, and calling it missing would be
            // the false negative DD26 refused to print.
            return 1;
        }
    }

    private static string Ports(IReadOnlyList<PortAnswer> answers) =>
        string.Join(
            " and ",
            answers.Select(a => a.HostPort.ToString(CultureInfo.InvariantCulture)));
}
