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

### §DD202 The suite that cannot pass where the product is installed

`SingleTrayTests` and `SingleEngineTests` claim the session-local mutexes that hold the
tray and the engine host to one process each. A machine running FreeWilly holds both, so
all fourteen fail together, every run, on the machine most likely to be developing it.

They already know. Each prints that the object it found is the very one the test claims
and that nothing below was asserted, which is the right diagnosis written into the wrong
outcome: a red suite that says "this did not run" is indistinguishable at a glance from
one that says "this is broken", and the habit it teaches is reading past fourteen
failures to find the one that matters.

A skip is the honest marker for it. The condition is knowable before the assertions run
and it is not a defect in anything, so `Assert.Skip` with the same sentence puts the
fact where a reader already looks for it. What must not happen is the tests quietly
passing: the slot being held is exactly what they exist to notice, and a green run that
asserted nothing is worse than a red one that said so.

CI is unaffected either way, since no FreeWilly runs there, which is also why this has
survived: the only person it costs is whoever is working on the product.

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

### §DD197 Six readings a user should not have to take by hand

Diagnosing the 29 August 2026 failure meant reading six sources by hand: `wsl --list
--verbose` for the state of the distribution, `dmesg` out of a second distribution for
the ext4 errors, `blkid` for the device, the Lxss registry key for the path of the vhdx,
a PowerShell query for free space on the Windows volume, and the journal for what the
host had seen. Every one of those is a reading FreeWilly is better placed to take than a
user is.

The Engine page DD165 added is where they belong, because it already carries the journal
and is already the page somebody opens when the engine will not start. What it should
say:

- the WSL version and kernel, and whether the distribution is registered and running
- the root device, its mount options, and whether it is still writable
- the ext4 error counters, and the function that recorded the first one
- the size of the vhdx on the Windows volume beside the space used inside the distribution,
  because those two numbers together are what a question about a full disk actually needs
- the engine: whether the pipe answers, the API version, and the relay figures DD180 exposes

And a copy button, because the point of the page is handing what it says to somebody
else.

Out of scope: the remedy. This page reports, and DD190 owns what to do about what it
reports.

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

### §DD198 The diagnosis an agent can ask for in one call

`read doctor` answers for one container that is not responding. Nothing answers for the
machine underneath it, so an agent asked why the engine will not start has the same six
tools a human has, and has to shell out to `wsl.exe` and parse console output that
arrives in UTF-16 in the language Windows is set to. That is what happened on 29 August
2026.

The reading is the one DD197 renders, and the two surfaces should share one
implementation rather than each asking the machine in its own spelling. `read health`
fits the namespace the agent surface already declares: reads mutate nothing, which is
what lets a single allowlist line cover all of them, and a diagnosis an agent can ask
for in one call is the difference between a session that answers the question and one
that spends its budget rediscovering how to ask.

Budget is what shapes the payload. The surface charges tokens, so what comes back is the
verdict and the readings that support it, never a dmesg dump. A journal tail belongs
behind a flag rather than in the default answer; the error counters, the mount options
and the two disk numbers are small enough to carry every time.

## Block H — The public surface (the site a reader and an agent both read)
