# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

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

Two calls a session ending depends on run under `ConsoleTool.Timeout`, which is fifteen
seconds. `EngineLifecycle.StopAsync` reaches `wsl --terminate` through `_wsl.Run(...)`
with no budget named, and `LiveEngineTeardown.DistributionIsUp` asks `wsl --list
--running` the same way. The whole teardown has four seconds, which is
`SessionEndingBudget`, and Windows is not waiting longer.

Fifteen seconds was written for a preflight probe standing at a prompt. It and the
shutdown budget do not disagree on a healthy machine, because both calls answer in well
under a second. They disagree exactly where it matters: a `wsl.exe` that starts and then
stops answering. The call sits inside a budget it can never exhaust, the four seconds
run out around it, and the process is killed somewhere in the middle. What reaches the
journal is nothing, because the line naming the outcome is written after the call
returns. The backstop's one-second poll cadence is the same fiction: a probe that hangs
makes the cadence meaningless.

DD270 removed one way that happens and only one, a launch Windows refuses now fails at
once. A launch that succeeds and hangs is untouched by it, and the failing teardowns
between 29 and 31 August all ended at the four-second mark rather than at fifteen.

So a hurried teardown carries its own budget down to every call it makes, rather than
each call taking a constant written for another caller. The patient path keeps the
timeout it has.

### §DD276 A distribution that was never there is not a failed terminate

DD271 took the `DistributionRegistered` gate off the hurried path on purpose: the gate
is a `wsl --list`, so asking whether the terminate is worth running costs the same
launch as running it, and under a four-second budget that launch is the thing being
protected.

What it did not settle is what the journal then says. `wsl --terminate freewilly`
against a distribution that is not registered exits non-zero and prints "There is no
distribution with the supplied name", so `StopAsync` reports `could not terminate
freewilly: ...`. On a machine where the engine was never provisioned, or where somebody
unregistered it, that line is now written at every logoff. The tray reaches it by the
ordinary route: with no host to tell, `SessionTeardown` terminates itself.

Nothing is wrong on that machine, and the file says otherwise. That is the same defect
DD26 puts above every other consideration, arriving in the one file a reader opens
precisely because they suspect something went wrong overnight.

The terminate stays ungated. What changes is that its own output is read: a refusal
naming a distribution that is not there is the answer "there was nothing to take down",
and it should be written that way. Every other failure keeps the sentence it has,
because those are failures.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
