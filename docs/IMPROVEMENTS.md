# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD242 One of the three has only ever had one surface

DD204 made the check and the repair reachable from the verb and the window through one
seam, and gave the reason: two copies of a five-step sequence are fine until one of them
is edited. The compaction arrived in DD211 with a button and no verb, and has stayed
that way through DD224, DD225, DD226, DD234, DD237 and DD238.

The cost showed up while DD238 was being verified. There was no way to run the real
compaction except to drive the window through UI Automation, press the button, and
answer a message box by control id. `--compact-drill` exists but rehearses on a scratch
distribution with the prune stubbed out, so it exercises the sequence and not the work.

So the verb is not a convenience. It is how the thing gets run by anything that is not a
person at a desk: a release check, an agent, somebody reporting a bug who can paste what
it printed.

The elevated half needs deciding rather than assuming. A terminal cannot be handed a UAC
prompt silently, so it either asks for one and says the prompt is coming, or refuses and
names the button. The unelevated half has no such question and is most of the value.

### §DD243 The step that takes minutes says nothing while it does

DD239 made the steps appear as they land, which fixed the page that looked hung. What it
does not fix is the shape of this particular run: `compact the virtual disk` is one
line, and on a 58 GB disk it stands there unchanged for about two and a half minutes.

diskpart is not silent about it. Measured on 30 August 2026, its log carried the whole
climb: 0, 10, 19, 20, 29, 52, 86, 87, 98, 100 per cent. That went to `diskpart.log`
because an elevated child's handles cannot be read from this side, and the file is
written for the failing case. Nothing reads it while it matters.

Following the file is the obvious shape and it is a tail rather than a parse: the last
percentage in it is the answer, and the words around the number are translated on every
machine. So the number is what is matched and the sentence beside it stays this tool's
own.

An idea rather than a task because the cost is not obvious. It is a second reader of a
file being written by a process this one cannot see, and the payoff is a number on a
line that is already honest about what it is doing. Worth doing after somebody has
watched a slow compaction and said whether the one line was enough.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

### §DD241 The path that shipped broken is the one nothing drives

DD237 shipped with `wsl --terminate` where it needed `wsl --shutdown`. The suite was
green on 1481 tests, because the elevation is a seam and a fake seam does not care
whether the disk was actually released. What found it was a person pressing the button
once, and diskpart refusing.

That is the class of defect DD214 built this verb for, and the verb cannot reach here.
It drives the check and stops at the compaction, so the newest path on the page, the
only one that raises a UAC prompt, and the one that reaches past the engine into every
distribution on the machine, is exercised by nobody but whoever remembers.

A flag beside `--check`, and the same bargain: it is not in the suite, because it stops
Docker and shuts WSL down. What it buys is that the path can be run deliberately, before
a release, by somebody who did not write it.

The UAC prompt is the part it cannot drive, and that is not a reason to skip the rest.
Windows raises it on the secure desktop and no automation touches it, so the run stops
at the prompt and says so. Everything before it is what broke: the announced stop, the
shutdown, the wait for the file, and the dialog naming what else goes down. Everything
after it is readable from the panel once a person has answered.

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
