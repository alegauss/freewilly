# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD260 The exit code no test asserts

The relay's tests drive requests that carry a Content-Length and assert what came back.
That is the one shape [[DD259]] spares, which is how a defect that breaks every attach
on the machine reaches a release with the suite green.

What has to be asserted is the exit code a real client reports, not the bytes the relay
forwarded. `tests/FreeWilly.Preflight.Tests` already runs `docker compose` against a
live engine in `ComposeUpTests`, so the seam exists: a `compose run` on a service whose
command is `true`, and an `exec` on a running container, each checked for 0 rather than
for output.

The exec case earns its place beside the compose one. It fails after delivering its
payload in full, so a test that only looks at stdout passes while the caller sees a
failure, and that is exactly the gap being closed.

This needs a live engine, which is what the preflight suite is for. It does not belong
in the unit tests, where the relay is talked to over a pipe with no daemon behind it.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
