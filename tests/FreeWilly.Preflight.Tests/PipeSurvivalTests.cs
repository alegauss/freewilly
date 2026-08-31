using System.IO.Pipes;
using FreeWilly.Core.Engine;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// The relay outliving a machine that momentarily will not hand out a pipe instance (DD142).
/// </summary>
/// <remarks>
/// The failure this is about arrives in bursts: every docker client on the machine fails together
/// with "cannot find the file", nothing is done, and moments later all of them work again. That
/// wording is not an engine that is down — it is a pipe name that does not exist at all, which is a
/// different fact and has exactly one cause on this side.
///
/// <para>The accept loop replaces its listener the instant one is taken, and used to do it with a
/// bare call. A throw from that call ended the loop; the connection in hand was disposed when it
/// finished; and nothing created another. The pipe stopped existing, and because the loop's task is
/// awaited only in <c>DisposeAsync</c>, the exception that did it was never observed by anything.
/// </para>
/// </remarks>
public sealed class PipeSurvivalTests
{
    private static string Pipe() => $"freewilly-survival-{Guid.NewGuid():N}";

    /// <summary>A backend that answers anything with one small response.</summary>
    private sealed class Answering : IEngineBackend
    {
        public IEngineChannel Open() => new Channel();

        private sealed class Channel : IEngineChannel
        {
            private readonly MemoryStream _in = new();

            public Stream ToEngine => _in;

            public Stream FromEngine { get; } = new MemoryStream(
                System.Text.Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok"));

            public void Dispose()
            {
                _in.Dispose();
                FromEngine.Dispose();
            }
        }
    }

    /// <summary>Connect once, and say whether the pipe was there to connect to.</summary>
    private static async Task<bool> ReachableAsync(string pipe)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);

            // Generous, and it was not always. Two seconds looked right — the question is whether
            // the pipe exists, not whether the machine is busy — and it made this test flaky in the
            // full suite while passing five times out of five on its own. The accept loop waits
            // between attempts on a thread-pool thread, and a suite running eleven hundred tests at
            // once can leave that continuation queued for longer than the client was willing to
            // wait. That is a property of the suite and not of the relay.
            //
            // Patience is the only thing this changes. A pipe that is genuinely gone stays gone,
            // and the assertion still fails — it just no longer fails for being measured on a busy
            // machine, which is the one reading that says nothing about the defect.
            await client.ConnectAsync(30000);
            return true;
        }
        catch (Exception exception) when (exception is TimeoutException or IOException)
        {
            Why = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    /// <summary>Why the last connection did not land, for a failure message worth reading.</summary>
    private static string Why { get; set; } = "";

    [Fact]
    public async Task A_machine_that_refuses_one_pipe_instance_does_not_take_docker_down_with_it()
    {
        // The defect, driven. Creation fails twice — which is what an operating system under load
        // does, and what no test can ask it for — and before DD142 the second connection below
        // found no pipe at all, for good, on a relay whose engine was perfectly healthy.
        var pipe = Pipe();
        var attempt = 0;

        await using var relay = new EnginePipeRelay(new Answering(), pipe);
        relay.Listener = () =>
        {
            // The first one is the listener Start creates, and it has to succeed — a relay that
            // cannot come up at all is a different failure with a caller to report it to. What this
            // test is about is the replacement, which happens where nobody is watching.
            attempt++;
            if (attempt is 2 or 3)
            {
                throw new IOException("all pipe instances are busy");
            }

            return NamedPipeServerStreamAcl.Create(
                pipe,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                inBufferSize: 0,
                outBufferSize: 0,
                pipeSecurity: OnlyThisUser());
        };

        relay.Start();

        // The first connection takes the listener the start created, which is what makes the loop
        // reach for another one — and that is the call this test makes fail.
        Assert.True(await ReachableAsync(pipe), "the relay never served its first connection");

        // Waited for rather than timed. The accept loop runs on a thread-pool thread, and the full
        // suite starves that pool badly enough to leave its continuation queued for tens of seconds
        // — measured, at stumbles=0 with a thirty-second client timeout, which is the loop not
        // having run at all rather than the relay having failed. Asserting on a clock made this
        // test say "the pipe is gone" about a machine that was merely busy.
        //
        // What is actually being claimed has no clock in it: the loop meets both refusals and
        // carries on. Without the fix it meets the first, throws, and this count never moves — so
        // the deadline below is what fails, and it fails saying so.
        Assert.True(
            await Reached(() => relay.Stumbles == 2),
            $"the accept loop never got past a refused listener (stumbles={relay.Stumbles}), so "
            + "the pipe stopped existing and every docker client on this machine would fail "
            + "together with \"cannot find the file\"");

        // And with the loop past them, the pipe is there — which is the half the count cannot say.
        Assert.True(
            await ReachableAsync(pipe),
            $"the loop recovered and the pipe is still unreachable — {Why}");
    }

    /// <summary>Wait for something to become true, or give up loudly.</summary>
    /// <remarks>
    /// A deadline and not a sleep: on an idle machine this returns in milliseconds, and on one
    /// running the whole suite it waits as long as the pool makes it. Neither reading is about the
    /// relay, which is why nothing here asserts on how long it took.
    /// </remarks>
    private static async Task<bool> Reached(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(25);
        }

        return condition();
    }

    [Fact]
    public async Task The_host_can_read_the_count_without_reaching_inside_the_relay()
    {
        // The counter is only worth keeping if something says it out loud, and the host is the one
        // thing in a position to: it owns DD137's journal, and this is the event that leaves the
        // engine reading perfectly healthy while every docker client on the machine fails.
        //
        // A lifecycle that never started one answers zero rather than throwing — the supervisor
        // reads this on every turn of its loop, including the turns before there is a relay at all.
        await using var lifecycle = new EngineLifecycle(
            new FakeWsl(), new FakeDaemon(), new Answering());

        Assert.Equal(0, lifecycle.Stumbles);
    }

    [Fact]
    public async Task A_healthy_relay_stumbles_over_nothing()
    {
        // The other half, so the counter above cannot quietly become noise: a run with nothing
        // wrong reports nothing wrong, which is what makes a non-zero reading worth acting on.
        var pipe = Pipe();

        await using var relay = new EnginePipeRelay(new Answering(), pipe);
        relay.Start();

        Assert.True(await ReachableAsync(pipe));
        Assert.True(await ReachableAsync(pipe));
        Assert.Equal(0, relay.Stumbles);
    }

    [Fact]
    public async Task An_accept_loop_that_dies_says_what_killed_it()
    {
        // The defect, driven. The loop caught four exception types out of WaitForConnection and
        // returned on all of them, so the one ending that leaves the pipe unserved for the rest of
        // the process's life was indistinguishable from a clean stop — and wrote nothing either way.
        // Measured on 24 August 2026 as six polls of "no connection within 3s" against a daemon that
        // answered pidof throughout, with no line anywhere naming the relay.
        var pipe = Pipe();
        var attempt = 0;

        await using var relay = new EnginePipeRelay(new Answering(), pipe);
        relay.Listener = () =>
        {
            attempt++;

            var server = NamedPipeServerStreamAcl.Create(
                pipe,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                inBufferSize: 0,
                outBufferSize: 0,
                pipeSecurity: OnlyThisUser());

            // The first is the one Start creates and it has to work, or the loop never reaches the
            // wait this test is about. The replacement is handed over already closed, which is a
            // handle lost from under the loop — the shape of the failure, arriving as the exception
            // a clean stop also throws, which is exactly why the type cannot be what tells them
            // apart.
            if (attempt >= 2)
            {
                server.Dispose();
            }

            return server;
        };

        relay.Start();

        // Takes the working listener, which is what makes the loop reach for the closed one.
        Assert.True(await ReachableAsync(pipe), "the relay never served its first connection");

        Assert.True(
            await Reached(() => relay.WhatEndedAccepting is not null),
            "the accept loop ended and left no account of it, so a pipe that stopped being served "
            + "would read as a healthy engine for the rest of this process's life");

        // The type is in the sentence because it is the whole of what a reader has: the loop is
        // gone, nothing else on the machine observed the throw, and the journal line the host writes
        // from this is the only place the event exists.
        Assert.Contains(nameof(ObjectDisposedException), relay.WhatEndedAccepting!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_relay_that_was_stopped_blames_nothing()
    {
        // The other half, and it is not a formality. DisposeAsync ends the blocking wait by closing
        // the handle underneath it, so a clean stop throws the same ObjectDisposedException a lost
        // handle does — and a check written on the exception type would report every orderly
        // shutdown in this product as a relay that died.
        var pipe = Pipe();

        var relay = new EnginePipeRelay(new Answering(), pipe);
        relay.Start();
        Assert.True(await ReachableAsync(pipe));

        await relay.DisposeAsync();

        Assert.Null(relay.WhatEndedAccepting);
    }

    [Fact]
    public async Task The_host_can_read_what_killed_the_loop_without_reaching_inside_the_relay()
    {
        // Same seam as the stumble count, for the same reason: the relay has no journal and the
        // supervisor does. A lifecycle with no relay yet answers null rather than throwing, because
        // the loop that reads this runs before there is a relay at all.
        await using var lifecycle = new EngineLifecycle(
            new FakeWsl(), new FakeDaemon(), new Answering());

        Assert.Null(lifecycle.WhatEndedAccepting);
    }

    [Fact]
    public async Task A_working_relay_reports_what_it_has_served_and_that_it_is_still_serving()
    {
        // DD180. The figures are only worth carrying if a healthy relay reads as healthy in them,
        // which is what makes the other reading worth acting on — the same property the stumble
        // count is held to above.
        var pipe = Pipe();

        await using var relay = new EnginePipeRelay(new Answering(), pipe);
        relay.Start();

        Assert.True(await ReachableAsync(pipe));

        // Waited out rather than only counted, since DD263 put what the relay is holding into the
        // sentence. A connection is accepted before it is served, so a client that has hung up can
        // still have a serve in flight — and this asserts the whole string, which would then read
        // "holds 1 open" for as long as that lasted.
        Assert.True(await Reached(() => relay.Accepted == 1 && relay.Holding == 0));

        Assert.Equal("the relay accepted 1 and is still accepting", relay.Figures);
    }

    [Fact]
    public async Task A_relay_whose_loop_died_says_so_in_its_figures()
    {
        // The reading the journal exists to carry: a relay that accepted work and then stopped. It
        // is the sentence that separates DD179's defect from a machine merely too busy to refill a
        // pipe instance in time, and neither is visible from a status that only ever says Starting.
        var pipe = Pipe();
        var attempt = 0;

        await using var relay = new EnginePipeRelay(new Answering(), pipe);
        relay.Listener = () =>
        {
            attempt++;

            var server = NamedPipeServerStreamAcl.Create(
                pipe,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                inBufferSize: 0,
                outBufferSize: 0,
                pipeSecurity: OnlyThisUser());

            if (attempt >= 2)
            {
                server.Dispose();
            }

            return server;
        };

        relay.Start();
        Assert.True(await ReachableAsync(pipe));

        // And the serve settled, for the reason the test above waits for it: this asserts the whole
        // sentence, and DD263 gave the sentence a clause about what is still in flight.
        Assert.True(
            await Reached(() => relay.WhatEndedAccepting is not null && relay.Holding == 0));

        Assert.Equal("the relay accepted 1 and has stopped accepting", relay.Figures);
    }

    [Fact]
    public async Task The_host_reads_the_figures_through_the_lifecycle()
    {
        // Same seam as the other two, and the same answer before there is a relay: null rather than
        // a sentence, because a start has polls in it and none of them has a relay to describe.
        await using var lifecycle = new EngineLifecycle(
            new FakeWsl(), new FakeDaemon(), new Answering());

        Assert.Null(lifecycle.RelayFigures);
    }

    /// <summary>A backend whose channel never says anything and never ends (DD263).</summary>
    /// <remarks>
    /// The serve that does not finish, which is the only failure the count can catch by itself. Both
    /// pumps end up waiting — the daemon's because this never answers, the client's because a client
    /// that connects and sends nothing has nothing to forward — so the connection is held open with
    /// nothing wrong anywhere, exactly as an attach to an idle container is.
    /// </remarks>
    private sealed class Stalling : IEngineBackend
    {
        public IEngineChannel Open() => new Channel();

        private sealed class Channel : IEngineChannel
        {
            private readonly MemoryStream _in = new();

            public Stream ToEngine => _in;

            public Stream FromEngine { get; } = new Waiting();

            public void Dispose()
            {
                _in.Dispose();
                FromEngine.Dispose();
            }
        }

        /// <summary>A stream whose read is answered by cancellation and nothing else.</summary>
        private sealed class Waiting : Stream
        {
            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override async ValueTask<int> ReadAsync(
                Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                return 0;
            }

            public override int Read(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException();

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) =>
                throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException();
        }
    }

    [Fact]
    public async Task A_relay_whose_clients_have_all_gone_holds_nothing_and_says_nothing_about_it()
    {
        // The half that keeps the clause worth reading. Every relay is holding something some of the
        // time — a log window, a compose run — so a figure that appeared at any non-zero value would
        // be in every sentence this product writes, and the one reading worth acting on would be
        // buried in the ones that are not.
        var pipe = Pipe();

        await using var relay = new EnginePipeRelay(new Answering(), pipe);
        relay.Start();

        Assert.True(await ReachableAsync(pipe));
        Assert.True(await Reached(() => relay.Holding == 0));
        Assert.DoesNotContain("holds", relay.Figures, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_relay_holding_a_channel_open_says_how_many_it_is_holding()
    {
        // The reading nothing in the product had. Before DD263 the figures said "accepted 1 and is
        // still accepting" over a relay with a wsl.exe attached to the daemon and no client left to
        // read it — indistinguishable from a relay holding nothing, which is what made an engine
        // that leaks one channel per abandoned stream look healthy for the rest of its life.
        var pipe = Pipe();

        await using var relay = new EnginePipeRelay(new Stalling(), pipe);
        relay.Start();

        var client = new NamedPipeClientStream(
            ".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(30000);

        try
        {
            Assert.True(
                await Reached(() => relay.Holding == 1),
                $"the relay is serving a connection it opened a channel for and reports holding "
                + $"{relay.Holding}");

            Assert.Contains("holds 1 open", relay.Figures, StringComparison.Ordinal);
        }
        finally
        {
            await client.DisposeAsync();
        }

        // And it comes back down, which is the property that makes a count that does not worth
        // acting on. A number that only ever climbed would say the same thing about a relay serving
        // a hundred connections properly as about one that had leaked a hundred.
        Assert.True(
            await Reached(() => relay.Holding == 0),
            $"the client is gone and the relay still reports holding {relay.Holding}");

        Assert.DoesNotContain("holds", relay.Figures, StringComparison.Ordinal);
    }

    private static System.IO.Pipes.PipeSecurity OnlyThisUser()
    {
        var security = new System.IO.Pipes.PipeSecurity();
        var self = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        security.AddAccessRule(new PipeAccessRule(
            self,
            PipeAccessRights.FullControl,
            System.Security.AccessControl.AccessControlType.Allow));
        return security;
    }
}
