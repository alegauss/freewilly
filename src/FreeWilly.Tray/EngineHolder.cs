using System.Diagnostics;
using FreeWilly.Core.Api;
using FreeWilly.Core.Engine;

namespace FreeWilly.Tray;

/// <summary>How a process is started. The seam the holder is testable through.</summary>
public interface IProcessLauncher
{
    /// <summary>Start <paramref name="fileName"/> with <paramref name="arguments"/>, detached.</summary>
    /// <param name="fileName">The executable.</param>
    /// <param name="arguments">Its command line.</param>
    /// <returns>Null when it started, or why it did not.</returns>
    string? Launch(string fileName, string arguments);
}

/// <summary>
/// Starts the engine as a process that outlives this one.
/// </summary>
/// <remarks>
/// The engine only stays up while a Windows process holds the <c>wsl.exe</c> children and the pipe
/// relay — measured, since neither <c>nohup</c> nor <c>setsid</c> survives inside WSL2. So something
/// has to hold it, and the shape that follows is a separate <c>--run</c> the tray launches and does
/// not own.
///
/// <para><b>Not owning it is no longer the same as not stopping it</b> (DD128). This used to say
/// that quitting the tray must not stop the engine, because a container running a database another
/// process is using cannot die because somebody closed an icon. What that reasoning left out is the
/// WSL2 virtual machine underneath: quitting gave back the icon and kept the gigabytes, which is the
/// complaint this whole project answers. So <see cref="TrayApplication.Quit"/> now runs
/// <see cref="Stop"/> on its way out. The launch is still detached — that is what lets the engine
/// outlive a tray that crashes, and what keeps the icon from waiting on a virtual machine — but the
/// ordinary exit takes the engine with it.</para>
///
/// A process and not a Windows service. The service is what the non-goal rules out, and it is also
/// what would put the engine back on every boot without being asked.
///
/// The process it starts is this same executable with <c>--run</c> (DD14). It used to be a second
/// file, <c>dockerdesk-engine.exe</c>, expected beside the tray: a copy that arrived without it —
/// half an unzip, a shortcut to the wrong folder — had a Start engine menu item whose only possible
/// answer was that the engine was missing. An executable is always beside itself.
/// </remarks>
public sealed class EngineHolder(string enginePath, IProcessLauncher launcher)
{
    /// <summary>Where the engine is: this executable.</summary>
    /// <returns>The path.</returns>
    public static string ThisProcess() =>
        // System.IO.Path spelled out: enabling WPF brings System.Windows.Shapes.Path into the
        // implicit usings, and the unqualified name then resolves to a drawing primitive.
        //
        // ProcessPath is null only for a host this project does not produce, and the fallback names
        // the file the installer laid down rather than throwing at the first click.
        Environment.ProcessPath
        ?? System.IO.Path.Combine(AppContext.BaseDirectory, Cli.CommandLine.ExecutableName);

    /// <summary>The engine executable this holder drives.</summary>
    public string EnginePath { get; } = enginePath;

    /// <summary>Start the engine, in a process this one does not own.</summary>
    /// <returns>Null when it started, or why it did not.</returns>
    public string? Start() => launcher.Launch(EnginePath, "--run");

    /// <summary>
    /// Stop the engine.
    /// </summary>
    /// <remarks>
    /// Through the same executable rather than by killing a pid: <c>--stop</c> terminates the
    /// distribution, and whatever is holding the engine notices and comes down with it. That works
    /// whoever started it, including a run from a terminal before the tray existed.
    /// </remarks>
    /// <returns>Null when it ran, or why it did not.</returns>
    public string? Stop() => launcher.Launch(EnginePath, "--stop");
}

/// <summary>Launches through the shell, so the child inherits no console from this process.</summary>
public sealed class DetachedLauncher : IProcessLauncher
{
    /// <inheritdoc/>
    public string? Launch(string fileName, string arguments)
    {
        // Reported and not thrown. A missing executable used to take the whole tray down from a
        // click handler — measured, with the engine simply not beside the tray in a dev build — and
        // an icon that vanishes when you press its menu item is worse than any error message.
        if (!System.IO.File.Exists(fileName))
        {
            return $"{System.IO.Path.GetFileName(fileName)} is not in "
                + $"{System.IO.Path.GetDirectoryName(fileName)}";
        }

        try
        {
            // UseShellExecute, deliberately: started with it false the child inherits this process's
            // console, and a Ctrl+C meant for the tray would reach the engine too.
            using var started = Process.Start(new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            return null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or InvalidOperationException or System.IO.IOException)
        {
            return $"starting {System.IO.Path.GetFileName(fileName)} failed: {exception.Message}";
        }
    }
}

/// <summary>What the tray shows, derived from what the event stream is doing.</summary>
/// <remarks>
/// Nothing here polls the engine. The stream's own connection state is the indicator: it is
/// connected exactly when the engine is answering, which is the same definition the lifecycle uses,
/// arrived at without a timer.
/// </remarks>
public static class TrayState
{
    /// <summary>The engine state to show.</summary>
    /// <param name="stream">What the event loop is doing.</param>
    /// <param name="startRequested">Whether the user asked for a start that has not landed yet.</param>
    /// <returns>The state.</returns>
    public static EngineState For(EventStreamState stream, bool startRequested)
    {
        if (stream is EventStreamState.Watching)
        {
            return EngineState.Running;
        }

        // Connecting and Reconnecting both mean "not answering". Which of Stopped and Starting that
        // is depends on whether anybody asked for it to come up, and only the tray knows that.
        return startRequested ? EngineState.Starting : EngineState.Stopped;
    }

    /// <summary>
    /// How long a requested start may outlive the evidence for it before the tray stops claiming it.
    /// </summary>
    /// <remarks>
    /// Longer than the 60 seconds <see cref="EngineLifecycle.StartAsync"/> gives the daemon inside
    /// <c>--run</c>, and that ordering is the whole value: a tray that gave up first would report a
    /// failure over a start that is still going, which is the same lie as Starting forever told the
    /// other way round.
    /// </remarks>
    public static readonly TimeSpan StartBudget = TimeSpan.FromSeconds(75);

    /// <summary>
    /// Why a start cannot be attempted at all, or <see langword="null"/> where it can (DD120).
    /// </summary>
    /// <param name="engineInstalled">Whether the owned distribution is registered with WSL.</param>
    /// <param name="distribution">The distribution this install owns, so the answer names it.</param>
    /// <returns>What to say instead of starting, or <see langword="null"/>.</returns>
    /// <remarks>
    /// Asked before the launch rather than diagnosed after it. <c>--run</c> already refuses this
    /// case and says so, but it says it on a hidden console nobody is reading — so the tray showed
    /// Starting and the one fact the user needed went nowhere. The remedy is in the sentence,
    /// because "not installed" without it is the same dead end one step later.
    /// </remarks>
    public static string? WhyAStartWouldNotLand(bool engineInstalled, string distribution) =>
        engineInstalled
            ? null
            : $"the engine is not installed: no WSL distribution named {distribution} is "
              + "registered. Run `freewilly --provision` to download and install it.";

    /// <summary>What to say about a start that was launched and never answered.</summary>
    /// <param name="budget">How long it was given.</param>
    /// <param name="journal">Where the host wrote its account of the start (DD137).</param>
    /// <returns>The sentence.</returns>
    /// <remarks>
    /// <para>It used to name <c>/var/log/dockerd.log</c> inside the distribution, and DD190 took
    /// that out. DD162 had already removed the same pointer from the host and the tray kept its own
    /// copy: the log is the right file for a daemon that started and then died, and the wrong one
    /// for every launch that never reached a daemon at all — which on 29 August 2026 meant sending
    /// the reader into a filesystem the failure had just made unreadable, for a file dockerd had
    /// never opened.</para>
    ///
    /// <para>What it names instead is the journal, which is on Windows, is readable whatever the
    /// distribution is doing, and since DD190 carries both the launcher's words and what they mean.
    /// That is the file with the answer in it in every case rather than in one of them.</para>
    /// </remarks>
    public static string StartDidNotLand(TimeSpan budget, string journal) =>
        $"the engine did not answer within {budget.TotalSeconds:0}s. What the host saw is in "
        + $"{journal}.";
}
