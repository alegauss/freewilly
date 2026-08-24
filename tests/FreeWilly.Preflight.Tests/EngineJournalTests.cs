using FreeWilly.Core.Engine;
using FreeWilly.Core.Fixtures;
using FreeWilly.Tray.Ui;
using FreeWilly.Tray.Ui.Pages;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// Reading the engine's journal, and holding it still while it is written to (DD165).
/// </summary>
public sealed class EngineJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"freewilly-journal-{Guid.NewGuid():N}");

    private string Log => Path.Combine(_root, "engine.log");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// A machine whose engine has never been troubled reads as empty, not as a failure (DD137).
    /// </summary>
    /// <remarks>
    /// The absent file is the deliberate answer for a healthy machine, so a reader that threw on it
    /// would make the ordinary case the error case — and the page would show "could not be read" to
    /// somebody whose engine is perfectly fine.
    /// </remarks>
    [Fact]
    public void A_journal_that_was_never_written_reads_as_empty()
    {
        var journal = new EngineJournalFile(Log);

        Assert.Empty(journal.Read());
        Assert.Equal(Log, journal.Path);
    }

    /// <summary>
    /// The reader does not lock out the writers (DD163, DD165).
    /// </summary>
    /// <remarks>
    /// The host appends whenever something happens and the tray appends beside it, and both swallow
    /// the failure. A reader that took the file exclusively would make having this page open the
    /// reason a line went missing — silently, and precisely while somebody is watching for it.
    /// </remarks>
    [Fact]
    public void Reading_the_journal_does_not_stop_it_being_written()
    {
        var log = new EngineHostLog(Log);
        log.Say("host      serving as pid 1");

        var journal = new EngineJournalFile(Log);

        using (var reader = new FileStream(Log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            log.Say("Running   the engine answered");
            Assert.Equal(2, journal.Read().Count);
        }

        log.Say("tray      the engine stopped answering");
        Assert.Equal(3, journal.Read().Count);
    }

    /// <summary>The restart count is what this page exists to put in front of somebody.</summary>
    /// <remarks>
    /// Counted off the words <see cref="EngineRevival.RestartMark"/> names, which is the constant the
    /// host writes with — the two were the same sentence typed in two files before DD165, and a
    /// rewording of one would have made this report no restarts on a machine that had several.
    /// </remarks>
    [Fact]
    public void The_digest_counts_the_restarts_the_host_recorded()
    {
        var digest = JournalDigest.Of(new SampleJournal().Read());

        Assert.Equal(2, digest.Restarts);
        Assert.Equal(17, digest.Lines);
        Assert.Equal("2026-08-21 09:19:41", digest.Since);
        Assert.Equal("2 restarts since 2026-08-21 09:19:41 · 17 lines", digest.Summary());
    }

    /// <summary>The words the digest counts are the words the host writes (DD165).</summary>
    /// <remarks>
    /// The coupling stated as an assertion rather than left in a comment. Nothing fails to compile
    /// when the host's sentence is reworded; the number on the page simply goes wrong, which is
    /// worse than it being absent because a reader trusts it.
    /// </remarks>
    [Fact]
    public void A_restart_the_host_wrote_is_a_restart_the_digest_counts()
    {
        var revival = new EngineRevival();
        revival.Revived();

        var written = revival.BroughtItBack(
            new EngineStatus(EngineState.Running, "the engine answered"));

        Assert.Equal(1, JournalDigest.Of([$"2026-08-21 15:45:41  {written}"]).Restarts);
    }

    [Fact]
    public void An_untroubled_machine_is_told_so_rather_than_shown_a_blank_box()
    {
        Assert.Equal(JournalDigest.Nothing, JournalDigest.Of([]));
        Assert.Equal("nothing recorded", JournalDigest.Nothing.Summary());

        var empty = Assert.IsType<LogEmptyState>(EnginePage.EmptyState(0));
        Assert.Contains("Nothing has happened", empty.Headline, StringComparison.Ordinal);
        Assert.Null(EnginePage.EmptyState(1));
    }

    /// <summary>The stamp is split off by width, so the page can draw it as its own column.</summary>
    [Fact]
    public void A_line_splits_into_the_clock_and_what_happened()
    {
        var line = JournalLine.Of("2026-08-21 14:35:11  tray      the engine stopped answering");

        Assert.Equal("2026-08-21 14:35:11", line.Stamp);
        Assert.Equal("tray      the engine stopped answering", line.Said);
    }

    /// <summary>
    /// Something in the file the writer did not put there goes through whole (DD165).
    /// </summary>
    /// <remarks>
    /// A half-written line, a file somebody edited, an old format. Dropping it would hide the one
    /// thing that is unexpected from the reader who opened this page because something is.
    /// </remarks>
    [Fact]
    public void A_line_with_no_stamp_is_shown_rather_than_dropped()
    {
        var line = JournalLine.Of("not a stamp");

        Assert.Equal(string.Empty, line.Stamp);
        Assert.Equal("not a stamp", line.Said);
    }

    /// <summary>
    /// A file that grew is appended to, not rebuilt, because a rebuild loses the reader's place.
    /// </summary>
    /// <remarks>
    /// The whole reason <see cref="JournalView"/> exists. Asserting the lines are right would pass
    /// for a rebuild as readily as for an append, so the count of rebuilds is what is asserted.
    /// </remarks>
    [Fact]
    public void A_journal_that_grew_is_appended_to_rather_than_rebuilt()
    {
        var view = new JournalView();

        // The first read is an append too — an empty view is a prefix of anything — so a page that
        // opens on a journal already thousands of lines long has never rebuilt.
        Assert.True(view.Update(["a", "b"]));
        Assert.Equal(0, view.Rebuilds);

        Assert.True(view.Update(["a", "b", "c"]));
        Assert.Equal(0, view.Rebuilds);
        Assert.Equal(3, view.Lines.Count);

        // Nothing moved: no work, and nothing for the page to redraw.
        Assert.False(view.Update(["a", "b", "c"]));
        Assert.Equal(0, view.Rebuilds);
    }

    /// <summary>
    /// A file trimmed from the front is rebuilt, because there is nothing to append onto (DD163).
    /// </summary>
    /// <remarks>
    /// <see cref="EngineHostLog"/> keeps the newest 64 KB and drops what is older, so the oldest
    /// line on screen can stop existing between two reads. Treating that as an append is how a page
    /// ends up showing a duplicated tail over content that had already scrolled away — and the trim
    /// can leave the same number of lines, which is why the check is over all of them.
    /// </remarks>
    [Fact]
    public void A_journal_trimmed_from_the_front_is_rebuilt()
    {
        var view = new JournalView();
        view.Update(["a", "b", "c"]);

        // Same length, different middle: exactly what a trim plus an append produces.
        Assert.True(view.Update(["b", "c", "d"]));

        Assert.Equal(1, view.Rebuilds);
        Assert.Equal(["b", "c", "d"], view.Lines.Select(l => l.Said));
    }

    /// <summary>What is copied out is the file, not the page's rendering of it (DD165).</summary>
    /// <remarks>
    /// The next thing somebody does with this is paste it into a bug report. A copy that re-joined
    /// the two columns with this window's own spacing would be a picture of the log rather than the
    /// log, and the reader on the other end cannot tell which they were given.
    /// </remarks>
    [Fact]
    public void Copying_gives_back_the_lines_as_the_file_spells_them()
    {
        var view = new JournalView();
        var lines = new SampleJournal().Read();
        view.Update(lines);

        Assert.Equal(string.Join("\n", lines) + "\n", view.ToText());
    }
}
