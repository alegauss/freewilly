# Roadmap (active backlog)

## Priority

## Block A — The Windows engine (Docker without Docker Desktop)

- 📋 **DD273** (deps: —) **a teardown killed mid-way writes nothing, because the stop's one journal line is composed only after every step** — A session-ending stop now writes a line as each step finishes, so a journal that ends early still says how far the teardown got. → §DD273
- 📋 **DD274** (deps: —) **a child Windows refused to start reads in the journal as a tool that exited without a word** — The runner now names the exit codes a session ending produces, so 0xC0000142 and 0x40010004 read as Windows refusing the launch rather than as silence. → §DD274
- 📋 **DD275** (deps: —) **the terminate a session ending depends on is given 15 seconds inside a budget of 4** — A hurried stop now runs its wsl.exe under the budget the shutdown actually has, so one call that hangs cannot spend the whole teardown by itself. → §DD275
- 📋 **DD276** (deps: —) **a logoff on a machine that never provisioned an engine now writes a terminate failure** — A hurried terminate that finds no distribution says so plainly, so an ordinary session ending stops leaving a line that reads like something went wrong. → §DD276

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

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
