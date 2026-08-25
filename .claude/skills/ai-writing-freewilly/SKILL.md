---
name: ai-writing-freewilly
description: FreeWilly's house rules for the writing pass, layered on the general ai-writing skill. Use together with it whenever you write or edit text a human will read in this repository: site copy, README, llms.txt, docs/specs, CONTRIBUTING, commit messages, PR bodies, UI strings, error and log text, or a comment that argues rather than labels. Run it against your own draft before you finish, not only when asked to review something. Covers the ban on em dashes in published prose, the generated counts rule, and where the prose lives.
---

# The writing pass, in this repository

Every word in this repository was written by a model, so "does it read like a model wrote
it" is not a review someone else does. It is a step in writing it.

The general pass is the `ai-writing` skill, which every project on this machine gets, and
it carries the procedure this one builds on, `reference/AI_WRITING_CHECKLIST.md`. Read it
there when you need the *why* or an example. This file is what the pass means **here**,
and where the two say something different, this file wins.

## The one principle

**Fix the content, not the words.** The tell is almost always vague, inflated or
unsupported prose, and the fix is a specific fact or a deletion. Two corollaries that
matter more in this repo than anywhere else:

1. **Never invent a specific to replace a vague one.** If de-puffing a sentence needs a
   figure, a version, a count or a date you do not have, cut the sentence or flag it. This
   repo has machinery that makes that unnecessary, described under *Counts* below.
2. **Density is the signal, never a single word.** One `crucial`, one triple, is nothing.
   Do not strip a word on sight. Evaluate the cluster, or leave it.

When you are not sure, say so in the handover instead of making a confident bad edit.

## No em dashes in published prose

**The `—` is not house style here, and it is not to be reintroduced.** The site copy, the
README and `llms.txt` once carried 207 of them: two of every three sentences in
`features.ts`, one in two in the README. A mark that frequent has stopped being a choice.
It was also doing work a word should do, leaving the reader to infer the relation between
the two halves of a sentence where `because`, `so` or `and` would have said it outright.
They were replaced one at a time, by the relation each sentence actually had.

The first pass over this defended them by citing DD40 §1. That reading was wrong: §1
protects the **reason attached to every claim**, not the punctuation that attaches it. A
colon, a comma, a `because` or a new sentence all carry a reason. The checklist's
do-not-flag entry covers an em dash *in isolation*, while B13 covers exactly this case and
says to replace most of them.

So when you reach for a `—`, name the relation instead:

| what the dash was doing | write instead |
|---|---|
| joining a claim to its reason | `because`, `so`, or a colon |
| an aside inside a sentence | parentheses, or a pair of commas |
| an afterthought bolted on | a full stop and a new sentence |
| a term before its definition, in a list | a colon, used the same way down the list |

Two things are **not** covered by this rule, and both must stay:

- **Product output quoted verbatim.** `preflightTerminal` in `diagrams.ts`, the fenced
  blocks in `README.md`, the mock window and tray tooltip in the SVG diagrams. Those em
  dashes come from `ReportText.cs`, `PreflightInspection.cs`, `StateIcon.cs` and
  `BuildRow.cs`. S1 says a depicted surface is the one the build produces, so editing the
  quotation to suit a writing rule would make the page wrong. Change the string in the
  product, or change neither.
- **C# doc comments.** 721 of them. Not published, and not prose a reader meets.

## What else is settled here, and is not to be "fixed"

- **`✓ ~ ✗`** in the compare matrix, and the `→` in terminal transcripts and pack lines.
  Those are data and rendered output, not decoration.
- **`⬇` on the download button.** That is what the control does.
- **Bold lead-ins in a list** (`{ b: "No refresh button." }, " The list is a view…"`). The
  checklist's B13 targets *mechanical* `**Term:** description` scaffolding; these are the
  genuine term-definition case it exempts, and each one carries a reason after it.
- **Formal register, `However`, `Notably`, perfect grammar, curly quotes.** On the
  do-not-flag list. Not evidence of anything.

## What the pass actually catches here

- **Emoji glued to the front of a line of prose.** The most recognisable mannerism of a
  generated page. It came out of `hero.meta` and `download.facts`; keep it out. The card
  `icon:` fields are different, because those sit in an `.ico` slot of their own and are a
  design decision, so raise them rather than silently emptying a div.
- **A count or a version typed into a sentence.** See *Counts* below.
- **Section A artifacts**: `utm_source=chatgpt.com`, `contentReference`, `turn0search0`,
  `[cite: 1]`, `[Your Name]`, "Let me know if…". A sweep across the repo finds none today,
  so one appearing means a paste went in unread.
- **Trailing "-ing" analysis** (`…, ensuring …`, `…, cementing its role as …`) and
  **significance inflation** (`a pivotal moment`, `underscores its significance`). Rare in
  this repo's voice, which is why one stands out badly when it lands.
- **`serves as` / `stands as` / `represents` where `is` belongs.** Zero today. Keep it
  there.

## Counts and versions are generated, not written

This is where the checklist's "never invent a specific" rule has teeth in this repo. The
site copy states no number it typed: `product.generated.ts` is regenerated on every build
from `ProvisioningStep`, `engine-manifest.json`, `PreflightInspection.Rows` and
`CommandLine.HelpText`, and the prose reaches it through `spelled(rowCount())` and friends
(DD159). A number you type into a sentence is true the day you type it and wrong in
silence afterwards.

So: **if you are about to write a count, find the generated accessor instead.** If there
is not one, that is the finding. Say so, and do not write the number.

`public/llms.txt`, `README.md` and `docs/specs/*` have no such gate, so their figures are
the ones to check by hand against `product.generated.ts`. DD165 added the Engine page and
left `llms.txt` saying four window destinations where there are five, which is the class
of defect to look for there.

## Where the prose lives

- **`site/src/lib/site-content.ts`**: every word on the landing page and the two inner
  pages. S3 says sections render the copy and never contain it, so this is the one file to
  edit. A few strings escaped it into `FeatureIndex.tsx`, `Footer.tsx` and `Nav.tsx`, and
  the SVG captions live in `diagrams.ts`, so sweep those too.
- **`site/src/lib/features.ts`**: the five depth pages.
- **`site/public/llms.txt`**: the agent's copy, hand-maintained.
- **`README.md`, `CONTRIBUTING.md`, `NOTICE`**: the repository's own front door.
- **`docs/ROADMAP.md`, `docs/CHANGELOG.md`, `docs/IMPROVEMENTS.md`**: roadkeep's, never
  hand-edited. The pass still applies to the text you hand roadkeep.
- **Commit messages**: a conventional-commits title and a body. Same rules, and the body
  is where "reflecting a broader shift" turns up.

A note on the hard-wrapped files. `llms.txt` and `README.md` wrap at a fixed width, and
`product.test.mjs` asserts that `llms.txt` names every visible tray menu item. Assert over
text with its whitespace collapsed, never over the raw file, or the test is really
checking where a paragraph happened to fold.

## Open

The engine's own user-facing strings still carry 64 em dashes across 27 files, and the
site quotes some of them faithfully, so six survive in the published Markdown twins.
Removing those is a change to the product rather than to the copy: it moves strings the
window, the tray tooltip and the CLI print, and that the window captures and the preflight
tests assert. It belongs in a DD task of its own, not in a writing pass.

## Reporting

When you have checked rather than silently fixed, keep it to: the verdict in one line,
saying whether the text shows a *pattern* or only isolated items and how confident you
are; what you changed; what you are flagging, with a proposed rewrite, a "cut", or "needs
a fact I do not have"; anything factually unverifiable; and one line for anything on the
do-not-flag list you left alone on purpose, so it is clear it was seen rather than missed.
Keep it tight, because the point is a truer document and not a lecture.
