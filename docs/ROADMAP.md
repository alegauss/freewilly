# Roadmap (active backlog)

## Priority

## Block A — The Windows engine (Docker without Docker Desktop)

- 📋 **DD192** (deps: —) **stdout and stderr share one buffer, so a journal line decodes half of what wsl.exe said as noise** — DD162 drains both streams into one list and Decode picks a single encoding, so a UTF-16 stdout beside a UTF-8 stderr wrote WSL_E_USER_NOT_FOUND into the journal as mojibake. → §DD192
- 📋 **DD196** (deps: —) **the distribution ships without e2fsprogs, so nothing on it can check or repair its own filesystem** — Provisioning installs the engine and nothing else, and apk cannot run on a root that already went read-only, so the tool has to be there before it is wanted. → §DD196
- 📋 **DD199** (deps: DD196, DD190 ✅) **nothing repairs a corrupt distribution filesystem, and the check cannot run against a mounted root** — The 29 August repair took a terminate, a second distribution to run e2fsck from and a read of the disk it left attached, a sequence no user should have to reconstruct. → §DD199

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

- 📋 **DD193** (deps: —) **A build's start is printed in the daemon's UTC, so one begun at 09:49 on this machine shows on the page as 12:49** — Both the WHEN column and the detail pane render the timestamp in its own offset, and buildx reports UTC, so every time on the page is hours from the clock beside it. → §DD193
- 📋 **DD194** (deps: DD193) **A fixture capture of the builds page draws its times in the operator's zone, so two machines make two pictures** — DD38 buys a capture that is the same picture everywhere, and a start rendered in the local zone is one field the README's screenshots draw differently per machine. → §DD194
- 📋 **DD197** (deps: DD191 ✅) **no single surface says what state WSL, the distribution and the engine are in** — Diagnosing the 29 August failure took wsl.exe, dmesg, blkid, the registry and a disk query, and every reading it needed is one the product could have shown on the Engine page. → §DD197

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

- 📋 **DD198** (deps: DD197) **the agent surface has no reading for the health of WSL, the distribution or the disk** — read doctor answers for one container and nothing answers for the machine under it, so an agent asked why the engine will not start has to shell out to wsl.exe and parse it. → §DD198

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
