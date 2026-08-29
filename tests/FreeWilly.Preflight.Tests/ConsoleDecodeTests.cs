using System.Text;
using FreeWilly.Core.Preflight.Windows;
using Xunit;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// wsl.exe writes UTF-16LE. Decoded as UTF-8 its output becomes a string with a NUL after every
/// character, which matches no version pattern and reads exactly like "WSL is not installed" — a
/// green machine reported red, on the one row a user cannot argue with.
/// </summary>
public sealed class ConsoleDecodeTests
{
    private const string Sample = "WSL version: 2.6.1.0\r\nKernel version: 6.6.87.2\r\n";

    [Fact]
    public void Utf16_with_a_byte_order_mark_decodes()
    {
        var bytes = Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes(Sample))
            .ToArray();

        Assert.Equal(Sample, ConsoleTool.Decode(bytes));
    }

    [Fact]
    public void Utf16_without_a_byte_order_mark_decodes()
    {
        Assert.Equal(Sample, ConsoleTool.Decode(Encoding.Unicode.GetBytes(Sample)));
    }

    [Fact]
    public void Utf8_decodes()
    {
        Assert.Equal(Sample, ConsoleTool.Decode(Encoding.UTF8.GetBytes(Sample)));
    }

    [Fact]
    public void Utf8_carrying_non_ascii_is_not_mistaken_for_utf16()
    {
        const string localized = "Version du noyau : 6.6.87.2 — installé\r\n";

        Assert.Equal(localized, ConsoleTool.Decode(Encoding.UTF8.GetBytes(localized)));
    }

    [Fact]
    public void Nothing_decodes_to_nothing()
    {
        Assert.Equal("", ConsoleTool.Decode([]));
    }

    [Fact]
    public void Decode_refuses_null() =>
        Assert.Throws<ArgumentNullException>(() => ConsoleTool.Decode(null!));

    // ---- the two streams, in the order the tool wrote them (DD217) --------------------------

    [Fact]
    public void What_a_tool_wrote_to_each_stream_comes_back_in_the_order_it_wrote_it()
    {
        // The defect, in its own shape. e2fsck writes its version banner to stderr before it writes
        // a single pass to stdout, and two buffers appended end to end put that banner underneath
        // the summary line it belongs above — measured on this machine on 29 August 2026.
        var text = ConsoleTool.Interleave(
        [
            ConsoleTool.FromError(Encoding.UTF8.GetBytes("e2fsck 1.47.4 (6-Mar-2025)\n")),
            ConsoleTool.FromOut(Encoding.UTF8.GetBytes("Pass 1: Checking inodes\n")),
            ConsoleTool.FromOut(Encoding.UTF8.GetBytes("Pass 5: Checking group summary\n")),
            ConsoleTool.FromError(Encoding.UTF8.GetBytes("Filesystem still has errors\n")),
            ConsoleTool.FromOut(Encoding.UTF8.GetBytes("8189/8192 files\n")),
        ]);

        Assert.Equal(
            "e2fsck 1.47.4 (6-Mar-2025)\nPass 1: Checking inodes\nPass 5: Checking group summary\n"
            + "Filesystem still has errors\n8189/8192 files\n",
            text);
    }

    [Fact]
    public void A_character_split_across_two_reads_survives()
    {
        // A read returns whatever the pipe had, not whole characters. Decoding each piece on its
        // own would turn a sequence split across two of them into replacement characters, which is
        // a quieter version of the failure DD191 removed.
        var whole = Encoding.UTF8.GetBytes("installé\n");

        var text = ConsoleTool.Interleave(
        [
            ConsoleTool.FromOut(whole[..8]),
            ConsoleTool.FromOut(whole[8..]),
        ]);

        Assert.Equal("installé\n", text);
    }

    [Fact]
    public void Each_stream_chooses_its_own_encoding_from_all_of_what_it_wrote()
    {
        // DD191's guarantee, kept through the interleaving. The heuristic counts zero bytes and a
        // single short read has too few to count, so the whole of a stream decides once — and the
        // two streams decide separately, because nothing says a tool writes both the same way.
        var text = ConsoleTool.Interleave(
        [
            ConsoleTool.FromError(Encoding.Unicode.GetBytes("WSL version: 2.6.1.0\r\n")),
            ConsoleTool.FromOut(Encoding.UTF8.GetBytes("Kernel version: 6.6.87.2\r\n")),
        ]);

        Assert.Equal("WSL version: 2.6.1.0\r\nKernel version: 6.6.87.2\r\n", text);
    }

    [Fact]
    public void A_byte_order_mark_is_the_encoding_talking_and_is_not_part_of_the_text()
    {
        // Dropped once per stream, at its first piece. Left in, it would appear as a zero-width
        // space in the middle of an interleaved transcript rather than harmlessly at the front.
        var text = ConsoleTool.Interleave(
        [
            ConsoleTool.FromOut(Encoding.Unicode.GetPreamble()
                .Concat(Encoding.Unicode.GetBytes("first\r\n")).ToArray()),
            ConsoleTool.FromError(Encoding.Unicode.GetPreamble()
                .Concat(Encoding.Unicode.GetBytes("second\r\n")).ToArray()),
        ]);

        Assert.Equal("first\r\nsecond\r\n", text);
        Assert.DoesNotContain('﻿', text);
    }

    [Fact]
    public void A_real_tool_that_writes_to_one_stream_and_then_the_other_comes_back_in_that_order()
    {
        // Through an actual process, because the ordering above is a decision and this is the
        // pumping that feeds it. The pause is what makes it an assertion rather than a race: the
        // first write is read and recorded long before the second one happens.
        var ran = ConsoleTool.Run(
            TimeSpan.FromSeconds(30),
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            "/c",
            "echo banner 1>&2 & ping -n 3 127.0.0.1 >nul & echo summary");

        Assert.True(ran.Succeeded, ran.Failure ?? ran.Output);

        var banner = ran.Output.IndexOf("banner", StringComparison.Ordinal);
        var summary = ran.Output.IndexOf("summary", StringComparison.Ordinal);

        Assert.True(banner >= 0, $"nothing on stderr reached the output: {ran.Output}");
        Assert.True(summary >= 0, $"nothing on stdout reached the output: {ran.Output}");
        Assert.True(
            banner < summary,
            "what was written to stderr first came back last, which is the two buffers being "
            + "appended rather than interleaved");
    }
}
