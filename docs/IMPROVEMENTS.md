# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

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
