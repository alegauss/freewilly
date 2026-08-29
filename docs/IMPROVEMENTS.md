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

### §DD205 The engine this left down

Checking the filesystem needs the root unmounted, so both surfaces stop the engine and
neither starts it. The CLI closes with "Start the engine when you are ready" and the
Engine page with "can be started again", and both are true and neither is a control.

The window is where this reads worst. A user pressed a button on the Engine page,
watched it stop the engine, read that it can be started again, and has to find the
Containers empty state or the tray menu to do it — on a page that is about the engine
and now shows it stopped. The nav strip is two clicks and the tray icon is off-window
entirely.

It is also the moment a start is most wanted and least risky. The filesystem has just
been read or mended, the distribution is terminated, and bringing it back is the check
that the repair worked at all.

What it wants is the action beside the outcome, on the prompt that says the engine is
stopped, doing what the tray's Start engine does. The one thing to be careful of is that
it must not appear where the run failed: an engine started on a filesystem the check
could not finish reading is the state DD190 was filed about.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
