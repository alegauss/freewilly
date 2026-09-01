# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

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

### §DD279 The relay's join is the last unbounded wait in a teardown

DD275 bounded every `wsl.exe` a hurried teardown makes. It did not reach the step that
is not a `wsl.exe`: `EnginePipeRelay.DisposeAsync` ends with
`_accepting?.Join(TimeSpan.FromSeconds(5))`, and five seconds does not fit inside the
four a session ending has.

That deadline is right for what it was written for. The comment beside it makes the
argument: a dispose that never returns takes the tray down, and a thread that has not
noticed the closed handle is a background one that goes with the process anyway. Five
seconds is generous rather than careless, because nothing was racing it.

A session ending is racing it. DD271 put the terminate first, so this can no longer cost
the unmount, and that is the reason this is a smaller defect than the one DD275 fixed
rather than the same one. What it still costs is everything after it: the SIGTERM, the
daemon stop, and the per-step lines DD273 added, which is the account of the teardown
that a reader opens the file for.

So the join takes the same budget the calls around it take. A thread that has not
returned by then is left to the process, which is what the existing comment already says
happens at five seconds and would say no differently at two.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
