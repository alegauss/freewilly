import { product } from "./product.generated";
import type { ProductData } from "./product-types";

// The one place the copy reads a count about this product from (DD159). Everything here
// derives from the generated module, so a sixth artefact or a twelfth provisioning step
// rewrites the sentence that states it — and a verb renamed out from under the help excerpt
// fails the build rather than printing a block that is quietly a plan.
export { product };
export type { ProductData };

const words = [
  "zero",
  "one",
  "two",
  "three",
  "four",
  "five",
  "six",
  "seven",
  "eight",
  "nine",
  "ten",
  "eleven",
  "twelve",
  "thirteen",
  "fourteen",
  "fifteen",
  "sixteen",
  "seventeen",
  "eighteen",
  "nineteen",
  "twenty",
];

/**
 * A small count as the copy says it: `spelled(5)` is "five".
 *
 * The copy is prose, and "5 rows" in a sentence reads as a table caption. Above twenty the
 * digits are what a reader wants anyway, and a page that had twenty-one provisioning steps
 * would have a bigger problem than its spelling.
 */
export function spelled(n: number): string {
  return words[n] ?? String(n);
}

/** The same, capitalised for the start of a heading or a sentence. */
export function Spelled(n: number): string {
  const word = spelled(n);
  return word.charAt(0).toUpperCase() + word.slice(1);
}

/** How many steps a provision runs. */
export function stepCount(): number {
  return product.provisioning.steps;
}

/** How many of those steps acquire an artefact — one per pinned artefact. */
export function acquireCount(): number {
  return product.provisioning.acquire.length;
}

/** How many artefacts this build pins. */
export function artefactCount(): number {
  return product.artefacts.count;
}

/** How many rows a preflight reports. */
export function rowCount(): number {
  return product.preflight.rows.length;
}

/**
 * How many items a tray menu a user opens actually shows (DD160).
 *
 * Not the number of items the menu declares: one of them exists so the strip's shape is fixed
 * and appears only once there is a release to install, and a heading that counted it would be
 * counting something most readers will never see.
 */
export function menuCount(): number {
  return product.tray.visible;
}

/** The items a menu shows, in the order it shows them. */
export function menuItems(): readonly string[] {
  return product.tray.items.filter((item) => !item.hidden).map((item) => item.caption);
}

/** The items that exist but are hidden until something reveals them. */
export function hiddenMenuItems(): readonly string[] {
  return product.tray.items.filter((item) => item.hidden).map((item) => item.caption);
}

/**
 * How many destinations of the window are views of the machine (DD165).
 *
 * Not the number of destinations the strip carries: About is one and is not a view of
 * anything on the machine, and the sentence this number sits in names the others one by one.
 * A sixth page therefore rewrites the sentence that counts it, which is the whole of DD159 —
 * and it is DD160's failure exactly, one section along, since this count was typed until the
 * Engine page made it wrong.
 */
export function destinationCount(): number {
  return product.window.machine;
}

/** The destinations, in the order the strip shows them. */
export function destinations(): readonly string[] {
  return product.window.destinations;
}

/**
 * One artefact's upstream version, keyed by its manifest id: "engine", "cli", "compose", …
 *
 * Throws on an id the manifest does not pin. A pill claiming a version for an artefact that
 * was renamed or dropped would otherwise render as "Moby undefined", which is worse than the
 * stale number this exists to prevent.
 */
export function version(id: string): string {
  const value = product.artefacts.versions[id];
  if (value === undefined) {
    throw new Error(
      `product: engine-manifest.json pins no "${id}" — it pins ` +
        `${Object.keys(product.artefacts.versions).join(", ")}`,
    );
  }
  return value;
}

/**
 * A slice of the real `--help` output, from the line naming `from` to the line naming `to`.
 *
 * DD157 found this block as a hand-picked half of the output under a title claiming to be the
 * whole of it. A slice cannot drift from what the command prints: the lines are the command's
 * own, and a verb renamed out of one of the two anchors fails the build here instead of
 * leaving an excerpt that is a plan.
 */
export function helpExcerpt(from: string, to: string): string {
  // On the whole verb and not a prefix of one (DD249). This project names a rehearsal after the
  // thing it rehearses, so `--fsck` sits under `--fsck-drill` and `--compact` under
  // `--compact-drill`; DD248 is what one such collision already cost. Today's anchors are safe by
  // luck rather than by construction, and luck is not what an excerpt of the real output rests on.
  const names = (verb: string) => (line: string) => {
    const trimmed = line.trimStart();
    if (!trimmed.startsWith(verb)) return false;
    const next = trimmed[verb.length];
    return next === undefined || next === " ";
  };

  const start = product.help.findIndex(names(from));
  const end = product.help.findIndex(names(to));

  if (start < 0 || end < 0 || end < start) {
    throw new Error(
      `product: CommandLine.HelpText has no slice from "${from}" to "${to}" — ` +
        "one of them is no longer a verb the help text names, or they have swapped order",
    );
  }

  return product.help.slice(start, end + 1).join("\n");
}
