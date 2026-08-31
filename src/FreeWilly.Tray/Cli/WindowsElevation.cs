using System.ComponentModel;
using System.Diagnostics;
using FreeWilly.Core.Engine;

namespace FreeWilly.Tray.Cli;

/// <summary>
/// One command, run with administrator rights, on this machine (DD237).
/// </summary>
/// <remarks>
/// <para>Here rather than in Core for the reason <see cref="FilesystemWork"/> is: this owns a
/// <see cref="Process"/> and puts a Windows dialog on the screen. What Core carries is the sequence
/// and the decisions, which is the half worth testing.</para>
///
/// <para><c>UseShellExecute</c> is what makes <c>runas</c> a verb at all, and it is also why nothing
/// here reads the child's output: the two settings are mutually exclusive, and a redirect asked for
/// alongside a verb throws rather than being ignored. The caller that needs words writes them to a
/// file from inside the command.</para>
/// </remarks>
internal sealed class WindowsElevation : IElevated
{
    /// <summary>What Windows returns when a UAC prompt is declined.</summary>
    /// <remarks>
    /// <c>ERROR_CANCELLED</c>. Told apart from every other <see cref="Win32Exception"/> because it
    /// is the one that is not a fault: somebody was asked and said no, and a tool that reported
    /// that as an error would be arguing with an answer it had solicited.
    /// </remarks>
    private const int Cancelled = 1223;

    /// <inheritdoc/>
    public ElevatedRun Run(string fileName, string arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        var start = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = true,
            Verb = "runas",

            // The child is a console tool and its output is already going to a file. A window that
            // flashed up and vanished would only make a UAC prompt somebody had just approved look
            // like something had gone wrong.
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,

            // Named and not inherited (DD261). The elevated child is often a compaction or a
            // repair, and the directory it would otherwise hold is whichever one the tray was
            // started from — which is exactly the sort of thing an update wants to replace.
            WorkingDirectory = Environment.SystemDirectory,
        };

        try
        {
            using var running = Process.Start(start);
            if (running is null)
            {
                // Documented as possible when the shell reuses an existing process, which no
                // elevated console tool does. Reported rather than assumed away: an exit code that
                // was never read must not be able to look like a zero.
                return new ElevatedRun(
                    Ran: false, Failure: "Windows started nothing and said nothing about it");
            }

            running.WaitForExit();
            return new ElevatedRun(Ran: true, ExitCode: running.ExitCode);
        }
        catch (Win32Exception refused) when (refused.NativeErrorCode == Cancelled)
        {
            return new ElevatedRun(Ran: false, Refused: true);
        }
        catch (Exception exception) when (exception is Win32Exception
            or InvalidOperationException or System.IO.FileNotFoundException)
        {
            return new ElevatedRun(Ran: false, Failure: exception.Message);
        }
    }
}
