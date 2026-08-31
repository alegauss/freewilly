# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD263 The connection the figures cannot count

DD262 asserts that a client walking away from a follow takes its channel with it, and
the only way it could was to wrap the backend in a counter of its own. Nothing in the
product can see what that counter sees.

What the product has is DD180's three figures: what the relay accepted, what it stumbled
over, and whether it is still accepting. That argument was for three and no more, and it
holds for the question it answered — the ways the Windows side of the pipe can be why a
client got nothing. A live-channel count answers a different one: what the host still
holds after every client has gone.

The failure it would make visible is a `wsl.exe` per abandoned stream, attached to the
daemon for the rest of the host's life. One is nothing. A tray up for a week, with an
editor reattaching its log view on every reload, is where they add up, and what the user
sees is memory and a distribution that will not terminate — neither of which says relay.

The shape is a count the relay keeps as channels open and close, read the way the
stumbles are. What it must not become is a fourth figure reporting connections now,
which is healthy at any value and therefore says nothing: the reading worth a journal
line is a count that stops coming back down, so the number to keep is the one after the
last client left.

### §DD264 The deadline nothing has run

DD259's teardown waits for the client to take every byte before closing the handle, and
bounds that wait at five seconds because a client that stops reading without hanging up
would otherwise hold a thread for as long as it liked.

DD262 was filed on the assumption that its abandoned follow would drive that wait. It
does not, and this was measured while writing it: killing the client closes its end of
the pipe, so `IsConnected` is already false and the drain is skipped entirely. Every
case in the suite reaches the teardown with a client that has gone.

The client that would drive it is one that connects, sends a request whose response
streams, and then reads nothing while keeping its handle open. That is not a docker
client — it is a `NamedPipeClientStream`, which is what the teardown tests already drive
the relay with, so the seam is there.

What has to hold is that the teardown returns rather than blocking, and that the
connection is closed after it. The cost is the other half: the wait sits on a pool
thread, so what is being asserted is that one stuck client costs one bounded wait and
not an unbounded one.

Worth a task rather than a line in an existing test because the five seconds is a number
nobody has measured against anything. A later change could make the wait unbounded again
and every test in this repository would still pass.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
