# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD215 The write path is exercised on a disk made dirty on purpose

Everything ever measured has been a clean disk. The check runs `e2fsck -fn`, which
answers no to every question and writes nothing. The repair runs `-fy`, which answers
yes, and some of those answers discard a damaged inode rather than mending it. That is
the path the confirmation warns about, it is the one that can lose somebody's images and
volumes, and it has never once run against a filesystem with errors on it.

The fakes cover the exit codes, and only those: 1 and 2 read as corrected, 4 as errors
left alone. What a queued integer cannot cover is what e2fsck really prints when it
finds something, whether the findings shown above the button are legible enough to
approve a write on, or whether the repaired ending tells the truth after a run that
actually changed the disk.

A disk can be dirtied on purpose. A scratch ext4 image, corrupted deliberately and run
through the same sequence, answers all three questions once and costs nothing. It does
not have to be this machine's engine, and it should not be.

### §DD216 The rescue carries its tools rather than fetching them

The rescue is imported fresh for every check and then fetches e2fsprogs with apk, so
every check needs a working network, and a first check also downloads the Alpine rootfs
before that.

Warm, the whole sequence is 8.3 seconds, measured here on 29 August 2026 across four
runs. Cold it is not, and the report that started DD210 described Docker being
unavailable for around forty.

So the dialog DD210 wrote is precise about the wrong thing. It says how long the check
takes depends on the size of the disk. On a warm machine the disk is indeed the cost; on
a first run, or on a slow link, the fetch is, and somebody told to expect a disk-sized
wait is given the wrong number on the one run where they have no prior experience to
correct it with.

The worse case is offline. The moment e2fsck is wanted is the moment something is
already wrong, and a machine whose network is part of what is wrong gets a check that
refuses at the fetch step. DD199 named that cost and accepted it deliberately, in
exchange for a rescue that leaves nothing behind. The trade is worth revisiting now that
the rescue is imported and unregistered cleanly: a cached package, or an image kept
beside the pinned rootfs, would make the check work when it is most needed.

### §DD217 Two streams are interleaved and not appended

The findings somebody reads before approving a repair end with the tool's version
banner, printed after the summary line it belongs above. Captured from this machine on
29 August 2026: the five passes, then the inode and block counts, then `e2fsck 1.47.4
(6-Mar-2025)` last of all.

e2fsck writes its banner to stderr and its passes to stdout. DD191 stopped those two
streams being decoded as one, which was the defect that mattered and destroyed text
outright. What it left is the two buffers being concatenated rather than interleaved, so
everything written to stderr lands after everything written to stdout whatever order the
tool emitted them in.

Cosmetic on a clean disk, and it will not stay cosmetic. A repair prints what it mended
on stdout and its complaints on stderr, and which of those came first is part of reading
what happened to a filesystem somebody just agreed to have written to.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

### §DD214 Something drives the window, rather than reading it

Every assertion this project makes about the window reads the window's own source. DD207
checks that one file mentions the stop signal before the teardown; DD210 checks that
another mentions the interlude before the work. Both were the right call at the time,
and both are string matching over code. Neither can fail when a handler is left unwired,
when an `x:Name` moves in markup but not in code, or when a button renders disabled.

Driving it is not hard, and this session did it. UI Automation found the window,
selected the Engine destination, invoked Check filesystem, located the confirmation by
its window class, pressed the Portuguese Sim by the Win32 control id rather than its
caption, and read the outcome and the machine readings back off the panel. That is the
whole DD210 path end to end against a real machine, and it is what found DD212, which no
source assertion could have.

What it must not be is part of the ordinary suite. The run it drives stops Docker and
terminates a distribution, so it belongs behind a flag or in a project of its own,
invoked deliberately, the way `--capture-window` is a verb and not a test.

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
