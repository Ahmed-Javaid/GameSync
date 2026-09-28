Pill that shows a game's one status as an icon plus a word, in colours that never change with the theme.

Provide `status`: `synced`, `playing`, `upload-pending`, `newer-in-cloud`, `conflict`, `held`, `in-use`, `not-found`, `not-available`, `blocked`, `backup-only`. Optional `label` overrides the word ("Save synced · 21:06"); `plain` drops the chip background for table cells.

- `backup-only` is a game its store already syncs; GameSync keeps a backup of every version. It reads "Synced by its store": give `label` "Synced by Steam" (or Epic, Xbox) when the store is known. Never "Backup only".
- On cover art (a `GameTile`, the `HeroBanner`) a badge takes its `-art` tint, a deep one that keeps 4.5:1 over any art in both surfaces.

- Colour follows meaning: `ok`, `warn`, `play`, `danger`, `neutral` (see the README's Colour section); never use a badge's colour for anything else.
