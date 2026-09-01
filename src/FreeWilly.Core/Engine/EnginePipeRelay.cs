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
    private int _holding;
    private int _undrained;

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
    ///
    /// <para>DD263 adds a fourth, and only where it is not zero. <see cref="Holding"/> is not a
    /// figure about the pipe at all, so on its own it would not have earned a place here; what earns
    /// it is where this sentence is read. The supervisor asks for the figures when the engine has
    /// gone quiet, and a quiet engine holding forty channels is a different failure from a quiet
    /// engine holding none — the first is this process, the second is the daemon. Silent at zero,
    /// like the stumbles, so a healthy relay still reads as one.</para>
    /// </remarks>
    public string Figures =>
        $"the relay accepted {Accepted}"
        + (Stumbles > 0 ? $" over {Stumbles} stumbles" : "")
        + (Holding > 0 ? $", holds {Holding} open" : "")
        + (Accepting ? " and is still accepting" : " and has stopped accepting");

    /// <summary>
    /// How many channels to the daemon this relay has opened and not yet released (DD263).
    /// </summary>
    /// <remarks>
    /// Every one of them is a <c>wsl.exe</c> attached to the daemon's socket. A serve that never
    /// finishes holds one for the rest of the host's life, and until this existed nothing in the
    /// product could say so: the count of accepted connections only ever goes up, and a relay
    /// holding forty channels reads exactly like one holding none.
    ///
    /// <para><b>Zero is not the healthy value, and that is the point.</b> A tray with a log window
    /// open is legitimately holding one, and a compose run driving several clients holds several, so
    /// no threshold here means anything on its own. What is worth reading is the number that stops
    /// coming back down — which is why this is a figure the supervisor quotes when something else
    /// has already gone wrong, and not a crossing it reports on its own.</para>
    ///
    /// <para>What it catches is a serve that stops making progress: the pumps never end, the finally
    /// never runs, and the channel stays open. What it cannot catch is a future edit that removes
    /// the release itself, because the count is kept beside it — that one is held by a test, which is
    /// where DD262 put it.</para>
    /// </remarks>
    public int Holding => Volatile.Read(ref _holding);

    /// <summary>
    /// What ended the accept loop, where something other than a stop did (DD179).
    /// </summary>
    /// <remarks>
    /// Null on a relay that is still accepting and on one that was disposed, which is the whole
    /// distinction this property exists to draw. The loop has always caught four exception types out
    /// of <see cref="NamedPipeServerStream.WaitForConnection"/> and returned on all of them, and only
    /// one of the four is a stop: <see cref="DisposeAsync()"/> closes the handle underneath a blocking
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

            // Counted from the moment there is something to release (DD263). Above the try's own
            // work rather than inside it, so a pump that never ends is a channel this says is held —
            // which is the reading the count exists for.
            Interlocked.Increment(ref _holding);

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
            if (channel is not null)
            {
                channel.Dispose();
                Interlocked.Decrement(ref _holding);
            }

            await EndAsync(client).ConfigureAwait(false);
        }
    }

    /// <summary>How long the drain is given before the handle is closed regardless (DD259).</summary>
    /// <remarks>
    /// <b>Never reached, and DD264 is the measurement rather than the guess.</b> DD259 wrote this
    /// against the client that stops reading without hanging up, and that client turns out not to
    /// arrive here at all: driven on 31 August 2026 with the deadline at 250 ms, a client that
    /// connected, sent an attach and then never read left the relay holding the connection and
    /// nothing undrained, for as long as the test cared to wait.
    ///
    /// <para>The listener's own shape is why. Server instances are created with no buffers reserved,
    /// so a write into the pipe does not complete until the client takes it, and the teardown is
    /// only reached once a pump has ended — so a client that is not reading blocks the pump that
    /// would have to finish first. The connection stays in the serve instead of arriving at the close
    /// with bytes still in flight, which is the only state a drain could wait on.</para>
    ///
    /// <para>It stays anyway. The path costs nothing when it is not taken, and what makes it
    /// unreachable is two arguments to <see cref="NamedPipeServerStreamAcl.Create"/> — change the
    /// buffer sizes and the wait becomes both real and unbounded. <see cref="Undrained"/> is what
    /// would say so.</para>
    ///
    /// <para>Shortened by a test and only by a test, the same seam and the same reason as
    /// <see cref="Listener"/>: an expiry that takes five seconds is one a suite cannot afford to
    /// wait for, and a deadline nothing ever tries to reach is indistinguishable from one that does
    /// not work.</para>
    /// </remarks>
    internal TimeSpan DrainDeadline { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How many connections were let go of with the client still not reading (DD264).
    /// </summary>
    /// <remarks>
    /// Zero, structurally, for as long as the listeners are created with no buffers — see
    /// <see cref="DrainDeadline"/> for why the state it counts cannot occur. It exists because that
    /// is a claim about a buffer size rather than a law: a count moving off zero here is the drain
    /// having become reachable, and the wait behind it is on a pool thread and bounded only by the
    /// deadline nobody has ever tested against a real client.
    ///
    /// <para>Internal rather than public, unlike <see cref="Stumbles"/>. The supervisor has nothing
    /// to do about a client that stopped reading — that is the client's business, and the relay has
    /// already handled it — so this is a fact for a test to hold and not a figure for a journal.
    /// What would change that is a machine where it moves.</para>
    /// </remarks>
    internal int Undrained => Volatile.Read(ref _undrained);

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
    private async Task EndAsync(NamedPipeServerStream client)
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

                // Counted rather than merely bounded (DD264). Which of the two finished is the
                // difference between a client that took its bytes and one that stopped reading and
                // was let go of, and the second is the only reason the deadline exists.
                var expiry = Task.Delay(DrainDeadline);
                if (await Task.WhenAny(drained, expiry).ConfigureAwait(false) == expiry)
                {
                    Interlocked.Increment(ref _undrained);
                }
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

    /// <summary>
    /// How long a dispose waits for the accept thread when nothing is racing it.
    /// </summary>
    /// <remarks>
    /// Generous rather than careless: nothing on the ordinary path is waiting, and the alternative
    /// to waiting is a thread that may still be inside a Win32 call.
    /// </remarks>
    public static readonly TimeSpan Join = TimeSpan.FromSeconds(5);

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => DisposeAsync(Join);

    /// <summary>Stop serving, waiting no longer than the caller has (DD279).</summary>
    /// <param name="join">How long to wait for the accept thread before leaving it.</param>
    /// <returns>The work.</returns>
    /// <remarks>
    /// <see cref="Join"/> is five seconds and a session ending has four, so a teardown taking the
    /// default could spend its whole budget here. DD271 put the terminate first, so this can no
    /// longer cost the unmount; what it still costs is everything after it, which is the account of
    /// the teardown DD273 exists to write.
    ///
    /// <para>Nothing changes about what happens at the deadline, and that is why a shorter one is
    /// safe: a thread that has not returned is left to the process, which is what the wait already
    /// did at five seconds and does no differently at two.</para>
    /// </remarks>
    public async ValueTask DisposeAsync(TimeSpan join)
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
        _accepting?.Join(join);
        _accepting = null;

        _stopping.Dispose();
    }
}
