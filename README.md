# FreeWilly

**A free Docker Desktop alternative for Windows, under Apache-2.0: no headcount
threshold, no revenue threshold, no licence to buy.** Docker Desktop is free only below
its own limits, and the reason to look for an alternative is usually that your employer
crossed one. This is the whole answer to that: [Apache-2.0](LICENSE), including the
patent grant that makes a corporate legal review straightforward.

It installs and drives Docker on Windows: a preflight that says whether this machine can
run it, an owned WSL2 distribution so nothing of yours is touched, a tray icon carrying
the engine's state, and one window for containers, images and volumes.

## What it is

- **A preflight, not a support case.** Every check states a fact, a verdict, and the one
  action that changes it, before anything is installed.
- **An owned distribution.** The engine lives in a WSL2 distribution called `freewilly`
  that this tool created. An `apt upgrade` or a `wsl --unregister` you ran for another
  reason cannot take the engine with it, and the uninstall is one command.
- **A tray icon you can read at a glance, once Windows shows it.** Shape carries the
  state and colour only reinforces it, so it survives a taskbar, a colour-blind reader
  and a black-and-white screenshot. Windows 11 files an icon it has not seen before into
  the overflow behind the chevron, and nothing here promotes itself out of there: drag it
  onto the taskbar once and Windows remembers. Until then the state is on hover.
- **One window.** Containers with their state in a chip that says *which kind of stopped*
  (a clean exit is muted, a kill is not), their ports as links, their logs de-framed and
  followed, and a shell in the terminal you already have. Three controls per row rather
  than six: Logs, the one verb the row was opened for, and an overflow that is always
  drawn. Images sorted by size with dangling and in-use named. Volumes with what mounts
  them, because a volume is the one thing here that does not come back. **Every heading
  sorts and every list has a filter box**, over the rows already in hand and never a
  second call to the daemon, and both survive the redraw the event stream causes, which
  is the part that is easy to get wrong. Containers open running-first.
- **No daemon of its own.** Quitting the tray leaves the engine exactly as it was.

## What it is not

These are binding, not aspirational. See the non-goals in
[docs/ROADMAP.md](docs/ROADMAP.md):

- Feature parity with Docker Desktop
- A fork of the engine
- macOS and Linux
- Telemetry, accounts or a sign-in
- A resident background service
- A model, prompts or API keys
- A second Docker CLI

## Installing

One `.exe`, and an installer around it. Both are per-user: FreeWilly installs into
`%LOCALAPPDATA%\FreeWilly` and **asks for no administrator prompt**, which is what
reaches a managed corporate laptop, the audience Docker Desktop's terms send here. The
engine's WSL2 feature may still need elevation of its own, and the installer runs the
preflight and says so rather than failing halfway through a download.

Windows 10 2004 (build 19041) or later, 64-bit. Nothing else: the executable carries its
own .NET runtime, so a clean machine needs no prerequisite.

Uninstalling removes what was installed and **asks about what was created**. The
`freewilly` WSL2 distribution holds every image, container and volume you have, so it is
never deleted without a question, and an unattended uninstall keeps it.

**If your shell is your own WSL2 distribution**, the Linux `docker` in it reaches nothing:
the daemon's socket lives inside the distribution this tool owns, and its only way out is a
Windows named pipe a Linux client cannot dial. Run the Windows binary instead: WSL's
interop makes `/mnt/c/Users/you/AppData/Local/FreeWilly/bin/docker.exe ps` work from a
Linux shell. It is a Windows process, so **every path you hand it is read as a Windows
path**: `-v $(pwd):/data` typed in a Linux shell mounts an empty directory rather than your
project. `freewilly do compose up` is the exception, because it respells bind sources for
you.

There is deliberately no per-distribution integration. Docker Desktop's is a toggle that
writes a CLI and a socket into each distribution you tick, which is both the largest
version of touching your machine that this project refuses, and a way of handing the
Engine API to every distribution when the pipe's ACL exists to hold it to one account.

The same executable is every verb, and there is no second tool to
find:

```
FreeWilly.exe                 the tray icon and the window
FreeWilly.exe --tray          the tray icon alone, which is what "start with Windows" uses
FreeWilly.exe --preflight     what this machine can host; --json for a script
FreeWilly.exe --provision     download, verify and install the engine
FreeWilly.exe --run           start the engine and serve the pipe until Ctrl+C
FreeWilly.exe --capture-window <png> [page] [--fixture]
                               render the window to a PNG off-screen
FreeWilly.exe --help          every verb
```

## The agent surface

FreeWilly's other operator is a coding agent, and the split that matters to one is in argv:

```
freewilly read context       the whole machine in one budgeted payload
freewilly read doctor <name> why one container is not answering
freewilly read ps            every container, one line each; mutates nothing
freewilly read logs <name>   --since --level --dedup --budget --out --follow --until
freewilly read ports [port]  what holds a host port, which Docker cannot say
freewilly read verify <name> proof that it answers; --request --expect --wait
freewilly read changes       what moved; --since for a delta, --session for mine
freewilly do   engine start  brings the engine up
freewilly do   reclaim       remove exactly that, and nothing else
```

`port is already allocated` is the refusal an agent cannot act on: the daemon knows a bind
failed and no Docker command anywhere knows what holds the socket. A Windows process does:

```
$ freewilly read ports 135
port 135 is already held on this machine
  heldBy    pid 2416  svchost.exe  (path not readable by this process)
  fix       Stop process 2416 (svchost.exe), or publish a different host port.
```

That join is the argument for this surface existing: a JSON re-wrapping of what `docker`
already says adds nothing, since `--format json` exists. Every refusal carries the fact
that explains it, a fix, and where it applies the nearest matching name. And `cannot
connect to the Docker daemon`, one sentence for three unrelated causes, is now three: a
**rival engine** on the pipe, a **context pointing elsewhere**, or an **engine that is
simply stopped**, each with its own remedy and its own stable `type`.

`read logs` is the one with a contract, because logs are the largest token sink here. A
container that restarted eight times writes the same trace eight times: **634 estimated
tokens deduped to 95**, and `× 8` is the same answer at an eighth of the price. It is
bounded by default, truncates **with a cursor and never in silence**, and `--level error`
keeps every line whose level it could not read, because a stack trace's continuation
lines say nothing about severity, and dropping them would leave you an error with no
trace.

`--out <path>` is the argument that matters most and is the least obvious:

```
freewilly read logs shop-api-1 --out .freewilly/logs/api.log
  wrote D:\shop\.freewilly\logs\api.log  1284 line(s)  91043 bytes
  Grep it: the matching lines cost tokens, the rest does not.
  cursor  t:2026-08-13T09:16:01.884Z
```

A ten-megabyte log becomes affordable rather than merely truncated. It writes, and it is
still a `read`: the promise is that a read does not mutate **the engine**, and a file at a
path you named in the same breath is not a mutation of anything you did not ask for. Two
guards hold it: every daemon request is a `GET`, and a read verb touches no path other
than the one it was given.

`--follow` is for the run you want to watch, and it is bounded three ways so it is never
open-ended:

```
freewilly read logs shop-db-1 --follow --until "database system is ready" --timeout 90s
```

It returns the moment that line arrives, the way `read verify --wait` returns the moment a
service answers. `--until` is a case-insensitive substring, not a pattern language.
Following starts from now, because the run you want to watch is the one you are about to
make; `--since <cursor>` is already the word for replaying what came before. The token
budget is the third bound, so a container printing faster than the ceiling allows ends the
follow rather than the session's context.

A pattern that never arrives exits 1, and the last line says which ending it was, because
your next move differs for each:

```
until   "seed complete" did not arrive in 90s
until   "seed complete" did not arrive, and the log ended
until   "seed complete" did not arrive, and the budget filled first: raise --budget, or --out to a file
until   "seed complete" had not arrived when this was stopped
until   "seed complete" did not arrive, and the container is gone
```

Only the first names a duration, because it is the only one that waited one.

A follow holds a stream belonging to one container id, and `compose up` recreates under a
new one, so the stream ends while the service is running, printing, and about to print the
line you asked for. Where you addressed a **role** rather than a container, the follow
crosses to the replacement and marks the seam in the payload:

```
O  migrating
--- svc:shop/api was replaced: aaaaaaaaaaaa -> bbbbbbbbbbbb ---
O  listening on :8080
```

A role is `svc:<project>/<service>`, or a name, which compose reuses. An id prefix names
one container and no other container is it, so there the follow ends. It crosses once: a
service in a crash loop is recreated over and over, and chasing every one would be bounded
only by the deadline it shares with the first, so a second replacement is reported rather
than followed.

Nothing is printed until it returns. The reader is an agent, which sees stdout once the
process has ended, so a live scroll buys it nothing and would cost `--level`, `--dedup`,
the budget and the cursor, all of which are whole-payload facts. Ctrl+C is a normal ending
and keeps what was read.

`read doctor` closes a join that five commands used to leave to the caller, and returns
conclusions rather than fields:

```
  [FAIL]  memory    the kernel killed it for exceeding 512M
           -> Raise it above 512M, or hold less.
  [FAIL]  ports     :8080→8080/tcp nothing listening
           -> Port 8080 is published and nothing on Windows holds it: it is not running,
              or its process never bound.
  [FAIL]  mounts    /app ← C:\Users\dev\shop\api MISSING, /data ← volume:shop_data
```

The rows are the preflight's own (a fact, a verdict and the one action that changes it), so
nothing new has to be learned to read them. The ports row is the one Docker structurally
cannot answer: the daemon knows what was published and only Windows knows whether anything
holds the socket. A mount this tool did not map is reported **unchecked** rather than broken,
because a false "does not resolve" is worse than no answer.

`read verify` closes the last gap an agent cannot close itself. `running` and *answering*
are different facts, and until now the difference was settled by a person opening a
browser and reporting back, the most expensive cycle in the system. So it connects to the
published port **from Windows**, optionally asks for one path, and reads the health
check's state *beside what the check printed*:

```
  [FAIL]  port     :8080→8080/tcp no answer (ConnectionRefused)
           -> It is running and port 8080 refuses from Windows: the process inside
              never bound, or bound 127.0.0.1 rather than 0.0.0.0.
  [FAIL]  health   unhealthy, 3 failing in a row, last said: connect ECONNREFUSED 127.0.0.1:8080
```

`read doctor` says a port is **listening**, read from the socket table; this says it
**accepts**, and the difference is the whole point: a published port with a dead process
behind it is listening and answers nothing. A connect is the one thing on this surface
that reaches something other than the daemon, so it is deliberately narrow: it opens and
closes, and a request that would appear in somebody's access log needs `--request` and is
a GET. The mount row stops at the Windows side and says `unchecked` for the rest, because
counting inside the container means an exec, which is a POST, and buying one row is not
worth what it costs the guarantee that a read is a read.

A removal is as much a claim as an arrival, so `--expect 404` (or `--expect 404,410` for
either) turns the request row into "this path answered what I named". Proving a retired
page is gone used to be the run that printed red, leaving the caller to decide by hand
that this particular red was the green one. Without `--expect`, `--request` still means
2xx or 3xx, and a missed expectation prints the status that arrived instead.

`--wait --timeout 30s` is the same command as the readiness primitive. It returns the
moment the condition holds, and on a timeout it prints the same report saying which rows
did not pass. A sleep loop written by the caller has neither property, and pays for
every poll.

`read context` is the one that replaces a session's first five calls:

```
engine  running  wsl:freewilly  api=v1.43  ctx=default(ok)
shop-api-1  exited 137  svc:shop/api  8080->8080/tcp  OOM  ×3  limit=512M
shop-db-1  up 4m (healthy)  svc:shop/db  5432->5432/tcp
disk    images 14G (2G dangling)  volumes 2
cursor  c:231884
```

**102 estimated tokens** for a five-service stack, against 5718 measured for the three
container-list reads a diagnosis makes today. The first row already answers *why is the api
container not responding* (`OOM limit=512M`) with no second call. Order is deterministic so
the payload caches and a diff means something, the ceiling is hard and a truncated payload
**says how many rows went** rather than cutting silently, and the cursor fingerprints the
machine rather than the text. `--json` is there for callers that parse.

Everything above makes one session cheaper. `read changes --since` makes the **next** one
cheaper, which over a week is the larger number, because a follow-up syncs on what moved
instead of re-deriving the machine:

```
$ freewilly read changes --since t:2026-08-13T11:45:00Z
shop-api-1              stopped
shop-db-1               running
shop-worker-1           restarted ×2, exited 137
shop_data               created
cursor  t:2026-08-13T12:00:00Z
```

**49 estimated tokens against 102 for the whole pack**, and it is collapsed per object
rather than per event: a container that crash-looped emits twelve lines saying one thing,
and the count with the exit code beside it is what a caller was going to reduce them to.
The history is the daemon's own, so this needs no resident process of its own and answers
whether the tray is running or not. And it reports what **you** did from the tray too,
because the daemon does not know which of you asked.

The daemon keeps its last 256 events. A cursor reaching past them is answered with `too
old, re-read the context` and a non-zero exit, never with a silent partial: a delta that
quietly skips is worse than no delta, because nothing downstream can detect it. The same
rule bounds a busy machine: rows go from the end and how many went is stated.

What an agent created is indistinguishable from what you created, so the only cleanup on
offer is `prune`, scoped to the whole machine, unable to tell this afternoon's
scaffolding from the database you have been filling since March, and therefore the one
command nobody delegates. Everything created through `do` is stamped
`freewilly.session=<id>`, `read changes` answers from that label and never from a
timestamp, and the undo is scoped to it:

```
$ freewilly do reclaim --session repro-17
session  repro-17
would remove  2 container(s)
  container shop-api-1              exited  shop/api:latest
  container shop-db-1               running  postgres:16-alpine
KEEPING  1 volume(s)  a container comes back from its image; a volume comes back from nothing
  volume    shop-data               not mounted
  --volumes takes them too, and changes the token below.
confirm  freewilly do reclaim --session repro-17 --confirm k:6e80b8
```

The session is `DOCKERDESK_SESSION`, because every call is a separate process and an id
minted per invocation would put every object in a session of its own. Unset, the id is
derived from the working directory and **says so**, as `dir:f00bf3`, so a scope that is
really "this folder, forever" is not mistaken for a piece of work.

**The token is computed over that list.** The second call matches only if the list is
still the one printed, so a container that arrived in between makes the confirm refuse
and name what would go now, rather than quietly taking something nobody approved.
Volumes are the exception this is loudest about, and `--volumes` changes the token,
because a token issued for the containers cannot be replayed to take the data with
them.

`docker ps` and `docker rm -f -v` are the same string to an allowlist, so a rule either
grants the whole verb namespace (which permits deleting a volume) or every call stops to
ask. Separating them makes the rule one line:

```jsonc
// .claude/settings.json
"allow": ["Bash(freewilly read:*)"]
```

A surface nobody discovers is one nobody uses, so the install ships how it is found, and
**proposes it, never writes it**. Two files land in `%LOCALAPPDATA%\FreeWilly\agent\`: a
skill naming the verbs and the one rule that matters, and the allowlist line above. The
after-install page prints the two commands. Nothing here touches your `.claude` directory,
which is where a tool editing your configuration unasked would be least forgivable, and a
test asserts the installer script names no such path.

The skill **names verbs and defers**: every sentence explaining what one does lives in
`--help`, which is one copy and the one you already have. Two descriptions of a surface
drift, and the one loaded every session drifts unnoticed, so a test holds the skill's verb
list equal to the registry: a verb that ships without appearing there fails the build.

```
freewilly read context --as brief --out .freewilly/brief.md
```

writes what a session should start knowing, generated from the live machine rather than
hand-maintained and rotting. It writes where it was told and nowhere else, refuses to
replace a file that is already there unless you pass `--force`, and carries no timestamp,
so re-running it on an unchanged machine produces no diff at all.

**`read` is a promise, not a prefix.** A verb under it that writes is a defect, and two
things keep that honest: a read verb is handed a handle with no start, remove or prune on
it, and a test drives every registered read verb and requires every request it made to be
a `GET`. Addresses are names, a container by its name and a compose service as
`svc:<project>/<service>`, because an id changes when a container is recreated.

Every response shape has a ceiling in [`agent-budget.json`](agent-budget.json), and a test
fails a build that made one more expensive. See
[docs/specs/DD23-agent-first-freewilly.md](docs/specs/DD23-agent-first-freewilly.md).

`--capture-window <png> [page] --fixture` draws the window from a **known machine** rather
than from whatever is running: five containers covering running and exited, a published
port and an exposed-only one, dangling images and an anonymous volume. Every name starts
with `sample-`, so a screenshot of it is obviously a fixture and never somebody's real
project; every write refuses, in the engine's own voice. The captures are deterministic,
which is what makes a change to the window reviewable as a picture. And the three empty
states, otherwise the hardest thing here to reach, are one flag away.

`--capture-window` renders the window's own content and never photographs the screen, so it
cannot catch anything that happens to be in front of it, and it needs no desktop at all,
which a screen copy does. [`scripts/Capture-Window.ps1`](scripts/Capture-Window.ps1) is the
screen-copy fallback for popups, and it refuses rather than writing when something overlaps
the window, or when the window has a translucent system backdrop, which composites what is
behind it into the copy and is a leak no overlap check can see.

A windowed program does not hold the prompt, so a typed verb prints *after* the prompt
returns. Redirecting (`FreeWilly.exe --preflight > report.txt`) has neither problem, and
is the form a script or an installer uses anyway.

## Licence and attribution

FreeWilly is [Apache-2.0](LICENSE). Copyright FreeWilly contributors.

The engine it installs is upstream software under its own terms, and those files are not
redistributed here. They are downloaded from their official locations at install time,
against the versions and digests this build pins. [NOTICE](NOTICE) lists every one of
them, its licence and where it came from; the window's **About** says the same thing
where the choice is actually made.

## Building

```
dotnet build
dotnet test
build\build.cmd              one self-contained FreeWilly.exe
build\build-installer.cmd    that, wrapped in dist\FreeWilly-Setup.exe
```

Requires the .NET 10 SDK and Windows; the installer also needs [Inno Setup
6](https://jrsoftware.org/isdl.php), found machine-wide, per-user or on the PATH. The version
is stated once, in [Directory.Build.props](Directory.Build.props), and the installer reads it
back off the built `.exe` rather than repeating it. The mark and the app icon are committed,
and neither is part of the build: `build\trace-logo.mjs` traces
[`build/logo-source.png`](build/logo-source.png) into `site/public/logo.svg` and the
tray-sized `build/icon.svg`, and `build\icon.mjs` rasterises those two into the `.ico`, using
the simplified one below 48 pixels. See [CONTRIBUTING.md](CONTRIBUTING.md) for how the
roadmap, changelog and rationale under `docs/` are written: they are governed by a tool and a
hand edit is refused.
