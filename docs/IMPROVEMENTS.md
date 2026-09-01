# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

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

### §DD277 A teardown line should say which process wrote it

DD273 gave the teardown a line per step, written through `EngineCommand.Step`, which
stamps the column word `stop`. Two processes now reach it during one session ending: the
host, from its own `StopAsync`, and the tray, from `LiveEngineTeardown.Terminate` when
the backstop fires. They write the same words into the same file.

That undoes the one thing DD163 built the shared journal for. Every other line in there
says who is speaking: `host` for the engine host, `tray` for the icon, `session` for
what Windows said, `power`, `fs`. The reader is meant to be able to follow two processes
through one timeline, and the run of 21 August 2026 is exactly why: the engine going
quiet, a gap, and a human clicking Start, which only reads as one story because each
half is attributed.

The teardown is the worst place to lose that. DD188's whole design is that the host
should do this and the tray is a backstop that runs only where the host did not, so
"which of the two terminated it" is the first question anyone opens the file to answer.
Two identical `stop  terminated freewilly` lines make that unanswerable, and a single
one is worse: it looks like the host did its job.

The word is already a parameter of the sink; what is missing is that each caller passes
its own.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
