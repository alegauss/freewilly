# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD271 The terminate goes first when the stop is hurried

Under `HurriedGrace`, `StopAsync` does four things in order: it drops the relay, asks
whether the distribution is running, sends dockerd a SIGTERM and waits for it, and only
then runs `wsl --terminate`. Three of those four reach for `wsl.exe`, and the terminate
is last.

That order is right when somebody is at a keyboard. It is wrong when Windows is ending
the session, because the budget is four seconds and a single `wsl.exe` launch in that
window can consume all of it. The journal shows the consequence: the sessions of 29, 30
and 31 August 2026 all end at "still tearing down after 4s" with no "terminated
freewilly" line behind them. The step that unmounts ext4 never ran, which is the unclean
unmount DD187 and DD188 were written to prevent and the one repaired by hand on 29
August.

Containers stopping themselves is worth having, and DD189 is why. It is not worth having
ahead of the unmount, because a container reaped by WSL2 recovers on the next start and
a root filesystem torn off mid-write may not. So a hurried stop terminates first and
does the rest with whatever budget is left. A patient stop keeps the order it has: there
the SIGTERM has twenty seconds and the terminate is not racing anything.

### §DD272 A quiet pipe is not a stopped distribution

`SessionTeardown` decides the host handled the teardown by pinging the engine and
finding nothing there. The premise is that a quiet pipe means the distribution is down.
It does not. Dropping the relay is the first thing `StopAsync` does, so the pipe goes
quiet at the start of the teardown rather than at the end, and the backstop stands down
at the moment the work it exists to cover has barely begun.

Every failing shutdown in the journal shows it. On 31 August 2026 the tray wrote "the
engine host took it down" at 21:51:46, and two seconds later the host itself wrote
"still tearing down after 4s". Both lines are about the same teardown and the second one
is the true one. No terminate ever ran, and the tray had already decided one had.

What the backstop should ask is what it actually cares about: whether the distribution
is still registered and running. That is a different question from whether anything is
serving the pipe, and it stays true right up to the terminate. Answering it needs
`wsl.exe`, which is the launch that may not work, so a failed launch has to read as
unknown and terminate anyway. Terminating a distribution that is already down costs one
wasted call. Not terminating one that is up costs the ext4.

### §DD273 A teardown that is killed should still have written something

`StopAsync` collects what it did into a list and turns that list into one journal line
when it returns. Nothing is written until every step has finished, so a teardown Windows
kills part way through leaves no trace that it ran at all.

That is what the last three shutdowns look like. The session lines are there, because
the handler writes those itself, and then nothing: no relay line, no SIGTERM line, no
terminate line, no "this host is done". Read the next morning, that file says only that
the teardown did not finish, which was already obvious from the four-second line above
it. It does not say which step it was in, whether `wsl.exe` was reached, or what the
terminate returned. Finding that out took the Windows System log, which is the wrong
place to have to go.

The clean session ending on 30 August 2026 at 10:08 shows what the aggregate line is
worth when it does get written, and there is no reason to lose it. So the aggregate
stays for the patient path, and a hurried stop adds a line per step, written and flushed
as that step finishes. Under a session ending this file is the only witness, and a
witness that speaks after the room is empty is not one.

### §DD274 The exit codes a session ending produces

On 29 August 2026 the journal recorded "the daemon exited: wsl.exe exited 1073807364
without a word". That number is 0x40010004, DBG_TERMINATE_PROCESS, and it means Windows
killed the process during the shutdown. Nothing in the line says so. The 0xC0000142 case
is worse: a child that fails DLL initialisation exits with empty stdout and stderr, so
the runner reports a tool that ran and said nothing, which is the same shape as a tool
that ran and had nothing to say.

Two different failures, both invisible, both specific to a session ending. The evidence
that either was happening came from the Windows System log rather than from this
journal, which is backwards for a tool whose entire account of a shutdown is a file read
the next morning.

So the runner names them. An exit code in the range NTSTATUS uses is reported as what it
is, with the two a shutdown produces spelled out: 0xC0000142 as Windows refusing to
initialise the process, and 0x40010004 as Windows killing it. The rest keep their number
and gain the hex, because a reader who has to convert 1073807364 by hand before they can
search for it will not.

### §DD275 A step cannot be given more time than the teardown has

`EngineLifecycle.StopAsync` reaches `wsl --terminate` through `_wsl.Run(...)` with no
budget, so it inherits `ConsoleTool.Timeout`, which is fifteen seconds. That number was
written for a preflight probe standing at a prompt. During a session ending the whole
teardown has four, which is `SessionEndingBudget`, and Windows is not waiting longer.

The two do not disagree on a healthy machine, because a terminate takes well under a
second. They disagree exactly where it matters: a `wsl.exe` that starts and then stops
answering. The call sits inside a budget it can never exhaust, the four seconds run out
around it, and the process is killed somewhere in the middle. What reaches the journal
is nothing, because the line naming the outcome is written after the call returns.

DD270 removed one way that happens, and only one: a launch Windows refuses now fails at
once instead of waiting on a dialog. A launch that succeeds and hangs is untouched by
it, and the failing teardowns between 29 and 31 August all ended at the four-second mark
rather than at fifteen, which is what says the budget above was never the binding one.

The fix is that a hurried stop carries its own budget down to the calls it makes, rather
than each call choosing from a constant written for another caller. The patient path
keeps the timeout it has, because a quit really can wait.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
