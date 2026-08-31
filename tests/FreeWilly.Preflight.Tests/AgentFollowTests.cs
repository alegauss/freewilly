using System.Buffers.Binary;
using System.Text;
using FreeWilly.Core.Agent;
using FreeWilly.Core.Api;
using FreeWilly.Tray.Cli;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// Following a log to a named line, a deadline or the ceiling, so watching a run does not send the
/// session back to the docker CLI (DD251).
/// </summary>
public sealed class AgentFollowTests
{
    /// <summary>One frame: the stream number, three zeroes, the length big-endian, the payload.</summary>
    private static byte[] Frame(byte stream, string payload)
    {
        var body = Encoding.UTF8.GetBytes(payload);
        var frame = new byte[LogFrames.HeaderSize + body.Length];
        frame[0] = stream;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(4, 4), (uint)body.Length);
        body.CopyTo(frame, LogFrames.HeaderSize);
        return frame;
    }

    /// <summary>
    /// A followed stream: it hands over what has been written and then waits, the way the daemon does
    /// for a container that is still running.
    /// </summary>
    /// <remarks>
    /// The waiting is the whole point. A <see cref="MemoryStream"/> ends, and a follow that only ever
    /// meets a stream that ends never exercises the deadline, the ceiling or the early return — which
    /// are the three endings this task added.
    /// </remarks>
    private sealed class LiveStream(byte[] content) : Stream
    {
        private int _position;

        /// <summary>Whether the reader was still waiting when the follow let go.</summary>
        internal bool Waited { get; private set; }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var left = content.Length - _position;
            if (left == 0)
            {
                Waited = true;
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }

            var give = Math.Min(buffer.Length, left);
            content.AsSpan(_position, give).CopyTo(buffer.Span);
            _position += give;
            return give;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => content.Length;

        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private static Stream Live(params string[] lines) =>
        new LiveStream([.. lines.SelectMany(l => Frame(1, l))]);

    private static LogQuery Query(int? budget = null) =>
        new(BudgetTokens: budget ?? LogDigest.DefaultBudgetTokens);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    // ---- the line the caller named ---------------------------------------------------------------

    [Fact]
    public void The_named_line_ends_the_follow_before_the_deadline()
    {
        using var stream = Live("migrating\n", "seed complete\n", "still going\n");

        var started = DateTimeOffset.UtcNow;
        var followed = AgentSurface.Follow(stream, Query(), "seed complete", Patience, ceiling: true);

        Assert.True(followed.Matched);

        // The point of --until: it returns when the line arrives, rather than paying out the deadline.
        Assert.True(DateTimeOffset.UtcNow - started < Patience);
    }

    [Fact]
    public void The_match_is_case_insensitive_and_a_substring()
    {
        using var stream = Live("Listening on :8080\n");

        Assert.True(AgentSurface.Follow(stream, Query(), "listening on", Patience, ceiling: true).Matched);
    }

    [Fact]
    public void A_line_that_never_arrives_ends_on_the_deadline_and_says_it_did_not()
    {
        var stream = new LiveStream([.. Frame(1, "migrating\n")]);

        var followed = AgentSurface.Follow(
            stream, Query(), "seed complete", TimeSpan.FromMilliseconds(250), ceiling: true);

        Assert.False(followed.Matched);
        Assert.True(stream.Waited);

        // What was read still counts: the caller gets the lines that did arrive, not an empty payload.
        Assert.Single(followed.Lines);
    }

    [Fact]
    public void A_match_split_across_two_frames_is_still_a_match()
    {
        // The daemon frames by chunk, not by line, so a pattern straddling a boundary is the case a
        // per-frame match would miss.
        using var stream = new LiveStream([.. Frame(1, "seed com"), .. Frame(1, "plete\n")]);

        Assert.True(AgentSurface.Follow(stream, Query(), "seed complete", Patience, ceiling: true).Matched);
    }

    // ---- the bounds ------------------------------------------------------------------------------

    [Fact]
    public void A_follow_with_no_pattern_reads_to_the_deadline()
    {
        var stream = new LiveStream([.. Frame(1, "one\n"), .. Frame(1, "two\n")]);

        var followed = AgentSurface.Follow(
            stream, Query(), until: null, TimeSpan.FromMilliseconds(250), ceiling: true);

        Assert.False(followed.Matched);
        Assert.True(stream.Waited);
        Assert.Equal(2, followed.Lines.Count);
    }

    [Fact]
    public void The_budget_stops_a_stream_that_would_otherwise_never_end()
    {
        // A container printing faster than the ceiling allows is the case that makes a follow
        // dangerous, and the budget is what makes it affordable rather than open-ended.
        var chatty = Enumerable.Range(0, 400)
            .Select(i => $"2024-01-01T00:00:0{i % 10}.000000000Z line {i} of a very talkative service\n")
            .ToArray();
        using var stream = Live(chatty);

        var started = DateTimeOffset.UtcNow;
        var followed = AgentSurface.Follow(stream, Query(budget: 60), until: null, Patience, ceiling: true);

        Assert.True(DateTimeOffset.UtcNow - started < Patience);
        Assert.True(followed.Lines.Count < chatty.Length);
    }

    [Fact]
    public void A_follow_writing_a_file_is_not_stopped_by_the_ceiling()
    {
        // Same reasoning as `--out` on the plain read: the ceiling exists because a payload is read
        // by something paying per token, and a file is not.
        var chatty = Enumerable.Range(0, 200)
            .Select(i => $"line {i} of a very talkative service indeed\n")
            .ToArray();
        var stream = new LiveStream([.. chatty.SelectMany(l => Frame(1, l))]);

        var followed = AgentSurface.Follow(
            stream, Query(budget: 60), until: null, TimeSpan.FromMilliseconds(400), ceiling: false);

        Assert.Equal(chatty.Length, followed.Lines.Count);
        Assert.True(stream.Waited);
    }

    [Fact]
    public void A_stream_that_ends_on_its_own_ends_the_follow()
    {
        // The container exited. Nothing is waited for, and no deadline is paid.
        using var ended = new MemoryStream([.. Frame(1, "done\n")]);

        var started = DateTimeOffset.UtcNow;
        var followed = AgentSurface.Follow(ended, Query(), until: null, Patience, ceiling: true);

        Assert.Single(followed.Lines);
        Assert.True(DateTimeOffset.UtcNow - started < Patience);
    }

    // ---- a container replaced under the follow (DD257) -------------------------------------------

    private const string Containers = """
        [{"Id":"aaaaaaaaaaaa0000","Names":["/shop-api-1"],"Image":"shop/api:latest",
          "State":"running","Status":"Up 4 minutes","Ports":[]}]
        """;

    private const string Recreated = """
        [{"Id":"bbbbbbbbbbbb1111","Names":["/shop-api-1"],"Image":"shop/api:latest",
          "State":"running","Status":"Up 2 seconds","Ports":[]}]
        """;

    [Fact]
    public void A_stream_that_ran_out_is_three_endings_and_the_daemon_says_which()
    {
        static ContainerSummary Was(string id) => new()
        {
            Id = id, Names = ["/shop-api-1"], Image = "shop/api:latest", State = "running",
        };

        Assert.Equal(AgentSurface.FollowEnd.Ended, AgentSurface.Resettled(Was("aaaa"), "aaaa"));
        Assert.Equal(AgentSurface.FollowEnd.Replaced, AgentSurface.Resettled(Was("bbbb"), "aaaa"));
        Assert.Equal(AgentSurface.FollowEnd.Gone, AgentSurface.Resettled(null, "aaaa"));
    }

    [Fact]
    public async Task A_replaced_container_is_not_reported_as_a_log_that_ended()
    {
        await using var daemon = Printing("migrating\n")
            .Then(ApiPath("containers/json?all=1"), Recreated);
        var output = new StringWriter();

        var code = Logs(daemon, output, "shop-api-1", "--follow", "--until", "seed complete");

        // The service is running and about to print the line; saying the log ended would be a
        // confidently wrong answer in the shape of a right one.
        Assert.Equal(1, code);
        Assert.Contains("the container was replaced", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("the log ended", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_replacement_is_worth_saying_even_where_nothing_was_claimed()
    {
        await using var daemon = Printing("migrating\n")
            .Then(ApiPath("containers/json?all=1"), Recreated);
        var output = new StringWriter();

        // No --until, so nothing failed and the code is 0. The payload still stops for a reason the
        // reader cannot see in it.
        Assert.Equal(0, Logs(daemon, output, "shop-api-1", "--follow"));
        Assert.Contains("the container was replaced", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_container_that_simply_stopped_printing_still_says_the_log_ended()
    {
        await using var daemon = Printing("migrating\n")
            .Then(ApiPath("containers/json?all=1"), Containers);
        var output = new StringWriter();

        Assert.Equal(1, Logs(daemon, output, "shop-api-1", "--follow", "--until", "seed complete"));
        Assert.Contains("the log ended", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_container_that_went_away_says_it_is_gone()
    {
        await using var daemon = Printing("migrating\n")
            .Then(ApiPath("containers/json?all=1"), "[]");
        var output = new StringWriter();

        Assert.Equal(1, Logs(daemon, output, "shop-api-1", "--follow", "--until", "seed complete"));
        Assert.Contains("the container is gone", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_a_stream_that_ran_out_pays_for_a_second_list()
    {
        // The refinement costs a call on the agent surface, so it is asked on the one ending that
        // cannot tell itself apart and on no other.
        await using var daemon = Printing("migrating\n", "seed complete\n")
            .Then(ApiPath("containers/json?all=1"), Recreated);

        Assert.Equal(0, Logs(daemon, "shop-api-1", "--follow", "--until", "seed complete"));
        Assert.Equal(
            1,
            daemon.Requested.Count(r => r.Contains("containers/json?all=1", StringComparison.Ordinal)));
    }

    // ---- which ending it was (DD256) -------------------------------------------------------------

    [Fact]
    public void A_deadline_and_a_stream_that_ended_are_not_the_same_ending()
    {
        // Both used to be reported as the deadline, so a container that stopped printing after two
        // seconds was said to have been waited on for the whole timeout.
        var waiting = new LiveStream([.. Frame(1, "migrating\n")]);
        var over = new MemoryStream([.. Frame(1, "migrating\n")]);

        Assert.Equal(
            AgentSurface.FollowEnd.Deadline,
            AgentSurface.Follow(waiting, Query(), "seed", TimeSpan.FromMilliseconds(250), true).End);
        Assert.Equal(
            AgentSurface.FollowEnd.Ended,
            AgentSurface.Follow(over, Query(), "seed", Patience, ceiling: true).End);
    }

    [Fact]
    public void The_ceiling_is_its_own_ending_rather_than_the_deadline()
    {
        var chatty = Enumerable.Range(0, 400)
            .Select(i => $"line {i} of a service with a great deal to say for itself\n")
            .ToArray();
        using var stream = Live(chatty);

        var followed = AgentSurface.Follow(stream, Query(budget: 60), "seed", Patience, ceiling: true);

        // The reader's next move is to raise --budget or write to a file, which is a different move
        // from raising --timeout.
        Assert.Equal(AgentSurface.FollowEnd.Ceiling, followed.End);
    }

    [Fact]
    public async Task A_log_that_ended_says_so_rather_than_naming_a_wait_that_never_happened()
    {
        await using var daemon = Printing("migrating\n", "rolling back\n");
        var output = new StringWriter();

        var code = Logs(
            daemon, output, "shop-api-1", "--follow", "--until", "seed complete", "--timeout", "90s");

        Assert.Equal(1, code);
        Assert.Contains("the log ended", output.ToString(), StringComparison.Ordinal);

        // The whole of DD256: it never waited ninety seconds, so it must not say it did.
        Assert.DoesNotContain("90s", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_deadline_names_a_duration()
    {
        // A duration is a claim about how long this waited, so only the ending that waited one may
        // make it. Every other ending naming the timeout is exactly the defect DD256 fixed.
        var ninety = TimeSpan.FromSeconds(90);

        foreach (var end in Enum.GetValues<AgentSurface.FollowEnd>())
        {
            if (end == AgentSurface.FollowEnd.Matched)
            {
                continue;
            }

            var said = AgentSurface.Missing("seed complete", ninety, end);

            Assert.Contains("seed complete", said, StringComparison.Ordinal);
            Assert.Equal(
                end == AgentSurface.FollowEnd.Deadline,
                said.Contains("90s", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Each_ending_says_something_a_reader_can_act_on()
    {
        var ninety = TimeSpan.FromSeconds(90);

        // The next move differs per ending, and the line is where it is named.
        Assert.Contains(
            "--budget",
            AgentSurface.Missing("x", ninety, AgentSurface.FollowEnd.Ceiling),
            StringComparison.Ordinal);
        Assert.Contains(
            "stopped",
            AgentSurface.Missing("x", ninety, AgentSurface.FollowEnd.Stopped),
            StringComparison.Ordinal);
        Assert.Contains(
            "the log ended",
            AgentSurface.Missing("x", ninety, AgentSurface.FollowEnd.Ended),
            StringComparison.Ordinal);
    }

    // ---- what a follow costs (DD253) -------------------------------------------------------------

    [Fact]
    public void A_follow_costs_what_it_reads_rather_than_the_square_of_it()
    {
        // `--out` lifts the ceiling on purpose, so the only bounds left are the deadline and the
        // pattern. Re-splitting the whole buffer per chunk spent the deadline splitting; doubling the
        // input has to roughly double the time, not quadruple it.
        static TimeSpan Cost(int lines)
        {
            var body = Enumerable.Range(0, lines)
                .Select(i => $"2024-01-01T00:00:00.000000000Z line {i} of a service that does not stop\n")
                .SelectMany(l => Frame(1, l))
                .ToArray();

            // Warm first, so the measurement is the work rather than the first-call JIT.
            AgentSurface.Follow(
                new MemoryStream(body), new LogQuery(), "never arrives", Patience, ceiling: false);

            var started = System.Diagnostics.Stopwatch.StartNew();
            AgentSurface.Follow(
                new MemoryStream(body), new LogQuery(), "never arrives", Patience, ceiling: false);
            return started.Elapsed;
        }

        var small = Cost(4_000);
        var large = Cost(16_000);

        // Four times the input. Linear lands near 4x, quadratic near 16x; the bar is deliberately
        // slack because this is wall clock on a shared machine, and it still separates the two.
        var ratio = large.TotalMilliseconds / Math.Max(small.TotalMilliseconds, 1);
        Assert.True(ratio < 9, $"four times the input cost {ratio:F1} times the time, which is not linear.");
    }

    [Fact]
    public void The_tally_counts_only_what_the_query_would_keep()
    {
        var tally = new LogTally(new LogQuery(MinimumLevel: LogLevel.Error));
        tally.Add(new LogChunk(LogStream.StdOut, "INFO warming up\n"));
        var quiet = tally.Tokens;

        tally.Add(new LogChunk(LogStream.StdOut, "ERROR connect refused\n"));

        // Both lines are read, and only the one the filter keeps is charged for.
        Assert.Equal(0, quiet);
        Assert.True(tally.Tokens > 0);
        Assert.Equal(2, tally.Lines.Count);
    }

    [Fact]
    public void The_tally_charges_a_deduped_repeat_nothing()
    {
        var tally = new LogTally(new LogQuery(Dedup: true));
        tally.Add(new LogChunk(LogStream.StdOut, "connect ECONNREFUSED\n"));
        var once = tally.Tokens;

        for (var i = 0; i < 50; i++)
        {
            tally.Add(new LogChunk(LogStream.StdOut, "connect ECONNREFUSED\n"));
        }

        // A restart loop is the case `--dedup` exists for, and it must not walk into the ceiling on
        // the strength of lines that collapse into one row.
        Assert.Equal(once, tally.Tokens);
        Assert.Equal(51, tally.Lines.Count);
    }

    [Fact]
    public void The_tally_hands_back_only_the_lines_a_chunk_completed()
    {
        var tally = new LogTally(new LogQuery());

        Assert.Empty(tally.Add(new LogChunk(LogStream.StdOut, "half a li")));

        // Both of these, and not the whole buffer again: matching only the fresh lines is what makes
        // the follow linear.
        var fresh = tally.Add(new LogChunk(LogStream.StdOut, "ne\nand another\n"));
        Assert.Equal(["half a line", "and another"], fresh.Select(l => l.Text));
        Assert.Equal(2, tally.Lines.Count);

        // The carry is a line all the same, once nothing more is coming.
        Assert.Empty(tally.Flush());
    }

    // ---- what the surface refuses ----------------------------------------------------------------

    private static string ApiPath(string endpoint) => $"/{DockerApi.ApiVersion}/{endpoint}";

    private static FakeDockerDaemon Daemon() => new FakeDockerDaemon()
        .Fails(ApiPath("_ping"), "200 OK", "OK")
        .Json(ApiPath("containers/json?all=1"), Containers);

    private static int Logs(FakeDockerDaemon daemon, params string[] arguments) =>
        Logs(daemon, new StringWriter(), arguments);

    private static int Logs(FakeDockerDaemon daemon, TextWriter output, params string[] arguments)
    {
        using var api = new DockerApi(daemon.PipeName);
        return AgentSurface.Read(AgentSurface.Find(["read", "logs"])!, api, arguments, output);
    }

    /// <summary>
    /// A daemon that hands over a container's log the way the real one frames it (DD255).
    /// </summary>
    /// <remarks>
    /// Counted rather than close-delimited, so the body ends where the count says and the follow ends
    /// on the stream. That is the container exiting, which is one of the endings a follow has, and it
    /// is the one a test can reach without teaching the fake to write half a body and pause.
    /// </remarks>
    private static FakeDockerDaemon Printing(params string[] lines)
    {
        var body = lines.SelectMany(l => Frame(1, l)).ToArray();
        var head = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\n"
            + $"Content-Length: {body.Length}\r\nApi-Version: 1.55\r\n\r\n");

        return Daemon().Raw(
            ApiPath("containers/aaaaaaaaaaaa0000/logs?stdout=1&stderr=1&tail=0&follow=1&timestamps=1"),
            [.. head, .. body]);
    }

    // ---- the exit code a session branches on (DD255) ---------------------------------------------

    [Fact]
    public async Task A_pattern_that_arrives_exits_zero_through_the_surface()
    {
        await using var daemon = Printing("migrating\n", "seed complete\n");
        var output = new StringWriter();

        var code = Logs(daemon, output, "shop-api-1", "--follow", "--until", "seed complete");

        Assert.Equal(0, code);
        Assert.Contains("seed complete", output.ToString(), StringComparison.Ordinal);

        // A pass says nothing about the pattern; the payload is the answer.
        Assert.DoesNotContain("until  ", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pattern_that_never_arrives_exits_one_and_still_hands_back_what_did()
    {
        await using var daemon = Printing("migrating\n", "rolling back\n");
        var output = new StringWriter();

        var code = Logs(daemon, output, "shop-api-1", "--follow", "--until", "seed complete");

        // The value a session branches on, read where it is decided rather than a layer below it.
        Assert.Equal(1, code);
        Assert.Contains("rolling back", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("seed complete", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_follow_with_no_pattern_exits_zero_when_the_container_stops_printing()
    {
        await using var daemon = Printing("one\n", "two\n");
        var output = new StringWriter();

        Assert.Equal(0, Logs(daemon, output, "shop-api-1", "--follow"));
        Assert.Contains("two", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missed_pattern_writing_a_file_writes_it_and_still_exits_one()
    {
        var scratch = Directory.CreateTempSubdirectory("freewilly-follow-out");
        try
        {
            var target = System.IO.Path.Combine(scratch.FullName, "seed.log");
            await using var daemon = Printing("migrating\n");
            var output = new StringWriter();

            var code = Logs(
                daemon, output, "shop-api-1", "--follow", "--until", "seed complete", "--out", target);

            // Both halves: what was read is on disk, and the code still says the claim failed.
            Assert.Equal(1, code);
            Assert.True(File.Exists(target));
            Assert.Contains("migrating", File.ReadAllText(target), StringComparison.Ordinal);
            Assert.Contains("wrote ", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    // ---- what the surface refuses ----------------------------------------------------------------

    [Theory]
    [InlineData("--until", "seed complete")]
    [InlineData("--timeout", "5s")]
    public async Task A_bound_on_a_follow_is_refused_without_the_follow(string flag, string value)
    {
        await using var daemon = Daemon();

        // Handing back the plain read would answer a question the caller did not ask, and the answer
        // would look like a success.
        Assert.Equal(2, Logs(daemon, "shop-api-1", flag, value));
        Assert.Empty(daemon.Requested);
    }

    [Fact]
    public async Task A_timeout_that_is_not_a_number_is_refused_before_anything_is_read()
    {
        await using var daemon = Daemon();

        Assert.Equal(2, Logs(daemon, "shop-api-1", "--follow", "--timeout", "soon"));
        Assert.Empty(daemon.Requested);
    }

    [Fact]
    public async Task An_empty_pattern_is_refused_rather_than_matching_every_line()
    {
        await using var daemon = Daemon();

        Assert.Equal(2, Logs(daemon, "shop-api-1", "--follow", "--until", ""));
        Assert.Empty(daemon.Requested);
    }

    [Fact]
    public async Task A_follow_asks_the_daemon_to_follow_and_starts_from_now()
    {
        var daemon = Daemon().Fails(
            ApiPath("containers/aaaaaaaaaaaa0000/logs?stdout=1&stderr=1&tail=0&follow=1&timestamps=1"),
            "200 OK",
            "");
        await using var _ = daemon;

        // tail=0 is the "from now" half: the run a caller wants to watch is the one it is about to
        // make, and `--since` is already the word for replaying what came before.
        Assert.Equal(0, Logs(daemon, "shop-api-1", "--follow", "--timeout", "1s"));
        Assert.Contains(
            daemon.Requested,
            r => r.Contains("tail=0&follow=1", StringComparison.Ordinal));
    }

    // ---- what the digest still does --------------------------------------------------------------

    [Fact]
    public void What_a_follow_collected_is_a_payload_the_digest_reads_as_usual()
    {
        using var stream = Live(
            "2024-01-01T00:00:01.000000000Z ERROR connect refused\n",
            "2024-01-01T00:00:02.000000000Z ERROR connect refused\n",
            "2024-01-01T00:00:03.000000000Z ready\n");

        var followed = AgentSurface.Follow(stream, Query(), "ready", Patience, ceiling: true);
        var rendered = LogDigest.Render(
            followed.Lines, Query() with { Dedup = true });

        // Collecting and then rendering is what keeps the dedup, the level filter and the cursor
        // working; a line-at-a-time print would have none of the three.
        Assert.Contains("× 2", rendered.Text, StringComparison.Ordinal);
        Assert.NotNull(rendered.Cursor);
    }
}
