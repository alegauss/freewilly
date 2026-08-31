using System.Diagnostics;
using FreeWilly.Core.Agent;
using FreeWilly.Core.Engine;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// Whether this machine can be asked what a real docker client reports (DD260).
/// </summary>
/// <remarks>
/// The suite's other engine tests talk to the relay over a pipe with no daemon behind it, which is
/// what makes them fast and what makes them blind to an exit code: DD259 broke every attach on the
/// machine and every one of them stayed green. What has to be asserted is the number the client
/// exits with, and there is no fake that has one.
///
/// <para><b>The relay is served here rather than found.</b> Written first against
/// <c>docker_engine</c>, the tests measured whatever build the machine had installed — which on the
/// day DD259 was fixed was the build without the fix, so a green working tree stayed red until
/// somebody reinstalled. A relay of this working tree's own, in front of the same live daemon,
/// answers the question actually being asked.
///
/// <para>Answered once per run and cached, because it is read at discovery for every test that
/// stands on it and each question costs a subprocess.</para>
/// </remarks>
internal static class LiveEngine
{
    /// <summary>
    /// The image these tests run. Small, and its <c>true</c> and <c>sh</c> are all they need.
    /// </summary>
    internal const string Image = "busybox:latest";

    /// <summary>
    /// What every container and project these tests create is named after.
    /// </summary>
    /// <remarks>
    /// A prefix of their own so the sweep below can be sure of what it is removing. Anything else
    /// on the machine belongs to somebody, including the images and volumes this never touches.
    /// </remarks>
    internal const string Prefix = "fw-dd260-";

    /// <summary>A name no other run has.</summary>
    internal static string Named() => $"{Prefix}{Guid.NewGuid():N}";

    private static readonly Lazy<string?> Reason = new(Look);

    /// <summary>Why these tests cannot run here, or <see langword="null"/> where they can.</summary>
    internal static string? Absent => Reason.Value;

    /// <summary>Somewhere to run a client from that is nobody's project.</summary>
    internal static string Anywhere => Path.GetTempPath();

    /// <summary>
    /// A relay out of this working tree, on a pipe of its own, in front of the live daemon.
    /// </summary>
    internal sealed class Served : IAsyncDisposable
    {
        private readonly EnginePipeRelay _relay;
        private readonly Counted _backend = new(new WslSocatBackend());
        private readonly EnginePaths _paths = new();
        private readonly BundledComposeCli _cli;

        /// <summary>Serve one.</summary>
        internal Served()
        {
            Pipe = $"freewilly-live-{Guid.NewGuid():N}";
            _cli = new BundledComposeCli(_paths);
            _relay = new EnginePipeRelay(_backend, Pipe);
            _relay.Start();
        }

        /// <summary>The pipe this is serving, which is not the one the install owns.</summary>
        internal string Pipe { get; }

        /// <summary>The endpoint a client is pointed at with <c>-H</c>.</summary>
        internal string Host => $"npipe:////./pipe/{Pipe}";

        /// <summary>
        /// How many channels to the daemon are open — opened and not yet disposed (DD262).
        /// </summary>
        /// <remarks>
        /// Each one is a <c>wsl.exe</c>. Counted at the seam rather than by looking for the process,
        /// because a machine running this suite has several <c>wsl.exe</c> of its own and a count of
        /// them would answer about the machine instead of about the relay.
        /// </remarks>
        internal int OpenChannels => _backend.Alive;

        /// <summary>Run this install's own docker against this relay.</summary>
        /// <param name="workingDirectory">Where the client runs, which is where a project is.</param>
        /// <param name="arguments">What to ask it, after the host.</param>
        /// <returns>What the client wrote and the code it exited with.</returns>
        /// <remarks>
        /// The host goes in as <c>-H</c> rather than as an environment variable, because the runner
        /// is the shipped one and it sets <c>DOCKER_CONFIG</c> and nothing else — which is the half
        /// that has to keep coming from the install, or <c>compose</c> is not a subcommand.
        /// </remarks>
        internal ComposeResult Run(string workingDirectory, params string[] arguments) =>
            _cli.Run(workingDirectory, ["-H", Host, .. arguments]);

        /// <summary>
        /// Start a client, wait for it to write a line, then end it the way a user does (DD262).
        /// </summary>
        /// <param name="arguments">What to ask it, after the host.</param>
        /// <param name="patience">How long to wait for the first line before giving up.</param>
        /// <returns>The first line it wrote, or empty where it wrote none in time.</returns>
        /// <remarks>
        /// The shipped runner cannot do this: it waits for the process to exit, and the whole point
        /// of a follow is that it does not. So the process is started here — and this is a test, so
        /// DD261's rule about naming a working directory is met rather than enforced.
        ///
        /// <para>Killed and not signalled. Ctrl+C is what a user presses and there is no way to send
        /// one to a child without sharing a console group with it, which would deliver it to the
        /// test host too. What reaches the relay is the same either way: the client's end of the pipe
        /// closes with the connection still live, which is the ending being asserted.</para>
        /// </remarks>
        internal string ReadALineThenEnd(string[] arguments, TimeSpan patience)
        {
            var startInfo = new ProcessStartInfo(_paths.DockerCli)
            {
                WorkingDirectory = Anywhere,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.Environment[BundledComposeCli.ConfigVariable] = _paths.ConfigDirectory;
            foreach (var argument in new[] { "-H", Host }.Concat(arguments))
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var client = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"{_paths.DockerCli} could not be started");

            try
            {
                var line = client.StandardOutput.ReadLineAsync();
                return line.Wait(patience) ? line.Result ?? "" : "";
            }
            finally
            {
                try
                {
                    client.Kill(entireProcessTree: true);
                }
                catch (Exception exception) when (exception is InvalidOperationException
                    or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    // It ended on its own, which is one of the two endings being asserted.
                }

                client.WaitForExit((int)TimeSpan.FromSeconds(30).TotalMilliseconds);
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync() => _relay.DisposeAsync();
    }

    /// <summary>A backend that says how many of its channels are still open (DD262).</summary>
    /// <remarks>
    /// The claim it exists to support is that a client hanging up mid-stream is an ending the relay
    /// acts on: a follow that leaked its channel would leave a <c>wsl.exe</c> attached to the daemon
    /// for the rest of the host's life, one per abandoned follow, and nothing in the product would
    /// say so. Wrapped around the real backend rather than replacing it, because what is being
    /// measured is the live path.
    /// </remarks>
    private sealed class Counted(IEngineBackend inner) : IEngineBackend
    {
        private int _opened;
        private int _closed;

        /// <summary>Opened and not yet disposed.</summary>
        internal int Alive => Volatile.Read(ref _opened) - Volatile.Read(ref _closed);

        /// <inheritdoc/>
        public IEngineChannel Open()
        {
            Interlocked.Increment(ref _opened);
            return new Channel(inner.Open(), () => Interlocked.Increment(ref _closed));
        }

        private sealed class Channel(IEngineChannel inner, Action closed) : IEngineChannel
        {
            private bool _done;

            public Stream ToEngine => inner.ToEngine;

            public Stream FromEngine => inner.FromEngine;

            public void Dispose()
            {
                inner.Dispose();
                if (!_done)
                {
                    _done = true;
                    closed();
                }
            }
        }
    }

    private static string? Look()
    {
        var paths = new EnginePaths();
        if (!File.Exists(paths.DockerCli))
        {
            return $"{paths.DockerCli} is not there, so there is no client to ask for an exit "
                + "code. Install FreeWilly on this machine and re-run to assert against it.";
        }

        var served = new Served();
        try
        {
            if (served.Run(Anywhere, "version", "--format", "{{.Server.Version}}").ExitCode != 0)
            {
                return "no daemon answered through a relay served here, so nothing can report an "
                    + "exit code. Start FreeWilly and re-run to assert against it.";
            }

            Sweep(served);

            if (served.Run(Anywhere, "compose", "version").ExitCode != 0)
            {
                return "this install's docker has no compose subcommand, so the client that "
                    + "reported DD259 cannot be driven. Provision the plugin and re-run to assert "
                    + "against it.";
            }

            if (served.Run(Anywhere, "image", "inspect", Image).ExitCode == 0)
            {
                return null;
            }

            // Pulled rather than skipped over. A gate that stood aside for a missing 7 MB image
            // would stand aside on most machines, and a test that never runs is how DD259 shipped.
            return served.Run(Anywhere, "pull", Image).ExitCode == 0
                ? null
                : $"{Image} is not on this machine and could not be pulled. Run "
                  + $"`docker pull {Image}` and re-run to assert against it.";
        }
        finally
        {
            served.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>Remove whatever a failed run left on the machine.</summary>
    /// <param name="served">The relay to ask, which is the one this run is measuring.</param>
    /// <remarks>
    /// Each test removes its own container, and a run that fails an assertion first does not: the
    /// removal is in a <c>finally</c>, but a broken teardown is exactly the thing that also makes
    /// the removal report a failure. Swept at the start of the next run rather than chased at the
    /// end of the last, because a red suite is where they accumulate and a red suite is not in a
    /// position to tidy up after itself.
    /// </remarks>
    private static void Sweep(Served served)
    {
        var stale = served.Run(Anywhere, "ps", "-aq", "--filter", $"name={Prefix}");
        foreach (var id in stale.Output.Split(
            ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            served.Run(Anywhere, "rm", "-f", id);
        }
    }
}

/// <summary>A fact that stands aside where no daemon is answering (DD260).</summary>
/// <remarks>
/// The opposite condition to <see cref="FactUnlessTheEngineIsRunningAttribute"/>, and the two are
/// not in conflict: that one claims the single-instance object a running host holds, and this one
/// needs the daemon that host is serving. Same door into xUnit 2.9 for both — a virtual
/// <see cref="FactAttribute.Skip"/> read at discovery, which is where a condition known before the
/// body belongs.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class FactWhenTheEngineAnswersAttribute : FactAttribute
{
    /// <inheritdoc/>
    public override string? Skip
    {
        get => LiveEngine.Absent ?? base.Skip;
        set => base.Skip = value;
    }
}
