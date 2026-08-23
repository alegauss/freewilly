# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

### §DD168 Sixty-four characters where the dialog next door shows twelve

An anonymous volume's name is not a name somebody lost — nobody ever gave it one, so the
daemon generated a 64-character digest and that string is what `docker volume ls` prints
too. Nothing is missing. What is wrong is how much of it the list draws.

Every one of those characters is noise to the only decision this tab exists for. A
reader picking which volume to delete uses the size, the MOUNTED BY column and, for a
named one, the compose prefix. The digest distinguishes one anonymous volume from
another, and twelve characters do that as well as sixty-four while leaving the column to
the rest of the row.

The window already knows this. `VolumeRemoval.Question` writes `row.Name[..12]…` for
exactly this case, so the dialog a user reaches by clicking Delete shows a short form of
the identifier the row above it just showed in full. One window, one identifier, two
spellings — the same fault DD166 found in the containers list, where the fix was to
write the rule once and let both surfaces read it.

So the list shortens an anonymous volume the way the dialog already does, and a named
volume is left exactly as it is: a name somebody chose is the answer the column wanted.
The full digest stays reachable, because a deletion is addressed to it and a user
copying one out of the window needs all of it — the tooltip is where it goes.

### §DD169 A guess where the daemon states the fact, on the path that deletes data

`LooksAnonymous` decides by shape: sixty-four characters, all of them hex. That was a
reasonable guess when the volumes tab was written and it is no longer the best
information available.

The daemon states the fact. An anonymous volume carries `com.docker.volume.anonymous` in
its labels, `VolumeSummary.Labels` is already parsed and already read for the session
label, and `docker volume inspect` on this machine returns it on both loose volumes.
Guessing at something the answer already contains is how a list ends up disagreeing with
the engine it is describing.

The failure mode is not symmetric, which is why this is worth the change rather than
being tidy. A named volume that happens to be sixty-four hex characters — a digest used
as a name, which is what a script that names volumes after content produces — is
classified anonymous, counted in the totals line, and swept into `Prune anonymous`. That
button's own dialog promises named volumes are not touched. Whatever was inside does not
come back.

So the label decides where there is one. The shape test stays as the fallback for a
daemon old enough not to stamp it, and it stays clearly marked as a fallback so the next
reader does not take it for the rule.

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
