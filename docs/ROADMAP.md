# Roadmap (active backlog)

## Priority

## Block A — The Windows engine (Docker without Docker Desktop)

- 📋 **DD187** (deps: —) **the engine host is never told the session is ending, so nothing tears it down at a shutdown** — Seven session endings in the journal are followed by no Stopped line and no host-is-done line, while every Quit writes both in the same second, so the host dies with its daemon still up. → §DD187
- 📋 **DD188** (deps: DD187) **the tray answers a session ending by spawning a stop process Windows kills before it reaches the distribution** — DD129 starts the stop through ShellExecuteEx and waits for nothing, so no session ending since it shipped has produced the Stopped line a Quit produces. → §DD188
- 📋 **DD189** (deps: —) **the daemon is killed rather than asked to stop, so no container gets a SIGTERM at a quit or a shutdown** — WslDaemonProcess.Stop kills the launcher tree and WSL2 reaps dockerd behind it, so a database container is killed rather than shut down on every teardown. → §DD189
- 📋 **DD190** (deps: —) **a distribution whose root went read-only fails with getpwnam and a pointer to a log inside it** — The 29 August start died on getpwnam(root) failed 5, which is an EIO and not a missing user, and the tray answered it by naming a log the unreadable filesystem was holding. → §DD190
- 📋 **DD191** (deps: DD190) **nothing asks whether the distribution filesystem is clean, so a dirty one is found by the start that fails** — WSL said the filesystem needed e2fsck on the mount before the fatal one, and FreeWilly read neither that nor the read-only remount that followed. → §DD191
- 📋 **DD192** (deps: —) **stdout and stderr share one buffer, so a journal line decodes half of what wsl.exe said as noise** — DD162 drains both streams into one list and Decode picks a single encoding, so a UTF-16 stdout beside a UTF-8 stderr wrote WSL_E_USER_NOT_FOUND into the journal as mojibake. → §DD192

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)

- 📋 **DD185** (deps: —) **the window, tray and CLI print em dashes the site is bound to quote** — S1 forbids the page to paraphrase a string the product prints, so the six em dashes left in the published Markdown twins can only be removed at their source. → §DD185

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
