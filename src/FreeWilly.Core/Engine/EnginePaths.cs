namespace FreeWilly.Core.Engine;

/// <summary>
/// Where everything this tool owns lives, and what its distribution is called. One root under
/// <c>%LOCALAPPDATA%</c>, because a per-user install is what reaches a managed laptop with no
/// administrator prompt.
/// </summary>
/// <remarks>
/// <b>A root and a distribution name are state on somebody's machine, not text in a build.</b> The
/// distribution holds every image, container and volume the user created, and <c>distro</c>,
/// <c>downloads</c> and <c>bin</c> hang off the root — <c>bin</c> being what the installer put on
/// <c>PATH</c>. So renaming either in a build does not rename what is on disk: it starts an empty
/// engine beside a full one and reports nothing installed on a machine that has everything.
///
/// <para><b>Which is why a rename adopts in place and never re-imports.</b> Exporting the
/// distribution and importing it under a new name copies gigabytes to change a label and has a
/// failure mode in the middle that loses the lot. The root cannot move for the same reason from the
/// other end: <c>distro</c> is the BasePath WSL registered the distribution at, so moving the
/// directory orphans the distribution exactly as surely as renaming it would. That argument is
/// about WSL rather than about this project's history, and the next rename needs it again — which
/// is why it survives DD86, and the adoption code that once acted on it does not.</para>
///
/// <para>DD55 carried both spellings for an install made before the rename. DD86 removed them:
/// nothing was ever released under the old name, so the compatibility was with nobody and its cost
/// was a second name in every path, label and probe a reader meets.</para>
///
/// <para>An owned distribution makes the uninstall exactly one command: it unregisters
/// <see cref="DistributionName"/>.</para>
/// </remarks>
public sealed class EnginePaths
{
    /// <summary>
    /// The distribution a fresh install creates. Deliberately not the user's own: an
    /// <c>apk upgrade</c> or a <c>wsl --unregister</c> they ran for another reason would take the
    /// engine with it.
    /// </summary>
    public const string CurrentDistribution = "freewilly";

    /// <summary>The directory a fresh install owns under <c>%LOCALAPPDATA%</c>.</summary>
    public const string CurrentRootName = "FreeWilly";

    /// <summary>Construct the layout under an explicit root.</summary>
    /// <param name="root">The directory this tool owns.</param>
    /// <param name="distributionName">
    /// The distribution this install owns. Defaults to what a fresh install creates.
    /// </param>
    public EnginePaths(string root, string distributionName = CurrentDistribution)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(distributionName);
        Root = root;
        DistributionName = distributionName;
    }

    /// <summary>Construct the layout this machine has.</summary>
    /// <remarks>
    /// One root and one distribution name, both derived from constants. They were resolved — a
    /// candidate pair searched on disk and against WSL's registered list — for as long as an install
    /// could carry the names from before the rename, and DD86 removed that: nothing shipped under
    /// them, so there was nothing to find.
    /// </remarks>
    public EnginePaths()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            CurrentRootName))
    {
    }

    /// <summary>The directory this tool owns.</summary>
    public string Root { get; }

    /// <summary>The WSL2 distribution this install owns.</summary>
    public string DistributionName { get; }

    /// <summary>Whether that distribution is registered with WSL on this machine (DD120).</summary>
    /// <remarks>
    /// The one question that separates "the engine is not running" from "there is no engine", and
    /// the tray has to answer it on a click: before DD120 it launched a start that could only fail,
    /// and then showed Starting for as long as the icon lived.
    ///
    /// <para>Through the registry, which is what <see cref="Wsl.RegisteredDistributions"/> reads and
    /// why it reads it — a click cannot afford a subprocess, and <c>wsl --list</c>'s UTF-16LE output
    /// is the decoding wart this project has already been bitten by. <see cref="EngineLifecycle"/>
    /// and <see cref="EngineProvisioner"/> ask the same question of their <see cref="IWsl"/> seam
    /// instead, deliberately: both are constructed with one so a test can answer them, and neither
    /// is on a path where the cost matters.</para>
    ///
    /// <para>False is the honest answer where the key cannot be read. Reporting an engine that is
    /// not there sends the user back to the state this exists to remove; reporting none where one
    /// exists costs a start attempt that says so.</para>
    /// </remarks>
    public bool DistributionRegistered =>
        Wsl.RegisteredDistributions()
            .Any(name => name.Equals(DistributionName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Verified artefacts, kept so a repeated install does not download again.</summary>
    public string Downloads => Path.Combine(Root, "downloads");

    /// <summary>Where the distribution's virtual disk is imported to.</summary>
    public string Distribution => Path.Combine(Root, "distro");

    /// <summary>
    /// The directory that goes on the user's PATH — the one thing an installer needs from here, and
    /// putting it there is the installer's job, not this one's.
    /// </summary>
    /// <remarks>
    /// Since DD141 this holds the forwarder rather than the vendor's CLI, and the name still means
    /// what it always did: the directory whose contents are the commands this install owns.
    /// </remarks>
    public string CliDirectory => Path.Combine(Root, "bin");

    /// <summary>
    /// Where the vendor's CLI lands, which is deliberately not the directory on PATH (DD141).
    /// </summary>
    /// <remarks>
    /// The two cannot share a directory, and that is a rule of Windows rather than a preference:
    /// PATHEXT resolves <c>.EXE</c> before everything else, so any forwarder placed beside
    /// <c>docker.exe</c> is a file nothing would ever run. Moving the vendor's copy one directory
    /// across is what makes room for a <c>docker</c> of this project's own.
    /// </remarks>
    public string VendorCliDirectory => Path.Combine(Root, "cli");

    /// <summary>The Windows <c>docker</c> CLI.</summary>
    public string DockerCli => Path.Combine(VendorCliDirectory, "docker.exe");

    /// <summary>The <c>docker</c> a shell actually runs: this project's forwarder (DD141).</summary>
    /// <remarks>
    /// Placed by the installer and not by the provision, because it is part of owning the command
    /// rather than part of having an engine — and gated on the same checkbox as the PATH entry,
    /// since a user who declined that keeps whatever <c>docker</c> they already had.
    /// </remarks>
    public string DockerShim => Path.Combine(CliDirectory, "docker.exe");

    /// <summary>
    /// The directory the CLI is pointed at with <c>DOCKER_CONFIG</c>, so it finds this install's
    /// plugins (DD73).
    /// </summary>
    /// <remarks>
    /// The root itself, because <c>DOCKER_CONFIG</c> names a <i>config directory</i> and the CLI
    /// looks for <c>cli-plugins</c> inside it. It is deliberately not the user's own
    /// <c>%USERPROFILE%\.docker</c>: writing there is the thing this project has refused to do since
    /// DD32, and reading it would inherit whatever context a rival installed — which is the whole of
    /// DD20, since a project brought up on somebody else's engine carries a session stamp whose
    /// reclaim then finds nothing.
    /// </remarks>
    public string ConfigDirectory => Root;

    /// <summary>Where a CLI plugin has to sit for <c>docker</c> to find it.</summary>
    /// <remarks>
    /// The name is the CLI's, not this project's: <c>$DOCKER_CONFIG/cli-plugins</c> is where the
    /// Docker CLI looks, so this directory is spelled that way or the plugin is invisible.
    /// </remarks>
    public string PluginsDirectory => Path.Combine(ConfigDirectory, "cli-plugins");

    /// <summary>
    /// The Compose plugin, named the way the CLI derives a subcommand from a filename.
    /// </summary>
    /// <remarks>
    /// <c>docker-compose.exe</c> is what makes the subcommand <c>compose</c>: the CLI takes the
    /// name after <c>docker-</c> and before the extension. Renaming this file renames the verb.
    /// </remarks>
    public string ComposePlugin => Path.Combine(PluginsDirectory, "docker-compose.exe");

    /// <summary>The Buildx plugin, named the same way (DD74).</summary>
    /// <remarks>
    /// Placing this is also what makes plain <c>docker build</c> use BuildKit: the CLI routes the
    /// build to buildx when the plugin is there and falls back to the legacy builder when it is not.
    /// </remarks>
    public string BuildxPlugin => Path.Combine(PluginsDirectory, "docker-buildx.exe");

    /// <summary>
    /// The handful of values the window remembers between runs — where it was, and what was being
    /// read (DD39).
    /// </summary>
    /// <remarks>
    /// In the root rather than under a directory of its own, and not created by <see cref="Create"/>:
    /// it is written when a window closes and its absence is the answer for a first run.
    /// </remarks>
    public string WindowState => Path.Combine(Root, "window.json");

    /// <summary>
    /// The build a second launch is asking the running window to open (DD126).
    /// </summary>
    /// <remarks>
    /// A file because the signal cannot carry one. One tray owns the window, so a
    /// <c>docker-desktop://</c> link opened while it is running has to hand the ref across a process
    /// boundary — and the object that already does the handing is a named event, which carries no
    /// payload. Beside <see cref="WindowState"/> for the same reason: a value written by one run and
    /// read by another, whose absence is a meaningful answer.
    /// </remarks>
    public string PendingBuild => Path.Combine(Root, "open-build.txt");

    /// <summary>What the user has decided about how this tool behaves (DD135).</summary>
    /// <remarks>
    /// Beside <see cref="WindowState"/> and separate from it, because they answer to different
    /// things: that file is written by a window closing and describes one, while this is written by
    /// a menu click and describes the tool. Also not created by <see cref="Create"/> — its absence
    /// is the answer for an install nobody has changed anything on, which is most of them.
    /// </remarks>
    public string Settings => Path.Combine(Root, "settings.json");

    /// <summary>What the engine host saw, kept after the window that showed it is gone (DD137).</summary>
    /// <remarks>
    /// Beside <c>provision.log</c>, which the installer already writes into this same root and for
    /// the same reason: it is the file somebody opens once the thing that produced it has closed.
    /// The host is launched detached and hidden — a console window nobody asked for is not an
    /// improvement — so everything it says goes to a window that was never readable.
    ///
    /// <para>Not created by <see cref="Create"/>. A machine whose engine has never been run has
    /// nothing to say, and an empty log is a file somebody has to decide is empty.</para>
    /// </remarks>
    public string HostLog => Path.Combine(Root, "engine.log");

    /// <summary>
    /// That Windows refused to make this distribution's disk sparse (DD226).
    /// </summary>
    /// <remarks>
    /// <para>A fact about the machine and not a setting, which is why it is here rather than in
    /// <see cref="Settings"/>: nobody chose it, and a user who copied their settings to a new
    /// machine would be carrying somebody else's answer.</para>
    ///
    /// <para>Its absence is the ordinary state and means only that no compaction has been refused
    /// here yet, so it is not created by <see cref="Create"/>. A hand-back that succeeds removes it,
    /// because Windows disabled sparse disks rather than removing them and may enable them again.
    /// </para>
    /// </remarks>
    public string SparseRefusal => Path.Combine(Root, "sparse-refused.txt");

    /// <summary>The file a provision holds open while it rewrites the engine (DD269).</summary>
    /// <remarks>
    /// A path rather than a handle, because the two processes that care about it are not the same
    /// process: <c>--provision</c> holds it and the engine host asks about it, and a lock inside one
    /// of them would be a lock the other cannot see.
    ///
    /// <para>Not created by <see cref="Create"/>, and its contents are never read. What is being
    /// asked is whether anything holds it, so a file left behind by a provision that was killed
    /// answers no, which is the right answer: nothing is writing the binaries any more.</para>
    /// </remarks>
    public string UnpackLock => Path.Combine(Root, "unpack.lock");

    /// <summary>Create every directory this layout names. Idempotent.</summary>
    public void Create()
    {
        Directory.CreateDirectory(Downloads);
        Directory.CreateDirectory(Distribution);
        Directory.CreateDirectory(CliDirectory);
        Directory.CreateDirectory(VendorCliDirectory);
        Directory.CreateDirectory(PluginsDirectory);
    }
}
