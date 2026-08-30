# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD243 The step that takes minutes says nothing while it does

DD239 made the steps appear as they land, which fixed the page that looked hung. What it
does not fix is the shape of this particular run: `compact the virtual disk` is one
line, and on a 58 GB disk it stands there unchanged for about two and a half minutes.

diskpart is not silent about it. Measured on 30 August 2026, its log carried the whole
climb: 0, 10, 19, 20, 29, 52, 86, 87, 98, 100 per cent. That went to `diskpart.log`
because an elevated child's handles cannot be read from this side, and the file is
written for the failing case. Nothing reads it while it matters.

Following the file is the obvious shape and it is a tail rather than a parse: the last
percentage in it is the answer, and the words around the number are translated on every
machine. So the number is what is matched and the sentence beside it stays this tool's
own.

An idea rather than a task because the cost is not obvious. It is a second reader of a
file being written by a process this one cannot see, and the payoff is a number on a
line that is already honest about what it is doing. Worth doing after somebody has
watched a slow compaction and said whether the one line was enough.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
