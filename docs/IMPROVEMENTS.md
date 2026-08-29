# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD196 The tool that has to arrive before the failure

Provisioning installs the engine into the distribution and stops there. `command -v
e2fsck` inside freewilly answers nothing, and so does `dumpe2fs`, so on 29 August 2026
the machine held a corrupt ext4 and no program able to say so or to mend it. The check
had to come from an unrelated Ubuntu that happened to be registered on the same machine.

The timing is the argument. apk needs a writable root and a network, and the moment the
tool is wanted is exactly the moment the root has gone read-only. A package added during
provisioning costs a few megabytes once, while the same package fetched on demand is a
download onto a filesystem that cannot accept writes.

What to install is short and worth pinning rather than resolving: e2fsprogs carries
dumpe2fs and e2fsprogs-extra carries e2fsck and resize2fs. `engine-manifest.json`
already pins four artefacts by digest and the Alpine rootfs is one of them, so a fifth
entry is a shape this project already has.

Detection does not wait on this. /sys/fs/ext4/<device>/errors_count and the mount
options in /proc/mounts both answer with no package installed at all, which is what
DD191 reads. This task is about the remedy DD190 names having something to run.

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

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

### §DD193 The builds page renders a start in the machine's own clock

buildx reports `created_at` in UTC, and `BuildRow.When` renders the value in whatever
offset it arrived with, so the column states a real instant in a zone nobody at this
machine reads it in. The capture that opened this task shows 2026-08-29 12:49 for a
build its operator started at 09:49, three hours behind UTC. `BuildsPage.Fields` prints
`Started` the same way and to the second, so the field the column defers to for the
exact moment is wrong by the same amount.

The offset was deliberate: the doc comment argues that the timestamp's own offset keeps
a window capture identical whichever machine drew it, which is what DD38's fixture buys.
That reasoning holds for the picture and not for the reader. A time is read against the
clock in the corner of the same screen, and one three hours from it is not a time
anybody can act on, while a capture whose only varying field is a time costs less than a
page that is wrong on every machine outside UTC.

So the render converts to local. The invariant culture stays, being about digit shapes
and separators rather than about the zone, and so does the dash the empty cell prints,
that being a glyph in a data column. `BuildRowTests` asserts the literal 2026-03-14
09:10 against a fixture anchored at offset zero, so that expectation is computed through
the same conversion rather than typed. What the conversion then costs a fixture capture
is a line of its own.

### §DD194 A fixture capture stops being the same picture once a time is local

`SampleBuilds.Anchor` is fixed at offset zero and every row is derived from it, so today
the WHEN column draws the same digits whichever machine ran `--fixture`. Once the render
follows the machine's clock, four rows in that capture and the `Started` field beside
them move with the operator's zone, and two people documenting the same build history
produce two different pictures. That is the property DD38 exists to hold: a picture is
comparable between runs only where nothing outside the fixture reaches it.

The fixture is where this is answered, not the render. A render that treats fixture data
differently is a second code path nobody looks at, which is the thing the seam was
introduced to avoid.

Two ways out, and they trade against each other. The capture can pin a zone for the
process it draws in, so the conversion always lands on the offset the fixture states and
the digits in a committed picture never move again. Or the anchor can be built from the
local zone, so the digits are the anchor's whatever machine drew them, which keeps the
fixture honest about what a real machine shows and gives the byte comparison up.

Nothing compares those captures byte for byte today, and the README carries them as
illustrations rather than as evidence, so the choice is open. Whoever takes this line
decides it and says so here, because the reason will not be recoverable from the diff.

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
