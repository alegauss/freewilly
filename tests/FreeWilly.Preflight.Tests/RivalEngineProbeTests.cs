using FreeWilly.Core.Preflight;
using FreeWilly.Core.Preflight.Windows;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// The one row that must never be wrongly green (DD16).
/// </summary>
/// <remarks>
/// The judging was untested before this file, and that is where the defect was: the probe asked
/// where a vendor installs, Docker Desktop moved to a per-user directory, and a machine with it
/// installed and shut down answered no to all three signals. The report said
/// <c>[ok] Container engine</c> and exited 0, which clears an install to walk into the pipe
/// collision the row exists to prevent.
///
/// A rival engine cannot be installed inside a test, so these drive <see cref="RivalSignals"/>
/// directly — which is why the reading and the deciding were split.
/// </remarks>
public sealed class RivalEngineProbeTests
{
    /// <summary>The machine this row got wrong, as signals.</summary>
    private static RivalSignals DevelopmentMachine => new()
    {
        // Measured: this is where `docker` resolved, and where.exe agreed.
        DockerCommand =
            @"C:\Users\alexa\AppData\Local\Programs\DockerDesktop\resources\bin\docker.exe",
        Distributions = ["Ubuntu", "docker-desktop"],
        VendorInstalls = [],   // neither %ProgramFiles%\Docker nor Rancher was there
        EnginePipeOpen = false, // the app was not running
        OwnCliDirectory = @"C:\Users\alexa\AppData\Local\FreeWilly\bin",
    };

    // ---- the defect ---------------------------------------------------------------------------

    [Fact]
    public void Docker_Desktop_installed_per_user_and_shut_down_is_found()
    {
        var found = RivalEngineProbe.Judge(DevelopmentMachine);

        var rival = Assert.Single(found);
        Assert.Equal("Docker Desktop", rival.Name);
    }

    [Fact]
    public void The_row_that_found_it_blocks_the_install()
    {
        // The whole point: not merely that the probe noticed, but that the report refuses. Green
        // here is the failure mode, and it exited 0 before this.
        var report = PreflightInspection.Run(new FakeMachine
        {
            RivalEngines = RivalEngineProbe.Judge(DevelopmentMachine),
        });

        Assert.False(report.CanHostEngine);
        var row = Assert.Single(report.Blockers);
        Assert.Equal("Container engine", row.Title);
        Assert.Contains("Docker Desktop", row.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_signals_are_carried_as_evidence_so_the_user_can_argue_with_it()
    {
        var rival = Assert.Single(RivalEngineProbe.Judge(DevelopmentMachine));

        Assert.Contains("docker resolves to", rival.Evidence, StringComparison.Ordinal);
        Assert.Contains(@"DockerDesktop\resources\bin\docker.exe", rival.Evidence, StringComparison.Ordinal);
        Assert.Contains("docker-desktop", rival.Evidence, StringComparison.Ordinal);
    }

    // ---- one product, one row -----------------------------------------------------------------

    [Fact]
    public void Three_signals_for_one_product_are_one_row_and_not_three()
    {
        var found = RivalEngineProbe.Judge(new RivalSignals
        {
            DockerCommand = @"C:\Program Files\Docker\Docker\resources\bin\docker.exe",
            Distributions = ["docker-desktop", "docker-desktop-data"],
            VendorInstalls = [new RivalEngine("Docker Desktop", @"C:\Program Files\Docker\Docker\Docker Desktop.exe")],
            EnginePipeOpen = true,
        });

        var rival = Assert.Single(found);
        Assert.Equal("Docker Desktop", rival.Name);
        // Every signal is still in the evidence; only the row count collapsed.
        Assert.Contains("docker resolves to", rival.Evidence, StringComparison.Ordinal);
        Assert.Contains("docker-desktop-data", rival.Evidence, StringComparison.Ordinal);
        Assert.Contains("Docker Desktop.exe", rival.Evidence, StringComparison.Ordinal);
        Assert.Contains("pipe", rival.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_different_products_are_two_rows()
    {
        var found = RivalEngineProbe.Judge(new RivalSignals
        {
            Distributions = ["docker-desktop", "rancher-desktop"],
        });

        Assert.Equal(2, found.Count);
        Assert.Equal(["Docker Desktop", "Rancher Desktop"], found.Select(r => r.Name));
    }

    // ---- not a rival --------------------------------------------------------------------------

    [Fact]
    public void An_empty_machine_is_still_empty()
    {
        Assert.Empty(RivalEngineProbe.Judge(new RivalSignals()));
    }

    [Fact]
    public void This_tool_is_not_a_rival_to_itself()
    {
        // DD14 puts this tool's own bin on PATH, so after a working install `docker` resolves there.
        // Without this the fix for a wrongly green row is a wrongly red one on every machine where
        // the product is doing its job.
        var found = RivalEngineProbe.Judge(new RivalSignals
        {
            DockerCommand = @"C:\Users\alexa\AppData\Local\FreeWilly\bin\docker.exe",
            OwnCliDirectory = @"C:\Users\alexa\AppData\Local\FreeWilly\bin",
        });

        Assert.Empty(found);
    }

    [Theory]
    [InlineData(@"C:\Users\alexa\AppData\Local\FreeWilly\bin\")]
    [InlineData(@"c:\users\alexa\appdata\local\freewilly\bin")]
    public void Its_own_directory_is_recognised_whatever_the_case_or_trailing_slash(string own) =>
        Assert.Empty(RivalEngineProbe.Judge(new RivalSignals
        {
            DockerCommand = @"C:\Users\alexa\AppData\Local\FreeWilly\bin\docker.exe",
            OwnCliDirectory = own,
        }));

    [Fact]
    public void This_project_s_own_distribution_is_not_a_rival()
    {
        // This used to assert two names, the second being the distribution from before the rename,
        // which DD55 adopted where it stood and DD86 removed. What remains is DD56's rule: the one
        // name this tool owns is never reported as an unidentified engine, on the row this project
        // says must never be wrongly red.
        Assert.Empty(RivalEngineProbe.Judge(new RivalSignals { Distributions = ["freewilly"] }));

        // However WSL happens to report it.
        Assert.Empty(RivalEngineProbe.Judge(new RivalSignals { Distributions = ["  FreeWilly  "] }));
    }

    [Fact]
    public void Skipping_our_own_name_does_not_skip_anybody_else_s()
    {
        // The other half of the same rule, and the one that would go wrong silently.
        var found = RivalEngineProbe.Judge(new RivalSignals
        {
            Distributions = ["freewilly", "docker-desktop"],
        });

        Assert.Equal("Docker Desktop", Assert.Single(found).Name);
    }

    [Fact]
    public void Ownership_is_by_name_rather_than_by_what_Known_happens_to_list()
    {
        // Stated rather than left to the accident that no entry in Known spells our name. Adding a
        // product whose distribution list did would otherwise make this engine report itself, and
        // the row that must never be wrongly red is the worst place to rely on a coincidence.
        Assert.True(RivalEngineProbe.IsOurDistribution("freewilly"));
        Assert.False(RivalEngineProbe.IsOurDistribution("docker-desktop"));
        Assert.False(RivalEngineProbe.IsOurDistribution("Ubuntu"));
    }

    // ---- naming what it found -----------------------------------------------------------------

    [Theory]
    [InlineData(@"C:\Users\x\AppData\Local\Programs\DockerDesktop\resources\bin\docker.exe", "Docker Desktop")]
    [InlineData(@"C:\Program Files\Docker\Docker\resources\bin\docker.exe", "Docker Desktop")]
    [InlineData(@"C:\Users\x\AppData\Local\Programs\Rancher Desktop\resources\docker.exe", "Rancher Desktop")]
    [InlineData(@"C:\Program Files\RedHat\Podman\docker.exe", "Podman")]
    [InlineData(@"C:\Users\x\.minikube\bin\docker.exe", "minikube")]
    [InlineData(@"C:\tools\somebody-elses\docker.exe", "another engine")]
    public void The_product_is_named_from_where_the_command_lives(string docker, string expected)
    {
        var rival = Assert.Single(RivalEngineProbe.Judge(new RivalSignals { DockerCommand = docker }));

        Assert.Equal(expected, rival.Name);
    }

    [Fact]
    public void An_open_pipe_nobody_owns_is_still_reported()
    {
        var rival = Assert.Single(RivalEngineProbe.Judge(
            new RivalSignals { EnginePipeOpen = true }));

        Assert.Equal("an unidentified engine", rival.Name);
        Assert.Contains("docker_engine", rival.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pipe_this_tool_is_serving_on_is_not_a_rival()
    {
        // DD231, measured by running --preflight on a machine with this tool's engine up: the row
        // came back red naming an unidentified engine, with `uninstall it first` as the remedy. It
        // was telling somebody to uninstall FreeWilly before installing FreeWilly, and
        // CanHostEngine went false with it, so --provision refused on a machine that was working.
        //
        // Signals 1 and 2 each drop this project's own before judging (DD16, DD56). The pipe was
        // the one that could not tell whose it was.
        var found = RivalEngineProbe.Judge(
            new RivalSignals { EnginePipeOpen = true, OurEngineServing = true });

        Assert.Empty(found);
    }

    [Fact]
    public void An_engine_of_ours_does_not_hide_a_rival_that_was_identified()
    {
        // The direction that matters most: a rival mistaken for us is a green row clearing an
        // install into the collision DD16 exists to prevent. Our engine explains the pipe and
        // nothing else, so everything the other signals found is still reported, and the pipe stops
        // being offered as evidence against somebody it does not belong to.
        var found = RivalEngineProbe.Judge(new RivalSignals
        {
            DockerCommand = @"C:\Program Files\Docker\Docker\resources\bin\docker.exe",
            Distributions = ["docker-desktop"],
            EnginePipeOpen = true,
            OurEngineServing = true,
        });

        var rival = Assert.Single(found);
        Assert.Equal("Docker Desktop", rival.Name);
        Assert.Contains("docker resolves to", rival.Evidence, StringComparison.Ordinal);
        Assert.DoesNotContain("docker_engine", rival.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void An_engine_of_ours_that_is_not_running_hides_nothing()
    {
        // The exclusion is about a host that is alive, not about this product being installed. A
        // machine with FreeWilly on it and nothing serving still has to report whoever holds the
        // pipe, because that somebody is not us.
        var rival = Assert.Single(RivalEngineProbe.Judge(
            new RivalSignals { EnginePipeOpen = true, OurEngineServing = false }));

        Assert.Equal("an unidentified engine", rival.Name);
    }

    [Fact]
    public void The_probe_and_the_host_name_one_object()
    {
        // Two literals is a probe that stops recognising its own engine the day one is renamed, on
        // the row that must never be wrongly red.
        Assert.Equal(
            FreeWilly.Core.Engine.EngineHostSlot.Name, FreeWilly.Tray.Cli.SingleEngine.Name);
    }

    [Fact]
    public void A_null_signal_set_is_a_defect_here_rather_than_an_empty_machine() =>
        Assert.Throws<ArgumentNullException>(() => RivalEngineProbe.Judge(null!));

    // ---- resolving the command ----------------------------------------------------------------

    [Fact]
    public void The_command_is_resolved_the_way_a_shell_resolves_it()
    {
        // A real directory with a real file, because File.Exists is the whole mechanism.
        var folder = Directory.CreateTempSubdirectory("freewilly-path").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "docker.exe"), "");

            var found = RivalEngineProbe.ResolveOnPath("docker", folder, ".COM;.EXE");

            Assert.Equal(Path.Combine(folder, "docker.exe"), found);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void PATHEXT_order_decides_which_of_two_files_would_run()
    {
        // Measured on the development machine: that directory holds both an extensionless `docker`
        // and a `docker.exe`, and where.exe listed both. The resolved path has to be the one that
        // would actually run, or the evidence names a file the shell would never execute.
        var folder = Directory.CreateTempSubdirectory("freewilly-pathext").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "docker.cmd"), "");
            File.WriteAllText(Path.Combine(folder, "docker.exe"), "");

            Assert.Equal(
                Path.Combine(folder, "docker.exe"),
                RivalEngineProbe.ResolveOnPath("docker", folder, ".EXE;.CMD"));
            Assert.Equal(
                Path.Combine(folder, "docker.cmd"),
                RivalEngineProbe.ResolveOnPath("docker", folder, ".CMD;.EXE"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Earlier_directories_win_and_a_quoted_entry_is_still_a_directory()
    {
        var first = Directory.CreateTempSubdirectory("freewilly-first").FullName;
        var second = Directory.CreateTempSubdirectory("freewilly-second").FullName;
        try
        {
            File.WriteAllText(Path.Combine(second, "docker.exe"), "");

            // The first directory holds nothing, the second is quoted the way a PATH entry can be.
            var found = RivalEngineProbe.ResolveOnPath(
                "docker", $"{first};\"{second}\"", ".EXE");

            Assert.Equal(Path.Combine(second, "docker.exe"), found);
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }

    [Fact]
    public void A_malformed_entry_does_not_stop_the_search()
    {
        var folder = Directory.CreateTempSubdirectory("freewilly-malformed").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "docker.exe"), "");

            // A PATH with rubbish in it is normal, and one bad entry must not hide the answer that
            // comes after it.
            var found = RivalEngineProbe.ResolveOnPath(
                "docker", $"C:\\no|such<dir>;;   ;{folder}", ".EXE");

            Assert.Equal(Path.Combine(folder, "docker.exe"), found);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_on_PATH_resolves_to_nothing(string? path) =>
        Assert.Null(RivalEngineProbe.ResolveOnPath("docker", path, ".EXE"));

    [Fact]
    public void A_command_that_is_not_there_resolves_to_nothing()
    {
        var folder = Directory.CreateTempSubdirectory("freewilly-absent").FullName;
        try
        {
            Assert.Null(RivalEngineProbe.ResolveOnPath("docker", folder, ".EXE;.CMD"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void An_unset_PATHEXT_falls_back_to_what_cmd_uses()
    {
        var folder = Directory.CreateTempSubdirectory("freewilly-noext").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "docker.CMD"), "");

            Assert.Equal(
                Path.Combine(folder, "docker.CMD"),
                RivalEngineProbe.ResolveOnPath("docker", folder, null));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // ---- what the machine actually says -------------------------------------------------------

    [Fact]
    public void The_registry_read_answers_without_throwing()
    {
        // Not asserted on content: a machine may have no distributions at all, and this row must
        // never fail because of that. What is asserted is that reading it is safe, since the
        // alternative considered was `wsl --list --quiet`, whose UTF-16LE output this project has
        // already been bitten by.
        var distributions = RivalEngineProbe.ReadDistributions();

        Assert.NotNull(distributions);
        Assert.All(distributions, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }
}
