# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

### §DD250 Proving a path is gone

`read verify --request /path` answers one question: does this path answer. A 404 is
therefore always `[FAIL]`, with the remedy "this path is not what was expected".

Half the checks an agent runs after a change are the opposite claim. A page was removed,
a route was renamed, an endpoint was retired: the proof of the work is that the path
does **not** answer, and the run that proves it is the run that prints red. The session
then has to hold in its head that this particular red is the green one — which is the
failure mode the whole verdict-plus-remedy shape exists to avoid, and it costs a
paragraph of reasoning in the transcript every time.

Measured here: after six pages were removed from a WordPress project, `read verify` was
called on four paths, two of which had to be gone. Both correct removals reported
`[FAIL]`, and nothing distinguished them from the mapping being broken.

The shape that fits is an expectation on the row: `--expect 404`, or a small set
(`--expect 404,410`), turning the check into "this path answers with what I said" and
letting the remedy line say which status arrived instead. `--request` with no `--expect`
keeps meaning "2xx or 3xx", so nothing already written changes.

Acceptance: a call naming the status a removed path must return exits 0 and prints
`[ok]`, and one that gets a different status prints the status it got.

### §DD251 Following a log without leaving the surface

`read logs` reads what a container has already printed: `--since`, `--level`, `--dedup`,
`--budget`. It does not stay attached, so a run an agent wants to watch — a database
seed, a migration, a build inside a container — has no read verb, and the documented
answer is `docker compose logs -f`.

That is the one hole in an otherwise complete read surface, and it costs more than the
keystrokes. `Bash(freewilly read:*)` covers every diagnostic a session makes except this
one, so the exception is also the line that has to be approved separately, and a session
that has already dropped to the docker CLI once tends to stay there for the next check
too — which is how `docker compose ps` gets typed in a project whose own briefing says
it should not be.

Following is still a read: nothing is mutated, and the token budget is the reason to be
careful, not a reason to refuse. The budget flags `read logs` already has are what make
it affordable — a followed stream needs `--budget` to be a stop condition rather than a
cap, plus a deadline (`--timeout`) and, ideally, `--until <pattern>`, which is what an
agent actually wants: return when the line I am waiting for arrives, the way `read
verify --wait` already returns the moment a service answers.

Acceptance: a session can watch a container's output to a named line or a deadline
without typing `docker`, and the stream is bounded by tokens or time, never open-ended.

## Block H — The public surface (the site a reader and an agent both read)
