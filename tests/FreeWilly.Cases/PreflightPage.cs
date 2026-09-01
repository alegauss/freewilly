using System.Diagnostics;
using System.Text;

using Winwright.Asserting;
using Winwright.Capturing;
using Winwright.Verdicts;

using Xunit;

namespace FreeWilly.Cases;

/// <summary>
/// DD145, migrated under WW87: measuring a wizard page instead of reading it.
/// <para>
/// Four tasks built a page in Pascal — DD121's uninstall form, DD123's tasks page, DD131's blocked
/// install page, DD132's button on it — and every one was checked by reading the script. The
/// failures that misses are the ones it had already produced: a caption assigned before its width
/// wrapped at column zero; a page that rendered correctly above a screenful of blank space; and a
/// Copy button standing nine pixels below the box it belongs to, because an edit sizes itself to its
/// font and a button does not. Each was found by running an installer, which is the most expensive
/// place to find anything.
/// </para>
/// <para>
/// What is this project's own is everything above the dump: the page has no accessibility tree, so
/// it is taken out of <c>build\installer.iss</c> between its markers, wrapped in a Setup that shows
/// it under a named state, and asked to report every control's rectangle. The <c>[Code]</c> block is
/// the input either way, so this compiles the same text the installer ships and cannot drift from it.
/// </para>
/// <para>
/// What is <em>not</em> this project's own, and is why <c>scripts\page-probe.ps1</c> is gone: the
/// checks. It had its own loops for a control that measures nothing, one that starts off the top or
/// the left, one that ends past the surface, and two that overlap — which are the four faults
/// <see cref="Layout" /> reads off a dump, against the parent the depth says each element has rather
/// than against a rectangle typed here. Nothing in this file states a coordinate.
/// </para>
/// <para>
/// It gains a reading the script did not have. DD145's own note names the page that rendered above a
/// screenful of blank space, and nothing in the script could see it: every control was inside the
/// surface and none overlapped, which is what that page looks like. <see cref="LayoutReading.FillsAtLeast" />
/// is the question the four faults cannot ask.
/// </para>
/// <para>
/// Measured on the same machine, on the same five states, the day the script went: 152.3 s for
/// <c>scripts\page-probe.ps1</c> and 4 s for this. The 354 lines it removes are most of the
/// difference in reading and none of it in time — that one is <see cref="Render" />, which waits
/// for what the page wrote rather than for a Setup that never exits.
/// </para>
/// <para>
/// The two agree about what they saw, which is what says nothing was lost: 1102x660 surface, and
/// 8, 8, 8, 4 and 3 controls showing across the five states, both ways round.
/// </para>
/// <para>
/// What it does NOT prove is that the installer as a whole compiles — that is the separate step
/// DD102 put in check.yml, and neither replaces the other.
/// </para>
/// </summary>
public sealed class PreflightPage
{
    /// <summary>
    /// How much of the surface's height has to be drawn on.
    /// <para>
    /// Measured on this page rather than chosen. Four of its five states draw to the last row of a
    /// 1102x660 surface; <c>cleared</c> fills 92.6% and leaves 49 rows blank, because it is the one
    /// state that hides the footer as well as the command box, the Copy button, the link and the
    /// button that elevates. Half is well below the thinnest of those and well above the failure it
    /// is for — the page DD145 names rendered in the top third and left the rest empty.
    /// </para>
    /// <para>
    /// A floor and not the measurement: a fraction set at 92% would go red the day somebody adds a
    /// row to the footer, which is the page being edited rather than the page being wrong.
    /// </para>
    /// </summary>
    private const double Fills = 0.5;

    /// <summary>What the page is between in <c>build\installer.iss</c>.</summary>
    private const string Opens = "// >>> page-probe";

    /// <summary>And where it ends.</summary>
    private const string Closes = "// <<< page-probe";

    /// <summary>
    /// One shape the page can take, and what it should say in it.
    /// <para>
    /// One per shape, because the defects this catches are per state: a control hidden in one state
    /// and not repositioned in another is exactly the kind of thing reading cannot see.
    /// </para>
    /// </summary>
    /// <param name="Name">What the state is called, and what a failure is reported under.</param>
    /// <param name="Arguments">What the probe is told, in the Setup parameters the page reads.</param>
    /// <param name="Heading">What the heading should say.</param>
    /// <param name="TurnOn">What the button that elevates should say, where it is on the page.</param>
    /// <param name="Next">Whether the wizard's Next button should be enabled.</param>
    /// <param name="Shown">Every control the page has to be showing.</param>
    /// <param name="Hidden">Every control it has to be hiding, which is never merely emptying.</param>
    private sealed record State(
        string Name,
        string[] Arguments,
        string Heading,
        string Next,
        IReadOnlyList<string> Shown,
        string TurnOn = "",
        IReadOnlyList<string>? Hidden = null);

    private static readonly string[] Everything =
        ["heading", "memo", "commandbox", "copy", "link", "turnon", "again", "footer"];

    private static readonly State[] States =
    [
        new(
            "wsl2-blocked",
            ["/wsl2=yes", "/command=wsl.exe --install --no-distribution"],
            "Windows needs one feature turned on first",
            "no",
            Everything,
            TurnOn: "Turn it on for me"),
        new(
            "wsl2-refused",
            ["/wsl2=yes", "/refused=yes", "/command=wsl.exe --install --no-distribution"],
            "Windows needs one feature turned on first",
            "no",
            Everything,
            TurnOn: "Turn it on for me"),
        new(
            "feature-on",
            ["/wsl2=yes", "/on=yes", "/pending=yes", "/command=wsl.exe --install --no-distribution"],
            "Windows needs to restart to finish turning it on",
            "no",
            Everything,
            TurnOn: "Restart now"),

        // No command and not the WSL2 row, so the box, the Copy button, the link and the button that
        // elevates all have to be gone rather than merely empty.
        new(
            "other-blocker",
            ["/wsl2=no"],
            "This machine cannot host the container engine yet",
            "no",
            ["heading", "memo", "again", "footer"],
            Hidden: ["commandbox", "copy", "link", "turnon"]),

        // Reachable only through Check again, and the whole point of it is that Next comes back.
        new(
            "cleared",
            ["/clear=yes", "/wsl2=yes", "/command=wsl.exe --install --no-distribution"],
            "Nothing blocks an install any more",
            "yes",
            ["heading", "memo", "again"]),
    ];

    [Fact]
    public void The_preflight_page_renders_in_every_state_it_has()
    {
        // Refused rather than skipped. This harness compiles a real Setup, and a test that quietly
        // stood down when the compiler was missing would be a guard nobody could tell was absent —
        // which is the whole reason it runs after the step that puts ISCC on the PATH.
        var iscc = Iscc()
            ?? throw new InvalidOperationException(
                "ISCC.exe was not found. This harness compiles a real Setup; install Inno Setup 6.");

        var work = Path.Combine(Path.GetTempPath(), $"freewilly-page-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);

        try
        {
            var probe = Compile(iscc, work);
            var problems = new List<string>();

            foreach (var state in States)
                Measure(probe, work, state, problems);

            Assert.True(
                problems.Count == 0,
                $"{problems.Count} problem(s) on the preflight page:{Environment.NewLine}  "
                    + string.Join($"{Environment.NewLine}  ", problems));
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (Exception left) when (left is IOException or UnauthorizedAccessException)
            {
                // Disk, and never a stale answer: the next run builds its own directory under a new
                // guid. Worth saying and not worth failing the page over.
                Console.WriteLine($"the work directory under {work} could not be removed: {left.Message}");
            }
        }
    }

    /// <summary>Render one state, read what it drew, and add whatever is wrong to the list.</summary>
    /// <param name="probe">The Setup this compiled.</param>
    /// <param name="work">Where the dumps go.</param>
    /// <param name="state">The state to render.</param>
    /// <param name="problems">Where a finding is added, named by its state.</param>
    private static void Measure(string probe, string work, State state, List<string> problems)
    {
        var dump = Path.Combine(work, $"{state.Name}.geometry.txt");
        var facts = Path.Combine(work, $"{state.Name}.facts.txt");

        Render(probe, [$"/out={dump}", $"/facts={facts}", .. state.Arguments], dump, facts);

        if (!File.Exists(dump))
        {
            problems.Add($"{state.Name}: the page never rendered, so nothing could be measured");
            return;
        }

        var said = Facts(facts);
        var geometry = GeometryDump.Read(dump);

        // --- what the page says ------------------------------------------------------------
        Against(problems, state.Name, said, "heading-says", state.Heading, "the heading");
        Against(problems, state.Name, said, "next-enabled", state.Next, "Next enabled");
        if (state.TurnOn.Length > 0)
            Against(problems, state.Name, said, "turnon-says", state.TurnOn, "the button that elevates");

        // --- which controls are there -------------------------------------------------------
        foreach (var name in state.Shown)
            Showing(problems, state.Name, geometry, name, wanted: true);

        foreach (var name in state.Hidden ?? [])
            Showing(problems, state.Name, geometry, name, wanted: false);

        // --- and where they are -------------------------------------------------------------
        var reading = Layout.Of(geometry);

        // Printed on a pass and not only on a failure. What a state filled and how many rows it left
        // blank are the numbers that would have to move before the fraction below is worth changing,
        // and a run nobody reads until it goes red is a run that never reports them.
        Console.WriteLine($"{state.Name}: {reading.Sentence()}");
        Console.WriteLine($"{state.Name}: {reading.FillsAtLeast(0).Detail}");

        if (geometry.Unreadable > 0)
            problems.Add($"{state.Name}: {geometry.Unreadable} line(s) of the dump did not parse");

        if (!reading.Held)
            problems.Add($"{state.Name}: {reading.Sentence()}");

        // Not merely "did not fail": a reading nobody could take is Unchecked, and treating that as
        // a pass is the one thing this whole framework exists to refuse.
        var filled = reading.FillsAtLeast(Fills, $"{state.Name} fills its surface");
        if (filled.Outcome != AssertionOutcome.Passed)
            problems.Add($"{state.Name}: {filled.Detail}");
    }

    /// <summary>One fact the page reported against what this state asks for.</summary>
    /// <param name="problems">Where a finding is added.</param>
    /// <param name="state">Which state, so a finding names it.</param>
    /// <param name="said">What the page reported.</param>
    /// <param name="key">The fact to read.</param>
    /// <param name="wanted">What it should be.</param>
    /// <param name="called">What to call it in the sentence.</param>
    private static void Against(
        List<string> problems,
        string state,
        IReadOnlyDictionary<string, string> said,
        string key,
        string wanted,
        string called)
    {
        if (!said.TryGetValue(key, out var read))
        {
            problems.Add($"{state}: the page reported no '{key}', so {called} was never read");
            return;
        }

        if (!string.Equals(read, wanted, StringComparison.Ordinal))
            problems.Add($"{state}: {called} says '{read}', not '{wanted}'");
    }

    /// <summary>
    /// Whether the page is showing one control, read off the visibility the dump carries.
    /// <para>
    /// A claim about what the page draws and not about a rectangle: an element that reserves its
    /// space and draws nothing is what "hidden" means, and every geometry check leaves one alone.
    /// So a control the page forgot to hide would pass every fault and fail here.
    /// </para>
    /// </summary>
    /// <param name="problems">Where a finding is added.</param>
    /// <param name="state">Which state, so a finding names it.</param>
    /// <param name="geometry">What the page dumped.</param>
    /// <param name="name">The control.</param>
    /// <param name="wanted">Whether the page should be showing it.</param>
    private static void Showing(
        List<string> problems, string state, ReadGeometry geometry, string name, bool wanted)
    {
        var found = geometry.Named(name);
        if (found.Count != 1)
        {
            problems.Add($"{state}: the dump holds {found.Count} element(s) called '{name}', and it should hold one");
            return;
        }

        if (found[0].IsShown != wanted)
        {
            problems.Add(wanted
                ? $"{state}: {name} is {found[0].Visibility.ToString().ToLowerInvariant()} and should be shown"
                : $"{state}: {name} is shown and should be hidden");
        }
    }

    /// <summary>The key-and-value lines the page wrote, which are what it says rather than where.</summary>
    /// <param name="path">The facts file.</param>
    private static IReadOnlyDictionary<string, string> Facts(string path)
    {
        var said = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
            return said;

        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            var at = line.IndexOf('=', StringComparison.Ordinal);
            if (at > 0)
                said[line[..at]] = line[(at + 1)..];
        }

        return said;
    }

    /// <summary>
    /// Compile the page into a Setup of its own, and answer where the Setup is.
    /// <para>
    /// The variables the page reads are declared in the head rather than sliced out, because they
    /// live in the installer's own var block among things this Setup has no use for. A variable
    /// added there and used by the page arrives here as a compile error, which is loud — the failure
    /// mode to avoid is a harness that silently stops covering something.
    /// </para>
    /// </summary>
    /// <param name="iscc">The Inno Setup compiler.</param>
    /// <param name="work">Where to build.</param>
    private static string Compile(string iscc, string work)
    {
        var installer = Path.Combine(Tree.Root(), "build", "installer.iss");
        var lines = File.ReadAllLines(installer);

        var open = Array.FindIndex(lines, one => one.Contains(Opens, StringComparison.Ordinal));
        var close = Array.FindIndex(lines, one => one.Contains(Closes, StringComparison.Ordinal));
        Assert.True(
            open >= 0 && close > open,
            $"{installer} no longer carries a page-probe block between its markers");

        // The page anchors itself after the tasks page, which this Setup does not have. wpWelcome is
        // the only substitution made to the shipped text, and it is one token.
        var page = System.Text.RegularExpressions.Regex.Replace(
            string.Join('\n', lines[(open + 1)..close]),
            @"(?m)^\s*TasksPage\.ID,\s*$",
            "    wpWelcome,",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromSeconds(5));

        var script = Path.Combine(work, "page-probe.iss");
        File.WriteAllText(script, Head + page + Tail, new UTF8Encoding(false));

        var compiled = Run(iscc, [script], work);
        Assert.True(compiled.Code == 0, $"ISCC refused the page probe (exit {compiled.Code}){Environment.NewLine}{compiled.Said}");

        var probe = Path.Combine(work, "page-probe.exe");
        Assert.True(File.Exists(probe), $"ISCC exited 0 and produced no {probe}");
        return probe;
    }

    /// <summary>How long any one of these gets before it is taken down.</summary>
    private const int DeadlineMs = 30_000;

    /// <summary>
    /// Show one state and wait for what it wrote, not for the process.
    /// <para>
    /// Setup does not exit on <c>WizardForm.Close</c>: closing a wizard puts up its own "exit
    /// Setup?" confirmation, and nobody is here to answer one. The script this replaces waited the
    /// whole thirty seconds every time and then killed whatever was standing, which is five
    /// dialogs and two and a half minutes for a page that had already reported itself.
    /// </para>
    /// <para>
    /// The dump is written before the close and the facts after it, so the facts file arriving is
    /// what says the dump is whole — an ordering rather than a settling delay, which is the
    /// difference between waiting for a fact and hoping.
    /// </para>
    /// </summary>
    /// <param name="probe">The Setup.</param>
    /// <param name="arguments">What to tell it.</param>
    /// <param name="dump">The geometry it writes first.</param>
    /// <param name="facts">And what it says, written after.</param>
    private static void Render(string probe, string[] arguments, string dump, string facts)
    {
        var start = new ProcessStartInfo(probe) { WorkingDirectory = Path.GetDirectoryName(probe)! };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using (var running = Process.Start(start) ?? throw new InvalidOperationException($"{probe} started nothing"))
        {
            var until = DateTime.UtcNow.AddMilliseconds(DeadlineMs);
            while (DateTime.UtcNow < until && !running.HasExited && !(File.Exists(dump) && File.Exists(facts)))
                Thread.Sleep(50);
        }

        // Whatever is still standing goes, including the confirmation nobody answered. By name and
        // not by the handle above, because Setup re-launches itself out of the temp directory as
        // page-probe.tmp and the process this started is not the one holding the window.
        foreach (var name in new[] { "page-probe", "page-probe.tmp" })
        {
            foreach (var straggler in Process.GetProcessesByName(name))
                using (straggler)
                {
                    try
                    {
                        straggler.Kill(entireProcessTree: true);
                    }
                    catch (Exception gone)
                        when (gone is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        // It left between the listing and the kill, which is this arm existing
                        // because it has already happened.
                    }
                }
        }
    }

    /// <summary>Run something that exits on its own, and answer what it said and what it exited with.</summary>
    /// <param name="executable">What to run.</param>
    /// <param name="arguments">What to tell it.</param>
    /// <param name="within">The working directory.</param>
    private static (int Code, string Said) Run(string executable, string[] arguments, string within)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = within,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var running = Process.Start(start)
            ?? throw new InvalidOperationException($"{executable} started nothing");

        // Read on the process's own time and wait on this run's. ReadToEnd blocks until the pipe
        // closes, which is until the process exits — so a deadline written after one is a deadline
        // that cannot fire, which is why this helper is only ever handed something that ends.
        var said = new StringBuilder();
        running.OutputDataReceived += (_, line) => Append(said, line.Data);
        running.ErrorDataReceived += (_, line) => Append(said, line.Data);
        running.BeginOutputReadLine();
        running.BeginErrorReadLine();

        if (!running.WaitForExit(DeadlineMs))
            running.Kill(entireProcessTree: true);

        running.WaitForExit();
        return (running.ExitCode, said.ToString());
    }

    /// <summary>One line of what a process said, where it said anything.</summary>
    /// <param name="said">Everything so far.</param>
    /// <param name="line">The line, or null at the end of the stream.</param>
    private static void Append(StringBuilder said, string? line)
    {
        if (line is null)
            return;

        lock (said)
            said.AppendLine(line);
    }

    /// <summary>
    /// The Inno Setup compiler, or null where there is none. The same three places
    /// <c>build\build-installer.cmd</c> looks, in the same order: a fourth opinion about where Inno
    /// Setup lives is how two of them come to disagree.
    /// </summary>
    private static string? Iscc() => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Inno Setup 6", "ISCC.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Inno Setup 6", "ISCC.exe"),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Inno Setup 6",
            "ISCC.exe"),
    }.FirstOrDefault(File.Exists);

    /// <summary>The Setup around the page: what it needs declared, and nothing it does not.</summary>
    private const string Head = """
[Setup]
AppName=PageProbe
AppVersion=1.0
DefaultDirName={tmp}\PageProbe
PrivilegesRequired=lowest
OutputDir=.
OutputBaseFilename=page-probe
Uninstallable=no
CreateAppDir=no
WizardStyle=modern
DisableWelcomePage=yes

[Code]
var
  PreflightPage: TWizardPage;
  PreflightHeading, PreflightFooter: TNewStaticText;
  PreflightMemo: TNewMemo;
  PreflightCommandBox: TNewEdit;
  PreflightCopy, PreflightAgain, PreflightTurnOn: TNewButton;
  PreflightLink: TNewLinkLabel;
  PreflightAsked, PreflightClear, PreflightWsl2: Boolean;
  PreflightRefused, PreflightFeatureOn, PreflightRestartWanted: Boolean;
  PreflightSaid, PreflightCommand, PreflightReport: string;

// The page never calls this; the buttons do, and this Setup presses nothing.
function Preflight: Boolean;
begin
  Result := PreflightClear;
end;


""";

    /// <summary>
    /// What renders the page once and writes down what it drew.
    /// <para>
    /// The dump is winwright's geometry format and not this project's: depth, kind, name, left, top,
    /// right, bottom, visibility and who put it there, separated by tabs, in the pixels the surface
    /// lays out in. Every control is depth 1 under the surface at depth 0, which is what lets a flat
    /// file carry the tree — half the checks worth making are about a child against the thing
    /// containing it, and the depth is the only thing that says which that is.
    /// </para>
    /// </summary>
    private const string Tail = """

function YesNo(V: Boolean): string;
begin
  if V then Result := 'yes' else Result := 'no';
end;

procedure Note(var Report: TArrayOfString; const Kind, Name: string; C: TControl);
var
  N: Integer;
  T, Seen: string;
begin
  T := Chr(9);
  // Hidden and never Collapsed: a control this page hides keeps its rectangle, which is exactly
  // what the format's Hidden means and exactly what a check has to leave alone.
  if C.Visible then Seen := 'Visible' else Seen := 'Hidden';
  N := GetArrayLength(Report);
  SetArrayLength(Report, N + 1);
  Report[N] := '1' + T + Kind + T + Name + T
    + IntToStr(C.Left) + T + IntToStr(C.Top) + T
    + IntToStr(C.Left + C.Width) + T + IntToStr(C.Top + C.Height) + T
    + Seen + T + 'Application';
end;

procedure Dump;
var
  Report, Facts: TArrayOfString;
  T: string;
begin
  T := Chr(9);
  SetArrayLength(Report, 1);
  Report[0] := '0' + T + 'TWizardPage' + T + 'surface' + T + '0' + T + '0' + T
    + IntToStr(PreflightPage.SurfaceWidth) + T + IntToStr(PreflightPage.SurfaceHeight) + T
    + 'Visible' + T + 'Application';

  Note(Report, 'TNewStaticText', 'heading', PreflightHeading);
  Note(Report, 'TNewMemo', 'memo', PreflightMemo);
  Note(Report, 'TNewEdit', 'commandbox', PreflightCommandBox);
  Note(Report, 'TNewButton', 'copy', PreflightCopy);
  Note(Report, 'TNewLinkLabel', 'link', PreflightLink);
  Note(Report, 'TNewButton', 'turnon', PreflightTurnOn);
  Note(Report, 'TNewButton', 'again', PreflightAgain);
  Note(Report, 'TNewStaticText', 'footer', PreflightFooter);

  // Two files. What the page SAYS is not geometry, and a reader of the dump counts every line it
  // cannot parse - so three sentences of prose in there would arrive as three unreadable elements.
  SetArrayLength(Facts, 3);
  Facts[0] := 'next-enabled=' + YesNo(WizardForm.NextButton.Enabled);
  Facts[1] := 'heading-says=' + PreflightHeading.Caption;
  Facts[2] := 'turnon-says=' + PreflightTurnOn.Caption;

  SaveStringsToUTF8File(ExpandConstant('{param:out}'), Report, False);
  SaveStringsToUTF8File(ExpandConstant('{param:facts}'), Facts, False);
end;

procedure InitializeWizard;
begin
  PreflightSaid := ExpandConstant('{param:said|a row  some detail}');
  PreflightCommand := ExpandConstant('{param:command}');
  PreflightWsl2 := ExpandConstant('{param:wsl2|no}') = 'yes';
  PreflightRefused := ExpandConstant('{param:refused|no}') = 'yes';
  PreflightFeatureOn := ExpandConstant('{param:on|no}') = 'yes';
  PreflightRestartWanted := ExpandConstant('{param:pending|no}') = 'yes';
  PreflightClear := ExpandConstant('{param:clear|no}') = 'yes';
  PreflightReport := 'C:\Users\someone\AppData\Local\Temp\preflight.txt';
  PreflightAsked := True;

  BuildPreflightPage;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID <> PreflightPage.ID then
  begin
    WizardForm.NextButton.Enabled := True;
    Exit;
  end;

  ShowTheVerdict;
  Dump;
  WizardForm.Close;
end;
""";
}
