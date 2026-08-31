# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

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
