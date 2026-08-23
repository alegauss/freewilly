# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

### §DD170 Two spellings of one destination, and the capture believes both

`ShowTab` finds the destination case-insensitively, which is right for a name typed on a
command line. `Show` then decides what to draw with a `switch` on an exact string.
Between them sits the difference: the not-yet-ticked path sets `IsChecked`, and the
`Checked` handler passes the button's own `Tag` — always the canonical spelling. The
already-ticked path calls `Show(header)` and passes whatever the caller wrote. No case
matches, `default` runs, and the containers list is drawn under a nav strip that says
Volumes.

It hid because it needs both halves at once: the destination has to be the one the
window reopened on, and the caller has to spell it differently. `--capture-window <png>
volumes` on a machine whose saved state is `"Destination": "Volumes"` is exactly that,
and it was found by capturing the window after a change to the volumes list and seeing
containers in the picture.

What it costs is trust in the capture itself. That verb exists so a change can be looked
at rather than reasoned about, and this makes it silently photograph the wrong page and
report success — the one failure a verification tool must not have. It is also reachable
from any caller passing a name it did not read off the strip.

The fix is to stop carrying two spellings: the already-ticked path has the button in
hand, so it can pass the same `Tag` the event handler passes and both routes agree by
construction.

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
