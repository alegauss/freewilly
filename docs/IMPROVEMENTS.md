# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD266 The words the launcher did not keep

DD162 split the account of a dead daemon in two: where the launcher exited with
something to say, say it and do not mention the daemon's log, because sending a reader
to a second file when the first already named the cause is how an earlier version wasted
an hour. Where it went quietly, name the log.

The split is drawn on the wrong thing. It asks whether `wsl.exe` wrote anything, and the
launch command ends in `>>/var/log/dockerd.log 2>&1` — so the shell's own failures are
written into that log by construction and never reach the launcher's streams. An exec
that fails is therefore always silent on stdout and stderr, and always has its sentence
in the file the branch declines to mention.

Measured on 31 August 2026, and it cost the time DD162 exists to save. The journal said
`wsl.exe exited 126 without a word` and the log said `/bin/sh: exec: line 0:
/usr/local/bin/dockerd: Text file busy`, which names the cause exactly. See [[DD265]].

So a non-zero exit with no words should name the log rather than treating silence as
nothing to read. DD162's argument survives intact: it was about not naming a second file
when the first one answered, and here the first one said only a number. The stronger
form is to read the log's tail and put the line in the journal, since the host can
already read inside the distribution, but naming the file is the part that is certainly
right.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
