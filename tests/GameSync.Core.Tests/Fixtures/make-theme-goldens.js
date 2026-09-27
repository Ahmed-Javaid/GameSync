// Regenerates theme-goldens.json from the design system's own theme engine (design/system/components/bundle.js),
// which the app's C# port must match. Run from the repository root:
//   node tests/GameSync.Core.Tests/Fixtures/make-theme-goldens.js
const fs = require('fs'), path = require('path');
const root = path.resolve(__dirname, '../../..');
const bundle = fs.readFileSync(path.join(root, 'design/system/components/bundle.js'), 'utf8');

// The bundle only needs React to define its components; the theme engine never calls it.
global.window = { React: { createElement() {}, useState() {}, useRef() {}, useEffect() {} } };
new Function(bundle)();
const theme = global.window.GameSync.theme;

const modes = [{ mode: 'dark' }, { mode: 'light' }, { mode: 'dark', pureBlack: true }];
const cases = [];
for (const p of theme.presets.filter(p => !p.dynamic)) for (const m of modes) cases.push({ preset: p.id, ...m });
for (const s of theme.swatches) for (const m of modes) {
  cases.push({ preset: 'arcade', ...m, primary: s.id });
  cases.push({ preset: 'tidal', ...m, secondary: s.id });
}
for (const accent of ['#0078d4', '#e81123', '#107c10', '#ffb900', '#744da9', '#2d2d2d', '#ffffff'])
  for (const m of modes) cases.push({ preset: 'windows', ...m, accent });

const out = cases.map(c => ({ choice: c, tokens: theme.build(c) }));
fs.writeFileSync(path.join(__dirname, 'theme-goldens.json'), JSON.stringify(out, null, 1) + '\n');
console.log(`${out.length} theme choices written.`);
