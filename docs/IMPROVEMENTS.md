# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD199 The repair behind one button, and what it is allowed to write

Repairing the filesystem on 29 August 2026 took four steps nobody should have to
reconstruct: terminate the distribution so its root is unmounted, notice that WSL leaves
the disk attached to the shared utility VM afterwards, run `e2fsck -fy` against it from
a second distribution that happened to be holding that VM open, and read the result. It
fixed bitmap damage in three block groups and lost no file, which is the ordinary
outcome after an unclean shutdown and the reason a button for it is defensible.

Two mechanisms can carry it and the task has to choose between them. A rescue
distribution imported from the Alpine rootfs the manifest already pins needs no
elevation and no new download, but it rests on WSL leaving the disk attached after a
terminate, which is observed behaviour rather than a documented contract. `wsl --mount
--vhd --bare` is documented and deterministic and costs a UAC prompt. Measure the first
before committing to it. The e2fsck binary comes from DD196 either way, since no
distribution here carries one today.

Three things the design settles regardless of the mechanism. A read-only check runs
freely and a repair asks first, because a repair writes to the filesystem holding every
image and volume the user has. The user sees what the check found before being asked to
approve anything. And the engine is down for the duration, so the control belongs where
that state is already on screen.

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
