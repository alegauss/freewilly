namespace FreeWilly.Core.Engine;

/// <summary>What a drill found, at each of the three moments it looked (DD215).</summary>
/// <param name="Steps">The steps, in the order they ran.</param>
public sealed record DrillOutcome(IReadOnlyList<RepairStep> Steps)
{
    /// <summary>What the check said about the dirtied image, before anything was written.</summary>
    public FsckReading? Found { get; init; }

    /// <summary>What the repair said while it was mending it.</summary>
    public FsckReading? Mended { get; init; }

    /// <summary>What a second check said afterwards.</summary>
    public FsckReading? After { get; init; }

    /// <summary>Whether every step landed.</summary>
    public bool Succeeded => Steps.Count > 0 && Steps.All(step => step.Ok);

    /// <summary>The step that failed, or <see langword="null"/>.</summary>
    public RepairStep? Failure => Steps.FirstOrDefault(step => !step.Ok);

    /// <summary>
    /// Whether the rehearsal proved what it set out to prove.
    /// </summary>
    /// <remarks>
    /// Three things, and all three have to hold or the run rehearsed something else: the check had
    /// to find the damage, the repair had to mend it, and the disk had to come back clean. A drill
    /// whose dirtying did not take reports a clean disk at every stage and looks exactly like a
    /// success, which is the one outcome this must not be able to be mistaken for.
    /// </remarks>
    public bool Rehearsed =>
        Succeeded
        && Found is { Clean: false, Step.Ok: true }
        && Mended is { Clean: false, Step.Ok: true }
        && After is { Clean: true };
}

/// <summary>
/// Runs the write path against an ext4 image dirtied on purpose (DD215).
/// </summary>
/// <remarks>
/// <para><b>Everything ever measured here has been a clean disk.</b> The check runs
/// <c>e2fsck -fn</c>, which answers no to every question and writes nothing. The repair runs
/// <c>-fy</c>, which answers yes, and some of those answers discard a damaged inode rather than
/// mending it. That is the path the confirmation warns about, it is the one that can lose somebody's
/// images and volumes, and until this it had never once run against a filesystem with errors on
/// it.</para>
///
/// <para><b>The fakes cover the exit codes and only those.</b> What a queued integer cannot cover is
/// what <c>e2fsck</c> really prints when it finds something, whether the findings shown above the
/// button are legible enough to approve a write on, or whether the repaired ending tells the truth
/// after a run that actually changed a disk. Those are three questions, and a scratch image answers
/// all three at once.</para>
///
/// <para><b>It is not this machine's engine and it must not be.</b> The image is a file inside a
/// distribution imported for the drill and unregistered at the end, so the worst this can do to a
/// user is leave a temporary distribution behind, which the teardown is written to prevent. Nothing
/// here terminates anything the user owns and nothing here needs the engine down.</para>
/// </remarks>
public sealed class RepairDrill
{
    /// <summary>The distribution the drill runs in, and then removes.</summary>
    /// <remarks>
    /// Deliberately not <see cref="FilesystemRepair.RescueName"/>. A drill and a real check must not
    /// be able to collide over one distribution: they can be started a second apart by two people,
    /// and the teardown of either would take the other's tools away mid-run.
    /// </remarks>
    public const string DrillName = "freewilly-drill";

    /// <summary>Where the scratch image is made, inside the drill.</summary>
    public const string ImagePath = "/tmp/scratch.img";

    /// <summary>How large the image is, in one-megabyte blocks.</summary>
    /// <remarks>
    /// Small on purpose. What is being rehearsed is the reading of a damaged filesystem, not how
    /// long <c>e2fsck</c> takes on a real one, and thirty-two megabytes is enough for a full five
    /// passes while keeping the whole drill inside a minute.
    /// </remarks>
    private const int ImageMegabytes = 32;

    /// <summary>
    /// The damage, done through <c>debugfs</c> rather than by writing over bytes.
    /// </summary>
    /// <remarks>
    /// <c>dd</c> over a guessed offset is the obvious way and is the wrong one: where the group
    /// descriptors sit depends on the block size and on the version of <c>mke2fs</c> that made the
    /// image, so a drill built that way corrupts something different on every machine and sometimes
    /// nothing at all. <c>set_super_value</c> names the fields, which is stable across versions.
    ///
    /// <para>Two kinds, and the second one is load-bearing. The counters and the clean flag are the
    /// shape an unclean shutdown really leaves, and the first run of this drill found that they are
    /// <em>not enough</em>: <c>e2fsck -fn</c> printed <c>Free blocks count wrong</c> in full and
    /// exited zero, because the superblock's free counts are recomputed rather than trusted. A drill
    /// built on those alone would rehearse a check that finds nothing and a Repair button that is
    /// never offered, which is the whole path it exists to walk.</para>
    ///
    /// <para>So the reference count on <c>lost+found</c> is broken as well. Inode 11 is that
    /// directory on every filesystem <c>mke2fs</c> makes, so no inode has to be discovered first,
    /// and a wrong <c>links_count</c> is a Pass 4 error rather than a summary difference: it leaves
    /// <c>-fn</c> exiting 4 with the filesystem uncorrected, which is the reading that offers a
    /// repair.</para>
    /// </remarks>
    private const string Damage =
        "debugfs -w -R 'sif <11> links_count 7' " + ImagePath + " >/dev/null 2>&1; "
        + "debugfs -w -R 'ssv free_blocks_count 3' " + ImagePath + " >/dev/null 2>&1; "
        + "debugfs -w -R 'ssv free_inodes_count 3' " + ImagePath + " >/dev/null 2>&1; "
        + "debugfs -w -R 'ssv state 0' " + ImagePath + " >/dev/null 2>&1; "
        + "dumpe2fs -h " + ImagePath + " 2>/dev/null | grep -E 'Free (blocks|inodes):|state:'";

    private readonly IWsl _wsl;
    private readonly EnginePaths _paths;

    /// <summary>Construct a drill.</summary>
    /// <param name="wsl">The WSL command.</param>
    /// <param name="paths">Where the drill's distribution is imported to.</param>
    public RepairDrill(IWsl wsl, EnginePaths paths)
    {
        ArgumentNullException.ThrowIfNull(wsl);
        ArgumentNullException.ThrowIfNull(paths);
        _wsl = wsl;
        _paths = paths;
    }

    /// <summary>Where the drill's distribution is imported to.</summary>
    public string DrillRoot => Path.Combine(_paths.Root, "drill");

    /// <summary>Make a dirty filesystem, read it, mend it, and read it again.</summary>
    /// <param name="rootfsPath">The verified Alpine rootfs tarball.</param>
    /// <param name="report">Called with each step as it lands.</param>
    /// <returns>What happened, and what the tool said at each of the three moments.</returns>
    public DrillOutcome Run(string rootfsPath, Action<RepairStep>? report = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootfsPath);

        var steps = new List<RepairStep>();
        if (!Record(steps, report, Import(rootfsPath)))
        {
            return new DrillOutcome(steps);
        }

        try
        {
            if (!Record(steps, report, InstallTools())
                || !Record(steps, report, MakeTheImage())
                || !Record(steps, report, Dirty()))
            {
                return new DrillOutcome(steps);
            }

            // The three readings, through the same code the product reads an exit code with. A
            // drill with its own idea of what 4 means would agree with itself and with nothing
            // that ships.
            var found = Fsck(write: false);
            Record(steps, report, found.Step);

            var mended = Fsck(write: true);
            Record(steps, report, mended.Step);

            var after = Fsck(write: false);
            Record(steps, report, after.Step with { What = "check again" });

            return new DrillOutcome(steps)
            {
                Found = found,
                Mended = mended,
                After = after,
            };
        }
        finally
        {
            // Always, including after a failure, and terminated before it is unregistered — DD209
            // is what skipping that cost: WSL accepts an unregister of a running distribution, puts
            // it in state 4, and blocks the service on something that never stops.
            Record(steps, report, Remove());
        }
    }

    private static bool Record(
        List<RepairStep> steps, Action<RepairStep>? report, RepairStep step)
    {
        steps.Add(step);
        report?.Invoke(step);
        return step.Ok;
    }

    private RepairStep Import(string rootfsPath)
    {
        Directory.CreateDirectory(DrillRoot);
        var imported = _wsl.Run(
            WslBudget.Work, "--import", DrillName, DrillRoot, rootfsPath, "--version", "2");

        return imported.Succeeded
            ? new RepairStep("bring up the drill", true, $"{DrillName} imported into {DrillRoot}")
            : new RepairStep(
                "bring up the drill", false, $"importing {DrillName} failed: {Said(imported)}");
    }

    /// <summary>Put the tools in, and check they are really there.</summary>
    /// <remarks>
    /// <c>command -v</c> and not apk's exit code, for the reason DD196 gives: a mirror can succeed
    /// and install nothing useful. Both packages, because the drill needs <c>debugfs</c> and
    /// <c>dumpe2fs</c> from the extra one as well as <c>mke2fs</c> and <c>e2fsck</c> from the first.
    /// </remarks>
    private RepairStep InstallTools()
    {
        var added = _wsl.Run(
            WslBudget.Work, "-d", DrillName, "-u", "root", "--exec", "/bin/sh", "-c",
            "apk add --no-cache --no-progress e2fsprogs e2fsprogs-extra "
            + "&& command -v e2fsck && command -v debugfs");

        return added.Succeeded
            ? new RepairStep("fetch e2fsprogs", true, $"e2fsck and debugfs are in {DrillName}")
            : new RepairStep(
                "fetch e2fsprogs",
                false,
                $"{DrillName} could not fetch e2fsprogs, which needs a network: {Said(added)}");
    }

    private RepairStep MakeTheImage()
    {
        var made = _wsl.Run(
            WslBudget.Work, "-d", DrillName, "-u", "root", "--exec", "/bin/sh", "-c",
            $"dd if=/dev/zero of={ImagePath} bs=1M count={ImageMegabytes} 2>/dev/null "
            + $"&& mke2fs -q -F -t ext4 {ImagePath} && e2fsck -fn {ImagePath}");

        return made.Succeeded
            ? new RepairStep(
                "make a scratch disk",
                true,
                $"{ImageMegabytes} MB of ext4 at {ImagePath}, clean before anything is done to it")
            : new RepairStep(
                "make a scratch disk", false, $"the image could not be made: {Said(made)}");
    }

    private RepairStep Dirty()
    {
        var dirtied = _wsl.Run(
            WslBudget.Work, "-d", DrillName, "-u", "root", "--exec", "/bin/sh", "-c", Damage);

        return dirtied.Succeeded
            ? new RepairStep(
                "dirty it on purpose",
                true,
                "counters and the clean flag now disagree with the disk: "
                + Said(dirtied))
            : new RepairStep(
                "dirty it on purpose",
                false,
                $"debugfs would not write to the image, so there is nothing to rehearse on: "
                + Said(dirtied));
    }

    private FsckReading Fsck(bool write) => FsckReading.Of(
        _wsl.Run(
            WslBudget.Work, "-d", DrillName, "-u", "root", "--exec",
            "/bin/sh", "-c", $"e2fsck -f{(write ? 'y' : 'n')} {ImagePath}"),
        write);

    private RepairStep Remove()
    {
        _wsl.Run(WslBudget.Work, "--terminate", DrillName);
        var gone = _wsl.Run(WslBudget.Work, "--unregister", DrillName);
        return gone.Succeeded
            ? new RepairStep("put the drill away", true, $"{DrillName} unregistered")
            : new RepairStep(
                "put the drill away",
                false,
                $"{DrillName} is still registered and can be removed with "
                + $"`wsl --unregister {DrillName}`: {Said(gone)}");
    }

    private static string Said(WslResult result) =>
        result.Failure ?? result.Output.Trim().ReplaceLineEndings(" ");
}
