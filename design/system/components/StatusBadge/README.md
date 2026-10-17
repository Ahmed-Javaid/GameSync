Pill that shows a game's one status as an icon plus a word, in colours that never change with the theme.

Provide `status`: `synced`, `playing`, `upload-pending`, `not-synced`, `newer-in-cloud`, `conflict`, `held`, `in-use`, `not-found`, `not-available`, `blocked`, `backup-only`, `not-syncing`. Optional `label` overrides the word ("Save synced · 21:06"); `plain` drops the chip background for table cells.

- **The words since version 57** (built from version 56's Guide; a friend, 8 Oct 2026: "It should show not synced, instead of uploading pending"): `upload-pending` reads **Sending**, a version on its way to the cloud, shown only while it goes or until the PC is back online; `not-synced` (neutral, the PC) is a game backed up on a PC with no cloud connected, never Upload pending; `not-syncing` reads **Not backed up**, as Not syncing yet read too like Not synced.

- `backup-only` is a game its store already syncs; GameSync keeps a backup of every version. It reads "Synced by its store": give `label` "Synced by Steam" (or Epic, Xbox) when the store is known. Never "Backup only".
- `not-syncing` is a game GameSync found but whose saves nobody chose to sync yet: the hero of Home and of its page says so. A tile shows no badge for it, like a synced one, so the covers stay about games.
- On cover art (a `GameTile`, the `HeroBanner`) a badge takes its `-art` tint, a deep one that keeps 4.5:1 over any art in both surfaces.

- **Synced is a cloud with a check, never a bare tick** (version 35; the owner, 3 Oct 2026: "tick could mean anything"): on your PCs and in the cloud. `backup-only` is a shield with a check: kept safe, not synced between PCs by GameSync (its store syncs it, or it's Backed up by choice). `not-syncing` keeps the plain cloud: a cloud with no check yet.
- Colour follows meaning: `ok`, `warn`, `play`, `danger`, `neutral` (see the README's Colour section); never use a badge's colour for anything else.
