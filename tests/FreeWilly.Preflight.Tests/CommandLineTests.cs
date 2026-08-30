using FreeWilly.Core.Engine;
using FreeWilly.Tray.Cli;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// Which face of the one executable a command line reaches (DD14).
/// </summary>
/// <remarks>
/// This is the seam that replaced three executables with one, so it is the seam where a mistake is
/// silent: a verb routed to the tray shows an icon and prints nothing, and a verb routed to a
/// console surface that the tray also matches would flash a window instead of answering. Neither
/// fails loudly, and both are one line of a switch.
/// </remarks>
public sealed class CommandLineTests
{
    [Fact]
    public void No_arguments_is_the_tray_and_the_window()
    {
        // Inverted by DD80. Explorer, the Start menu, the desktop icon and a developer running the
        // published .exe by hand all pass nothing, and every one of them used to land in silence:
        // the icon goes to the Windows 11 overflow, which nothing here can promote it out of, so
        // there was no feedback at all and a user with none clicks again.
        var route = CommandLine.Of([]);

        Assert.Equal(Surface.Tray, route.Surface);
        Assert.True(route.OpenWindow);
    }

    [Theory]
    [InlineData("--tray")]
    [InlineData("--TRAY")]
    public void The_tray_verb_is_the_one_way_to_ask_for_silence(string spelling)
    {
        // The Run key the installer writes for "start with Windows" is the only caller that wants
        // it: a window in the face at every logon is the regression inverting the default could
        // otherwise cause. Case-insensitive, because a Run value is edited by hand.
        var route = CommandLine.Of([spelling]);

        Assert.Equal(Surface.Tray, route.Surface);
        Assert.False(route.OpenWindow);
    }

    [Fact]
    public void The_tray_verb_takes_nothing_else()
    {
        Assert.Equal(Surface.Unknown, CommandLine.Of(["--tray", "--window"]).Surface);
    }

    [Theory]
    [InlineData("--window")]
    [InlineData("--WINDOW")]
    [InlineData("--Window")]
    public void The_window_verb_still_works_and_now_asks_for_the_default(string spelling)
    {
        // Kept as a synonym rather than removed (DD80): a shortcut created by an earlier install
        // carries it, and a user edits one of those by hand in a properties dialog. Refusing it
        // would break a shortcut that has been working. Case-insensitive for the same reason.
        var route = CommandLine.Of([spelling]);

        Assert.Equal(Surface.Tray, route.Surface);
        Assert.True(route.OpenWindow);
    }

    [Fact]
    public void The_preflight_verb_reaches_the_preflight_without_being_passed_on_to_it()
    {
        // The preflight refuses arguments it does not have, and --preflight is one of them: leaving
        // the verb in the list would make `--preflight --json` exit 2 instead of printing a report.
        var route = CommandLine.Of(["--preflight", "--json"]);

        Assert.Equal(Surface.Preflight, route.Surface);
        Assert.Equal(["--json"], route.Arguments);
    }

    [Fact]
    public void The_preflight_verb_on_its_own_passes_nothing_on()
    {
        var route = CommandLine.Of(["--preflight"]);

        Assert.Equal(Surface.Preflight, route.Surface);
        Assert.Empty(route.Arguments);
    }

    [Theory]
    [InlineData("--plan")]
    [InlineData("--acquire")]
    [InlineData("--provision")]
    [InlineData("--run")]
    [InlineData("--stop")]
    [InlineData("--status")]
    [InlineData("--api")]
    [InlineData("--watch")]
    [InlineData("--autostart")]
    public void Every_engine_verb_reaches_the_engine_with_the_verb_still_in_its_hand(string verb)
    {
        // The verb stays: the engine switches on args[0], unlike the preflight.
        var route = CommandLine.Of([verb]);

        Assert.Equal(Surface.Engine, route.Surface);
        Assert.Equal([verb], route.Arguments);
    }

    [Fact]
    public void The_autostart_value_travels_with_it()
    {
        var route = CommandLine.Of(["--autostart", "on"]);

        Assert.Equal(Surface.Engine, route.Surface);
        Assert.Equal(["--autostart", "on"], route.Arguments);
    }

    [Fact]
    public void The_verb_the_tray_launches_for_itself_is_one_of_them() =>
        // The tray starts the engine as this same executable with --run (EngineHolder). A rename on
        // one side of that and the Start engine menu item silently opens a second tray icon.
        Assert.Equal(Surface.Engine, CommandLine.Of(["--run"]).Surface);

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void Help_is_a_console_surface_and_not_a_tray(string spelling) =>
        Assert.Equal(Surface.Help, CommandLine.Of([spelling]).Surface);

    [Fact]
    public void The_quit_verb_is_a_console_surface_and_not_a_second_tray() =>
        // DD121. It reaches into the tray that is already running, so routing it anywhere near
        // Surface.Tray would start the very process it exists to end.
        Assert.Equal(Surface.Quit, CommandLine.Of(["--quit"]).Surface);

    [Fact]
    public void The_quit_verb_takes_nothing_else() =>
        // The same refusal --tray gets, for the same reason: this reaches into a running process,
        // and guessing what an argument it does not have was meant to qualify is worse than saying
        // so. The uninstaller is the caller, and a mistyped argument there must not be absorbed.
        Assert.Equal(Surface.Unknown, CommandLine.Of(["--quit", "--force"]).Surface);

    [Fact]
    public void The_version_verb_is_its_own_surface() =>
        Assert.Equal(Surface.Version, CommandLine.Of(["--version"]).Surface);

    [Theory]
    [InlineData("--nonsense")]
    [InlineData("-x")]
    [InlineData("provision")]
    [InlineData("--Run")]
    public void An_argument_this_executable_does_not_have_is_refused(string argument) =>
        // --Run among them, deliberately: the engine's own switch is case-sensitive, so routing a
        // differently-cased verb to it would reach the engine's "unknown argument" rather than this
        // one, and the exit code would be right for the wrong reason.
        Assert.Equal(Surface.Unknown, CommandLine.Of([argument]).Surface);

    [Fact]
    public void The_window_verb_mixed_with_anything_else_is_refused() =>
        // A tray cannot also be a console verb, and guessing which half was meant is worse than
        // saying so.
        Assert.Equal(Surface.Unknown, CommandLine.Of(["--window", "--status"]).Surface);

    [Fact]
    public void Every_verb_that_routes_somewhere_is_in_the_help_text()
    {
        // The help used to be two texts, one in the engine and one in the preflight, and a verb
        // documented in neither is a verb nobody can find. There is one text now; this is what
        // keeps it honest when a verb is added to the router.
        var help = CommandLine.HelpText;
        var verbs = DeclaredVerbs();

        // The count is asserted so the reflection cannot quietly find nothing. A rename of the
        // "Verb" suffix would otherwise turn this into a loop over an empty list — which is the
        // shape of the defect it replaces, not a fix for it.
        Assert.True(verbs.Count >= 15, $"only {verbs.Count} verbs were found to check");

        foreach (var verb in verbs)
        {
            Assert.Contains(verb, help, StringComparison.Ordinal);
        }

        Assert.Contains(CommandLine.ExecutableName, help, StringComparison.Ordinal);
    }

    /// <summary>Every verb this executable declares, found rather than listed (DD100).</summary>
    /// <remarks>
    /// What stood here was a hand-written list of names beside a loop over <c>EngineVerbs</c>, and
    /// <c>--capture-window</c> and <c>--tray</c> had both gone missing from it, silently, while it
    /// stayed green — so a test called "every verb that routes somewhere" was asserting about
    /// whichever verbs somebody had remembered.
    /// </remarks>
    private static IReadOnlyList<string> DeclaredVerbs()
    {
        var found = new List<string>(CommandLine.EngineVerbs);

        foreach (var type in new[] { typeof(CommandLine), typeof(AgentSurface) })
        {
            found.AddRange(type
                .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(field => field is { IsLiteral: true, IsInitOnly: false })
                .Where(field => field.FieldType == typeof(string))
                .Where(field => field.Name.EndsWith("Verb", StringComparison.Ordinal))
                .Select(field => (string)field.GetRawConstantValue()!));
        }

        return found;
    }

    [Fact]
    public void Every_verb_the_router_matches_on_is_one_it_declares()
    {
        // The other half, and the one that closes the hole. Reflection finds a verb that was given
        // a constant; a verb typed straight into `Of` as a literal is invisible to it, and that is
        // exactly how a route gets added without the help ever hearing about it. So the router's
        // own body is read: since DD100 it compares against constants alone, and any string literal
        // appearing there is a verb nobody declared.
        var source = File.ReadAllText(System.IO.Path.Combine(
            RepositoryRoot(), "src/FreeWilly.Tray/Cli/CommandLine.cs"));

        var start = source.IndexOf("public static Route Of(", StringComparison.Ordinal);
        Assert.True(start > 0, "the router was renamed and this guard now reads nothing");

        var end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "the end of the router was not found");

        var body = source[start..end]
            .Split('\n')
            .Select(line => line.TrimStart())
            .Where(line => !line.StartsWith("//", StringComparison.Ordinal));

        var literals = System.Text.RegularExpressions.Regex
            .Matches(string.Join('\n', body), "\"([^\"]*)\"")
            .Select(match => match.Groups[1].Value)
            .ToList();

        Assert.True(
            literals.Count == 0,
            "the router matches on string literals rather than declared verbs, so nothing can "
            + $"enumerate them for the help: {string.Join(", ", literals)}");
    }

    [Fact]
    public void Every_flag_the_help_text_offers_is_one_the_router_answers()
    {
        // DD204's finding, and it is the guard rather than the fix. `--fsck` shipped with a line in
        // the help, a case in EngineCommand's switch and no entry in EngineVerbs, so the router sent
        // it to Unknown and the verb was unreachable from the moment it landed. Nothing noticed,
        // because the help lists it and the switch handles it and neither is where the gap was.
        // Given whatever the line says it takes. A flag written `--open-build <link>` refuses
        // without one, and that refusal is correct rather than the gap being looked for here.
        var offered = CommandLine.HelpText
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("--", StringComparison.Ordinal))
            .Select(line => (Flag: line.Split(' ', 2)[0], Takes: line.Contains('<', StringComparison.Ordinal)))
            .DistinctBy(offer => offer.Flag, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(offered);
        foreach (var (flag, takes) in offered)
        {
            string[] line = takes ? [flag, "argument"] : [flag];

            Assert.True(
                CommandLine.Of(line).Surface != Surface.Unknown,
                $"{flag} is in the help text and the router does not answer it, so a caller who "
                + "reads the help gets `unknown argument` back");
        }
    }

    [Fact]
    public void The_compaction_is_reachable_from_a_terminal_like_the_check_is()
    {
        // DD242. DD204 gave the check and the repair two surfaces through one seam and said why:
        // two copies of a sequence are fine until one is edited. The compaction arrived in DD211
        // with a button and no verb and stayed that way through six tasks, so the only way to run
        // the real thing was to drive the window through UI Automation and answer a message box.
        Assert.Contains("--compact", CommandLine.EngineVerbs);
        Assert.Equal(Surface.Engine, CommandLine.Of(["--compact"]).Surface);

        // The flag reaches the verb rather than being eaten as an unexpected argument, which is the
        // half that would leave the elevated route unreachable from here.
        var elevated = CommandLine.Of(["--compact", "--as-administrator"]);
        Assert.Equal(Surface.Engine, elevated.Surface);
        Assert.Equal(["--compact", "--as-administrator"], elevated.Arguments);

        // And it is not the drill, which rehearses on a scratch distribution with the prune stubbed
        // out. Confusing the two is the one mistake here that would matter.
        Assert.Contains("--compact-drill", CommandLine.EngineVerbs);
        Assert.NotEqual("--compact", "--compact-drill");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(System.IO.Path.Combine(directory.FullName, "FreeWilly.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "the repository root was not found above the test binaries");
        return directory!.FullName;
    }

    // ---- the verb that opens a popup so something can photograph it (DD67) --------------------

    [Fact]
    public void The_show_menu_verb_routes_to_its_own_surface_without_its_own_name()
    {
        // Dropped like --capture-window's and --preflight's: what follows belongs to the surface,
        // and MenuPreview would refuse its own verb as an argument it does not have.
        var route = CommandLine.Of(["--show-menu", "running", "--seconds", "5"]);

        Assert.Equal(Surface.ShowMenu, route.Surface);
        Assert.False(route.OpenWindow);
        Assert.Equal(["running", "--seconds", "5"], route.Arguments);
    }

    [Fact]
    public void A_bare_show_menu_is_a_stopped_engine_and_a_deadline()
    {
        // The default has to need no engine: a machine with nothing installed is the state a
        // reviewer most needs a picture of, and the one a capture runs on.
        Assert.True(MenuPreview.TryRead([], out var state, out var seconds, out var refusal));

        Assert.Null(refusal);
        Assert.Equal(EngineState.Stopped, state);
        Assert.Equal(MenuPreview.DefaultSeconds, seconds);
    }

    [Theory]
    [InlineData("running", EngineState.Running)]
    [InlineData("STARTING", EngineState.Starting)]
    [InlineData("stopped", EngineState.Stopped)]
    public void The_state_named_is_the_state_the_menu_reflects(string argument, EngineState expected)
    {
        Assert.True(MenuPreview.TryRead([argument], out var state, out _, out _));
        Assert.Equal(expected, state);
    }

    [Theory]
    [InlineData("--seconds")]
    [InlineData("--seconds", "0")]
    [InlineData("--seconds", "601")]
    [InlineData("--seconds", "-5")]
    [InlineData("--seconds", "soon")]
    [InlineData("paused")]
    public void An_argument_this_verb_does_not_have_is_refused_and_named(params string[] arguments)
    {
        // It holds a menu open on somebody's desktop, so a misread argument that fell back to a
        // default would be a run that looks like it worked and photographed the wrong thing.
        Assert.False(MenuPreview.TryRead(arguments, out _, out _, out var refusal));
        Assert.NotNull(refusal);
    }

    [Fact]
    public void A_null_command_line_is_a_defect_here_rather_than_a_route() =>
        Assert.Throws<ArgumentNullException>(() => CommandLine.Of(null!));

    [Fact]
    public void A_build_link_routes_to_the_window_carrying_the_link_alone()
    {
        // The shell invokes a handler with the URL as the only thing after the verb (DD126). The
        // verb is dropped, and the window opens: a link that showed only a tray icon would look
        // like the click did nothing.
        var route = CommandLine.Of([CommandLine.OpenBuildVerb, "docker-desktop://dashboard/build/a/b/c"]);

        Assert.Equal(Surface.OpenBuild, route.Surface);
        Assert.True(route.OpenWindow);
        Assert.Equal(["docker-desktop://dashboard/build/a/b/c"], route.Arguments);
    }

    [Theory]
    [InlineData()]
    [InlineData("a", "b")]
    public void A_build_link_with_anything_but_one_argument_is_not_one(params string[] rest)
    {
        // Same refusal as --quit's and for the same reason: this reaches into a running process, and
        // guessing what an argument it does not have was meant to qualify is worse than saying so.
        Assert.Equal(
            Surface.Unknown,
            CommandLine.Of([CommandLine.OpenBuildVerb, .. rest]).Surface);
    }

    [Fact]
    public void The_build_link_verb_is_named_in_the_help()
    {
        // A verb documented in one of two lists is a verb somebody cannot find.
        Assert.Contains(CommandLine.OpenBuildVerb, CommandLine.HelpText, StringComparison.Ordinal);
    }
}
