namespace FreeWilly.Core.Engine;

/// <summary>What a virtual disk cost at one moment, read both ways (DD221).</summary>
/// <param name="Length">What the file says it is, which a sparse file keeps.</param>
/// <param name="OnDisk">What the volume is actually charging for.</param>
/// <remarks>
/// Both, because handing blocks back to Windows changes one and not the other, and a rehearsal that
/// watched the wrong one would report the mechanism as doing nothing.
/// </remarks>
public sealed record VirtualDiskSize(long? Length, long? OnDisk);

/// <summary>What a compaction rehearsal did, and what the disk cost either side of it (DD221).</summary>
/// <param name="Steps">The steps, in the order they ran.</param>
public sealed record CompactionDrillOutcome(IReadOnlyList<RepairStep> Steps)
{
    /// <summary>What the scratch disk cost after it was filled and emptied.</summary>
    public VirtualDiskSize? Before { get; init; }

    /// <summary>What it cost after the compaction.</summary>
    public VirtualDiskSize? After { get; init; }

    /// <summary>What the compaction itself reported, unchanged.</summary>
    public CompactionOutcome? Compaction { get; init; }

    /// <summary>Whether every step landed.</summary>
    public bool Succeeded => Steps.Count > 0 && Steps.All(step => step.Ok);

    /// <summary>The step that failed, or <see langword="null"/>.</summary>
    public RepairStep? Failure => Steps.FirstOrDefault(step => !step.Ok);

    /// <summary>How much of the volume came back.</summary>
    public long? Reclaimed =>
        Before?.OnDisk is { } before && After?.OnDisk is { } after && after < before
            ? before - after
            : null;

    /// <summary>
    /// Whether the rehearsal proved what it set out to prove.
    /// </summary>
    /// <remarks>
    /// The compaction has to have succeeded and the volume has to have got space back. Either alone
    /// is the outcome this exists to be able to tell apart: a sequence that reported success while
    /// the disk stayed the size it was is the claim nobody had ever checked.
    /// </remarks>
    public bool Rehearsed => Succeeded && Compaction is { Succeeded: true } && Reclaimed is > 0;
}

/// <summary>
/// Runs the compaction against a virtual disk grown and emptied on purpose (DD221).
/// </summary>
/// <remarks>
/// <para><b>DD215 made the argument and this is the other half of it.</b> The compaction prunes the
/// daemon's build cache, trims the filesystem, terminates a distribution and converts its virtual
/// disk to sparse. Every one of those writes, and until this none of them had run outside a fake:
/// the tests queue exit codes and the window was photographed, which between them prove the sequence
/// and prove nothing about what the sequence does.</para>
///
/// <para><b>It drives the shipped <see cref="DiskCompaction"/> and not a copy of it.</b> That is the
/// whole constraint: a rehearsal with its own idea of the order, the flags or the readings would
/// rehearse something that ships nowhere. What it substitutes is only the machine — a scratch
/// distribution with its own <see cref="EnginePaths"/>, so every <c>wsl</c> call the compaction makes
/// names that distribution and never the engine's.</para>
///
/// <para><b>The disk is grown before it is emptied, because an empty one proves nothing.</b> A
/// virtual disk that never held anything has nothing to hand back, and a compaction against it would
/// report success and no bytes, which is indistinguishable from the mechanism not working.</para>
/// </remarks>
public sealed class CompactionDrill
{
    /// <summary>The distribution the rehearsal runs against, and then removes.</summary>
    /// <remarks>
    /// Its own name, distinct from the engine's and from the repair drill's. The compaction
    /// terminates whatever <see cref="EnginePaths.DistributionName"/> says, so the one thing this
    /// class must never get wrong is which distribution that is.
    /// </remarks>
    public const string DrillName = "freewilly-compact-drill";

    /// <summary>How much is written into the scratch disk before it is emptied again.</summary>
    /// <remarks>
    /// Large enough to be unmistakable against the noise of a distribution's own files, and small
    /// enough that writing it is seconds. What is being watched is a virtual disk growing by
    /// something and then giving it back, so the figure only has to be well clear of the tens of
    /// megabytes an Alpine rootfs occupies.
    /// </remarks>
    private const string FillMegabytes = "512";

    /// <summary>Grow the disk, then free it inside the filesystem.</summary>
    /// <remarks>
    /// <c>/dev/zero</c> rather than <c>/dev/urandom</c>: ext4 does not compress, so zeroes allocate
    /// exactly as many blocks and arrive an order of magnitude faster. The <c>sync</c> between the
    /// write and the delete is what makes the growth reach the virtual disk rather than sitting in
    /// the guest's page cache, and the second one is the delete reaching it.
    /// </remarks>
    private const string FillAndEmpty =
        "dd if=/dev/zero of=/fill bs=1M count=" + FillMegabytes + " 2>/dev/null; sync; "
        + "df -k / | awk 'NR==2{print \"full: \" $3 \" KB used\"}'; "
        + "rm -f /fill; sync; "
        + "df -k / | awk 'NR==2{print \"emptied: \" $3 \" KB used\"}'";

    private readonly IWsl _wsl;
    private readonly EnginePaths _install;

    /// <summary>Construct a rehearsal.</summary>
    /// <param name="wsl">The WSL command.</param>
    /// <param name="install">The real install, for the rootfs and the prepared rescue image.</param>
    public CompactionDrill(IWsl wsl, EnginePaths install)
    {
        ArgumentNullException.ThrowIfNull(wsl);
        ArgumentNullException.ThrowIfNull(install);
        _wsl = wsl;
        _install = install;
    }

    /// <summary>
    /// The scratch machine the compaction is pointed at.
    /// </summary>
    /// <remarks>
    /// A root of its own whose <c>distro</c> directory is where the scratch distribution is
    /// imported, because that is where <see cref="DiskCompaction.VirtualDiskPath"/> looks. Building
    /// the paths rather than the path is what lets the shipped class be driven unchanged.
    /// </remarks>
    public EnginePaths Scratch => new(Path.Combine(_install.Root, "compact-drill"), DrillName);

    /// <summary>Grow a virtual disk, empty it, compact it, and report both readings.</summary>
    /// <param name="rootfsPath">The verified Alpine rootfs tarball.</param>
    /// <param name="report">Called with each step as it lands.</param>
    /// <param name="elevated">
    /// Where given, the rehearsal takes the diskpart route instead of the hand-back (DD247). The
    /// caller supplies it because raising a UAC prompt is not something Core does.
    /// </param>
    /// <param name="saying">Called as diskpart says how far in it is, on the elevated route.</param>
    /// <returns>What happened.</returns>
    /// <remarks>
    /// <para><b>The elevated route is rehearsed here because it is the one with the record
    /// (DD247).</b> DD215's argument was that a sequence which has never run against a real virtual
    /// disk is a sequence nobody has tested, and DD221 proved it on the first go. The elevated route
    /// then shipped broken twice, both times found by somebody pressing a button on the disk holding
    /// every image they own.</para>
    ///
    /// <para>What it cannot rehearse is the prompt: UAC is on the secure desktop. What it buys is
    /// that the answer is given over half a gigabyte of deliberately wasted disk rather than over
    /// the real one.</para>
    ///
    /// <para><b>The shutdown's cost does not shrink for being a rehearsal.</b> diskpart needs the
    /// WSL2 utility VM down, so this stops every distribution on the machine exactly as the real
    /// route does, and the verb that offers it has to say so first.</para>
    /// </remarks>
    public CompactionDrillOutcome Run(
        string rootfsPath,
        Action<RepairStep>? report = null,
        IElevated? elevated = null,
        Action<string>? saying = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootfsPath);

        var scratch = Scratch;
        var image = new RescueImage(_wsl, _install);
        var steps = new List<RepairStep>();

        // Through the same image the check uses, so a machine that has run one of these needs no
        // network for the others (DD216). The tools are not needed here, but the import is the same.
        if (!Record(steps, report, image.Import(
            DrillName, scratch.Distribution, rootfsPath, "scratch machine").Step))
        {
            return new CompactionDrillOutcome(steps);
        }

        try
        {
            if (!Record(steps, report, FillItAndEmptyIt()))
            {
                return new CompactionDrillOutcome(steps);
            }

            // Stopped before it is measured. A running distribution is still writing to its virtual
            // disk, so a reading taken beside one is a reading of a moving number.
            _wsl.Run(WslBudget.Work, "--terminate", DrillName);
            var before = Measure(scratch);
            Record(steps, report, new RepairStep("read the scratch disk", true, Describe(before)));

            // The shipped sequence, unchanged, pointed at the scratch machine. The seams are the
            // things a scratch distribution does not have: a daemon holding build cache, and a host
            // that would put an engine back.
            var nothingToTell = new RepairStep(
                FilesystemRepair.StopStep,
                true,
                "nothing is serving the scratch machine, so there is nothing to tell");

            var compaction = elevated is null
                ? new DiskCompaction(
                    _wsl,
                    scratch,
                    () => new RepairStep(
                        DiskCompaction.PruneStep,
                        true,
                        "there is no daemon on the scratch machine, so it holds no build cache"),
                    () => nothingToTell)
                    .Run(step => Record(steps, report, step))
                : new ElevatedCompaction(_wsl, scratch, elevated, () => nothingToTell)
                    .Run(step => Record(steps, report, step), saying);

            var after = Measure(scratch);
            Record(steps, report, new RepairStep("read it again", true, Describe(after)));

            return new CompactionDrillOutcome(steps)
            {
                Before = before,
                After = after,
                Compaction = compaction,
            };
        }
        finally
        {
            // Always, and terminated before it is unregistered (DD209). Never kept: this
            // distribution is half a gigabyte of deliberately wasted disk and is of no use to
            // anything after the reading.
            Record(steps, report, new RescueImage(_wsl, _install)
                .PutAway(DrillName, keep: false, what: "scratch machine"));
        }
    }

    private static bool Record(
        List<RepairStep> steps, Action<RepairStep>? report, RepairStep step)
    {
        steps.Add(step);
        report?.Invoke(step);
        return step.Ok;
    }

    private RepairStep FillItAndEmptyIt()
    {
        var filled = _wsl.Run(
            WslBudget.Work, "-d", DrillName, "-u", "root", "--exec", "/bin/sh", "-c", FillAndEmpty);

        return filled.Succeeded
            ? new RepairStep(
                "waste some disk",
                true,
                $"{FillMegabytes} MB written and deleted: {Said(filled)}")
            : new RepairStep(
                "waste some disk",
                false,
                $"nothing could be written into the scratch machine, so there is nothing for a "
                + $"compaction to hand back: {Said(filled)}");
    }

    /// <summary>Both readings of the scratch virtual disk, at this moment.</summary>
    private static VirtualDiskSize Measure(EnginePaths scratch)
    {
        var vhdx = Path.Combine(scratch.Distribution, "ext4.vhdx");
        long? length = null;
        try
        {
            if (File.Exists(vhdx))
            {
                length = new FileInfo(vhdx).Length;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            length = null;
        }

        return new VirtualDiskSize(length, FileOnDisk.Bytes(vhdx));
    }

    private static string Describe(VirtualDiskSize size) =>
        $"the file says {Size(size.Length)}, the volume is charging for {Size(size.OnDisk)}";

    private static string Size(long? bytes) =>
        bytes is { } value ? MachineReport.Size(value) : MachineReport.Unread;

    private static string Said(WslResult result) =>
        result.Detail("; ");
}
