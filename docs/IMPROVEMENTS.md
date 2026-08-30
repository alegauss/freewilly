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

The doubt this was filed under is now settled. A log being written through a `>`
redirect can be opened with `FileShare.ReadWrite` and read while the child is still
going: probed on 30 August 2026 against a command writing eight lines over eight
seconds, a read at four seconds returned four of them. So the mechanism works.

What is still unmeasured is diskpart's own buffering. A console tool writing to a
redirected handle often switches to block buffering, so the climb may arrive in two or
three jumps rather than smoothly. Two jumps over two and a half minutes is still worth
more than one unchanging line.

The cost is a second callback beside the step one, through `IFilesystemWork`, because a
percentage is not a step and must not be one: `Succeeded` and `Failure` read steps by
name.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
