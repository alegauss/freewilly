using System.IO.Pipes;
using System.Text;
using FreeWilly.Core.Engine;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// A backend that answers like a daemon, or refuses like a dead one.
/// </summary>
/// <remarks>
/// The reply is served only after a request arrives, which is not decoration: a channel that hands
/// back the reply regardless lets the ping succeed before the relay has finished forwarding, and an
/// assertion about what the backend received then passes or fails depending on scheduling. That is
/// how this test first went green alone and red in the suite.
/// </remarks>
internal sealed class FakeBackend(string? reply) : IEngineBackend
{
    private int _opened;

    internal int Opened => Volatile.Read(ref _opened);

    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _requests = new();

    internal string Received => string.Concat(_requests);

    /// <summary>
    /// What it answers with now, or <see langword="null"/> to refuse like a dead daemon.
    /// </summary>
    /// <remarks>
    /// Settable, so one engine can go quiet, answer, and go quiet again inside a single test. A
    /// backend fixed at construction can only describe an engine that was always one thing, and the
    /// finding a silence produces is supposed to belong to that silence rather than to the process.
    /// </remarks>
    internal string? Reply { get; set; } = reply;

    public IEngineChannel Open()
    {
        Interlocked.Increment(ref _opened);
        if (Reply is not { } answer)
        {
            throw new IOException("pretend the daemon socket is not there");
        }

        return new Channel(_requests, answer);
    }

    private sealed class Channel : IEngineChannel
    {
        private readonly SemaphoreSlim _requestArrived = new(0, 1);

        internal Channel(
            System.Collections.Concurrent.ConcurrentQueue<string> requests, string reply)
        {
            ToEngine = new RecordingStream(requests, _requestArrived);
            FromEngine = new ReplyStream(Encoding.ASCII.GetBytes(reply), _requestArrived);
        }

        public Stream ToEngine { get; }

        public Stream FromEngine { get; }

        public void Dispose()
        {
            ToEngine.Dispose();
            FromEngine.Dispose();
            _requestArrived.Dispose();
        }
    }

    /// <summary>Keeps what the relay forwarded, and releases the reply once something arrives.</summary>
    private sealed class RecordingStream(
        System.Collections.Concurrent.ConcurrentQueue<string> requests, SemaphoreSlim arrived)
        : WriteOnlyStream
    {
        private bool _released;

        public override void Write(byte[] buffer, int offset, int count)
        {
            requests.Enqueue(Encoding.ASCII.GetString(buffer, offset, count));
            if (!_released)
            {
                _released = true;
                arrived.Release();
            }
        }
    }

    /// <summary>Serves the reply, but not before a request has been forwarded.</summary>
    private sealed class ReplyStream(byte[] reply, SemaphoreSlim arrived) : Stream
    {
        private int _position;
        private bool _waited;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => reply.Length;

        public override long Position { get => _position; set { } }

        public override void Flush()
        {
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_waited)
            {
                await arrived.WaitAsync(cancellationToken).ConfigureAwait(false);
                _waited = true;
            }

            var left = reply.Length - _position;
            if (left <= 0)
            {
                return 0;
            }

            var take = Math.Min(left, buffer.Length);
            reply.AsMemory(_position, take).CopyTo(buffer);
            _position += take;
            return take;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}

/// <summary>A backend that takes everything and answers nothing — a daemon gone quiet (DD173).</summary>
/// <remarks>
/// The half of the split <see cref="FakeBackend"/> cannot express. Its refusing form throws before a
/// channel exists, so the relay closes the connection under the client and the ping comes back
/// having read nothing at all — a fast, conclusive answer. A wedged daemon does the opposite: it
/// accepts the connection, swallows the request, and leaves the reply to a deadline. That is the
/// only way to spend the budget on the read rather than on the connect, which is the one distinction
/// DD173 exists to make.
/// </remarks>
internal sealed class SilentBackend : IEngineBackend
{
    public IEngineChannel Open() => new Channel();

    private sealed class Channel : IEngineChannel
    {
        public Stream ToEngine { get; } = new SwallowingStream();

        public Stream FromEngine { get; } = new SilentStream();

        public void Dispose()
        {
            ToEngine.Dispose();
            FromEngine.Dispose();
        }
    }

    /// <summary>Takes the request and does nothing with it.</summary>
    private sealed class SwallowingStream : WriteOnlyStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
        }
    }

    /// <summary>Never returns, until the connection it belongs to is torn down.</summary>
    /// <remarks>
    /// Waits on the token rather than returning zero. Zero is end-of-stream, which the relay reads
    /// as a finished response and answers by closing the client — the ping would come back at once,
    /// having read nothing, and that is the failure this class is not.
    /// </remarks>
    private sealed class SilentStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => 0;

        public override long Position { get => 0; set { } }

        public override void Flush()
        {
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}

/// <summary>The uninteresting half of a one-way stream.</summary>
internal abstract class WriteOnlyStream : Stream
{
    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => 0;

    public override long Position { get => 0; set { } }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>A daemon that can be made to be dead.</summary>
internal sealed class FakeDaemon(bool aliveWhenLaunched = true) : IDaemonProcess
{
    internal int Launches { get; private set; }

    internal int Stops { get; private set; }

    public bool Alive { get; private set; }

    /// <summary>What the launcher is to be found saying, if anything (DD162).</summary>
    public string? LastWords { get; set; }

    /// <summary>What the launcher is to be found having exited with (DD265).</summary>
    public int? ExitCode { get; set; }

    /// <summary>
    /// How many launches stay dead before one lives, which is how a transient exec failure is
    /// driven (DD265).
    /// </summary>
    internal int DeadLaunches { get; set; }

    public void Launch()
    {
        Launches++;
        Alive = aliveWhenLaunched && Launches > DeadLaunches;
    }

    /// <summary>Run at the moment the kill happens, so a test can see what came before it (DD189).</summary>
    internal Action? Watching { get; set; }

    public void Stop()
    {
        Watching?.Invoke();
        Stops++;
        Alive = false;
    }

    public void Dispose() => Alive = false;
}

/// <summary>
/// The state machine and the relay. "Running" has to mean the engine answered, so the tests that
/// matter are the ones where something is up and the engine still is not.
/// </summary>
public sealed class EngineLifecycleTests
{
    private const string Ok200 =
        "HTTP/1.1 200 OK\r\nApi-Version: 1.55\r\nContent-Length: 2\r\n\r\nOK";

    private static string Pipe() => $"freewilly-test-{Guid.NewGuid():N}";

    /// <summary>The distribution these tests pretend this install owns.</summary>
    /// <remarks>
    /// Passed rather than resolved (DD55). The lifecycle otherwise asks the machine which name it
    /// owns, and a developer whose laptop carries an install made before the rename would run a
    /// suite that answers <c>dockerdesk</c> where every fixture here says <c>freewilly</c> — a test
    /// that passes or fails on what is registered outside the repository.
    /// </remarks>
    private const string Owned = "freewilly";

    // ---- reading a reply --------------------------------------------------------------------

    [Fact]
    public void A_200_with_an_api_version_is_the_engine_answering()
    {
        var result = EnginePing.Interpret(Ok200);

        Assert.True(result.Answered);
        Assert.Equal("1.55", result.ApiVersion);
    }

    [Fact]
    public void A_400_is_something_being_there_and_is_not_running()
    {
        // The daemon replying 400 proves it exists, and proves this code sent it nonsense. Calling
        // that Running would hide the defect — a BOM in front of the request line did exactly this.
        var result = EnginePing.Interpret("HTTP/1.1 400 Bad Request\r\n\r\n");

        Assert.False(result.Answered);
        Assert.Contains("400", result.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("HTTP/1.1 500 Internal Server Error\r\n\r\n")]
    [InlineData("HTTP/1.1 404 Not Found\r\n\r\n")]
    [InlineData("garbage that is not http at all")]
    [InlineData("")]
    public void Anything_that_is_not_a_success_is_not_running(string reply) =>
        Assert.False(EnginePing.Interpret(reply).Answered);

    // ---- the relay, over a real named pipe --------------------------------------------------

    [Fact]
    public async Task The_relay_carries_a_request_to_the_backend_and_the_reply_back()
    {
        var backend = new FakeBackend(Ok200);
        var pipe = Pipe();
        await using var relay = new EnginePipeRelay(backend, pipe);
        relay.Start();

        var ping = await EnginePing.AskAsync(pipe, TimeSpan.FromSeconds(10));

        Assert.True(ping.Answered, ping.Detail);
        Assert.Equal("1.55", ping.ApiVersion);
        Assert.Equal(1, backend.Opened);
        Assert.Contains("GET /_ping", backend.Received, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_request_reaches_the_backend_with_no_byte_order_mark_in_front_of_it()
    {
        // The defect this asserts against cost an afternoon: a UTF-8 writer puts EF BB BF before
        // the request line, and the daemon answers 400 to a request that looks perfect on screen.
        var backend = new FakeBackend(Ok200);
        var pipe = Pipe();
        await using var relay = new EnginePipeRelay(backend, pipe);
        relay.Start();

        await EnginePing.AskAsync(pipe, TimeSpan.FromSeconds(10));

        Assert.StartsWith("GET /_ping", backend.Received, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_relay_serves_more_than_one_connection()
    {
        var backend = new FakeBackend(Ok200);
        var pipe = Pipe();
        await using var relay = new EnginePipeRelay(backend, pipe);
        relay.Start();

        for (var i = 0; i < 3; i++)
        {
            Assert.True((await EnginePing.AskAsync(pipe, TimeSpan.FromSeconds(10))).Answered);
        }

        Assert.Equal(3, relay.Accepted);
    }

    [Fact]
    public async Task A_relay_whose_backend_refuses_leaves_the_pipe_answering_nothing()
    {
        // The pipe exists — the relay created it — and the engine is not there. This is the exact
        // gap that makes "does the pipe exist" the wrong question.
        var backend = new FakeBackend(null);
        var pipe = Pipe();
        await using var relay = new EnginePipeRelay(backend, pipe);
        relay.Start();

        var ping = await EnginePing.AskAsync(pipe, TimeSpan.FromSeconds(5));

        Assert.False(ping.Answered);
        Assert.Equal(1, backend.Opened);
    }

    [Fact]
    public async Task Starting_a_relay_twice_is_refused()
    {
        await using var relay = new EnginePipeRelay(new FakeBackend(Ok200), Pipe());
        relay.Start();

        Assert.Throws<InvalidOperationException>(relay.Start);
    }

    [Fact]
    public async Task Nothing_answers_a_pipe_no_relay_is_serving()
    {
        var ping = await EnginePing.AskAsync(Pipe(), TimeSpan.FromSeconds(2));

        Assert.False(ping.Answered);

        // DD173. The connection is where the whole budget went, and the sentence has to say so:
        // "no answer" would send a reader to a daemon this ping never got near.
        Assert.Equal("no connection within 2s", ping.Detail);
    }

    [Fact]
    public async Task A_relay_that_takes_the_request_and_stays_quiet_is_a_reply_that_never_came()
    {
        // The other half of DD173, and the one the 24 August incident was. The pipe accepted, the
        // request went, and the silence belongs to what is behind the relay rather than to the relay
        // — which is the opposite conclusion to the test above, from a sentence that used to be the
        // same one.
        var pipe = Pipe();
        await using var relay = new EnginePipeRelay(new SilentBackend(), pipe);
        relay.Start();

        var ping = await EnginePing.AskAsync(pipe, TimeSpan.FromSeconds(2));

        Assert.False(ping.Answered);
        Assert.Equal("no answer within 2s", ping.Detail);
    }

    // ---- the state machine -----------------------------------------------------------------

    [Fact]
    public async Task An_unregistered_distribution_is_stopped_and_says_to_install()
    {
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "Ubuntu\r\n", null);
        await using var engine = new EngineLifecycle(
            wsl, new FakeDaemon(), new FakeBackend(Ok200), Pipe(), Owned);

        var status = await engine.StartAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(EngineState.Stopped, status.State);
        Assert.Contains("not registered", status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_daemon_that_dies_while_starting_names_its_log_rather_than_timing_out()
    {
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "freewilly\r\n", null);
        await using var engine = new EngineLifecycle(
            wsl, new FakeDaemon(aliveWhenLaunched: false), new FakeBackend(null), Pipe(), Owned);

        var status = await engine.StartAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(EngineState.Stopped, status.State);
        Assert.Contains(EngineLifecycle.LogPath, status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_daemon_that_could_not_be_executed_yet_is_launched_again()
    {
        // DD265, measured on 31 August 2026 upgrading 1.0.10 to 1.0.11. The installer was still
        // writing /usr/local/bin/dockerd into the distribution when the new tray asked for a start,
        // and a shell cannot exec a file another process holds open for writing: it exits 126 and
        // the journal read "wsl.exe exited 126 without a word". Nothing tried again, so the engine
        // stayed down until it was started by hand.
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "freewilly\r\n", null);
        var daemon = new FakeDaemon
        {
            ExitCode = 126,

            // The first launch dies and the second lives, which is the condition passing.
            DeadLaunches = 1,
        };

        await using var engine = new EngineLifecycle(
            wsl, daemon, new FakeBackend(Ok200), Pipe(), Owned);

        var status = await engine.StartAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(EngineState.Running, status.State);
        Assert.Equal(2, daemon.Launches);

        // And it says so. A retry that hides a failure must not hide itself: an upgrade needing one
        // every time is worth seeing, and the journal is where a reader would look.
        Assert.Contains("after 1 relaunch", status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_daemon_that_keeps_refusing_to_execute_is_reported_rather_than_retried_forever()
    {
        // The bound, because the code above cannot tell the transient case from a daemon whose bytes
        // or permission bits are wrong, and that one never clears. What it costs to be wrong is the
        // pauses, and this is the assertion that the cost is finite.
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "freewilly\r\n", null);
        var daemon = new FakeDaemon(aliveWhenLaunched: false) { ExitCode = 126 };

        await using var engine = new EngineLifecycle(
            wsl, daemon, new FakeBackend(null), Pipe(), Owned);

        var status = await engine.StartAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(EngineState.Stopped, status.State);
        Assert.Equal(1 + EngineLifecycle.MostRelaunches, daemon.Launches);
    }

    [Fact]
    public async Task A_daemon_that_died_for_any_other_reason_is_not_launched_again()
    {
        // The half that keeps the retry honest. Every other way a launch dies is a state that does
        // not pass, so retrying them all would only make the report of a real failure arrive four
        // times later — which is the opposite of what DD162 spent a task achieving.
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "freewilly\r\n", null);
        var daemon = new FakeDaemon(aliveWhenLaunched: false) { ExitCode = 1 };

        await using var engine = new EngineLifecycle(
            wsl, daemon, new FakeBackend(null), Pipe(), Owned);

        var status = await engine.StartAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(EngineState.Stopped, status.State);
        Assert.Equal(1, daemon.Launches);
    }

    /// <summary>What the daemon's log is to be found ending with, for DD266's tests.</summary>
    private const string Busy =
        "/bin/sh: exec: line 0: /usr/local/bin/dockerd: Text file busy";

    /// <summary>A wsl whose distribution is registered and whose daemon log ends as given.</summary>
    private static FakeWsl LoggingTail(string tail)
    {
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "freewilly\r\n", null);
        return wsl.AnswerWhen(
            argv => argv.Any(a => a.Contains("tail -n 1", StringComparison.Ordinal)), 0, tail);
    }

    [Fact]
    public async Task A_launcher_that_exited_silently_quotes_what_the_daemon_log_ends_with()
    {
        // DD266. Measured on 31 August 2026: the journal said "wsl.exe exited 126 without a word"
        // and the cause was sitting in the daemon's log, because the launch command redirects the
        // shell's own stderr into it. So a silent launcher is the case where the log has the words,
        // and it was the one case DD162's split declined to name the log for.
        var daemon = new FakeDaemon(aliveWhenLaunched: false)
        {
            ExitCode = 126,
            LastWords = "wsl.exe exited 126" + WslDaemonProcess.WithoutAWord,
        };

        await using var engine = new EngineLifecycle(
            LoggingTail(Busy), daemon, new FakeBackend(null), Pipe(), Owned);

        var status = await engine.StartAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(EngineState.Stopped, status.State);
        Assert.Contains("Text file busy", status.Detail, StringComparison.Ordinal);
        Assert.Contains(EngineLifecycle.LogPath, status.Detail, StringComparison.Ordinal);

        // And the claim that there was nothing to read is withdrawn, because there was.
        Assert.DoesNotContain(
            WslDaemonProcess.WithoutAWord, status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_launcher_that_exited_silently_over_an_unreadable_log_still_names_the_file()
    {
        // The fallback, and it is the old sentence exactly. A log that cannot be read is still a
        // log, and this still cannot quote it, so the reader is sent where the answer is.
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "freewilly\r\n", null);
        wsl.AnswerWhen(
            argv => argv.Any(a => a.Contains("tail -n 1", StringComparison.Ordinal)), 1);

        var daemon = new FakeDaemon(aliveWhenLaunched: false)
        {
            ExitCode = 126,
            LastWords = "wsl.exe exited 126" + WslDaemonProcess.WithoutAWord,
        };

        await using var engine = new EngineLifecycle(
            wsl, daemon, new FakeBackend(null), Pipe(), Owned);

        var status = await engine.StartAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(EngineState.Stopped, status.State);
        Assert.Contains(EngineLifecycle.LogPath, status.Detail, StringComparison.Ordinal);
        Assert.Contains("read", status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_launcher_that_named_its_own_cause_is_still_not_sent_to_the_log()
    {
        // DD162's property, held while DD266 moves the other branch. A reader handed a cause and a
        // file goes and opens the file, and that is the hour this pair of tasks is about.
        var daemon = new FakeDaemon(aliveWhenLaunched: false)
        {
            ExitCode = 1,
            LastWords = "wsl.exe exited 1: The Windows Subsystem for Linux instance has terminated.",
        };

        await using var engine = new EngineLifecycle(
            LoggingTail(Busy), daemon, new FakeBackend(null), Pipe(), Owned);

        var status = await engine.StartAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(EngineState.Stopped, status.State);
        Assert.Contains("has terminated", status.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(EngineLifecycle.LogPath, status.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Text file busy", status.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The half of that failure the daemon's log cannot hold, since DD162.
    /// </summary>
    /// <remarks>
    /// Measured on 21 August 2026: five restarts in a row reported the daemon exiting and named
    /// <c>/var/log/dockerd.log</c>, and that file held nothing between the failure and the manual
    /// start an hour later — the launch never reached a daemon, so nothing was there to write. The
    /// assertion that the log is <em>not</em> named is the point rather than an extra: a reader who
    /// is handed a cause and a file goes and opens the file.
    /// </remarks>
    [Fact]
    public async Task A_launcher_that_died_is_quoted_instead_of_the_daemon_log()
    {
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "freewilly\r\n", null);
        var daemon = new FakeDaemon(aliveWhenLaunched: false)
        {
            LastWords = "wsl.exe exited 1: The Windows Subsystem for Linux instance has terminated.",
        };

        await using var engine = new EngineLifecycle(
            wsl, daemon, new FakeBackend(null), Pipe(), Owned);

        var status = await engine.StartAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(EngineState.Stopped, status.State);
        Assert.Contains("instance has terminated", status.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(EngineLifecycle.LogPath, status.Detail, StringComparison.Ordinal);
    }

    // ---- the failure that names a WSL internal (DD190) -----------------------------------------

    [Fact]
    public async Task An_unreadable_root_is_read_back_rather_than_quoted_at_the_user()
    {
        // What the host wrote on 29 August 2026, verbatim. Nothing in it says what happened, and
        // the obvious reading is wrong: errno 5 is EIO, so root was not missing.
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "freewilly\r\n", null);
        var daemon = new FakeDaemon(aliveWhenLaunched: false)
        {
            LastWords = "wsl.exe exited -1: getpwnam(root) failed 5",
        };

        await using var engine = new EngineLifecycle(
            wsl, daemon, new FakeBackend(null), Pipe(), Owned);

        var status = await engine.StartAsync(TimeSpan.FromSeconds(20));

        // The evidence survives. A reading that replaced the launcher's own words would leave
        // nobody able to check it.
        Assert.Contains("getpwnam(root) failed 5", status.Detail, StringComparison.Ordinal);
        Assert.Contains("EIO", status.Detail, StringComparison.Ordinal);
        Assert.Contains("read-only", status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_user_is_not_read_as_a_broken_filesystem()
    {
        // The whole diagnosis turns on the errno. Failing with 2 is ENOENT and really is a user that
        // is not there, and answering that with e2fsck would send somebody to repair a healthy disk.
        Assert.Null(WslFailure.Of(
            "wsl.exe exited -1: getpwnam(root) failed 2", "freewilly", @"C:\FreeWilly\distro"));
        Assert.Null(WslFailure.Of(
            "wsl.exe exited 1: The Windows Subsystem for Linux instance has terminated.",
            "freewilly",
            @"C:\FreeWilly\distro"));
        Assert.Null(WslFailure.Of(null, "freewilly", @"C:\FreeWilly\distro"));
    }

    [Fact]
    public void The_remedy_names_the_disk_and_checks_it_from_somewhere_else()
    {
        // A root cannot check itself, which is the fact that makes this four commands rather than
        // one and is why printing them is the value here. Nobody derives them from "getpwnam
        // failed".
        var failure = WslFailure.Of(
            "wsl.exe exited -1: getpwuid(0) failed 5", "freewilly", @"C:\FreeWilly\distro");

        Assert.NotNull(failure);
        var remedy = string.Join("\n", failure.Remedy);

        Assert.Contains(@"C:\FreeWilly\distro\ext4.vhdx", remedy, StringComparison.Ordinal);
        Assert.Contains("e2fsck", remedy, StringComparison.Ordinal);
        Assert.Contains("wsl --terminate freewilly", remedy, StringComparison.Ordinal);

        // The distribution's own root is what is broken, so the check runs from another one.
        Assert.Contains("another distribution", remedy, StringComparison.Ordinal);

        // DD128's non-goal, and it would take every other distribution on the machine down.
        Assert.DoesNotContain("--shutdown", remedy, StringComparison.Ordinal);
    }

    // ---- the warning that arrives a boot early (DD191) -----------------------------------------

    /// <summary>
    /// What the live distribution answered on 29 August 2026, field for field.
    /// </summary>
    /// <remarks>
    /// The healthy case, and it is the one the previous check got wrong: this filesystem's own error
    /// count was zero while the kernel log still held the incident that had been repaired.
    /// <c>errors=remount-ro</c> is in the options of every healthy ext4 mount and is the trap a
    /// substring match for "ro" falls into.
    /// </remarks>
    private const string Well =
        "device=/dev/sdd\n"
        + "options=rw,relatime,discard,errors=remount-ro,data=ordered\n"
        + "errors=0\n"
        + "where=unknown\n";

    [Fact]
    public void A_filesystem_that_recorded_an_error_is_named_and_carries_its_repair()
    {
        // Its own count, out of /sys/fs/ext4/<device>, which is per-filesystem and which a repair
        // clears. That is the whole difference from the kernel log this replaced.
        var wsl = new FakeWsl();
        wsl.Answer(0, "freewilly\r\n").Answer(
            0,
            "device=/dev/sdd\noptions=rw,relatime,errors=remount-ro\nerrors=3\n"
            + "last=ext4_validate_block_bitmap\n");

        var engine = new EngineLifecycle(
            wsl, new FakeDaemon(), new FakeBackend(Ok200), Pipe(), Owned);

        var found = engine.CheckFilesystem();

        Assert.NotNull(found);
        Assert.Contains("3 error(s)", found.Meaning, StringComparison.Ordinal);
        Assert.Contains("ext4_validate_block_bitmap", found.Meaning, StringComparison.Ordinal);

        // Not a refusal. The engine on it is still worth having and the sentence has to say so.
        Assert.Contains("running on it meanwhile", found.Meaning, StringComparison.Ordinal);
        Assert.Contains("e2fsck", string.Join("\n", found.Remedy), StringComparison.Ordinal);
    }

    [Fact]
    public void A_well_filesystem_says_nothing_at_all()
    {
        // The common case, and it has to cost one line in no file. A start that reported a healthy
        // filesystem every time would be the poll this journal refuses to be.
        var wsl = new FakeWsl();
        wsl.Answer(0, "freewilly\r\n").Answer(0, Well);
        var engine = new EngineLifecycle(
            wsl, new FakeDaemon(), new FakeBackend(Ok200), Pipe(), Owned);

        Assert.Null(engine.CheckFilesystem());
    }

    [Fact]
    public void The_remount_ro_in_every_healthy_mounts_options_is_not_read_as_a_fault()
    {
        // `errors=remount-ro` says what the kernel would do if there were an error, not that there
        // was one. A substring match calls every healthy machine broken, which is a check nobody
        // would keep for long.
        var wsl = new FakeWsl();
        wsl.Answer(0, "freewilly\r\n").Answer(0, Well);
        var engine = new EngineLifecycle(
            wsl, new FakeDaemon(), new FakeBackend(Ok200), Pipe(), Owned);

        Assert.Null(engine.CheckFilesystem());
        Assert.Contains("errors=remount-ro", Well, StringComparison.Ordinal);
    }

    [Fact]
    public void A_root_the_kernel_has_already_remounted_read_only_is_named_at_the_start()
    {
        // The state DD190's failure arrives in. `ro` stands alone in the options where it is real.
        var wsl = new FakeWsl();
        wsl.Answer(0, "freewilly\r\n").Answer(
            0, "device=/dev/sdd\noptions=ro,relatime,errors=remount-ro\nerrors=0\nlast=unknown\n");

        var engine = new EngineLifecycle(
            wsl, new FakeDaemon(), new FakeBackend(Ok200), Pipe(), Owned);

        var found = engine.CheckFilesystem();

        Assert.NotNull(found);
        Assert.Contains("mounted read-only", found.Meaning, StringComparison.Ordinal);
    }

    [Fact]
    public void The_check_needs_nothing_a_minirootfs_does_not_have()
    {
        // Measured against the live distribution: awk and blkid are there; findmnt, dumpe2fs and
        // e2fsck are not, because BusyBox is not util-linux. Every distribution provisioned before
        // DD196 is in that state, so a check that needed a package would answer on none of them.
        Assert.Contains("/proc/mounts", EngineLifecycle.StateScript, StringComparison.Ordinal);
        Assert.Contains("/sys/fs/ext4", EngineLifecycle.StateScript, StringComparison.Ordinal);

        foreach (var absent in new[] { "findmnt", "dumpe2fs", "e2fsck", "lsblk" })
        {
            Assert.DoesNotContain(absent, EngineLifecycle.StateScript, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_shared_kernel_log_is_not_consulted_at_all()
    {
        // DD200. WSL2 runs one kernel, so dmesg carries every distribution's disks and every mount
        // they have had. Filtering it by device would fix the first half and leave the second, so it
        // is gone rather than narrowed.
        Assert.DoesNotContain("dmesg", EngineLifecycle.StateScript, StringComparison.Ordinal);
    }

    [Fact]
    public void A_distribution_that_is_not_running_is_not_booted_to_be_asked_about()
    {
        // `wsl -d` starts a stopped distribution. A health probe that boots the virtual machine it
        // was only meant to look at has changed the thing it was reporting on.
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "Ubuntu\r\n", null);
        var engine = new EngineLifecycle(
            wsl, new FakeDaemon(), new FakeBackend(Ok200), Pipe(), Owned);

        Assert.Null(engine.CheckFilesystem());
        Assert.Null(engine.CheckRootIsWritable());
        Assert.DoesNotContain(wsl.Invocations, argv => argv.Length > 0 && argv[0] == "-d");
    }

    [Fact]
    public void A_root_that_has_gone_read_only_under_a_running_engine_is_named()
    {
        // The probe the mount check cannot be: ext4 remounted read-only answers every read
        // perfectly well, so only a write tells the two apart.
        var wsl = new FakeWsl();
        wsl.Answer(0, "freewilly\r\n").Answer(1, "touch: /var/lib/.freewilly-writable: Read-only file system");
        var engine = new EngineLifecycle(
            wsl, new FakeDaemon(), new FakeBackend(Ok200), Pipe(), Owned);

        var found = engine.CheckRootIsWritable();

        Assert.NotNull(found);
        Assert.Contains("no longer writable", found.Meaning, StringComparison.Ordinal);

        // On the root filesystem and not on a tmpfs, which is what /run and /tmp are and what would
        // make this pass on a broken disk.
        Assert.Contains(
            wsl.Invocations,
            argv => argv.Any(word => word.Contains("/var/lib/", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_writable_root_leaves_nothing_behind_and_says_nothing()
    {
        var wsl = new FakeWsl();
        wsl.Answer(0, "freewilly\r\n").Answer(0);
        var engine = new EngineLifecycle(
            wsl, new FakeDaemon(), new FakeBackend(Ok200), Pipe(), Owned);

        Assert.Null(engine.CheckRootIsWritable());

        // Created and removed in the one command, so a probe that ran a thousand times leaves no
        // file in somebody's distribution.
        var probe = wsl.Invocations.Last();
        Assert.Contains(probe, word => word.Contains("rm -f", StringComparison.Ordinal));
    }

    [Fact]
    public void The_error_code_spelling_of_the_same_failure_is_recognised_too()
    {
        // WSL resolves the user before it execs anything, so a root it cannot read surfaces either
        // as the C call or as the code the launcher maps it to. DD192 found the second arriving as
        // mojibake, which is why the match is on the code and not on a sentence around it.
        Assert.NotNull(WslFailure.Of(
            "wsl.exe exited -1: WSL_E_USER_NOT_FOUND", "freewilly", @"C:\FreeWilly\distro"));
    }

    /// <summary>
    /// The bytes a real refused launch produced, decoded and flattened into one line (DD162).
    /// </summary>
    /// <remarks>
    /// Captured rather than invented: <c>wsl.exe -d &lt;missing&gt; -u root --exec …</c> on Windows 11
    /// exits -1 and writes 190 bytes to <b>standard output</b> — not standard error — as UTF-16LE
    /// with no byte-order mark. Every part of that is a way this could have been written wrong:
    /// draining only stderr collects nothing, and decoding as UTF-8 yields a NUL after every
    /// character, which is the wart <see cref="ConsoleTool.Decode"/> exists for.
    /// </remarks>
    [Fact]
    public void A_refused_launch_is_decoded_from_the_encoding_wsl_actually_used()
    {
        var wire = Encoding.Unicode.GetBytes(
            "Não há distribuição com o nome fornecido.\r\n"
            + "Código de erro: Wsl/Service/WSL_E_DISTRO_NOT_FOUND\r\n");

        var said = WslDaemonProcess.Sentence(-1, wire, []);

        Assert.Equal(
            "wsl.exe exited -1: Não há distribuição com o nome fornecido. "
            + "Código de erro: Wsl/Service/WSL_E_DISTRO_NOT_FOUND",
            said);
        Assert.DoesNotContain('\n', said);
    }

    /// <summary>
    /// The two streams do not agree on an encoding, and one buffer cannot hold both (DD192).
    /// </summary>
    /// <remarks>
    /// The 29 August 2026 launch, reconstructed from what the journal kept. <c>wsl.exe</c> wrote its
    /// relay error to standard error as plain bytes and its own refusal to standard output as
    /// UTF-16LE. Concatenated, the zero-counting heuristic in
    /// <see cref="ConsoleTool.Decode"/> resolved the pair to UTF-8, and the file ended up holding
    /// "getpwnam(root) failed 5 U s u ? r i o" — the UTF-16 half read as UTF-8. The half it
    /// destroyed was the one naming the condition.
    /// </remarks>
    [Fact]
    public void Each_stream_is_decoded_in_the_encoding_it_was_written_in()
    {
        var wroteErr = Encoding.UTF8.GetBytes("getpwnam(root) failed 5\n");
        var wroteOut = Encoding.Unicode.GetBytes(
            "Usuário não encontrado.\r\nCódigo de erro: Wsl/Service/WSL_E_USER_NOT_FOUND\r\n");

        var said = WslDaemonProcess.Sentence(-1, wroteOut, wroteErr);

        // The half that says what the condition was, which never reached the journal before.
        Assert.Contains("WSL_E_USER_NOT_FOUND", said, StringComparison.Ordinal);
        Assert.Contains("Usuário não encontrado.", said, StringComparison.Ordinal);

        // And the half that was surviving, still intact rather than traded for the other one.
        Assert.Contains("getpwnam(root) failed 5", said, StringComparison.Ordinal);

        // The mojibake the single buffer produced: UTF-16LE ASCII read as UTF-8 puts a NUL after
        // every character, which is what the journal was rendering as spaced-out letters.
        Assert.DoesNotContain('\0', said);
        Assert.DoesNotContain('\n', said);
    }

    [Fact]
    public void A_noisy_stream_does_not_decide_how_much_of_the_other_one_survives()
    {
        // The cap is per stream since DD192. Shared, a launcher looping on stderr would push the
        // sentence naming the failure out of the buffer before anybody read it.
        var flood = Encoding.UTF8.GetBytes(new string('x', WslDaemonProcess.KeptBytes * 2));
        var wroteOut = Encoding.Unicode.GetBytes("Código de erro: Wsl/Service/WSL_E_USER_NOT_FOUND");

        var said = WslDaemonProcess.Sentence(-1, wroteOut, flood);

        Assert.Contains("WSL_E_USER_NOT_FOUND", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// A launcher that exited saying nothing still names the exit code (DD162).
    /// </summary>
    /// <remarks>
    /// The sentence exists for this case as much as for the loud one. Without it the caller falls
    /// back to naming the daemon's log, and the reader is told a daemon died when what died was the
    /// process that was going to start one.
    /// </remarks>
    [Fact]
    public void A_silent_launcher_is_still_reported_by_its_exit_code()
    {
        var said = WslDaemonProcess.Sentence(1, [], []);

        Assert.Equal("wsl.exe exited 1 without a word", said);
    }

    // ---- what is actually there, rather than what the handle implies (DD175) ------------------

    /// <summary>A lifecycle whose ping never answers, so every reading is a silent one.</summary>
    /// <param name="wsl">The machine, with its answers already queued.</param>
    /// <param name="backend">The backend, held by the caller where it needs to change.</param>
    /// <returns>The lifecycle.</returns>
    private static EngineLifecycle Silent(FakeWsl wsl, FakeBackend backend) =>
        new(wsl, new FakeDaemon(), backend, Pipe(), Owned);

    /// <summary>How many times the machine was asked which distributions are running.</summary>
    private static int RunningLists(FakeWsl wsl) =>
        wsl.Invocations.Count(argv => argv.Contains("--running"));

    [Fact]
    public async Task A_distribution_that_stopped_running_is_named_rather_than_called_a_daemon()
    {
        // DD175, and the failure the whole supervisor exists for. WSL2 does not survive every
        // suspend, and the wsl.exe handle on this side survives the virtual machine going — so this
        // exact machine used to be reported as "the daemon is running".
        var wsl = new FakeWsl();
        wsl.Answer(0, "freewilly\r\n").Answer(0, "\r\n");

        await using var engine = Silent(wsl, new FakeBackend(null));
        var status = await engine.StartAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(EngineState.Starting, status.State);
        Assert.Contains($"{Owned} is not running", status.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("the daemon is running", status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_distribution_that_is_not_running_is_never_asked_to_run_anything()
    {
        // Not a detail of the implementation. `wsl -d <name> --exec` starts a distribution that is
        // down, so asking it about the daemon would boot a virtual machine to answer a question
        // about whether one was there — and leave the next poll looking at a fresh distribution
        // with no daemon in it. A status probe does not get to change what it is reporting on.
        var wsl = new FakeWsl();
        wsl.Answer(0, "freewilly\r\n").Answer(0, "\r\n");

        await using var engine = Silent(wsl, new FakeBackend(null));
        await engine.StartAsync(TimeSpan.FromSeconds(1));

        Assert.Null(wsl.WithVerb("-d"));
    }

    [Fact]
    public async Task A_daemon_gone_from_a_distribution_that_is_up_is_reported_gone()
    {
        // The other world the reader is trying to tell apart: the virtual machine is fine and the
        // process inside it is not, which is a log to go and read rather than a relay to suspect.
        var wsl = new FakeWsl();
        wsl.Answer(0, "freewilly\r\n").Answer(0, "freewilly\r\n").Answer(1, "");

        await using var engine = Silent(wsl, new FakeBackend(null));
        var status = await engine.StartAsync(TimeSpan.FromSeconds(1));

        Assert.Contains("the daemon is not running", status.Detail, StringComparison.Ordinal);
        Assert.NotNull(wsl.WithVerb("-d"));
    }

    [Fact]
    public async Task A_machine_that_will_not_answer_leaves_the_line_talking_about_the_launcher()
    {
        // DD134's direction, arriving here. Load makes wsl.exe late exactly when the ping beside it
        // is late, and folding late into "gone" would manufacture the one reading the watch is
        // entitled to act on out of a busy machine. So the fallback is the honest version of the
        // sentence this replaced: the launcher has not exited, and that is all anybody knows.
        var wsl = new FakeWsl();
        wsl.Answer(0, "freewilly\r\n").Answer(null, "", "wsl.exe did not answer in 15s");

        await using var engine = Silent(wsl, new FakeBackend(null));
        var status = await engine.StartAsync(TimeSpan.FromSeconds(1));

        Assert.Contains("the launcher is alive", status.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("the daemon is", status.Detail, StringComparison.Ordinal);
        Assert.Null(wsl.WithVerb("-d"));
    }

    [Fact]
    public async Task The_machine_is_asked_once_for_a_silence_and_not_once_a_poll()
    {
        // The bound DD134 put on this path, kept. A subprocess per poll is the load that times out
        // the ping, and here it would close a loop: each quiet poll spawning two more wsl.exe
        // children makes the poll after it likelier to be quiet as well.
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "freewilly\r\n", null);

        await using var engine = Silent(wsl, new FakeBackend(null));
        await engine.StartAsync(TimeSpan.FromSeconds(1));
        await engine.StatusAsync();
        await engine.StatusAsync();
        await engine.StatusAsync();

        Assert.Equal(1, RunningLists(wsl));
    }

    [Fact]
    public async Task An_engine_that_answers_and_goes_quiet_again_is_asked_about_again()
    {
        // The finding belongs to the silence and not to the process. Without the clearing, an engine
        // that recovered and failed a second time would carry the first failure's answer for the
        // rest of the host's life — which is the shape of wrong this task is about, only cached.
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "freewilly\r\n", null);
        var backend = new FakeBackend(null);

        await using var engine = Silent(wsl, backend);
        await engine.StartAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, RunningLists(wsl));

        backend.Reply = Ok200;
        Assert.Equal(EngineState.Running, (await engine.StatusAsync()).State);

        backend.Reply = null;
        await engine.StatusAsync();

        Assert.Equal(2, RunningLists(wsl));
    }

    // ---- a repeated finding says which poll established it (DD181) ----------------------------

    /// <summary>An engine that started, answered, and then went quiet — the real sequence.</summary>
    /// <remarks>
    /// Through a start that lands rather than one that times out, because that is the only way the
    /// supervisor is ever reached: <c>Serve</c> returns before supervising anything the start could
    /// not make usable. So the run of silence the caching is about always begins with
    /// <see cref="EngineLifecycle._found"/> cleared by an answer, and a test that skipped the answer
    /// would be measuring a state the host cannot be in.
    /// </remarks>
    private static async Task<EngineLifecycle> WentQuietAfterAnsweringAsync(
        FakeWsl wsl, FakeBackend backend)
    {
        var engine = Silent(wsl, backend);
        backend.Reply = Ok200;
        Assert.Equal(EngineState.Running, (await engine.StartAsync(TimeSpan.FromSeconds(2))).State);
        backend.Reply = null;
        return engine;
    }

    [Fact]
    public async Task The_poll_that_establishes_a_finding_states_it_flat()
    {
        // Nothing to date: this poll is making the observation and reporting it in the same breath,
        // and DD174's line — the one this poll produces — carries the timestamp itself.
        var wsl = new FakeWsl { Default = new WslResult(0, "freewilly\r\n", null) };
        var backend = new FakeBackend(null);

        await using var engine = await WentQuietAfterAnsweringAsync(wsl, backend);
        var first = await engine.StatusAsync();

        Assert.Contains("the daemon is running and", first.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(EngineWatch.FirstQuietPoll, first.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_poll_that_repeats_a_finding_says_which_poll_established_it()
    {
        // DD181, and the line it was measured from. On 24 August 2026 the verdict read "the daemon
        // is running and no connection within 3s — 6 polls in a row", and that opening clause was
        // twenty-six seconds old — the age being exactly where a virtual machine lost under the
        // host's feet would have gone. Undated, a reader takes it as the state at the verdict.
        var wsl = new FakeWsl { Default = new WslResult(0, "freewilly\r\n", null) };
        var backend = new FakeBackend(null);

        await using var engine = await WentQuietAfterAnsweringAsync(wsl, backend);
        await engine.StatusAsync();
        var later = await engine.StatusAsync();

        Assert.Contains(
            $"the daemon is running as of the {EngineWatch.FirstQuietPoll}",
            later.Detail,
            StringComparison.Ordinal);

        // The pointer is only worth writing if it aims at a line that exists, and the machine is
        // still asked once per silence — the marking is a sentence, not a second measurement.
        Assert.Equal(1, RunningLists(wsl));
    }

    [Fact]
    public async Task An_answer_makes_the_next_finding_a_fresh_one_again()
    {
        // The clearing DD175 put in, seen from the sentence. An engine that recovered and failed
        // again is a new incident with a new crossing, so pointing its first line back at the
        // previous silence's would send a reader to a timestamp belonging to another failure.
        var wsl = new FakeWsl { Default = new WslResult(0, "freewilly\r\n", null) };
        var backend = new FakeBackend(null);

        await using var engine = await WentQuietAfterAnsweringAsync(wsl, backend);
        await engine.StatusAsync();
        Assert.Contains(
            EngineWatch.FirstQuietPoll,
            (await engine.StatusAsync()).Detail,
            StringComparison.Ordinal);

        backend.Reply = Ok200;
        Assert.Equal(EngineState.Running, (await engine.StatusAsync()).State);
        backend.Reply = null;

        Assert.DoesNotContain(
            EngineWatch.FirstQuietPoll,
            (await engine.StatusAsync()).Detail,
            StringComparison.Ordinal);
    }

    /// <summary>A launcher that went quietly leaves the pointer exactly as it was (DD162).</summary>
    [Fact]
    public async Task A_daemon_that_died_after_answering_says_so_with_what_the_launcher_said()
    {
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "freewilly\r\n", null);
        var daemon = new FakeDaemon(aliveWhenLaunched: false)
        {
            LastWords = "wsl.exe exited 4294967295 without a word",
        };

        await using var engine = new EngineLifecycle(
            wsl, daemon, new FakeBackend(null), Pipe(), Owned);

        // Launched, so the status reads the handle this lifecycle owns rather than the machine.
        _ = await engine.StartAsync(TimeSpan.FromSeconds(2));
        var status = await engine.StatusAsync();

        Assert.Equal(EngineState.Stopped, status.State);
        Assert.True(status.Conclusive);
        Assert.Contains("the daemon exited: wsl.exe exited", status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_daemon_that_lives_and_never_answers_times_out_as_Starting()
    {
        // Not Running and not Stopped: something is up and the engine is not usable, which is the
        // state the whole enum exists to be able to say.
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "freewilly\r\n", null);
        await using var engine = new EngineLifecycle(
            wsl, new FakeDaemon(), new FakeBackend(null), Pipe(), Owned);

        var status = await engine.StartAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(EngineState.Starting, status.State);
        Assert.False(status.Usable);
        Assert.Contains("did not answer", status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_start_that_reaches_an_answering_engine_is_Running_with_its_api_version()
    {
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "freewilly\r\n", null);
        await using var engine = new EngineLifecycle(
            wsl, new FakeDaemon(), new FakeBackend(Ok200), Pipe(), Owned);

        var status = await engine.StartAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(EngineState.Running, status.State);
        Assert.True(status.Usable);
        Assert.Equal("1.55", status.ApiVersion);
    }

    [Fact]
    public async Task Stopping_stops_the_daemon_and_terminates_the_distribution()
    {
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "freewilly\r\n", null);
        var daemon = new FakeDaemon();
        await using var engine = new EngineLifecycle(
            wsl, daemon, new FakeBackend(Ok200), Pipe(), Owned);
        await engine.StartAsync(TimeSpan.FromSeconds(20));

        // Queued after the start, which has its own probes and would eat these: the running gate,
        // the SIGTERM, then a pidof that finds nothing, which is the daemon having answered.
        wsl.Answer(0, "freewilly\r\n").Answer(0).Answer(1);

        var status = await engine.StopAsync(EngineLifecycle.HurriedGrace);

        Assert.Equal(EngineState.Stopped, status.State);
        Assert.Equal(1, daemon.Stops);
        Assert.Contains(
            wsl.Invocations,
            argv => argv.Length > 1 && argv[0] == "--terminate" && argv[1] == "freewilly");
    }

    [Fact]
    public async Task Stopping_what_was_never_started_says_so_and_does_nothing()
    {
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "Ubuntu\r\n", null);
        var daemon = new FakeDaemon();
        await using var engine = new EngineLifecycle(wsl, daemon, new FakeBackend(Ok200), Pipe(), Owned);

        var status = await engine.StopAsync(EngineLifecycle.HurriedGrace);

        Assert.Equal(EngineState.Stopped, status.State);
        Assert.Contains("nothing was running", status.Detail, StringComparison.Ordinal);
        Assert.Equal(0, daemon.Stops);
    }

    // ---- the containers get a stop signal (DD189) ----------------------------------------------

    [Fact]
    public async Task The_daemon_is_asked_to_stop_before_anything_is_killed()
    {
        // The kill takes the launcher tree and WSL2 reaps dockerd behind it with a SIGKILL, so a
        // signal sent after it reaches nothing. Ordering is the whole of this task: every teardown
        // since DD128, the Quit menu item included, killed a database rather than stopping it.
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "freewilly\r\n", null);

        var asked = -1;
        var daemon = new FakeDaemon();
        daemon.Watching = () => asked = wsl.Invocations.FindIndex(
            argv => argv.Any(word => word.Contains("kill -TERM", StringComparison.Ordinal)));

        await using var engine = new EngineLifecycle(
            wsl, daemon, new FakeBackend(Ok200), Pipe(), Owned);
        await engine.StartAsync(TimeSpan.FromSeconds(20));

        // Queued after the start, whose own probes would consume them.
        wsl.Answer(0, "freewilly\r\n").Answer(0).Answer(1);

        var status = await engine.StopAsync(EngineLifecycle.HurriedGrace);

        Assert.True(asked >= 0, "the daemon was killed without being asked to stop first");
        Assert.Contains("stopped its containers", status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_distribution_that_is_not_running_is_not_started_in_order_to_be_stopped()
    {
        // `wsl -d` against a stopped distribution boots it. A teardown that starts a virtual machine
        // so that it can shut one down is worse than the kill this replaced, so the running list
        // gates the signal the same way it gates the status probe.
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "Ubuntu\r\n", null);
        var daemon = new FakeDaemon();
        await using var engine = new EngineLifecycle(
            wsl, daemon, new FakeBackend(Ok200), Pipe(), Owned);

        await engine.StopAsync(EngineLifecycle.HurriedGrace);

        Assert.DoesNotContain(wsl.Invocations, argv => argv.Length > 0 && argv[0] == "-d");
    }

    [Fact]
    public async Task A_daemon_that_will_not_go_within_the_grace_is_named_and_then_killed()
    {
        // The one outcome where a container was killed after all, so the journal says which of the
        // two teardowns this was rather than reporting them identically.
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "freewilly\r\n", null);
        var daemon = new FakeDaemon();
        await using var engine = new EngineLifecycle(
            wsl, daemon, new FakeBackend(Ok200), Pipe(), Owned);
        await engine.StartAsync(TimeSpan.FromSeconds(20));

        // Nothing is queued, so every pidof succeeds: dockerd is still there when the budget runs
        // out. Zero rather than a real grace, because what is asserted is the sentence and not the
        // wait, and a test that spent the budget would be measuring the build machine.
        var status = await engine.StopAsync(TimeSpan.Zero);

        Assert.Contains("did not stop within", status.Detail, StringComparison.Ordinal);
        Assert.Equal(1, daemon.Stops);
    }

    [Fact]
    public void The_two_teardown_budgets_are_ordered_the_way_the_two_endings_are()
    {
        // A quit waits and a shutdown cannot, which is the argument for there being two of these
        // rather than one constant chosen for whichever caller was thought of first. Fifteen seconds
        // is the daemon's own per-container default, and a patient stop that undercut it would be
        // giving containers a budget their own engine does not believe in.
        Assert.True(EngineLifecycle.PatientGrace > EngineLifecycle.HurriedGrace);
        Assert.True(EngineLifecycle.PatientGrace >= TimeSpan.FromSeconds(15));
        Assert.InRange(
            EngineLifecycle.HurriedGrace,
            TimeSpan.FromSeconds(1),
            FreeWilly.Tray.Cli.EngineCommand.SessionEndingBudget);
    }

    [Fact]
    public async Task Status_reports_Running_only_when_the_engine_answers()
    {
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "freewilly\r\n", null);
        var pipe = Pipe();
        await using var relay = new EnginePipeRelay(new FakeBackend(Ok200), pipe);
        await using var engine = new EngineLifecycle(
            wsl, new FakeDaemon(), new FakeBackend(Ok200), pipe, Owned);

        var before = await engine.StatusAsync();
        relay.Start();
        var after = await engine.StatusAsync();

        Assert.Equal(EngineState.Stopped, before.State);
        Assert.Equal(EngineState.Running, after.State);
    }

    // ---- what a status is entitled to claim (DD134) ------------------------------------------

    [Fact]
    public async Task A_wsl_list_that_never_answered_is_not_reported_as_an_engine_that_is_gone()
    {
        // The half of DD134 that manufactured evidence out of load. `wsl --list` timing out used to
        // be folded into "not registered", which is the one Stopped the watch was entitled to act
        // on — so a busy machine could produce the verdict that terminates its own distribution.
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(null, "", "wsl.exe did not answer within 15s");
        await using var engine = new EngineLifecycle(
            wsl, new FakeDaemon(), new FakeBackend(null), Pipe(), Owned);

        var status = await engine.StatusAsync();

        Assert.Equal(EngineState.Stopped, status.State);
        Assert.False(status.Conclusive);
        Assert.True(engine.DistributionRegistered, "a probe that did not answer is not a denial");
    }

    [Fact]
    public async Task A_daemon_this_host_launched_and_lost_is_conclusive()
    {
        // The handle is the witness that load cannot slow down or lie with, so once this lifecycle
        // owns a daemon it stops asking wsl anything on the poll path at all.
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "freewilly\r\n", null);
        var daemon = new FakeDaemon();
        await using var engine = new EngineLifecycle(
            wsl, daemon, new FakeBackend(null), Pipe(), Owned);
        await engine.StartAsync(TimeSpan.FromSeconds(2));

        daemon.Stop();
        var status = await engine.StatusAsync();

        Assert.Equal(EngineState.Stopped, status.State);
        Assert.True(status.Conclusive);
    }

    [Fact]
    public async Task A_launched_daemon_that_is_merely_quiet_is_never_conclusive()
    {
        // The daemon is up and the pipe said nothing, which is the exact reading the failure of
        // 17 August 2026 acted on. It has to stay an open question however many times it repeats.
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "freewilly\r\n", null);
        await using var engine = new EngineLifecycle(
            wsl, new FakeDaemon(), new FakeBackend(null), Pipe(), Owned);
        await engine.StartAsync(TimeSpan.FromSeconds(2));

        var status = await engine.StatusAsync();

        Assert.Equal(EngineState.Starting, status.State);
        Assert.False(status.Conclusive);
    }

    [Fact]
    public async Task An_unregistered_distribution_is_conclusive_because_the_probe_answered()
    {
        // The other side of the first test here: `wsl --list` ran, succeeded, and did not name the
        // distribution. That is a fact about the machine rather than about its load, and a host
        // that keeps serving a pipe for an engine which is not installed helps nobody.
        var wsl = new FakeWsl();
        wsl.Default = new WslResult(0, "Ubuntu\r\n", null);
        await using var engine = new EngineLifecycle(
            wsl, new FakeDaemon(), new FakeBackend(null), Pipe(), Owned);

        var status = await engine.StatusAsync();

        Assert.Equal(EngineState.Stopped, status.State);
        Assert.True(status.Conclusive);
        Assert.Contains("not registered", status.Detail, StringComparison.Ordinal);
    }
}
