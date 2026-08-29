# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

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

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
