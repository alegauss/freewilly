using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using FreeWilly.Core.Engine;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// The ending of a connection, where the end of the stream is the result (DD259).
/// </summary>
/// <remarks>
/// Measured on 31 August 2026: a container that exited 0 was reported as a failure by every client
/// that reads a hijacked stream to its end. <c>docker compose run</c> returned 1, <c>docker start
/// -a</c> returned 1, and <c>docker exec probe echo hi</c> printed <c>hi</c> and then returned 1 —
/// the payload delivered in full and only the ending wrong, which is what made it look like the
/// container's own failure.
///
/// <para>The A/B is what placed it on this side: the same daemon and the same container over the
/// distribution's unix socket returned 0. What the pipe changed was the ending. The relay tore a
/// connection down with <c>Disconnect</c>, and the Win32 call underneath discards what the pipe
/// still holds and answers the client's next read with ERROR_PIPE_NOT_CONNECTED — "no process is on
/// the other end of the pipe". A response with a Content-Length had been read whole by then, so the
/// error arrived after the answer and nothing ever noticed.</para>
///
/// <para><b>Why this reads the pipe through Win32 and not through a stream.</b> Written first with
/// a <see cref="NamedPipeClientStream"/>, this test passed against the defect: .NET folds
/// ERROR_PIPE_NOT_CONNECTED into a zero-byte read, so the one distinction under test is the one a
/// managed client cannot see. The client that reports the failure is Go's, and Go's named-pipe
/// package folds only ERROR_BROKEN_PIPE into end-of-file. The error code therefore <i>is</i> the
/// subject, and reading it is the only way to hold this fix.</para>
/// </remarks>
public sealed class PipeTeardownTests
{
    /// <summary>A pipe with nothing on the other end. The defect, as a number.</summary>
    private const int PipeNotConnected = 233;

    /// <summary>A pipe whose server closed its handle. Every client reads this as the end.</summary>
    private const int BrokenPipe = 109;

    private static string Pipe() => $"freewilly-teardown-{Guid.NewGuid():N}";

    /// <summary>What a hijacked endpoint answers: a body whose only framing is the close.</summary>
    private const string Hijacked =
        "HTTP/1.1 200 OK\r\nContent-Type: application/vnd.docker.raw-stream\r\n\r\nhi\n";

    // DllImport and not LibraryImport: the generated form needs unsafe code and does not marshal a
    // SafePipeHandle, and a handle that can be closed out from under this call is the one thing
    // worth keeping safe here.
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        SafePipeHandle handle,
        byte[] buffer,
        uint toRead,
        out uint read,
        nint overlapped);

    /// <summary>A backend that answers with an upgraded stream and then ends it.</summary>
    private sealed class Attaching : IEngineBackend
    {
        public IEngineChannel Open() => new Channel();

        private sealed class Channel : IEngineChannel
        {
            private readonly MemoryStream _in = new();

            public Stream ToEngine => _in;

            public Stream FromEngine { get; } =
                new MemoryStream(Encoding.ASCII.GetBytes(Hijacked));

            public void Dispose()
            {
                _in.Dispose();
                FromEngine.Dispose();
            }
        }
    }

    /// <summary>How one client's connection ended, in the terms the client itself has.</summary>
    /// <param name="Read">Everything the client got before the stream stopped.</param>
    /// <param name="Ended">The Win32 code the last read gave, or zero where it read zero bytes.</param>
    private readonly record struct Ending(string Read, int Ended);

    /// <summary>
    /// Attach, read to the end, and report the ending — the whole of what a docker client does.
    /// </summary>
    private static async Task<Ending> AttachAsync(string pipe)
    {
        // Not asynchronous, unlike every other client in this suite: an overlapped handle cannot be
        // read by the blocking call below, and the blocking call is what surfaces the code.
        using var client = new NamedPipeClientStream(
            ".", pipe, PipeDirection.InOut, PipeOptions.None);

        await client.ConnectAsync(30000);

        client.Write(Request());
        client.Flush();

        var got = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            if (!ReadFile(client.SafePipeHandle, buffer, (uint)buffer.Length, out var read, 0))
            {
                return new Ending(
                    Encoding.ASCII.GetString(got.ToArray()), Marshal.GetLastWin32Error());
            }

            if (read == 0)
            {
                return new Ending(Encoding.ASCII.GetString(got.ToArray()), 0);
            }

            got.Write(buffer.AsSpan(0, (int)read));
        }
    }

    /// <summary>An attach, which is what a client sends to stop the connection being HTTP.</summary>
    private static byte[] Request() => Encoding.ASCII.GetBytes(
        "POST /v1.45/containers/probe/attach?stream=1&stdout=1 HTTP/1.1\r\n"
        + "Host: docker\r\nConnection: Upgrade\r\nUpgrade: tcp\r\nContent-Length: 0\r\n\r\n");

    /// <summary>Name a Win32 ending, so a failure says what happened rather than which number.</summary>
    private static string Named(int code) => code switch
    {
        0 => "a zero-byte read",
        BrokenPipe => "ERROR_BROKEN_PIPE, the server's handle closing",
        PipeNotConnected => "ERROR_PIPE_NOT_CONNECTED, the pipe disconnected under the client",
        _ => $"Win32 {code}",
    };

    [Fact]
    public async Task A_client_reading_an_upgraded_stream_to_its_end_is_given_an_end_and_not_an_error()
    {
        var pipe = Pipe();

        await using var relay = new EnginePipeRelay(new Attaching(), pipe);
        relay.Start();

        var ending = await AttachAsync(pipe);

        // The payload first, because it is the half that always worked and the half that made the
        // defect hard to place: a client that got every byte and then failed anyway.
        Assert.Equal(Hijacked, ending.Read);

        Assert.True(
            ending.Ended is BrokenPipe or 0,
            "the relay disconnected the pipe instead of closing it, so a client that reads a "
            + "hijacked stream to its end reads a failure where the stream ended — got "
            + Named(ending.Ended));
    }

    [Fact]
    public async Task A_client_that_stops_reading_never_reaches_the_drain_at_all()
    {
        // DD264, and the answer turned out to be that the state the deadline guards cannot occur.
        //
        // Driven on 31 August 2026, with the deadline shortened to 250 ms so an expiry could not be
        // missed for want of waiting: a client that connects, sends an attach and then never reads
        // leaves the relay at holding=1 and undrained=0 for as long as it likes. The teardown is
        // never entered, so there is never anything to drain.
        //
        // The reason is the listener's own shape. The server instances are created with no buffers
        // reserved, so a write into the pipe does not complete until the client takes it — and the
        // teardown is only reached once a pump has ended. A client that is not reading blocks the
        // pump that would have to finish first, so the connection stays in the serve rather than
        // arriving at the close with bytes still in flight.
        //
        // The deadline stays. It costs nothing on a path it never reaches, and the property it
        // depends on is a buffer size two arguments away from being changed — at which point the
        // wait becomes real and unbounded. This test is what would notice: undrained moving off
        // zero here means the drain became reachable.
        var pipe = Pipe();

        await using var relay = new EnginePipeRelay(new Attaching(), pipe)
        {
            DrainDeadline = TimeSpan.FromMilliseconds(250),
        };

        relay.Start();

        using var client = new NamedPipeClientStream(
            ".", pipe, PipeDirection.InOut, PipeOptions.None);
        await client.ConnectAsync(30000);

        client.Write(Request());
        client.Flush();

        // The serve is in flight, which is DD263's count saying where the connection actually is.
        Assert.True(
            await Reached(() => relay.Holding == 1),
            $"the relay is not holding the connection this client opened (holding={relay.Holding}, "
            + $"accepted={relay.Accepted}), so the state under test was never reached");

        // Well past the deadline, and nothing expired: there was nothing waiting to.
        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Equal(1, relay.Holding);
        Assert.Equal(0, relay.Undrained);

        // And the relay goes on serving while that one sits there, which is what makes the held
        // connection a held connection rather than a wedged relay.
        var next = await AttachAsync(pipe);
        Assert.Equal(Hijacked, next.Read);
        Assert.True(next.Ended is BrokenPipe or 0, Named(next.Ended));
    }

    [Fact]
    public async Task A_client_that_comes_back_and_reads_is_served_the_response_it_left_waiting()
    {
        // The mechanism above, shown from the other end. If the write really is waiting for the
        // client rather than lost, then a client that reads late gets everything — which is also
        // what says the held serve is a pause and not a leak.
        var pipe = Pipe();

        await using var relay = new EnginePipeRelay(new Attaching(), pipe)
        {
            DrainDeadline = TimeSpan.FromMilliseconds(250),
        };

        relay.Start();

        using var client = new NamedPipeClientStream(
            ".", pipe, PipeDirection.InOut, PipeOptions.None);
        await client.ConnectAsync(30000);

        client.Write(Request());
        client.Flush();

        Assert.True(await Reached(() => relay.Holding == 1));
        await Task.Delay(TimeSpan.FromSeconds(1));

        var got = new byte[Hijacked.Length];
        var read = 0;
        while (read < got.Length)
        {
            var some = client.Read(got, read, got.Length - read);
            if (some == 0)
            {
                break;
            }

            read += some;
        }

        Assert.Equal(Hijacked, Encoding.ASCII.GetString(got, 0, read));

        // Read late, served in full, and the serve then finishes on its own.
        Assert.True(
            await Reached(() => relay.Holding == 0),
            $"the client took everything and the relay still holds {relay.Holding}");

        Assert.Equal(0, relay.Undrained);
    }

    /// <summary>Wait for something to become true, or give up loudly.</summary>
    private static async Task<bool> Reached(Func<bool> condition, TimeSpan? patience = null)
    {
        var deadline = DateTime.UtcNow.Add(patience ?? TimeSpan.FromSeconds(60));
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
    public async Task A_connection_ended_cleanly_leaves_the_relay_serving_the_next_one()
    {
        // The ending is only right if it is also an ending. Draining before the close is a wait,
        // and a wait that did not finish would hold the connection open — so the claim being made
        // is that the fix ends connections rather than merely ending them politely.
        var pipe = Pipe();

        await using var relay = new EnginePipeRelay(new Attaching(), pipe);
        relay.Start();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var ending = await AttachAsync(pipe);
            Assert.True(ending.Ended is BrokenPipe or 0, Named(ending.Ended));
            Assert.Equal(Hijacked, ending.Read);
        }

        Assert.Equal(0, relay.Stumbles);
        Assert.True(relay.Accepting);
    }
}
