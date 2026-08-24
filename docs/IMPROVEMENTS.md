# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD173 A ping that fails should say where it failed

The 24 August incident is the case for this. The host wrote `the daemon is running and
no answer within 3s — 6 polls in a row`, restarted, and had the engine back ten seconds
later; the daemon's own log showed it alive and processing a signal half a minute after
the pipe had gone quiet. So the daemon was not the fault — and the journal could not say
which part of the path to it was.

`EnginePing.AskAsync` catches the cancellation once, around a block that connects,
writes and reads. All three deadlines produce the same sentence. They are not the same
failure: a connection that never opens is the relay and its `wsl.exe`/socat hop, which
is the load story DD133 is about, while a connection that opens and then says nothing is
the daemon. The two send a reader to opposite ends of the machine, and today the file
picks neither.

Carrying the stage into the detail costs a local variable, and changes nothing about the
budget, the verdict, or what the supervisor does with it. It is the smallest change that
lets an incident be classified from the journal alone rather than by shelling into the
distribution afterwards to read a log the restart is about to destroy — which is what
this incident actually took.

### §DD174 When the silence started is worth a line

`Supervise` writes only where the watch has already decided, which is the rule that
keeps this file worth opening: a quiet engine writes nothing. The cost is that the six
polls behind `— 6 polls in a row` are invisible. At two seconds between polls and three
for each ping, that verdict is reached anywhere from twelve to thirty seconds after the
engine actually stopped answering, and on 24 August that was the whole difference
between an engine that went quiet while idle and one that went quiet under load — a
question the journal could not settle either way.

One line on the crossing into silence fixes it, and only the crossing: the first poll
after a run of good ones, then nothing until the verdict or the recovery. An engine that
answers still writes nothing, an incident costs one extra line, and a reader gets both
ends of the gap instead of the far end alone.

The pairing with DD173 is the point. DD173 says what broke; this says when. Neither is
worth much by itself, and together they turn a restart line into an account of an
incident.

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
