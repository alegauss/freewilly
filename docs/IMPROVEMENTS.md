# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

### §DD258 Following the replacement, not just naming it

DD257 shipped the cheap half: a follow whose container is replaced says so and tells the
caller to run it again. That is honest, and it costs a call.

Calls are the unit this surface is argued in, and the flow it costs one on is the
canonical one. `do compose up` recreates every service, and the read that pairs with it
is `read logs svc:shop/api --follow --until "ready"`. Across a recreate that read ends
early with an instruction rather than an answer, so the session pays a call to be told
to pay another.

The address is what makes re-attaching defensible. `svc:<project>/<service>` names a
role rather than a container, and a caller who asked about the role has said it does not
care which id fills it. A container name behaves the same way, since compose reuses
`shop-api-1`. An id prefix does not and must keep ending: a caller who named one
container meant that one.

Against it: a follow silently spanning two containers shows a line from each with
nothing marking the seam, which is DD257's confusion moved rather than removed. The seam
has to be visible in the payload.

The bounds stay the deadline, the budget and the pattern, so this adds no fourth way to
run forever. How long to look before calling it gone is the open question.

Acceptance: a follow on a service address crosses one recreate without being run again,
the seam is visible in the payload, and an id address still ends.

## Block H — The public surface (the site a reader and an agent both read)
