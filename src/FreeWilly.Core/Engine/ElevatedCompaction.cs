namespace FreeWilly.Core.Engine;

/// <summary>
/// Compacts the virtual disk with administrator rights, once somebody has asked for it (DD237).
/// </summary>
/// <remarks>
/// <para><b>Offered only where the unelevated route is gone.</b> DD199 weighed <c>diskpart compact
/// vdisk</c> against <c>wsl --manage --set-sparse</c> and chose the second, because the first puts a
/// UAC prompt behind a housekeeping button. Windows has since disabled sparse VHDs over possible
/// data corruption (DD224), which leaves the operation with no unelevated route at all — so the
/// ending that was a wall becomes the offer this class is.</para>
///
/// <para><b>What is not being reversed is the product's own premise.</b> FreeWilly is not an
/// elevated program and this does not make it one. What elevates is one named command, for as long
/// as it takes to run, after a press that asked for it — the same bargain the installer struck for
/// turning the WSL feature on, and the reason a refused prompt here costs nothing at all.</para>
///
/// <para><b>Through <c>cmd</c> rather than straight at <c>diskpart</c>,</b> which buys the one thing
/// an elevated child otherwise cannot give back: its words. Standard handles belong to the elevated
/// process, so nothing here could read them; a redirect written into the command line puts the log
/// in a file this side can open once the child is gone. Without it a failure would be an exit code
/// with no way to say which of the four lines produced it.</para>
/// </remarks>
public sealed class ElevatedCompaction
{
    /// <summary>The step that takes the disk out of use so diskpart can open it.</summary>
    /// <remarks>
    /// <para><b>A shutdown and not a terminate, which DD238 cost a shipped run to learn.</b>
    /// Terminating the distribution unmounts it and leaves the WSL2 utility VM holding the VHD:
    /// measured with the engine stopped and nothing left to revive it, the file was still
    /// unopenable and <c>vmmemWSL</c> was still running. After <c>wsl --shutdown</c> it opened at
    /// once.</para>
    ///
    /// <para><c>--set-sparse</c> never needed this, because the process changing the file is WSL
    /// itself. diskpart needs exclusive access, and only the VM going down gives it.</para>
    /// </remarks>
    public const string TakeItDownStep = "take the disk out of use";

    /// <summary>How often diskpart's log is asked how far it has got (DD243).</summary>
    /// <remarks>
    /// Half a second, which is the page's own redraw interval, so a percentage never waits on this
    /// side longer than it waits on the other. Cheap: a file this size is in the cache, and the
    /// alternative to reading it is a step that says nothing for two and a half minutes.
    /// </remarks>
    public static readonly TimeSpan AskEvery = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// How far diskpart said it had got, out of the log it is writing (DD243).
    /// </summary>
    /// <param name="log">Whatever the log holds so far.</param>
    /// <returns>The last percentage, or <see langword="null"/> where it has not said one.</returns>
    /// <remarks>
    /// <para><b>The number and never the sentence around it.</b> diskpart is translated: this
    /// machine reads "52 por cento concluído" and another reads "52 percent completed". Matching
    /// the words would be a progress bar that works on one desk, which is the mistake
    /// <see cref="DiskCompaction.WindowsWithdrewIt"/> already exists to avoid.</para>
    ///
    /// <para>Split on the carriage return, because that is how a console tool rewrites a line in
    /// place: each update is its own segment, the last one is where it has got to, and the version
    /// banner above them has its digits behind dots where this pattern will not read them.</para>
    /// </remarks>
    public static int? PercentIn(string? log)
    {
        if (string.IsNullOrEmpty(log))
        {
            return null;
        }

        for (var i = log.Length - 1; i >= 0; i--)
        {
            if (log[i] != '\r')
            {
                continue;
            }

            var segment = log.AsSpan(i + 1);
            var digits = 0;
            while (digits < segment.Length && char.IsWhiteSpace(segment[digits]))
            {
                digits++;
            }

            var start = digits;
            while (digits < segment.Length && char.IsAsciiDigit(segment[digits]))
            {
                digits++;
            }

            if (digits > start
                && int.TryParse(segment[start..digits], out var percent)
                && percent is >= 0 and <= 100)
            {
                return percent;
            }
        }

        return null;
    }

    /// <summary>How long Windows is given to actually let go of the file.</summary>
    /// <remarks>
    /// A ceiling on the failing case: the wait ends when the handle does, and on the machine this
    /// was measured on that was immediate. It exists because a shutdown returning is not the same
    /// event as the last handle closing, and the alternative to waiting is diskpart reporting the
    /// race in its own translated words.
    /// </remarks>
    public static readonly TimeSpan ReleaseBudget = TimeSpan.FromSeconds(30);

    /// <summary>The step that runs diskpart.</summary>
    public const string CompactStep = "compact the virtual disk";

    /// <summary>What diskpart is asked to do, one verb per line.</summary>
    /// <remarks>
    /// <para><c>attach vdisk readonly</c> is not decoration. <c>compact vdisk</c> reclaims nothing
    /// from a disk it cannot see the contents of, and read-only is what lets it look without
    /// anything being able to write while it does.</para>
    ///
    /// <para>The path is quoted because <c>{localappdata}</c> is under a user profile and a profile
    /// name with a space in it is ordinary.</para>
    /// </remarks>
    /// <param name="virtualDisk">The <c>ext4.vhdx</c> to compact.</param>
    /// <returns>The script, ready to be written to a file.</returns>
    public static string Script(string virtualDisk)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualDisk);
        return $"""
            select vdisk file="{virtualDisk}"
            attach vdisk readonly
            compact vdisk
            detach vdisk

            """;
    }

    /// <summary>
    /// The running distributions this shutdown would also stop, out of what WSL listed (DD238).
    /// </summary>
    /// <param name="listed">What <c>wsl --list --running --quiet</c> wrote.</param>
    /// <param name="ours">This install's own distribution, which the dialog already accounts for.</param>
    /// <returns>The other names, in the order WSL gave them.</returns>
    /// <remarks>
    /// <para>A pure function over the text, so the parsing is testable without a machine that
    /// happens to have two distributions up. <c>--quiet</c> is what makes it one name per line with
    /// no header to skip and nothing translated to match against.</para>
    ///
    /// <para>Blank lines are dropped rather than trusted. wsl.exe writes UTF-16LE and the decoder
    /// this project settled on under DD191 hands back a trailing newline and, on some builds, a
    /// leading byte-order mark — neither of which is a distribution.</para>
    /// </remarks>
    public static IReadOnlyList<string> OthersRunning(string? listed, string ours) =>
        (listed ?? "")
            .Split('\n')
            .Select(line => line.Trim().Trim('﻿', '\r').Trim())
            .Where(name => name.Length > 0
                && !name.Equals(ours, StringComparison.OrdinalIgnoreCase))
            .ToList();

    private readonly IWsl _wsl;
    private readonly EnginePaths _paths;
    private readonly IElevated _elevated;
    private readonly Func<RepairStep> _stopEngine;
    private readonly TimeSpan _releaseBudget;

    /// <summary>Construct one.</summary>
    /// <param name="wsl">The WSL command.</param>
    /// <param name="paths">Where the distribution and its virtual disk are.</param>
    /// <param name="elevated">How a command is run with administrator rights.</param>
    /// <param name="stopEngine">
    /// Takes the engine down the announced way, so the host does not read this teardown as the
    /// engine dying under a suspend and put it back mid-compaction (DD136, DD207). The same seam
    /// <see cref="DiskCompaction"/> takes, and for the same reason.
    /// </param>
    /// <param name="releaseBudget">
    /// How long Windows is given to let go of the virtual disk after WSL is shut down. Overridden
    /// only by a test, which cannot wait <see cref="ReleaseBudget"/> to watch the failing path.
    /// </param>
    public ElevatedCompaction(
        IWsl wsl,
        EnginePaths paths,
        IElevated elevated,
        Func<RepairStep> stopEngine,
        TimeSpan? releaseBudget = null)
    {
        ArgumentNullException.ThrowIfNull(wsl);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(elevated);
        ArgumentNullException.ThrowIfNull(stopEngine);
        _wsl = wsl;
        _paths = paths;
        _elevated = elevated;
        _stopEngine = stopEngine;
        _releaseBudget = releaseBudget ?? ReleaseBudget;
    }

    /// <summary>Where the virtual disk sits on the Windows volume.</summary>
    public string VirtualDiskPath => Path.Combine(_paths.Distribution, "ext4.vhdx");

    /// <summary>Where diskpart's own words are kept afterwards.</summary>
    public string LogPath => Path.Combine(_paths.Root, "diskpart.log");

    /// <summary>Run it.</summary>
    /// <param name="report">Called with each step as it lands.</param>
    /// <param name="saying">
    /// Called as diskpart reports how far in it is (DD243). Beside the steps rather than among
    /// them: a percentage is not a step, and the two above read steps by name.
    /// </param>
    /// <returns>What was done, and what the disk was either side of it.</returns>
    /// <remarks>
    /// No prune and no <c>fstrim</c>. This is offered at the end of a compaction that has just run
    /// both and been refused only at the hand-back, so repeating them would be two more minutes of
    /// work whose result is already on the disk.
    /// </remarks>
    public CompactionOutcome Run(
        Action<RepairStep>? report = null, Action<string>? saying = null) =>
        DiskCompaction.Walk(
            _wsl,
            _paths,
            report,

            // Nothing. This is offered at the end of a compaction that has just pruned and trimmed
            // and been refused only at the hand-back, so repeating them would be minutes of work
            // whose result is already on the disk.
            preparing: [],
            stopEngine: _stopEngine,
            takeDown: TakeItDown,
            act: () => Compact(saying));

    private RepairStep TakeItDown()
    {
        var down = _wsl.Run(WslBudget.Work, "--shutdown");
        if (!down.Succeeded)
        {
            return new RepairStep(
                TakeItDownStep,
                false,
                "WSL would not shut down, and diskpart will not compact a disk that is in use: "
                + Said(down));
        }

        // Asked of the file rather than assumed from the command returning (DD238). The two are
        // different events, and the gap between them used to surface as diskpart's own translated
        // complaint about a file it does not name.
        return Released(VirtualDiskPath, _releaseBudget)
            ? new RepairStep(
                TakeItDownStep,
                true,
                "WSL is shut down and Windows has released the disk, so diskpart can open it")
            : new RepairStep(
                TakeItDownStep,
                false,
                $"WSL shut down, but Windows still had the virtual disk open "
                + $"{_releaseBudget.TotalSeconds:0} seconds later, so nothing was compacted");
    }

    /// <summary>Wait until nothing holds <paramref name="path"/>, or the budget runs out.</summary>
    /// <param name="path">The virtual disk.</param>
    /// <param name="budget">How long to wait.</param>
    /// <returns><see langword="true"/> where it can be opened with no sharing.</returns>
    /// <remarks>
    /// <see cref="FileShare.None"/> is the question diskpart is about to ask, asked the same way and
    /// early enough to answer it in this tool's own words. A file that is not there at all counts as
    /// released: there is nothing to wait for, and the step after this one is what says so.
    /// </remarks>
    private static bool Released(string path, TimeSpan budget)
    {
        var until = DateTime.UtcNow + budget;
        while (true)
        {
            if (!File.Exists(path))
            {
                return true;
            }

            try
            {
                using var held = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return true;
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= until)
                {
                    return false;
                }

                Thread.Sleep(250);
            }
        }
    }

    /// <summary>Ask diskpart, elevated, and read back what it said.</summary>
    /// <param name="saying">Told how far in it is, as diskpart says so (DD243).</param>
    private RepairStep Compact(Action<string>? saying)
    {
        string script;
        try
        {
            Directory.CreateDirectory(_paths.Root);
            script = Path.Combine(_paths.Root, "compact.diskpart");
            File.WriteAllText(script, Script(VirtualDiskPath));

            // The last run's log, gone before this one starts. Left in place it would be read as
            // this run's progress and would say a hundred per cent before diskpart had opened
            // anything — the one reading that must never be wrong.
            File.Delete(LogPath);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return new RepairStep(
                CompactStep, false, $"the diskpart script could not be written: {exception.Message}");
        }

        // Quoted the way cmd wants a command line that itself contains quotes: the outer pair is
        // what /c strips, so both paths inside keep theirs.
        var run = Watched(
            () => _elevated.Run(
                "cmd.exe", $"/c \"\"{Diskpart}\" /s \"{script}\" > \"{LogPath}\" 2>&1\""),
            saying);

        if (run.Refused)
        {
            // Not a failure and deliberately not worded as one. Somebody was asked and said no,
            // which is the whole point of asking rather than running elevated all the time.
            return new RepairStep(
                CompactStep,
                false,
                "administrator rights were declined, so nothing was compacted and nothing was "
                + "changed");
        }

        if (!run.Ran)
        {
            return new RepairStep(
                CompactStep,
                false,
                $"diskpart could not be started with administrator rights: "
                + (run.Failure ?? "no reason was given"));
        }

        return run.Succeeded
            ? new RepairStep(
                CompactStep, true, $"diskpart compacted the virtual disk; its log is {LogPath}")
            : new RepairStep(
                CompactStep,
                false,
                $"diskpart exited {run.ExitCode}: {Complaint()}");
    }

    /// <summary>
    /// Run the elevated call, following its log while it goes (DD243).
    /// </summary>
    /// <param name="run">The elevated call, which blocks until the child is gone.</param>
    /// <param name="saying">Told each percentage as diskpart reaches it, or null.</param>
    /// <returns>What the elevated call returned.</returns>
    /// <remarks>
    /// <para>The call is put on a thread so this one can read, which is the only way round the fact
    /// that an elevated child's handles cannot be redirected into this process. What it writes goes
    /// to a file either way, for the failing case; this reads the same file while it is still being
    /// written, which was measured to work before it was built.</para>
    ///
    /// <para><b>Only when the number changes.</b> diskpart repeats the same percentage for many
    /// updates in a row, so reporting every read would be a hundred lines saying fifty-two.</para>
    ///
    /// <para>Progress is not a step and never becomes one. <see cref="CompactionOutcome.Succeeded"/>
    /// and <see cref="CompactionOutcome.Failure"/> read steps by name, and DD244 is what a step that
    /// meant something other than what it said already cost.</para>
    /// </remarks>
    private ElevatedRun Watched(Func<ElevatedRun> run, Action<string>? saying)
    {
        var running = Task.Run(run);
        if (saying is null)
        {
            return running.GetAwaiter().GetResult();
        }

        int? last = null;
        while (!running.Wait(AskEvery))
        {
            if (SoFar() is not { } percent || percent == last)
            {
                continue;
            }

            // The leading hundreds are not this step's. Measured against a real log: `select vdisk`
            // and `attach vdisk readonly` each report a completion of their own before `compact`
            // starts at zero, so a reader who was told the first number they saw would watch the
            // bar hit a hundred and then fall to nothing. Silence until it starts climbing is the
            // honest reading, and a compact that finished between two polls has nothing to show
            // anyway.
            if (last is null && percent == 100)
            {
                continue;
            }

            last = percent;
            saying($"diskpart is {percent} per cent through the disk");
        }

        return running.GetAwaiter().GetResult();
    }

    /// <summary>How far the log says diskpart has got, or null.</summary>
    /// <remarks>
    /// <see cref="FileShare.ReadWrite"/>, because the writer still has it open and anything stricter
    /// would fail every read. A read that cannot be taken is silence rather than a failure: this is
    /// a progress line, and losing one costs nothing the run depends on.
    /// </remarks>
    private int? SoFar()
    {
        try
        {
            using var reading = new FileStream(
                LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var text = new StreamReader(reading);
            return PercentIn(text.ReadToEnd());
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Where diskpart lives, by full path.</summary>
    /// <remarks>
    /// Under System32 by name rather than trusted to the PATH, which is the rule this project
    /// already applies to elevated commands: what is about to run with administrator rights is not
    /// a name a directory earlier in somebody's PATH gets to answer for.
    /// </remarks>
    private static string Diskpart => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "diskpart.exe");

    /// <summary>What diskpart wrote, on one line, or a pointer to where it is.</summary>
    private string Complaint()
    {
        try
        {
            var said = File.ReadAllText(LogPath).Trim().ReplaceLineEndings(" ");
            return said.Length > 0 ? said : $"it wrote nothing to {LogPath}";
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            return $"and its log could not be read from {LogPath}";
        }
    }

    private static string Said(WslResult result) =>
        result.Detail();
}
