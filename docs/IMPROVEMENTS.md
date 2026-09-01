# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

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

### §DD278 One answer to what a wsl call said

DD274 gave `WslResult` a `Detail`: the tool's own words where there are any, then the
failure, then the exit code spelled out. It was written for the terminate line, which is
where the gap was measured, and it is the general answer.

Six other places already spell a weaker version of it. `DiskCompaction`,
`ElevatedCompaction`, `FilesystemRepair`, `RepairDrill`, `RescueImage` and
`CompactionDrill` each carry a line reading `result.Failure ??
result.Output.Trim().ReplaceLineEndings(...)`. Every one of them stops where DD274
starts: a call that ran, exited non-zero and wrote nothing resolves to the empty string,
and the sentence built around it says a repair failed and names nothing at all. That is
the same shape the terminate line had, in the five commands a user runs precisely
because something is already wrong.

They differ in one detail that has to survive: the separator. Four join wrapped output
with a space and one with `"; "`, which is a real choice about how a multi-line `e2fsck`
transcript reads on one journal line, not an accident to be flattened away.

So `Detail` takes the separator and the six call sites become one call each. What that
buys is not fewer lines: it is that the next reader of a failed compaction gets the exit
code rather than a sentence that trails off.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
