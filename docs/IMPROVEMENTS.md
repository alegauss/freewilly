# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD211 The gap between the two sizes is on the page, and nothing acts on it

DD197 put the two sizes side by side for exactly this reading: the virtual disk on the
Windows volume, and the space used inside the distribution. A machine showing fifty
gigabytes of virtual disk against sixteen gigabytes of images has said everything except
what to do about it, and the page offers nothing to do.

The gap has two halves and takes two steps. Deleted layers and buildx cache still hold
blocks the filesystem no longer counts, so a `docker builder prune` and an `fstrim`
inside the distribution come first. Only then is there anything to hand back, and
handing it back is `wsl --manage <distro> --set-sparse true`, which wants the
distribution stopped and wants no elevation. That last part is why it is the mechanism:
DD199 measured the non-elevating path and chose it, and `diskpart compact vdisk` or
`Optimize-VHD` would put a UAC prompt and a Hyper-V dependency behind a housekeeping
button.

Stopping the distribution is the interruption Check filesystem already costs, so this
reuses what DD210 builds rather than inventing a second way to take the engine down and
put it back.

What it must not do is remove what nobody offered. Images and volumes stay, and the only
thing pruned is cache the daemon itself calls reclaimable. The plan is shown before
anything runs, both readings are taken before and after, and the panel refreshes when it
finishes, so the button is answerable for the bytes it claims.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
