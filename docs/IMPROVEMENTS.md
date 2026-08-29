# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD228 A run that meets its own wreck can clear it

Reproduced on 29 August 2026 by killing a `--compact-drill` part-way through. The
teardown never ran, so `freewilly-compact-drill` stayed registered with 76 MB of virtual
disk under the install. The next run then stopped on its first step:
`ERROR_ALREADY_EXISTS`, WSL suggesting `--name` to choose a different one.

Three temporary distributions have this shape: the rescue a check runs from, the repair
drill, and the compaction drill. Each is imported under a fixed name and removed in a
`finally`, which covers every ending the process reaches and none of the ones it does
not. A machine that lost power, a closed terminal, a pipeline that stopped reading: the
same wreck.

The refusal is the wrong half to argue with. Importing over a registered distribution
should fail, and DD199 already anticipated somebody finding one of these after a crash:
it named them after this tool so a leftover would be identifiable. What is missing is
the other half. A run that meets its own leftover knows what it is, knows nothing else
owns it, and could take it back.

Recovering is not forcing. These names belong to this tool, they hold nothing a user
created, and the disk under them is scratch. Removing one before importing returns the
machine to the state the previous run promised to leave, which differs in kind from
overwriting something somebody else made.

Worth saying in the step, because one silently reused is a run reporting a clean import
over a disk it did not make.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
