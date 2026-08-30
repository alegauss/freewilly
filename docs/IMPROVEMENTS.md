# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

### §DD245 One sequence written twice, and the copy has already gone stale

`DiskCompaction.Run` and `ElevatedCompaction.Run` are the same five beats: read the
disk, stop the engine the announced way, take the disk out of use, do the one thing that
differs, read the disk again. Only the fourth is genuinely different, and only the third
differs in a detail.

That detail is what makes this a task rather than a tidy-up.
`ElevatedCompaction.TakeItDown` was written by copying `TakeTheDistributionDown`, which
terminates the distribution, and DD238 is the whole story of what that cost: it shipped,
the suite stayed green on 1481 tests because the elevation is a seam, and it failed on
the first real press because diskpart needs the WSL2 utility VM down and a terminate
leaves it up. The copy inherited a verb that was right where it came from and wrong
where it landed.

DD204 made this argument for the check and the repair and gave the shape: one assembly,
with what differs passed in. The same shape fits here, with the acting step as the seam
and the take-down as a second one, so the two routes cannot disagree about the order the
engine comes down in.

What must survive the merge is that the elevated route runs no prune and no `fstrim`. It
is offered at the end of a compaction that has just run both, and repeating them would
be minutes of work whose result is already on the disk.

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)

### §DD246 The public prose stops at a wall that has a door now

Two files describe the Compact button, `site/public/llms.txt` and
`site/src/lib/site-content.ts`, and both end the sentence the same way: it hands the
freed blocks back "where Windows still allows it". That was exactly right when DD224
wrote it. DD237 and DD238 then put a route past the refusal, and the sentence now reads
as a limitation the product has accepted.

It is also the sentence an agent reads. `llms.txt` exists so a reader who is not a
person gets the same account as one who is, and this one would have it advising somebody
that a full virtual disk is the end of the road on a current Windows.

The same paragraph names `freewilly --fsck` as the terminal way to run the check and had
nothing to name for the compaction, because until DD242 there was nothing. There is now,
and the elevated half has a flag of its own.

What the new prose must not do is oversell it. The route needs administrator rights, it
shuts every WSL distribution down and not only this product's, and the prompt is
refusable at no cost. Those are the three facts the dialog already carries, and the page
should carry them too rather than saying the problem is solved.

DD230's guard regenerates the help and checks the direction that goes stale; this is the
half of the surface that guard does not read.
