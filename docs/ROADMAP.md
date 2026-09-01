# Roadmap (active backlog)

## Priority

## Block A — The Windows engine (Docker without Docker Desktop)

- 📋 **DD275** (deps: —) **the terminate a session ending depends on is given 15 seconds inside a budget of 4** — A hurried stop now runs its wsl.exe under the budget the shutdown actually has, so one call that hangs cannot spend the whole teardown by itself. → §DD275
- 📋 **DD276** (deps: —) **a logoff on a machine that never provisioned an engine now writes a terminate failure** — A hurried terminate that finds no distribution says so plainly, so an ordinary session ending stops leaving a line that reads like something went wrong. → §DD276
- 📋 **DD277** (deps: —) **the teardown lines two processes now write are both labelled stop, so nobody can tell which wrote one** — Each teardown line names the process it came from, so a reader can tell the host's own stop from the tray's backstop where DD188 says the difference matters. → §DD277
- 📋 **DD278** (deps: —) **six callers spell their own version of what a wsl call said, and none falls back to the exit code** — The one Detail DD274 added answers all of them, so a wsl.exe Windows refused inside a repair or a compaction says so rather than producing an empty sentence. → §DD278

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
