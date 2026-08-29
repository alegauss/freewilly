namespace FreeWilly.Tray.Cli;

/// <summary>Which of this executable's faces a command line asked for.</summary>
public enum Surface
{
    /// <summary>The tray icon, and a window if it was asked for.</summary>
    Tray,

    /// <summary>The preflight report, on the console.</summary>
    Preflight,

    /// <summary>One of the engine's modes, on the console.</summary>
    Engine,

    /// <summary>What this build calls itself, on the console.</summary>
    Version,

    /// <summary>Every verb there is, on the console.</summary>
    Help,

    /// <summary>The agent surface: <c>read</c> and <c>do</c> (DD24).</summary>
    Agent,

    /// <summary>A PNG of the window, rendered rather than photographed.</summary>
    CaptureWindow,

    /// <summary>The real window, driven through UI Automation rather than read (DD214).</summary>
    DriveWindow,

    /// <summary>The tray's context menu, held open so a screen copy can reach it (DD67).</summary>
    ShowMenu,

    /// <summary>Ask the tray this session is running to exit, on the console (DD121).</summary>
    Quit,

    /// <summary>Open one build, because a <c>docker-desktop://</c> link named it (DD126).</summary>
    OpenBuild,

    /// <summary>A verb this executable does not have.</summary>
    Unknown,
}

/// <summary>
/// What the first argument means.
/// </summary>
/// <remarks>
/// One executable, so one place that decides. Before this there were three — a tray, a preflight and
/// an engine — and the installer had three files to ship, three signatures to buy and three copies
/// of the runtime to carry; the tray also had to find <c>dockerdesk-engine.exe</c> beside itself,
/// which is a way to be broken by a half-finished copy.
///
/// A pure function of the arguments and nothing else: it reaches no console, starts no window and
/// touches no machine, which is what lets every route be asserted on rather than described.
/// </remarks>
public static class CommandLine
{
    /// <summary>The name this executable is shipped as.</summary>
    public const string ExecutableName = "FreeWilly.exe";

    /// <summary>The verb that asks for the preflight.</summary>
    public const string PreflightVerb = "--preflight";

    /// <summary>
    /// The verb that opens the window along with the tray, kept as a synonym that does nothing.
    /// </summary>
    /// <remarks>
    /// A bare launch already opens the window (DD80), so this asks for what it would get anyway. It
    /// stays accepted because shortcuts created by an earlier install carry it and a user edits one
    /// of those by hand — refusing it would break a shortcut that has been working.
    /// </remarks>
    public const string WindowVerb = "--window";

    /// <summary>
    /// The verb that starts the tray and no window.
    /// </summary>
    /// <remarks>
    /// The inversion DD80 makes, and the one caller that wants it: the Run key the installer writes
    /// for "start with Windows". A window in the face at every logon is the regression the rest of
    /// this change would otherwise cause, so silence stops being the default and becomes the thing
    /// that has to ask.
    /// </remarks>
    public const string TrayOnlyVerb = "--tray";

    /// <summary>The verb that prints what this build calls itself.</summary>
    /// <remarks>
    /// These three were string literals in the router until DD100. That mattered because the test
    /// named for verb coverage checks a list, and a verb with no constant to reflect over is one
    /// nothing can enumerate — which is how <c>--capture-window</c> and <c>--tray</c> went missing
    /// from that list, silently, while it stayed green.
    /// </remarks>
    public const string VersionVerb = "--version";

    /// <inheritdoc cref="VersionVerb"/>
    public const string HelpVerb = "--help";

    /// <inheritdoc cref="VersionVerb"/>
    public const string HelpShortVerb = "-h";

    /// <summary>
    /// The verb that asks a running tray to exit, and waits until it has (DD121).
    /// </summary>
    /// <remarks>
    /// Written for the uninstaller, which cannot delete an executable a process is holding open, and
    /// which had nothing to ask with: it removed the Run value and the Add/Remove entry and then
    /// failed on the one file, leaving a root nobody owns. A verb rather than a kill, because the
    /// tray owns an icon the shell has to be told about and a stream the daemon has to be let go of
    /// — a terminated process leaves both, and the icon stays in the overflow until something
    /// hovers it.
    ///
    /// <para>It waits, and that is the half that makes it usable from a script. Signalling and
    /// returning would hand the caller a race it has no way to win.</para>
    /// </remarks>
    public const string QuitVerb = "--quit";

    /// <summary>The verb that renders the window to a PNG without showing it (DD22).</summary>
    public const string CaptureWindowVerb = "--capture-window";

    /// <summary>
    /// The verb that drives the real window through UI Automation (DD214).
    /// </summary>
    /// <remarks>
    /// A verb and not a test, and the asymmetry with <see cref="CaptureWindowVerb"/> is the same one
    /// stated the other way round: that one renders a window nobody is looking at, and this one works
    /// the window a user has. The path it drives stops Docker, so it is invoked deliberately.
    /// </remarks>
    public const string DriveWindowVerb = "--drive-window";

    /// <summary>
    /// The verb a <c>docker-desktop://</c> build link arrives on (DD126).
    /// </summary>
    /// <remarks>
    /// Registered as this install's handler for that scheme, so what follows is whatever any process
    /// on the machine put in a link — which is why the argument is read by
    /// <see cref="Core.Builds.BuildAddress.RefIn"/> and refused rather than passed on when it does
    /// not name a build.
    /// </remarks>
    public const string OpenBuildVerb = "--open-build";

    /// <summary>
    /// The verb that holds the tray's context menu open (DD67).
    /// </summary>
    /// <remarks>
    /// It shows rather than captures, and the asymmetry with <see cref="CaptureWindowVerb"/> is the
    /// point: a window can be rendered off-screen, and a popup is its own top-level window that has
    /// to actually exist before anything can copy it. So the product opens it and
    /// <c>scripts\Capture-Window.ps1</c> — which already refuses to photograph anything unsafe —
    /// takes the picture.
    /// </remarks>
    public const string ShowMenuVerb = "--show-menu";

    /// <summary>
    /// The engine's modes, exactly as <see cref="EngineCommand"/> reads them.
    /// </summary>
    /// <remarks>
    /// Public so a test can hold this list against the one the engine's own help prints. Two lists
    /// of verbs is how a verb becomes reachable in one of them and not the other.
    /// </remarks>
    public static readonly IReadOnlySet<string> EngineVerbs =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "--plan",
            "--acquire",
            "--provision",
            "--run",
            "--stop",
            "--status",
            "--fsck",
            "--fsck-drill",
            "--compact-drill",
            "--api",
            "--watch",
            "--autostart",
        };

    /// <summary>What a command line asked for.</summary>
    /// <param name="Surface">Which face of this executable.</param>
    /// <param name="OpenWindow">Whether the tray should show the window straight away.</param>
    /// <param name="Arguments">What the surface itself should read, the verb included.</param>
    public sealed record Route(Surface Surface, bool OpenWindow, string[] Arguments);

    /// <summary>Read a command line.</summary>
    /// <param name="arguments">Everything after the executable's own name.</param>
    /// <returns>The route. Never null, and never throws.</returns>
    public static Route Of(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        // A bare launch shows the window (DD80). Explorer, the Start menu, the desktop icon and a
        // developer running it out of the publish folder all pass nothing, and every one of them
        // used to land in silence — the icon goes to the Windows 11 overflow, which DD21 measured
        // and which nothing here can promote it out of, so there was no feedback at all and a user
        // with none clicks again.
        if (arguments.Length == 0)
        {
            return new Route(Surface.Tray, OpenWindow: true, []);
        }

        var first = arguments[0];

        if (EngineVerbs.Contains(first))
        {
            return new Route(Surface.Engine, OpenWindow: false, arguments);
        }

        // The agent surface, and it is a bare word rather than a flag on purpose: an allowlist matches
        // the literal prefix `freewilly read`, and a flag would put the two halves in one namespace
        // again — which is the whole thing DD24 exists to undo.
        if (string.Equals(first, AgentSurface.ReadVerb, StringComparison.Ordinal)
            || string.Equals(first, AgentSurface.DoVerb, StringComparison.Ordinal))
        {
            return new Route(Surface.Agent, OpenWindow: false, arguments);
        }

        // Nothing follows it, and the refusal is the same shape as --tray's: this reaches into a
        // running process, and guessing what an argument it does not have was meant to qualify is
        // worse than saying so.
        if (string.Equals(first, QuitVerb, StringComparison.Ordinal))
        {
            return arguments.Length == 1
                ? new Route(Surface.Quit, OpenWindow: false, [])
                : new Route(Surface.Unknown, OpenWindow: false, arguments);
        }

        if (string.Equals(first, CaptureWindowVerb, StringComparison.Ordinal))
        {
            // The verb is dropped: what follows is the output path and an optional tab.
            return new Route(Surface.CaptureWindow, OpenWindow: false, arguments[1..]);
        }

        if (string.Equals(first, DriveWindowVerb, StringComparison.Ordinal))
        {
            // The verb is dropped: what follows is --check or nothing. OpenWindow stays false —
            // this reaches a window through the desktop and does not become one.
            return new Route(Surface.DriveWindow, OpenWindow: false, arguments[1..]);
        }

        // Exactly one argument, the link. A handler is invoked by the shell with the URL as the only
        // thing after the verb, and anything else arriving here is not the shell.
        if (string.Equals(first, OpenBuildVerb, StringComparison.Ordinal))
        {
            return arguments.Length == 2
                ? new Route(Surface.OpenBuild, OpenWindow: true, arguments[1..])
                : new Route(Surface.Unknown, OpenWindow: false, arguments);
        }

        if (string.Equals(first, ShowMenuVerb, StringComparison.Ordinal))
        {
            // The verb is dropped: what follows is the engine state and --seconds.
            return new Route(Surface.ShowMenu, OpenWindow: false, arguments[1..]);
        }

        if (string.Equals(first, PreflightVerb, StringComparison.Ordinal))
        {
            // The verb itself is dropped: what follows is the preflight's own argument list, and it
            // would refuse --preflight as an argument it does not have.
            return new Route(Surface.Preflight, OpenWindow: false, arguments[1..]);
        }

        // Case-insensitive and position-independent, because these are the arguments a Windows
        // shortcut or a Run value carries and both are edited by hand.
        if (arguments.Contains(TrayOnlyVerb, StringComparer.OrdinalIgnoreCase))
        {
            return arguments.Length == 1
                ? new Route(Surface.Tray, OpenWindow: false, [])
                : new Route(Surface.Unknown, OpenWindow: false, arguments);
        }

        if (arguments.Contains(WindowVerb, StringComparer.OrdinalIgnoreCase))
        {
            return arguments.Length == 1
                ? new Route(Surface.Tray, OpenWindow: true, [])
                : new Route(Surface.Unknown, OpenWindow: false, arguments);
        }

        return first switch
        {
            VersionVerb => new Route(Surface.Version, OpenWindow: false, []),
            HelpShortVerb or HelpVerb => new Route(Surface.Help, OpenWindow: false, []),
            _ => new Route(Surface.Unknown, OpenWindow: false, arguments),
        };
    }

    /// <summary>Every verb, in one place, for the console's own help.</summary>
    public static string HelpText =>
        $"""
        {ExecutableName} installs and drives Docker on Windows.

        With no arguments it is the tray icon and the window. Everything else is a console
        verb and prints into the terminal it was started from.

          read <verb>      the agent surface, which mutates nothing
          do <verb>        the agent surface that does
          {TrayOnlyVerb}           the tray icon alone, which is what "start with Windows" uses
          {WindowVerb}         accepted, and what a bare launch already does
          {QuitVerb}           ask the running tray to exit, and wait until it has
          {CaptureWindowVerb} <out.png> [tab]
                           render the window to a PNG off-screen, photographing nothing else
          {DriveWindowVerb} [--check]
                           work the real window through UI Automation and print what it says;
                           with --check it drives the filesystem check, which stops Docker
          {ShowMenuVerb} [state] [--seconds n]
                           hold the tray's context menu open, for scripts\Capture-Window.ps1
          {OpenBuildVerb} <link>
                           open one build in the window; this install's handler for the
                           docker-desktop:// link buildx prints at the end of a build

          {PreflightVerb}      what this machine can host; add --json for an installer
          --plan           the pinned versions, digests and paths; reaches nothing
          --acquire        download and verify every artefact, and stop
          --provision      acquire, import the distribution, install the engine

          --run            start the engine and serve the pipe until Ctrl+C
          --stop           stop the engine and terminate the distribution
          --status         what the engine is doing, by asking it
          --fsck           check the distribution's filesystem; --repair to mend what it finds
          --fsck-drill     rehearse the repair on a scratch disk dirtied on purpose, touching
                           nothing on this machine
          --compact-drill  rehearse the compaction on a scratch disk grown and emptied on
                           purpose, touching nothing on this machine
          --api            version and containers, read through the Engine API
          --watch          print /events as they happen, until Ctrl+C
          --autostart      on | off | status  - off unless you turn it on

          {VersionVerb}        what this build calls itself
          {HelpVerb}           this, and {HelpShortVerb} does the same

        Exit code 0 means the verb finished; 1 names what stopped it; 2 is an argument
        this executable does not have.

        """;
}
