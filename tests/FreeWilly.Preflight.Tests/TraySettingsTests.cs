using FreeWilly.Core.Engine;
using FreeWilly.Core.Settings;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// What the user has decided about how this tool behaves, and the file that remembers it (DD135,
/// DD154).
/// </summary>
public sealed class TraySettingsTests
{
    /// <summary>A path in a directory of this test's own, removed when it is done.</summary>
    private sealed class Scratch : IDisposable
    {
        private readonly string _directory =
            Path.Combine(Path.GetTempPath(), $"freewilly-settings-{Guid.NewGuid():N}");

        internal string File => Path.Combine(_directory, "settings.json");

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, recursive: true);
                }
            }
            catch (IOException)
            {
                // A temp directory that outlives the run costs nothing worth failing a suite over.
            }
        }
    }

    [Fact]
    public void An_install_nobody_has_changed_anything_on_starts_the_engine()
    {
        // The decision the user made, asserted rather than left to a literal somewhere. Shipping
        // this off would make the setting invisible: nobody goes looking in a menu for a box that
        // matches what already happens.
        using var scratch = new Scratch();

        Assert.True(TraySettings.Read(scratch.File).StartWithTheTray);
        Assert.True(TraySettings.EngineShipsOn);
    }

    [Fact]
    public void The_file_holds_one_setting_and_the_release_check_is_not_it()
    {
        // DD171 reversing DD154. The check shipped off behind a tick here, and a check nobody turns
        // on is a check nobody has — so it is not a setting any more and there is nothing in this
        // file that can stop it. Asserted on the shape of the record rather than on a value, because
        // the defect this guards is somebody reintroducing the switch.
        Assert.DoesNotContain(
            typeof(TraySettings).GetProperties(),
            property => property.Name.Contains("Release", StringComparison.Ordinal));
    }

    [Fact]
    public void Turning_it_off_survives_the_next_launch()
    {
        // The whole reason it is a setting and not a hard-coded start. If this does not round-trip,
        // the non-goal it sits beside has been quietly deleted rather than kept.
        using var scratch = new Scratch();
        new TraySettings { StartWithTheTray = false }.Write(scratch.File);

        Assert.False(TraySettings.Read(scratch.File).StartWithTheTray);
    }

    [Fact]
    public void Turning_it_back_on_survives_too()
    {
        using var scratch = new Scratch();
        new TraySettings { StartWithTheTray = false }.Write(scratch.File);
        new TraySettings { StartWithTheTray = true }.Write(scratch.File);

        Assert.True(TraySettings.Read(scratch.File).StartWithTheTray);
    }

    [Fact]
    public void A_file_that_cannot_be_read_answers_with_the_defaults_rather_than_throwing()
    {
        // A preference file truncated by a power cut is not a reason to refuse to start an engine,
        // and this runs in a constructor where throwing takes the tray icon with it.
        using var scratch = new Scratch();
        Directory.CreateDirectory(Path.GetDirectoryName(scratch.File)!);
        File.WriteAllText(scratch.File, "{ this is not json");

        Assert.True(TraySettings.Read(scratch.File).StartWithTheTray);
    }

    [Fact]
    public void A_file_written_before_DD154_keeps_its_answer()
    {
        // What is actually on the machines this ships to: settings.json holding the one property
        // DD135 wrote. Renaming the type must not have renamed the property, or every install that
        // had turned the engine start off would silently get it back.
        using var scratch = new Scratch();
        Directory.CreateDirectory(Path.GetDirectoryName(scratch.File)!);
        File.WriteAllText(scratch.File, "{\n  \"StartWithTheTray\": false\n}");

        Assert.False(TraySettings.Read(scratch.File).StartWithTheTray);
    }

    [Fact]
    public void A_file_written_while_the_release_check_was_a_setting_is_read_without_it()
    {
        // The upgrade path DD171 has to survive: every install that ever opened the menu wrote
        // CheckForReleases, most of them false. An unknown member is not an error, so the property
        // is ignored — a copy that had it off must not stay off now that there is no off.
        using var scratch = new Scratch();
        Directory.CreateDirectory(Path.GetDirectoryName(scratch.File)!);
        File.WriteAllText(
            scratch.File,
            "{\n  \"StartWithTheTray\": false,\n  \"CheckForReleases\": false\n}");

        Assert.False(TraySettings.Read(scratch.File).StartWithTheTray);
    }

    [Fact]
    public void Writing_where_nothing_exists_yet_creates_the_directory()
    {
        // The settings file lives beside the install and is not created by EnginePaths.Create, so
        // the first write is also the first time its folder may need to exist.
        using var scratch = new Scratch();

        new TraySettings { StartWithTheTray = false }.Write(scratch.File);

        Assert.True(File.Exists(scratch.File));
    }

    [Fact]
    public void It_is_kept_apart_from_the_window_and_from_the_run_key()
    {
        // Three different questions that all sound like "does it start": where the window was, what
        // logon runs, and what opening the tray does. DD97 already paid for conflating two of them.
        var paths = new EnginePaths();

        Assert.NotEqual(paths.WindowState, paths.Settings);
        Assert.EndsWith("settings.json", paths.Settings, StringComparison.Ordinal);
    }
}
