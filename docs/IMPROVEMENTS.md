# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD226 A refusal met once should not be paid for twice

DD224 fixed the ending and left the asking. The plan still says Docker stops, the build
cache goes, and the virtual disk hands those blocks back to Windows — then somebody
agrees, waits out the stop, and is told Windows turned the mechanism off. The dialog
described a result the machine cannot produce, and the price of finding out was every
container going down.

Once is unavoidable and twice is not. Nothing can be known before the first attempt: WSL
resolves the distribution before it checks the policy, so `--manage` against a name that
does not exist answers `WSL_E_DISTRO_NOT_FOUND` and says nothing about sparse support,
and probing the engine's own distribution would convert a live disk on a machine where
the policy is enabled. Measured on 29 August 2026, both of them.

What is left is memory. The refusal is recognised already, by the flag WSL names rather
than its translated prose, so the run that meets it knows exactly what it met. Writing
that down where the page can read it costs a file and turns the second press into a
sentence instead of an interruption.

The page has the shape for it. `ToolsAreReady` already lets the check's dialog say
whether a fetch is owed, and this is the same question about a different button. One
that explains itself beats one that has to be tried, and beats one silently removed: the
flag is disabled rather than gone, and may come back.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
