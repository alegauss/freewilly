namespace FreeWilly.Core.Preflight;

/// <summary>
/// Turns <see cref="IMachineFacts"/> into the report. Pure, and deliberately: the judgement is
/// the part worth testing, and the reading is the part that cannot be faked on demand.
/// </summary>
public static class PreflightInspection
{
    /// <summary>
    /// The oldest Windows build that ships the WSL2 kernel — version 2004, build 19041. Below it
    /// there is no amount of configuration that gets an engine running.
    /// </summary>
    public const int MinimumWindowsBuild = 19041;

    /// <summary>The row ids, so a caller names one without spelling a string twice.</summary>
    public static class Rows
    {
        /// <summary>The Windows build row.</summary>
        public const string WindowsBuild = "windows-build";

        /// <summary>The hardware virtualization row.</summary>
        public const string Virtualization = "virtualization";

        /// <summary>The WSL2 row.</summary>
        public const string Wsl2 = "wsl2";

        /// <summary>The rival-engine row.</summary>
        public const string RivalEngine = "rival-engine";

        /// <summary>The row saying where this user's own docker command points.</summary>
        public const string DockerContext = "docker-context";
    }

    /// <summary>Read every row from <paramref name="facts"/>, in report order.</summary>
    /// <param name="facts">What this machine says about itself.</param>
    /// <returns>The report.</returns>
    public static PreflightReport Run(IMachineFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return new PreflightReport([
            CheckWindowsBuild(facts),
            CheckVirtualization(facts),
            CheckWsl2(facts),
            CheckRivalEngines(facts),
            CheckDockerContext(facts),
        ]);
    }

    private static PreflightCheck CheckWindowsBuild(IMachineFacts facts)
    {
        var version = facts.OperatingSystem;
        var detail = $"Windows {version.Major}.{version.Minor}, build {version.Build}";

        return version.Build >= MinimumWindowsBuild
            ? new PreflightCheck
            {
                Id = Rows.WindowsBuild,
                Title = "Windows build",
                Verdict = Verdict.Pass,
                Detail = detail,
                Blocking = true,
            }
            : new PreflightCheck
            {
                Id = Rows.WindowsBuild,
                Title = "Windows build",
                Verdict = Verdict.Fail,
                Detail = $"{detail}: WSL2 needs {MinimumWindowsBuild} or later",
                Remedy = "Install Windows updates until the build reaches "
                    + $"{MinimumWindowsBuild} (Windows 10 version 2004).",
                Blocking = true,
            };
    }

    private static PreflightCheck CheckVirtualization(IMachineFacts facts)
    {
        // First, because inside a guest neither of the other two facts answers the question (DD19).
        // Measured on a VMware guest with nested virtualization switched off: HypervisorPresent is
        // true and the firmware bit is false — identical to a healthy laptop running Hyper-V. The
        // row used to read the first of those as proof, cleared the install, and the install then
        // failed at ImportDistribution with "WSL2 cannot start because virtualization is not
        // enabled on this machine". Nothing readable from inside a guest says whether the host
        // exposed nested virtualization, so this abstains rather than guesses either way.
        if (facts.IsVirtualMachine is true)
        {
            return new PreflightCheck
            {
                Id = Rows.Virtualization,
                Title = "Hardware virtualization",
                Verdict = Verdict.Unknown,
                Detail = "this is a virtual machine, and whether it can host another hypervisor "
                    + "cannot be read from inside it",
                Remedy = "Enable nested virtualization for this VM on its host, then run this "
                    + "check again. In VMware that is Virtualize Intel VT-x/EPT in the VM's "
                    + "processor settings; in Hyper-V it is Set-VMProcessor "
                    + "-ExposeVirtualizationExtensions $true.",
                Blocking = true,
            };
        }

        // Order matters. Windows reports the firmware bit as false once a hypervisor has claimed
        // it, so reading the bit first calls a machine that is plainly virtualizing "disabled"
        // and sends the user into a BIOS to switch on something already on.
        if (facts.HypervisorPresent is true)
        {
            return Green(Rows.Virtualization, "Hardware virtualization",
                "enabled: a hypervisor is already running");
        }

        return facts.VirtualizationFirmwareEnabled switch
        {
            true => Green(Rows.Virtualization, "Hardware virtualization",
                "enabled in firmware"),

            false => new PreflightCheck
            {
                Id = Rows.Virtualization,
                Title = "Hardware virtualization",
                Verdict = Verdict.Fail,
                Detail = "disabled in firmware",
                Remedy = "Reboot into UEFI/BIOS setup and enable virtualization "
                    + "(Intel VT-x or AMD-V), then run this check again.",
                Blocking = true,
            },

            null => new PreflightCheck
            {
                Id = Rows.Virtualization,
                Title = "Hardware virtualization",
                Verdict = Verdict.Unknown,
                Detail = "could not be read on this machine",
                Remedy = "Open Task Manager, Performance, CPU and read the Virtualization row. "
                    + "If it says Enabled, this check is what is wrong and not the machine.",
                Blocking = true,
            },
        };
    }

    private static PreflightCheck CheckWsl2(IMachineFacts facts)
    {
        var wsl = facts.Wsl;

        if (!wsl.CommandPresent)
        {
            return new PreflightCheck
            {
                Id = Rows.Wsl2,
                Title = "WSL2",
                Verdict = Verdict.Fail,
                Detail = "not installed: wsl.exe is not on this machine",
                Remedy = "Run `wsl --install --no-distribution` in an administrator terminal, "
                    + "then reboot.",
                Blocking = true,
            };
        }

        if (wsl.FeatureInstalled is false)
        {
            // The most common machine this installer meets, and the one the row used to spend
            // fifteen seconds failing to describe (DD18). `wsl.exe` ships in System32 whether or
            // not the feature does, so the launcher being there says nothing — and the remedy is
            // install, never update. `wsl --update` on this machine updates nothing.
            return new PreflightCheck
            {
                Id = Rows.Wsl2,
                Title = "WSL2",
                Verdict = Verdict.Fail,
                Detail = "not installed: wsl.exe ships with Windows, the feature does not",
                Remedy = "Run `wsl.exe --install --no-distribution` in an administrator terminal, "
                    + "then reboot.",
                Blocking = true,
            };
        }

        if (wsl.KernelVersion is null)
        {
            // wsl.exe present and no kernel behind it is the half-installed state a Windows
            // feature leaves: the command answers, and there is nothing for it to boot.
            return new PreflightCheck
            {
                Id = Rows.Wsl2,
                Title = "WSL2",
                Verdict = wsl.Unreadable is null ? Verdict.Fail : Verdict.Unknown,
                Detail = wsl.Unreadable ?? "wsl.exe is installed, and no WSL2 kernel is",
                Remedy = "Run `wsl --update` in an administrator terminal.",
                Blocking = true,
            };
        }

        if (wsl.DefaultVersion == 1)
        {
            // Warn, so it does not stop the install: the default decides what a *new* distro is
            // created as, and this installer names the version it wants. Worth saying anyway,
            // because the user's own distros are on WSL1 and that is a surprise they should meet
            // here rather than later.
            return new PreflightCheck
            {
                Id = Rows.Wsl2,
                Title = "WSL2",
                Verdict = Verdict.Warn,
                Detail = $"kernel {wsl.KernelVersion} is installed, and new distros default to WSL1",
                Remedy = "Run `wsl --set-default-version 2` to make WSL2 the default.",
                Blocking = true,
            };
        }

        var version = wsl.Version is null ? "" : $"WSL {wsl.Version}, ";
        return Green(Rows.Wsl2, "WSL2", $"{version}kernel {wsl.KernelVersion}");
    }

    /// <summary>
    /// Where the user's own <c>docker</c> will go, said out loud (DD20).
    /// </summary>
    /// <remarks>
    /// Never blocking, and that is deliberate: a leftover context does not stop this engine from
    /// working, it stops the user's CLI from finding it. The install succeeds and then the tool looks
    /// broken with nothing wrong with it, which is a thing to be told rather than stopped for.
    ///
    /// It also changes nothing. DD20 weighed registering a context of this project's own and making
    /// it active — what a rival does — against saying where the CLI points and leaving the setting to
    /// the person who owns it. This is the second.
    /// </remarks>
    private static PreflightCheck CheckDockerContext(IMachineFacts facts)
    {
        var client = facts.DockerClient;
        var ours = $@"\\.\pipe\{Api.DockerApi.DefaultPipeName}";

        if (client.Unreadable is { } why)
        {
            return new PreflightCheck
            {
                Id = Rows.DockerContext,
                Title = "docker context",
                Verdict = Verdict.Warn,
                Detail = why,
                Remedy = $"Run `docker context use {Windows.DockerContextProbe.DefaultContextName}` "
                    + $"to point your docker at {ours}.",
            };
        }

        if (Windows.DockerContextProbe.ReachesThisEngine(client.Host))
        {
            var via = client.FromEnvironment
                ? "DOCKER_HOST"
                : client.ContextName ?? Windows.DockerContextProbe.DefaultContextName;
            return Green(Rows.DockerContext, "docker context", $"{via} reaches this engine");
        }

        var where = client.Host is { Length: > 0 } host ? host : "somewhere this cannot read";

        // DOCKER_HOST outranks the active context, so a remedy naming `docker context use` would be
        // advice that changes nothing while the variable is set.
        var remedy = client.FromEnvironment
            ? $"DOCKER_HOST is set to {where}. Clear it to let the context decide, or point it at "
                + $"{ours}."
            : $"Run `docker context use {Windows.DockerContextProbe.DefaultContextName}` to point "
                + "your docker at this engine. Nothing here changes it for you.";

        var named = client.FromEnvironment
            ? $"DOCKER_HOST sends docker to {where}"
            : $"docker follows {client.ContextName} → {where}";

        return new PreflightCheck
        {
            Id = Rows.DockerContext,
            Title = "docker context",
            Verdict = Verdict.Warn,
            Detail = $"{named}, and this engine serves {ours}",
            Remedy = remedy,
        };
    }

    private static PreflightCheck CheckRivalEngines(IMachineFacts facts)
    {
        if (facts.RivalEngines.Count == 0)
        {
            return Green(Rows.RivalEngine, "Container engine",
                "nothing else owns the docker command or the docker_engine pipe");
        }

        // DD52. This used to be one joined line — 254 characters on the development machine, because
        // the row carries every signal it was found by. The names go on the row, where they are the
        // answer; the paths and pipes go underneath, one per line, where nothing can break them.
        //
        // The product prefix appears only where there is something to disambiguate. With two engines
        // installed, a bare path under a row naming both is evidence for neither; with one, the row
        // directly above already says whose it is, and repeating it on four lines is noise in the
        // place a user is trying to read a path.
        var names = string.Join(", ", facts.RivalEngines.Select(engine => engine.Name));
        var attribute = facts.RivalEngines.Count > 1;
        var evidence = facts.RivalEngines
            .SelectMany(engine => engine.Signals.Select(
                signal => attribute ? $"{engine.Name}: {signal}" : signal))
            .ToArray();

        return new PreflightCheck
        {
            Id = Rows.RivalEngine,
            Title = "Container engine",
            Verdict = Verdict.Fail,
            Detail = $"already installed: {names}",
            Evidence = evidence,
            Remedy = "Uninstall it first. Two engines competing for the docker_engine pipe "
                + "leaves neither of them working.",
            Blocking = true,
        };
    }

    private static PreflightCheck Green(string id, string title, string detail) => new()
    {
        Id = id,
        Title = title,
        Verdict = Verdict.Pass,
        Detail = detail,
        Blocking = true,
    };
}
