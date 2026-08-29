using System.IO;
using FreeWilly.Core.Api;
using FreeWilly.Core.Fixtures;
using FreeWilly.Core.Engine;

namespace FreeWilly.Tray.Cli;

/// <summary>
/// Photographs the window by rendering it, off-screen, where nothing else can be in the frame.
/// </summary>
/// <remarks>
/// DD22. A window used to be verified by copying the pixels on screen inside its rectangle, which
/// reads whatever is actually there rather than the window: twice that was somebody else's content —
/// an editor holding a credential and a messaging app holding an appointment — and both reached a
/// transcript, which deleting the file afterwards does not undo.
///
/// This cannot do that. A <c>RenderTargetBitmap</c> over the window's own visual tree has no access to
/// anything outside it, and the window is shown at <c>-32000</c> so it is never composited onto a
/// desktop at all. That also means this works with no interactive desktop present, which a screen copy
/// does not — measured while shipping DD21, where a copy of the notification area came back as a
/// single flat colour on a locked session.
///
/// The screen copy is kept for the one thing a render cannot see: a popup is its own top-level window
/// and is not in this window's visual tree. That path lives in <c>scripts\Capture-Window.ps1</c>, with
/// the overlap check that decides whether it is safe to photograph anything at all.
/// </remarks>
internal static class WindowCapture
{
    private const int Ok = 0;
    private const int Failed = 1;
    private const int Usage = 2;

    /// <summary>How long to let the window settle before rendering it.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(1);

    /// <summary>What asks for the fixture instead of the live engine (DD38).</summary>
    public const string FixtureFlag = "--fixture";

    /// <summary>Render the window to a PNG.</summary>
    /// <param name="args">The output path, a tab header, and <c>--fixture</c>, all optional but the path.</param>
    /// <returns>The process exit code.</returns>
    internal static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            return Complain(
                "takes an output path and optionally a tab, "
                + $"e.g. {CommandLine.ExecutableName} {CommandLine.CaptureWindowVerb} window.png Images");
        }

        var fixture = args.Contains(FixtureFlag, StringComparer.Ordinal);
        args = [.. args.Where(a => !string.Equals(a, FixtureFlag, StringComparison.Ordinal))];

        if (args.Length > 2)
        {
            // Naming the argument, not just the arity: a refusal that does not say what it failed to
            // understand leaves the caller to guess which of their words was wrong.
            return Complain(
                $"unexpected argument {args[2]}: this takes an output path and optionally a tab");
        }

        // A path that looks like a flag is a caller who forgot the path, and writing a file called
        // "--json" is how that mistake becomes silent. Borrowed from claude-tray, where exactly this
        // produced a file named after the flag next door.
        if (args[0].StartsWith('-'))
        {
            return Complain($"{args[0]} is not an output path");
        }

        var outPath = Path.GetFullPath(args[0]);
        var tab = args.Length == 2 ? args[1] : null;

        // The same application the tray makes, made the same way: a capture that resolved different
        // chrome would be a picture of something nobody runs (DD34).
        var app = Ui.Theme.Apply();

        // With no engine answering, the window renders its own empty state — a picture of a real
        // thing, and the only picture this verb could take before DD38. --fixture is the other half:
        // a known machine, so the rows, the ports, the sizes and the states are the same on every
        // machine and in CI. The window is handed one or the other and never learns which.
        IEngineClient api = fixture ? new SampleMachine() : new DockerApi();

        // The builds page reads its own history rather than the Engine API, so it needs its own
        // fixture or --fixture would photograph a page that shells out to the real Buildx and shows
        // whatever this machine happened to build (DD126, L6). Null is the live one.
        FreeWilly.Core.Builds.IBuildHistory? builds =
            fixture ? new FreeWilly.Core.Fixtures.SampleBuilds() : null;

        // And the engine page reads a file, which is the same problem once more (DD165, L6): the
        // live journal is whatever this machine's engine did that afternoon, under a path naming
        // the account it was taken on. Null is the file beside the install.
        IEngineJournal? journal = fixture ? new FreeWilly.Core.Fixtures.SampleJournal() : null;

        // And the readings beside it are the same problem a third time (DD197, L6). Those describe a
        // virtual machine: a capture taken against the real one shows this laptop's disk, its free
        // space and its distribution's error count, none of which belongs in a README. The fixture
        // also answers without yielding, so the panel is drawn rather than caught mid-read.
        IMachineReport? machine = fixture ? new FreeWilly.Core.Fixtures.SampleMachineReport() : null;

        // Before the window exists, because the water starts drifting the moment it is told the
        // engine is up and this verb's fixture says exactly that (DD69, DD70). A picture that caught a
        // random phase would make every capture differ from the last, which is the one property
        // this whole verb is for.
        Ui.Motion.Still = true;

        var window = new Ui.MainWindow(
            api, () => fixture ? EngineState.Running : EngineState.Stopped, () => { },
            builds, journal, machine)
        {
            WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
            // Off the desktop entirely. Not merely unfocused: there is no screen region for anything
            // else to be composited into.
            Left = -32000,
            Top = -32000,
            // Off-screen there is no system backdrop to follow, so ThemeMode="System" would leave the
            // Fluent brushes unresolved and render light text on an unpainted surface.
            ThemeMode = System.Windows.ThemeMode.Dark,
        };

        var code = Failed;
        window.Show();

        if (tab is not null && !window.ShowTab(tab))
        {
            // Refused, not defaulted, and before any file exists. A name this window does not have
            // would otherwise render Containers into the file somebody asked to hold Images and report
            // success about it.
            Console.Error.WriteLine(
                $"{CommandLine.ExecutableName}: this window has no {tab} tab. It has: "
                + string.Join(", ", window.TabNames));
            app.Shutdown();
            return Usage;
        }

        // The same read the tray does when it opens the window, so the picture is of the window a user
        // gets rather than of one that was never asked to draw anything. With no engine this fails and
        // the window shows its own empty state, which is the honest thing to photograph — without it
        // the header row came back blank, seen by looking at the first capture.
        _ = window.RefreshAsync();

        var settle = new System.Windows.Threading.DispatcherTimer { Interval = Settle };
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            try
            {
                var (width, height) = window.SaveSnapshot(outPath);
                Console.Out.WriteLine(
                    $"wrote {outPath}: {width}x{height}, "
                    + $"{tab ?? window.TabNames.FirstOrDefault() ?? "the window"} rendered off-screen");
                code = Ok;
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException or InvalidOperationException
                or ArgumentException or NotSupportedException)
            {
                Console.Error.WriteLine($"{CommandLine.ExecutableName}: {exception.Message}");
                code = Failed;
            }
            finally
            {
                app.Shutdown();
            }
        };
        settle.Start();

        app.Run();

        // The fixture holds nothing to release; the real client holds a pipe and a pool.
        (api as IDisposable)?.Dispose();
        return code;
    }

    private static int Complain(string problem)
    {
        Console.Error.WriteLine(
            $"{CommandLine.ExecutableName} {CommandLine.CaptureWindowVerb}: {problem}");
        return Usage;
    }
}
