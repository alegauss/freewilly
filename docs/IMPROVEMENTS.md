# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD175 Say what was actually checked

`StatusAsync` reports `the daemon is running and …` on the strength of `_daemon.Alive`,
which is `_process is { HasExited: false }` over the `wsl.exe` this side launched.
`Supervise` already knows that is not the same claim — its own comment says the handle
stays perfectly alive when the virtual machine has gone under a suspend — and the
sentence written to the journal does not carry the distinction. A reader believes it,
because there is nothing in the line to suggest it was inferred.

The cheap half is wording: name the launcher rather than the daemon, so the line stops
asserting a fact nothing established. The better half is to establish it. A poll that
has already found silence can ask the distribution once whether dockerd's pid is still
there, and that single answer splits the two worlds a reader is trying to tell apart: a
daemon that died with nothing to say, and a daemon that is perfectly fine behind a path
that broke.

The cost is a `wsl` call, which is exactly what DD134 took out of every poll for being
part of the load that timed the ping out in the first place. So it belongs on the
failing poll alone — the one that is about to be written down anyway — and never on an
ordinary turn of the loop.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
