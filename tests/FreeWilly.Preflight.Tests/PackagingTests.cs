using System.Reflection;
using FreeWilly.Core.Licensing;
using FreeWilly.Tray.Cli;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// The chain a release travels along (DD14): one version, stated once, and one file name.
/// </summary>
/// <remarks>
/// Every link here is a string that has to agree with a string somewhere else, and none of them
/// fails at compile time. The version is set in Directory.Build.props, compiled into the assembly,
/// read back off the published .exe by build\installer.iss, and shown by Windows in Add/Remove
/// Programs; the file name is set by AssemblyName, typed by a person, and written into the
/// installer's shortcuts. A break anywhere along either is found by running an installer, which is
/// the most expensive place to find anything.
/// </remarks>
public sealed class PackagingTests
{
    private static readonly Assembly Shipped = typeof(CommandLine).Assembly;

    [Fact]
    public void The_name_the_help_prints_is_the_name_the_build_produces()
    {
        // AssemblyName in FreeWilly.Tray.csproj, against the name every message and the installer
        // spell out. The assembly is a .dll here and an .exe when published, so compare the stem.
        var built = System.IO.Path.GetFileNameWithoutExtension(Shipped.Location);

        Assert.Equal(
            built,
            System.IO.Path.GetFileNameWithoutExtension(CommandLine.ExecutableName));
        Assert.Equal(".exe", System.IO.Path.GetExtension(CommandLine.ExecutableName));
    }

    [Fact]
    public void The_product_version_carries_no_commit_suffix()
    {
        // IncludeSourceRevisionInInformationalVersion=false in Directory.Build.props. Without it the
        // SDK appends "+<commit>", installer.iss reads that whole string out of the .exe with
        // GetStringFileInfo, and the version Windows shows in Add/Remove Programs has a git hash in
        // it. BuildVersion trims the suffix for its own display, so it cannot catch this.
        var informational = Shipped
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        Assert.NotNull(informational);
        Assert.DoesNotContain('+', informational);
    }

    [Fact]
    public void The_version_is_one_a_person_can_read_out_of_a_bug_report()
    {
        var current = BuildVersion.Current;

        Assert.NotEqual("0.0.0", current);
        Assert.True(
            Version.TryParse(current, out var parsed),
            $"{current} should parse as a version");
        Assert.Equal(current, parsed!.ToString());
    }

    /// <summary>The repository, found by walking up from the test binaries.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "FreeWilly.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "the repository root was not found above the test binaries");
        return directory!.FullName;
    }

    /// <summary>The installer script, read as text — the only way to assert on Inno's decisions.</summary>
    private static string InstallerScript() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "build", "installer.iss"));

    /// <summary>
    /// The installer script as Inno reads it: one string per directive, with the backslash
    /// continuations joined.
    /// </summary>
    /// <remarks>
    /// A directive here is wrapped across lines for width, so a test matching physical lines asserts
    /// on half a directive and passes or fails on where somebody put a line break. Joining first is
    /// what lets a single assertion say "this entry names this value AND is gated on this task".
    /// </remarks>
    private static IEnumerable<string> InstallerDirectives()
    {
        var joined = new List<string>();
        var pending = new System.Text.StringBuilder();

        foreach (var raw in InstallerScript().Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.TrimEnd().EndsWith('\\'))
            {
                pending.Append(line.TrimEnd().TrimEnd('\\')).Append(' ');
                continue;
            }

            joined.Add(pending.Append(line.Trim()).ToString());
            pending.Clear();
        }

        return joined;
    }

    /// <summary>A workflow, read as text.</summary>
    private static string Workflow(string name) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", name));

    /// <summary>The script that names, sums and creates a release, read as text.</summary>
    private static string PublishScript() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "build", "publish-release.ps1"));

    /// <summary>The one line in <paramref name="text"/> that runs the publish script.</summary>
    /// <remarks>
    /// Not the whole file, and not a line that only talks about it. Both callers explain
    /// <c>-Draft</c> in a comment and one prints the command as a recovery hint, so an assertion
    /// reading the file whole would be satisfied by the prose while the call did the opposite.
    /// </remarks>
    private static string Invocation(string text) =>
        text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith("echo", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("REM", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith('#'))
            .Single(line =>
                line.Contains("publish-release.ps1", StringComparison.Ordinal)
                && line.Contains("-Version", StringComparison.Ordinal));

    [Fact]
    public void Exactly_one_thing_creates_a_release_and_both_paths_run_it()
    {
        // DD161. The release used to be release.yml's, built from a clean checkout on a tag push;
        // it is now `update-release.cmd <version> upload`'s, built on the machine that can run the
        // installer. What must not happen is both: a tag push that still fired the workflow would
        // race the local `gh release create` for the same tag, and whichever lost would be a red
        // run on every release.
        var workflow = Workflow("release.yml");
        var command = File.ReadAllText(Path.Combine(RepositoryRoot(), "build", "update-release.cmd"));

        Assert.DoesNotContain("tags:", workflow, StringComparison.Ordinal);
        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);

        // And one implementation of the three decisions, not two. These would otherwise be the
        // same rename, the same digest and the same `gh release create` written twice, and the copy
        // nobody runs is the copy that drifts — which is the defect DD157 and DD159 spent two
        // tasks on, one release surface away.
        Assert.Contains("publish-release.ps1", command, StringComparison.Ordinal);
        Assert.Contains("publish-release.ps1", workflow, StringComparison.Ordinal);

        // The workflow keeps the draft, because that is the thing it is for: a person presses
        // Publish after the installer has been run somewhere with nested virtualization. The local
        // path does not, because the maintainer running it has the installer in front of them.
        //
        // Asserted on the line that invokes the script rather than on the whole file — both of
        // these explain the flag in a comment, and a test that read those would pass on the
        // explanation while the call did the opposite.
        Assert.Contains("-Draft", Invocation(workflow), StringComparison.Ordinal);
        Assert.DoesNotContain("-Draft", Invocation(command), StringComparison.Ordinal);
    }

    [Fact]
    public void The_asset_a_release_publishes_is_the_asset_the_updater_looks_for()
    {
        // DD152 named the installer after its version and DD154 made an installed copy find it by
        // matching that pattern — so the release script and the release check have to agree about
        // one file name, in PowerShell and in C#, with nothing failing at compile time.
        //
        // Asserted through ReleaseCheck's own parser rather than against a regex retyped here: what
        // matters is not that the names look alike but that the thing which reads a release accepts
        // the thing which writes one.
        Assert.Contains(
            "dist\\FreeWilly-Setup-$Version.exe", PublishScript(), StringComparison.Ordinal);

        var asset = $"FreeWilly-Setup-{BuildVersion.Current}.exe";
        var release = $$"""
            {
              "tag_name": "v{{BuildVersion.Current}}",
              "assets": [
                { "name": "{{asset}}", "browser_download_url": "https://example.invalid/i" },
                { "name": "{{FreeWilly.Core.Releases.ReleaseCheck.SumsAssetName}}",
                  "browser_download_url": "https://example.invalid/s" }
              ]
            }
            """;

        var found = FreeWilly.Core.Releases.ReleaseCheck.In(release);

        Assert.NotNull(found);
        Assert.Equal(asset, found.InstallerName);
    }

    [Fact]
    public void The_sums_line_a_release_writes_is_one_an_installed_copy_reads()
    {
        // The other half of the same join, and the one that fails silently: DD154 fetches
        // SHA256SUMS.txt before the installer and refuses to run anything it cannot find a digest
        // for. So a release whose sums file this parser does not understand is not a broken update
        // — it is an update every installed copy declines, with nobody to tell.
        var script = PublishScript();

        // Two spaces between the digest and the name, which is what sha256sum -c reads.
        Assert.Contains("'{0}  {1}' -f", script, StringComparison.Ordinal);

        // ASCII, because a UTF-8 BOM makes the first line fail to parse for sha256sum.
        Assert.Contains("-Encoding ascii", script, StringComparison.Ordinal);

        // And the shape itself, through the parser that has to read it. Written the way the script
        // writes it — Out-File on Windows PowerShell ends the line with CRLF — because the carriage
        // return is exactly the character a naive split would leave on the file name.
        var digest = new string('4', 64);
        var asset = $"FreeWilly-Setup-{BuildVersion.Current}.exe";

        Assert.Equal(
            digest,
            FreeWilly.Core.Releases.ReleaseSums.DigestFor($"{digest}  {asset}\r\n", asset));
    }

    [Fact]
    public void A_self_update_relaunches_and_an_unattended_install_still_does_not()
    {
        // DD154. Two files that have to agree about one switch and cannot fail at compile time: the
        // tray runs the installer with ReleaseUpdate.SilentArguments, and installer.iss decides
        // whether to start the app again by reading a parameter out of that string.
        //
        // What breaks if they drift is invisible until somebody presses Install: the tray closes to
        // be replaced and never comes back, which reads as an update that broke the product. And the
        // flag it is an exception to matters as much — the interactive entry stays skipifsilent so an
        // install pushed to a machine does not put a tray icon in somebody's session.
        var script = InstallerScript();
        var directives = InstallerDirectives().ToList();

        var switchName = FreeWilly.Core.Releases.ReleaseUpdate.SilentArguments
            .Split(' ')
            .Single(argument => argument.StartsWith("/RELAUNCH", StringComparison.Ordinal))
            .Split('=');

        Assert.Equal("/RELAUNCH", switchName[0]);
        Assert.Contains(
            $"{{param:{switchName[0].TrimStart('/')}|no}}", script, StringComparison.Ordinal);
        Assert.Contains($"'{switchName[1]}'", script, StringComparison.Ordinal);

        // Silent only, so the two [Run] entries cannot both fire and start two trays.
        Assert.Contains("WizardSilent", script, StringComparison.Ordinal);

        var run = directives
            .Where(line => line.StartsWith("Filename: \"{app}\\{#MyAppExeName}\"", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, run.Count);
        Assert.Contains(run, line => line.Contains("skipifsilent", StringComparison.Ordinal));
        Assert.Contains(run, line => line.Contains("Check: RelaunchAsked", StringComparison.Ordinal));

        // The relaunch is per-user, because the whole install is: an elevated Setup starting the tray
        // would leave it, its pipe and its window owned by the wrong account.
        Assert.Contains(
            "runasoriginaluser",
            run.Single(line => line.Contains("RelaunchAsked", StringComparison.Ordinal)),
            StringComparison.Ordinal);
    }

    /// <summary>The committed agent configuration, parsed.</summary>
    private static System.Text.Json.JsonElement Settings() =>
        System.Text.Json.JsonDocument
            .Parse(File.ReadAllBytes(Path.Combine(RepositoryRoot(), ".claude", "settings.json")))
            .RootElement;

    [Fact]
    public void The_committed_settings_still_grant_what_this_project_cannot_work_without()
    {
        // DD115. This file is committed on purpose — it grants this project's tools and wires the
        // roadkeep guard, so a clone works rather than prompting on every call — and it is rewritten
        // by whatever session happens to be open. Twice in one session fifteen entries vanished from
        // `allow` with nothing said, and the second time the deletion rode into a commit about
        // something else, because run-commit.cmd stages everything by design.
        //
        // A floor and not the whole list: pinning every entry would fail the build on a legitimate
        // addition, which is how a guard gets deleted. These are the ones whose loss costs
        // something — the tools this project's own loop is built on, and the two that let the
        // vendored roadkeep be called at all.
        var allowed = Settings()
            .GetProperty("permissions").GetProperty("allow")
            .EnumerateArray()
            .Select(entry => entry.GetString())
            .ToHashSet(StringComparer.Ordinal);

        string[] floor =
        [
            "Bash", "PowerShell", "Read", "Edit", "Write", "Glob", "Grep", "Skill", "Task",
            "mcp__roadkeep",
            "Bash(python .roadkeep/scripts/roadkeep.py:*)",
        ];

        foreach (var entry in floor)
        {
            Assert.True(
                allowed.Contains(entry),
                $".claude/settings.json no longer grants {entry}: a clone starts asking permission "
                + "for a tool this project had already granted, and nothing else would say so");
        }
    }

    [Fact]
    public void The_committed_settings_still_wire_the_guard_that_owns_the_governed_files()
    {
        // The consequential half of DD115, and the reason a floor over `allow` alone would not be
        // enough. roadkeep denies a hand-edit of ROADMAP, CHANGELOG and IMPROVEMENTS through this
        // hook; without it those files become ordinary text and the whole discipline this repository
        // runs on stops being enforced — silently, because nothing fails when a guard is absent.
        var hooks = Settings().GetProperty("hooks");

        foreach (var stage in new[] { "SessionStart", "PreToolUse", "Stop" })
        {
            var wired = hooks.GetProperty(stage).EnumerateArray()
                .SelectMany(group => group.GetProperty("hooks").EnumerateArray())
                .Select(hook => hook.GetProperty("command").GetString() ?? "")
                .ToList();

            Assert.True(
                wired.Exists(command => command.Contains("roadkeep-launch.py", StringComparison.Ordinal)
                    && command.Contains("guard", StringComparison.Ordinal)),
                $"no roadkeep guard is wired on {stage}, so a hand-edit of the governed files is "
                + "no longer refused");
        }

        // And the engine that guard reaches is the vendored one, which is the whole point of
        // carrying a copy: a checkout beside this repository is somebody else's working tree, and
        // mid-refactor it does not import at all.
        Assert.Equal(
            "${CLAUDE_PROJECT_DIR}/.roadkeep",
            Settings().GetProperty("env").GetProperty("ROADKEEP_HOME").GetString());
    }

    [Fact]
    public void The_engine_the_hooks_reach_is_the_one_this_repository_vendors()
    {
        // DD116, and it is asserted by running the launcher's own resolution rather than by reading
        // it: the defect was invisible in the file. `settings.json` sets ROADKEEP_HOME to
        // `${CLAUDE_PROJECT_DIR}/.roadkeep`, the harness passes env values through verbatim, and the
        // first candidate therefore never existed — so every hook fell through to a sibling
        // checkout, which is the neighbour's working tree that vendoring exists to stop depending
        // on. Earlier in this project's history that tree was mid-refactor and did not import, and
        // the guard denying hand-edits of the governed files was running a traceback.
        var root = RepositoryRoot();

        // Nothing below can be asked where the engine was never vendored, and that is every CI
        // runner by construction rather than by accident: `.gitignore` excludes `.roadkeep/` and
        // `scripts/install_roadkeep.py` says why in its own docstring — reproduced by that script,
        // never committed. So a checkout has the launcher but not the thing it resolves to, and this
        // test asserted a claim that cannot hold there. It was red on every push from DD116 until
        // here, and the release it eventually blocked is what made somebody read it: `dotnet test`
        // gates release.yml, so an unmakeable assertion held back an installer.
        //
        // A skip would say this better and xUnit v2 has no supported way to ask for one — the
        // reasoning is written out in SingleTrayTests.RequireTheTraySlot, and it lands the other way
        // there. The difference is what the missing precondition means. A running tray is a machine
        // state a developer fixes in two seconds, so failing is the remedy; an unvendored engine is
        // the documented state of every clone before `install-roadkeep.cmd` is run, so failing on it
        // is a gate that reports the design of the repository as a defect. The claim is therefore
        // conditional, and the condition is named rather than implied.
        var vendored = Path.Combine(root, ".roadkeep", "scripts", "roadkeep.py");
        if (!File.Exists(vendored))
        {
            return;
        }

        var launcher = Path.Combine(root, ".claude", "hooks", "roadkeep-launch.py");
        Assert.True(File.Exists(launcher), $"{launcher} is missing, so no hook can reach an engine");

        var probe = Path.Combine(Path.GetTempPath(), $"roadkeep-resolve-{Guid.NewGuid():N}.py");
        File.WriteAllText(
            probe,
            $"""
            import importlib.util
            spec = importlib.util.spec_from_file_location("rl", r"{launcher}")
            m = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(m)
            print(m._resolve())
            """);

        try
        {
            var run = new System.Diagnostics.ProcessStartInfo("python", probe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = root,
            };

            // Exactly what settings.json hands the hooks, so the test asks the question the harness
            // asks and not an easier one.
            run.Environment["CLAUDE_PROJECT_DIR"] = root;
            run.Environment["ROADKEEP_HOME"] = "${CLAUDE_PROJECT_DIR}/.roadkeep";

            // Importing the launcher makes Python write a .pyc beside it, inside the repository, on
            // every run of this test — and run-commit.cmd stages everything by design, so the first
            // one landed in a commit. A test must not leave anything behind in the tree it reads.
            run.Environment["PYTHONDONTWRITEBYTECODE"] = "1";

            using var process = System.Diagnostics.Process.Start(run);
            Assert.NotNull(process);
            var resolved = process!.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(30000);

            Assert.Equal(
                Path.Combine(root, ".roadkeep", "scripts", "roadkeep.py"),
                resolved);
        }
        finally
        {
            File.Delete(probe);
        }
    }

    /// <summary>Every script and workflow that could invoke the build, found rather than listed.</summary>
    private static IEnumerable<string> BuildScripts() =>
        new[] { "build", ".github/workflows", "scripts" }
            .Select(folder => Path.Combine(RepositoryRoot(), folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            .Where(file => Path.GetExtension(file) is ".cmd" or ".yml" or ".ps1");

    [Fact]
    public void No_build_command_names_the_folder_a_project_is_in()
    {
        // DD109. An interrupted WPF build leaves a <name>_<random>_wpftmp.csproj beside the project,
        // and MSBuild answers a folder holding two projects with MSB1050 — before it has evaluated
        // anything, so no target inside the project can prevent it. A command that has already
        // chosen its project cannot raise it at all, which closes the class rather than the case.
        //
        // Derived and never listed: a fourth script added next year is under this rule with no edit
        // here, which is the property the help-text guard was written for after two verbs went
        // missing unnoticed.
        var checkedAny = false;
        foreach (var script in BuildScripts())
        {
            foreach (var line in File.ReadAllLines(script))
            {
                if (!line.Contains("dotnet publish", StringComparison.Ordinal)
                    && !line.Contains("dotnet build", StringComparison.Ordinal))
                {
                    continue;
                }

                // A bare `dotnet build` takes the solution, which names its projects and cannot be
                // ambiguous. Only a line that points somewhere under src\ is making the choice.
                if (!line.Contains("src", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.True(
                    line.Contains(".csproj", StringComparison.Ordinal),
                    $"{Path.GetFileName(script)} names a folder rather than a project file, so a "
                    + $"leftover _wpftmp.csproj stops it with MSB1050:{Environment.NewLine}{line.Trim()}");
                checkedAny = true;
            }
        }

        Assert.True(checkedAny, "no build command was checked, so this guard proved nothing");
    }

    [Fact]
    public void The_stale_artefact_is_cleared_before_the_publish_it_would_stop()
    {
        // The sharper half of DD109. build.cmd already deleted the artefact — afterwards, to tidy
        // up — so the one script that owns the cleanup failed on exactly the run the cleanup exists
        // for and worked the second time, which is what made it read as flaky rather than as a rule.
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "build", "build.cmd"));

        var cleared = script.IndexOf("_wpftmp.csproj", StringComparison.Ordinal);
        var published = script.IndexOf("dotnet publish", StringComparison.Ordinal);

        Assert.True(cleared >= 0, "build.cmd no longer clears the stale _wpftmp.csproj at all");
        Assert.True(published >= 0, "build.cmd no longer publishes");
        Assert.True(
            cleared < published,
            "build.cmd clears the stale _wpftmp.csproj only after the publish that it stops");
    }

    [Fact]
    public void The_ordinary_path_compiles_the_installer_script_and_not_only_the_release()
    {
        // DD102, and the guard is over the workflow because that is where the defect was. Every
        // other test in this class asserts over the script as *text* — that a line says
        // `ValueType: none`, that an AppId is spelled a certain way — which proves the file says
        // what the author meant and can say nothing about whether Inno accepts it. DD97 shipped an
        // Inno construct on exactly that evidence, and the first reader of the file was the release,
        // by which point the tag was pushed.
        //
        // Asserted here rather than trusted to a reviewer, and for DD88's reason: the site build was
        // broken for 21 commits because its workflow was `workflow_dispatch` only, and nothing
        // noticed. A step deleted from check.yml would restore exactly that state, silently.
        var check = Workflow("check.yml");

        Assert.Contains("./.github/actions/inno-setup", check, StringComparison.Ordinal);
        Assert.Contains(@"build\installer.iss", check, StringComparison.Ordinal);

        // On every push and pull request, with no path filter in front of it. A conditional reader
        // of this file is the failure being repaired, not a cheaper version of the fix.
        Assert.Contains("on: [push, pull_request]", check, StringComparison.Ordinal);
        Assert.DoesNotContain("paths:", check, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_workflows_find_the_compiler_the_same_way()
    {
        // One definition, because there are two callers and they are one rule — the same reasoning
        // as Wsl.HasDriveLetter. Two inline probes would be two chances to disagree about where Inno
        // Setup lives, and the one that disagreed would be found by a release.
        foreach (var name in new[] { "check.yml", "release.yml" })
        {
            var workflow = Workflow(name);
            Assert.Contains("./.github/actions/inno-setup", workflow, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Inno Setup 6\\ISCC.exe",
                workflow);
        }
    }

    [Fact]
    public void The_AppId_is_pinned_here_so_a_future_tidy_cannot_move_it()
    {
        // Inno identifies a product by AppId and by nothing else, so from the first release onward
        // this is an identity rather than a spelling. Change it and a machine carrying the published
        // build ends up with two entries in Add/Remove Programs, two Run values and two roots — and
        // the old uninstaller then offers to delete the engine root the new install is using.
        //
        // DD86 changed it once, which was only free because nothing has been released. The literal
        // is repeated here rather than read from the script so that the next change has to be
        // deliberate in two files: this test is the record that it is not a name to sweep.
        Assert.Contains(
            "AppId={{6B0E4D2A-9C77-4A31-8F5E-FREEWILLY0001}",
            InstallerScript(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_tray_and_the_engine_start_at_logon_under_names_that_differ()
    {
        // DD97, and the assertion is inverted from what stood here. It used to require the two to
        // be spelled the SAME, reasoning that one value keeps the window and the uninstaller from
        // touching two entries. That holds for one feature and is exactly backwards for two: the
        // installer's checkbox writes `--tray` and `--autostart on` wrote `--run` over it, so
        // whichever ran last won. Ticking the box then turning the engine on stopped the tray
        // appearing, and turning the engine off deleted the box's entry outright.
        var script = InstallerScript();

        Assert.DoesNotContain("LegacyRunValue", script, StringComparison.Ordinal);
        Assert.NotEqual(
            Core.Engine.Autostart.TrayEntryName,
            Core.Engine.Autostart.EngineEntryName);

        // Each name is spelled in both files, which is the drift nothing else would notice.
        Assert.Contains(
            $"ValueName: \"{Core.Engine.Autostart.EngineEntryName}\"",
            script,
            StringComparison.Ordinal);
        Assert.Equal("FreeWilly", Core.Engine.Autostart.TrayEntryName);
    }

    [Fact]
    public void The_uninstaller_takes_the_engine_entry_without_the_installer_ever_writing_it()
    {
        // Two settings mean two values, and only one of them is the installer's to create. Leaving
        // the other behind is a Run entry pointing at an executable that has been deleted — the
        // exact state DD57 had to clean up once already.
        //
        // `ValueType: none` is what makes it delete-on-uninstall and touch nothing on install.
        // Writing it properly would turn the engine autostart on for everyone, and off-by-default
        // is not a preference here: it is the complaint about Docker Desktop that this answers.
        var engineValue = Assert.Single(
            InstallerScript().Split('\n'),
            line => line.Contains(
                $"ValueName: \"{Core.Engine.Autostart.EngineEntryName}\"", StringComparison.Ordinal));

        Assert.Contains("ValueType: none", engineValue, StringComparison.Ordinal);
        Assert.DoesNotContain("ValueData:", engineValue, StringComparison.Ordinal);
    }

    [Fact]
    public void The_installer_points_DOCKER_CONFIG_at_the_plugins_it_placed()
    {
        // DD124. DD73 and DD74 place Compose and Buildx under {app}\cli-plugins, and the CLI finds a
        // plugin in $DOCKER_CONFIG\cli-plugins and nowhere else — so the placement without this
        // variable is two executables nothing looks at. Measured before the fix: `ant` driving the
        // docker on PATH answered `unknown flag: --build`, because with no plugin found `compose`
        // is not a subcommand and the flag is parsed at the CLI's root.
        //
        // The variable name comes from the code rather than being retyped, so a rename cannot leave
        // the installer writing one name and DockerConfigEntry reading another.
        var entry = Assert.Single(
            InstallerDirectives(),
            line => line.Contains(
                $"ValueName: \"{Core.Engine.DockerConfigEntry.Variable}\"", StringComparison.Ordinal));

        // {app} is the root, which is what EnginePaths.ConfigDirectory resolves to — the CLI wants
        // the directory holding cli-plugins, not cli-plugins itself.
        Assert.Contains("ValueData: \"{app}\"", entry, StringComparison.Ordinal);

        // Gated on the same checkbox as the PATH entry, and that is the rule and not a convenience:
        // DOCKER_CONFIG is read by every docker.exe a shell runs and carries config.json, the
        // contexts and the login credentials with it, so it is only this install's to point when
        // this install owns the docker command. DockerConfigEntry.OwnsTheDockerCommand is the same
        // rule at tray startup, and these two must not disagree.
        Assert.Contains("Tasks: pathentry", entry, StringComparison.Ordinal);

        // A value naming a directory the uninstaller deleted is worse than no value: the next
        // docker.exe on that machine would read a config directory that is not there.
        Assert.Contains("uninsdeletevalue", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void The_installer_registers_this_install_as_the_build_link_handler()
    {
        // DD126. Buildx prints a docker-desktop:// link at the end of every build and nothing
        // configures that away, so on a machine without Docker Desktop the link opens an error.
        // Registering the scheme is what makes it resolve to the record the daemon already kept.
        var directives = InstallerDirectives().ToList();

        // HKCU and not HKCR: this is one user's choice about their own machine, needs no elevation,
        // and a Docker Desktop installed afterwards overwrites it — which is the right way round.
        var command = Assert.Single(
            directives,
            line => line.Contains(
                @"Subkey: ""Software\Classes\docker-desktop\shell\open\command""",
                StringComparison.Ordinal));

        Assert.Contains("Root: HKCU", command, StringComparison.Ordinal);

        // The verb comes from the code rather than being retyped, so a rename cannot leave the
        // registry pointing at a verb this executable no longer has.
        Assert.Contains(
            Core.Builds.BuildAddress.Scheme,
            command,
            StringComparison.Ordinal);
        Assert.Contains(
            $"{CommandLine.OpenBuildVerb} \"\"%1\"\"",
            command,
            StringComparison.Ordinal);

        // A URL is one argument. Unquoted, it splits on the first space and the handler is handed
        // half a link.
        Assert.Contains(@"""""%1""""", command, StringComparison.Ordinal);

        // Declared a URL scheme, or the shell does not treat it as one at all.
        Assert.Contains(
            directives,
            line => line.Contains(@"ValueName: ""URL Protocol""", StringComparison.Ordinal));

        // And it leaves when this does: a handler pointing at a deleted executable is worse than
        // none, because the link then fails in a way that names this product.
        Assert.Contains(
            directives,
            line => line.Contains(@"Subkey: ""Software\Classes\docker-desktop""", StringComparison.Ordinal)
                    && line.Contains("uninsdeletekey", StringComparison.Ordinal));
    }

    [Fact]
    public void Turning_the_engine_autostart_off_leaves_the_trays_entry_alone()
    {
        // The failure itself, driven rather than restated. `Disable` removes rather than blanks,
        // deliberately, so before DD97 turning the engine off silently undid a box somebody had
        // ticked in the installer — and turning it ON overwrote it with `--run`, so the tray
        // stopped appearing at logon.
        //
        // A scratch key, never the real Run: this writes and deletes, and the machine running the
        // suite must not be what it experiments on.
        var scratch = $@"Software\FreeWilly\Tests\{Guid.NewGuid():N}";
        try
        {
            var tray = new Core.Engine.Autostart(
                @"""C:\x\FreeWilly.exe"" --tray", Core.Engine.Autostart.TrayEntryName, scratch);
            var engine = new Core.Engine.Autostart(
                @"""C:\x\FreeWilly.exe"" --run", Core.Engine.Autostart.EngineEntryName, scratch);

            tray.Enable();
            engine.Enable();

            // Both, at once, which is the state that was unreachable before: one value could only
            // hold one of the two commands.
            Assert.True(tray.Enabled);
            Assert.True(engine.Enabled);
            Assert.EndsWith("--tray", tray.Registered!, StringComparison.Ordinal);
            Assert.EndsWith("--run", engine.Registered!, StringComparison.Ordinal);

            engine.Disable();

            Assert.False(engine.Enabled);
            Assert.True(tray.Enabled, "turning the engine off took the tray's entry with it");
            Assert.EndsWith("--tray", tray.Registered!, StringComparison.Ordinal);
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(scratch, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void Start_with_windows_asks_for_the_tray_alone()
    {
        // DD80 inverted the default: a bare launch opens the window, so the Run value has to say
        // it wants the tray on its own or every logon puts a window in somebody's face. The two
        // spellings are in two files and nothing else would notice them drifting apart.
        //
        // Found by its key rather than by "the line mentioning the executable": DD126 registers a
        // protocol handler that also names it, so that predicate stopped identifying anything. The
        // Run key is what this test is about, and it is the thing to match on.
        //
        // Not the whole literal: Inno doubles its own quotes, so an exact match asserts that
        // escaping as much as it asserts the argument, and the argument is the claim.
        var runValue = Assert.Single(
            InstallerDirectives(),
            line => line.Contains(
                        @"Subkey: ""Software\Microsoft\Windows\CurrentVersion\Run""",
                        StringComparison.Ordinal)
                    && line.Contains("ValueData:", StringComparison.Ordinal));

        Assert.Contains(
            $"{Tray.Cli.CommandLine.TrayOnlyVerb}\"",
            runValue,
            StringComparison.Ordinal);

        // And it really is silence: the flag the script writes has to be the one that means it.
        var route = Tray.Cli.CommandLine.Of([Tray.Cli.CommandLine.TrayOnlyVerb]);
        Assert.Equal(Tray.Cli.Surface.Tray, route.Surface);
        Assert.False(route.OpenWindow);
    }

    [Fact]
    public void The_install_provisions_the_engine_and_only_where_the_preflight_cleared_it()
    {
        // DD119. Setup used to lay down one executable, print a preflight and stop, which left the
        // engine to a verb the wizard never named: `docker` was not a command, Start engine had no
        // distribution to boot, and the install looked finished.
        //
        // The order is the whole assertion. `Preflight` answers whether this machine can host an
        // engine, and the provision is behind that answer — unpacking one onto a machine that cannot
        // host it is the failure the preflight exists to prevent, and it costs a quarter of a
        // gigabyte to discover the hard way.
        //
        // DD130 moved where that answer is settled — before the first file rather than after the
        // last — and left this property standing, which is why the assertion is unchanged apart
        // from the name. `Preflight` remembers its verdict, so the guard here reads the same answer
        // the wizard page showed and cannot come to a different one.
        var script = InstallerScript();

        Assert.Contains("if not Preflight then", script, StringComparison.Ordinal);
        Assert.Contains("WizardIsTaskSelected('engine')", script, StringComparison.Ordinal);
        Assert.Contains("'--provision'", script, StringComparison.Ordinal);

        var guard = script.IndexOf("if not Preflight then", StringComparison.Ordinal);
        var provision = script.IndexOf(
            "WizardIsTaskSelected('engine')", StringComparison.Ordinal);
        Assert.True(guard < provision, "the provision is no longer behind the preflight");
    }

    // ---- DD130: the preflight runs before the install, not after it ----------------------------

    /// <summary>The preflight half of the script, which is the only part these read.</summary>
    private static string PreflightSection()
    {
        var script = InstallerScript();
        var start = script.IndexOf(
            "// The preflight, run before the first file rather than after the last",
            StringComparison.Ordinal);

        Assert.True(start > 0, "the preflight section was renamed and these guards now read nothing");

        // Up to the wiring and no further. The section after that one provisions the engine, and it
        // does ask a question — about a download that failed on a machine the preflight cleared,
        // which is a different subject and a legitimate dialog.
        var end = script.IndexOf(
            "// The wizard, wired to the two pages above", StringComparison.Ordinal);

        Assert.True(end > start, "the preflight section no longer ends where these guards think");
        return script[start..end];
    }

    [Fact]
    public void The_machine_is_read_off_a_copy_that_no_install_had_to_happen_to_place()
    {
        // DD130, and the defect is an order rather than a missing check. The preflight ran at
        // ssPostInstall — after every file had been written, the PATH entry made and the Run value
        // set — so a laptop without WSL2 received a complete installation of a tool whose one job it
        // cannot do, plus a message box explaining that. Skipping the engine download was all the
        // late check still bought.
        //
        // What makes the check runnable early is where the executable comes from: {tmp}, extracted
        // from the archive, rather than {app}, which only holds an executable once the copy this is
        // supposed to gate has already happened.
        var script = InstallerScript();

        Assert.Contains(
            "ExtractTemporaryFile('{#MyAppExeName}')", script, StringComparison.Ordinal);
        Assert.Contains(@"{tmp}\{#MyAppExeName}", script, StringComparison.Ordinal);

        // The old caller, named so it cannot come back: running the installed copy is exactly the
        // dependency on a completed install that this removes.
        Assert.DoesNotContain(
            @"{app}\{#MyAppExeName}') + '"" --preflight",
            script,
            StringComparison.Ordinal);

        // ExtractTemporaryFile reads the archive and only the archive, and only an entry carrying
        // `dontcopy` is in it to be read. MergeDuplicateFiles is what keeps that free: measured on
        // the real payload, the second entry costs 1,607 bytes rather than a second 75 MB copy.
        Assert.Contains(
            InstallerDirectives(),
            line => line.Contains("Flags: dontcopy", StringComparison.Ordinal)
                    && line.Contains("{#MyAppExeName}", StringComparison.Ordinal));
    }

    [Fact]
    public void The_verdict_is_the_products_own_and_is_read_in_the_form_that_survives_the_trip()
    {
        // Still the product answering, which is the half DD130 did not change: a second opinion
        // written in Pascal would be two reports about one machine for a reader to reconcile.
        //
        // `--json` and not the text form, and the reason is the encoding rather than the structure.
        // The report a person reads is UTF-8 with em dashes and arrows in it, Inno reads a file as
        // ANSI, and the old code carried a comment explaining why the report could therefore only be
        // pointed at and never shown. System.Text.Json escapes everything outside ASCII as \uXXXX,
        // so the JSON form arrives intact — which is what lets the page say what the report says.
        var section = PreflightSection();

        Assert.Contains("--preflight --json", section, StringComparison.Ordinal);

        // Which the script therefore has to decode, and the decode has to build a whole character.
        // Measured: Inno's Chr truncates to a byte, so — came out as #$14 and the em dash was
        // simply missing from the page.
        Assert.Contains("Utf8Decode(", section, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blocked_machine_cannot_be_clicked_past_and_is_never_asked_a_question()
    {
        // The verdict decides whether Next is available at all. A warning somebody dismisses is the
        // old behaviour wearing a page: it ends with the files written either way.
        var script = InstallerScript();
        var section = PreflightSection();

        // Bound to the verdict rather than pinned off: DD131 added a re-check on this page, so the
        // button has to come back when the machine does. The claim is the same either way — nothing
        // past this page happens while something blocks.
        Assert.Contains(
            "WizardForm.NextButton.Enabled := PreflightClear;", script, StringComparison.Ordinal);

        // The page stands between the tasks page and wpReady, which is the last place a wizard can
        // stop before Setup commits to anything.
        Assert.Contains("CreateCustomPage(", section, StringComparison.Ordinal);
        Assert.Contains("TasksPage.ID,", section, StringComparison.Ordinal);

        // And no dialog anywhere in it. The old check asked "Open it now?" from ssPostInstall, which
        // is a question about a machine that had already been changed; the page carries what that
        // message box pointed at.
        Assert.DoesNotContain("MsgBox", section, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unattended_install_stops_on_the_same_verdict_without_growing_a_modal()
    {
        // A silent install never sees the page, so the stop has to exist somewhere it does reach.
        // PrepareToInstall is raised before Setup copies its first file, and returning a message
        // aborts with exit code 7 — "Preparing to Install determined that Setup cannot proceed" —
        // which is a distinct answer a deployment can read. Measured on a probe installer: exit 7,
        // and nothing written.
        var script = InstallerScript();

        var prepare = script.IndexOf(
            "function PrepareToInstall(", StringComparison.Ordinal);
        Assert.True(prepare > 0, "nothing gates the install for a caller that never sees a page");

        var body = script[prepare..];
        body = body[..body.IndexOf("\nend;", StringComparison.Ordinal)];

        Assert.Contains("if Preflight then", body, StringComparison.Ordinal);
        Assert.DoesNotContain("MsgBox", body, StringComparison.Ordinal);

        // And the report outlives Setup. {tmp} does not — Setup deletes it on the way out — and on
        // a fresh install stopped by this check there is no {app} either, because nothing has been
        // written yet. That is the point, so the fallback is the user's own TEMP.
        Assert.Contains("{%TEMP}", PreflightSection(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_install_that_went_through_keeps_the_reading_it_was_allowed_on()
    {
        // DD146, and it is a loose end DD130 left rather than a defect it introduced. Moving the
        // read in front of the copy moved the write with it, so the report only landed when
        // something blocked — and a successful install kept no record of what it was cleared on,
        // while the uninstall went on deleting a file nothing wrote.
        var script = InstallerScript();

        // Written in ssPostInstall, which is the first step at which {app} exists to write into.
        var post = script.IndexOf("if CurStep <> ssPostInstall then", StringComparison.Ordinal);
        Assert.True(post > 0, "the installer no longer has a post-install step to write from");

        var after = script[post..];
        Assert.Contains(
            @"KeepTheReport(ExpandConstant('{app}\preflight.txt'))",
            after,
            StringComparison.Ordinal);

        // Before the provision and not after it: this is the reading the wizard acted on, and the
        // provision is about to change the very row a re-read would differ on.
        var kept = after.IndexOf("KeepTheReport(", StringComparison.Ordinal);
        var provision = after.IndexOf("ProvisionEngine", StringComparison.Ordinal);
        Assert.True(kept < provision, "the report is kept after the provision has moved a row");

        // One writer for both paths, so the file a deployment reads and the file an install keeps
        // cannot come to say different things about the same machine.
        var writers = script.Split("procedure KeepTheReport(", StringSplitOptions.None).Length - 1;
        Assert.Equal(1, writers);

        // Every row rather than the blocking ones. What blocks is what the page is for; "was this
        // green when it was installed" is what the file is for, and the blockers alone cannot
        // answer it.
        Assert.Contains("PreflightRows", PreflightSection(), StringComparison.Ordinal);
    }

    // ---- DD131: the page a blocked install lands on --------------------------------------------

    [Fact]
    public void The_command_the_page_hands_over_is_the_one_the_remedy_already_named()
    {
        // DD131, and this is the seam that holds the two files together. The page finds the command
        // by the backticks the product writes it in — one source of truth, and the source is the row
        // that decided the remedy — so a remedy rephrased without them leaves the page with an empty
        // command box and nothing anywhere that would say so.
        //
        // The machine is the common one: wsl.exe is in System32 on every Windows, and the feature
        // behind it is not installed.
        var report = Core.Preflight.PreflightInspection.Run(FakeMachine.Healthy with
        {
            Wsl = new Core.Preflight.WslInstallation
            {
                CommandPresent = true,
                FeatureInstalled = false,
            },
        });

        var wsl2 = report[Core.Preflight.PreflightInspection.Rows.Wsl2];
        Assert.NotNull(wsl2);
        Assert.True(wsl2!.Blocks, "the machine this builds no longer blocks, so it proves nothing");

        var remedy = wsl2.Remedy;
        Assert.NotNull(remedy);

        var opened = remedy!.IndexOf('`');
        var closed = remedy.IndexOf('`', opened + 1);
        Assert.True(
            opened >= 0 && closed > opened,
            $"the WSL2 remedy no longer spells its command in backticks, so the installer's page "
            + $"has nothing to put in the box or on the clipboard:{Environment.NewLine}{remedy}");

        // And it is a command, not a sentence — the thing a Copy button is for.
        Assert.StartsWith("wsl", remedy[(opened + 1)..closed], StringComparison.Ordinal);

        // The page reads it by that rule and by no other.
        Assert.Contains("function Backticked(", PreflightSection(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_page_names_the_feature_in_plain_words_and_links_the_instructions()
    {
        // The whole complaint DD131 answers: a message box naming `wsl.exe --install
        // --no-distribution` is exactly right for a reader who already knows what WSL2 is, and is
        // the entire experience for a reader who does not.
        var section = PreflightSection();

        // Expanded, not assumed. The sentence has to say what the feature is before the steps say
        // what to do about it.
        Assert.Contains("Linux kernel", section, StringComparison.Ordinal);

        // Microsoft's own page, opened rather than printed: a URL on a wizard page is an address
        // nobody can click and most people will not retype.
        Assert.Contains(
            "https://learn.microsoft.com/en-us/windows/wsl/install",
            section,
            StringComparison.Ordinal);
        Assert.Contains("TNewLinkLabel.Create", section, StringComparison.Ordinal);

        // The row is named rather than guessed at from the wording, so a rephrased remedy cannot
        // quietly turn the WSL2 page back into the generic list of rows.
        Assert.Contains(
            $"Id = '{Core.Preflight.PreflightInspection.Rows.Wsl2}'",
            section.Replace("\"", "'", StringComparison.Ordinal),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Finding_out_whether_the_fix_worked_is_a_button_rather_than_a_reinstall()
    {
        // The button that matters most. Without it there is no way to learn whether turning the
        // feature on worked short of running Setup again — which is the loop DD131 exists to close.
        var section = PreflightSection();

        Assert.Contains("'Check again'", section, StringComparison.Ordinal);

        // Forgetting the last answer is the whole of the re-check: everything else already runs off
        // the one memoised function, so clearing the flag is what makes the second read a read.
        var again = section[section.IndexOf("procedure CheckAgain(", StringComparison.Ordinal)..];
        again = again[..again.IndexOf("\nend;", StringComparison.Ordinal)];
        Assert.Contains("PreflightAsked := False;", again, StringComparison.Ordinal);
        Assert.Contains("Preflight;", again, StringComparison.Ordinal);

        // And Next follows the verdict in both directions, which is what "releases Next the moment
        // the row turns green" means. Pinned to the variable rather than to False: a re-check that
        // cleared the machine and left the button dead would be the same dead end with more clicks.
        Assert.Contains(
            "WizardForm.NextButton.Enabled := PreflightClear;",
            section,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_copied_command_carries_no_newline_that_would_run_it_on_paste()
    {
        // Setup has no clipboard of its own, so this is clip.exe — and which shell builtin feeds it
        // is a safety property rather than a style choice. `echo` appends a newline, and a newline
        // on the clipboard turns a paste into the administrator terminal the page just told the
        // reader to open into a command that has already run before it could be read.
        var section = PreflightSection();
        var copy = section[section.IndexOf("procedure CopyTheCommand(", StringComparison.Ordinal)..];
        copy = copy[..copy.IndexOf("\nend;", StringComparison.Ordinal)];

        // The argument itself and not the whole procedure: the comment above it names `echo` in
        // order to say why this is not it, and a test that reads prose would fail on the sentence
        // explaining the very thing it is guarding.
        var argument = Assert.Single(
            copy.Split('\n'),
            line => line.Contains("'/C ", StringComparison.Ordinal));

        Assert.Contains("clip", argument, StringComparison.Ordinal);
        Assert.Contains("set /p=", argument, StringComparison.Ordinal);
        Assert.DoesNotContain("echo", argument, StringComparison.Ordinal);
    }

    // ---- DD132: Setup turns the feature on ------------------------------------------------------

    /// <summary>
    /// The preflight section with its prose removed, for a guard about what the script <em>does</em>.
    /// </summary>
    /// <remarks>
    /// The comments here explain the commands this file deliberately does not run, so a guard that
    /// read them would fail on the sentence describing the thing it exists to prevent.
    /// </remarks>
    private static string PreflightCode() =>
        string.Join(
            "\n",
            PreflightSection()
                .Split('\n')
                .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [Fact]
    public void The_elevation_is_bought_for_one_step_and_never_for_the_install()
    {
        // DD132. Docker Desktop can enable a Windows feature for free because it runs elevated from
        // its first dialog. This installer is PrivilegesRequired=lowest on purpose — the audience is
        // developers on managed laptops, and a UAC dialog at install time is where a large share of
        // them stop — so the elevation is bought per step instead.
        var script = InstallerScript();

        Assert.Contains("PrivilegesRequired=lowest", script, StringComparison.Ordinal);

        // Exactly one, and it is on the command the row named. A second would be a second prompt,
        // and the whole claim of this design is that turning the feature on costs one.
        var elevations = PreflightCode().Split("ShellExec('runas'", StringSplitOptions.None).Length - 1;
        Assert.Equal(1, elevations);
    }

    [Fact]
    public void Setup_never_spells_a_wsl_command_of_its_own()
    {
        // The sharp end of DD132, and the reason it is asserted as an absence. `--no-distribution`
        // does not exist on older WSL builds, and the generous repair — dropping the flag and
        // running `wsl --install` — installs Ubuntu on somebody's machine uninvited.
        //
        // So this file names no command at all: it runs the one the remedy spelled in backticks and
        // has no fallback to reach for. Measured against a real wsl.exe: an unknown flag is refused
        // with "Invalid command line argument" and nothing is installed, so a build too old for the
        // flag gets a visible failure and the steps it already had.
        var code = PreflightCode();

        Assert.DoesNotContain("--install", code, StringComparison.Ordinal);
        Assert.DoesNotContain("--no-distribution", code, StringComparison.Ordinal);

        // What it runs instead: the command the page already extracted, split into a program and
        // its arguments because ShellExec takes the two apart.
        Assert.Contains("SplitCommand(PreflightCommand", code, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refused_prompt_leaves_the_machine_and_the_page_exactly_as_they_were()
    {
        // An account that cannot elevate at all lands here too, and neither is an error: the steps
        // on the page are precisely what the button would have done.
        var code = PreflightCode();

        var turn = code[code.IndexOf("procedure TurnItOn;", StringComparison.Ordinal)..];
        turn = turn[..turn.IndexOf("\nend;", StringComparison.Ordinal)];

        Assert.Contains("if not ShellExec(", turn, StringComparison.Ordinal);
        Assert.Contains("PreflightRefused := True;", turn, StringComparison.Ordinal);

        // Nothing is claimed on that path. The two facts the page would otherwise state are set
        // after the refusal check and not before it.
        var refusal = turn.IndexOf("PreflightRefused := True;", StringComparison.Ordinal);
        var claimed = turn.IndexOf("PreflightFeatureOn := True;", StringComparison.Ordinal);
        Assert.True(
            claimed > refusal,
            "a refused elevation still leaves the page saying the feature was turned on");
    }

    [Fact]
    public void The_restart_is_asked_for_and_the_install_is_waiting_on_the_other_side()
    {
        // The feature needs a reboot before it is usable, so the run ends by asking for one rather
        // than leaving a Setup that has to be remembered.
        var code = PreflightCode();

        // RunOnce and not Run: it fires exactly once and deletes itself, so abandoning the install
        // after turning the feature on costs one wizard somebody can close.
        Assert.Contains(@"CurrentVersion\RunOnce", code, StringComparison.Ordinal);
        Assert.Contains("{srcexe}", code, StringComparison.Ordinal);

        // And the restart names itself and gives a moment's warning: this closes everything the
        // user has open, so the one thing it must not be is instant and unattributed.
        var restart = Assert.Single(
            code.Split('\n'),
            line => line.Contains("'/r /t ", StringComparison.Ordinal));
        Assert.Contains("/c \"", restart, StringComparison.Ordinal);
        Assert.DoesNotContain("/t 0", restart, StringComparison.Ordinal);
    }

    [Fact]
    public void The_engine_task_is_offered_ticked_so_a_default_install_has_an_engine()
    {
        // Unticking is the whole point of it being a task: a quarter of a gigabyte over somebody's
        // tethered connection is theirs to decline. Shipping it unticked is a different product —
        // one whose default install is the empty one DD119 was filed about.
        var task = Assert.Single(
            InstallerScript().Split('\n'),
            line => line.StartsWith("Name: \"engine\";", StringComparison.Ordinal));

        Assert.DoesNotContain("unchecked", task, StringComparison.Ordinal);
    }

    [Fact]
    public void The_bar_the_installer_draws_has_one_notch_for_every_step_the_provisioner_runs()
    {
        // The installer counts step lines to move its progress bar and needs a total to divide by.
        // A step added to ProvisioningStep without this number moving leaves a successful install
        // with a bar that stops short of the end, which is what a failure looks like.
        var declared = Assert.Single(
            InstallerScript().Split('\n'),
            line => line.TrimStart().StartsWith("ProvisioningSteps = ", StringComparison.Ordinal));

        Assert.Equal(
            Enum.GetValues<Core.Engine.ProvisioningStep>().Length,
            int.Parse(
                declared.Trim()["ProvisioningSteps = ".Length..].TrimEnd(';'),
                System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void The_verdict_the_installer_matches_is_the_one_the_verb_prints()
    {
        // A format string in C# and a Pos() call in Pascal, agreeing about six characters. Nothing
        // but this notices them drifting: the install would still succeed, and the bar would simply
        // never move — the least legible way for a download to look broken.
        var ok = Tray.Cli.EngineCommand.StepLine(
            new Core.Engine.StepResult(Core.Engine.ProvisioningStep.PlaceCli, true, "done"));
        var failed = Tray.Cli.EngineCommand.StepLine(
            new Core.Engine.StepResult(Core.Engine.ProvisioningStep.PlaceCli, false, "not done"));

        // Position 1 of the trimmed line, which is what the Pascal side matches on.
        Assert.StartsWith("[ok  ]", ok.Trim(), StringComparison.Ordinal);
        Assert.StartsWith("[FAIL]", failed.Trim(), StringComparison.Ordinal);

        var script = InstallerScript();
        Assert.Contains("Pos('[ok  ]', Line) = 1", script, StringComparison.Ordinal);
        Assert.Contains("Pos('[FAIL]', Line) = 1", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_uninstall_takes_back_every_file_the_provision_placed_under_the_root()
    {
        // Inno removes what Inno installed, and the provision writes four things it did not: the
        // CLI, the plugin directory, and the two reports. Left behind, they are what keeps {app} on
        // disk after an uninstall that took everything else — and they are this product's own files,
        // not anybody's data, so they go without being asked about.
        //
        // The question that remains is about images and volumes, and it is the only one.
        var script = InstallerScript();
        var removal = script.IndexOf("RemovePathEntry;", StringComparison.Ordinal);
        var question = script.IndexOf("if RemoveTheDistribution then", StringComparison.Ordinal);
        Assert.True(removal > 0 && question > removal);

        var unconditional = script[removal..question];
        foreach (var path in new[]
                 {
                     @"{app}\preflight.txt", @"{app}\provision.log", @"{app}\engine.log",
                     @"{app}\bin", @"{app}\cli", @"{app}\cli-plugins",
                 })
        {
            Assert.Contains($"'{path}'", unconditional, StringComparison.Ordinal);
        }
    }

    // ---- DD123: the tasks page is drawn rather than asked for ----------------------------------

    [Fact]
    public void The_page_this_script_draws_offers_exactly_the_tasks_the_script_declares()
    {
        // DD123. Setup's own Select Additional Tasks page draws each box narrower than the glyph it
        // holds — measured at 200% scaling on 4K, and reproduced with Inno Setup 6.7.3's factory
        // defaults and nothing of this script in them, so it is the control. The page is rebuilt out
        // of plain TNewCheckBox controls, which draw correctly at the same scaling.
        //
        // That leaves two lists of the same four things, and this is what holds them equal: a
        // checkbox whose caption drifts from its [Tasks] description promises one thing and does
        // another, and nothing else in the build would notice.
        var script = InstallerScript();
        var tasks = script[script.IndexOf("[Tasks]", StringComparison.Ordinal)..];
        tasks = tasks[..tasks.IndexOf("[Files]", StringComparison.Ordinal)];

        var page = script[script.IndexOf("procedure BuildTasksPage;", StringComparison.Ordinal)..];
        page = page[..page.IndexOf("function ChosenTasks:", StringComparison.Ordinal)];

        foreach (var (name, caption) in new[]
                 {
                     ("desktopicon", "{cm:CreateDesktopIcon}"),
                     ("startupicon", "Start {#MyAppName} with Windows"),
                     ("engine", "Download and install the container engine (about 250 MB)"),
                     ("pathentry", "Put docker and freewilly on my PATH"),
                 })
        {
            Assert.Contains($"Name: \"{name}\"", tasks, StringComparison.Ordinal);
            Assert.Contains($"Description: \"{caption}\"", tasks, StringComparison.Ordinal);
            Assert.Contains(caption, page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Each_box_starts_in_the_state_the_tasks_section_asks_for()
    {
        // The other half of DD123's cost, and the one that already bit once: WizardIsTaskSelected
        // cannot be the reader here, because Setup fills its task list while preparing the page this
        // one replaces — measured, it answered False for all four and every box came up empty. So
        // the starting state is restated in Pascal, the same shape as DistroName and
        // ProvisioningSteps, and this is what holds the two sides equal.
        //
        // `Flags: unchecked` is the whole of the fact. A task carrying it starts off; one without it
        // starts on. The first build of the page had the desktop icon ticked against a section that
        // says it must not be, and nothing else in the build would have said so.
        var script = InstallerScript();
        var tasks = script[script.IndexOf("[Tasks]", StringComparison.Ordinal)..];
        tasks = tasks[..tasks.IndexOf("[Files]", StringComparison.Ordinal)];

        var page = script[script.IndexOf("procedure BuildTasksPage;", StringComparison.Ordinal)..];
        page = page[..page.IndexOf("function ChosenTasks:", StringComparison.Ordinal)];

        foreach (var (name, box) in new[]
                 {
                     ("desktopicon", "WantDesktopIcon"),
                     ("startupicon", "WantStartupIcon"),
                     ("engine", "WantEngine"),
                     ("pathentry", "WantPathEntry"),
                 })
        {
            // The declaration is one logical line, continued with a backslash where it wraps.
            var at = tasks.IndexOf($"Name: \"{name}\"", StringComparison.Ordinal);
            var end = tasks.IndexOf("\nName: \"", at, StringComparison.Ordinal);
            var entry = end < 0 ? tasks[at..] : tasks[at..end];
            var startsOff = entry.Contains("Flags: unchecked", StringComparison.Ordinal);

            // The call that builds this box, whose Ticked argument is what the page opens with.
            var call = page[..page.IndexOf(box, StringComparison.Ordinal)];
            var lastCall = call.LastIndexOf("TaskBox(", StringComparison.Ordinal);
            var arguments = call[lastCall..];

            Assert.True(
                arguments.Contains(startsOff ? "False," : "True,", StringComparison.Ordinal),
                $"{name} is {(startsOff ? "unchecked" : "checked")} in [Tasks], and the page "
                + $"builds {box} the other way round");
        }
    }

    [Fact]
    public void The_declared_tasks_stay_the_answer_Setup_acts_on()
    {
        // The half that makes this a small change. [Tasks] is untouched, so every `Tasks:` parameter
        // in [Files], [Icons] and [Registry] keeps working and /MERGETASKS keeps steering an
        // unattended install — a page that replaced the section outright would have silently taken
        // the command-line switch with it, and nothing installs silently often enough to notice.
        var script = InstallerScript();

        Assert.Contains("WizardSelectTasks(ChosenTasks)", script, StringComparison.Ordinal);
        Assert.Contains("if PageID = wpSelectTasks then", script, StringComparison.Ordinal);
        Assert.Contains("WizardIsTaskSelected('engine')", script, StringComparison.Ordinal);

        // Every box states its answer either way, which `Term` is what guarantees: naming only the
        // ticked ones would let a default survive an untick, and the default that costs somebody a
        // quarter of a gigabyte is one of these four.
        var chosen = script[script.IndexOf("function ChosenTasks:", StringComparison.Ordinal)..];
        chosen = chosen[..chosen.IndexOf("// PATH", StringComparison.Ordinal)];

        foreach (var name in new[] { "desktopicon", "startupicon", "engine", "pathentry" })
        {
            Assert.Contains($"Term('{name}',", chosen, StringComparison.Ordinal);
        }

        Assert.Contains("Result := '!' + Name + ',';", script, StringComparison.Ordinal);
    }

    // ---- DD145: the page is measured rather than read -------------------------------------------

    [Fact]
    public void The_page_harness_is_on_the_ordinary_path_and_has_a_page_to_read()
    {
        // DD88's lesson and DD102's, in a third file: the site build was broken for 21 commits
        // because its workflow was workflow_dispatch only, and the installer script's first reader
        // was the release. A harness nothing runs is the same failure with better intentions.
        var check = Workflow("check.yml");
        Assert.Contains(@"scripts\page-probe.ps1", check, StringComparison.Ordinal);

        // After the compiler is on the PATH, because it compiles and runs a real Setup. Ordered
        // rather than assumed: moving it above that step would leave it failing for the one reason
        // that says nothing about the page.
        var inno = check.IndexOf("./.github/actions/inno-setup", StringComparison.Ordinal);
        var probe = check.IndexOf(@"scripts\page-probe.ps1", StringComparison.Ordinal);
        Assert.True(inno > 0 && probe > inno, "the page harness runs before ISCC is available");

        // And it still has something to slice. The markers are machine-readable precisely so this
        // does not depend on a section heading somebody renames while tidying up.
        var script = InstallerScript();
        var open = script.IndexOf("// >>> page-probe", StringComparison.Ordinal);
        var close = script.IndexOf("// <<< page-probe", StringComparison.Ordinal);
        Assert.True(
            open > 0 && close > open,
            "build/installer.iss no longer marks the block the harness compiles, so it measures "
            + "nothing while continuing to pass");

        // The harness knows the page by the two procedures it drives. Renaming either without
        // telling it produces a probe that compiles and reports on a page nobody built.
        var block = script[open..close];
        Assert.Contains("procedure BuildPreflightPage;", block, StringComparison.Ordinal);
        Assert.Contains("procedure ShowTheVerdict;", block, StringComparison.Ordinal);
    }

    // ---- DD141: the docker on PATH is this project's, and says what the failure could not --------

    /// <summary>The forwarder's project file, read as text.</summary>
    private static string ShimProject() =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "FreeWilly.Shim", "FreeWilly.Shim.csproj"));

    /// <summary>The forwarder's source, read as text.</summary>
    private static string ShimSource() =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "FreeWilly.Shim", "Program.cs"));

    [Fact]
    public void The_forwarder_is_a_console_command_because_that_is_why_it_is_a_second_binary()
    {
        // DD141, and this is the assertion the whole design rests on. A shell does not wait for a
        // windowed process — measured on this project's own .exe, whose PE subsystem is 2 — so a
        // copy of it named docker.exe would hand the prompt back before docker had printed anything
        // and would break every script and every agent driving this install.
        //
        // `Exe` and not `WinExe` is the one line that makes it subsystem 3. Flipping it would leave
        // a build that succeeds, an installer that installs, and a `docker` that silently stopped
        // being waited for, which is the least legible failure this repository could ship.
        var project = ShimProject();

        Assert.Contains("<OutputType>Exe</OutputType>", project, StringComparison.Ordinal);
        Assert.DoesNotContain("WinExe", project, StringComparison.Ordinal);
        Assert.DoesNotContain("<UseWPF>true</UseWPF>", project, StringComparison.Ordinal);

        // And it is the name on PATH. That name IS the interposition; without it this is a binary
        // nothing ever runs.
        Assert.Contains("<AssemblyName>docker</AssemblyName>", project, StringComparison.Ordinal);

        // Self-contained, because the machine is not promised a .NET runtime — a `docker` that fails
        // with "the framework was not found" is a worse failure than the one this explains.
        Assert.Contains("<SelfContained>true</SelfContained>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pipe_the_forwarder_watches_is_the_pipe_the_engine_serves()
    {
        // The forwarder references nothing on purpose: it runs on every docker command anybody
        // types, so it has to start in milliseconds. The price is a string restated in a second
        // place — the same shape as DistroName and ProvisioningSteps in the installer script — and
        // this is what holds the two equal.
        //
        // Nothing else would notice them drifting. The forwarder would simply stop recognising a
        // stopped engine, and the sentence DD141 exists to print would never appear again.
        Assert.Contains(
            $"EnginePipe = \"{Core.Api.DockerApi.DefaultPipeName}\"",
            ShimSource(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_forwarder_and_the_vendor_cli_cannot_share_a_directory()
    {
        // A rule of Windows rather than a preference. PATHEXT resolves .EXE before every other
        // extension, so a forwarder placed beside docker.exe is a file nothing would ever run —
        // which is why the vendor's copy moved one directory across rather than the forwarder being
        // given some other name.
        var paths = new Core.Engine.EnginePaths(@"C:\somewhere\FreeWilly");

        Assert.Equal(@"C:\somewhere\FreeWilly\bin\docker.exe", paths.DockerShim);
        Assert.Equal(@"C:\somewhere\FreeWilly\cli\docker.exe", paths.DockerCli);

        Assert.Equal(paths.CliDirectory, Path.GetDirectoryName(paths.DockerShim));
        Assert.NotEqual(paths.CliDirectory, Path.GetDirectoryName(paths.DockerCli));
    }

    [Fact]
    public void Owning_the_docker_command_is_the_same_choice_as_owning_the_PATH_entry()
    {
        // Gated on the same checkbox as the PATH entry, and that is the rule rather than a
        // convenience: placing this file is what makes this install the owner of the `docker`
        // command, so a user who declined that keeps whatever docker they already had.
        var entry = Assert.Single(
            InstallerDirectives(),
            line => line.Contains(@"\docker.exe""", StringComparison.Ordinal)
                    && line.Contains(@"DestDir: ""{app}\bin""", StringComparison.Ordinal));

        Assert.Contains("Tasks: pathentry", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void Everything_the_installer_packages_is_built_by_everything_that_compiles_it()
    {
        // DD150, and the guard it replaces is why it is written this way. That one named
        // build.cmd — the script the publish had just been added to — which is a test written from
        // the change rather than from the property. check.yml does not run build.cmd: it spells its
        // own publishes and then compiles the installer, so it met a source file nothing had made.
        // Reproduced by moving the shim's publish directory aside: "Source file ... does not exist.
        // Compile aborted." Every push, on the step DD102 added so the installer's first reader
        // would not be the release.
        //
        // Derived and never listed. A third binary added next year is under this rule with no edit
        // here, which is the same property No_build_command_names_the_folder_a_project_is_in has.
        var script = InstallerScript();

        // Each `#define <name> "..\src\<Project>\bin\..."` is a directory [Files] draws from, and
        // the project that fills it is the one the path names.
        var packaged = System.Text.RegularExpressions.Regex
            .Matches(script, @"#define\s+\w+\s+""\.\.\\src\\(?<project>[^\\""]+)\\bin\\")
            .Select(match => match.Groups["project"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Two, or this proves nothing about a second binary — which is the whole defect.
        Assert.Equal(2, packaged.Count);

        // check.yml spells its own publishes and then compiles the script, so each project has to
        // be named there. release.yml runs build-installer.cmd, which runs build.cmd, so that one
        // inherits whatever the script produces — checked against the script rather than the
        // workflow, because the workflow deliberately names nothing.
        var check = Workflow("check.yml");
        var build = File.ReadAllText(Path.Combine(RepositoryRoot(), "build", "build.cmd"));

        Assert.Contains(@"build\installer.iss", check, StringComparison.Ordinal);
        Assert.Contains("build-installer.cmd", Workflow("release.yml"), StringComparison.Ordinal);

        var missing = packaged
            .Where(project => !check.Contains(
                $@"dotnet publish src\{project}\{project}.csproj", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "check.yml compiles build\\installer.iss, which packages "
            + string.Join(" and ", missing)
            + " — and nothing on that workflow publishes "
            + (missing.Count == 1 ? "it" : "them")
            + ", so the compile meets a source file that was never made");

        var unbuilt = packaged
            .Where(project => !build.Contains(
                $@"dotnet publish src\{project}\{project}.csproj", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            unbuilt.Count == 0,
            "build\\build.cmd does not publish " + string.Join(" and ", unbuilt)
            + ", so build-installer.cmd — and the release that runs it — would compile the script "
            + "with a source file missing");
    }

    // ---- DD121: the uninstall stops what is running before it deletes anything ------------------

    /// <summary>The uninstall half of the script, which is the only part these read.</summary>
    private static string UninstallSection()
    {
        var script = InstallerScript();
        var start = script.IndexOf(
            "// Uninstall: stop what is running before deleting it", StringComparison.Ordinal);

        Assert.True(start > 0, "the uninstall section was renamed and these guards now read nothing");
        return script[start..];
    }

    [Fact]
    public void Nothing_is_deleted_until_what_is_holding_the_files_has_been_asked_to_go()
    {
        // The defect DD121 removes, asserted as an order rather than as a presence. Windows will not
        // delete an executable a process has open, so an uninstall run while the tray is in the
        // notification area took the Run value, the PATH entry and the Add/Remove Programs entry —
        // and then failed on FreeWilly.exe, leaving a root with no uninstaller left to take it.
        //
        // usUninstall is what makes it an order: Inno raises it before it removes a single file,
        // and usPostUninstall — where every DelTree in this script lives — after.
        var uninstall = UninstallSection();

        var stopping = uninstall.IndexOf("procedure StopEverything;", StringComparison.Ordinal);
        var stage = uninstall.IndexOf(
            "if CurUninstallStep = usUninstall then", StringComparison.Ordinal);
        var called = uninstall.IndexOf("StopEverything;", stage, StringComparison.Ordinal);
        var later = uninstall.IndexOf(
            "if CurUninstallStep <> usPostUninstall then", StringComparison.Ordinal);

        Assert.True(stopping > 0, "nothing in the uninstall stops the product");
        Assert.True(stage > stopping, "the uninstall never reaches usUninstall");
        Assert.True(called > stage && called < later,
            "the stop does not happen in usUninstall, which is the only step that runs before "
            + "Inno removes files");
    }

    [Fact]
    public void The_uninstall_stops_the_product_with_the_product_s_own_verbs()
    {
        // Two strings in Pascal against two constants in C#, and nothing but this notices them
        // drifting: a renamed verb leaves an uninstall that runs a command the executable refuses,
        // exits 2, and carries on into the delete that was the whole reason for running it.
        var uninstall = UninstallSection();

        Assert.Contains($"'{CommandLine.QuitVerb}'", uninstall, StringComparison.Ordinal);
        Assert.Contains("'--stop'", uninstall, StringComparison.Ordinal);
        Assert.Contains("--stop", CommandLine.EngineVerbs);

        // The engine before the tray: --stop terminates the distribution, and the detached --run
        // process serves something that lives inside it. Quitting the tray first would leave that
        // one running with nothing left to ask it to go.
        var stop = uninstall.IndexOf("'--stop'", StringComparison.Ordinal);
        var quit = uninstall.IndexOf($"'{CommandLine.QuitVerb}'", StringComparison.Ordinal);
        Assert.True(stop < quit, "the tray is asked to quit before the engine is stopped");
    }

    [Fact]
    public void Forcing_a_process_is_the_last_resort_and_never_the_first_move()
    {
        // A terminated tray leaves its icon in the notification area until something hovers it, so
        // the graceful verb has to be tried and its result read before anything is killed. The
        // ordering is the assertion: taskkill after the probe, and the probe after the verbs.
        var uninstall = UninstallSection();

        var quit = uninstall.IndexOf($"'{CommandLine.QuitVerb}'", StringComparison.Ordinal);
        var probe = uninstall.IndexOf("if not AnythingIsStillRunning then", StringComparison.Ordinal);
        var kill = uninstall.IndexOf("taskkill.exe", StringComparison.Ordinal);

        Assert.True(probe > quit, "nothing checks whether the graceful exit worked");
        Assert.True(kill > probe, "the uninstall forces processes without asking them first");
    }

    [Fact]
    public void The_one_choice_with_no_undo_is_off_until_somebody_ticks_it()
    {
        // Images and volumes, and the rule that survived DD121 unchanged: silence means keep. An
        // unattended uninstall has nobody to ask, so it must never be the thing that deletes them —
        // which is what the unset default buys, since UninstallSilent skips the page entirely.
        var uninstall = UninstallSection();

        Assert.Contains("Distribution.Checked := False;", uninstall, StringComparison.Ordinal);
        Assert.Contains("if not UninstallSilent then", uninstall, StringComparison.Ordinal);

        // Read after the page rather than set by it in the silent path, so the default is the
        // variable's own zero and not a line somebody has to remember to write.
        var reading = uninstall.IndexOf("if RemoveTheDistribution then", StringComparison.Ordinal);
        Assert.True(reading > 0, "nothing acts on the distribution answer");
    }

    [Fact]
    public void Everything_this_product_owns_is_gone_before_Inno_tries_to_remove_the_root()
    {
        // Inno removes the install directory during its own file pass — between usUninstall and
        // usPostUninstall — and only if it is empty by then. Measured: with this work in
        // usPostUninstall, an uninstall took the registration, the distribution, the virtual disk
        // and every file, and left an empty root standing that nothing would ever offer to take.
        //
        // So the assertion is a placement. Every delete has to sit above the usPostUninstall guard,
        // which is the line that separates the two steps in this procedure.
        var uninstall = UninstallSection();
        var boundary = uninstall.IndexOf(
            "if CurUninstallStep <> usPostUninstall then", StringComparison.Ordinal);

        Assert.True(boundary > 0, "the two uninstall steps are no longer told apart here");

        foreach (var delete in new[]
                 {
                     @"DelTree(ExpandConstant('{app}\bin')",
                     @"DelTree(ExpandConstant('{app}\cli')",
                     @"DelTree(ExpandConstant('{app}\cli-plugins')",
                     @"DelTree(ExpandConstant('{app}\distro')",
                     @"DelTree(ExpandConstant('{app}\downloads')",
                     @"DelTree(ExpandConstant('{app}\rescue-*.tar')",
                 })
        {
            var at = uninstall.IndexOf(delete, StringComparison.Ordinal);
            Assert.True(at > 0, $"{delete} is not in the uninstall at all");
            Assert.True(at < boundary,
                $"{delete} runs after Inno has already decided whether the root was empty, so an "
                + "uninstall that removed everything still leaves the folder behind");
        }
    }

    [Fact]
    public void The_prepared_rescue_goes_with_the_uninstall_and_not_with_the_question()
    {
        // DD223. It is a cache this product wrote from the rootfs it pins, not anybody's data, so
        // it belongs with preflight.txt and engine.log rather than behind the question about images
        // and volumes — a machine that answered "keep my data" still did not ask to keep eleven
        // megabytes of Alpine it cannot account for.
        var uninstall = UninstallSection();

        var rescue = uninstall.IndexOf(
            @"DelTree(ExpandConstant('{app}\rescue-*.tar')", StringComparison.Ordinal);
        var question = uninstall.IndexOf(
            "if RemoveTheDistribution then", StringComparison.Ordinal);

        Assert.True(rescue > 0, "the prepared rescue survives an uninstall");
        Assert.True(question > 0, "the question about the distribution is no longer asked here");
        Assert.True(
            rescue < question,
            "the prepared rescue is only removed where the user agreed to lose their images, so "
            + "keeping those keeps a cache nobody asked for");

        // The wildcard and not one exact name: the file carries the pinned rootfs digest, so an
        // install that has seen two Alpine versions has two of them.
        Assert.Contains("rescue-*.tar", uninstall, StringComparison.Ordinal);

        // And the note this install wrote about the machine's sparse disks goes the same way
        // (DD226): a fact this product recorded about somebody's Windows, not a setting they chose.
        var note = uninstall.IndexOf(
            @"DeleteFile(ExpandConstant('{app}\sparse-refused.txt')", StringComparison.Ordinal);

        Assert.True(note > 0, "the sparse-disk note survives an uninstall and keeps the root alive");
        Assert.True(note < question, "the note is only removed where the images are");
    }

    [Fact]
    public void The_report_checks_the_promise_that_had_no_undo_and_not_the_root()
    {
        // The root cannot be the check: the uninstaller is still running out of it, unins000.exe is
        // still in it, and Inno deletes both after usPostUninstall returns — so DirExists({app})
        // there is always true and the report always fires. Measured, on a successful uninstall.
        //
        // What can be checked is what the page promised: the distribution and its downloads gone.
        var uninstall = UninstallSection();
        var report = uninstall.IndexOf(
            "if CurUninstallStep <> usPostUninstall then", StringComparison.Ordinal);
        var after = uninstall[report..];

        Assert.Contains("not OwnedDataExists", after, StringComparison.Ordinal);
        Assert.DoesNotContain("DirExists(Left)", after, StringComparison.Ordinal);

        // Silence stays silent, and a kept distribution is a choice rather than a failure.
        Assert.Contains(
            "if UninstallSilent or not RemoveTheDistribution or not OwnedDataExists then",
            after,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_distribution_is_terminated_before_it_is_unregistered()
    {
        // An open virtual disk is a directory that survives its own deletion: unregistering a
        // running distribution leaves the .vhdx locked, the DelTree below fails, and {app} stays on
        // disk holding a quarter of a gigabyte nothing will ever offer to remove again.
        var uninstall = UninstallSection();

        var terminate = uninstall.IndexOf("'--terminate '", StringComparison.Ordinal);
        var unregister = uninstall.IndexOf("'--unregister '", StringComparison.Ordinal);
        // The removal and not the probe: OwnedDataExists names the same directory to decide whether
        // there is anything to ask about, and it stands above all of this.
        var tree = uninstall.IndexOf(
            @"DelTree(ExpandConstant('{app}\distro')", StringComparison.Ordinal);

        Assert.True(terminate > 0, "the distribution is unregistered without being terminated");
        Assert.True(unregister > terminate);
        Assert.True(tree > unregister);
    }

    [Fact]
    public void The_version_the_assembly_states_is_the_one_the_installer_would_read() =>
        // GetStringFileInfo(PRODUCT_VERSION) reads the informational version, which is what
        // BuildVersion prints. Two ways to ask, and they have to agree or the installed version and
        // the About box disagree about the same build.
        Assert.Equal(
            Shipped.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
            BuildVersion.Current);
}
