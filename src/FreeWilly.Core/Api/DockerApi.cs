using System.IO.Pipes;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FreeWilly.Core.Api;

/// <summary>The engine refused a call, and said why.</summary>
/// <param name="message">What went wrong, as a sentence naming the endpoint.</param>
/// <param name="status">The HTTP status, when there was one.</param>
/// <param name="detail">The sentence the daemon itself put in the body, if it put one there.</param>
public sealed class DockerApiException(
    string message, HttpStatusCode? status = null, string? detail = null)
    : Exception(message)
{
    /// <summary>The status the daemon returned, or <see langword="null"/> if it never answered.</summary>
    public HttpStatusCode? Status { get; } = status;

    /// <summary>
    /// What the daemon said, with none of this client's framing around it.
    /// </summary>
    /// <remarks>
    /// <see cref="Exception.Message"/> names the endpoint because a log needs to know which call
    /// this was. A row does not: the user pressed the button, so sixty characters of URL before
    /// "port is already allocated" is this tool talking over the engine.
    /// </remarks>
    public string? Detail { get; } = string.IsNullOrWhiteSpace(detail) ? null : detail;
}

/// <summary>
/// How long one Engine API call is given, which depends on what it was asked to do (DD234).
/// </summary>
/// <remarks>
/// <para>The same split <see cref="Engine.WslBudget"/> makes, for the same reason and against the
/// other client. Nearly everything this tool asks the daemon is a question, and a question that has
/// not been answered in twenty seconds is not going to be; a prune is the daemon walking its own
/// storage and unlinking layers, and how long that takes is a function of how much there is to
/// delete.</para>
///
/// <para>Raising the one constant was the fix not taken there and it is not taken here either. A
/// list that hangs has to fail while somebody is still looking at the page, so what changed is that
/// the caller names which kind of call it is making.</para>
/// </remarks>
public static class EngineBudget
{
    /// <summary>A question: a list, a version, an inspect, a ping.</summary>
    /// <remarks>
    /// The client's default, and the reason it stays twenty seconds. These are read on the way to
    /// drawing a page, and every one of them is answered off state the daemon already holds.
    /// </remarks>
    public static readonly TimeSpan Question = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Housekeeping: a prune, and anything else that asks the daemon to delete by the gigabyte.
    /// </summary>
    /// <remarks>
    /// Ten minutes, and a ceiling rather than an estimate: the wait ends when the call does, so this
    /// only bounds the failing case. What it has to cover is the run that most needs it, which is
    /// the one this budget exists for. The compaction is offered because the disk is full, the
    /// fuller it is the longer the prune runs, and under
    /// <see cref="Question"/> the step was surest to fail on the machine that had most to gain.
    /// </remarks>
    public static readonly TimeSpan Housekeeping = TimeSpan.FromMinutes(10);
}

/// <summary>
/// The Engine API over <c>\\.\pipe\docker_engine</c>. HTTP, JSON, and nothing from NuGet.
/// </summary>
/// <remarks>
/// The whole transport is a <see cref="NamedPipeClientStream"/> handed to
/// <see cref="SocketsHttpHandler.ConnectCallback"/>, which is why this needs no dependency: .NET
/// already speaks HTTP over any stream somebody can open.
///
/// Shelling out to <c>docker.exe</c> is the alternative and it is worse in ways that show up on the
/// first refresh — a process per call, text output that changes between versions, and no way to read
/// a streaming endpoint without owning a child's stdout. <see cref="StreamAsync(string, CancellationToken)"/> exists because of
/// that last one.
/// </remarks>
public sealed class DockerApi : IDisposable, Agent.IEngineRemovals, IEngineClient
{
    /// <summary>The pipe the engine serves on Windows.</summary>
    public const string DefaultPipeName = "docker_engine";

    /// <summary>
    /// The API version every request is made against.
    /// </summary>
    /// <remarks>
    /// Pinned rather than omitted. An unversioned path is answered with the daemon's newest version,
    /// so a daemon upgrade can change a response shape under a client that asked for nothing. This
    /// floor is old enough that any engine this project installs answers it, and new enough for
    /// every field read here.
    /// </remarks>
    public const string ApiVersion = "v1.43";

    /// <summary>
    /// How long opening the pipe may take before the engine counts as absent.
    /// </summary>
    private const int ConnectTimeoutMs = 2000;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly string _pipeName;
    private readonly TimeSpan _question;
    private readonly TimeSpan _housekeeping;

    /// <summary>Construct a client.</summary>
    /// <param name="pipeName">The pipe to talk to; overridden in tests.</param>
    /// <param name="timeout">How long a question may take. Streaming calls have no budget.</param>
    /// <param name="housekeeping">
    /// How long a prune may take, which is a separate number because it is a separate kind of call
    /// (DD235). Both are here rather than one: a client is shared by pages asking different things
    /// of the same daemon, so neither budget can be the other's.
    /// </param>
    public DockerApi(
        string pipeName = DefaultPipeName,
        TimeSpan? timeout = null,
        TimeSpan? housekeeping = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName;
        _question = timeout ?? EngineBudget.Question;
        _housekeeping = housekeeping ?? EngineBudget.Housekeeping;

        var handler = new SocketsHttpHandler
        {
            ConnectCallback = ConnectAsync,

            // The Engine API is a local socket; a pool that keeps connections for two minutes holds
            // pipe handles nothing is going to use again.
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(20),
        };

        // A host is required to build a request URI and is never resolved: the callback above is
        // what decides where the bytes go.
        //
        // No timeout on the client since DD235, and that is not the same as no timeout: one number
        // here would be every call's number, and this client is shared by a page listing containers
        // and a button pruning a hundred gigabytes. The budget is applied per call below, where
        // which kind of call it is happens to be known.
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/"),
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
    }

    /// <summary>
    /// A token that gives up after <paramref name="budget"/>, and when the caller does.
    /// </summary>
    /// <param name="budget">How long this kind of call is given.</param>
    /// <param name="cancellation">The caller's own token, which still ends the call early.</param>
    /// <returns>The source, which the caller disposes.</returns>
    /// <remarks>
    /// Linked rather than replacing: a page that navigates away has cancelled, and that is a
    /// different ending from a budget running out. <see cref="Elapsed"/> is what tells them apart
    /// afterwards, because both arrive as the same exception type.
    /// </remarks>
    private static CancellationTokenSource Budgeted(
        TimeSpan budget, CancellationToken cancellation)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        linked.CancelAfter(budget);
        return linked;
    }

    /// <summary>
    /// The sentence for a call that ran out of its budget, or <see langword="null"/> if it did not.
    /// </summary>
    /// <param name="thrown">What the call threw.</param>
    /// <param name="path">The endpoint, so the reader knows what was being waited on.</param>
    /// <param name="budget">How long it was given.</param>
    /// <param name="cancellation">The caller's token, whose cancellation is not this.</param>
    /// <returns>The sentence, or <see langword="null"/>.</returns>
    /// <remarks>
    /// Written here rather than passing <see cref="Exception.Message"/> through, which is what the
    /// client used to do and what DD235 read in a log: "The request was canceled due to the
    /// configured HttpClient.Timeout of 20 seconds elapsing" names a .NET class to somebody holding
    /// a docker problem, and does not name the endpoint that was slow.
    /// </remarks>
    private static string? Elapsed(
        Exception thrown, string path, TimeSpan budget, CancellationToken cancellation) =>
        thrown is OperationCanceledException && !cancellation.IsCancellationRequested
            ? $"the engine did not answer {Path(path)} within {Describe(budget)}"
            : null;

    /// <summary>A budget in the words a sentence about waiting wants.</summary>
    private static string Describe(TimeSpan budget) =>
        budget < TimeSpan.FromMinutes(1)
            ? $"{budget.TotalSeconds:0.#} seconds"
            : $"{budget.TotalMinutes:0.#} minutes";

    private async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellation)
    {
        var pipe = new NamedPipeClientStream(
            ".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            // Bounded, because ConnectAsync with no timeout waits for the pipe to *appear* rather
            // than failing when it is absent. Unbounded, a call against a stopped engine burns the
            // whole request budget and then surfaces as a timeout, which reads like a slow daemon
            // instead of no daemon.
            await pipe.ConnectAsync(ConnectTimeoutMs, cancellation).ConfigureAwait(false);
            return pipe;
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Whether the engine answers at all.</summary>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns><see langword="true"/> when it replied 200.</returns>
    public async Task<bool> PingAsync(CancellationToken cancellation = default)
    {
        try
        {
            using var budget = Budgeted(_question, cancellation);
            using var response = await _http.GetAsync(Path("_ping"), budget.Token)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException
            or IOException or TaskCanceledException or TimeoutException)
        {
            return false;
        }
    }

    /// <summary>What the daemon says about itself.</summary>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>Its versions and platform.</returns>
    public Task<EngineVersion> VersionAsync(CancellationToken cancellation = default) =>
        GetAsync<EngineVersion>("version", cancellation);

    /// <summary>Every container the daemon knows about.</summary>
    /// <param name="all">
    /// <see langword="true"/> for stopped ones too. The list a user opens this tool for includes
    /// the container that exited immediately, so this is normally true.
    /// </param>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>The containers, in the order the daemon returned them.</returns>
    public async Task<IReadOnlyList<ContainerSummary>> ContainersAsync(
        bool all = true, CancellationToken cancellation = default)
    {
        var query = all ? "containers/json?all=1" : "containers/json";
        return await GetAsync<List<ContainerSummary>>(query, cancellation).ConfigureAwait(false);
    }

    /// <summary>Every image the daemon holds, dangling ones included.</summary>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>The images, in the order the daemon returned them.</returns>
    public async Task<IReadOnlyList<ImageSummary>> ImagesAsync(
        CancellationToken cancellation = default) =>
        await GetAsync<List<ImageSummary>>("images/json?all=0", cancellation).ConfigureAwait(false);

    /// <summary>
    /// Remove one image.
    /// </summary>
    /// <remarks>
    /// Without <paramref name="force"/> the daemon refuses an image a container still references
    /// and names the container in the refusal, which is the sentence the user needs — so force is
    /// not the default and the refusal is not swallowed.
    /// </remarks>
    /// <param name="id">The image's id.</param>
    /// <param name="force">Whether to remove it despite a container referencing it.</param>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>A task that completes when the daemon accepted the call.</returns>
    public Task RemoveImageAsync(
        string id, bool force = false, CancellationToken cancellation = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return SendAsync(
            HttpMethod.Delete, $"images/{Uri.EscapeDataString(id)}?force={(force ? 1 : 0)}",
            cancellation);
    }

    /// <summary>
    /// Delete dangling images and report what came back.
    /// </summary>
    /// <remarks>
    /// The <c>dangling=true</c> filter is stated rather than left to the default. The same endpoint
    /// with <c>dangling=false</c> is <c>prune -a</c>, which deletes every image no *running*
    /// container uses — on a developer's machine that is most of them, and it is not a thing to
    /// arrive at by omitting a parameter.
    ///
    /// <para>Housekeeping and not a question (DD235). What the daemon does here is unlink layers by
    /// the gigabyte, and the machine where that takes longest is the machine somebody pressed the
    /// button on.</para>
    /// </remarks>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>What was deleted and how much space came back.</returns>
    public Task<ImagesPruned> PruneDanglingImagesAsync(CancellationToken cancellation = default) =>
        PostJsonAsync<ImagesPruned>(
            "images/prune?filters=" + Uri.EscapeDataString("""{"dangling":["true"]}"""),
            new { },
            cancellation,
            _housekeeping);

    /// <summary>
    /// Every volume, without their sizes.
    /// </summary>
    /// <remarks>
    /// Fast, because it reads metadata. The sizes are a separate call for a reason — see
    /// <see cref="VolumeSizesAsync"/>.
    /// </remarks>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>The volumes.</returns>
    public async Task<IReadOnlyList<VolumeSummary>> VolumesAsync(
        CancellationToken cancellation = default) =>
        (await GetAsync<VolumeList>("volumes", cancellation).ConfigureAwait(false)).Volumes ?? [];

    /// <summary>
    /// The same volumes, with what they cost on disk.
    /// </summary>
    /// <remarks>
    /// <c>/system/df</c> is the only endpoint that measures a volume, and it measures by walking
    /// the filesystem — seconds on a machine with a lot of data. It is called after the list is
    /// already on screen so that the slow answer fills a column rather than delaying the window.
    /// </remarks>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>The volumes, with <c>UsageData</c> filled in.</returns>
    public async Task<IReadOnlyList<VolumeSummary>> VolumeSizesAsync(
        CancellationToken cancellation = default) =>
        (await GetAsync<SystemUsage>("system/df", cancellation).ConfigureAwait(false)).Volumes ?? [];

    /// <summary>
    /// Remove one volume.
    /// </summary>
    /// <remarks>
    /// Never forced. A volume a container mounts is refused by the daemon, and that refusal is the
    /// correct outcome: the thing to deal with is the container, not the guard.
    /// </remarks>
    /// <param name="name">The volume's name.</param>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>A task that completes when the daemon accepted the call.</returns>
    public Task RemoveVolumeAsync(string name, CancellationToken cancellation = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return SendAsync(
            HttpMethod.Delete, $"volumes/{Uri.EscapeDataString(name)}", cancellation);
    }

    /// <summary>
    /// Delete anonymous unused volumes and report what came back.
    /// </summary>
    /// <remarks>
    /// At this API version the endpoint's default is anonymous volumes only; named ones need
    /// <c>all=true</c>, which is not sent and not offered. Every <c>docker run -v /data</c> without
    /// a name leaves an anonymous volume behind and nothing ever collects them, which is the whole
    /// reason this button exists — and a named volume is somebody's database.
    ///
    /// <para>Housekeeping and not a question (DD235), for the reason above: nothing ever collects
    /// these, so what has accumulated by the time somebody presses the button is a directory tree
    /// the daemon has to walk and delete rather than a record it has to look up.</para>
    /// </remarks>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>What was deleted and how much space came back.</returns>
    public Task<VolumesPruned> PruneAnonymousVolumesAsync(
        CancellationToken cancellation = default) =>
        PostJsonAsync<VolumesPruned>("volumes/prune", new { }, cancellation, _housekeeping);

    /// <summary>
    /// Drop the build cache the daemon itself calls reclaimable, and report what came back (DD211).
    /// </summary>
    /// <remarks>
    /// <para>No <c>all=true</c>, and that is the whole safety of it. The bare endpoint takes only
    /// what buildkit has marked reusable-no-longer; <c>all</c> takes every cached layer on the
    /// machine, which is a rebuild of everything somebody has built this month, and it is not a
    /// thing to arrive at by omitting a parameter. Same argument as
    /// <see cref="PruneDanglingImagesAsync"/>, and the same shape.</para>
    ///
    /// <para><b>Not on <see cref="IEngineClient"/>.</b> That interface is what the window's pages
    /// read the engine through, and this is not a page read: it is one step of a sequence that ends
    /// by terminating the distribution, wired where the rest of that sequence is. Putting it on the
    /// interface would make every fixture answer a question no page asks.</para>
    /// </remarks>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>What was deleted and how much space came back.</returns>
    public Task<BuildCachePruned> PruneBuildCacheAsync(CancellationToken cancellation = default) =>
        PostJsonAsync<BuildCachePruned>("build/prune", new { }, cancellation, _housekeeping);

    /// <summary>
    /// What happened between two moments, from the daemon's own bounded history.
    /// </summary>
    /// <remarks>
    /// <paramref name="until"/> is what makes this finite. <c>/events</c> with only a <c>since</c> is a
    /// subscription that replays the history and then holds the connection open forever, which is
    /// correct for the tray and wrong for a command that has to return.
    /// </remarks>
    /// <param name="since">The start of the window.</param>
    /// <param name="until">The end of it.</param>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>The events, oldest first.</returns>
    public async Task<IReadOnlyList<DockerEvent>> EventsAsync(
        DateTimeOffset since, DateTimeOffset until, CancellationToken cancellation = default)
    {
        var window = $"events?since={Seconds(since)}&until={Seconds(until)}";
        await using var stream = await StreamAsync(window, cancellation).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        var events = new List<DockerEvent>();
        while (await reader.ReadLineAsync(cancellation).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            try
            {
                if (JsonSerializer.Deserialize<DockerEvent>(line) is { } moved)
                {
                    events.Add(moved);
                }
            }
            catch (JsonException)
            {
                // One unreadable line is not a failed read. The daemon adds fields between versions
                // and a delta missing one event is better than a delta that threw.
            }
        }

        return events;
    }

    /// <summary>Whole seconds since the epoch, which is the only form /events takes.</summary>
    private static string Seconds(DateTimeOffset at) =>
        at.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Everything the daemon knows about one container.
    /// </summary>
    /// <remarks>
    /// Read for one field: whether a TTY was allocated. That is not on the list endpoint, and it is
    /// what decides whether the log stream carries frame headers — so a log window asks this once
    /// before it opens the stream, rather than guessing from the bytes that arrive.
    /// </remarks>
    /// <param name="id">The container's id.</param>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>What the daemon said.</returns>
    public Task<ContainerInspect> InspectAsync(string id, CancellationToken cancellation = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return GetAsync<ContainerInspect>(
            $"containers/{Uri.EscapeDataString(id)}/json", cancellation);
    }

    /// <summary>
    /// Open a container's log and keep it open.
    /// </summary>
    /// <remarks>
    /// The same streaming shape as <c>/events</c>, so it goes through <see cref="StreamAsync(string, CancellationToken)"/>.
    /// <paramref name="tail"/> is what makes opening a window on a container that has been running
    /// for a week finish at all: without it the daemon replays the entire log first.
    /// </remarks>
    /// <param name="id">The container's id.</param>
    /// <param name="tail">How many lines of history to open with.</param>
    /// <param name="follow">Whether to keep the stream open for new output.</param>
    /// <param name="timestamps">
    /// Whether the daemon prefixes each line with an RFC3339 stamp. What a log cursor is made of.
    /// </param>
    /// <param name="since">Only output written after this, to the second the endpoint works in.</param>
    /// <param name="cancellation">Cancellation. Closing it is how the stream ends.</param>
    /// <returns>The response body, framed unless the container has a TTY.</returns>
    public Task<Stream> LogsAsync(
        string id,
        int tail = 2000,
        bool follow = true,
        bool timestamps = false,
        DateTimeOffset? since = null,
        CancellationToken cancellation = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfNegative(tail);

        // since is seconds, which is the only unit the endpoint takes, and it is inclusive of the
        // second it names - so a cursor read back would repeat the last line. The digest filters on the
        // exact timestamp it was given for that reason; this only narrows what has to be read.
        var window = since is { } at
            ? $"&since={at.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : "";

        return StreamAsync(
            $"containers/{Uri.EscapeDataString(id)}/logs"
            + $"?stdout=1&stderr=1&tail={tail}&follow={(follow ? 1 : 0)}"
            + $"&timestamps={(timestamps ? 1 : 0)}{window}",
            cancellation);
    }

    /// <summary>Start a container.</summary>
    /// <param name="id">The container's id.</param>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>A task that completes when the daemon accepted the call.</returns>
    public Task StartContainerAsync(string id, CancellationToken cancellation = default) =>
        ActOnAsync(HttpMethod.Post, id, "start", cancellation);

    /// <summary>
    /// Stop a container, giving it the daemon's default grace period before the kill.
    /// </summary>
    /// <remarks>
    /// No <c>t</c> parameter, so the daemon's own ten seconds apply. Shortening it here would make
    /// this tool stop containers differently from every other Docker client on the machine, and the
    /// wait is the reason the row goes to a pending state rather than the reason to cut it short.
    /// </remarks>
    /// <param name="id">The container's id.</param>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>A task that completes when the daemon accepted the call.</returns>
    public Task StopContainerAsync(string id, CancellationToken cancellation = default) =>
        ActOnAsync(HttpMethod.Post, id, "stop", cancellation);

    /// <summary>Restart a container.</summary>
    /// <param name="id">The container's id.</param>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>A task that completes when the daemon accepted the call.</returns>
    public Task RestartContainerAsync(string id, CancellationToken cancellation = default) =>
        ActOnAsync(HttpMethod.Post, id, "restart", cancellation);

    /// <summary>Remove a container.</summary>
    /// <remarks>
    /// <paramref name="force"/> is what makes removing a running container possible at all: without
    /// it the daemon answers 409 and the container stays. The caller is the one that knows whether
    /// the user was asked, so this does not decide it.
    /// </remarks>
    /// <param name="id">The container's id.</param>
    /// <param name="force">Whether to kill it first.</param>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>A task that completes when the daemon accepted the call.</returns>
    public Task RemoveContainerAsync(
        string id, bool force = false, CancellationToken cancellation = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return SendAsync(
            HttpMethod.Delete, $"containers/{Uri.EscapeDataString(id)}?force={(force ? 1 : 0)}",
            cancellation);
    }

    /// <summary>
    /// Run one command inside a running container and hand back what it exited with.
    /// </summary>
    /// <remarks>
    /// Nothing is attached, so this is for commands whose answer is their exit code — asking a
    /// container whether it has a shell before opening a terminal on it. Attaching stdout would
    /// mean reading a hijacked stream, and a probe does not need to read anything.
    ///
    /// A command whose binary is not there is a refusal from <c>start</c>, not a non-zero exit, so
    /// a caller testing for a program has to treat <see cref="DockerApiException"/> as an answer.
    /// </remarks>
    /// <param name="id">The container's id.</param>
    /// <param name="command">The command and its arguments.</param>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>The exit code, or -1 when the daemon reported none.</returns>
    public async Task<int> RunInContainerAsync(
        string id, IReadOnlyList<string> command, CancellationToken cancellation = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(command);

        var created = await PostJsonAsync<ExecCreated>(
            $"containers/{Uri.EscapeDataString(id)}/exec",
            new { AttachStdout = false, AttachStderr = false, Tty = false, Cmd = command },
            cancellation).ConfigureAwait(false);

        // Detach false, so the response body ends when the process does. Nothing is attached, so
        // the body is empty and reaching its end is the whole wait — no polling, no timer.
        await using (var running = await StreamAsync(
            HttpMethod.Post,
            $"exec/{Uri.EscapeDataString(created.Id)}/start",
            JsonBody(new { Detach = false, Tty = false }),
            cancellation).ConfigureAwait(false))
        {
            await running.CopyToAsync(Stream.Null, cancellation).ConfigureAwait(false);
        }

        var status = await GetAsync<ExecStatus>(
            $"exec/{Uri.EscapeDataString(created.Id)}/json", cancellation).ConfigureAwait(false);

        return status.ExitCode ?? -1;
    }

    private static HttpContent JsonBody(object body) =>
        System.Net.Http.Json.JsonContent.Create(body, options: Json);

    private async Task<T> PostJsonAsync<T>(
        string path, object body, CancellationToken cancellation, TimeSpan? budget = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Path(path))
        {
            Content = JsonBody(body),
        };

        var given = budget ?? _question;
        using var giving = Budgeted(given, cancellation);
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, giving.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            (exception is HttpRequestException or IOException or TimeoutException
             || (exception is TaskCanceledException && !cancellation.IsCancellationRequested)))
        {
            throw new DockerApiException(
                Elapsed(exception, path, given, cancellation)
                ?? $"the engine did not answer {Path(path)}: {exception.Message}");
        }

        using (response)
        {
            // The body is inside the budget too, and not only the headers: a prune answers with a
            // list of every record it deleted, and a call that has spent its ten minutes has spent
            // them whichever half of the exchange is still going.
            await ThrowIfRefusedAsync(response, path, giving.Token).ConfigureAwait(false);
            try
            {
                return await response.Content
                    .ReadFromJsonAsync<T>(Json, giving.Token).ConfigureAwait(false)
                    ?? throw new DockerApiException($"{Path(path)} returned null");
            }
            catch (JsonException exception)
            {
                throw new DockerApiException(
                    $"{Path(path)} returned something that is not the JSON expected: "
                    + exception.Message,
                    response.StatusCode);
            }
        }
    }

    private Task ActOnAsync(
        HttpMethod method, string id, string verb, CancellationToken cancellation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return SendAsync(method, $"containers/{Uri.EscapeDataString(id)}/{verb}", cancellation);
    }

    private async Task SendAsync(
        HttpMethod method, string path, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(method, Path(path));
        using var giving = Budgeted(_question, cancellation);
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, giving.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            (exception is HttpRequestException or IOException or TimeoutException
             || (exception is TaskCanceledException && !cancellation.IsCancellationRequested)))
        {
            throw new DockerApiException(
                Elapsed(exception, path, _question, cancellation)
                ?? $"the engine did not answer {Path(path)}: {exception.Message}");
        }

        using (response)
        {
            // 304 is the daemon saying the container is already in the state that was asked for.
            // Nothing happened, and nothing needed to: surfacing that as a failure would put "not
            // modified" on a row that already reads the way the user wanted it to.
            if (response.StatusCode is HttpStatusCode.NotModified)
            {
                return;
            }

            await ThrowIfRefusedAsync(response, path, giving.Token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Open a streaming endpoint and hand back its body, unread.
    /// </summary>
    /// <remarks>
    /// <para>For <c>/events</c> and container logs: endpoints that never end. The response is not
    /// buffered, so the caller reads frames as the daemon writes them — which is the thing shelling
    /// out to a CLI cannot do without owning a child process's stdout.</para>
    ///
    /// <para><b>The one call with no budget of its own</b>, which is why DD235 could move the
    /// budgets off the client without changing anything here. An endpoint that never ends is the
    /// one shape a deadline cannot describe, and the caller's token is the whole of how it stops.
    /// </para>
    /// </remarks>
    /// <param name="path">The endpoint, without a leading slash or version.</param>
    /// <param name="cancellation">Cancellation. Closing it is how the stream ends.</param>
    /// <returns>The response body.</returns>
    public Task<Stream> StreamAsync(string path, CancellationToken cancellation = default) =>
        StreamAsync(HttpMethod.Get, path, body: null, cancellation);

    private async Task<Stream> StreamAsync(
        HttpMethod method, string path, HttpContent? body, CancellationToken cancellation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var request = new HttpRequestMessage(method, Path(path)) { Content = body };

        // Only as far as the headers, which is exactly how much of a streaming call the client's
        // own timeout used to bound: with ResponseHeadersRead it stopped counting once they
        // arrived. Keeping that under DD235 takes saying so, because a budget applied to the whole
        // call here would close /events twenty seconds after opening it.
        using var giving = Budgeted(_question, cancellation);
        HttpResponseMessage response;
        try
        {
            response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, giving.Token)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            (exception is HttpRequestException or IOException or TimeoutException
             || (exception is TaskCanceledException && !cancellation.IsCancellationRequested)))
        {
            throw new DockerApiException(
                Elapsed(exception, path, _question, cancellation)
                ?? $"the engine did not answer {Path(path)}: {exception.Message}");
        }

        // The caller's token from here, and not the budgeted one: what is left is a body with no
        // end, and the caller closing its token is the whole of how such a call stops.
        await ThrowIfRefusedAsync(response, path, cancellation).ConfigureAwait(false);
        return await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellation)
    {
        using var giving = Budgeted(_question, cancellation);
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(Path(path), giving.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            (exception is HttpRequestException or IOException or TimeoutException
             || (exception is TaskCanceledException && !cancellation.IsCancellationRequested)))
        {
            // Naming the endpoint matters: "the pipe is not there" is one failure and "this call was
            // refused" is another, and a UI that shows the same message for both teaches nothing.
            // TaskCanceledException is in the list because that is what a budget elapsing raises,
            // and letting it out raw is the same failure with no endpoint in it. A cancellation the
            // caller asked for is not caught: that one belongs to them.
            throw new DockerApiException(
                Elapsed(exception, path, _question, cancellation)
                ?? $"the engine did not answer {Path(path)}: {exception.Message}");
        }

        using (response)
        {
            await ThrowIfRefusedAsync(response, path, giving.Token).ConfigureAwait(false);
            try
            {
                return await response.Content
                    .ReadFromJsonAsync<T>(Json, giving.Token).ConfigureAwait(false)
                    ?? throw new DockerApiException($"{Path(path)} returned null");
            }
            catch (JsonException exception)
            {
                throw new DockerApiException(
                    $"{Path(path)} returned something that is not the JSON expected: "
                    + exception.Message,
                    response.StatusCode);
            }
        }
    }

    private static async Task ThrowIfRefusedAsync(
        HttpResponseMessage response, string path, CancellationToken cancellation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // The daemon puts a sentence in the body, and it is almost always the useful part.
        var body = "";
        try
        {
            body = (await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false))
                .Trim();
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            // Nothing readable; the status is what there is.
        }

        var said = body.Length == 0 ? "" : $": {Shorten(body)}";
        throw new DockerApiException(
            $"the engine answered {(int)response.StatusCode} {response.ReasonPhrase} "
            + $"for {ApiVersion}/{path}{said}",
            response.StatusCode,
            Shorten(body));
    }

    private static string Shorten(string text) =>
        text.Length <= 300 ? text : text[..300] + "…";

    private static string Path(string endpoint) => $"{ApiVersion}/{endpoint}";

    /// <inheritdoc/>
    public void Dispose() => _http.Dispose();
}
