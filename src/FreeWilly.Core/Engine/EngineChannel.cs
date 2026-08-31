using System.Diagnostics;

namespace FreeWilly.Core.Engine;

/// <summary>A duplex byte channel to the engine's API socket.</summary>
public interface IEngineChannel : IDisposable
{
    /// <summary>Bytes written here reach the daemon.</summary>
    Stream ToEngine { get; }

    /// <summary>Bytes the daemon wrote.</summary>
    Stream FromEngine { get; }
}

/// <summary>Opens channels to the engine. The seam the relay is testable through.</summary>
public interface IEngineBackend
{
    /// <summary>Open one channel. The caller disposes it.</summary>
    /// <returns>The channel.</returns>
    IEngineChannel Open();
}

/// <summary>
/// A channel over <c>wsl.exe</c>'s standard input and output, with <c>socat</c> inside the
/// distribution connecting to the daemon's unix socket.
/// </summary>
/// <remarks>
/// Why through stdio and not over a port: every IP-based route to a daemon inside WSL2 — a
/// forwarded localhost port, the distribution's own eth0 address — is reachable by any process on
/// the machine, and the Engine API grants whatever the daemon can do. A Windows named pipe carries
/// an ACL, so the relay in front of this can be restricted to one user. That is the whole reason
/// for the hop.
///
/// Measured: <c>wsl.exe</c>'s redirected stdio is byte-transparent, CRLF included. A request that
/// answered 400 through here turned out to be carrying a UTF-8 BOM added on the Windows side, not
/// anything WSL did to it.
/// </remarks>
public sealed class WslSocatBackend : IEngineBackend
{
    private readonly string _distribution;
    private readonly string _socketPath;

    /// <summary>Construct a backend.</summary>
    /// <param name="distribution">
    /// The owned distribution, or <see langword="null"/> to ask the machine which one this install
    /// owns — which is the legacy name where an older install is being adopted (DD55).
    /// </param>
    /// <param name="socketPath">Where the daemon listens inside it.</param>
    public WslSocatBackend(
        string? distribution = null,
        string socketPath = EngineLifecycle.SocketPath)
    {
        distribution ??= new EnginePaths().DistributionName;
        ArgumentException.ThrowIfNullOrWhiteSpace(distribution);
        ArgumentException.ThrowIfNullOrWhiteSpace(socketPath);
        _distribution = distribution;
        _socketPath = socketPath;
    }

    /// <inheritdoc/>
    public IEngineChannel Open()
    {
        var startInfo = new ProcessStartInfo(Wsl.LauncherPath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,

            // Named rather than inherited, and it is not a detail. A child holds a lock on its
            // working directory for as long as it lives, and this one lives for as long as a docker
            // client's connection does — so whatever directory the host happened to be started from
            // could not be renamed or removed while the engine served anything. The system
            // directory is nobody's project and cannot be the thing somebody is trying to delete.
            WorkingDirectory = Environment.SystemDirectory,
        };
        foreach (var argument in new[]
                 {
                     "-d", _distribution, "-u", "root", "--exec",
                     "/usr/bin/socat", "-", $"UNIX-CONNECT:{_socketPath}",
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = Process.Start(startInfo)
            ?? throw new IOException($"{Wsl.LauncherPath} could not be started");
        return new ProcessChannel(process);
    }

    private sealed class ProcessChannel(Process process) : IEngineChannel
    {
        public Stream ToEngine { get; } = process.StandardInput.BaseStream;

        public Stream FromEngine { get; } = process.StandardOutput.BaseStream;

        public void Dispose()
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException
                or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // It ended on its own, which is the normal way a channel closes.
            }

            process.Dispose();
        }
    }
}
