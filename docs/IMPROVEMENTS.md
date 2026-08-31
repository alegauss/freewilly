# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

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
