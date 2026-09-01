using FreeWilly.Core.Api;
using FreeWilly.Core.Engine;
using FreeWilly.Core.Preflight.Windows;

namespace FreeWilly.Tray;

/// <summary>The three things a session-ending teardown does to the machine (DD188).</summary>
/// <remarks>
/// An interface and not three lambdas because the shutdown path is the one path on this machine
/// nobody can step through: it runs while Windows is dismantling the session, on a thread that is
/// about to be killed, and its whole output is two lines in a file read the next morning. What can
/// be tested is the decision, so the decision is the part that has a seam.
/// </remarks>
internal interface IEngineTeardown
{
    /// <summary>Tell the live engine host the stop is deliberate.</summary>
    /// <returns><see langword="true"/> where a host was there to hear it.</returns>
    bool TellTheLiveHost();

    /// <summary>Whether the distribution this install owns is still up (DD272).</summary>
    /// <returns>
    /// <see langword="true"/> where it is running, and where the probe could not say. Unknown reads
    /// as up on purpose: terminating one that is already down costs a wasted call, and not
    /// terminating one that is up costs the ext4.
    /// </returns>
    bool DistributionIsUp();

    /// <summary>Take the distribution down from this process.</summary>
    /// <returns>What was done, for the journal.</returns>
    string Terminate();
}

/// <summary>
/// What the tray does about the engine when Windows ends the session (DD188).
/// </summary>
/// <remarks>
/// <para>What this replaced was a <see cref="System.Diagnostics.Process"/> start of this executable
/// with <c>--stop</c>, launched under <c>UseShellExecute</c> and waited for by nobody (DD129). Two
/// things about that shape fail during a shutdown, and both did: <c>ShellExecuteEx</c> routes
/// through a shell being torn down at the same moment, and the handler returned at once, so nothing
/// on the machine was waiting when Windows began killing the session. Seven session endings in the
/// journal between 23 and 28 August 2026 carry neither the Stopped line nor the host-is-done line
/// that follow every Quit in the same second, so the spawned process never reached even the named
/// event.</para>
///
/// <para><b>The host is the one that should do this, and since DD187 it hears Windows itself.</b>
/// So this is a backstop rather than the mechanism: it says the stop is deliberate, gives the host
/// the same budget Windows gives everyone, and only runs the terminate where the host is not there
/// or did not manage it. Racing the host to <c>wsl --terminate</c> would be two processes taking the
/// same distribution down, which is how a teardown turns back into the unclean unmount it exists to
/// prevent.</para>
/// </remarks>
internal static class SessionTeardown
{
    /// <summary>How often the wait asks whether the distribution has gone.</summary>
    /// <remarks>
    /// A second since DD272, and the change of question is the reason. What this used to ask was a
    /// pipe connect, which costs nothing and could be asked four times a second; what it asks now is
    /// <c>wsl --list --running</c>, which is a process. Four launches inside a four-second budget is
    /// already the load DD134 warns about, and asking sixteen times would spend the shutdown on the
    /// question rather than on the answer.
    /// </remarks>
    internal static readonly TimeSpan Poll = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Answer a session ending, and say what happened.
    /// </summary>
    /// <param name="machine">The machine this reaches.</param>
    /// <param name="now">The clock, so a test can spend the budget without spending the time.</param>
    /// <param name="pause">The wait between polls, for the same reason.</param>
    /// <returns>One line for the journal.</returns>
    internal static string Run(
        IEngineTeardown machine, Func<DateTimeOffset> now, Action<TimeSpan> pause)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(now);
        ArgumentNullException.ThrowIfNull(pause);

        // Said first, and it is the same ordering `--stop` uses (DD136): the host puts back an
        // engine it loses, so a teardown that has not announced itself is indistinguishable in
        // there from WSL2 dying under a suspend, and it would start reviving what this is taking
        // down.
        if (!machine.TellTheLiveHost())
        {
            // Nothing is serving this session's engine, so nothing else is going to take the
            // distribution down. This is the case DD187 cannot cover, because the process it gave
            // the subscription to is not running.
            return $"no engine host to tell, so this took it down: {machine.Terminate()}";
        }

        // The distribution and not the pipe, which is the whole of DD272. Dropping the relay is the
        // first thing `StopAsync` does, so a quiet pipe says the teardown has begun rather than that
        // it finished — and this stood down there. On 31 August 2026 the tray wrote "the engine host
        // took it down" at 21:51:46 and the host wrote "still tearing down after 4s" two seconds
        // later, about the same teardown; the second line is the true one, and no terminate ran.
        var deadline = now() + Cli.EngineCommand.SessionEndingBudget;
        while (now() < deadline)
        {
            if (!machine.DistributionIsUp())
            {
                return "the engine host took it down";
            }

            pause(Poll);
        }

        // The host had the whole budget and the pipe is still answering. Windows is not waiting
        // longer either way, so the choice here is between one more `wsl --terminate` and a virtual
        // machine reaped with its root never unmounted, which is the ext4 that was repaired by hand
        // on 29 August 2026.
        return $"the engine host ran out of time, so this took it down: {machine.Terminate()}";
    }
}

/// <summary>The real machine, for the tray that is actually signing out.</summary>
internal sealed class LiveEngineTeardown : IEngineTeardown
{
    private readonly EnginePaths _paths = new();
    // Held as the interface, so the probe budget comes from `IWsl.Run`'s own default rather than
    // being restated here. On the concrete class that overload does not exist.
    private readonly IWsl _wsl = new Wsl();

    /// <inheritdoc/>
    public bool TellTheLiveHost() => Cli.SingleEngine.TellTheLiveOneToStop();

    /// <inheritdoc/>
    /// <remarks>
    /// The registry first, because it answers without a process and it answers while WSL is shut
    /// down — a machine that never provisioned an engine reaches this on every logoff, and it should
    /// not pay a <c>wsl.exe</c> launch to be told there is nothing to wait for.
    ///
    /// <para>Then the running list, and a launch that did not answer reads as still up. That is the
    /// direction DD272 argues for and it is not symmetric: this is exactly the launch a session
    /// ending may refuse (DD270), so a probe that failed is no evidence the distribution went with
    /// it.</para>
    /// </remarks>
    public bool DistributionIsUp()
    {
        if (!_paths.DistributionRegistered)
        {
            return false;
        }

        var running = _wsl.Run("--list", "--running", "--quiet");
        return !running.Succeeded
            || running.Output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Any(line => line.Trim().Equals(
                    _paths.DistributionName, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The same lifecycle <c>--stop</c> builds, doing the same work. In this process the relay is
    /// null and no daemon handle is held, so what is left of it is the two calls that matter: the
    /// SIGTERM that lets the containers stop themselves (DD189), and the terminate that unmounts the
    /// distribution's ext4 where a kill would not.
    ///
    /// <para>Hurried, because this runs inside a session ending. It is the same reason the host uses
    /// that budget on the same event, and the two are the same constant so they cannot drift into
    /// disagreeing about how long a shutdown has.</para>
    /// </remarks>
    public string Terminate() =>
        new EngineLifecycle(new Wsl(), new WslDaemonProcess(), new WslSocatBackend())
            .StopAsync(EngineLifecycle.HurriedGrace).GetAwaiter().GetResult().Detail;
}
