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

### §DD200 The buffer that belongs to the machine, not the distribution

DD191 reads the distribution's `dmesg` after a start and reports the four phrases an
ext4 fault produces. Two things about that buffer make the reading wrong, and both were
seen on 29 August 2026 while measuring DD199.

It belongs to the virtual machine and not to a distribution. WSL2 runs one kernel for
all of them, so the same buffer carried lines for sda, sdb, sdd and sde at once. Every
line names its device, which is what the match has to use and does not: a complaint
about the user's Ubuntu is currently reported as the engine's filesystem needing a
check.

And it is a history rather than a state. The buffer held the original incident in full,
naming the bad block bitmap checksum in group 348, on a filesystem that `dumpe2fs -h`
called clean with no recorded errors and that a full `e2fsck -fn` agreed was clean. A
check that reads it announces a fault that was repaired.

The superblock answers what dmesg cannot. `dumpe2fs -h` reports Filesystem state and FS
Error count, both of which are this filesystem's own and both of which a repair clears,
and DD196 already put that binary in the distribution. dmesg is still worth reading for
what the superblock does not carry, which is the mount that has just happened, and the
device name in each line is what makes that half sound.

### §DD201 The sequence nobody has run

DD199's mechanism was measured on a real machine and its code was written afterwards.
Those are two different things, and only the first has met a real `wsl.exe`.

The nine tests drive `FilesystemRepair` through a fake, so what they prove is the
ordering and the decisions: that the hold opens before the terminate, that the disk is
found by UUID rather than by device name, that a read answers no and a write answers
yes, that exit 4 is a finding, and that the rescue is unregistered even after a failure.
None of that is the verb working.

What has never happened is one run of `--fsck` against this machine: an import of the
rescue from the pinned rootfs, an `apk add` into it, a terminate, a `blkid -U` that
resolves, an `e2fsck` that returns, and an unregister that leaves nothing behind. Each
of those is a place the real thing can differ from the fake, and the import and the
unregister are the two that leave state on somebody's machine if they half-happen.

It cannot become an ordinary test. Importing and unregistering a distribution is not
something a suite can do on a build machine, and the failure this guards against is
minutes long. What it can be is a scripted check run deliberately, the way
`--capture-window` is, with its output recorded next to the measurement DD199 already
has.

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
