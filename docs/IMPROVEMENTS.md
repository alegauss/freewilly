# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

### §DD171 A promise kept by making the feature useless

DD154 shipped the check off and argued it well: the site promised the only network
traffic this tool makes is the five pinned artefacts during a provision somebody asked
for, and one that shipped on would have made that sentence false before anyone chose
anything.

**That promise was kept by making the feature useless.** A user who never opens the tray
menu never turns it on, so the release exists and their copy never hears of it. The
1.0.1 notes already carried the consequence — upgrading from 1.0.0 is a manual download,
because 1.0.0 had no check to find it with. Off by default is that same failure a
version later, by choice.

claude-tray is this project's reference for how the tray behaves, and it checks on every
launch and every six hours with no switch anywhere. Replicating that reverses DD154's
default rather than refining it, and the ledger says so through `supersede`.

What the traffic is stays worth stating rather than hiding: four requests a day to
`api.github.com` for one release's tag, carrying this product's name and version. No id,
no token, no account, nothing about the machine. The sentence on the site and in
`llms.txt` changes to say that in the same commit — a promise quietly outgrown is worse
than one openly revised.

What does not change is everything after the check: the digest is still verified against
the published SHA-256, and installing still asks first, because it stops an engine that
may have containers on it.

### §DD172 The click the news already invited

The balloon is the only thing on screen at the moment the news arrives. Clicking it does
nothing, so a user who reacts to it has to find the tray icon, open the menu and read
down it — three deliberate actions to reach an offer they were just told about and were
already reaching for.

claude-tray wires `BalloonTipClicked` straight to applying the update, and that is the
affordance being replicated. The menu item stays exactly as it is: the balloon is gone
in eight seconds and the offer is not, so the click is the fast path and never the only
one.

The guard is what makes this safe rather than a trap. This tray's balloon carries more
than release news — a failed start, an engine that went away — and a click has to
install only when the balloon on screen was the one announcing a release. So what is
remembered is not "a release exists" but "the balloon showing now is that release's",
cleared whenever anything else is said through the same surface.

Everything downstream is untouched. The click reaches the same path the menu item
reaches: the question that names what installing costs, the digest checked against the
published SHA-256, and the engine stopped only after somebody agreed to it. A one-click
install that skipped any of that would be a different feature wearing this one's name.

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
