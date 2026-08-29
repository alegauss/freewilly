namespace FreeWilly.Core.Engine;

/// <summary>Bringing a temporary distribution up, and where it came from.</summary>
/// <param name="Step">The step, as the transcript prints it.</param>
/// <param name="FromPrepared">
/// Whether it came from the image kept on this machine rather than from the pinned rootfs. The
/// difference is a network call: a prepared image already carries <c>e2fsprogs</c>.
/// </param>
public sealed record RescueBringUp(RepairStep Step, bool FromPrepared);

/// <summary>
/// The throwaway distribution a check runs from, and the copy of it kept so the next one needs no
/// network (DD216).
/// </summary>
/// <remarks>
/// <para><b>Every check used to fetch its own tools.</b> DD199 imported the pinned Alpine rootfs and
/// ran <c>apk add e2fsprogs</c> into it, which needs a working network at the exact moment something
/// is already wrong. That cost was named and accepted deliberately, in exchange for a rescue that
/// leaves nothing in the user's <c>wsl --list</c>. What has changed is that the rescue is now
/// imported and unregistered cleanly, which makes the third option available: keep the prepared
/// filesystem rather than the registered distribution.</para>
///
/// <para><b>So the first check pays and no later one does.</b> After the tools land, the stopped
/// distribution is exported to a tarball beside the install, and every check after that imports from
/// it. Nothing is registered between runs, nothing is resident, and a machine whose network is part
/// of what is wrong still gets a check.</para>
///
/// <para><b>The file is named after the rootfs it was built from.</b> A manifest that bumps Alpine
/// therefore invalidates the image by not matching its name, rather than by anybody remembering to
/// delete it. A stale one would still run <c>e2fsck</c> perfectly well, which is why this is a
/// tidiness rule and not a correctness one — but a check that quietly ran on last year's userland
/// is not a thing to have to reason about.</para>
///
/// <para>Shared by the check and the drill (DD215) because it is one mechanism used twice, and the
/// second copy of a sequence is the one that goes stale.</para>
/// </remarks>
public sealed class RescueImage
{
    /// <summary>The one call that says whether the tools are really there.</summary>
    /// <remarks>
    /// <c>command -v</c> and not apk's exit code, for the reason DD196 gives: a mirror can succeed
    /// and install nothing useful. It is asked of a prepared image too, because a truncated export
    /// is a file that exists and imports and carries nothing.
    /// </remarks>
    private const string HasTools = "command -v e2fsck && command -v debugfs";

    private readonly IWsl _wsl;
    private readonly EnginePaths _paths;

    /// <summary>Construct the image.</summary>
    /// <param name="wsl">The WSL command.</param>
    /// <param name="paths">Where the install keeps its own files.</param>
    public RescueImage(IWsl wsl, EnginePaths paths)
    {
        ArgumentNullException.ThrowIfNull(wsl);
        ArgumentNullException.ThrowIfNull(paths);
        _wsl = wsl;
        _paths = paths;
    }

    /// <summary>
    /// How every prepared image is named, so the ones this build no longer wants can be found.
    /// </summary>
    /// <remarks>
    /// One pattern, matching what <see cref="PreparedPath"/> writes. The sweep and the write have to
    /// agree about the shape of the name or the sweep quietly stops finding anything, and an install
    /// directory that accumulates eleven megabytes per Alpine bump is exactly what DD223 is about.
    /// </remarks>
    public const string Pattern = "rescue-*.tar";

    /// <summary>Where the prepared image is kept.</summary>
    public string PreparedPath => Path.Combine(
        _paths.Root, $"rescue-{EngineManifest.Current.Rootfs.Sha256[..12]}.tar");

    /// <summary>Whether a check on this machine would need a network.</summary>
    /// <remarks>
    /// What the dialog reads to stop blaming the disk for a wait the fetch is causing (DD216). A
    /// file test and nothing more: asking WSL would be a subprocess on the way to drawing a
    /// paragraph.
    /// </remarks>
    public bool IsPrepared => File.Exists(PreparedPath);

    /// <summary>Bring a temporary distribution up, from the prepared image where there is one.</summary>
    /// <param name="name">What to call it.</param>
    /// <param name="root">Where to import it.</param>
    /// <param name="rootfsPath">The verified Alpine rootfs, for the first run and the fallback.</param>
    /// <param name="what">
    /// What the transcript calls it. The mechanism is one thing and the two callers are not: a drill
    /// reporting that it brought up the rescue would be describing a distribution that is not there.
    /// </param>
    /// <returns>The step, and whether the fetch is still owed.</returns>
    /// <remarks>
    /// The fallback is not decoration. A prepared image can be truncated by a machine that lost
    /// power mid-export, and a check that refused on that would be one this mechanism had broken:
    /// the pinned rootfs is still on disk and still works, so a bad image costs a network call
    /// rather than the run.
    /// </remarks>
    public RescueBringUp Import(string name, string root, string rootfsPath, string what = "rescue")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootfsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(what);

        Directory.CreateDirectory(root);

        // Before anything is imported, because a name that is still registered refuses the import
        // and takes the run with it (DD228).
        var cleared = TakeBackALeftover(name);

        if (IsPrepared)
        {
            var kept = ImportFrom(name, root, PreparedPath);
            if (kept.Succeeded)
            {
                return new RescueBringUp(
                    new RepairStep(
                        $"bring up the {what}",
                        true,
                        $"{name} imported from the image kept here, which carries e2fsck already"
                        + cleared),
                    FromPrepared: true);
            }

            // Thrown away rather than tried again next time. A prepared image that will not import
            // is one a machine that lost power mid-export left behind, and the pinned rootfs beside
            // it still works: a bad image costs a network call rather than the run.
            Discard();
        }

        var fresh = ImportFrom(name, root, rootfsPath);
        return new RescueBringUp(
            fresh.Succeeded
                ? new RepairStep(
                    $"bring up the {what}", true, $"{name} imported into {root}{cleared}")
                : new RepairStep(
                    $"bring up the {what}", false, $"importing {name} failed: {Said(fresh)}"),
            FromPrepared: false);
    }

    /// <summary>
    /// Take back a temporary distribution an interrupted run left registered (DD228).
    /// </summary>
    /// <param name="name">The name this class owns.</param>
    /// <returns>A clause naming what was cleared, or an empty string where there was nothing.</returns>
    /// <remarks>
    /// <para><b>Reproduced by killing a drill part-way through.</b> The teardown is a
    /// <c>finally</c>, which covers every ending the process reaches and none of the ones it does
    /// not: a machine that lost power, a closed terminal, a pipeline that stopped reading. What was
    /// left was a registered distribution and 76 MB of virtual disk, and the next run stopped on its
    /// first step with <c>ERROR_ALREADY_EXISTS</c>.</para>
    ///
    /// <para><b>Recovering is not forcing.</b> These names belong to this tool, they hold nothing a
    /// user created, and the disk under them is scratch. Removing one before importing returns the
    /// machine to the state the previous run promised to leave, which differs in kind from
    /// overwriting something somebody else made. Nothing here would ever name the engine's own
    /// distribution: the callers pass their own constants and a test holds them to it.</para>
    ///
    /// <para>Terminated before it is unregistered, for the reason DD209 cost a machine: WSL accepts
    /// an unregister of a running distribution, moves it to state 4 and blocks the service on
    /// something that never stops. The unregister's own result is the detection — it succeeds where
    /// there was something to take back and fails where the name was free, and neither is an error.
    /// </para>
    /// </remarks>
    private string TakeBackALeftover(string name)
    {
        _wsl.Run(WslBudget.Work, "--terminate", name);
        return _wsl.Run(WslBudget.Work, "--unregister", name).Succeeded
            ? $", after taking back the {name} an interrupted run left registered"
            : "";
    }

    /// <summary>Make sure the tools are in there, fetching them only where they are not.</summary>
    /// <param name="name">The distribution.</param>
    /// <returns>The step.</returns>
    /// <remarks>
    /// Asked before it is fetched, which is the whole saving: on a prepared image this is one
    /// <c>command -v</c> and no network at all. On a first run it is the DD199 fetch, unchanged, and
    /// it still costs what it always cost.
    /// </remarks>
    public RepairStep Tools(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var already = _wsl.Run(
            "-d", name, "-u", "root", "--exec", "/bin/sh", "-c", HasTools);

        if (already.Succeeded)
        {
            return new RepairStep(
                "fetch e2fsprogs", true, $"already in {name}, so nothing is fetched");
        }

        var added = _wsl.Run(
            WslBudget.Work, "-d", name, "-u", "root", "--exec", "/bin/sh", "-c",
            "apk add --no-cache --no-progress e2fsprogs e2fsprogs-extra && " + HasTools);

        return added.Succeeded
            ? new RepairStep("fetch e2fsprogs", true, $"e2fsprogs is in {name}")
            : new RepairStep(
                "fetch e2fsprogs",
                false,
                $"{name} could not fetch e2fsprogs, which needs a network: {Said(added)}");
    }

    /// <summary>Terminate the distribution, keep a copy where one is worth keeping, unregister it.</summary>
    /// <param name="name">The distribution.</param>
    /// <param name="keep">
    /// Whether this run got as far as having the tools, so there is something worth exporting.
    /// </param>
    /// <param name="what">What the transcript calls it, as in <see cref="Import"/>.</param>
    /// <returns>The step.</returns>
    /// <remarks>
    /// <para><b>The terminate is not tidiness, and skipping it wedged a real machine</b> (DD209).
    /// Unregistering a running distribution is not refused: it is accepted, the distribution goes to
    /// state 4 and the WSL service blocks on something that has not stopped, taking every other
    /// distribution on the machine with it.</para>
    ///
    /// <para><b>The export sits between the two, and that placement is the whole of it.</b>
    /// <c>wsl --export</c> of a distribution with a process still inside it is the case nothing here
    /// wants to be the first to find out about, and after the unregister there is nothing left to
    /// export. Written to a temporary name and moved into place, so a run interrupted halfway
    /// through leaves no half-written image for the next check to import.</para>
    /// </remarks>
    public RepairStep PutAway(string name, bool keep, string what = "rescue")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(what);

        _wsl.Run(WslBudget.Work, "--terminate", name);

        var kept = keep && !IsPrepared ? Keep(name) : null;

        var gone = _wsl.Run(WslBudget.Work, "--unregister", name);
        if (!gone.Succeeded)
        {
            return new RepairStep(
                $"put the {what} away",
                false,
                $"{name} is still registered and can be removed with "
                + $"`wsl --unregister {name}`: {Said(gone)}");
        }

        return new RepairStep(
            $"put the {what} away",
            true,
            kept is null
                ? $"{name} unregistered"
                : $"{name} unregistered, and {kept}");
    }

    /// <summary>Export the stopped distribution, atomically.</summary>
    /// <param name="name">The distribution.</param>
    /// <returns>What to say about it, or <see langword="null"/> where it did not work.</returns>
    private string? Keep(string name)
    {
        var half = PreparedPath + ".part";
        try
        {
            File.Delete(half);
            var exported = _wsl.Run(WslBudget.Work, "--export", name, half);
            if (!exported.Succeeded)
            {
                File.Delete(half);
                return null;
            }

            File.Move(half, PreparedPath, overwrite: true);

            // The moment a new one is kept is the moment to drop the others (DD223). Before this,
            // an Alpine bump left the old image on disk forever: the name carries the rootfs digest,
            // so a new manifest stops matching the old file rather than replacing it.
            var dropped = SweepOlderImages();

            return $"kept as {Path.GetFileName(PreparedPath)}, so the next check needs no network"
                + (dropped == 0 ? "" : $", and {dropped} older one(s) went with it");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A cache that could not be written is a slower next check and nothing worse, so it is
            // never allowed to be the thing that fails a repair.
            return null;
        }
    }

    /// <summary>
    /// Drop prepared images this build no longer names (DD223).
    /// </summary>
    /// <returns>How many went.</returns>
    /// <remarks>
    /// <para>Only files matching the shape this class writes, and never the one it just wrote. A
    /// tool that puts eleven megabytes into somebody's profile owes them the sweep as well as the
    /// write, and DD199 already settled the same argument one directory over: it refused to leave a
    /// rescue in a user's <c>wsl --list</c>.</para>
    ///
    /// <para><b>The <c>.tar</c> is checked again rather than trusted to the pattern.</b> Windows
    /// file globbing still carries its 8.3 inheritance, where a three-character extension also
    /// matches longer ones, so <c>rescue-*.tar</c> finds a half-written <c>.tar.part</c> as well.
    /// That file belongs to an export still running, and taking it would be this sweep breaking the
    /// very thing it is tidying up after.</para>
    /// </remarks>
    private int SweepOlderImages()
    {
        var dropped = 0;
        try
        {
            foreach (var stale in Directory.EnumerateFiles(_paths.Root, Pattern))
            {
                if (!stale.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(stale, PreparedPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                File.Delete(stale);
                dropped++;
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // A sweep that could not finish is disk this tool is still holding, and nothing worse.
            // It is never allowed to be the thing that fails a check.
        }

        return dropped;
    }

    /// <summary>Throw away an image that would not import, so the next run rebuilds it.</summary>
    private void Discard()
    {
        try
        {
            File.Delete(PreparedPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left where it is. The next run tries it, fails the same way and falls back the same
            // way, which is slow rather than wrong.
        }
    }

    private WslResult ImportFrom(string name, string root, string from) =>
        _wsl.Run(WslBudget.Work, "--import", name, root, from, "--version", "2");

    private static string Said(WslResult result) =>
        result.Failure ?? result.Output.Trim().ReplaceLineEndings(" ");
}
