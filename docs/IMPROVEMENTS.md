# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD231 The pipe is the one signal that does not know its own engine

Measured on 29 August 2026 by running `--preflight` from the Release build on a machine
with this tool's own engine up. The row reads: `[FAIL] Container engine, already
installed: an unidentified engine`, evidence `\\.\pipe\docker_engine is open`, remedy
`Uninstall it first`. It is telling the user to uninstall FreeWilly before installing
FreeWilly, and `CanHostEngine` goes false with it, so `--provision` refuses.

`RivalEngineProbe` already guards this twice. Signal 1 skips a `docker` resolving inside
this install's own CLI directory, and signal 2 removes this project's own distribution
by name, with a remark saying why: a probe reporting it "would have told a user to
uninstall the thing they were running, on the one row that must never be wrongly red".
Signal 4, the pipe, has no such exclusion, and it is the one firing here.

What it needs is a way to know the pipe is this tool's, and there are two candidates.
Asking Windows which process serves it is exact and costs opening the pipe as a client,
which takes an instance from a running engine. Inferring it from a FreeWilly process
being up beside a registered distribution costs nothing and is a narrow guess: one
server owns that name, so an engine of ours that answers means the pipe is ours.

The direction of the risk decides it. A rival mistaken for us is a green row clearing an
install into the collision DD16 exists to prevent, which is worse than a wrong red.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
