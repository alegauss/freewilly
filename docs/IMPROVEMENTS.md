# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD262 The upgraded endpoints the drill leaves out

DD259 broke every connection a client upgraded, and DD260 drives the three the report
named: `compose run`, `start -a` and `exec`. `docker logs --follow`, `docker attach` and
the websocket endpoints upgrade the same way and are asserted by nothing.

They are not more of the same, which is the reason to file them separately. Each of the
three already covered ends because the container exited, so the relay's last act is a
close over a stream nobody is still writing to. A follow ends the other way round: the
client hangs up on a container that is still producing output, and what has to hold is
that the relay treats the client's ending as an ending too — no failure reported, and no
`wsl.exe` left behind holding the channel open. DD259's drain has a deadline in it for
exactly this case and nothing exercises it.

The shape is a container that keeps writing, a `logs -f` against it, and the client
killed once a line has arrived: the exit code a killed client reports is its own, so the
assertion is on the channel, not on the code.

No product change is expected. `LiveEngine.Served` already serves a relay out of the
working tree and the shipped runner already captures an exit code, so this is a test
file and a container per case. If it does turn up a defect, that is a task of its own
rather than a fix hidden inside a test.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
