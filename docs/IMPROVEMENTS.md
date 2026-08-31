# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

### §DD252 A refusal that names the opposite rule

`TryTargetRequest` accepts `:8080/healthz` or a bare `/healthz`. A value that is neither
falls through to the branch that refuses it, and the sentence there is `"{request} is
not a path: it begins with a slash"` — reached only when the value does **not** begin
with a slash.

So `--request healthz` answers "healthz is not a path: it begins with a slash", which
states the false half as a fact about what was typed. Every other refusal on this
surface follows the same shape and gets it right: the clause after the colon says what a
correct value looks like. Here the clause reads as a description of the wrong one.

It costs a round trip and a paragraph of reasoning. The caller reads a sentence that
contradicts what they can see in their own argv, and the cheapest resolution is to try
the value again to find out which half is wrong.

Nothing covers it. `AgentVerifyTests` drives the two-port and the own-port refusals but
never a value that is neither, so the message has no test to have caught it.

Acceptance: `read verify <name> --request healthz` refuses with a sentence that is true
of the value it names, and a test asserts the refusal rather than only the exit code.

### §DD253 The cost of matching a pattern across frames

`AgentSurface.Follow` tests `--until` by calling `LogDigest.Split` over every chunk
collected so far, once per arriving chunk. A match can straddle a frame boundary, so the
whole buffer is the only correct thing to match against, and re-splitting it is the
obvious way to get there.

Under the token ceiling that is fine: the follow stops near the budget, so the buffer
never grows past a few hundred lines and the quadratic term never shows. `--out` is
where it stops being fine. Writing to a file deliberately lifts the ceiling, for the
same reason the plain read does, so the only bounds left are the deadline and the
pattern. A container printing steadily for ninety seconds then has a buffer measured in
megabytes, re-split from byte zero on every chunk, and the follow spends its deadline
splitting rather than reading.

The shape that fits is incremental: keep the per-stream carry `Split` already maintains,
hand each chunk to it, and match only the lines that chunk completed. That is the same
state `Split` builds internally and throws away, so the fix is to expose it rather than
to write a second de-framer.

Acceptance: a follow's cost is linear in what it reads, and `--follow --out --until`
over a stream large enough to show the difference finishes in time proportional to the
bytes.

### §DD254 The two help rows that outgrew the width

`AgentSurface.HelpText` prints one row per verb: two spaces, the verb padded to
eighteen, then its summary. Two rows run past the width a terminal gives them without
folding:

    read context   105 columns
    do reclaim     101 columns

Folded, the tail of a summary lands in column 0, which is where a verb name goes. A
reader scanning the list for the verb they want meets a fragment that looks like one,
and the flag that fell off the end reads as belonging to whatever row follows.

DD251 already built the mechanism, because `read logs` outgrew a line first: a summary
carrying a newline breaks there, and the continuation lands in the same column the first
half started in, so the verb column stays the verb column.
`A_summary_that_breaks_puts_the _second_line_under_the_first` holds it, and holds the
width of both halves. What it does not do is find a row that never declared a break,
which is exactly these two.

The fix is two edits and the guard that stops it recurring: assert the width over every
row rather than only over the rows that already break, so a summary that grows past the
width is refused by the suite instead of by somebody's terminal.

Acceptance: no help row exceeds the width, and a test fails when one does, whether or
not that row declares a break.

## Block H — The public surface (the site a reader and an agent both read)
