# Roadmap (active backlog)

## Priority

## Block A — The Windows engine (Docker without Docker Desktop)

- 📋 **DD270** (deps: —) **a wsl.exe that fails to start during a shutdown opens a modal Windows error box and blocks until dismissed** — The host sets a process error mode its children inherit, so a shutdown-time launch fails at once instead of waiting on a dialog nobody can click. → §DD270
- 📋 **DD271** (deps: DD270) **a session-ending stop reaches the terminate that unmounts ext4 only after three other wsl.exe calls** — A hurried stop now terminates the distribution first, so the one step that protects the filesystem runs while Windows still allows a process to start. → §DD271
- 📋 **DD272** (deps: —) **the tray's backstop reads a quiet pipe as the host having taken the distribution down, and the pipe goes quiet first** — The backstop now asks whether the distribution is still running, so it terminates where the host stopped serving but never reached the terminate. → §DD272
- 📋 **DD273** (deps: —) **a teardown killed mid-way writes nothing, because the stop's one journal line is composed only after every step** — A session-ending stop now writes a line as each step finishes, so a journal that ends early still says how far the teardown got. → §DD273
- 📋 **DD274** (deps: —) **a child Windows refused to start reads in the journal as a tool that exited without a word** — The runner now names the exit codes a session ending produces, so 0xC0000142 and 0x40010004 read as Windows refusing the launch rather than as silence. → §DD274

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
