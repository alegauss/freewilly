using System.Diagnostics;
using System.Text;

namespace FreeWilly.Core.Preflight.Windows;

/// <summary>What running a console tool produced.</summary>
/// <param name="ExitCode">The process exit code, or <see langword="null"/> if it never ran.</param>
/// <param name="Output">
/// Standard output and standard error, decoded and put back in the order the tool wrote them
/// (DD217).
/// </param>
/// <param name="Failure">Why it never ran or never finished, when that is what happened.</param>
internal sealed record ProcessOutput(int? ExitCode, string Output, string? Failure)
{
    /// <summary>Whether the tool ran to completion and said it succeeded.</summary>
    public bool Succeeded => ExitCode == 0;
}

/// <summary>Runs a console tool and decodes what it wrote, whichever encoding it chose.</summary>
internal static class ConsoleTool
{
    /// <summary>How long a preflight probe waits for a tool before giving up on it.</summary>
    /// <remarks>
    /// Short on purpose, and DD122 is why it stayed short. A probe asks a question — <c>wsl
    /// --status</c>, <c>--list</c> — and a machine that has not answered one in fifteen seconds is
    /// not slow, it is stuck; a preflight that waits minutes to say so has stopped being a
    /// preflight. What DD122 changed is that this is no longer the only budget there is.
    /// </remarks>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>Run a tool under the probe budget.</summary>
    /// <param name="fileName">The executable, as a full path.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>The exit code and output, or the reason there is neither.</returns>
    internal static ProcessOutput Run(string fileName, params string[] arguments) =>
        Run(Timeout, fileName, arguments);

    /// <summary>
    /// Run <paramref name="fileName"/> with <paramref name="arguments"/> and return what it wrote.
    /// </summary>
    /// <param name="budget">How long it may take before it is killed and reported as unfinished.</param>
    /// <param name="fileName">The executable, as a full path.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>The exit code and output, or the reason there is neither.</returns>
    /// <remarks>
    /// The budget is a parameter since DD122, because one number cannot serve both callers. It was
    /// <see cref="Timeout"/> for everything, and the provision inherited a budget written for a
    /// question: measured on a clean Windows 11 machine, every artefact downloaded and verified, the
    /// distribution imported, and the step that unpacks the engine inside it killed at fifteen
    /// seconds — because a distribution that has never run boots cold and then untars 85 MB.
    /// </remarks>
    internal static ProcessOutput Run(TimeSpan budget, string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,

            // Named and not inherited (DD261). This runner is the widest one in the product, and
            // every caller passes absolute paths — the distribution's, the downloads' — so nothing
            // here resolves against a directory. Naming one also settles what `wsl.exe` translates
            // its initial directory from, which an inherited UNC path would have made a warning.
            WorkingDirectory = Environment.SystemDirectory,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new ProcessOutput(null, "", $"{fileName} could not be started");
            }

            // Read the raw bytes rather than letting a StreamReader guess: wsl.exe writes UTF-16LE,
            // and decoding that as UTF-8 yields a string with a NUL after every character — which
            // matches no pattern and silently reads as "WSL is not installed" (DD191).
            //
            // Kept as arrival-ordered pieces rather than two buffers since DD217. Two buffers meant
            // everything written to stderr landed after everything written to stdout whatever order
            // the tool emitted them in, and what a reader saw was e2fsck's version banner printed
            // below the summary line it belongs above.
            var pieces = new List<Piece>();
            var reading = Task.WhenAll(
                PumpAsync(process.StandardOutput.BaseStream, Out, pieces),
                PumpAsync(process.StandardError.BaseStream, Error, pieces));

            if (!process.WaitForExit((int)budget.TotalMilliseconds))
            {
                TryKill(process);

                // The budget is in the sentence, so a log tells a slow machine from a stuck one.
                // With one constant it could not: "did not finish within 15 seconds" read the same
                // whether it was a question nothing answered or an unpack that needed a minute.
                return new ProcessOutput(
                    null, "", $"{fileName} did not finish within {Spell(budget)}");
            }

            reading.GetAwaiter().GetResult();
            return new ProcessOutput(process.ExitCode, Interleave(pieces), null);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new ProcessOutput(null, "", $"{fileName}: {exception.Message}");
        }
    }

    /// <summary>How a budget is written into the sentence that says it was exceeded.</summary>
    /// <param name="budget">The budget.</param>
    /// <returns>Seconds under a minute, minutes at or above one.</returns>
    /// <remarks>
    /// "300 seconds" is a number a reader converts before it means anything, and the two budgets
    /// this now has sit on either side of the unit that reads naturally for each.
    /// </remarks>
    internal static string Spell(TimeSpan budget) =>
        budget < TimeSpan.FromMinutes(1)
            ? $"{budget.TotalSeconds:0} seconds"
            : $"{budget.TotalMinutes:0} minutes";

    /// <summary>Which of the two streams a piece came off.</summary>
    private const int Out = 0;

    /// <summary>The other one.</summary>
    private const int Error = 1;

    /// <summary>One read from one stream, in the order the reads returned (DD217).</summary>
    /// <param name="Stream"><see cref="Out"/> or <see cref="Error"/>.</param>
    /// <param name="Bytes">What that read produced, undecoded.</param>
    /// <remarks>
    /// Internal so the ordering can be asserted on directly. Driving it through a real process
    /// proves the pumping and not the decision, and the decision is where a split character or a
    /// mis-read encoding would go wrong.
    /// </remarks>
    internal sealed record Piece(int Stream, byte[] Bytes);

    /// <summary>A piece off standard output.</summary>
    internal static Piece FromOut(byte[] bytes) => new(Out, bytes);

    /// <summary>A piece off standard error.</summary>
    internal static Piece FromError(byte[] bytes) => new(Error, bytes);

    /// <summary>Read one stream until it ends, appending what arrives to the shared list.</summary>
    /// <param name="from">The stream.</param>
    /// <param name="which">Which one it is.</param>
    /// <param name="into">Where the pieces go, in arrival order.</param>
    /// <returns>The work.</returns>
    /// <remarks>
    /// The lock is what makes the list an ordering rather than a race. Two tasks append to it and
    /// the interleaving is the whole point: a piece added under no lock could land beside the wrong
    /// neighbour, which is the defect this replaced said out loud.
    /// </remarks>
    private static async Task PumpAsync(Stream from, int which, List<Piece> into)
    {
        var buffer = new byte[8192];
        while (true)
        {
            var read = await from.ReadAsync(buffer).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            var piece = new Piece(which, buffer[..read]);
            lock (into)
            {
                into.Add(piece);
            }
        }
    }

    /// <summary>Decode the pieces back into one text, in the order they arrived (DD217).</summary>
    /// <param name="pieces">What both streams wrote.</param>
    /// <returns>The text.</returns>
    /// <remarks>
    /// <para><b>The encoding is decided per stream and from all of it</b>, which is what keeps
    /// DD191's guarantee. The heuristic needs enough bytes to count zeroes in, and a first read of
    /// forty characters is not enough to tell UTF-16LE from UTF-8 reliably — so the whole of each
    /// stream is weighed once, and only then is anything turned into text.</para>
    ///
    /// <para><b>A stateful decoder per stream is why a piece may end mid-character.</b> Reads return
    /// whatever the pipe had, not whole characters, and a UTF-8 sequence or a UTF-16 pair split
    /// across two reads would otherwise decode as two replacement characters. The decoder carries
    /// the remainder into the next piece from that same stream.</para>
    /// </remarks>
    internal static string Interleave(List<Piece> pieces)
    {
        lock (pieces)
        {
            var decoders = new Decoder[2];
            var pending = new bool[2];
            for (var stream = 0; stream < decoders.Length; stream++)
            {
                var all = pieces.Where(piece => piece.Stream == stream)
                    .SelectMany(piece => piece.Bytes).ToArray();
                decoders[stream] = EncodingOf(all).GetDecoder();
                pending[stream] = true;
            }

            var text = new StringBuilder();
            foreach (var piece in pieces)
            {
                var bytes = piece.Bytes;
                if (pending[piece.Stream])
                {
                    pending[piece.Stream] = false;

                    // The byte-order mark is the encoding's own announcement and not something the
                    // tool said, so it is dropped here rather than left to appear as a zero-width
                    // space in the middle of an interleaved transcript.
                    if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                    {
                        bytes = bytes[2..];
                    }
                }

                Append(text, decoders[piece.Stream], bytes, flush: false);
            }

            // What a split character left behind. Emitted rather than dropped: a truncated sequence
            // at the end of a stream is still something the tool wrote.
            foreach (var decoder in decoders)
            {
                Append(text, decoder, [], flush: true);
            }

            return text.ToString();
        }
    }

    private static void Append(StringBuilder text, Decoder decoder, byte[] bytes, bool flush)
    {
        var count = decoder.GetCharCount(bytes, 0, bytes.Length, flush);
        if (count == 0)
        {
            return;
        }

        var chars = new char[count];
        var written = decoder.GetChars(bytes, 0, bytes.Length, chars, 0, flush);
        text.Append(chars, 0, written);
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // It exited between the wait and the kill. Nothing to do, and nothing worth saying.
        }
    }

    /// <summary>
    /// Decode console bytes as UTF-16LE or UTF-8, deciding by what is there rather than by what
    /// the tool documents.
    /// </summary>
    /// <param name="bytes">The raw bytes.</param>
    /// <returns>The decoded text.</returns>
    internal static string Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
        {
            return "";
        }

        var start = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE ? 2 : 0;
        return EncodingOf(bytes).GetString(bytes, start, bytes.Length - start);
    }

    /// <summary>
    /// Which encoding a console tool's bytes are in, decided by what is there rather than by what
    /// the tool documents.
    /// </summary>
    /// <param name="bytes">Everything one stream wrote.</param>
    /// <returns>The encoding.</returns>
    /// <remarks>
    /// Split out from <see cref="Decode"/> by DD217, which needs the decision without the decoding:
    /// interleaving two streams means turning bytes into text a piece at a time, and the piece is
    /// far too small to make this call on. So the whole of a stream chooses the encoding once, and
    /// the pieces are decoded through it.
    /// </remarks>
    internal static Encoding EncodingOf(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode;
        }

        // No BOM: UTF-16LE ASCII text puts a zero in every odd byte, which valid UTF-8 never does.
        var pairs = bytes.Length / 2;
        if (pairs == 0)
        {
            return Encoding.UTF8;
        }

        var zeroes = 0;
        for (var i = 1; i < bytes.Length; i += 2)
        {
            if (bytes[i] == 0)
            {
                zeroes++;
            }
        }

        return zeroes * 2 > pairs ? Encoding.Unicode : Encoding.UTF8;
    }
}
