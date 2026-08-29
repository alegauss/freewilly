# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD224 Windows withdrew the mechanism the button was built on

Measured on 29 August 2026 by DD221's rehearsal, on its first run. Every step landed
until the last, where WSL answered: sparse VHD support is currently disabled due to
possible data corruption, and named `--allow-unsafe` as the only way past it. So the
button cannot succeed on this Windows, and the flag that would make it succeed is the
one DD211 refused on purpose.

That refusal still stands. A housekeeping button reaching for the unsafe form of a call,
on a filesystem holding every image and volume somebody has, is how a tidy-up becomes
the thing that loses their work — and Microsoft disabled the safe form for that reason
rather than a bureaucratic one.

Three routes and none is obvious. Waiting is one: the flag is disabled and not removed,
so the button could name the refusal rather than reading as broken. `Optimize-VHD` and
`diskpart compact vdisk` are the second, and DD199 measured and rejected both for
putting a UAC prompt and a Hyper-V dependency behind a housekeeping button. Withdrawing
the button is the third, and a control that cannot work is worse than no control.

Worth carrying into whichever is chosen: the rehearsal also could not make a scratch
disk grow. 512 MB written and deleted left the virtual disk at 76 MB, because WSL2
mounts with discard and reclaims as it goes. The gap DD197 measured is real and older
disks carry it, but a fresh one may no longer accumulate one.

### §DD225 A sparse file keeps its length and stops costing the volume

Handing blocks back makes the virtual disk a sparse file, and a sparse file keeps its
length. NTFS records the ranges nothing wrote to and stops charging for them, so the
volume gets its space back while `FileInfo.Length` goes on reporting the size the file
grew to. That is the point of `--set-sparse`, and it is why measuring the length cannot
see it work.

`DiskCompaction.Sizes` reads the length. So even on a machine where the hand-back
succeeds, both readings would be the same number, `HandedBack` would be null, and the
panel would say the disk was compacted and gave nothing back — about a run that had just
returned gigabytes. The one sentence the button exists to be able to say is the one it
cannot say.

Nothing had noticed because nothing had ever watched a successful compaction. DD221's
rehearsal reads both numbers for exactly this reason and carries `FileOnDisk`, which
asks Windows what a file is actually costing through `GetCompressedFileSize` — the call
that reports physical storage for sparse and compressed files and the plain length for
an ordinary one. There is no managed equivalent.

So that is the reading to take, and the two sizes already side by side become three
questions: what the filesystem may grow into, what it is using, and what the volume is
charging for. The third is the one a user came about, and the only one a compaction
moves.

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
