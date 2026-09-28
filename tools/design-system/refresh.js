// Refreshes the design system's offline snapshot after its files in design/system/ change (copied from the online
// design system): compiles tokens.css from tokens.json the way the design system's page does, and rebuilds the data
// viewer.html embeds (the stylesheet, the bundle, every card's preview and README, the Overview and Theming guides).
//   node tools/design-system/refresh.js
const fs = require("fs"), path = require("path");
const dir = path.resolve(__dirname, "../../design/system");
const read = (rel) => fs.readFileSync(path.join(dir, rel), "utf8").replace(/\r/g, "");

// tokens.css: one block per theme with every colour and shadow ({alias} as var()), then lengths, fonts and type styles.
function compileTokens(t) {
  const themes = t.color.themes.map((th) => th.id);
  const value = (tok, theme) => (typeof tok.value === "string" ? tok.value : tok.value[theme] ?? tok.value[themes[0]]);
  const css = (v) => v.replace(/^\{([A-Za-z0-9_.-]+)\}$/, "var(--$1)");
  const out = [`/* GameSync tokens, compiled from tokens.json. Themes: ${themes.join(", ")}. */`];
  themes.forEach((theme, i) => {
    out.push(i === 0 ? `:root, [data-theme="${theme}"] {` : `[data-theme="${theme}"] {`);
    for (const tok of [...t.color.tokens, ...((t.shadow && t.shadow.tokens) || [])]) out.push(`  --${tok.name}: ${css(value(tok, theme))};`);
    out.push("}");
  });
  out.push(":root {");
  for (const family of ["spacing", "radius", "size"]) for (const tok of (t[family] && t[family].tokens) || []) out.push(`  --${tok.name}: ${tok.value};`);
  for (const [key, stack] of Object.entries(t.type.families)) out.push(`  --font-${key}: ${stack};`);
  out.push("}");
  for (const group of t.type.groups) {
    for (const style of group.styles) {
      const parts = [`font-family: var(--font-${style.family || group.family})`, `font-size: ${style.fontSize}`, `line-height: ${style.lineHeight}`, `font-weight: ${style.fontWeight}`];
      if (style.letterSpacing) parts.push(`letter-spacing: ${style.letterSpacing}`);
      out.push(`.${style.name} { ${parts.join("; ")}; }`);
    }
  }
  return out.join("\n") + "\n";
}

// The Markdown the READMEs use: headings, paragraphs, lists, pipe tables, quotes, `code`, **bold** and links.
function esc(s) { return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;"); }
function inline(s) {
  const codes = [];
  s = s.replace(/`([^`]+)`/g, (m, c) => { codes.push(c); return "\u0000" + (codes.length - 1) + "\u0000"; });
  s = esc(s).replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>").replace(/\[([^\]]+)\]\(([^)]+)\)/g, '<a href="$2">$1</a>');
  return s.replace(/\u0000(\d+)\u0000/g, (m, i) => "<code>" + esc(codes[+i]) + "</code>");
}
function markdown(src) {
  const lines = src.split("\n"), out = [];
  const block = /^(#{1,6} |\s*[-*] |\||> ?)/;
  let i = 0;
  while (i < lines.length) {
    const line = lines[i];
    let m;
    if (!line.trim()) { i++; continue; }
    if ((m = /^(#{1,6}) (.*)$/.exec(line))) { out.push(`<h${m[1].length}>${inline(m[2])}</h${m[1].length}>`); i++; continue; }
    if (/^\s*[-*] /.test(line)) {
      const items = [];
      while (i < lines.length && /^\s*[-*] /.test(lines[i])) {
        let item = lines[i++].replace(/^\s*[-*] /, "");
        while (i < lines.length && lines[i].trim() && /^\s+/.test(lines[i]) && !/^\s*[-*] /.test(lines[i])) item += " " + lines[i++].trim();
        items.push(`<li>${inline(item)}</li>`);
      }
      out.push(`<ul>\n${items.join("\n")}\n</ul>`);
      continue;
    }
    if (/^\|/.test(line)) {
      const rows = [];
      while (i < lines.length && /^\|/.test(lines[i])) rows.push(lines[i++]);
      const cells = (r) => r.replace(/^\|/, "").replace(/\|\s*$/, "").split("|").map((c) => c.trim());
      out.push("<table><thead><tr>" + cells(rows[0]).map((c) => `<th>${inline(c)}</th>`).join("") + "</tr></thead><tbody>" +
        rows.slice(2).map((r) => "<tr>" + cells(r).map((c) => `<td>${inline(c)}</td>`).join("") + "</tr>").join("") + "</tbody></table>");
      continue;
    }
    if (/^> ?/.test(line)) {
      const quote = [];
      while (i < lines.length && /^> ?/.test(lines[i])) quote.push(lines[i++].replace(/^> ?/, ""));
      out.push(`<blockquote>${markdown(quote.join("\n"))}</blockquote>`);
      continue;
    }
    const para = [];
    while (i < lines.length && lines[i].trim() && !block.test(lines[i])) para.push(lines[i++].trim());
    out.push(`<p>${inline(para.join(" "))}</p>`);
  }
  return out.join("\n");
}

// A card's line-1 marker: <!-- @dsCard group="Screens" height=800 width=1280 subtitle="…" -->
function marker(src) {
  const attrs = {}, m = /^<!--\s*@dsCard([^>]*)-->/.exec(src);
  if (m) m[1].replace(/(\w+)=("([^"]*)"|(\S+))/g, (x, k, v, quoted, bare) => { attrs[k] = quoted !== undefined ? quoted : bare; });
  return attrs;
}

fs.writeFileSync(path.join(dir, "tokens.css"), compileTokens(JSON.parse(read("tokens.json"))));

const viewerPath = path.join(dir, "viewer.html");
const viewer = fs.readFileSync(viewerPath, "utf8");
const start = viewer.indexOf("var LIB = "), end = viewer.indexOf("var themeSel", start);
const data = new Function(viewer.slice(start, end) + "; return { LIB, THEMES, PAGES, DOCS };")();
data.LIB.tokens = read("tokens.css");
data.LIB.css = read("components/bundle.css");
data.LIB.bundle = read("components/bundle.js");
data.PAGES = data.PAGES.map((page) => {
  const src = read(`components/${page.name}/preview.html`), a = marker(src);
  const readme = fs.existsSync(path.join(dir, "components", page.name, "README.md")) ? markdown(read(`components/${page.name}/README.md`)) : "";
  return { name: page.name, group: a.group || page.group, height: a.height ? +a.height : page.height, width: a.width ? +a.width : page.width, subtitle: a.subtitle ?? page.subtitle, src, readme };
});
// The local README opens with a note about this snapshot, which the online one doesn't have.
data.DOCS = { Overview: markdown(read("README.md").replace(/^# GameSync design system\n\n(>.*\n)+\n/, "")), Theming: markdown(read("Theming.md")) };
const js = (v) => JSON.stringify(v).replace(/<\//g, "<\\/").replace(/<!--/g, "<\\!--");
fs.writeFileSync(viewerPath, viewer.slice(0, start) + `var LIB = ${js(data.LIB)};\nvar THEMES = ${js(data.THEMES)};\nvar PAGES = ${js(data.PAGES)};\nvar DOCS = ${js(data.DOCS)};\n` + viewer.slice(end));
console.log(`tokens.css compiled; viewer.html rebuilt with ${data.PAGES.length} cards. A new card also needs an entry in viewer.html's PAGES.`);
