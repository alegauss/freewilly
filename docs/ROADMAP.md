# Roadmap (active backlog)

## Priority

## Block A — The Windows engine (Docker without Docker Desktop)

- ⏳ **DD199** (deps: DD196 ✅, DD190 ✅) **nothing repairs a corrupt distribution filesystem, and the check cannot run against a mounted root** — the check and the repair are only reachable from a terminal, and the engine state they need is on the window's Engine page. → §DD199
- 📋 **DD202** (deps: —) **fourteen tests fail whenever a real FreeWilly is running, so the suite cannot go green on a machine that has one** — SingleTrayTests and SingleEngineTests claim the very mutex a live tray and host hold, and they report that as a failure rather than as the state it is. → §DD202
- 📋 **DD203** (deps: —) **no run of the repair verb has ever imported a rescue, terminated the engine and read a disk in sequence** — DD201 fixed the two commands a minirootfs lacks by measuring them one at a time, and the six steps have still only ever run against a fake. → §DD203

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

- 📋 **DD198** (deps: DD197 ✅) **the agent surface has no reading for the health of WSL, the distribution or the disk** — read doctor answers for one container and nothing answers for the machine under it, so an agent asked why the engine will not start has to shell out to wsl.exe and parse it. → §DD198

## Block H — The public surface (the site a reader and an agent both read)

## Non-goals

- **Feature parity with Docker Desktop** Kubernetes, the extensions marketplace and Dev
  Environments are most of that product and none of them is why anyone leaves it; the
  scope here is install, see, start, stop.
- **A fork of the engine** This drives upstream Moby unmodified. A fork would make every
  Docker answer on the internet subtly wrong for this tool's users, which is a worse tax
  than any licence.
- **macOS and Linux** The problem being solved is Windows-specific: Docker Desktop's
  terms plus WSL2 plumbing. Linux needs no GUI to install an engine, and macOS already
  has free alternatives.
- **Telemetry, accounts or a sign-in** Nothing here phones home and there is nothing to
  log into. A tool adopted to escape a licence check must not ship a different reason to
  be blocked by a corporate proxy.
- **A resident background service** The complaint this project answers is a desktop app
  holding gigabytes at every boot. Both the app and the engine run when asked, and
  autostart stays a setting the user turns on.
- **A model, prompts or API keys** FreeWilly is the substrate an external agent drives,
  never a place intelligence lives: the caller already has a model, and hosting one
  would end the free, offline, no-account tool this is.
- **A second Docker CLI** The agent surface answers the joins the Engine API cannot
  make; what docker already answers well is not re-wrapped, so there is no build, no
  push and no registry credentials here.
- **Renumbering the DD task prefix** The rename stops at the product. Every id appears
  in a dependency, a section anchor, a shipped ledger entry and a pushed commit message,
  so a two-letter prefix change rewrites all of it to say exactly the same thing.
