# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

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
