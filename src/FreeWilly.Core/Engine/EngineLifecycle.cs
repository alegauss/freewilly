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

    /// <summary>
    /// The code the launcher exited with, or <see langword="null"/> while it is still up (DD265).
    /// </summary>
    /// <remarks>
    /// The raw number, beside <see cref="LastWords"/> rather than inside it. A start has one decision
    /// to make about a launch that died — whether it is worth making again — and a sentence written
    /// for a person to read is not the thing to make it on.
    ///
    /// <para>Unlike <see cref="LastWords"/> this is not silenced by an ending that was asked for. A
    /// stop is not followed by a start deciding anything, so there is nothing for the silence to
    /// protect, and a number that disappears is a number a caller has to cache.</para>
    /// </remarks>
    int? ExitCode { get; }

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

    /// <summary>What a shell exits with when it found the daemon and could not execute it.</summary>
    /// <remarks>
    /// 126 is the shell's own code for that, and 127 is the one for a file that is not there, so this
    /// is already narrower than "the launch failed". It is not narrow enough to be only the transient
    /// case: a daemon whose bytes are wrong or whose permission bits are would exit the same way. The
    /// cost of being wrong about it is <see cref="MostRelaunches"/> pauses before the same failure is
    /// reported, which is why it is enough to act on without reading anything else.
    ///
    /// <para>It is not, however, the whole of the condition, and since DD267 it is only the first
    /// question <see cref="WorthLaunchingAgain"/> asks. This code can only ever be about the file the
    /// shell was pointed at.</para>
    /// </remarks>
    private const int CouldNotExec = 126;

    /// <summary>What the kernel's refusal to execute a file being written reads as (DD267).</summary>
    /// <remarks>
    /// Matched case-folded because the two writers disagree and both reach this log. The shell writes
    /// <c>Text file busy</c>, which is strerror's own spelling, and Go's <c>os/exec</c> writes
    /// <c>text file busy</c> lowercased into the error it wraps the syscall in. Measured on 31 August
    /// 2026 the second one is the line that mattered, so an ordinal match on the first would have
    /// read the evidence and thrown it away.
    /// </remarks>
    private const string Busy = "text file busy";

    /// <summary>How many times a start will launch a daemon it could not execute (DD265).</summary>
    /// <remarks>
    /// Three, which is not a tuned number. The condition it exists for is a file held open by an
    /// installer that has already finished everything else, so it clears in one pause or it was never
    /// the condition at all, and the second and third attempt are there because a slow disk is not a
    /// different failure.
    /// </remarks>
    internal const int MostRelaunches = 3;

    /// <summary>How long a start waits before launching the daemon again (DD265).</summary>
    /// <remarks>
    /// Flat and short. There is nothing to be polite to here: the file is either still held or it is
    /// not, and every second spent waiting is one where the engine somebody just upgraded is down.
    /// </remarks>
    private static readonly TimeSpan BeforeRelaunching = TimeSpan.FromMilliseconds(700);

    /// <summary>How long a start waits for a provision to finish unpacking (DD269).</summary>
    /// <remarks>
    /// Measured, and then tripled. The unpack that caused this is 85 MB and took about six seconds on
    /// the machine it was caught on, so twenty is room for a slow disk rather than a guess at one.
    ///
    /// <para>It is a bound and not a promise, and an import onto a fresh install can exceed it by
    /// minutes. That is deliberate: past this the start goes ahead and falls back on the relaunch,
    /// because a start that waited out an arbitrarily long install would be indistinguishable from
    /// the hang this product spends DD134 and DD175 refusing to report as one. It also comes out of
    /// the caller's own budget, so a wait can never push a start past the timeout it was given.</para>
    /// </remarks>
    private static readonly TimeSpan WaitForTheUnpack = TimeSpan.FromSeconds(20);

    /// <summary>Whichever is less, a span or what is left before a deadline (DD269).</summary>
    /// <param name="span">The span asked for.</param>
    /// <param name="deadline">The deadline it has to fit inside.</param>
    /// <returns>The span to actually wait, never negative.</returns>
    private static TimeSpan Shorter(TimeSpan span, DateTimeOffset deadline)
    {
        var left = deadline - DateTimeOffset.UtcNow;
        if (left < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return left < span ? left : span;
    }

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
    /// <param name="unpackLock">The provision's lock file; overridden in tests (DD269).</param>
    public EngineLifecycle(
        IWsl wsl,
        IDaemonProcess daemon,
        IEngineBackend backend,
        string pipeName = EnginePipeRelay.DefaultPipeName,
        string? distribution = null,
        string? unpackLock = null)
    {
        ArgumentNullException.ThrowIfNull(wsl);
        ArgumentNullException.ThrowIfNull(daemon);
        ArgumentNullException.ThrowIfNull(backend);
        _wsl = wsl;
        _daemon = daemon;
        _backend = backend;
        _pipeName = pipeName;

        // One EnginePaths for both, because the disk belongs to the distribution and reading them
        // from two constructions is how the pair drifts on a machine whose root has moved.
        var paths = new EnginePaths();
        Distribution = distribution ?? paths.DistributionName;
        _basePath = paths.Distribution;
        _unpackLock = unpackLock ?? paths.UnpackLock;
    }

    /// <summary>The file a provision holds while it rewrites the binaries (DD269).</summary>
    /// <remarks>
    /// Overridable for the reason <see cref="Distribution"/> is, which is that the default reads the
    /// machine the suite happens to be running on. Left alone it answers false everywhere no
    /// provision is in flight, so the tests that are not about this never notice it; a test that is
    /// about it needs a lock it can hold and release on purpose, and holding the real one would mean
    /// a suite that stalls any install running beside it.
    /// </remarks>
    private readonly string _unpackLock;

    /// <summary>Where WSL registered the distribution, which is where its disk is (DD190).</summary>
    private readonly string _basePath;

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

        // Taken before the wait below rather than after it (DD269). The caller asked for an engine
        // within this budget, and time spent waiting for an install to put the binaries down is time
        // spent getting one: a deadline restarted after the wait would let a start overrun the
        // timeout its caller set by however long the unpack took.
        var deadline = DateTimeOffset.UtcNow + budget;

        // DD269. Launching now would exec a directory somebody is still writing, which is what
        // happened twice on 31 August 2026. Zero on every start that did not collide with an install.
        var waited = await EngineUnpack.WaitForIdleAsync(
            _unpackLock, Shorter(WaitForTheUnpack, deadline), cancellation).ConfigureAwait(false);

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
        var lastDetail = already.Detail;
        var relaunched = 0;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellation.ThrowIfCancellationRequested();

            if (!_daemon.Alive)
            {
                if (relaunched < MostRelaunches && WorthLaunchingAgain())
                {
                    // DD265. The launch is tried again rather than reported, because the one
                    // condition that produces this code on this machine passes on its own: an
                    // upgrade writes the daemon into the distribution, and a shell cannot exec a
                    // file somebody else still holds open for writing. Measured on 31 August 2026,
                    // five seconds after the previous daemon had logged its shutdown.
                    relaunched++;
                    await Task.Delay(BeforeRelaunching, cancellation).ConfigureAwait(false);
                    _daemon.Launch();
                    continue;
                }

                return new EngineStatus(
                    EngineState.Stopped, $"the daemon exited while starting: {WhyItDied()}");
            }

            var ping = await EnginePing.AskAsync(
                _pipeName, TimeSpan.FromSeconds(2), cancellation).ConfigureAwait(false);
            if (ping.Answered)
            {
                _found = null;

                // The relaunch is in the sentence where there was one, and absent where there was
                // not (DD265). A retry that made a failure invisible should not also make itself
                // invisible: an upgrade that needs one every time is worth knowing about, and the
                // journal is where a reader would find it.
                return new EngineStatus(EngineState.Running,
                    $"the engine answered on \\\\.\\pipe\\{_pipeName}"
                    + $"{Waited(waited)}{Relaunches(relaunched)}",
                    ping.ApiVersion);
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

    /// <summary>
    /// How long one <c>wsl.exe</c> call gets inside a teardown that is out of time (DD275).
    /// </summary>
    /// <remarks>
    /// <see cref="WslBudget.Probe"/> is fifteen seconds and was written for a preflight standing at a
    /// prompt. A session ending has four in total, so a call taking that constant is a call that
    /// cannot exhaust its own budget before the shutdown's runs out around it: the process is killed
    /// mid-call and the line naming the outcome, which is written after the call returns, never
    /// reaches the journal.
    ///
    /// <para>Half of the four, so the terminate and one call after it can both finish. That is the
    /// whole claim: one <c>wsl.exe</c> that hangs must not be able to spend the teardown by itself.
    /// It is not a promise that every step runs, and nothing here can make one — Windows stops
    /// waiting on its own schedule.</para>
    ///
    /// <para>On a healthy machine none of this is reached. Both calls answer in well under a second,
    /// and this only bounds the failing case.</para>
    /// </remarks>
    public static readonly TimeSpan HurriedCall = TimeSpan.FromSeconds(2);

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
    /// <param name="step">
    /// Told what each step did the moment it finishes, where the stop is hurried (DD273). Null
    /// elsewhere, and ignored on the patient path: a quit reaches the end and the aggregate below
    /// says the same thing in one line.
    /// </param>
    /// <returns>Stopped, and what was done.</returns>
    /// <remarks>
    /// The order the steps run in is decided by <paramref name="grace"/>, and that is DD271. A stop
    /// somebody asked for does the polite thing first and unmounts last; a stop that is out of time
    /// unmounts first, because it may not get a second chance.
    /// </remarks>
    public async Task<EngineStatus> StopAsync(
        TimeSpan grace, CancellationToken cancellation = default, Action<string>? step = null)
    {
        // Whatever was found about the engine being stopped here is about to stop being true of it.
        _found = null;
        var done = new List<string>();

        // DD271. Under a session ending the whole teardown has four seconds and a single wsl.exe
        // launch can consume all of it, so the step that unmounts ext4 must not be queued behind
        // three others: the sessions of 29, 30 and 31 August 2026 all end at "still tearing down
        // after 4s" with no terminate line behind them, which is the unclean unmount DD187 and DD188
        // were written to prevent. What that costs is DD189's SIGTERM, and the trade is deliberate —
        // a container reaped by WSL2 recovers on the next start and a root torn off mid-write may
        // not. A patient stop is not racing anything and keeps the order it has.
        var hurried = grace <= HurriedGrace;

        // DD275. Every wsl.exe below runs under the budget this teardown actually has, rather than
        // each call taking the probe constant written for a preflight at a prompt. Fifteen seconds
        // inside a budget of four is a call that cannot time out before the shutdown does, and a
        // process killed mid-call writes no line at all.
        var budget = hurried ? HurriedCall : WslBudget.Probe;

        // DD273. The aggregate below is composed only once every step has finished, so a teardown
        // Windows kills part way through left no trace that it ran at all: the last three shutdowns
        // of August 2026 carry the session line and then nothing, and which step it died in had to
        // be read out of the Windows System log. Under a session ending this file is the only
        // witness, and a witness that speaks after the room is empty is not one.
        void Did(string what)
        {
            done.Add(what);
            if (hurried)
            {
                step?.Invoke(what);
            }
        }

        if (hurried)
        {
            Did(Terminated(budget));
        }

        if (_relay is not null)
        {
            // Under the teardown's own budget since DD279. The relay's default join is five seconds
            // and a session ending has four, so the one step here that is not a wsl.exe was the last
            // way a single wait could still spend the whole shutdown.
            await _relay.DisposeAsync(hurried ? HurriedCall : EnginePipeRelay.Join)
                .ConfigureAwait(false);
            _relay = null;
            Did("stopped serving the pipe");
        }

        // Before the kill, and it is the whole of DD189. What follows kills the launcher tree, and
        // WSL2 then reaps the user processes behind it with a SIGKILL — so dockerd never ran its own
        // shutdown and no container ever received a stop signal, on every exit since DD128 including
        // the Quit menu item. The difference this buys is a MariaDB that closed its tables and one
        // that recovers them on the next boot.
        if (await AskTheDaemonToStopAsync(grace, budget, cancellation).ConfigureAwait(false)
            is { } asked)
        {
            Did(asked);
        }

        if (_daemon.Alive)
        {
            _daemon.Stop();
            Did("stopped the daemon");
        }

        if (!hurried && DistributionRegistered)
        {
            Did(Terminated(budget));
        }

        return new EngineStatus(EngineState.Stopped,
            done.Count == 0 ? "nothing was running" : string.Join(", ", done));
    }

    /// <summary>Take the distribution down, and say what that did (DD271).</summary>
    /// <param name="budget">How long the call gets, which the teardown decides (DD275).</param>
    /// <returns>The line for the journal.</returns>
    /// <remarks>
    /// Ungated where a hurried stop calls it, which is the half of DD271 that is easy to miss:
    /// <see cref="DistributionRegistered"/> is a <c>wsl --list</c>, so asking whether the terminate
    /// is worth running costs the same launch as running it. A terminate against a distribution that
    /// is not registered fails and says so, which is a truthful line in the journal and no worse than
    /// the launch the question would have spent. The patient path keeps the gate, because there the
    /// launch is free and "nothing was running" is a better answer than a failure.
    /// </remarks>
    private string Terminated(TimeSpan budget)
    {
        var terminated = _wsl.Run(budget, "--terminate", Distribution);
        if (terminated.Succeeded)
        {
            return $"terminated {Distribution}";
        }

        // DD276. Dropping the gate was right and its consequence was not: on a machine that never
        // provisioned an engine, every logoff now writes a failure about a distribution nobody ever
        // had. Nothing is wrong there, and the file saying otherwise is the false diagnosis DD26
        // puts above every other consideration — arriving in the one file somebody opens because
        // they already suspect something went wrong overnight.
        if (WslFailure.SaysNoSuchDistribution(terminated.Output))
        {
            return $"there was no {Distribution} to take down";
        }

        // Detail rather than the trimmed output, since DD274. A wsl.exe Windows refused to start
        // writes nothing at all, and this line used to end at its colon — which is the one place
        // in a session ending where "it said nothing" and "it never ran" look identical.
        return $"could not terminate {Distribution}: {terminated.Detail()}";
    }

    /// <summary>
    /// Send the daemon a SIGTERM and wait for it to go, so its containers are stopped (DD189).
    /// </summary>
    /// <param name="grace">How long it is given.</param>
    /// <param name="budget">How long each <c>wsl.exe</c> below gets (DD275).</param>
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
        TimeSpan grace, TimeSpan budget, CancellationToken cancellation)
    {
        if (!DistributionIsRunning(budget))
        {
            return null;
        }

        var signalled = _wsl.Run(
            budget,
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
                budget,
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

    /// <summary>Whether the owned distribution is up right now.</summary>
    /// <param name="budget">
    /// How long the call gets, defaulting to the probe's own (DD275). A teardown that is out of time
    /// names a shorter one, because this gate is one of the launches that can spend a shutdown.
    /// </param>
    /// <returns><see langword="true"/> where WSL lists it as running.</returns>
    /// <remarks>
    /// The gate every <c>--exec</c> in this file goes through, and it is not about cost. Asking
    /// <c>--exec</c> of a distribution that is not running <em>starts</em> it, so a probe without
    /// this boots the virtual machine it was only meant to look at.
    /// </remarks>
    private bool DistributionIsRunning(TimeSpan? budget = null)
    {
        var running = _wsl.Run(budget ?? WslBudget.Probe, "--list", "--running", "--quiet");
        return running.Succeeded && NamesTheDistribution(running.Output);
    }

    /// <summary>
    /// What the root filesystem says about itself at a start, if it complains (DD191, DD200).
    /// </summary>
    /// <returns>The reading and its repair, or <see langword="null"/> where it is well.</returns>
    /// <remarks>
    /// <para>The 29 August 2026 failure was announced a boot early and nothing was listening. WSL
    /// said the filesystem needed checking while mounting this distribution, the mount then
    /// succeeded, and the start was reported healthy. Seconds later ext4 aborted its journal and
    /// remounted the root read-only.</para>
    ///
    /// <para><b>Not a refusal.</b> A start that succeeded on a filesystem that needs checking is
    /// still a start, and the engine on it is worth having. What was missing is anybody saying
    /// so.</para>
    ///
    /// <para>The gate lives in <see cref="ReadState"/> and is deliberately not repeated here: it was
    /// taken twice for one call once, which cost nothing on a real machine and made every test that
    /// queued two answers read the second one into the wrong question.</para>
    /// </remarks>
    public WslFailure? CheckFilesystem() =>
        ReadState() is { } state && WhatItSaysAboutItself(state) is { } complaint
            ? WslFailure.OfDirtyFilesystem(complaint, Distribution, _basePath)
            : null;

    /// <summary>
    /// Take the one reading the start check and the Engine page share (DD197).
    /// </summary>
    /// <returns>What the root filesystem says, or <see langword="null"/> where it would not say.</returns>
    /// <remarks>
    /// Public because the page needs the same six answers this check turns into a verdict, and two
    /// readings are how a page comes to disagree with the journal printed beside it. The gate is
    /// here rather than at the caller: an <c>--exec</c> against a distribution that is not running
    /// would start it, and a page that boots a virtual machine to describe it has changed what it
    /// was describing.
    /// </remarks>
    public DistributionState? ReadState()
    {
        if (!DistributionIsRunning())
        {
            return null;
        }

        var read = _wsl.Run(
            "-d", Distribution, "-u", "root", "--exec", "/bin/sh", "-c", DistributionState.Script);

        return read.Succeeded ? DistributionState.Of(read.Output) : null;
    }

    /// <summary>
    /// The one call that asks the root filesystem about itself, in what a minirootfs has (DD200).
    /// </summary>
    /// <remarks>
    /// <para><b>Nothing here is a package.</b> <c>/proc/mounts</c> and <c>/sys/fs/ext4</c> are the
    /// kernel's, so this answers on a distribution provisioned before DD196 put <c>e2fsprogs</c>
    /// in one — which is every distribution installed to date. Measured on the live one: <c>awk</c>
    /// and <c>blkid</c> are there and <c>findmnt</c>, <c>dumpe2fs</c> and <c>e2fsck</c> are not,
    /// because BusyBox is not util-linux.</para>
    ///
    /// <para><c>/sys/fs/ext4</c> has a directory per device, so the counter read here belongs to
    /// this filesystem and to no other. That is the half DD191 got wrong.</para>
    /// </remarks>
    internal const string StateScript = DistributionState.Script;

    /// <summary>
    /// What the root filesystem reports about itself, or <see langword="null"/> where it is well.
    /// </summary>
    /// <param name="state">What the root filesystem reported.</param>
    /// <returns>The complaint, in one clause.</returns>
    /// <remarks>
    /// <para><b>The kernel log is not read here any more, and DD200 is why.</b> DD191 matched four
    /// ext4 phrases in <c>dmesg</c>, and that buffer is wrong for this question twice over. WSL2
    /// runs one kernel for every distribution, so it carried lines for four disks at once and a
    /// complaint about the user's Ubuntu was reported as the engine's. And it is a history: on
    /// 29 August 2026 it still held the original incident in full, naming the bad block bitmap
    /// checksum in group 348, on a filesystem whose own error count was zero and which a full
    /// <c>e2fsck</c> called clean.</para>
    ///
    /// <para>Filtering it by device would have fixed the first half only, so it is gone rather than
    /// narrowed. Everything it was contributing is here and is per-filesystem: that an error was
    /// recorded, and where. Both are cleared by the repair that fixes them, which is what makes
    /// this a state rather than an account of one.</para>
    /// </remarks>
    private string? WhatItSaysAboutItself(DistributionState state)
    {
        if (!state.Writable)
        {
            return $"{Distribution}'s root is mounted read-only, which is what the kernel does to a "
                + "filesystem it has hit an error on, and the engine is running on it meanwhile";
        }

        if (state.Errors is > 0)
        {
            return $"{Distribution}'s filesystem has {state.Errors} error(s) recorded against it, "
                + $"the last in {state.LastError ?? "an unnamed function"}, and the engine is "
                + "running on it meanwhile";
        }

        return null;
    }

    /// <summary>
    /// Whether the root is still writable, which is the failure a running engine walks into (DD191).
    /// </summary>
    /// <returns>The reading and its repair, or <see langword="null"/> where the write landed.</returns>
    /// <remarks>
    /// The mount check above is a boot-time question and this is the one that catches a filesystem
    /// going read-only under a session that was working a minute earlier — which is exactly what
    /// happened, seconds after a mount this tool had called healthy. A write is the only honest test
    /// of it: <c>ext4</c> remounted read-only answers every read perfectly well.
    ///
    /// <para><c>/var/lib</c> because it is on the root filesystem rather than on a tmpfs, which is
    /// what <c>/run</c> and <c>/tmp</c> are and what would make this pass on a broken disk. Created
    /// and removed in one command, so a probe that ran a thousand times leaves nothing.</para>
    /// </remarks>
    public WslFailure? CheckRootIsWritable()
    {
        if (!DistributionIsRunning())
        {
            return null;
        }

        var wrote = _wsl.Run(
            "-d", Distribution, "-u", "root", "--exec", "/bin/sh", "-c",
            "touch /var/lib/.freewilly-writable && rm -f /var/lib/.freewilly-writable");

        return wrote.Succeeded
            ? null
            : WslFailure.OfDirtyFilesystem(
                $"{Distribution}'s root is no longer writable, so its filesystem has been remounted "
                + "read-only under a running engine",
                Distribution,
                _basePath);
    }


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

    /// <summary>Whether a launch that died is one of the ones that passes on its own (DD267).</summary>
    /// <returns>Whether to launch it again.</returns>
    /// <remarks>
    /// DD265 asked this of the launcher's exit code alone, and the code answers for one binary out of
    /// the eight the install unpacks: 126 is a <em>shell</em> that could not exec what it was pointed
    /// at, which here is <c>dockerd</c> and nothing else. Measured on 31 August 2026 upgrading 1.0.11
    /// to 1.0.12, the shell exec'd dockerd perfectly, dockerd forked <c>containerd</c> against a file
    /// the installer still held open, and the whole thing came back as an ordinary exit 1 that this
    /// declined to retry. Same condition, same remedy, and the gate never opened.
    ///
    /// <para>So the daemon's own log is read where the code did not answer. DD266 already fetches
    /// that line to write the journal's sentence, and the note that used to sit on
    /// <see cref="CouldNotExec"/> said the code was enough to act on and the log need not be read
    /// here. That claim had a counterexample four days after it was written.</para>
    ///
    /// <para><b>The code is still asked first</b>, and not only to save a subprocess. A shell's 126
    /// is evidence about the launch that just died, while the log is a file appended to across every
    /// launch this distribution has ever had: a stale busy line from the previous failure can answer
    /// yes to a launch that died of something else. What that costs is bounded by
    /// <see cref="MostRelaunches"/> and paid only on a path where a start has already failed, which
    /// is the same trade DD265 took for a 126 that was never transient.</para>
    /// </remarks>
    private bool WorthLaunchingAgain() =>
        _daemon.ExitCode == CouldNotExec
        || LastLineOfTheDaemonLog()?.Contains(Busy, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>The clause a start adds where it waited for an unpack to finish (DD269).</summary>
    /// <param name="waited">How long it waited.</param>
    /// <returns>The clause, or empty where there was nothing to wait for.</returns>
    /// <remarks>
    /// Empty on nearly every start, which is the point: a sentence saying "waited 0s for the install"
    /// on a machine nobody is installing anything on is noise in a file read as a column of stamped
    /// lines, and it would make the one line that matters harder to find rather than easier.
    ///
    /// <para>Whole seconds, because that is the resolution the fact has. What a reader does with this
    /// clause is notice that a start collided with an install, and no decision they then make turns
    /// on the milliseconds.</para>
    /// </remarks>
    private static string Waited(TimeSpan waited) => waited > TimeSpan.Zero
        ? $", after waiting {waited.TotalSeconds:0}s for the install to finish unpacking"
        : "";

    /// <summary>The clause a start adds where it had to launch the daemon again (DD265).</summary>
    /// <param name="relaunched">How many extra launches it took.</param>
    /// <returns>The clause, or empty where the first launch was enough.</returns>
    private static string Relaunches(int relaunched) => relaunched switch
    {
        <= 0 => "",
        1 => ", after 1 relaunch",
        _ => $", after {relaunched} relaunches",
    };

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
    ///
    /// <para><b>DD266 corrects where that split falls.</b> It asked whether <c>wsl.exe</c> wrote
    /// anything, and the launch command ends in <c>&gt;&gt;/var/log/dockerd.log 2&gt;&amp;1</c>: the
    /// shell's own failures are written into that log by construction and can never reach the
    /// launcher's streams. So a launch that failed to exec the daemon was <i>always</i> silent and
    /// always had its sentence in the file this declined to name. Measured on 31 August 2026, the
    /// journal said <c>wsl.exe exited 126 without a word</c> while the log said
    /// <c>/bin/sh: exec: line 0: /usr/local/bin/dockerd: Text file busy</c>, which is the cause
    /// exactly, and finding it took the hour DD162 exists to save.</para>
    ///
    /// <para>The log is therefore read rather than pointed at, and DD162's argument is untouched:
    /// it was about not naming a second file when the first one had answered, and a launcher that
    /// said only a number has not answered. Where the log cannot be read the pointer stands, because
    /// then there is still a file and this still cannot quote it.</para>
    /// </remarks>
    private string WhyItDied()
    {
        if (_daemon.LastWords is not { } said)
        {
            return $"read {LogPath} inside {Distribution}";
        }

        if (said.EndsWith(WslDaemonProcess.WithoutAWord, StringComparison.Ordinal))
        {
            // DD266. The exit code rather than the sentence, because the sentence's own tail is the
            // claim being withdrawn: there were words, and they are in the log.
            var code = _daemon.ExitCode is { } number
                ? $"wsl.exe exited {Preflight.Windows.WindowsExit.Spell(number)}"
                : "the launcher exited";

            return LastLineOfTheDaemonLog() is { } ended
                ? $"{code}, and {LogPath} ends: {ended}"
                : $"{said}: read {LogPath} inside {Distribution}";
        }

        // DD190. The launcher's own words are kept and the reading is added after them, never
        // instead: "getpwnam(root) failed 5" is the evidence and has to survive into the journal,
        // and what it means is the half a reader could not have supplied.
        return WslFailure.Of(said, Distribution, _basePath) is { } read
            ? $"{said} ({read.Meaning})"
            : said;
    }

    /// <summary>How much of the daemon log's last line a journal line will carry (DD266).</summary>
    /// <remarks>
    /// Enough for the shell's own failures, which are the case this exists for and run to about
    /// seventy characters. A line from <c>dockerd</c> itself is structured logging and runs to
    /// several hundred, and the journal is read as a column of stamped lines: one entry wide enough
    /// to wrap costs the shape of every line around it.
    /// </remarks>
    private const int KeptOfTheLastLine = 200;

    /// <summary>The last thing written to the daemon's log inside the distribution (DD266).</summary>
    /// <returns>That line, or <see langword="null"/> where there is nothing to quote.</returns>
    /// <remarks>
    /// A subprocess on a path that has already failed, which is the only reason it is affordable:
    /// this runs once per dead launch and not on a poll, and the alternative is the reader running
    /// the same command by hand after being told to.
    /// </remarks>
    private string? LastLineOfTheDaemonLog()
    {
        var read = _wsl.Run(
            "-d", Distribution, "-u", "root", "--exec", "/bin/sh", "-c", $"tail -n 1 {LogPath}");

        if (!read.Succeeded)
        {
            return null;
        }

        var last = read.Output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();

        if (string.IsNullOrEmpty(last))
        {
            return null;
        }

        return last.Length > KeptOfTheLastLine ? last[..KeptOfTheLastLine] : last;
    }

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
    /// How much of what the launcher wrote is kept, per stream.
    /// </summary>
    /// <remarks>
    /// A few lines is the whole of what this ever carries — <c>wsl.exe</c>'s own refusals are one
    /// sentence — and the cap is here for the case that is not that: a launcher looping on an error
    /// against a process nobody is reading. Everything past it is read and dropped rather than left
    /// in the pipe, because a full pipe is a child blocked on a write.
    ///
    /// <para>Per stream since DD192, which is the same bound doubled. The two are kept apart because
    /// they are not written in the same encoding, and a shared budget would also mean a noisy stderr
    /// deciding how much of stdout survives.</para>
    /// </remarks>
    public const int KeptBytes = 4 * 1024;

    /// <summary>How a sentence ends where the launcher exited and wrote nothing (DD266).</summary>
    /// <remarks>
    /// A constant because it is read as well as written. It is the whole of what tells a launcher
    /// that failed silently from one that named its own cause, and the two get different answers:
    /// the silent one has its words in the daemon's log, put there by the launch command's own
    /// redirection, and a caller that could not recognise the case would send nobody to them.
    /// </remarks>
    internal const string WithoutAWord = " without a word";

    /// <summary>How long <see cref="LastWords"/> waits for the streams to finish closing.</summary>
    /// <remarks>
    /// The process exiting and its pipes reaching end-of-stream are two events, in that order, and
    /// the gap between them is where a launcher's last line still is. Short, because the caller is
    /// a supervisor poll that has an engine to get back and the alternative to waiting is a
    /// sentence missing its cause.
    /// </remarks>
    private static readonly TimeSpan LastWordsWait = TimeSpan.FromMilliseconds(500);

    private readonly string _distribution;

    // Two buffers and not one, and that is the whole of DD192. They held a single list, and
    // Sentence handed the mixture to a decoder that picks one encoding for the whole of what it is
    // given. The streams do not agree: on 29 August 2026 wsl.exe wrote its relay error to stderr as
    // plain bytes and its own refusal to stdout as UTF-16LE, the zero-counting heuristic resolved
    // the pair to UTF-8, and the journal kept "getpwnam(root) failed 5 U s u ? r i o". Everything
    // after the 5 was the UTF-16 half read as UTF-8, and what it destroyed was the useful half:
    // wsl.exe had named the condition as Wsl/WSL_E_USER_NOT_FOUND and that never reached the file.
    private readonly List<byte> _out = [];
    private readonly List<byte> _err = [];
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
    public int? ExitCode => _process is { HasExited: true } exited ? exited.ExitCode : null;

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

            byte[] wroteOut;
            byte[] wroteErr;
            lock (_pen)
            {
                wroteOut = [.. _out];
                wroteErr = [.. _err];
            }

            return Sentence(exited.ExitCode, wroteOut, wroteErr);
        }
    }

    /// <summary>
    /// What a launcher that exited is quoted as saying (DD162).
    /// </summary>
    /// <param name="exitCode">What it exited with.</param>
    /// <param name="wroteOut">The raw bytes of standard output, in the encoding it chose for it.</param>
    /// <param name="wroteErr">The raw bytes of standard error, in the encoding it chose for that.</param>
    /// <returns>The one line a status detail carries.</returns>
    /// <remarks>
    /// Separate from the property, and internal, because the property cannot be reached without a
    /// real <c>wsl.exe</c> and this is the whole of what there is to get wrong: the decoding, the
    /// flattening, and the sentence for a launcher that said nothing. The bytes the suite hands
    /// this were captured from a real failed launch.
    ///
    /// <para><b>Two arguments since DD192, because the streams choose their encodings
    /// independently.</b> Decoding a concatenation of the two is decoding a buffer that has no
    /// single encoding, and the answer is not half right: the heuristic picks one, and the half it
    /// picked against comes out as noise. Each is decoded on its own and the results are joined as
    /// text, which is the same thing <c>ConsoleTool.Run</c> has always done.</para>
    ///
    /// <para>The exit code is in the sentence even where there is no text, and that is not padding.
    /// It is the one thing that distinguishes a launcher that died from a daemon that did — which
    /// is the distinction the reader was previously left to guess at.</para>
    /// </remarks>
    internal static string Sentence(int exitCode, byte[] wroteOut, byte[] wroteErr)
    {
        // Standard output first, because it is where wsl.exe puts its own refusal: on 29 August 2026
        // stderr carried a relay error and stdout carried the sentence naming the condition, and a
        // detail read at a glance should lead with the half that says what happened.
        var line = string.Join(" ", Flatten(wroteOut).Concat(Flatten(wroteErr)));

        // Spelled since DD274. A code Windows chose rather than the tool used to reach the journal
        // as a bare number, and 1073807364 is the one that mattered: DBG_TERMINATE_PROCESS, which
        // is the session ending killing the launcher. Read as a decimal it says nothing at all.
        var exited = Preflight.Windows.WindowsExit.Spell(exitCode);

        return line.Length == 0
            ? $"wsl.exe exited {exited}{WithoutAWord}"
            : $"wsl.exe exited {exited}: {line}";
    }

    /// <summary>Decode one stream and reduce it to words a single journal line can hold.</summary>
    /// <param name="bytes">That stream's raw bytes.</param>
    /// <returns>Its non-empty lines, trimmed.</returns>
    /// <remarks>
    /// Decoded by what is in the bytes rather than by what <c>wsl.exe</c> documents. Measured: a
    /// missing distribution answers on stdout, in UTF-16LE with no BOM, and reading that as UTF-8
    /// gives a NUL after every character — a message no reader can use.
    ///
    /// <para>Flattened, because the journal is read as a column of stamped lines and <c>wsl.exe</c>
    /// puts its error code on a second line. A detail carrying its own newline breaks the shape of
    /// every line after it.</para>
    /// </remarks>
    private static IEnumerable<string> Flatten(byte[] bytes) =>
        Preflight.Windows.ConsoleTool.Decode(bytes)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);

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

            // Named and not inherited (DD261). This launcher lives for as long as the daemon does,
            // which is the whole session, and the directory it would hold is the one the engine
            // host was started from — the install's own, for an update or an uninstall.
            WorkingDirectory = Environment.SystemDirectory,
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
            _out.Clear();
            _err.Clear();
        }

        _asked = false;
        _process = Process.Start(startInfo)
            ?? throw new IOException($"{Wsl.LauncherPath} could not be started");

        // Both, and started at once. The daemon's own output never comes this way — the shell above
        // redirects it into the distribution's own log — so what these carry is wsl.exe's, which is
        // a sentence or nothing.
        _draining =
        [
            DrainAsync(_process.StandardOutput.BaseStream, _out),
            DrainAsync(_process.StandardError.BaseStream, _err),
        ];
    }

    /// <summary>Read one of the launcher's streams to its end, keeping the first of it.</summary>
    /// <param name="from">The stream.</param>
    /// <param name="into">That stream's own buffer, which is not the other one's (DD192).</param>
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
    private async Task DrainAsync(Stream from, List<byte> into)
    {
        var buffer = new byte[1024];
        try
        {
            int read;
            while ((read = await from.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                lock (_pen)
                {
                    var room = KeptBytes - into.Count;
                    if (room > 0)
                    {
                        into.AddRange(buffer.AsSpan(0, Math.Min(read, room)));
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
