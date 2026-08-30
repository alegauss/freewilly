# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD247 The route with the worst record is the one with no drill

DD215 built the compaction drill on an argument that has since been proved twice: a
sequence that has never run against a real virtual disk is a sequence nobody has tested.
DD221 ran it and found the sparse refusal on the first go, which is the whole of DD224.

The elevated route has no such rehearsal, and it has the worse record. DD237 shipped it
with `wsl --terminate` where diskpart needs `wsl --shutdown`, on a green suite of 1481
tests. DD238 fixed that and it failed again on the next press, this time on the file
still being open. Both were found by a person pressing a button on a disk holding every
image they have.

DD245 makes the drill cheap. The sequence is now one method with the take-down and the
acting step passed in, so pointing it at the scratch distribution costs the two seams
the existing drill already supplies.

What the drill cannot rehearse is the prompt. UAC is on the secure desktop, so this is a
verb somebody runs and answers, like `--compact` itself rather than like a test. What it
buys is that the answer is given over a scratch disk of deliberately wasted space
instead of over the real one.

The `--shutdown` cost does not go away: rehearsing still stops every distribution on the
machine. The verb has to say so before it starts rather than after, which is what the
dialog already does on the page.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
