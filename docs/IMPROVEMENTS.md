# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

### §DD240 The driver hands its console to a window that outlives it

Measured while verifying DD237. `--drive-window` with a tray already running answers in
about a second. With no window open it never returns at all, which is precisely the
machine the verb was written to serve: a clean checkout, a CI runner, a developer who
has not opened the tray.

It is not the driver hanging. The driver launches the window with `UseShellExecute =
false` and no redirection, so the child inherits this process's standard handles. The
driver then does its work and exits, but the write end of the pipe is still held open by
a window that is meant to stay up, so whoever is reading that pipe waits for an
end-of-file that will not arrive until somebody closes the window. Redirecting the
driver's output to a file instead of a pipe makes the same run finish normally, which is
what identifies the handle rather than the logic.

The fix is at the launch: give the child its own handles rather than this process's.
Leaving the window up afterwards is deliberate and stays, because the panel it just read
is the thing an operator wants to look at.

Worth more than the inconvenience suggests. This verb exists to catch what a string
match over source cannot, and it is unusable on exactly the machines that have no window
already open, which is where an automated check would run it.

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
