using System.IO.Pipes;
using System.Text;

namespace FreeWilly.Core.Engine;

/// <summary>What asking the pipe produced.</summary>
/// <param name="Answered">Whether the daemon replied with an HTTP success.</param>
/// <param name="ApiVersion">The Engine API version it reported, when it replied.</param>
/// <param name="Detail">What happened, for a report.</param>
public sealed record PingResult(bool Answered, string? ApiVersion, string Detail);

/// <summary>
/// Asks the engine whether it is there, by talking to it.
/// </summary>
/// <remarks>
/// Deliberately not "does the pipe exist". A pipe exists the moment the relay creates its listener,
/// which is before the daemon has finished booting — so existence is exactly the lie this project
/// is trying not to tell. What counts is a reply.
/// </remarks>
public static class EnginePing
{
    /// <summary>The smallest request the Engine API answers.</summary>
    private const string Request = "GET /_ping HTTP/1.1\r\nHost: docker\r\nConnection: close\r\n\r\n";

    /// <summary>Ask the engine on <paramref name="pipeName"/> whether it is answering.</summary>
    /// <param name="pipeName">The pipe to ask.</param>
    /// <param name="timeout">How long to wait for the connection and the reply.</param>
    /// <param name="cancellation">Cancellation.</param>
    /// <returns>What it said.</returns>
    public static async Task<PingResult> AskAsync(
        string pipeName = EnginePipeRelay.DefaultPipeName,
        TimeSpan? timeout = null,
        CancellationToken cancellation = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        var budget = timeout ?? TimeSpan.FromSeconds(3);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(budget);

        // What ran out of budget, in the words the caller will read (DD173). One catch covers a
        // connect, a write and a read, and every one of them used to come back saying "no answer" —
        // which is true of the read and a guess about the other two. They are not the same failure:
        // a connection nothing accepted is the relay and its wsl.exe hop, the load story DD133 is
        // about, while a connection that opened and then went quiet is the daemon behind it. The
        // two send a reader to opposite ends of the machine, so the sentence has to pick one.
        var whatRanOut = "no connection";

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
            whatRanOut = "the request did not go";

            // ASCII and no encoder preamble. A UTF-8 writer puts a byte-order mark in front of the
            // request line, and the daemon answers that with 400 — measured, and confusing enough
            // to be worth spelling out here.
            var bytes = Encoding.ASCII.GetBytes(Request);
            await pipe.WriteAsync(bytes, deadline.Token).ConfigureAwait(false);
            await pipe.FlushAsync(deadline.Token).ConfigureAwait(false);
            whatRanOut = "no answer";

            var buffer = new byte[4096];
            var read = await pipe.ReadAsync(buffer, deadline.Token).ConfigureAwait(false);
            if (read == 0)
            {
                return new PingResult(false, null, "the pipe accepted a connection and said nothing");
            }

            var reply = Encoding.ASCII.GetString(buffer, 0, read);
            return Interpret(reply);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return new PingResult(false, null,
                $"{whatRanOut} within {budget.TotalSeconds:0.#}s");
        }
        catch (Exception exception) when (exception is IOException
            or TimeoutException or UnauthorizedAccessException or ObjectDisposedException)
        {
            return new PingResult(false, null, exception.Message);
        }
    }

    /// <summary>Read a status line and the API version out of a reply.</summary>
    /// <param name="reply">What the daemon wrote.</param>
    /// <returns>The verdict.</returns>
    internal static PingResult Interpret(string reply)
    {
        var lines = reply.Split('\n');
        var status = lines.Length > 0 ? lines[0].Trim() : "";
        var version = lines
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("Api-Version:", StringComparison.OrdinalIgnoreCase))
            ?.Split(':', 2)[1].Trim();

        // Any 2xx is the daemon answering. A 400 is too — it proves something is there — but it
        // means this code sent it something wrong, and calling that "running" would hide a defect.
        var answered = status.Contains(" 200", StringComparison.Ordinal)
            || status.Contains(" 204", StringComparison.Ordinal);

        return new PingResult(answered, version,
            answered ? status : $"the daemon replied: {status}");
    }
}
