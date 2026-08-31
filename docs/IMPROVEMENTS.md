# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

### §DD257 A follow outliving the container it follows

`ReadLogs` resolves the address once, through `Match`, and hands `container.Id` to
`LogsAsync`. The daemon's stream belongs to that id for as long as it lasts, which is
correct for a `--follow` until something replaces the container, and the address that
exists to name a replaceable thing is `svc:<project>/<service>`.

Compose recreates on `up`, and a recreated container is a new id. The stream the follow
holds belongs to the old one, so it ends, and `--until` reports `did not arrive, and the
log ended`. That is true of the stream and false of the service, which is running,
printing, and about to print the line asked for.

A wrong answer in the same confident form as a right one is the shape this surface
argues against everywhere else: `read verify` probes nothing for a container that is not
running, and the mount row says `unchecked` rather than guessing.

Reasoned from the code rather than measured. The id binding is in `ReadLogs`; that a
recreate changes the id is Docker's. What is unknown is how often a session hits it,
which is the first thing to find out.

The cheap half may be enough: a follow ending on the stream could re-resolve the address
and say `the container was replaced` instead. Re-attaching is the expensive half and
needs its own argument.

Acceptance: a follow whose container is replaced says so, rather than reporting the
ending that belongs to one that simply stopped printing.

## Block H — The public surface (the site a reader and an agent both read)
