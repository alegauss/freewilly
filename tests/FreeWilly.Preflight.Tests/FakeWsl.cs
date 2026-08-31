using System.Text;
using FreeWilly.Core.Engine;

namespace FreeWilly.Preflight.Tests;

/// <summary>
/// A <c>wsl.exe</c> that records what it was asked and answers what a test says. Importing a
/// distribution cannot be undone inside a test run, so what is asserted is the invocation.
/// </summary>
internal sealed class FakeWsl : IWsl
{
    private readonly Queue<WslResult> _answers = new();

    /// <summary>Every argument list this was called with, in order.</summary>
    internal List<string[]> Invocations { get; } = [];

    /// <summary>The budget each of those calls named, in the same order (DD122).</summary>
    internal List<TimeSpan> Budgets { get; } = [];

    /// <summary>Whatever is left unconsumed answers success with no output.</summary>
    internal WslResult Default { get; set; } = new(0, "", null);

    private readonly List<(Func<string[], bool> When, WslResult Then)> _matched = [];

    /// <summary>Queue the next answer.</summary>
    internal FakeWsl Answer(int? exitCode, string output = "", string? failure = null)
    {
        _answers.Enqueue(new WslResult(exitCode, output, failure));
        return this;
    }

    /// <summary>Answer whichever calls match, whenever they come.</summary>
    /// <param name="when">Which argument lists this answers.</param>
    /// <param name="exitCode">Its exit code.</param>
    /// <param name="output">What it wrote.</param>
    /// <returns>This, so a test reads as one statement.</returns>
    /// <remarks>
    /// Matched rather than queued, because the queue is an order and a lifecycle's calls are not one
    /// a test should have to know: a start asks what is registered, launches, polls, and only then
    /// asks about a log, and a queue makes every one of those a position to get wrong.
    /// </remarks>
    internal FakeWsl AnswerWhen(Func<string[], bool> when, int? exitCode, string output = "")
    {
        _matched.Add((when, new WslResult(exitCode, output, null)));
        return this;
    }

    /// <inheritdoc/>
    public WslResult Run(TimeSpan budget, params string[] arguments)
    {
        Invocations.Add(arguments);
        Budgets.Add(budget);

        foreach (var (when, then) in _matched)
        {
            if (when(arguments))
            {
                return then;
            }
        }

        return _answers.Count > 0 ? _answers.Dequeue() : Default;
    }

    /// <summary>The invocation whose first argument is <paramref name="verb"/>, or null.</summary>
    internal string[]? WithVerb(string verb) =>
        Invocations.FirstOrDefault(argv => argv.Length > 0 && argv[0] == verb);

    /// <summary>The budget the call whose first argument is <paramref name="verb"/> named.</summary>
    /// <param name="verb">The first argument, e.g. <c>--import</c>.</param>
    /// <returns>Its budget, or null where no call opened with that argument.</returns>
    internal TimeSpan? BudgetForVerb(string verb)
    {
        var at = Invocations.FindIndex(argv => argv.Length > 0 && argv[0] == verb);
        return at < 0 ? null : Budgets[at];
    }
}

/// <summary>An <see cref="IArtefactFetcher"/> that writes bytes a test chose.</summary>
internal sealed class FakeFetcher : IArtefactFetcher
{
    private readonly Func<string, byte[]?> _bytesFor;

    /// <summary>Construct a fetcher.</summary>
    /// <param name="bytesFor">
    /// What to write for a URL. Returning null throws instead, which is how a download that failed
    /// rather than one that arrived wrong is injected.
    /// </param>
    internal FakeFetcher(Func<string, byte[]?> bytesFor) => _bytesFor = bytesFor;

    /// <summary>A fetcher that always writes this text.</summary>
    internal static FakeFetcher Writing(string content) =>
        new(_ => Encoding.UTF8.GetBytes(content));

    /// <summary>Every URL it was asked for.</summary>
    internal List<string> Requested { get; } = [];

    /// <inheritdoc/>
    public async Task FetchAsync(string url, string destination, CancellationToken cancellation)
    {
        Requested.Add(url);
        var bytes = _bytesFor(url)
            ?? throw new HttpRequestException($"pretend the network refused {url}");
        await File.WriteAllBytesAsync(destination, bytes, cancellation);
    }
}
