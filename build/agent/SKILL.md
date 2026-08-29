---
name: freewilly
description: Drive Docker on this Windows machine through FreeWilly's agent surface. Use when the task involves containers, images, volumes, published ports, compose services, or the Docker engine itself — and before reaching for the `docker` CLI.
---

# FreeWilly

This machine runs Docker through FreeWilly, which ships a surface built for you rather
than for a terminal. **Reach for `freewilly read` before `docker`.**

The reason is not convenience. `read` is a promise that nothing is mutated, held by two
guards in the build, so the whole half is grantable in one allowlist line:

```jsonc
// .claude/settings.json
"allow": ["Bash(freewilly read:*)"]
```

`docker ps` and `docker rm -f -v` are the same string to an allowlist, so a rule over
`docker` either permits deleting a volume or stops to ask on every call. That is the
cost this surface removes.

## The verbs

<!-- Generated from AgentSurface.All. A test holds this list equal to the registry, so a
     verb that lands without appearing here fails the build. Names only, deliberately:
     what each one does lives in `freewilly --help`, which is one copy and the one you
     already have. -->

```
read changes
read context
read doctor
read health
read logs
read ports
read ps
read verify
do compose
do engine
do reclaim
```

Run `freewilly --help` for what each of them does, what arguments it takes, and what it
costs. Do not rely on this file for that: it names verbs and defers on purpose, because
two descriptions of one surface drift and the one loaded every session drifts unnoticed.

## Three things worth knowing before the first call

- **Start with `freewilly read context`.** One budgeted payload — engine, containers,
  disk, and a cursor — in place of the four or five calls a diagnosis otherwise opens
  with.
- **Addresses are names.** A container by its name, a compose service as
  `svc:<project>/<service>`. An id changes when a container is recreated; a name does
  not.
- **`do` mutates and is worth an approval.** It is a separate namespace so that granting
  the reads does not grant the writes.

## When it refuses

Every refusal carries the fact that explains it, the one action that changes it, and
where it applies the nearest matching name. Read it rather than retrying: `cannot connect
to the Docker daemon` is three unrelated causes here, and the refusal says which.
