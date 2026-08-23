using System.Text.RegularExpressions;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// The one thing about the list window that can be wrong without any code being wrong.
/// </summary>
/// <remarks>
/// The captions live in a Grid of their own and the cells live in a DataTemplate, and the two carry
/// separate copies of the same column definitions. They drifted apart once already — a search and
/// replace matched the header's indentation and not the template's — and the result was every
/// heading sitting over the wrong cell and the action buttons clipped at the right edge. No test
/// that constructs a row can see that; reading the markup can.
///
/// <para>It moved with the pages it checks (DD35). Each list is its own file now, so the rule is
/// applied per page rather than to one window that happened to hold all three — which is also what
/// makes a fourth list safe: it is checked by the same rule with no edit here.</para>
/// </remarks>
public sealed class MainWindowLayoutTests
{
    private static string RepositoryFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeWilly.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "the repository root was not found above the test binaries");
        return Path.Combine(directory!.FullName, relative);
    }

    [Fact]
    public void A_page_projects_each_container_once_per_refresh()
    {
        // DD110, and it is a guard rather than a measurement because the cost was never the point.
        // DD107 needed each container's project label for the prune and reached for
        // `ContainerRow.From` to get it, three lines above the loop that built the same rows for
        // real — so every row was made twice on every engine event, and this window redraws on every
        // engine event by design (DD70).
        //
        // Asserted on the source like the machine-read guard on the agent surface, and for its
        // reason: the second call compiles, passes, and looks from the outside exactly like the
        // first one was for something else.
        // Comments stripped first, or the guard counts the paragraph explaining itself — and a
        // guard a comment can trip is one the next author satisfies by rewording rather than by
        // fixing anything.
        var page = string.Join(
            '\n',
            File.ReadAllLines(RepositoryFile("src/FreeWilly.Tray/Ui/Pages/ContainersPage.xaml.cs"))
                .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        var projections = Regex.Matches(page, @"ContainerRow\.From\b").Count;

        Assert.True(
            projections == 1,
            $"ContainersPage calls ContainerRow.From {projections} times; one refresh is one "
            + "projection, and anything else needing a row's fields reads them off the list it made");
    }

    /// <summary>Every page that draws a list, found rather than listed.</summary>
    private static IEnumerable<string> Pages() =>
        Directory.EnumerateFiles(
            RepositoryFile("src/FreeWilly.Tray/Ui/Pages"), "*Page.xaml", SearchOption.TopDirectoryOnly);

    /// <summary>
    /// The pages that are a list, which is what the column rules are about.
    /// </summary>
    /// <remarks>
    /// Every page was a list until DD83 gave About a destination of its own. Its grid captions
    /// nothing and has no header to match, so pairing its column blocks would fail a rule it is not
    /// under — the rule is that a list's header sits on the same columns as its rows.
    /// </remarks>
    private static IEnumerable<string> ListPages() =>
        Pages().Where(page => File.ReadAllText(page).Contains("<ListView ", StringComparison.Ordinal));

    private static List<List<string>> ColumnBlocks(string page)
    {
        var xaml = File.ReadAllText(page);
        return
        [
            .. Regex.Matches(
                    xaml,
                    @"<Grid\.ColumnDefinitions>(.*?)</Grid\.ColumnDefinitions>",
                    RegexOptions.Singleline)
                .Select(block => Regex
                    .Matches(block.Groups[1].Value, @"Width=""([^""]+)""")
                    .Select(width => width.Groups[1].Value)
                    .ToList()),
        ];
    }

    [Fact]
    public void Every_header_is_laid_out_on_the_same_columns_as_the_rows_under_it()
    {
        // The file order is header, then the row template it captions. Pairing them this way is what
        // makes adding a page safe: a new pair is checked by the same rule with no edit here.
        var checkedAny = false;
        foreach (var page in ListPages())
        {
            var blocks = ColumnBlocks(page);
            Assert.True(
                blocks.Count % 2 == 0,
                $"{Path.GetFileName(page)} has {blocks.Count} column blocks: one is unpaired");

            for (var pair = 0; pair < blocks.Count; pair += 2)
            {
                Assert.Equal(blocks[pair], blocks[pair + 1]);
                checkedAny = true;
            }
        }

        Assert.True(checkedAny, "no page was checked, so this guard proved nothing");
    }

    [Fact]
    public void The_actions_column_fits_the_three_controls_a_row_now_shows()
    {
        // It was 320 for five buttons — Logs, Shell, Stop, Restart, Remove — and at 236 the last one
        // was clipped to a sliver. DD36 moved three of them behind the overflow, so the column carries
        // Logs, one verb and the ⋯ button: measured at about 160, and the number here is what stops it
        // being shrunk to where the last one clips again.
        var widths = ColumnBlocks(
            Pages().Single(p => Path.GetFileName(p) == "ContainersPage.xaml"))[0];

        Assert.True(
            int.TryParse(widths[^1], out var actions),
            $"the actions column should be a fixed width, not '{widths[^1]}'");
        Assert.True(actions >= 170, $"the actions column is {actions}, too narrow for three controls");
        Assert.True(
            actions <= 220,
            $"the actions column is {actions}: three controls do not need what five did, and the "
            + "space belongs to the columns that are read.");
    }

    [Fact]
    public void Every_destination_is_in_the_strip_and_the_containers_one_is_first()
    {
        var xaml = File.ReadAllText(RepositoryFile("src/FreeWilly.Tray/Ui/MainWindow.xaml"));
        var destinations = Regex.Matches(xaml, @"Tag=""([^""]+)"" Checked=""Destination_Checked""")
            .Select(match => match.Groups[1].Value)
            .ToList();

        // Containers first because it is what the window is opened for, and it is the one built with
        // the window rather than on first visit. The rest of the list is not fixed here: DD83 added
        // About, and a guard that has to be edited every time a destination lands is a guard that
        // says nothing about the one thing that matters.
        Assert.Equal("Containers", destinations[0]);
        Assert.Equal(destinations.Count, destinations.Distinct(StringComparer.Ordinal).Count());

        // And there is a page behind each one: a strip entry with nothing to show would navigate to
        // an empty host and look like a window that failed to load.
        foreach (var destination in destinations)
        {
            Assert.Contains(
                Pages(),
                page => Path.GetFileName(page) == destination + "Page.xaml");
        }
    }

    [Fact]
    public void Every_destination_in_the_strip_is_a_case_the_switch_answers()
    {
        // DD170. `Show` decides with a switch on an exact string and its `default` draws the
        // containers list, so a destination the switch does not name is one that silently shows the
        // wrong page under a strip that says otherwise — which is what a capture then photographs
        // and reports success about. Containers is excluded because it *is* the default.
        var xaml = File.ReadAllText(RepositoryFile("src/FreeWilly.Tray/Ui/MainWindow.xaml"));
        var code = File.ReadAllText(RepositoryFile("src/FreeWilly.Tray/Ui/MainWindow.xaml.cs"));

        var destinations = Regex.Matches(xaml, @"Tag=""([^""]+)"" Checked=""Destination_Checked""")
            .Select(match => match.Groups[1].Value)
            .Where(destination => !string.Equals(destination, "Containers", StringComparison.Ordinal));

        foreach (var destination in destinations)
        {
            Assert.Contains($"case \"{destination}\":", code, StringComparison.Ordinal);
        }
    }
}
