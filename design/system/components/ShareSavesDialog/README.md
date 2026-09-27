Dialog that packs saves into one zip for sharing, either picked saves or everything.

Provide `games` (`{id, name, art, size, totalSize, versionCount, blocked?, versions: [{id, label, when, pc, size, pinned?}]}`), `mode` (`pick` or `all`), `initialSelection` (game ids, from the table's selection), `expanded` (games open to show versions), `saveRoot`, `outDir`, `onCreate(request)` and `onClose`.

- **Choose saves**: ticking a game takes its latest version; the chevron opens its versions so the person can add older or pinned ones. The footer shows games, versions, the zip name and total size as they change.
- **Share all**: packs the whole save folder and shows its size up front; "Only the latest save of each game" makes it smaller.
- **Locked games**: give a game `blocked` with the reason ("Ships an anti-cheat and has an online mode, so it can't be shared."). It shows locked and can't be ticked, and Share all names it as left out.
- After Create zip it shows progress, then the path with Copy path and Show in folder.
- Always keep the note that only save data goes in the zip and that a README inside explains a manual restore.
