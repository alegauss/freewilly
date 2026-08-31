# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

### §DD255 The exit code nothing reads

`read logs --follow --until <line>` exits 1 when the line never arrives, which is what
makes it usable as a gate: a session runs it, reads the code, and branches. That code is
decided in `ReadLogs`, and every test of it stops one layer short. `AgentFollowTests`
drives `AgentSurface.Follow` directly with a stream, so it asserts the match and the
three endings but never an exit code. The surface-level tests assert the refusals, and
the one that does reach the daemon reads an empty body, so `missed` is never true where
it is turned into a return value.

Nothing in between is covered: that `--out` prints its line and *then* the miss, that
the payload still arrives when the pattern did not, that a match exits 0 through the
same path. A session branching on this is branching on the one value no test reads.

The fake can serve it as it stands. `FakeDockerDaemon.Raw(path, byte[])` exists for
framed logs, and a body with neither `Content-Length` nor chunked encoding is
close-delimited, so the pipe closes and the follow ends on the stream rather than on the
deadline. That is the end-to-end shape without teaching the fake to write half a body
and pause.

Acceptance: a follow whose pattern arrives exits 0 and one whose pattern does not exits
1, both driven through `AgentSurface.Read` against the fake daemon, with the payload
asserted alongside the code.

### §DD256 Two endings that arrive as one

`AgentSurface.Follow` ends on the deadline and on Ctrl+C through the same
`OperationCanceledException`, because both cancel the same source. It cannot tell them
apart afterwards, so `ReadLogs` prints the one line it has for a pattern that did not
arrive:

    until   "seed complete" did not arrive in 90s

Press Ctrl+C two seconds in and that is still what it says. The duration is wrong, and
it is wrong in the direction that costs the reader most: a session reading its own
transcript later, or a person scrolling back, is told the run waited a minute and a half
for a line that a human stopped waiting for almost immediately. The exit code is 1
either way, which is right for the deadline and arguably wrong for a deliberate
interrupt.

`DockerApi` already draws this distinction and names the reason: `Budgeted` links the
two sources rather than replacing either, and `Elapsed` tells "the budget ran out" from
"the caller cancelled" after the fact, precisely because both surface as the same
exception. The follow needs the same two sources and the same question asked of them.

What the interrupted case should say is shorter than the deadline's, because the reader
already knows why it stopped: the lines that arrived, the cursor, and that it was
stopped rather than that it timed out.

Acceptance: a follow ended by Ctrl+C says it was stopped and names no duration it did
not wait, and one ended by the deadline still names the deadline.

## Block H — The public surface (the site a reader and an agent both read)
