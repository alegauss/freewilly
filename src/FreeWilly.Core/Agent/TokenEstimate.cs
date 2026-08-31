namespace FreeWilly.Core.Agent;

/// <summary>
/// What a response costs an agent, in the two units that decide the design.
/// </summary>
/// <param name="Calls">Round trips. The unit an allowlist prompt is charged against.</param>
/// <param name="Tokens">Estimated tokens, by <see cref="TokenEstimate"/>'s stated method.</param>
public readonly record struct AgentCost(int Calls, int Tokens)
{
    /// <summary>Add two costs.</summary>
    /// <param name="left">One.</param>
    /// <param name="right">The other.</param>
    /// <returns>The sum of both units.</returns>
    public static AgentCost operator +(AgentCost left, AgentCost right) =>
        new(left.Calls + right.Calls, left.Tokens + right.Tokens);

    /// <summary>Add two costs, for callers that cannot use the operator.</summary>
    /// <param name="left">One.</param>
    /// <param name="right">The other.</param>
    /// <returns>The sum of both units.</returns>
    public static AgentCost Add(AgentCost left, AgentCost right) => left + right;
}

/// <summary>
/// Estimates what a payload costs to read, so a design argued in tokens can be checked in tokens.
/// </summary>
/// <remarks>
/// DD23. Every figure in the constitution's accounting table is an estimate — 30–60k tokens and 15–30
/// calls for the canonical diagnosis, against a target of 2–5k and five — and an estimate is what a
/// design is argued from rather than what a build can refuse. This is the unit those figures get
/// replaced with.
///
/// <para><b>The method, stated because it is an approximation.</b> One token per four characters. That
/// is the ratio commonly quoted for English prose and JSON in byte-pair vocabularies, and it is wrong
/// in both directions: dense punctuation and long identifiers cost more per character, repeated
/// structure costs less. It is used anyway, for one reason — it needs no tokenizer, so it is the same
/// number on every machine and in CI, and a ratio between two payloads measured the same wrong way is
/// still a ratio. Nothing here should be read as a token count from a model's own tokenizer.</para>
///
/// <para>Deliberately not wall-clock. That is a different question with a different answer, and a suite
/// that mixes them is one where neither number is trusted.</para>
/// </remarks>
public static class TokenEstimate
{
    /// <summary>Characters per estimated token.</summary>
    /// <remarks>
    /// Public because the number is the method: a reader checking a budget should not have to guess
    /// what divided what.
    /// </remarks>
    public const int CharactersPerToken = 4;

    /// <summary>Estimate what reading <paramref name="payload"/> costs.</summary>
    /// <param name="payload">The response body, as the agent would receive it.</param>
    /// <returns>The estimate, in tokens, rounded up.</returns>
    public static int Of(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return 0;
        }

        // Rounded up, so a payload that exists never costs nothing.
        return (payload.Length + CharactersPerToken - 1) / CharactersPerToken;
    }

    /// <summary>Estimate what a payload of this many characters costs.</summary>
    /// <remarks>
    /// For a caller counting a payload as it grows rather than measuring one it already holds, so the
    /// arithmetic and its rounding stay in one place (DD253).
    /// </remarks>
    /// <param name="characters">How many characters the payload holds.</param>
    /// <returns>The estimate, in tokens, rounded up.</returns>
    public static int OfCharacters(int characters)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(characters);

        return (characters + CharactersPerToken - 1) / CharactersPerToken;
    }

    /// <summary>Estimate what reading every one of <paramref name="payloads"/> costs.</summary>
    /// <remarks>Not an overload of <see cref="Of(string?)"/>: a collection expression cannot choose between them.</remarks>
    /// <param name="payloads">The response bodies, one per round trip.</param>
    /// <returns>One call per payload, and the tokens of all of them.</returns>
    public static AgentCost OfAll(IEnumerable<string> payloads)
    {
        ArgumentNullException.ThrowIfNull(payloads);

        var calls = 0;
        var tokens = 0;
        foreach (var payload in payloads)
        {
            calls++;
            tokens += Of(payload);
        }

        return new AgentCost(calls, tokens);
    }
}
