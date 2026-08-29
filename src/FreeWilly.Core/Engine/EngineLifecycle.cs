using System.Diagnostics;

namespace FreeWilly.Core.Engine;

/// <summary>The daemon process, behind a seam so the lifecycle's decisions are testable.</summary>
public interface IDaemonProcess : IDisposable
{
    /// <summary>Whether it is running right now.</summary>
    bool Alive { get; }

    /// <summary>
    /// What the launcher said on its way out, or <see langword="null"/> while it is still up or
    /// where its ending was asked for (DD162).
    /// </summary>
    /// <remarks>
    /// The daemon runs inside a distribution and a Windows process launches it, so there are two
    /// ways a launch dies and only one of them has ever been readable. A daemon that started and
    /// then failed wrote its reason to its own log; a launch that never got that far — a
    /// distribution WSL would not boot, a virtual machine Hyper-V refused — failed in the launcher,
    /// which is the half this carries.
    /// </remarks>
    string? LastWords { get; }

    /// <summary>Start it. Returns once launched, which is long before the socket exists.</summary>
    void Launch();

    /// <summary>Stop it.</summary>
    void Stop();
}

/// <summary>
/// Starts and stops the engine, and reports a state that is never ahead of the truth.
/// </summary>
/// <remarks>
/// The order matters and each step's failure is named separately, because there are three ways this
/// goes wrong and they have three different remedies: the distribution is not provisioned, the
/// daemon starts and dies, or the daemon runs and nothing on Windows is serving the pipe.
/// </remarks>
public sealed class EngineLifecycle : IAsyncDisposable
{
    /// <summary>Where the daemon listens inside the distribution.</summary>
    public const string SocketPath = "/var/run/docker.sock";

    /// <summary>Where the daemon's own log is kept inside the distribution.</summary>
    public const string LogPath = "/var/log/dockerd.log";

    private readonly IWsl _wsl;
    private readonly IDaemonProcess _daemon;
    private readonly IEngineBackend _backend;
    private readonly string _pipeName;
    private EnginePipeRelay? _relay;

    /// <summary>
    /// How many times the relay had to ask twice for a pipe instance (DD142).
    /// </summary>
    /// <remarks>
    /// Zero on every healthy run, and it is exposed here because the host is the only thing in a
    /// position to say so out loud. What this counts used to end the relay outright, leaving no pipe
    /// for any client on the machine and nothing anywhere that named the reason.
    /// </remarks>
    public int Stumbles => _relay?.Stumbles ?? 0;

    /// <summary>
    /// What ended the relay's accept loop, where anything other than a stop did (DD179).
    /// </summary>
    /// <remarks>
    /// Exposed here for the reason <see cref="Stumbles"/> is: the relay counts and the host speaks,
    /// and a lifecycle with no relay yet answers null rather than throwing, because the supervisor
    /// reads this on every turn including the ones before there is a relay at all.
    /// </remarks>
    public string? WhatEndedAccepting => _relay?.WhatEndedAccepting;

    /// <summary>
    /// The relay's account of itself, or <see langword="null"/> where there is no relay (DD180).
    /// </summary>
    /// <remarks>
    /// Null and not a sentence about zero. A poll taken before this lifecycle has served anything has
    /// no relay to report on, and manufacturing "accepted 0 and has stopped accepting" for it would
    /// put the signature of the failure DD179 hunts into every line written during a start.
    /// </remarks>
    public string? RelayFigures => _relay?.Figures;

    private bool _launched;

    /// <summary>
    /// What the machine said about the daemon, kept for the run of silence in progress (DD175).
    /// </summary>
    /// <remarks>
    /// Asked once and reused, and the alternative is what DD134 removed. A subprocess on every poll
    /// is the load that times out the ping beside it, and here it would close a loop: each quiet
    /// poll would spawn two more <c>wsl.exe</c> children onto a machine whose pings are already
    /// losing a race for process creation, making the next poll likelier to be quiet too.
    ///
    /// <para>Cleared by any answer, so it never outlives the failure it describes. Within one
    /// failure it does go stale — the verdict line carries what was found up to thirty seconds
    /// earlier — and that is the trade taken deliberately: the finding belongs to the incident
    /// rather than to the poll, and the line that first reports it is written at the moment it was
    /// asked (DD174).</para>
    ///
    /// <para>Since DD181 the staleness is in the sentence rather than only in this remark. The
    /// first poll of a silence states the finding flat, because it is being made and reported in the
    /// same breath; every poll after it says the finding is as of that first one. A reader who wants
    /// the clock follows the pointer to DD174's line, which has it.</para>
    /// </remarks>
    private string? _found;

    /// <summary>Construct a lifecycle.</summary>
    /// <param name="wsl">The WSL command.</param>
    /// <param name="daemon">The daemon process.</param>
    /// <param name="backend">How the relay reaches the daemon.</param>
    /// <param name="pipeName">The pipe to serve; overridden in tests.</param>
    /// <param name="distribution">
    /// The distribution this install owns, or <see langword="null"/> to ask the machine (DD55).
    /// </param>
    public EngineLifecycle(
        IWsl wsl,
        IDaemonProcess daemon,
        IEngineBackend backend,
        string pipeName = EnginePipeRelay.DefaultPipeName,
        string? distribution = null)
    {
        ArgumentNullException.ThrowIfNull(wsl);
        ArgumentNullException.ThrowIfNull(daemon);
        ArgumentNullException.ThrowIfNull(backend);
        _wsl = wsl;
        _daemon = daemon;
        _backend = backend;
        _pipeName = pipeName;
        Distribution = distribution ?? new EnginePaths().DistributionName;
    }

    /// <summary>
    /// The distribution this install owns — the current name, or the legacy one where an install
    /// made before the rename is being adopted (DD55).
    /// </summary>
    public string Distribution { get; }

    /// <summary>Whether the distribution this tool owns is registered, as far as can be told.</summary>
    /// <remarks>
    /// A probe that did not answer reads as registered here, and that is the safe direction for
    /// every caller of this property: a start tries rather than refusing, and a stop terminates
    /// rather than skipping. The one place the difference has to be visible reads
    /// <see cref="Registration"/> instead.
    /// </remarks>
    public bool DistributionRegistered => Registration is not false;

    /// <summary>
    /// Whether the owned distribution is registered, or <see langword="null"/> where the probe did
    /// not answer (DD134).
    /// </summary>
    /// <remarks>
    /// This shells out to <c>wsl --list</c>, and on the loaded machine the engine host exists for,
    /// that command times out exactly as readily as the ping beside it. Folding a timeout into
    /// "not registered" manufactured the one answer the watch was entitled to act on out of nothing
    /// but load — so the two are told apart here, at the only place that can still tell.
    /// </remarks>
    private bool? Registration
    {
        get
        {
            var listed = _wsl.Run("--list", "--quiet");
            return listed.Succeeded
                ? listed.Output
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Any(line => line.Trim().Equals(Distribution, StringComparison.OrdinalIgnoreCase))
                : null;
        }
    }

    /// <summary>Read the state, by asking the engine rather than by remembering what was asked.</summary>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>The state and what was observed.</returns>
    public async Task<EngineStatus> StatusAsync(CancellationToken cancellation = default)
    {
        var ping = await EnginePing.AskAsync(_pipeName, cancellation: cancellation)
            .ConfigureAwait(false);
        if (ping.Answered)
        {
            // The engine proved itself, so whatever was found about a previous silence describes an
            // engine that no longer exists (DD175).
            _found = null;
            return new EngineStatus(EngineState.Running,
                $"the engine answered on \\\\.\\pipe\\{_pipeName}", ping.ApiVersion);
        }

        // The held handle before the subprocess, once this lifecycle has launched a daemon of its
        // own (DD134). HasExited is a local question about a process we own: the machine's load
        // cannot slow it down, and it cannot answer "gone" about something that is running.
        // `wsl --list` is neither of those things, and asking it on every poll was itself part of
        // the load that timed out the ping above.
        if (_launched)
        {
            if (!_daemon.Alive)
            {
                return new EngineStatus(EngineState.Stopped, WhatTookTheDaemon())
                {
                    Conclusive = true,
                };
            }

            // DD175. The handle above says the launcher has not exited, and this sentence used to
            // report that as "the daemon is running" — a claim about a Linux process, made from a
            // Windows one. They come apart exactly where it matters: a virtual machine lost to a
            // suspend leaves the wsl.exe on this side perfectly alive, which is the failure the
            // whole supervisor exists for and the one the line described as a healthy daemon.
            // DD181. The reading is cached for the load — asking `wsl --exec` six times over a
            // machine whose pings are already losing a race for process creation would make the next
            // poll likelier to be quiet too — and every poll after the first is therefore quoting an
            // older observation. Measured on 24 August 2026: the verdict line read "the daemon is
            // running and no connection within 3s — 6 polls in a row", and that opening clause was
            // established twenty-six seconds earlier.
            //
            // A reader takes an undated clause as the state at the verdict, and in the failure this
            // supervisor exists for — a virtual machine lost under the host's feet — those seconds
            // are exactly where the daemon stops being there. So the reuse is marked, and marked by
            // pointing at the line that carries the timestamp rather than by restating one: DD174
            // already wrote the crossing, it is a few lines up the same file, and a clock of its own
            // here would be a second answer to a question already answered.
            if (_found is null)
            {
                _found = WhatIsThere();
                return new EngineStatus(EngineState.Starting, $"{_found} and {ping.Detail}");
            }

            return new EngineStatus(EngineState.Starting,
                $"{_found} as of the {EngineWatch.FirstQuietPoll} and {ping.Detail}");
        }

        // Nothing of ours has been launched, so the question is whether there is anything to launch.
        var registered = Registration;
        if (registered is false)
        {
            return new EngineStatus(EngineState.Stopped,
                $"{Distribution} is not registered: the engine is not installed")
            {
                Conclusive = true,
            };
        }

        return new EngineStatus(EngineState.Stopped,
            registered is null
                ? "the daemon is not running here, and wsl --list did not answer"
                : "the daemon is not running");
    }

    /// <summary>
    /// Boot the distribution, launch the daemon, serve the pipe, and wait until the engine answers.
    /// </summary>
    /// <param name="timeout">How long the engine has to answer before this gives up.</param>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>Running, or the state it got stuck in with the step named.</returns>
    public async Task<EngineStatus> StartAsync(
        TimeSpan? timeout = null, CancellationToken cancellation = default)
    {
        var budget = timeout ?? TimeSpan.FromSeconds(60);

        if (!DistributionRegistered)
        {
            return new EngineStatus(EngineState.Stopped,
                $"{Distribution} is not registered: run the install first");
        }

        var already = await EnginePing.AskAsync(_pipeName, cancellation: cancellation)
            .ConfigureAwait(false);
        if (already.Answered)
        {
            _found = null;
            return new EngineStatus(EngineState.Running,
                "the engine was already answering", already.ApiVersion);
        }

        if (!_daemon.Alive)
        {
            _daemon.Launch();
        }

        // From here on this lifecycle owns a daemon, and the handle is a better witness than any
        // subprocess (DD134). Set after the launch rather than before it, so a Launch that throws
        // leaves the status reading the machine rather than a process that does not exist.
        _launched = true;

        _relay ??= StartRelay();

        // Poll, because there is no event for "the socket is open now". The daemon dying is checked
        // on every turn: waiting the full minute for a process that is already gone reports a
        // timeout where the real answer is in its log.
        var deadline = DateTimeOffset.UtcNow + budget;
        var lastDetail = already.Detail;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellation.ThrowIfCancellationRequested();

            if (!_daemon.Alive)
            {
                return new EngineStatus(
                    EngineState.Stopped, $"the daemon exited while starting: {WhyItDied()}");
            }

            var ping = await EnginePing.AskAsync(
                _pipeName, TimeSpan.FromSeconds(2), cancellation).ConfigureAwait(false);
            if (ping.Answered)
            {
                _found = null;
                return new EngineStatus(EngineState.Running,
                    $"the engine answered on \\\\.\\pipe\\{_pipeName}", ping.ApiVersion);
            }

            lastDetail = ping.Detail;
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellation).ConfigureAwait(false);
        }

        // The same claim the poll used to make, and corrected the same way (DD175). Asked here
        // rather than cached, because a whole start budget has been spent since anything was
        // looked at, and this line is written once at the end of it rather than on every turn.
        _found = WhatIsThere();
        return new EngineStatus(EngineState.Starting,
            $"{_found} and the pipe did not answer within {budget.TotalSeconds:0}s "
            + $"({lastDetail})");
    }

    /// <summary>
    /// How long a deliberate teardown gives the daemon to stop its containers (DD189).
    /// </summary>
    /// <remarks>
    /// The daemon's own default is fifteen seconds per container, and this is that with room for one
    /// slow one. It costs nothing a user waits on: the Quit menu item spawns <c>--stop</c> and
    /// returns, so the icon is gone while this is still going.
    /// </remarks>
    public static readonly TimeSpan PatientGrace = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How long a teardown that is out of time gives it instead (DD189).
    /// </summary>
    /// <remarks>
    /// A session ending, and a revival that has an engine to get back. Neither can spend
    /// <see cref="PatientGrace"/> — Windows is not waiting and the second is a recovery — and both
    /// are still worth two seconds, because a database that flushes its tables in under a second is
    /// the common case rather than the lucky one.
    /// </remarks>
    public static readonly TimeSpan HurriedGrace = TimeSpan.FromSeconds(2);

    /// <summary>How often the grace asks whether the daemon has gone.</summary>
    private static readonly TimeSpan GracePoll = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Stop serving, stop the daemon, and terminate the distribution — an idle WSL2 virtual machine
    /// holds memory a laptop user notices, which is the complaint this project exists about.
    /// </summary>
    /// <param name="grace">
    /// How long the daemon is given to stop its containers before it is killed. No default, because
    /// there is no number that is right for both callers: a quit can afford
    /// <see cref="PatientGrace"/> and a session ending cannot.
    /// </param>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>Stopped, and what was done.</returns>
    public async Task<EngineStatus> StopAsync(
        TimeSpan grace, CancellationToken cancellation = default)
    {
        // Whatever was found about the engine being stopped here is about to stop being true of it.
        _found = null;
        var done = new List<string>();

        if (_relay is not null)
        {
            await _relay.DisposeAsync().ConfigureAwait(false);
            _relay = null;
            done.Add("stopped serving the pipe");
        }

        // Before the kill, and it is the whole of DD189. What follows kills the launcher tree, and
        // WSL2 then reaps the user processes behind it with a SIGKILL — so dockerd never ran its own
        // shutdown and no container ever received a stop signal, on every exit since DD128 including
        // the Quit menu item. The difference this buys is a MariaDB that closed its tables and one
        // that recovers them on the next boot.
        if (await AskTheDaemonToStopAsync(grace, cancellation).ConfigureAwait(false) is { } asked)
        {
            done.Add(asked);
        }

        if (_daemon.Alive)
        {
            _daemon.Stop();
            done.Add("stopped the daemon");
        }

        if (DistributionRegistered)
        {
            var terminated = _wsl.Run("--terminate", Distribution);
            done.Add(terminated.Succeeded
                ? $"terminated {Distribution}"
                : $"could not terminate {Distribution}: {terminated.Output.Trim()}");
        }

        return new EngineStatus(EngineState.Stopped,
            done.Count == 0 ? "nothing was running" : string.Join(", ", done));
    }

    /// <summary>
    /// Send the daemon a SIGTERM and wait for it to go, so its containers are stopped (DD189).
    /// </summary>
    /// <param name="grace">How long it is given.</param>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>What to report, or <see langword="null"/> where there was nothing to ask.</returns>
    /// <remarks>
    /// Gated on the distribution being up, for the reason <see cref="WhatIsThere"/> gives: an
    /// <c>--exec</c> against one that is not running would <em>start</em> it, and a teardown that
    /// boots a virtual machine in order to shut it down is worse than the kill it replaced.
    ///
    /// <para><c>kill -TERM</c> over <c>pidof</c> rather than <c>pkill</c>, because <c>pidof</c> is
    /// the spelling this file already relies on being present in the minirootfs. A distribution with
    /// no daemon in it makes that command fail, which is the answer rather than an error.</para>
    /// </remarks>
    private async Task<string?> AskTheDaemonToStopAsync(
        TimeSpan grace, CancellationToken cancellation)
    {
        var running = _wsl.Run("--list", "--running", "--quiet");
        if (!running.Succeeded || !NamesTheDistribution(running.Output))
        {
            return null;
        }

        var signalled = _wsl.Run(
            "-d", Distribution, "-u", "root", "--exec", "/bin/sh", "-c", "kill -TERM $(pidof dockerd)");
        if (!signalled.Succeeded)
        {
            return null;
        }

        // Waiting on the daemon and not on the containers, because they are the same event: dockerd
        // answers a SIGTERM by stopping what it is running and then exiting, so it being gone is the
        // proof that they were stopped rather than reaped.
        var deadline = DateTimeOffset.UtcNow + grace;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(GracePoll, cancellation).ConfigureAwait(false);

            var alive = _wsl.Run(
                "-d", Distribution, "-u", "root", "--exec", "/bin/sh", "-c", "pidof dockerd");
            if (!alive.Succeeded)
            {
                return "the daemon stopped its containers and exited";
            }
        }

        // Said out loud rather than folded into the kill below, because it is the one outcome where
        // a container was killed after all and the reader of this file is entitled to know which of
        // the two teardowns they got.
        return $"the daemon did not stop within {grace.TotalSeconds:0}s";
    }

    /// <summary>Whether a <c>wsl --list</c> answer names the distribution this install owns.</summary>
    /// <param name="output">What the launcher printed.</param>
    /// <returns><see langword="true"/> where the name is in it.</returns>
    private bool NamesTheDistribution(string output) => output
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Any(line => line.Trim().Equals(Distribution, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What is actually there, asked of the machine rather than inferred from a handle (DD175).
    /// </summary>
    /// <returns>The clause a journal line carries in place of a guess.</returns>
    /// <remarks>
    /// Four answers, and they are the worlds a reader of the journal is trying to tell apart: the
    /// daemon is fine and something between here and it broke; the daemon died inside a
    /// distribution that is still up; the distribution itself is gone, which is what a suspend does;
    /// and the machine would not say, which is the honest version of the sentence this replaced.
    ///
    /// <para><b>The cheap question gates the expensive one</b>, and not only for the load. Asking
    /// <c>--exec</c> of a distribution that is not running would <em>start</em> it — a status probe
    /// that boots a virtual machine has changed the thing it was reporting on, and would leave a
    /// booted distribution with no daemon in it for the next poll to be confused by. So the running
    /// list is asked first, and it can only ever cost one call in the case that matters most.</para>
    ///
    /// <para>A probe that did not answer resolves to the launcher and never to a verdict, the same
    /// direction <see cref="Registration"/> takes and for the same reason (DD134): load can make
    /// <c>wsl.exe</c> late, and folding late into "gone" manufactures evidence out of a busy
    /// machine.</para>
    /// </remarks>
    private string WhatIsThere()
    {
        var running = _wsl.Run("--list", "--running", "--quiet");
        if (!running.Succeeded)
        {
            return "the launcher is alive";
        }

        if (!NamesTheDistribution(running.Output))
        {
            return $"{Distribution} is not running";
        }

        // Spelled the way the launch is, because it is the same process being asked about: `-u root`
        // and a shell, rather than trusting a path for `pidof` that this project does not install.
        var daemon = _wsl.Run(
            "-d", Distribution, "-u", "root", "--exec", "/bin/sh", "-c", "pidof dockerd");

        return daemon.Succeeded ? "the daemon is running" : "the daemon is not running";
    }

    /// <summary>
    /// Why a daemon that was launched is no longer there, said as well as it can be (DD162).
    /// </summary>
    /// <returns>The clause that follows "the daemon exited while starting:".</returns>
    /// <remarks>
    /// This used to be one sentence with no branch in it: read the daemon's log inside the
    /// distribution. That is the right answer for a daemon that started and then died, and the
    /// wrong one for every launch that never reached a daemon at all — measured on 21 August 2026,
    /// five attempts in a row reported it and the log they named held nothing whatsoever between
    /// the failure and the manual start an hour later, because dockerd had never run to write in
    /// it.
    ///
    /// <para>So the launcher is asked first. Where it exited with something to say, that is the
    /// answer and the daemon's log is not mentioned — sending a reader to a second file when the
    /// first one already named the cause is how the previous sentence wasted an hour. Where it went
    /// quietly, the pointer stands exactly as it did, because then the daemon really is the thing
    /// that died.</para>
    /// </remarks>
    private string WhyItDied() =>
        _daemon.LastWords ?? $"read {LogPath} inside {Distribution}";

    /// <summary>The same question, for a daemon that was up and is not (DD162).</summary>
    /// <returns>The detail.</returns>
    /// <remarks>
    /// Separate from <see cref="WhyItDied"/> because the daemon's log is not offered here. This
    /// reading is reached by a poll long after the start succeeded, so the engine ran and whatever
    /// it wrote is about a working daemon until the last line — a reader sent there finds the log
    /// of the engine they already know they had.
    /// </remarks>
    private string WhatTookTheDaemon() =>
        _daemon.LastWords is { } said ? $"the daemon exited: {said}" : "the daemon exited";

    private EnginePipeRelay StartRelay()
    {
        var relay = new EnginePipeRelay(_backend, _pipeName);
        relay.Start();
        return relay;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_relay is not null)
        {
            await _relay.DisposeAsync().ConfigureAwait(false);
        }

        _daemon.Dispose();
    }
}

/// <summary>
/// The daemon as a held Windows child process.
/// </summary>
/// <remarks>
/// Held, and not detached, because measured on a real distribution neither <c>nohup … &amp;</c> nor
/// <c>setsid</c> survives: WSL2 reaps the user processes shortly after the launching <c>wsl.exe</c>
/// exits, and the daemon's log came back zero bytes. A process on this side is what keeps it up —
/// which also means stopping the engine is killing something we own, rather than hunting a pid.
/// </remarks>
public sealed class WslDaemonProcess : IDaemonProcess
{
    /// <summary>
    /// How much of what the launcher wrote is kept.
    /// </summary>
    /// <remarks>
    /// A few lines is the whole of what this ever carries — <c>wsl.exe</c>'s own refusals are one
    /// sentence — and the cap is here for the case that is not that: a launcher looping on an error
    /// against a process nobody is reading. Everything past it is read and dropped rather than left
    /// in the pipe, because a full pipe is a child blocked on a write.
    /// </remarks>
    public const int KeptBytes = 4 * 1024;

    /// <summary>How long <see cref="LastWords"/> waits for the streams to finish closing.</summary>
    /// <remarks>
    /// The process exiting and its pipes reaching end-of-stream are two events, in that order, and
    /// the gap between them is where a launcher's last line still is. Short, because the caller is
    /// a supervisor poll that has an engine to get back and the alternative to waiting is a
    /// sentence missing its cause.
    /// </remarks>
    private static readonly TimeSpan LastWordsWait = TimeSpan.FromMilliseconds(500);

    private readonly string _distribution;
    private readonly List<byte> _said = [];
    private readonly object _pen = new();
    private Process? _process;
    private Task[] _draining = [];
    private bool _asked;

    /// <summary>Construct a daemon launcher.</summary>
    /// <param name="distribution">
    /// The owned distribution, or <see langword="null"/> to ask the machine which one this install
    /// owns — which is the legacy name where an older install is being adopted (DD55).
    /// </param>
    public WslDaemonProcess(string? distribution = null)
    {
        distribution ??= new EnginePaths().DistributionName;
        ArgumentException.ThrowIfNullOrWhiteSpace(distribution);
        _distribution = distribution;
    }

    /// <inheritdoc/>
    public bool Alive => _process is { HasExited: false };

    /// <inheritdoc/>
    /// <remarks>
    /// Silent about an ending this process asked for, which is what <c>_asked</c> is for. A killed
    /// launcher exits with a code Windows chose and says nothing, and reporting "wsl.exe exited
    /// -1" every time the engine is stopped normally would put a line that reads like a failure
    /// into the ordinary path — the file DD137 keeps is worth opening because everything in it
    /// happened to somebody.
    /// </remarks>
    public string? LastWords
    {
        get
        {
            if (_process is not { HasExited: true } exited || _asked)
            {
                return null;
            }

            // The exit and the end of the pipes are not the same instant, and the last line is
            // usually written just before the first. Bounded, and the timeout is not an error: what
            // has arrived by then is still better than the pointer this replaced.
            try
            {
                _ = Task.WaitAll(_draining, LastWordsWait);
            }
            catch (AggregateException)
            {
                // A drain that faulted read less than all of it. Say what did arrive.
            }

            byte[] said;
            lock (_pen)
            {
                said = [.. _said];
            }

            return Sentence(exited.ExitCode, said);
        }
    }

    /// <summary>
    /// What a launcher that exited is quoted as saying (DD162).
    /// </summary>
    /// <param name="exitCode">What it exited with.</param>
    /// <param name="said">The raw bytes of everything it wrote, in the encoding it chose.</param>
    /// <returns>The one line a status detail carries.</returns>
    /// <remarks>
    /// Separate from the property, and internal, because the property cannot be reached without a
    /// real <c>wsl.exe</c> and this is the whole of what there is to get wrong: the decoding, the
    /// flattening, and the sentence for a launcher that said nothing. The bytes the suite hands
    /// this were captured from a real failed launch.
    ///
    /// <para>The exit code is in the sentence even where there is no text, and that is not padding.
    /// It is the one thing that distinguishes a launcher that died from a daemon that did — which
    /// is the distinction the reader was previously left to guess at.</para>
    /// </remarks>
    internal static string Sentence(int exitCode, byte[] said)
    {
        // Decoded by what is in the bytes rather than by what wsl.exe documents. Measured: a
        // missing distribution answers on stdout, in UTF-16LE with no BOM, and reading that as
        // UTF-8 gives a NUL after every character — a message no reader can use.
        var text = Preflight.Windows.ConsoleTool.Decode(said).Trim();

        // Flattened, because the journal is read as a column of stamped lines and wsl.exe puts its
        // error code on a second line. A detail carrying its own newline breaks the shape of every
        // line after it.
        var line = string.Join(
            " ",
            text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries));

        return line.Length == 0
            ? $"wsl.exe exited {exitCode} without a word"
            : $"wsl.exe exited {exitCode}: {line}";
    }

    /// <inheritdoc/>
    public void Launch()
    {
        var startInfo = new ProcessStartInfo(Wsl.LauncherPath)
        {
            // Redirected since DD162, and this was the one process in the project that was not.
            // What the launcher says goes nowhere otherwise: the engine host is started detached
            // and hidden, so its console — the destination of an un-redirected stderr — is the same
            // console DD137 exists because nobody can read.
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
                 {
                     "-d", _distribution, "-u", "root", "--exec",
                     "/bin/sh", "-c",
                     $"exec /usr/local/bin/dockerd -H unix://{EngineLifecycle.SocketPath} "
                     + $">>{EngineLifecycle.LogPath} 2>&1",
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        lock (_pen)
        {
            _said.Clear();
        }

        _asked = false;
        _process = Process.Start(startInfo)
            ?? throw new IOException($"{Wsl.LauncherPath} could not be started");

        // Both, and started at once. The daemon's own output never comes this way — the shell above
        // redirects it into the distribution's own log — so what these carry is wsl.exe's, which is
        // a sentence or nothing.
        _draining =
        [
            DrainAsync(_process.StandardOutput.BaseStream),
            DrainAsync(_process.StandardError.BaseStream),
        ];
    }

    /// <summary>Read one of the launcher's streams to its end, keeping the first of it.</summary>
    /// <param name="from">The stream.</param>
    /// <returns>The task that completes when the stream does.</returns>
    /// <remarks>
    /// Bytes and not lines, for the reason <see cref="Preflight.Windows.ConsoleTool"/> reads bytes:
    /// <c>wsl.exe</c> writes UTF-16LE, and a <see cref="StreamReader"/> decoding that as UTF-8
    /// yields a NUL after every character — which is not a message anybody can read and is the
    /// decoding wart this project has already been bitten by.
    ///
    /// <para>It reads past the cap rather than stopping at it. Stopping would leave a pipe nobody
    /// is draining, and the child blocks on the write that fills it — which for this child means an
    /// engine that will not come up because its log was too long.</para>
    /// </remarks>
    private async Task DrainAsync(Stream from)
    {
        var buffer = new byte[1024];
        try
        {
            int read;
            while ((read = await from.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                lock (_pen)
                {
                    var room = KeptBytes - _said.Count;
                    if (room > 0)
                    {
                        _said.AddRange(buffer.AsSpan(0, Math.Min(read, room)));
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException
            or OperationCanceledException)
        {
            // The process was killed or disposed under the read. Whatever arrived first still
            // stands, and a launcher whose output could not be read must not take the engine with
            // it — this whole class exists to make a failure legible, not to become one.
        }
    }

    /// <inheritdoc/>
    public void Stop()
    {
        if (_process is null)
        {
            return;
        }

        // Set before the kill and never after it. The kill is what makes the launcher exit, so a
        // flag written afterwards leaves a window in which LastWords reports the ending this
        // process just asked for as though something had gone wrong.
        _asked = true;

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(10_000);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Already gone.
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Stop();
        _process?.Dispose();
        _process = null;
    }
}
