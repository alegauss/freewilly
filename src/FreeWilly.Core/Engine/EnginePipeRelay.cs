using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace FreeWilly.Core.Engine;

/// <summary>
/// Serves the Windows named pipe every Docker client already looks for, and forwards each
/// connection to the daemon inside the distribution.
/// </summary>
/// <remarks>
/// A Linux <c>dockerd</c> cannot create a Windows named pipe — that is a Win32 object — so
/// something on this side has to, or `docker` needs a DOCKER_HOST in every shell and every script
/// the user already has. This is that something.
///
/// The pipe's ACL is the reason this exists rather than a forwarded port: only the account that
/// started the relay can connect, and the Engine API is equivalent to root on the machine.
/// </remarks>
public sealed class EnginePipeRelay : IAsyncDisposable
{
    /// <summary>The pipe name Docker clients use on Windows.</summary>
    public const string DefaultPipeName = "docker_engine";

    private readonly IEngineBackend _backend;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Lock _listeners = new();
    private Thread? _accepting;
    private NamedPipeServerStream? _listening;

    /// <summary>
    /// The one free listener, held where both the accept thread and a dispose can reach it.
    /// </summary>
    /// <remarks>
    /// Guarded, because the two touch it from different threads and the reason they do is the whole
    /// of DD142's second half: a blocking <c>WaitForConnection</c> is ended by closing the handle
    /// out from under it, so a dispose has to be able to find the object the loop is sitting on.
    /// </remarks>
    private NamedPipeServerStream? Listening
    {
        get { lock (_listeners) { return _listening; } }
        set { lock (_listeners) { _listening = value; } }
    }

    /// <summary>Construct a relay.</summary>
    /// <param name="backend">How a channel to the daemon is opened.</param>
    /// <param name="pipeName">The pipe to serve. Overridden in tests so a run is isolated.</param>
    public EnginePipeRelay(IEngineBackend backend, string pipeName = DefaultPipeName)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _backend = backend;
        _pipeName = pipeName;
    }

    /// <summary>How many connections have been accepted. For a test, and for a status line.</summary>
    public int Accepted { get; private set; }

    /// <summary>
    /// How many times creating the next listener failed and had to be tried again (DD142).
    /// </summary>
    /// <remarks>
    /// Zero on every healthy run. It is public because the failure this counts used to be invisible:
    /// the pipe simply stopped existing, every client on the machine failed together, and nothing
    /// anywhere said why.
    /// </remarks>
    public int Stumbles { get; private set; }

    /// <summary>Whether the accept thread is still alive (DD180).</summary>
    /// <remarks>
    /// Asked of the thread rather than inferred from <see cref="WhatEndedAccepting"/>, because the
    /// two answer different questions: that property says why a loop ended and this says whether one
    /// is running, and a relay that was never started has neither a reason nor a thread.
    /// </remarks>
    public bool Accepting => _accepting?.IsAlive ?? false;

    /// <summary>
    /// The relay's own account of itself, for the line that reports a silence (DD180).
    /// </summary>
    /// <remarks>
    /// DD173 split a ping's timeout into the stage that ran out of budget, and the first outage
    /// recorded with that vocabulary said <c>no connection</c> — the client never got a handle on the
    /// pipe. That is a statement about this class, and this class was the one participant the journal
    /// said nothing about: only <see cref="Stumbles"/> was ever written, and only when it moved, so a
    /// silence with no stumble in it left a reader unable to tell a relay refilling too slowly from
    /// one that had stopped accepting altogether.
    ///
    /// <para>Three figures and no more. What has been accepted says whether this relay was ever
    /// working, the stumbles say whether the machine was refusing instances, and the thread says
    /// whether there is still a loop to refill them — which between them name every way the Windows
    /// side of the pipe can be the reason a client got nothing.</para>
    /// </remarks>
    public string Figures =>
        $"the relay accepted {Accepted}"
        + (Stumbles > 0 ? $" over {Stumbles} stumbles" : "")
        + (Accepting ? " and is still accepting" : " and has stopped accepting");

    /// <summary>
    /// What ended the accept loop, where something other than a stop did (DD179).
    /// </summary>
    /// <remarks>
    /// Null on a relay that is still accepting and on one that was disposed, which is the whole
    /// distinction this property exists to draw. The loop has always caught four exception types out
    /// of <see cref="NamedPipeServerStream.WaitForConnection"/> and returned on all of them, and only
    /// one of the four is a stop: <see cref="DisposeAsync"/> closes the handle underneath a blocking
    /// wait on purpose, so the throw that follows is this class working as designed.
    ///
    /// <para><b>The other three are the loop dying, and it died silently.</b> Nothing replaces the
    /// loop, so the pipe is unserved for the rest of the process's life — every docker client on the
    /// machine fails together while the daemon inside the distribution goes on answering
    /// <c>pidof</c>, which is a healthy engine by every measure the host has. Measured on 24 August
    /// 2026: six polls of <c>no connection within 3s</c> against a daemon that was up, no stumble in
    /// the journal, and a full stop and start was what brought the pipe back.</para>
    ///
    /// <para>DD142 closed this same hole on the other side of the loop — a throw from creating the
    /// next listener used to end it, unobserved, with the identical consequence — and left this side
    /// open. Read by the host rather than logged from here, the way <see cref="Stumbles"/> is: this
    /// class has no journal and should not grow one, and the supervisor is already asking it
    /// questions every two seconds.</para>
    /// </remarks>
    public string? WhatEndedAccepting { get; private set; }

    /// <summary>
    /// How the next listener is made. Replaced only by a test that needs creation to fail (DD142).
    /// </summary>
    /// <remarks>
    /// A delegate rather than an interface because there is exactly one caller and one thing to
    /// vary. The defect below cannot be reproduced any other way: it needs
    /// <see cref="NamedPipeServerStreamAcl.Create"/> to fail transiently, which is a thing the
    /// operating system does under load and a test cannot ask for.
    /// </remarks>
    internal Func<NamedPipeServerStream>? Listener { get; set; }

    /// <summary>Start accepting. Returns as soon as the first listener is up.</summary>
    public void Start()
    {
        if (_accepting is not null)
        {
            throw new InvalidOperationException("this relay is already started");
        }

        // The first server instance is created synchronously, so a caller that polls the pipe
        // immediately afterwards cannot observe "not there yet" and conclude the engine is down.
        _listening = CreateServer();

        // A thread of its own, and not the thread pool (DD142). This loop is the only thing that
        // puts a listener back after one is taken, so for as long as it does not run, the pipe has
        // no free instance and every docker client on the machine waits — together, for as long as
        // the stall lasts, on an engine that goes on reporting itself healthy. That is the reported
        // symptom exactly.
        //
        // Measured: with the loop awaiting on the pool, a process busy enough to starve it left the
        // loop unrun for over a minute after a connection was accepted — the counter below still at
        // zero, so it was not the relay failing but the relay never being scheduled. The host is a
        // long-lived process that blocks pool threads on its own supervision, so this is not only a
        // property of a test suite.
        //
        // Synchronous, because that is what makes the thread mean anything: an `await` hands the
        // continuation back to the pool however the loop was started.
        _accepting = new Thread(() => AcceptLoop(_stopping.Token))
        {
            IsBackground = true,
            Name = "freewilly-pipe-accept",
        };

        _accepting.Start();
    }

    private NamedPipeServerStream CreateServer()
    {
        if (Listener is { } made)
        {
            return made();
        }

        // Only the current user. A forwarded TCP port cannot express this, and the Engine API is
        // not something to leave open to every process on the machine.
        var security = new PipeSecurity();
        var self = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("this process has no user SID");
        security.AddAccessRule(new PipeAccessRule(self, PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security);
    }

    private void AcceptLoop(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            var server = Listening;
            if (server is null)
            {
                return;
            }

            try
            {
                // Blocking, on this thread and nobody else's. Cancellation reaches it by DisposeAsync
                // closing the handle underneath, which is the documented way to end a blocking wait
                // on a named pipe and the reason Listening is held where that can find it.
                server.WaitForConnection();
            }
            catch (Exception exception) when (exception is OperationCanceledException
                or ObjectDisposedException or IOException or InvalidOperationException)
            {
                // DD179. Two exits, and until now they were one. A dispose cancels before it closes
                // the handle, so a stop always arrives here with the token already set — which makes
                // the token, and not the exception type, the thing that tells a shutdown from a
                // death. Typing the four apart would not: ObjectDisposedException is what a clean
                // stop throws and also what a handle lost some other way does.
                if (!cancellation.IsCancellationRequested)
                {
                    WhatEndedAccepting = $"{exception.GetType().Name}: {exception.Message}";
                }

                return;
            }

            Accepted++;
            var connected = server;

            // The next listener goes up before this connection is served, so a client is never
            // refused because the relay is busy with the one before it.
            //
            // DD142. This used to be a bare call, and a throw from it ended the loop — which is the
            // one failure in this class that takes the whole machine's docker with it. The instance
            // that just connected is disposed when its connection finishes, and with the loop gone
            // nothing replaces it, so the pipe stops existing altogether: every client, at once,
            // reports "cannot find the file" rather than anything about an engine. Nothing observed
            // the faulted task either — it is awaited only in DisposeAsync — so the account of what
            // happened was a stack trace nobody ever read.
            var next = NextListener(cancellation);

            // Serving happens on the pool and always did: one connection stalling there delays one
            // client, where the loop stalling delays every client there will ever be.
            _ = ServeAsync(connected, cancellation);

            if (next is null)
            {
                // Cancelled while retrying. The connection in hand is still served; what stops is
                // the accepting, which is what cancellation asked for.
                return;
            }

            Listening = next;
        }
    }

    /// <summary>Make the next listener, and keep trying while the machine refuses (DD142).</summary>
    /// <param name="cancellation">Stops the retrying, and nothing else.</param>
    /// <returns>The listener, or <see langword="null"/> where cancellation ended the wait.</returns>
    /// <remarks>
    /// Unbounded on purpose, and the alternative is what this replaces. A creation that fails is
    /// the machine being momentarily unable to give out a pipe instance — out of handles, or under
    /// a load that a compose run driving several clients at once is well able to produce — and it
    /// is a state that passes. Giving up after N attempts would restore exactly the defect being
    /// removed, only later and with a number attached to it.
    ///
    /// <para>The wait is short and flat rather than backing off. There is no server to be polite to
    /// here: this is a local object the kernel either can or cannot hand over, and every millisecond
    /// spent waiting is one where the docker command somebody has already typed is failing.</para>
    /// </remarks>
    private NamedPipeServerStream? NextListener(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                return CreateServer();
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException)
            {
                Stumbles++;
            }

            // Sleep and not Task.Delay: this loop owns its thread, so there is nothing to yield to
            // and a continuation would put it straight back on the pool it left.
            if (cancellation.WaitHandle.WaitOne(20))
            {
                return null;
            }
        }

        return null;
    }

    private async Task ServeAsync(NamedPipeServerStream client, CancellationToken cancellation)
    {
        IEngineChannel? channel = null;
        try
        {
            channel = _backend.Open();

            // Both directions, and the first one to end ends the pair: a response that completes
            // must close the client's read, and a client that hangs up must not leave the channel
            // holding a process.
            using var pair = CancellationTokenSource.CreateLinkedTokenSource(cancellation);

            // The client's direction reads its HTTP on the way past, so a bind source spelled the
            // Windows way is respelled the distribution's (DD125). Everything it does not understand
            // it forwards byte for byte, so this stays a pipe. The daemon's direction is the plain
            // copy it always was: nothing in a response names a source this had to change.
            var toEngine = EngineRequestFilter.PumpAsync(client, channel.ToEngine, pair.Token);
            var toClient = Pump(channel.FromEngine, client, pair.Token);
            await Task.WhenAny(toEngine, toClient).ConfigureAwait(false);
            await pair.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(
                Swallow(toEngine), Swallow(toClient)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException
            or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            // One client's connection failing is not the relay failing.
        }
        finally
        {
            channel?.Dispose();
            await EndAsync(client).ConfigureAwait(false);
        }
    }

    /// <summary>How long the drain is given before the handle is closed regardless (DD259).</summary>
    /// <remarks>
    /// Normally unreached: the listeners are created with no buffers, so a write has already been
    /// taken by the client before it completes and there is nothing left to drain. The deadline is
    /// here for the client that stops reading without hanging up, which would otherwise hold a
    /// thread for as long as it liked.
    /// </remarks>
    private static readonly TimeSpan DrainDeadline = TimeSpan.FromSeconds(5);

    /// <summary>End one connection the way a client reading to EOF can survive (DD259).</summary>
    /// <param name="client">The connection, served or failed.</param>
    /// <remarks>
    /// This used to call <c>Disconnect</c>. The Win32 call underneath discards whatever the pipe
    /// still holds and leaves the client's next read with ERROR_PIPE_NOT_CONNECTED — "no process is
    /// on the other end of the pipe". A response with a Content-Length had been read whole long
    /// before that, so the error landed after the client already had its answer and nothing ever
    /// noticed.
    ///
    /// <para>On an upgraded connection the end of the stream is the result. <c>docker compose
    /// run</c>, <c>docker start -a</c> and <c>docker exec</c> all read to the end and report what
    /// the last read said, so a container that exited 0 came back as exit 1 — with its output
    /// delivered in full, which is what made the failure look like it belonged to the container.
    /// </para>
    ///
    /// <para>Drain, then close, and never disconnect. The drain returns once the client has taken
    /// every byte, and closing the handle after it ends the pipe with ERROR_BROKEN_PIPE, which is
    /// the ending every client reads as a stream that finished.</para>
    /// </remarks>
    private static async Task EndAsync(NamedPipeServerStream client)
    {
        try
        {
            if (client.IsConnected)
            {
                // On a thread of its own because the wait is a blocking Win32 call, and abandoned
                // rather than awaited past the deadline: the dispose below is what ends it, and it
                // catches its own ending because by then there is nobody left to hand it to.
                var drained = Task.Run(() =>
                {
                    try
                    {
                        client.WaitForPipeDrain();
                    }
                    catch (Exception exception) when (exception is IOException
                        or ObjectDisposedException or InvalidOperationException)
                    {
                        // The client went away, which is this wait finishing by the other route.
                    }
                });

                await Task.WhenAny(drained, Task.Delay(DrainDeadline)).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException
            or InvalidOperationException)
        {
            // The client hung up first. Nothing to drain, and the close below is all that is left.
        }

        await client.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task Pump(Stream from, Stream to, CancellationToken cancellation)
    {
        var buffer = new byte[16 * 1024];
        while (!cancellation.IsCancellationRequested)
        {
            var read = await from.ReadAsync(buffer, cancellation).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            await to.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
            await to.FlushAsync(cancellation).ConfigureAwait(false);
        }
    }

    private static async Task Swallow(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException
            or ObjectDisposedException or OperationCanceledException)
        {
            // Expected: cancelling a pump is how a finished connection is torn down.
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);

        // Closing the handle is what ends the blocking wait. Cancellation alone cannot: the loop is
        // inside a Win32 call that knows nothing about a token, and it is sitting there precisely
        // because nothing has connected.
        NamedPipeServerStream? listening;
        lock (_listeners)
        {
            listening = _listening;
            _listening = null;
        }

        listening?.Dispose();

        // Joined with a deadline rather than indefinitely. A thread that has not noticed is a
        // background one and goes with the process; a dispose that never returns takes the tray
        // down with it, which is a worse failure than a thread lingering for a moment.
        _accepting?.Join(TimeSpan.FromSeconds(5));
        _accepting = null;

        _stopping.Dispose();
    }
}
