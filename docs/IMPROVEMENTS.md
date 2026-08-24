# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD181 A cached reading read as a current one

DD175 stopped the host claiming "the daemon is running" from a Windows process handle
and made it ask the machine instead. The answer is cached in `_found` and reused for
every poll of the same silence, which is right for the load — `wsl --exec` on a machine
that is already struggling is the last thing to run six times — and wrong for the
sentence.

The incident of 24 August 2026 shows the cost. The verdict line at 14:01:12 reads "the
daemon is running and no connection within 3s — 6 polls in a row", and the clause it
opens with was measured at 14:00:46, twenty-six seconds earlier. A reader takes it as
the state at the verdict. In the failure this whole supervisor exists for — a virtual
machine lost under the host's feet — those twenty-six seconds are exactly where the
daemon stops being there, so the one line that would show it is the one repeating an
older reading.

Keeping the cache and dating the clause costs nothing and answers the reader's actual
question, which is not "was the daemon up" but "was it up when you gave up on it".

### §DD182 A restart that does not name its outage

`brought the engine back (restart 1)` reports the attempt. What a reader wants from it
is the outage: how long this machine had no engine. Today that is a subtraction across
two lines and a scroll — the crossing DD174 writes, and this one — and it is the number
somebody skimming a night's journal is actually counting.

It matters most where the journal is skimmed rather than read. A host that brought the
engine back four times overnight is a different machine depending on whether each gap
was ten seconds or four minutes, and the restart count alone — the number the window
already draws from these lines — cannot tell those apart.

The host knows both ends: the first quiet poll is when the engine was last seen, and
this line is written the moment it answered again. Carrying the span in the sentence
that closes the incident keeps the whole event legible from one line, which is the
property the rest of this file is written for.

### §DD183 A balloon for a failure already repaired

DD164 gave the tray a balloon for an engine that went away on its own, and the reasoning
holds: a host that now keeps trying instead of exiting is a host that can look like
nothing happening, and a user who sees a grey dot and no explanation clicks Start on an
engine already being restarted.

What it did not separate is the outage from the blip. The balloon fires on the
Running-to-Stopped crossing itself, and on 24 August 2026 that crossing lasted ten
seconds: the host noticed at 14:01:14, the first revival attempt landed at 14:01:24, and
the user was interrupted to be told about a failure that had already been repaired. The
text makes it worse rather than better — "there is nothing to click" is precisely right
and precisely not worth a notification for something over before it was read.

The distinction the balloon needs is one the host already draws. A first revival attempt
that works is a blip; one that fails is the outage DD164 wrote this sentence for.
Waiting for that answer costs an announcement a few seconds of lateness and buys the
surface back its meaning — a balloon that only ever appears for something still
happening is one a user keeps reading.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
