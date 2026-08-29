# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD189 DD189: the containers are killed, never stopped

WslDaemonProcess.Stop calls Kill(entireProcessTree: true) on the launcher, and the class
already records what follows: WSL2 reaps the user processes shortly after the launching
wsl.exe exits. That reaping is a SIGKILL. dockerd never receives a SIGTERM, so it never
runs the shutdown that stops containers, and no container receives one either.

This is not only a shutdown problem. Every teardown this tool performs takes the same
path, the Quit menu item included, so a database container has been killed rather than
stopped on every exit since DD128. A stop signal is the difference between a MariaDB
that closed its tables and one that recovers them on the next boot.

The shape is a signal before the kill: ask dockerd to stop inside the distribution, give
it a budget, then kill whatever is left and terminate as now. The budget is the
argument. The daemon's own default is fifteen seconds per container, which is more than
a session ending can spend, so the two callers want different numbers rather than one. A
quit can afford to wait and a shutdown cannot, and the honest version of that is a
parameter rather than a single constant chosen for whichever caller was thought of
first.

### §DD190 DD190: the failure that names a WSL internal and no remedy

What the host wrote on 29 August was "the daemon exited while starting", followed by
"wsl.exe exited -1: getpwnam(root) failed 5". Nothing in that says what happened. errno
5 is EIO, so root was not missing: the file holding it could not be read, because the
root filesystem of the distribution had remounted read-only after an unclean shutdown.
The tray then sent the reader to /var/log/dockerd.log inside that distribution, which is
the file the failure had just made unreadable and which dockerd had never reached in any
case. DD162 removed exactly this pointer from the host, and the tray kept its own copy
of it.

So there are two pieces. Recognise the signature, which is getpwnam or getpwuid failing
with 5, or WSL_E_USER_NOT_FOUND out of a distribution that is registered, and say in the
sentence what it means rather than leaving a reader holding a WSL internal. Then name
the remedy, which is e2fsck against the disk of the distribution.

The remedy is awkward and the task has to decide how far to carry it. Repairing needs
the filesystem unmounted and a second distribution to run the check from, because a root
cannot check itself. Printing the exact commands is the floor and is worth shipping on
its own; doing the repair unattended is a larger question about what this tool is
allowed to register on somebody's machine.

### §DD191 DD191: the warning that arrived a boot early

The 29 August failure was announced a boot early and nothing was listening. WSL wrote
"Filesystem error recorded from previous mount: IO failure" and "running e2fsck is
recommended" while mounting the distribution, and the mount then succeeded, so FreeWilly
reported a healthy start. Seconds later ext4 hit a bad block bitmap checksum in group
348, aborted its journal and remounted the root read-only, and from there every read of
/etc/passwd returned EIO.

Two probes would have caught it and both are cheap. At start, the dmesg of the
distribution says whether the filesystem mounted with errors. During a run, a write
inside the distribution says whether the root is still writable, which is what catches a
filesystem that goes read-only under a session that was working a minute earlier.

Where the answer goes is the second half of the task. A start that succeeded on a
filesystem WSL says needs checking is still a start, so this is not a refusal. It is a
line in the journal and a state the window can carry, saying that the distribution needs
a check and that the engine is running on it meanwhile. The remedy itself belongs to
DD190.

### §DD192 DD192: two encodings, one buffer

WslDaemonProcess drains stdout and stderr into one List of bytes, and Sentence hands the
combined result to ConsoleTool.Decode, which picks a single encoding for the whole
buffer. The two streams do not agree. On 29 August wsl.exe wrote its relay error to
stderr as plain bytes and its own refusal to stdout as UTF-16LE, and the heuristic that
counts zeroes in odd positions resolved the mixture to UTF-8.

The journal kept "getpwnam(root) failed 5 U s u ? r i o", and everything after the 5 is
the UTF-16 half read as UTF-8. What it destroyed was the useful half: wsl.exe had named
the condition as Wsl/WSL_E_USER_NOT_FOUND, and that never reached the file. The line
DD162 added to make a failed launch legible was the line that hid the answer.

The fix is two buffers rather than one, decoded separately and joined afterwards. Decode
is already written to be called per stream, and the suite already feeds it bytes
captured from a real failed launch, so the change is in the draining and in what
Sentence receives. Worth reading at the same time: ProcessOutput reads the same pair of
streams and may carry the same defect.

### §DD195 DD193: the tool that has to arrive before the failure

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

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
