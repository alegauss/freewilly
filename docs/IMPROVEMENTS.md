# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD220 The verdict and the findings must describe one reading

Measured on 29 August 2026, by the drill DD215 built. An ext4 image with its superblock
free counters broken was read with `e2fsck -fn`, which printed `Free blocks count wrong
(3, counted=25798). Fix? no` in full and then exited zero. Those counts are recomputed
rather than trusted, so the tool considers there to be nothing to correct.

The page reads the exit code, which DD199 chose deliberately and for good reason: the
text is a tool's prose and the code is its verdict. What nobody looked at is that the
findings are shown whatever the verdict was. So the panel drew "The filesystem is clean"
and "Nothing needed mending" above a transcript complaining about the disk, and the
reader is left holding two answers to one question on the page whose whole job is being
handed to somebody else.

Neither half is wrong on its own and the fix is not to start parsing e2fsck. What the
headline and the transcript owe each other is that they describe the same reading: a
check that found nothing to correct and printed something anyway is a third case, and
saying so costs a sentence.

It is also the one shape a user cannot resolve themselves. A dirty disk with a headline
saying so is legible, and a clean disk with an empty transcript is legible; this is the
combination that reads as the tool having lost track of what it just did.

### §DD221 The path that hands blocks back is rehearsed too

DD215 made the argument and DD211 is the other half of it. The compaction prunes the
daemon's build cache, trims the filesystem, terminates the distribution and converts its
virtual disk to sparse. Every one of those writes, and not one of them has run outside a
fake: the tests queue exit codes and the window was photographed, which between them
prove the sequence and prove nothing about what the sequence does.

Three things a queued integer cannot answer. What `wsl --manage --set-sparse true`
prints and exits with on a disk that is already sparse, on a WSL too old to have the
flag, and on one that is in use. Whether `fstrim` is in the distribution this tool
provisions at all, which the code guesses at in a comment. And whether the arithmetic
over the two readings tells the truth: the panel claims a number of gigabytes handed
back, and nobody has watched a virtual disk actually get smaller.

The rehearsal already exists. `--fsck-drill` imports a distribution, makes a disk inside
it and walks a sequence against it, and a compaction rehearsal is the same shape: a
scratch image, filled and then emptied, trimmed and handed back, with both readings
taken either side. It does not have to be this machine's engine and it should not be.

What it must not become is a second copy of the sequence. The steps the drill exercises
have to be the ones `DiskCompaction` runs, or it rehearses something that ships nowhere.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

### §DD222 The driver's own second half is still unexercised

DD214 built the driver and ran half of it. Against the tray running on the machine it
was written on, it found the window, selected the Engine destination, found Check
filesystem, and refused because that window has no Compact button: the tray was from a
build older than DD211. That refusal is the finding the whole verb exists to produce,
and it is not the same as having driven the thing.

What has never run is `--drive-window --check`. That is the half with the parts most
likely to be wrong, and every one of them is a guess until somebody watches it: whether
a WPF `MessageBox` really exposes its buttons under the Win32 control ids, whether
inode-level waiting on the panel's headline survives a run that takes minutes, whether
the buttons come back enabled where the driver looks for them.

The reason it did not run is worth writing down, because it will be the reason again.
The verb drives whatever tray holds `FreeWilly.tray`, and quitting a stale one takes the
engine down with it since DD128. So a clean run wants a machine where the tray is
already the current build, or a deliberate quit and relaunch, and neither is something
to do to somebody in the middle of their afternoon.

One recorded run, with what it printed kept, is the whole of this. It is not a test and
must not become one: the path it drives stops Docker.

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

### §DD223 A tool that writes a cache owes the sweep as well

DD216 writes a prepared rescue into the install directory and nothing ever takes it
away. Measured here on 29 August 2026: `rescue-41f73e3cf5fa.tar`, 10.8 MB, written by
the first drill and reused by every run after it.

Two ways that becomes litter. The file is named after the pinned rootfs digest, so a
manifest that bumps Alpine stops matching it rather than replacing it — correct, and it
leaves the old one on disk forever with nothing that will ever open it again. And the
uninstaller knows about the distribution and the downloads and not about this, so a
machine that removed the product keeps a tarball it cannot account for.

Neither is urgent and both are the same rule this project already holds elsewhere. DD199
refused to leave a rescue in somebody's `wsl --list`, and the argument does not stop at
the distribution list: a tool that writes eleven megabytes into a user's profile owes
them the sweep as well as the write.

The sweep is cheap and belongs where the image is written. Anything matching the image's
own naming that is not the one this build wants is a file this tool made and no longer
uses, so the moment a new one is kept is the moment to drop the others. The uninstaller
half is a line in the script beside the ones already removing the downloads.

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
