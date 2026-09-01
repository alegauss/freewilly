# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

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
