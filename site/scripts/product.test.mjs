// DD159 — the join surface.test.mjs already asserts for the verbs, for the counts.
//
// Five of the eight drifts DD157 corrected were numbers: seven provisioning steps against
// eleven, three artefacts against five, four preflight rows against five, and a --help block
// edited by hand under a title claiming to be the command's output. Each was true when typed.
// This is the gate that was missing — it holds the generator to its four sources, and holds
// the copy to the generator, so a count cannot be typed back in.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const siteDir = join(dirname(fileURLToPath(import.meta.url)), "..");
const repoRoot = join(siteDir, "..");

// The generated module is TypeScript whose payload is pure JSON, so it is read as text and its
// object literal parsed out — no build step required. The same trick surface.test.mjs uses.
function loadGenerated(file, exportName) {
  const text = readFileSync(join(siteDir, "src", "lib", file), "utf8");
  const anchor = text.indexOf(`export const ${exportName}`);
  assert.ok(anchor >= 0, `${file} no longer exports ${exportName}`);
  const start = text.indexOf("{", anchor);
  const end = text.lastIndexOf("}");
  return JSON.parse(text.slice(start, end + 1));
}

/**
 * Whether a help line names this verb, on the whole verb and not a prefix of one.
 *
 * DD248 is what the prefix spelling cost: `--compact` shipped, the generated help never carried
 * it, and the guard below answered the question with the `--compact-drill` line that was already
 * there. The collision is not an accident — this project names a rehearsal after the thing it
 * rehearses, so `--fsck` sits under `--fsck-drill` and there will be more.
 *
 * One function for every guard in this file since DD249, because the second copy was the one in
 * the excerpt test, and a guard written from the same assumption as the thing it guards is the
 * one arrangement that cannot catch anything.
 */
function names(line, verb) {
  const trimmed = line.trimStart();
  if (!trimmed.startsWith(verb)) return false;
  const next = trimmed[verb.length];
  return next === undefined || next === " ";
}

const product = loadGenerated("product.generated.ts", "product");
const content = readFileSync(join(siteDir, "src", "lib", "site-content.ts"), "utf8");
const featurePages = readFileSync(join(siteDir, "src", "lib", "features.ts"), "utf8");
const diagrams = readFileSync(join(siteDir, "src", "lib", "diagrams.ts"), "utf8");
const llms = readFileSync(join(siteDir, "public", "llms.txt"), "utf8");
const source = (...parts) => readFileSync(join(repoRoot, ...parts), "utf8");

/** The tray-menu diagram's markup alone, so a caption elsewhere cannot satisfy it. */
function trayDiagram() {
  const at = diagrams.indexOf("export const trayMenuDiagram");
  assert.ok(at >= 0, "diagrams.ts no longer exports trayMenuDiagram");
  const end = diagrams.indexOf("</svg>`;", at);
  assert.ok(end > at, "the tray-menu diagram is not a closed svg");
  return diagrams.slice(at, end);
}

/** How many top-level entries an array literal named `field:` in the copy module has. */
function entriesIn(field) {
  const anchor = content.indexOf(`${field}: [`);
  assert.ok(anchor >= 0, `site-content.ts no longer declares ${field}: [`);

  let depth = 0;
  let entries = 0;
  for (let i = content.indexOf("[", anchor); i < content.length; i += 1) {
    const c = content[i];
    if (c === "[") {
      depth += 1;
      if (depth === 2) entries += 1;
    }
    if (c === "]") {
      depth -= 1;
      if (depth === 0) return entries;
    }
  }

  throw new Error(`site-content.ts has an unclosed ${field}: [`);
}

test("the step count is ProvisioningStep's own member count", () => {
  const provisioner = source("src", "FreeWilly.Core", "Engine", "EngineProvisioner.cs");
  const body = provisioner.slice(provisioner.indexOf("public enum ProvisioningStep"));
  const members = [...body.slice(0, body.indexOf("\n}")).matchAll(/^\s{4}([A-Z][A-Za-z]*),/gm)];

  assert.ok(members.length > 0, "no members parsed out of ProvisioningStep");
  assert.equal(product.provisioning.steps, members.length);
});

test("the acquire steps and the pinned artefacts are the same set", () => {
  // What "one per artefact" rests on. A sixth artefact with no step to acquire it, or a step
  // acquiring something the manifest no longer pins, makes that sentence false in a way only
  // this direction catches.
  const manifest = JSON.parse(source("src", "FreeWilly.Core", "Engine", "engine-manifest.json"));
  const ids = Object.keys(manifest).filter((key) => key !== "comment");

  assert.deepEqual([...product.provisioning.acquire].sort(), [...ids].sort());
  assert.equal(product.artefacts.count, ids.length);
});

test("every pinned version and host is the manifest's, verbatim", () => {
  const manifest = JSON.parse(source("src", "FreeWilly.Core", "Engine", "engine-manifest.json"));

  for (const [id, version] of Object.entries(product.artefacts.versions)) {
    assert.equal(version, manifest[id].version, `${id} version`);
  }

  const hosts = new Set(
    Object.keys(manifest)
      .filter((key) => key !== "comment")
      .map((key) => new URL(manifest[key].url).hostname),
  );
  assert.deepEqual([...product.artefacts.hosts].sort(), [...hosts].sort());
});

test("the preflight rows are the ones PreflightInspection declares and Run returns", () => {
  // Both halves, because they can drift apart: a constant declared and never added to Run is a
  // row the page counts and the product never prints. The first run of the generator undercounted
  // both by one and agreed with itself — Wsl2 carries a digit, and a letters-only pattern dropped
  // it from each — which is why the ids are asserted here and not just the number.
  const inspection = source("src", "FreeWilly.Core", "Preflight", "PreflightInspection.cs");
  const declared = [
    ...inspection.matchAll(/public const string [A-Za-z0-9]+ = "([a-z0-9-]+)";/g),
  ].map((m) => m[1]);
  const returned = [...inspection.matchAll(/Check[A-Za-z0-9]+\(facts\)/g)].length;

  assert.deepEqual(product.preflight.rows, declared);
  assert.equal(product.preflight.rows.length, returned);
  assert.ok(product.preflight.rows.includes("wsl2"), "the WSL2 row is missing from the count");
});

test("the page lists exactly as many preflight rows as the product reports", () => {
  // The heading states the number and this list is what a reader counts against it. DD157 found
  // them disagreeing; the heading is generated now, and this is the other end of that.
  assert.equal(entriesIn("  rows"), product.preflight.rows.length);
});

test("the tray menu is what TrayMenu builds, item for item", () => {
  // Order included, because the strip is built in one place so that what a photograph shows is
  // what ships — DD140 moved the window to the front and the page's own drawing kept the old
  // order for two tasks. The hidden item is separated here rather than filtered away: what the
  // heading must not count is the one thing about this menu a reader cannot verify by opening it.
  const trayMenu = source("src", "FreeWilly.Tray", "TrayMenu.cs");
  const captions = Object.fromEntries(
    [...trayMenu.matchAll(/internal const string (\w+Text) = "([^"]+)";/g)].map((m) => [
      m[1],
      m[2].replaceAll("&", ""),
    ]),
  );

  assert.ok(Object.keys(captions).length > 0, "no captions parsed out of TrayMenu");
  assert.equal(product.tray.visible, product.tray.items.filter((i) => !i.hidden).length);
  assert.ok(product.tray.items.length > product.tray.visible, "no item is hidden any more");

  for (const item of product.tray.items) {
    assert.ok(
      Object.values(captions).includes(item.caption),
      `the generated menu names "${item.caption}", which TrayMenu declares no caption for`,
    );
  }

  assert.equal(product.tray.items[0].caption, captions.WindowText);
  assert.equal(product.tray.items.at(-1).caption, captions.QuitText);
});

test("the window's destinations are the ones the nav strip carries", () => {
  // DD165. The same join the tray menu has, one section along: the window section opens with a
  // count and then names the destinations one by one, so a page added to the strip and not to
  // the sentence leaves the site counting to four on a window that shows five. That is exactly
  // the drift DD160 corrected, and it happened again the moment a destination was added.
  const strip = source("src", "FreeWilly.Tray", "Ui", "MainWindow.xaml");
  const carried = [...strip.matchAll(/<RadioButton[^>]*?Content="([^"]+)"/gs)].map((m) => m[1]);

  assert.ok(carried.length > 0, "no destinations parsed out of MainWindow.xaml");
  assert.deepEqual(product.window.destinations, carried);

  // About is a destination and is deliberately not one of the machine's views, so the two
  // numbers must differ by exactly the About the strip carries.
  assert.ok(carried.includes("About"), "the strip no longer carries About");
  assert.equal(product.window.machine, carried.length - 1);
});

// There is deliberately no test that the window section NAMES each destination it counts, and
// the omission is worth more written down than left to be rediscovered. The tray section has
// one because its captions are distinctive strings — "Start engine" appears in the copy for
// exactly one reason. A destination is a single common word: "engine" is this product's central
// noun and appears in the window section's own first bullet, "images" and "volumes" are used
// throughout the agent surface. A substring assertion over those passes whether the sentence is
// right or wrong — measured, by deleting the Engine sentence and watching it stay green — and a
// gate that cannot fail is worse than no gate, because it is read as coverage.
//
// What is gated instead is the number, which is the half that goes wrong in silence: the count
// above comes from the strip, and "no count the generator states is typed in the copy as well"
// keeps it from being typed back in.

test("the page names every item the menu shows, and does not count the hidden one", () => {
  // DD160. The heading states the number and the bullets are what a reader counts against it,
  // so one bullet per item is the shape that makes the two inseparable. This said four while
  // describing three, which no count on its own would have caught.
  const shown = product.tray.items.filter((item) => !item.hidden);

  assert.equal(entriesIn("  splitList"), shown.length);
  for (const item of shown) {
    assert.ok(
      content.includes(`"${item.caption}"`),
      `the tray section does not name "${item.caption}"`,
    );
  }

  for (const item of product.tray.items.filter((item) => item.hidden)) {
    assert.ok(
      !content.includes(`"${item.caption}"`),
      `the tray section lists "${item.caption}", which the menu hides until there is one`,
    );
  }
});

test("the drawing of the menu is a drawing of this menu", () => {
  // The captions in the SVG are hand-placed text nodes and cannot be generated — x and y are
  // chosen per line. So they are asserted instead: a renamed item fails the build rather than
  // leaving a picture of a menu nobody ships, which is what this drawing was.
  const svg = trayDiagram();

  for (const item of product.tray.items.filter((i) => !i.hidden)) {
    assert.ok(svg.includes(`>${item.caption}<`), `the diagram does not draw "${item.caption}"`);
  }
});

test("llms.txt states the menu the tray has and the Quit the product does", () => {
  // The agent-readable twin of the same claims, and the one a model quotes back. It carried the
  // pre-DD128 Quit — "the only thing that stops the engine is the menu item that says so" —
  // under a count that named four of five items.
  assert.ok(
    !llms.includes("Quitting the\n  tray leaves the engine running"),
    "llms.txt still says quitting leaves the engine running, which DD128 reversed",
  );
  assert.ok(
    !/the only thing that stops the engine/i.test(llms),
    "llms.txt still claims one menu item is the only thing that stops the engine",
  );

  // Over the text with its wrapping collapsed, not over the file. llms.txt is hard-wrapped,
  // so a caption falls across a line break the moment a sentence before it gets a word
  // longer, and this asserted where the paragraph happened to fold rather than what it said.
  const flat = llms.replace(/\s+/g, " ").toLowerCase();
  for (const item of product.tray.items.filter((i) => !i.hidden)) {
    assert.ok(
      flat.includes(item.caption.toLowerCase()),
      `llms.txt does not name the "${item.caption}" menu item`,
    );
  }
});

test("the depth pages read their counts through the generated module too", () => {
  // features.ts carried its own copy of every number on the landing page — the preflight row
  // count in a title, an og:description and a heading, and the step count in three more. A
  // depth page is where a reader goes to check the summary.
  assert.match(featurePages, /from "\.\/product"/);
  for (const call of ["rowCount()", "stepCount()", "artefactCount()", "acquireCount()"]) {
    assert.ok(featurePages.includes(call), `features.ts no longer states its count with ${call}`);
  }
});

test("no count the generator states is typed in the copy as well", () => {
  // The regression this whole task is about. Each of these is a sentence that was true when it
  // was written and went stale with nothing to notice, so the exact wording is refused rather
  // than merely replaced — a count typed back in beside a generated one is the same defect.
  //
  // Both modules, because the depth pages carried their own copies of the same numbers and a
  // reader who goes to one to check the other is the person the drift reaches first.
  const typed = [
    "Eleven steps",
    "Eleven unattended",
    "eleven steps",
    "in five rows",
    "Five rows",
    "five common causes",
    "Five checks",
    "five artefacts",
    "the five pinned artefacts",
    "Five things this project has decided against",
    "Moby 29.7.2",
    "Four items",
  ];

  for (const phrase of typed) {
    assert.ok(
      !content.includes(phrase),
      `site-content.ts types "${phrase}" — state it from scripts/product.mjs instead`,
    );
    assert.ok(
      !featurePages.includes(phrase),
      `features.ts types "${phrase}" — state it from scripts/product.mjs instead`,
    );
  }
});

test("a verb is never answered by a longer one that begins with it", () => {
  // DD249. The matcher above is shared by every guard in this file and by helpExcerpt on the page,
  // so this is the one place the rule itself is stated rather than relied on. Both pairs below are
  // real: the project names a rehearsal after the thing it rehearses.
  assert.ok(!names("  --compact-drill  rehearse the compaction", "--compact"));
  assert.ok(!names("  --fsck-drill     rehearse the repair", "--fsck"));

  // And it still answers the verb it is actually given, whether the line ends after it or not.
  assert.ok(names("  --compact        hand back what the disk holds", "--compact"));
  assert.ok(names("  --compact-drill  rehearse the compaction", "--compact-drill"));
  assert.ok(names("  --autostart", "--autostart"));

  // The page slices the published excerpt with a matcher of its own, in TypeScript this file
  // cannot import. Asserted on its source instead, because a rule the guard follows and the page
  // does not is the arrangement DD249 was filed about, one layer down.
  const page = readFileSync(join(siteDir, "src", "lib", "product.ts"), "utf8");
  const excerpt = page.slice(page.indexOf("export function helpExcerpt("));

  assert.match(excerpt, /next === undefined \|\| next === " "/);
  assert.ok(
    !/=> \(line: string\) => line\.trimStart\(\)\.startsWith\(verb\)/.test(excerpt),
    "helpExcerpt is back to matching a prefix, so a longer verb can move where the slice begins",
  );
});

test("the help block on the page is a slice of the real help text", () => {
  // DD157 found this block as a hand-picked half of the output under a title claiming to be the
  // whole of it, and the half had itself drifted: it printed a pipe path the command does not.
  // A slice cannot — the lines are the command's own.
  const anchors = [...content.matchAll(/helpExcerpt\("([^"]+)", "([^"]+)"\)/g)];
  assert.equal(anchors.length, 1, "the page no longer slices the help text");

  const [, from, to] = anchors[0];
  const start = product.help.findIndex((line) => names(line, from));
  const end = product.help.findIndex((line) => names(line, to));

  assert.ok(start >= 0, `the help text no longer names "${from}"`);
  assert.ok(end >= start, `the help text no longer names "${to}" after "${from}"`);

  const excerpt = product.help.slice(start, end + 1);
  assert.ok(excerpt.length > 1, "the excerpt is one line, which is not the engine verbs");
  assert.ok(excerpt.some((line) => line.includes("--provision")), "the excerpt lost --provision");
});

test("the generated help text is CommandLine's, with its verb constants resolved", () => {
  // Resolved rather than raw: HelpText is an interpolated raw string, so an unresolved hole
  // would put "{PreflightVerb}" on the page where a verb belongs.
  const commandLine = source("src", "FreeWilly.Tray", "Cli", "CommandLine.cs");

  assert.ok(
    !product.help.some((line) => /\{[A-Za-z]+\}/.test(line)),
    "an interpolation hole survived into the generated help text",
  );

  for (const line of product.help) {
    for (const verb of line.trimStart().match(/^--[a-z-]+/) ?? []) {
      assert.ok(
        commandLine.includes(`"${verb}"`) || commandLine.includes(`${verb} `),
        `the help text names ${verb} and CommandLine.cs does not`,
      );
    }
  }

  // And back the other way, which is the direction that actually goes stale (DD230). The check
  // above catches a verb removed from the command and left on the page; nothing caught a verb
  // added to the command and never regenerated, and three of them reached the site that way,
  // shipped in DD214, DD215 and DD221 and missing here until somebody happened to run a build.
  //
  // Read out of the help text itself rather than out of the router, because that is the string
  // the generator copies: a verb the command does not print is not one this artefact is missing.
  const printed = [...commandLine.matchAll(/^ {2,}(--[a-z-]+)/gm)].map(([, verb]) => verb);
  assert.ok(printed.length > 5, "the help text in CommandLine.cs no longer lists verbs");

  for (const verb of new Set(printed)) {
    assert.ok(
      product.help.some((line) => names(line, verb)),
      `CommandLine.cs prints ${verb} and the generated help does not: run npm run generate`,
    );
  }
});

test("the copy reads its counts through the generated module", () => {
  // The import is the mechanism, and a page that stopped importing it would go back to typing
  // numbers with nothing to notice — which is the state this task found.
  assert.match(content, /from "\.\/product"/);
  for (const call of ["rowCount()", "stepCount()", "acquireCount()", "artefactCount()", "menuCount()"]) {
    assert.ok(content.includes(call), `the copy no longer states its count with ${call}`);
  }
});
