# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

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
