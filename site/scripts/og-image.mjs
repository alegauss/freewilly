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

if (!card.includes("{{mark}}")) throw new Error("og-image: og-card.svg has no {{mark}} slot");
const svg = card.replace("{{mark}}", mark);

const resvg = new Resvg(svg, {
  fitTo: { mode: "width", value: 1200 },
  // the card names DejaVu explicitly; loading system fonts is what makes its text render
  font: { loadSystemFonts: true, defaultFontFamily: "DejaVu Sans" },
});
const png = resvg.render();
const buf = png.asPng();

const { width, height } = png;
if (width !== 1200 || height !== 630) {
  throw new Error(`og-image: expected 1200x630, got ${width}x${height}`);
}

writeFileSync(outPath, buf);
console.log(`og-image: dist/og.png  ${width}x${height}  (${(buf.length / 1024).toFixed(0)} kB)`);
