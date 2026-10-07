// Generates the Zenith badge's shapes (design system version 44 on) once, in its 64 × 64 box: the pines and the grass,
// which are drawn with seeded randomness, as path data, and the rings of dots as numbers both sides work out the same way.
// The design system and the app then draw the very same badge:
//   node tools/design-system/zenith-art.js <output folder>
// writes src/GameSync.UI/Controls/ZenithArt.cs for the app, and <output folder>/zenith_art.js, the design system's
// ZENITH_ART: it replaces the one in the design system's components/bundle.js (above ZENITH_PRINT), published as usual.
// Change the badge's trees, grass or rings here, never in either output; its sky, sun and peaks are written out in both
// the design system's ZENITH_PRINT and the app's GsZenithBadge.
"use strict";
const fs = require("fs");
const path = require("path");

const out = process.argv[2];
if (!out) { console.error("usage: zenith-art.js <output folder>"); process.exit(1); }

// The design's own seeded randomness (Park and Miller), so the same trees and grass come out every time.
function lcg(seed) {
  let s = seed;
  return () => { s = (s * 16807) % 2147483647; return (s - 1) / 2147483646; };
}

// A number in a path: two decimals at most, no trailing zeros.
function f(v) {
  const t = v.toFixed(2).replace(/0+$/, "").replace(/\.$/, "");
  return t === "-0" || t === "" ? "0" : t;
}

// One pine: drooping layers of branches, narrower to the top, round (x, base) with its height and half-width.
function pine(x, base, h, w, r) {
  const tiers = Math.max(4, Math.round(h / 2.2));
  const left = [], right = [];
  for (let i = 1; i <= tiers; i++) {
    const t = i / tiers, y = base - h + h * t, ww = w * (0.18 + 0.82 * t) * (0.85 + 0.3 * r());
    left.push([x - ww, y + 0.6], [x - ww * 0.35, y - 0.4]);
    right.push([x + ww * 0.35, y - 0.4], [x + ww, y + 0.6]);
  }
  const pts = [[x, base - h], ...right, [x + 0.5, base], [x - 0.5, base], ...left.reverse()];
  return "M" + pts.map(([a, b]) => `${f(a)} ${f(b)}`).join("L") + "Z";
}

// (x, base, height, half-width): the hazy pines behind, then the dark ones either side and in front of the summit's flanks.
const FAR = [[6, 50, 12, 3.2], [10, 49, 15, 3.6], [15, 50, 10, 3], [49, 50, 11, 3], [54, 49, 16, 3.6], [58, 51, 12, 3.2]];
const NEAR = [[3, 58, 22, 4.6], [9, 58, 26, 5], [14.5, 58, 18, 4], [50, 58, 20, 4.2], [55.5, 58, 27, 5.2], [61, 58, 21, 4.6], [20.2, 60, 24, 3.8],
  [46.4, 60, 22, 3.6], [24.6, 59, 10, 2.4], [41.6, 59, 11, 2.6]];

// (radius, spacing, dot radius, opacity): round the sun (halo, sun) or round the medal (rim), per size: fine from 48px,
// mid from 30px, small under that.
const RINGS = {
  fine: {
    halo: [[15.5, 1.7, 0.42, 0.9], [17.4, 1.8, 0.38, 0.75], [19.3, 1.9, 0.33, 0.6], [21.2, 2.0, 0.28, 0.45], [23.1, 2.1, 0.24, 0.32], [25, 2.2, 0.2, 0.22]],
    sun: [[3, 1.6, 0.32, 0.35], [5.4, 1.6, 0.34, 0.32], [7.8, 1.6, 0.36, 0.3], [10.2, 1.6, 0.38, 0.3], [12.4, 1.6, 0.4, 0.45]],
    rim: [[30.4, 1.25, 0.22, 0.55]],
  },
  mid: { halo: [[16, 2.2, 0.55, 0.8], [19, 2.4, 0.45, 0.5], [22, 2.6, 0.38, 0.3]], sun: [], rim: [] },
  small: { halo: [], sun: [], rim: [] },
};

// The trees come first from the same seed, so every size has the same ones; the grass follows, finer and denser from 48px.
function build(size) {
  const fine = size === "fine", mid = size === "fine" || size === "mid";
  const r = lcg(7);
  const far = FAR.map(t => pine(...t, r)).join("");
  const near = NEAR.map(t => pine(...t, r)).join("");
  let grass = [];
  if (mid) {
    const thin = [], middle = [], thick = [];
    const cut = fine ? [0.38, 0.48] : [0.5, 0.6];
    const n = fine ? 96 : 40;
    for (let i = 0; i < n; i++) {
      const x = 1 + i * (62 / n) + r() * 0.6;
      const h = 3 + r() * (fine ? 8 : 6);
      const lean = (r() - 0.5) * 3;
      const sw = (fine ? 0.28 : 0.4) + r() * 0.3;
      r(); // each blade's own opacity in the sketch; one for all here
      let blade = `M${f(x)} 64.5Q${f(x + lean * 0.3)} ${f(64 - h * 0.55)} ${f(x + lean)} ${f(64 - h)}`;
      if (fine && r() > 0.45) {
        const fy = 64 - h * (0.45 + r() * 0.25);
        const fx = x + lean * 0.45;
        const side = r() > 0.5 ? 1 : -1;
        blade += `M${f(fx)} ${f(fy)}q${f(side * 1.2)} -1 ${f(side * 2.2)} -2.6`;
      }
      (sw < cut[0] ? thin : sw < cut[1] ? middle : thick).push(blade);
    }
    const widths = fine ? [0.33, 0.43, 0.53] : [0.45, 0.55, 0.65];
    grass = [thin, middle, thick].map((b, i) => [b.join(""), widths[i]]).filter((_, i) => [thin, middle, thick][i].length > 0);
  }
  return { far, near, grass };
}

const built = { fine: build("fine"), mid: build("mid"), small: build("small") };
for (const b of Object.values(built)) {
  if (b.far !== built.fine.far || b.near !== built.fine.near) throw new Error("every size must have the same trees");
}
const data = { far: built.fine.far, near: built.fine.near, grass: { fine: built.fine.grass, mid: built.mid.grass, small: built.small.grass }, rings: RINGS };

fs.mkdirSync(out, { recursive: true });
const js = "var ZENITH_ART = " + JSON.stringify(data) + ";\n";
fs.writeFileSync(path.join(out, "zenith_art.js"), js);

const csString = s => '"' + s.replace(/\\/g, "\\\\").replace(/"/g, '\\"') + '"';
const csRings = rings => "[" + rings.map(([r, st, d, o]) => `(${r}, ${st}, ${d}, ${o})`).join(", ") + "]";
const lines = [
  "// Generated with the design system's Zenith badge (version 44 on): the same shapes in a 64 x 64 box. Change them in",
  "// tools/design-system/zenith-art.js and run it (docs/handoff.md says how) rather than editing this.",
  "namespace GameSync.UI.Controls;",
  "",
  "/// <summary>",
  "/// The Zenith badge's shapes (design system version 44 on): the pines and the grass, drawn once with the design system's",
  "/// seeded randomness, and the rings of dots as (radius, spacing, dot radius, opacity), for fine (48px and up), mid (30 to",
  "/// 47px) and small (under 30px) badges.",
  "/// </summary>",
  "internal static class ZenithArt",
  "{",
  `    internal const string Far = ${csString(data.far)};`,
  "",
  `    internal const string Near = ${csString(data.near)};`,
  "",
];
for (const size of ["fine", "mid", "small"]) {
  const name = size[0].toUpperCase() + size.slice(1);
  lines.push(`    internal static readonly (string Path, double Width)[] ${name}Grass = [` + data.grass[size].map(([p, w]) => `(${csString(p)}, ${w})`).join(", ") + "];");
  lines.push("");
  for (const part of ["halo", "sun", "rim"]) {
    lines.push(`    internal static readonly (double Radius, double Step, double Dot, double Opacity)[] ${name}${part[0].toUpperCase() + part.slice(1)} = ${csRings(RINGS[size][part])};`);
  }
  lines.push("");
}
lines.push("}");
const cs = path.join(__dirname, "..", "..", "src", "GameSync.UI", "Controls", "ZenithArt.cs");
fs.writeFileSync(cs, lines.join("\n") + "\n");
console.log(path.join(out, "zenith_art.js") + " (" + js.length + " bytes)");
console.log(cs);
