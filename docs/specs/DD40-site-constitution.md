# DD40 — The public site — what shio.viglet.org already settled

> Roadmap: [ROADMAP.md](../ROADMAP.md) Block **H** ·
> Design: [IMPROVEMENTS.md](../IMPROVEMENTS.md) §DD40–§DD51
> Status: 📋 designed, not started · deps: none — DD40 is the floor the rest stand on
> Scope: this document is the **constitution for the published surface** — who reads it,
> the laws a page is judged by, the page map, the build, and the decomposition into
> DD40–DD51. [DD23](DD23-agent-first-freewilly.md) is the constitution for the agent
> surface and [DD34](DD34-window-constitution.md) for the desktop one; this is the
> constitution for the *third* surface, the one both audiences meet first.
> Each task keeps its own rationale in `IMPROVEMENTS.md`; this is the premise under all
> of them.

---

## 1. Where this comes from

Two projects by the same author now make the same argument. DD23 states DockerDesk's:

> DockerDesk is a Docker installation whose primary operator is a coding agent. The
> agent runs, inspects and diagnoses. You install, approve and intervene.

Viglet Shio states its own in one line on its home page — *the CMS whose operator is
Claude Code* — and it has already **built the site that sells that argument**:
[`shio-site`](https://shio.viglet.org/), a Vite + React 19 + TypeScript + Tailwind v4 +
shadcn/ui workspace, ten prerendered routes, a Markdown twin beside every one of them,
its copy in a single module, its measured figures generated rather than typed, and a
`workflow_dispatch` publish whose gates are the build's own.

This project's site was a **single hand-written 1080-line `docs/index.html`**: dark-only,
one page, no second page reachable, every claim typed into the markup that displays it,
and a status section listing DD7 as *in progress* and DD8–DD12 as *open* — all five of
which have shipped. Its footer states that `LICENSE` and `NOTICE` "are DD13 on the
roadmap and not written yet", which stopped being true the day DD13 shipped both files.
The two sites are selling the same thing in the same voice, and only one of them can be
added to without retyping.

This document is `shio-site` restated for DockerDesk, so the borrowing is **structural
rather than cosmetic** — the same reasoning DD34 applies to claude-tray's design system.
Where a decision is taken differently here, §3 says why.

**What is already right and is not up for revision.** The voice: full sentences, a
reason attached to every claim, no feature bullets that could describe anything. The
azure palette and the engine's own three state colours, which are the hex values the
tray actually draws. The stated non-goals, written down where they can be pointed at.
The honest-status section, which says there is nothing to download yet — a site that
implied otherwise would be the worst defect on this list. The trademark disclaimer. The
SVG diagram of the pipe, which is the one picture that explains the product. Every law
below is written to keep these, not to trade them for a framework.

---

## 2. Who reads this, and the split that follows

| Reader | Arrives asking | Leaves with |
|---|---|---|
| **A person** on a managed laptop | "can I have Docker without the licence?" | the preflight, the four commands, the honest status |
| **A person** running an agent | "what does my agent get out of this?" | the read/do split, the one allowlist line |
| **An agent** sent to evaluate it | "what is this and what does it cost me?" | `llms.txt`, a Markdown twin, a manifest |

The third reader is the one the current site cannot serve at all, and it is the one this
project's own constitution says is the primary operator. A Claude-first product whose
public site answers an agent only in hydrated React markup is failing its own argument
in the first thing anybody reads. That is DD42, and it is the task with the least
defensible absence.

---

## 3. The laws

Binding, in the same sense as DD23's. A page that breaks one is wrong even if it looks
better.

### S1 — A claim is about a real surface

A route, a flag, a verb, an error body or a pinned version that appears on the site is
the one the build produces. `docs/specs/`, `docs/ROADMAP.md` and the source are the
authority for each. This is Shio's first content rule and it exists because **a
marketing claim that has gone false is invisible** until somebody reads the page against
the product — the one class of defect a site cannot notice about itself.

The corollary is what makes it enforceable: **claim the shape, not the inventory**,
wherever a shape is what is true. Shio's licence claim survived 195 files changing
licence because it was a *link* to `LICENSE` rather than the string `GPL v3`. Ours must
survive DD13 the same way.

### S2 — A status is derived, never typed

The current status section is the proof of the law: five shipped tasks are listed as
unshipped because the rows are prose in markup, and nothing anywhere fails when the
roadmap moves under them. `roadkeep export --json` already emits exactly this payload —
blocks with their counts, every task with its id, marker, block, symptom and why, and
the next ready line — and roadkeep's own `export --site` exists because a projection
into a page is a thing it expects to own.

So: **a figure about this project's own progress is generated or it is not on the page.**
This is Shio's *a measured number is generated, never typed* (SH533), and DD43 satisfied
it by generating the whole board from `roadkeep export --json`. → **DD43**

**DD91 reversed the conclusion and kept the law.** The board was correct for as long as it
existed and answered a question only the author has: a reader arriving to ask whether they
can have Docker without the licence was handed a burndown of somebody else's backlog. So
the page publishes no progress figure at all, which satisfies this law the other way — what
cannot be derived does not go on the page, and what nobody asked for does not either. The
figures that stayed are the ones about the product rather than about the plan: the measured
token costs, generated from `agent-budget.json` and the verb registry, which is why
`scripts/surface.mjs` outlived `scripts/roadmap.mjs`. The backlog is reachable by the one
reader it answers, as a link out to the governed files and never as a projection of them.

### S3 — Copy lives in one module; sections only render it

All page copy in `site/src/lib/site-content.ts`. The sections import and iterate it.
This is what turned Shio's twelve-tool list into *a visible wrong row* rather than a
number in a sentence nobody re-derives, and it is the mechanism S1 depends on: a claim
you can see as an array element is one a reviewer can check. → **DD40**

### S4 — One route table, checked in both directions

No router. A `ROUTES` map the prerender and the client both read, and a per-route
`{ path, title, description }` table beside it, asserted against each other **at import
time**. The two failures this prevents are both silent: a route with no page prerenders
the landing page under somebody else's `<title>`, and a page missing from the table
never gets a file at all. Every `<head>` patch is a replace-or-throw for the same
reason. → **DD41**

### S5 — Every route has a Markdown twin

`<path>.md` beside every route, converted from the same render — not written separately,
because the copy lives in the content module but the *composition* lives in the JSX, and
generating Markdown from the data alone would re-declare that composition and let the
two drift. The nav and the footer never reach a twin; a call to action is dropped from it
by an attribute, because it converts a reader and costs an agent forty identical words
per page. → **DD42**

### S6 — The theme follows the OS

The app's own rule, already stated in `MainWindow.xaml`: `ThemeMode="System"`, light and
dark following the OS. The site is dark-only, which makes the product and its site
disagree about the same question in public. Tokens defined for light, redefined under
`prefers-color-scheme: dark` and again under an explicit toggle, with the stored choice
applied before first paint — the prerendered HTML paints before the bundle, so a theme
class read after hydration is a flash of the wrong one. → **DD40**

### S7 — Only the reader moves the window

Anything that keeps its own content in view scrolls **its own element**.
`scrollIntoView()` scrolls every scrollable ancestor including the document, so an
autoplaying panel that calls it drags a reader who has scrolled past it back every few
seconds. Shio shipped that defect and then shipped the source lint that prevents it
(SH566); we take the lint without the defect. → **DD44**, asserted by **DD51**

### S8 — The gate is the build

No step exists to check what the build already refuses. The publish job runs typecheck →
build → prerender, and the prerender's own throws are the route and `<head>` gates. A
gate that runs twice is one that can be satisfied by the copy nobody kept current. →
**DD50**

### S9 — Publishing is deliberate

`workflow_dispatch` only. A deploy that fires on every push is one nobody can hold still
while reviewing it, and the site is the one artefact where a defect is public
immediately. → **DD50**

### S10 — The honest status is a feature, not a disclaimer

Every alternative in the comparison matrix has something it is genuinely better at, and the
matrix says what. A page that wins every row is one nobody believes, which is the argument
Shio's `/compare` is built on. → **DD45**, **DD47**

**The availability half of this law expired as it was written to.** It said: there is no
release, the site says so above the fold in the badge and in a section of its own, and it
keeps saying so *until DD15 ships*. DD15 shipped, with DD14, so DD92 took the badge wording,
the call to action and the section out. What replaces the claim is not silence: the
build-from-source section begins with `git clone` and a .NET SDK, which no reader mistakes
for an offer to download. That makes it load-bearing — it is not to be shortened while it is
the only thing carrying this.

The risk was raised and accepted: no tag exists yet, so a visitor still cannot download
anything and the site no longer says so in as many words. The moment a tag exists that
section stops being the whole truth, and the task belongs to whoever publishes the first
release — filed then, against an artefact that exists, which is the reasoning §5 used to
keep an `/install` page off the map.

---

## 4. The law that was deferred, and is now in force

Shio's site publishes **generated token figures** from the properties file its Java
suite asserts every response against — four bars, regenerated on every build, a renamed
key failing it.

This project had no such file, so the law was written as a deferral: *the benchmark has not
been written, so the site must not carry a measured number.* **DD93 closed it, because the
condition it was waiting on is gone.** DD23 built the benchmark, DD65 measured the shaped
side against it, and `agent-budget.json` is now this site's generated source exactly the way
`token-budgets.properties` is Shio's.

So the law is in force in its full form, and it has two halves:

- **A measured cost on this page is generated, or it is not on the page.** The baseline, the
  per-shape costs and the ceiling beside every verb are read out of `agent-budget.json` and
  the verb registry by `scripts/surface.mjs`, and `scripts/surface.test.mjs` fails the build
  on a row citing a shape the benchmark does not measure or a verb the registry does not
  dispatch. A figure typed into the copy is the defect this prevents.
- **An unmeasured cost is not a figure on the page.** What remains an acceptance criterion is
  stated as one — the hero transcript's per-call costs are targets the benchmark must prove
  or falsify, its own note says so, and they are never rendered as a chart or reported as
  achievements.

What this law does *not* license is a figure about the project's own progress. That was DD43,
and DD91 reversed it: see S2. Measured is about the product; a burndown is about the plan.

---

## 5. The page map

Ten routes, mirroring `shio-site`'s five-plus-depth-pages shape.

| Route | What it is | Task |
|---|---|---|
| `/` | The landing page: the argument, in the order an agent then a reader meets it | DD40 |
| `/claude-code` | The agent's operator: the read/do split, the one allowlist line, the plugin | DD46 |
| `/compare` | Against Docker Desktop, Rancher Desktop, Podman Desktop and plain WSL2 | DD47 |
| `/features/preflight` | Four rows, each with its remedy, and the hypervisor-before-firmware order | DD48 |
| `/features/engine` | Upstream Moby into an owned distro: seven steps, pinned by digest | DD48 |
| `/features/pipe` | Why a named pipe and not a port, and the ACL that is the reason | DD48 |
| `/features/window` | The tray, the container list, the logs, the shell, images and volumes | DD48 |
| `/features/agent-surface` | The context pack, `read doctor`, teaching errors with the Windows join | DD48 |
| `/index.md` &c. | The Markdown twin of each of the above, plus `manifest.json`, `llms.txt` | DD42 |

The landing section order is an argument, not a feature list. Shio opens on a session
because what it sells is how cheaply an agent operates the product; that is true here
too, so the hook is DD23 §3.1's five calls (DD44), and only then: who does what (the two
actors) → the ten laws → the pipe, which is the mechanism → the preflight → the window
→ nothing resident → non-goals → the honest status → build from source → convert.

A `/install` page is deliberately **not** on this list. There is nothing to download, so
the landing page's build-from-source section is the whole truth today; the page that
replaces it is work for after DD15, filed then rather than designed now against an
artefact that does not exist.

---

## 6. The build, and where it lives

```
site/                        a self-contained Node workspace — this repo's first
  index.html                 the template every route is patched from
  src/lib/site-content.ts    all copy (S3)
  src/App.tsx                ROUTES (S4)
  src/entry-server.tsx       the route table + render() (S4)
  src/components/sections/   one component per landing section, rendering content
  scripts/prerender.mjs      per-route HTML + the Markdown twin + manifest.json
  scripts/roadmap.mjs        roadkeep export --json → generated module (S2)
  scripts/og-image.mjs       og.svg → og.png, 1200x630
  scripts/*.test.mjs         the site's own claims (S1), run by node --test
  public/                    robots.txt, sitemap.xml, llms.txt, favicon, og.svg
```

Three facts about this repository shape it:

- **It is a .NET repository and this is its first Node workspace.** The site is
  standalone — no dependency on the solution, and `dotnet build` neither builds nor
  needs it. The CI job is a separate one.
- **`docs/` is roadkeep's, not a web root.** `docs/ROADMAP.md`, `docs/CHANGELOG.md`,
  `docs/IMPROVEMENTS.md` and `docs/specs/` are governed files, and a build that empties
  its output directory would delete them. So the site builds to `site/dist/` and the
  published tree is assembled from there — never written into `docs/`.
- **The base path is derived, not chosen.** The site is served at
  <https://alegauss.github.io/freewilly/>, so Vite's `base` is `/freewilly/` and every
  canonical, sitemap entry and asset path carries that prefix. This was written as *the URL
  does not change*, and DD59 falsified it: GitHub Pages derives the prefix from the
  repository name, so renaming the repository moved every published address at once.
  Moving Pages from *deploy from `main` `/docs`* to *deploy from a GitHub Actions artefact*
  is a repository setting the publish task must state, because the job is inert until it is
  changed. The safe order this section described — the old `docs/index.html` keeps serving
  while the new site is built — held until DD89, which removed that folder because keeping
  two publishes meant every correction had to be made twice. From DD89 on the artefact is
  the only publish, so the setting is what the site is waiting on rather than a tidy-up.

---

## 7. Block H — the published surface

| Task | What it is |
|---|---|
| **DD40** | The workspace and the landing page: Vite, React, Tailwind, shadcn; copy in one module; the OS theme (S3, S6) |
| **DD41** | The prerender and the route pair, both directions, replace-or-throw (S4) |
| **DD42** | The Markdown twin per route, `manifest.json`, and `llms.txt` refreshed (S5) |
| **DD43** | `/status` and every progress figure generated from `roadkeep export --json` (S2) — **reversed by DD91**: the apparatus is removed and the site publishes no progress figure |
| **DD44** | The hero: the five-call session, scrolling its own list (S7) |
| **DD45** | The ten laws and the two actors — the description this project ships under (S10) |
| **DD46** | `/claude-code` — the read/do split and the one allowlist line |
| **DD47** | `/compare` — checkable rows, and what each alternative wins (S10) |
| **DD48** | The five `/features/<slug>` depth pages, one record each |
| **DD49** | The social card and the brand marks: a rasterised 1200x630 og image |
| **DD50** | The publish job: `workflow_dispatch`, the build as the gate (S8, S9) |
| **DD51** | The site's own claims, asserted beside the scripts that own them (S1) |

DD40 is the floor; DD41 follows it and DD42, DD48, DD50 and DD51 follow DD41. DD43,
DD44, DD45, DD46, DD47 and DD49 need only the workspace, so after DD41 the block is
wide rather than deep.

Nothing in this block depends on Block F or Block G, and that is deliberate: the site
describes the designed surface with its status visible (S2, S10), so it can be built and
published while the installer and the CLI are still open. The reverse dependency is the
real one — **DD32's plugin is discovered through `/claude-code`**, and a surface nobody
discovers is one nobody uses.

---

## 8. Non-goals

- **No docs set.** This is a project page, not documentation. `README`, the specs and
  `--help` are the documentation, and a docs site is a fourth surface to keep true.
- **No blog, no changelog page, no release notes.** `CHANGELOG.md` is roadkeep's and
  `git log` is authoritative; a second telling of either is a second thing to drift.
- **No analytics, no fonts fetched from a third party at page load, no cookie banner.**
  The product claims no telemetry and no account. A site that measures its readers to
  advertise a tool that measures nothing is the same defect the product refuses.
- **No live demo instance.** Shio's remaining site task is a public read-only CMS; the
  equivalent here would be a Windows machine somebody else can drive, which is not a
  thing to expose to the internet at any price.
