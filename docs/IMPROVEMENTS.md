# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD207 The stop that was not announced

`--stop` announces itself through `SingleEngine.TellTheLiveOneToStop` before it tears
anything down, and DD136 is why: the host puts back an engine it loses, so a teardown it
was not told about is indistinguishable from WSL2 dying under a suspend.
`FilesystemWork` calls `StopAsync` directly and skips the announcement, so the host does
exactly what it was built to do.

Measured on the first end-to-end run of the check. The engine went down at 14:31:29 and
the host had it back at 14:31:39, mounting the distribution's root read-write nine
seconds into a check that had another two minutes to run. `e2fsck -fn` writes nothing,
so that run was safe by luck of the flag rather than by design.

`--fsck --repair` is the same sequence with `-fy`. That is a write to a filesystem the
kernel has mounted underneath it, which is the one way this tool could destroy the thing
it exists to mend, and it is reachable from a button in the window.

The fix is the line `--stop` already has. What is worth more than the line is why no
test could see it: the fake answers a terminate without a host behind it, so the revival
that makes this dangerous exists only on a machine with the product running. That is the
second defect in this family DD203 found by being run rather than reasoned about.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
