# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD187 DD187: the host that owns the teardown hears nothing

The engine host holds the two things a teardown needs: the wsl.exe handle the daemon
runs under, and the relay serving the pipe. It already subscribes to PowerModeChanged
and acts on a suspend, so the hidden window SystemEvents keeps for a process with no
message loop of its own does receive broadcasts. SessionEnding is the one it never asked
for.

The journal shows the cost. Every ending the host reaches on its own passes through the
finally that writes "this host is done" (DD163). That line follows every Quit and none
of the seven session endings recorded between 23 and 28 August 2026, and the Stopped
line naming the terminated distribution is missing from all seven as well. The host is
killed where it stands, the launcher goes with it, and WSL2 reaps dockerd with no clean
unmount of the distribution root. The ext4 corruption repaired by hand on 29 August is
what that leaves behind.

The fix is the subscription the host already has the shape for: SessionEnding cancels
the token Ctrl+C cancels, and the handler waits for StopAsync to return rather than
returning first. Waiting is what makes it work and also what bounds it, because Windows
treats a slow WM_QUERYENDSESSION as a hung app. Terminating the distribution is one
wsl.exe call and it is the one that matters, since a terminate unmounts ext4 and a kill
does not.

### §DD188 DD188: the stop nobody waits for

DD129 answers SessionEnding by calling EngineHolder.Stop, which is a Process.Start of
this executable with --stop under UseShellExecute. Two things about that shape fail
during a shutdown. ShellExecuteEx routes through the shell, which is being torn down at
the same moment, and the handler returns at once, so nothing on the machine is waiting
when Windows begins killing the session.

The evidence is the same seven endings. A Quit produces the Stopped line and the
host-is-done line in the same second, because --stop signals the live host through
SingleEngine and the host writes both. After a session ending neither line was ever
written, so the spawned process did not reach even the named event.

What replaces it belongs in this process. The tray has a message loop, so it can hold
the shutdown open with ShutdownBlockReasonCreate, signal the live host, wait a bounded
time for the pipe to go quiet, and destroy the block. Where no host is running it runs
the terminate itself. Two details decide the design: the block reason needs a real
top-level window and the one NotifyIcon keeps may not qualify, and the string it carries
is shown to the user on the shutdown screen, so it has to say what is being waited for.

Out of scope: wsl --shutdown, for the reason DD128 gave. It would take every other
distribution on the machine with it.

### §DD189 DD189: the containers are killed, never stopped

WslDaemonProcess.Stop calls Kill(entireProcessTree: true) on the launcher, and the class
already records what follows: WSL2 reaps the user processes shortly after the launching
wsl.exe exits. That reaping is a SIGKILL. dockerd never receives a SIGTERM, so it never
runs the shutdown that stops containers, and no container receives one either.

This is not only a shutdown problem. Every teardown this tool performs takes the same
path, the Quit menu item included, so a database container has been killed rather than
stopped on every exit since DD128. A stop signal is the difference between a MariaDB
that closed its tables and one that recovers them on the next boot.

The shape is a signal before the kill: ask dockerd to stop inside the distribution, give
it a budget, then kill whatever is left and terminate as now. The budget is the
argument. The daemon's own default is fifteen seconds per container, which is more than
a session ending can spend, so the two callers want different numbers rather than one. A
quit can afford to wait and a shutdown cannot, and the honest version of that is a
parameter rather than a single constant chosen for whichever caller was thought of
first.

### §DD190 DD190: the failure that names a WSL internal and no remedy

What the host wrote on 29 August was "the daemon exited while starting", followed by
"wsl.exe exited -1: getpwnam(root) failed 5". Nothing in that says what happened. errno
5 is EIO, so root was not missing: the file holding it could not be read, because the
root filesystem of the distribution had remounted read-only after an unclean shutdown.
The tray then sent the reader to /var/log/dockerd.log inside that distribution, which is
the file the failure had just made unreadable and which dockerd had never reached in any
case. DD162 removed exactly this pointer from the host, and the tray kept its own copy
of it.

So there are two pieces. Recognise the signature, which is getpwnam or getpwuid failing
with 5, or WSL_E_USER_NOT_FOUND out of a distribution that is registered, and say in the
sentence what it means rather than leaving a reader holding a WSL internal. Then name
the remedy, which is e2fsck against the disk of the distribution.

The remedy is awkward and the task has to decide how far to carry it. Repairing needs
the filesystem unmounted and a second distribution to run the check from, because a root
cannot check itself. Printing the exact commands is the floor and is worth shipping on
its own; doing the repair unattended is a larger question about what this tool is
allowed to register on somebody's machine.

### §DD191 DD191: the warning that arrived a boot early

The 29 August failure was announced a boot early and nothing was listening. WSL wrote
"Filesystem error recorded from previous mount: IO failure" and "running e2fsck is
recommended" while mounting the distribution, and the mount then succeeded, so FreeWilly
reported a healthy start. Seconds later ext4 hit a bad block bitmap checksum in group
348, aborted its journal and remounted the root read-only, and from there every read of
/etc/passwd returned EIO.

Two probes would have caught it and both are cheap. At start, the dmesg of the
distribution says whether the filesystem mounted with errors. During a run, a write
inside the distribution says whether the root is still writable, which is what catches a
filesystem that goes read-only under a session that was working a minute earlier.

Where the answer goes is the second half of the task. A start that succeeded on a
filesystem WSL says needs checking is still a start, so this is not a refusal. It is a
line in the journal and a state the window can carry, saying that the distribution needs
a check and that the engine is running on it meanwhile. The remedy itself belongs to
DD190.

### §DD192 DD192: two encodings, one buffer

WslDaemonProcess drains stdout and stderr into one List of bytes, and Sentence hands the
combined result to ConsoleTool.Decode, which picks a single encoding for the whole
buffer. The two streams do not agree. On 29 August wsl.exe wrote its relay error to
stderr as plain bytes and its own refusal to stdout as UTF-16LE, and the heuristic that
counts zeroes in odd positions resolved the mixture to UTF-8.

The journal kept "getpwnam(root) failed 5 U s u ? r i o", and everything after the 5 is
the UTF-16 half read as UTF-8. What it destroyed was the useful half: wsl.exe had named
the condition as Wsl/WSL_E_USER_NOT_FOUND, and that never reached the file. The line
DD162 added to make a failed launch legible was the line that hid the answer.

The fix is two buffers rather than one, decoded separately and joined afterwards. Decode
is already written to be called per stream, and the suite already feeds it bytes
captured from a real failed launch, so the change is in the draining and in what
Sentence receives. Worth reading at the same time: ProcessOutput reads the same pair of
streams and may carry the same defect.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
