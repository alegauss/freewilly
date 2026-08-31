using System.Diagnostics;
using System.IO;
using FreeWilly.Core.Engine;

namespace FreeWilly.Tray.Cli;

/// <summary>
/// Keeps WSL2's shared virtual machine up while something else is being taken down (DD199).
/// </summary>
/// <remarks>
/// <para><b>This exists because of a measurement rather than a guess.</b> Repairing the engine's
/// filesystem rests on WSL leaving a terminated distribution's disk attached to the virtual machine,
/// and that was measured on 29 August 2026: with a second distribution up, terminating the engine's
/// left its disk attached under the same ext4 UUID and <c>e2fsck</c> ran against it.</para>
///
/// <para><b>The same measurement found what nearly broke it.</b> A started distribution is not
/// enough. WSL's idle timeout takes the machine down once no distribution has a running process, and
/// the first attempt lost the virtual machine — and the attachment with it — the moment the second
/// distribution's command returned. So this holds a process rather than a session, and the repair
/// opens it before it terminates anything.</para>
///
/// <para>A <c>sleep</c> and not something cleverer, because the only property wanted is a process
/// that exists. Bounded rather than infinite so a crash here leaves a distribution that empties
/// itself out, and killed on the way past so the ordinary path does not wait for the bound.</para>
/// </remarks>
internal sealed class VmHold : IDisposable
{
    /// <summary>
    /// How long the held process lives if nothing ever disposes it.
    /// </summary>
    /// <remarks>
    /// Longer than any check this holds open — <c>e2fsck</c> over a 60 GB disk is minutes, not
    /// tens of them — and short enough that a process killed between the open and the dispose does
    /// not pin somebody's virtual machine up for the rest of the day.
    /// </remarks>
    internal static readonly TimeSpan Bound = TimeSpan.FromMinutes(30);

    private readonly Process? _held;

    private VmHold(Process? held) => _held = held;

    /// <summary>Open a hold on the machine through <paramref name="distribution"/>.</summary>
    /// <param name="distribution">A distribution that is registered and is not the one being torn down.</param>
    /// <returns>The hold, which ends when it is disposed.</returns>
    /// <remarks>
    /// A failure to start is not thrown. The caller's next steps report their own failures with far
    /// more to say than "the holder did not launch", and the one this would produce is a distribution
    /// that will not run — which the import step immediately before it has already established
    /// works.
    /// </remarks>
    internal static IDisposable On(string distribution)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(distribution);

        var startInfo = new ProcessStartInfo(Wsl.LauncherPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,

            // Named and not inherited (DD261). This is the longest-lived child in the product: the
            // hold exists to keep the machine up for minutes at a time, and for all of them it
            // would otherwise be locking whatever directory the caller happened to be in.
            WorkingDirectory = Environment.SystemDirectory,
        };
        foreach (var argument in new[]
                 {
                     "-d", distribution, "-u", "root", "--exec",
                     "/bin/sh", "-c", $"sleep {Bound.TotalSeconds:0}",
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            return new VmHold(Process.Start(startInfo));
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or InvalidOperationException or IOException)
        {
            return new VmHold(null);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_held is null)
        {
            return;
        }

        try
        {
            if (!_held.HasExited)
            {
                _held.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // It went on its own, which is the bound expiring or the distribution being unregistered
            // underneath it. Both are this hold ending, which is what was wanted.
        }

        _held.Dispose();
    }
}
