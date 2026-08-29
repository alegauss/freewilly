// §DD49 — the social card. The og:image used to point at an svg, which every platform
// that renders a card cannot rasterise, so a shared link introduced the project as an empty
// rectangle. This rasterises the card to dist/og.png at 1200x630 on every build, so
// the card is regenerated whenever the marks or the copy on it change.
//
// §DD218 — the card is a template, not a finished drawing. Its mark is spliced in from
// public/logo.svg at build time rather than pasted into the file, because a card carrying its
// own copy of the artwork is a card that goes stale the next time the artwork changes, which is
// exactly how it came to be showing a logo the site had already replaced. The template lives
// here and not in public/ for the same reason: it is an input to this script, and nothing ever
// linked the svg that used to sit beside the png.
//
// §DD219 — and the status pill is measured, not typed. The card asks for DejaVu and takes
// whatever the build machine has, so the same markup rasterises at one width on a runner that
// ships DejaVu and another on a workstation that falls back to something condensed. A pill
// hand-fitted to one of those overflows on the other, which is how a shipped card came to have
// its chip text running out the side. resvg measures the text here and the pill is drawn round
// the answer, so it fits whatever font resolves and survives the copy being reworded.
import { readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { Resvg } from "@resvg/resvg-js";

const here = dirname(fileURLToPath(import.meta.url));
const siteDir = join(here, "..");
const cardPath = join(here, "og-card.svg");
const logoPath = join(siteDir, "public", "logo.svg");
const outPath = join(siteDir, "dist", "og.png");

// Where the mark sits on the card, and how big. Height only: the artwork is taller than it is
// wide and carries its own silhouette, so it is sized by height and left to find its width — the
// same rule .brand img and .hero-icon follow in src/index.css. A width here would squash it.
const MARK = { left: 90, top: 110, height: 148 };

// The pill's left edge, and the space left after the text ends. The dot sits 22px inside the
// left edge and the text 46px, so 26 on the right reads as the same inset once the corner
// radius is allowed for. GUTTER is the card's right margin, which the pill may not cross.
const CHIP = { left: 90, padding: 26 };
const GUTTER = 1110;

// The card names DejaVu and every rendering here has to agree about what that resolved to,
// measurement and final raster alike, or the pill is fitted to a font the card is not drawn in.
const FONT = { loadSystemFonts: true, defaultFontFamily: "DejaVu Sans" };

const card = readFileSync(cardPath, "utf8");
const logo = readFileSync(logoPath, "utf8");

const viewBox = logo.match(/\bviewBox="([\d.\-\s]+)"/);
if (!viewBox) throw new Error(`og-image: no viewBox on ${logoPath}`);
const [minX, minY, , boxHeight] = viewBox[1].trim().split(/\s+/).map(Number);

// The logo's own root <svg> cannot come along: an svg element nested in the card would bring its
// own viewport and re-fit the artwork to a box this script did not choose. Its children can.
const artwork = logo.replace(/^[\s\S]*?<svg\b[^>]*>/, "").replace(/<\/svg>\s*$/, "").trim();

const scale = MARK.height / boxHeight;
const mark = [
  `<g transform="translate(${MARK.left - minX * scale},${MARK.top - minY * scale})`,
  ` scale(${scale.toFixed(6)})">\n${artwork}\n  </g>`,
].join("");

// Where a piece of the card's own markup lands once this font has had its say. The element is
// rendered alone on a card-sized canvas, so the box comes back in the card's coordinates.
function place(element) {
  const canvas = `<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="630" viewBox="0 0 1200 630">${element}</svg>`;
  const box = new Resvg(canvas, { font: FONT }).getBBox();
  if (!box) throw new Error(`og-image: nothing to measure in ${element.slice(0, 60)}`);
  return { start: box.x, end: box.x + box.width };
}

const chipText = card.match(/<text\b[^>]*\bid="chip"[\s\S]*?<\/text>/);
if (!chipText) throw new Error('og-image: og-card.svg has no <text id="chip">');
const chipWidth = Math.ceil(place(chipText[0]).end + CHIP.padding - CHIP.left);
if (CHIP.left + chipWidth > GUTTER) {
  throw new Error(`og-image: the status pill needs ${chipWidth}px and runs past x=${GUTTER}`);
}

// A slot that is not there is a card drawn without the thing it was supposed to carry, and
// String.replace says nothing about a pattern it never found.
const filled = { "{{mark}}": mark, "{{chip-width}}": String(chipWidth) };
let svg = card;
for (const [slot, value] of Object.entries(filled)) {
  if (!svg.includes(slot)) throw new Error(`og-image: og-card.svg has no ${slot} slot`);
  svg = svg.replace(slot, value);
}

const resvg = new Resvg(svg, {
  fitTo: { mode: "width", value: 1200 },
  // the card names DejaVu explicitly; loading system fonts is what makes its text render
  font: FONT,
});
const png = resvg.render();
const buf = png.asPng();

const { width, height } = png;
if (width !== 1200 || height !== 630) {
  throw new Error(`og-image: expected 1200x630, got ${width}x${height}`);
}

writeFileSync(outPath, buf);
console.log(`og-image: dist/og.png  ${width}x${height}  (${(buf.length / 1024).toFixed(0)} kB)`);
