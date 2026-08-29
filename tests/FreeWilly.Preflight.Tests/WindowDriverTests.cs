using FreeWilly.Tray.Cli;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// The driving verb's command line, and the fact that it stays out of the suite (DD214).
/// </summary>
/// <remarks>
/// <para><b>The drive itself is not asserted here, and that is the whole point of the task.</b> It
/// needs a real window on a real desktop, and the half behind <c>--check</c> stops Docker and
/// terminates a distribution — which is why it is a verb somebody runs rather than a test that runs
/// itself. It was verified by running it: against a tray left over from an older build it found the
/// window, selected the Engine destination, found Check filesystem, and refused because that window
/// has no Compact button. The source has one. That gap is exactly the class of defect every other
/// assertion this project makes about the window cannot see.</para>
///
/// <para>What is asserted here is the part that is pure: which surface the verb reaches, that its
/// one flag survives the routing, and that it is not filed among the verbs that start an engine.</para>
/// </remarks>
[Collection(ConsoleCollection.Name)]
public sealed class WindowDriverTests
{
    [Fact]
    public void The_driving_verb_reaches_its_own_surface_and_opens_no_window_of_its_own()
    {
        // It reaches a window through the desktop rather than becoming one. A route that carried
        // OpenWindow would make this verb create the very thing it is supposed to find.
        var route = CommandLine.Of([CommandLine.DriveWindowVerb]);

        Assert.Equal(Surface.DriveWindow, route.Surface);
        Assert.False(route.OpenWindow);
        Assert.Empty(route.Arguments);
    }

    [Fact]
    public void The_flag_that_stops_docker_travels_with_it()
    {
        // Behind a flag on top of being a verb, which is the same asymmetry DD199 settled for the
        // check and the repair: reading the window changes nothing, and driving the check does not.
        var route = CommandLine.Of([CommandLine.DriveWindowVerb, WindowDriver.CheckFlag]);

        Assert.Equal(Surface.DriveWindow, route.Surface);
        Assert.Equal([WindowDriver.CheckFlag], route.Arguments);
    }

    [Fact]
    public void An_argument_it_does_not_have_is_refused_before_it_reaches_a_window() =>
        // Refused at the surface rather than after a window has been launched: this verb starts a
        // tray where there is none, and doing that on the way to complaining about a typo would
        // leave a window behind for a command that never ran.
        Assert.Equal(2, WindowDriver.Run(["--repair"]));

    [Fact]
    public void The_verb_is_in_the_help_text() =>
        // The one help text there is, so a verb added to the router is a verb somebody can find.
        Assert.Contains(
            CommandLine.DriveWindowVerb, CommandLine.HelpText, StringComparison.Ordinal);

    [Fact]
    public void It_is_not_one_of_the_engine_verbs() =>
        // The engine verbs are matched as a set, and one of them here would route a drive into
        // EngineCommand — which reads --check as an unknown argument and exits 2.
        Assert.DoesNotContain(CommandLine.DriveWindowVerb, CommandLine.EngineVerbs);

    [Fact]
    public void It_is_not_the_capture_verb_and_the_two_do_not_collide()
    {
        // Two verbs about the same window with opposite jobs: one renders a window nobody is
        // looking at, the other works the window a user has. A router that confused them would
        // photograph nothing and drive nothing.
        Assert.NotEqual(CommandLine.CaptureWindowVerb, CommandLine.DriveWindowVerb);
        Assert.Equal(
            Surface.CaptureWindow,
            CommandLine.Of([CommandLine.CaptureWindowVerb, "out.png"]).Surface);
        Assert.Equal(
            Surface.DriveWindow, CommandLine.Of([CommandLine.DriveWindowVerb]).Surface);
    }

    [Fact]
    public void The_controls_it_drives_are_the_names_the_markup_carries()
    {
        // WPF publishes x:Name as the automation id, which is the whole address this driver uses.
        // Asserted against the markup because the failure is silent in both directions: a rename in
        // XAML leaves the driver looking for a control that is gone, and a rename here leaves it
        // looking for one that never existed. Neither is a compile error.
        var markup = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Ui/Pages/EnginePage.xaml"));
        var shell = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Ui/MainWindow.xaml"));
        var driver = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Cli/WindowDriver.cs"));

        foreach (var id in new[] { "Check", "Compact", "FoundHeadline", "FoundDetail", "FoundSteps" })
        {
            Assert.Contains($"x:Name=\"{id}\"", markup, StringComparison.Ordinal);
            Assert.Contains($"\"{id}\"", driver, StringComparison.Ordinal);
        }

        Assert.Contains("x:Name=\"NavEngine\"", shell, StringComparison.Ordinal);
        Assert.Contains("\"NavEngine\"", driver, StringComparison.Ordinal);
    }

    [Fact]
    public void The_confirmation_is_answered_by_control_id_and_never_by_its_caption()
    {
        // Measured on the machine this was written on, where Windows renders IDYES as "Sim". A
        // driver that matched the word would pass on exactly one desk and report the dialog as
        // missing everywhere else.
        var driver = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/FreeWilly.Tray/Cli/WindowDriver.cs"));

        Assert.Contains("AutomationIdProperty", driver, StringComparison.Ordinal);
        foreach (var caption in new[] { "\"Sim\"", "\"Yes\"", "\"Ja\"", "\"Oui\"" })
        {
            Assert.DoesNotContain(caption, driver, StringComparison.Ordinal);
        }
    }

    /// <summary>Where the repository is, from a test binary under bin/.</summary>
    /// <returns>The root.</returns>
    private static string RepositoryRoot()
    {
        var at = new DirectoryInfo(AppContext.BaseDirectory);
        while (at is not null && !File.Exists(Path.Combine(at.FullName, "FreeWilly.slnx")))
        {
            at = at.Parent;
        }

        Assert.True(at is not null, "the repository root was not found above the test binaries");
        return at!.FullName;
    }
}
