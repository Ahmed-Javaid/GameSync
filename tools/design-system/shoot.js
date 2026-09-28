// Renders design-system cards to PNG files at full size, with Edge (or Chrome) headless, for looking at a design up
// close next to the app's own snapshots:
//   node tools/design-system/shoot.js <output folder> [--theme dark|light|sakura|…] [--surface glossy|solid] <Card> [<Card> …]
// A card is a folder in design/system/components with a preview.html. Screens render at their card size (1280 × 800).
const fs = require("fs"), path = require("path"), { execFileSync } = require("child_process");
const dir = path.resolve(__dirname, "../../design/system");
const args = process.argv.slice(2);
const take = (name, fallback) => { const i = args.indexOf(name); if (i < 0) return fallback; const v = args[i + 1]; args.splice(i, 2); return v; };
const theme = take("--theme", "dark"), surface = take("--surface", "glossy"), hash = take("--hash", "");
const [out, ...cards] = args;
if (!out || cards.length === 0) { console.error("usage: shoot.js <output folder> [--theme dark] [--surface glossy|solid] <Card> …"); process.exit(1); }
fs.mkdirSync(out, { recursive: true });

const viewer = fs.readFileSync(path.join(dir, "viewer.html"), "utf8");
const start = viewer.indexOf("var LIB = "), end = viewer.indexOf("var themeSel", start);
const { LIB } = new Function(viewer.slice(start, end) + "; return { LIB };")();
const read = (rel) => fs.readFileSync(path.join(dir, rel), "utf8").replace(/\r/g, "");
const browser = ["C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe", "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe",
  "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe"].find((p) => fs.existsSync(p));
if (!browser) { console.error("Neither Edge nor Chrome was found."); process.exit(1); }

for (const card of cards) {
  const src = read(`components/${card}/preview.html`);
  const size = /width=(\d+)/.exec(src.split("\n")[0]), height = /height=(\d+)/.exec(src.split("\n")[0]);
  const w = size ? +size[1] : 1000, h = height ? +height[1] : 600;
  // The surface is kept in the page's storage; the card reads it as it starts.
  const head = `<script>try{localStorage.setItem("gamesync.surface",${JSON.stringify(surface)})}catch(e){}<\/script>` +
    `<style>${LIB.tokens}</style><style>${LIB.css}</style><script>${LIB.react}<\/script><script>${LIB.reactDom}<\/script><script>${LIB.bundle}<\/script>`;
  const html = src.replace(/<!--[\s\S]*?-->/, "").replace("<head>", "<head>" + head)
    .replace(/<html([^>]*)>/, (m, a) => `<html${a} data-theme="${theme}">`);
  const name = `${card}-${theme}-${surface}${hash ? "-" + hash.replace(/[^A-Za-z0-9]+/g, "-") : ""}`;
  const page = path.resolve(out, `${name}.html`), png = path.resolve(out, `${name}.png`);
  fs.writeFileSync(page, html);
  execFileSync(browser, ["--headless=new", "--disable-gpu", "--hide-scrollbars", `--window-size=${w},${h}`, "--virtual-time-budget=4000",
    `--screenshot=${png}`, "file:///" + page.replace(/\\/g, "/") + (hash ? "#" + hash : "")], { stdio: "ignore" });
  fs.unlinkSync(page);
  console.log(png);
}
