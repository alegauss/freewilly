using FreeWilly.Core.Engine;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// The endings DD260's drill left out: a stream that outlives the request, ended from either
/// side (DD262).
/// </summary>
/// <remarks>
/// DD260 drives `compose run`, `start -a` and `exec`, and all three end the same way — the
/// container exits, so the daemon stops writing and the relay's last act is a close over a stream
/// nobody is still reading. A follow ends the other way round: the client hangs up on a container
/// that is still producing output. Nothing asserted that, and it is not the same code path
/// arriving at the same place — the pump that ends first is the other one.
///
/// <para><b>`docker logs --follow` is not an HTTP upgrade, and it belongs here anyway.</b> The
/// client sends a plain GET with no <c>Connection: upgrade</c>, so the request filter never stops
/// parsing. What puts it in DD259's family is the response: no Content-Length, framed by the close,
/// so the end of the stream is the result exactly as it is for an attach. Filing it under
/// "upgraded" was loose; the property that matters is where the framing comes from.</para>
///
/// <para><b>Why there is no websocket case.</b> `/attach/ws` has no verb in the Docker CLI, so
/// testing it would mean testing a client written here rather than a client. It also would not
/// answer anything new: the last assertion below shows the relay decides to stop parsing from the
/// <c>Connection</c> header alone, so a websocket attach and a raw-stream attach are the same path
/// through it, byte for byte, from the head onward.</para>
/// </remarks>
public sealed class RelayStreamEndTests
{
    /// <summary>Long enough for a container to produce a line, short enough to fail fast.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    [FactWhenTheEngineAnswers]
    public async Task A_follow_that_ends_because_the_container_exited_is_reported_as_a_success()
    {
        // The half that matches DD260's three: the daemon stops writing, and what the client reads
        // is the end. `logs -f` on a container that exits returns when it does.
        //
        // Measured, and it is worth saying because the opposite would be assumed: with DD259's
        // teardown put back, this case still passes. The log client reads to the end and reports 0
        // over a pipe that was disconnected under it, where `attach` below reports 1 — so this
        // asserts that a follow streams and ends, and it is not a second guard on that defect.
        await using var engine = new LiveEngine.Served();
        var name = LiveEngine.Named();

        var started = engine.Run(
            LiveEngine.Anywhere, "run", "-d", "--name", name, LiveEngine.Image,
            "sh", "-c", "echo first; sleep 1");
        Assert.Equal(0, started.ExitCode);

        try
        {
            var followed = engine.Run(LiveEngine.Anywhere, "logs", "-f", name);

            Assert.Contains("first", followed.Output, StringComparison.Ordinal);
            Assert.Equal(0, followed.ExitCode);
        }
        finally
        {
            engine.Run(LiveEngine.Anywhere, "rm", "-f", name);
        }
    }

    [FactWhenTheEngineAnswers]
    public async Task An_attach_that_ends_because_the_container_exited_reports_its_exit_code()
    {
        // An attach is the upgraded one, and it reports the container's code rather than its own.
        // The container waits before it exits so the attach cannot lose the race to connect.
        await using var engine = new LiveEngine.Served();
        var name = LiveEngine.Named();

        var started = engine.Run(
            LiveEngine.Anywhere, "run", "-d", "--name", name, LiveEngine.Image,
            "sh", "-c", "sleep 2; echo done; exit 4");
        Assert.Equal(0, started.ExitCode);

        try
        {
            var attached = engine.Run(LiveEngine.Anywhere, "attach", name);

            Assert.Contains("done", attached.Output, StringComparison.Ordinal);
            Assert.Equal(4, attached.ExitCode);
        }
        finally
        {
            engine.Run(LiveEngine.Anywhere, "rm", "-f", name);
        }
    }

    [FactWhenTheEngineAnswers]
    public async Task A_follow_the_client_walks_away_from_takes_its_channel_with_it()
    {
        // The ending nothing asserted. A container that keeps writing, a follow against it, and the
        // client gone while the daemon is still producing output — so the pump that ends first is
        // the client's, which is the path the other cases never reach.
        //
        // What has to hold is that the relay acts on it. A channel that leaked would leave a
        // wsl.exe attached to the daemon for the rest of the host's life, one per abandoned follow,
        // and the product has nothing that would say so.
        await using var engine = new LiveEngine.Served();
        var name = LiveEngine.Named();

        var started = engine.Run(
            LiveEngine.Anywhere, "run", "-d", "--name", name, LiveEngine.Image,
            "sh", "-c", "while true; do echo tick; sleep 1; done");
        Assert.Equal(0, started.ExitCode);

        try
        {
            var line = engine.ReadALineThenEnd(["logs", "-f", name], Patience);

            // The follow has to have been streaming before it was ended, or what follows asserts
            // nothing: a client that never connected leaves no channel to leak either.
            Assert.Contains("tick", line, StringComparison.Ordinal);

            Assert.True(
                await Settled(() => engine.OpenChannels == 0),
                $"{engine.OpenChannels} channel(s) to the daemon are still open after the client "
                + "that owned them was gone, so an abandoned follow leaves a wsl.exe attached to "
                + "the daemon for the rest of the host's life");
        }
        finally
        {
            engine.Run(LiveEngine.Anywhere, "rm", "-f", name);
        }
    }

    [Fact]
    public void An_upgrade_is_read_off_the_connection_header_and_not_off_the_path()
    {
        // Why the websocket endpoints need no case of their own, asserted rather than claimed. The
        // relay stops parsing on the Connection header alone, so `attach` and `attach/ws` reach the
        // raw copy through the same branch and are the same connection from the head onward. A
        // second live case would drive the same code twice and read as coverage it is not.
        var raw = Upgrade("POST /v1.45/containers/probe/attach?stream=1", "tcp");
        var socket = Upgrade("GET /v1.45/containers/probe/attach/ws?stream=1", "websocket");

        Assert.NotNull(raw);
        Assert.NotNull(socket);
        Assert.True(raw.Upgrades);
        Assert.True(socket.Upgrades);

        // And the other direction, so the property above is not simply always true: a follow is a
        // plain GET, and its ending comes from the response rather than from an upgrade.
        var follow = EngineRequestFilter.RequestHead.Parse(
            System.Text.Encoding.ASCII.GetBytes(
                "GET /v1.45/containers/probe/logs?follow=1 HTTP/1.1\r\nHost: docker\r\n\r\n"));

        Assert.NotNull(follow);
        Assert.False(follow.Upgrades);
    }

    private static EngineRequestFilter.RequestHead? Upgrade(string start, string protocol) =>
        EngineRequestFilter.RequestHead.Parse(
            System.Text.Encoding.ASCII.GetBytes(
                $"{start} HTTP/1.1\r\nHost: docker\r\nConnection: Upgrade\r\n"
                + $"Upgrade: {protocol}\r\nContent-Length: 0\r\n\r\n"));

    /// <summary>Wait for something to become true, or give up loudly.</summary>
    /// <remarks>
    /// A deadline and not a sleep. The relay notices a client hanging up when a pump's read fails,
    /// which is prompt on an idle machine and is a scheduling question on one running this suite —
    /// so nothing here asserts how long it took, only that it happened.
    /// </remarks>
    private static async Task<bool> Settled(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.Add(Patience);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return condition();
    }
}
