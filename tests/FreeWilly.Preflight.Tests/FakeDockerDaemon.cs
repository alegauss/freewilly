using System.Globalization;
using System.IO.Pipes;
using System.Text;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// A daemon that answers on a real named pipe. The transport under test is the actual one — a
/// pipe, HTTP over it, and .NET's own parser — so what is faked is only what the engine would say.
/// </summary>
/// <remarks>
/// It speaks HTTP/1.1 rather than one-request-per-connection, and that is load-bearing rather than
/// tidy (DD64). A server that closes after every answer hands the client's pool a connection that
/// is already dead, and .NET retries such a request transparently — so the same call arrives twice,
/// is recorded twice, and the count this fake exists to report is one higher than the calls that
/// were made. Keeping the connection open for a body the client can measure removes the close that
/// the retry was racing.
/// </remarks>
internal sealed class FakeDockerDaemon : IAsyncDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly Dictionary<string, byte[]> _routes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _prefixes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TimeSpan> _slow = new(StringComparer.Ordinal);
    private readonly List<string> _requested = [];
    private readonly Lock _guard = new();
    private readonly Task _serving;

    /// <summary>The pipe this is listening on.</summary>
    internal string PipeName { get; } = $"freewilly-api-{Guid.NewGuid():N}";

    /// <summary>Start serving.</summary>
    internal FakeDockerDaemon() => _serving = Task.Run(() => ServeAsync(_stopping.Token));

    /// <summary>Every request line this saw, in order.</summary>
    internal IReadOnlyList<string> Requested
    {
        get
        {
            lock (_guard)
            {
                return [.. _requested];
            }
        }
    }

    /// <summary>Answer <paramref name="path"/> with a 200 carrying <paramref name="json"/>.</summary>
    internal FakeDockerDaemon Json(string path, string json) =>
        Raw(path, Http("200 OK", "application/json", json));

    /// <summary>Answer <paramref name="path"/> with a status and a body.</summary>
    internal FakeDockerDaemon Fails(string path, string status, string body) =>
        Raw(path, Http(status, "text/plain", body));

    /// <summary>
    /// Take <paramref name="taking"/> to answer <paramref name="path"/>, as a busy daemon does.
    /// </summary>
    /// <remarks>
    /// For DD234, which is about a call whose duration is a function of how much the daemon has to
    /// delete rather than of whether it is alive. A prune is not a question, and the only way to
    /// test a budget is against a daemon that spends some of it.
    /// </remarks>
    internal FakeDockerDaemon Takes(string path, TimeSpan taking)
    {
        lock (_guard)
        {
            _slow[path] = taking;
        }

        return this;
    }

    /// <summary>Answer <paramref name="path"/> with exactly these bytes.</summary>
    internal FakeDockerDaemon Raw(string path, string response) =>
        Raw(path, Encoding.UTF8.GetBytes(response));

    /// <summary>
    /// Answer <paramref name="path"/> with bytes no string could carry.
    /// </summary>
    /// <remarks>
    /// A container's log arrives multiplexed, and a frame header is a stream byte and a big-endian
    /// length — arbitrary bytes, which is exactly what a UTF-8 encoder does not round-trip. A fixture
    /// the surface's own frame reader can read has to be handed over as bytes.
    /// </remarks>
    internal FakeDockerDaemon Raw(string path, byte[] response)
    {
        lock (_guard)
        {
            _routes[path] = response;
        }

        return this;
    }

    /// <summary>
    /// Answer anything beginning with <paramref name="prefix"/>, where an exact route does not.
    /// </summary>
    /// <remarks>
    /// For <c>/events?since=..&amp;until=..</c>, whose query carries a clock. A caller that can fix its
    /// own clock should still use <see cref="Json"/> and assert the exact URL; this is for the guards
    /// that drive every registered verb and cannot.
    /// </remarks>
    internal FakeDockerDaemon JsonPrefix(string prefix, string json)
    {
        lock (_guard)
        {
            _prefixes[prefix] = Encoding.UTF8.GetBytes(Http("200 OK", "application/json", json));
        }

        return this;
    }

    /// <remarks>
    /// No <c>Connection: close</c>: the length is what ends this body, so the close would only be a
    /// second, racier way of saying the same thing — see the note on the class.
    /// </remarks>
    private static string Http(string status, string type, string body) =>
        $"HTTP/1.1 {status}\r\nContent-Type: {type}\r\n"
        + $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n"
        + "Api-Version: 1.55\r\n\r\n"
        + body;

    private async Task ServeAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(
                    PipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellation).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException
                or ObjectDisposedException or IOException)
            {
                return;
            }

            _ = AnswerAsync(server, cancellation);
        }
    }

    private async Task AnswerAsync(NamedPipeServerStream client, CancellationToken cancellation)
    {
        try
        {
            var conversation = new Conversation(client);
            while (await conversation.NextAsync(cancellation).ConfigureAwait(false) is { } asked)
            {
                lock (_guard)
                {
                    _requested.Add(asked.Line);
                }

                if (DelayFor(asked.Line) is { } spent)
                {
                    await Task.Delay(spent, cancellation).ConfigureAwait(false);
                }

                var response = ResponseFor(asked.Line);
                await client.WriteAsync(response, cancellation).ConfigureAwait(false);
                await client.FlushAsync(cancellation).ConfigureAwait(false);

                // A body the client can count ends on its own, so the connection stays open and the
                // next request arrives on it. A body delimited by end-of-stream has no other ending
                // — there the close IS the framing, and it is safe at exactly this point because
                // nothing is still draining behind it.
                if (asked.WantsClose || !EndsOnItsOwnCount(response))
                {
                    client.WaitForPipeDrain();
                    return;
                }
            }
        }
        catch (Exception exception) when (exception is IOException
            or ObjectDisposedException or OperationCanceledException)
        {
            // A client that hung up is not this fake failing.
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>How long this path was told to take, or <see langword="null"/> for at once.</summary>
    private TimeSpan? DelayFor(string request)
    {
        lock (_guard)
        {
            return _slow.TryGetValue(PathOf(request), out var spent) ? spent : null;
        }
    }

    /// <summary>The canned answer for a request line, or a 404 naming the path.</summary>
    private byte[] ResponseFor(string request)
    {
        var path = PathOf(request);
        lock (_guard)
        {
            return _routes.TryGetValue(path, out var canned)
                ? canned
                : _prefixes.FirstOrDefault(p =>
                    path.StartsWith(p.Key, StringComparison.Ordinal)).Value
                  ?? Encoding.UTF8.GetBytes(
                      Http("404 Not Found", "text/plain", $"no route for {path}"));
        }
    }

    /// <summary>The path out of a request line, which is what both maps are keyed by.</summary>
    /// <remarks>The line is <c>GET /v1.43/version HTTP/1.1</c>.</remarks>
    private static string PathOf(string request) =>
        request.Split(' ') is [_, var target, ..] ? target : "";

    /// <summary>Whether a response says where its body ends without closing the connection.</summary>
    private static bool EndsOnItsOwnCount(byte[] response)
    {
        // Only the head, and only as ASCII: the body may be frames, which is not text at all.
        var head = Encoding.ASCII.GetString(response, 0, HeadLength(response));
        return !Has(head, "Connection: close")
            && (Has(head, "Content-Length:") || Has(head, "Transfer-Encoding: chunked"));
    }

    private static int HeadLength(byte[] response)
    {
        for (var i = 0; i + 3 < response.Length; i++)
        {
            if (response[i] == (byte)'\r' && response[i + 1] == (byte)'\n'
                && response[i + 2] == (byte)'\r' && response[i + 3] == (byte)'\n')
            {
                return i;
            }
        }

        return response.Length;
    }

    private static bool Has(string head, string header) =>
        head.Contains(header, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        try
        {
            await _serving.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException
            or ObjectDisposedException or IOException)
        {
            // Shutting down.
        }

        _stopping.Dispose();
    }

    /// <summary>One request, as much of it as answering it needs.</summary>
    /// <param name="Line">The request line, which is what the routes are keyed by.</param>
    /// <param name="WantsClose">Whether the caller asked for the connection to end after it.</param>
    private readonly record struct Asked(string Line, bool WantsClose);

    /// <summary>
    /// One connection's bytes, read a whole request at a time.
    /// </summary>
    /// <remarks>
    /// Reading only as far as the blank line was enough while every connection carried one request.
    /// It is not enough now: a POST's body sits behind that line, and left in the pipe it would be
    /// parsed as the request after it.
    /// </remarks>
    private sealed class Conversation(Stream pipe)
    {
        private byte[] _held = new byte[8192];
        private int _count;

        /// <summary>The next request, or <see langword="null"/> once the client hung up.</summary>
        internal async Task<Asked?> NextAsync(CancellationToken cancellation)
        {
            int headEnd;
            while ((headEnd = IndexPastHeaders()) < 0)
            {
                if (!await ReadMoreAsync(cancellation).ConfigureAwait(false))
                {
                    return null;
                }
            }

            var head = Encoding.ASCII.GetString(_held, 0, headEnd);
            Consume(headEnd);

            // A body whose length the sender could not compute arrives in chunks, which is what
            // HttpClient does with JSON content — so the frames have to be read off rather than
            // counted off, and either way none of it may be left for the next request to find.
            var read = head.Contains("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase)
                ? await ConsumeChunkedAsync(cancellation).ConfigureAwait(false)
                : await ConsumeCountedAsync(ContentLength(head), cancellation).ConfigureAwait(false);
            if (!read)
            {
                return null;
            }

            return new Asked(
                head.Split("\r\n")[0],
                head.Contains("Connection: close", StringComparison.OrdinalIgnoreCase));
        }

        private async Task<bool> ConsumeCountedAsync(int length, CancellationToken cancellation)
        {
            while (_count < length)
            {
                if (!await ReadMoreAsync(cancellation).ConfigureAwait(false))
                {
                    return false;
                }
            }

            Consume(length);
            return true;
        }

        private async Task<bool> ConsumeChunkedAsync(CancellationToken cancellation)
        {
            while (true)
            {
                int eol;
                while ((eol = IndexOfLineEnd()) < 0)
                {
                    if (!await ReadMoreAsync(cancellation).ConfigureAwait(false))
                    {
                        return false;
                    }
                }

                // `1a;ext=1` is a legal chunk header, and the size is the part before the marker.
                var size = Encoding.ASCII.GetString(_held, 0, eol).Split(';')[0].Trim();
                if (!int.TryParse(
                    size, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var length))
                {
                    return false;
                }

                // The header's own CRLF, the data, and the CRLF that closes the chunk.
                if (!await ConsumeCountedAsync(eol + 2 + length + 2, cancellation)
                    .ConfigureAwait(false))
                {
                    return false;
                }

                if (length == 0)
                {
                    return true;
                }
            }
        }

        private int IndexOfLineEnd()
        {
            for (var i = 0; i + 1 < _count; i++)
            {
                if (_held[i] == (byte)'\r' && _held[i + 1] == (byte)'\n')
                {
                    return i;
                }
            }

            return -1;
        }

        private int IndexPastHeaders()
        {
            for (var i = 0; i + 3 < _count; i++)
            {
                if (_held[i] == (byte)'\r' && _held[i + 1] == (byte)'\n'
                    && _held[i + 2] == (byte)'\r' && _held[i + 3] == (byte)'\n')
                {
                    return i + 4;
                }
            }

            return -1;
        }

        private async Task<bool> ReadMoreAsync(CancellationToken cancellation)
        {
            if (_count == _held.Length)
            {
                Array.Resize(ref _held, _held.Length * 2);
            }

            var read = await pipe.ReadAsync(_held.AsMemory(_count), cancellation)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            _count += read;
            return true;
        }

        private void Consume(int bytes)
        {
            Buffer.BlockCopy(_held, bytes, _held, 0, _count - bytes);
            _count -= bytes;
        }

        private static int ContentLength(string head)
        {
            const string name = "Content-Length:";
            foreach (var line in head.Split("\r\n"))
            {
                if (line.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(
                        line[name.Length..].Trim(), NumberStyles.None,
                        CultureInfo.InvariantCulture, out var length))
                {
                    return length;
                }
            }

            return 0;
        }
    }
}
