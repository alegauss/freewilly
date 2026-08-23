# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

### §DD166 A digest in the image column is a constant, drawn forty characters wide

The daemon fills a container's `Image` field with whatever reference the container was
created with, and falls back to the raw image id when that reference no longer resolves
— which is what happens the moment a tag is rebuilt or removed underneath a running
container. On this machine four containers created from `schools:slim` and
`schools:local` all report
`sha256:e4af4f3e24fdad8647264213f60f94ba16f67ae71a4b7281e6a0cd55de2627d1`, and the
column shows the first forty characters of it. Every one of those forty characters is
shared by every image on the machine, so the column is drawing a constant.

`docker ps` prints twelve characters of the digest and no algorithm prefix, and the
images list here already agrees with it: `ImageSummary.ShortId` strips `sha256:` and
takes twelve. The container row does not go through that rule — `ContainerRow.From`
passes `container.Image` through untouched — so one window spells the same identifier
two ways.

The fix is the rule, not the column width: a reference that parses as a digest reads as
a short id, and a reference that is a name is left exactly as the daemon said it.
Widening the column would only show more of a prefix nobody reads, and truncating with
an ellipsis is what it already does.

### §DD167 Two correct pages that read as a contradiction

Both pages are already correct, which is exactly why the pair is confusing. The
containers page shows a digest because the daemon has no name left to give. The images
page shows nothing in use because `ImageRow.From` joins on image id — the only join that
survives a tag moving — and no listed image carries the id those containers hold. A user
reading the two together concludes the window is lying, when what it is reporting is
that the images those containers run on were rebuilt or removed out from under them.

That is a real condition with a real consequence: the container is running code that is
no longer the code any tag names, and `docker restart` will not change that — only
recreating it will. It is also cheap to detect, because the containers page already
fetches both lists for DD106's project grouping: a container whose `ImageID` is absent
from the image list is in this state, and no container whose image is present ever is.

So the row should say it — near the image, in the register the rest of the window uses
for a condition rather than an error, since nothing is broken. The name to show is not
recoverable from the list endpoint (the daemon keeps it in `Config.Image`, which only
inspect returns), so the honest sentence is about the image being gone, not about which
tag it used to answer to.

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)
