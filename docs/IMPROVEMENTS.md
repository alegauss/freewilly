# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD203 The run that needs a machine

DD199 was measured and DD201 was measured, and neither was run. The mechanism holds and
each command now answers on a real distribution, but an import, an `apk add`, a
terminate, a resolve, an `e2fsck` and an unregister have never happened one after
another from this code.

The two defects DD201 found are the argument. Both were invisible to thirteen tests and
to a careful reading, and both took one command against a live distribution to expose.
What is left untried is everything the fake cannot answer for: whether the rescue
imports from the pinned rootfs without a name collision, whether `apk` reaches a mirror
from a distribution that has never resolved a name, whether the hold survives the
terminate in practice as it did in the measurement, and whether the unregister leaves
the directory behind.

Two of those leave state on somebody's machine if they half-happen, which is why this is
not something to find out during an incident. The import and the unregister are the
pair: a rescue left registered is this tool having put something in a `wsl --list` after
saying it would not.

It cannot be an ordinary test, because a build machine has no distribution and the
sequence is minutes long. What it can be is a script run deliberately, the way
`--capture-window` is, that takes the engine down, runs `--fsck`, restores it, and
prints what each step did — with the output kept beside DD199's measurement so the next
reader inherits both.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
