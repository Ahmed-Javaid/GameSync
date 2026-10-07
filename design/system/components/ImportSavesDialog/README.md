Dialog that brings saves from a shared zip into GameSync as pinned versions.

Provide `zipName`, `from` (when it was packed), `items` (`{id, name, art, match: matched, not-installed or unknown, versions, size, warn?, removed?}`), `onImport(ids)` and `onClose`.

- Matched games add their versions as pinned, never current. A not-installed game joins the library as Not installed, kept with the zip's rules and backed up only, and its saves wait until the game is found (built 1 Oct 2026). An unknown game, one whose saves this PC can't place, can't be ticked, and its `warn` says why.
- `warn` shows in `warn` under the game (a save made on another account may not load); `removed` shows in `danger` (a program file was left out).
- `blocked` (version 51, R16): a game with an anti-cheat, or that plays only online, can't be imported, whether this PC has it or not: it can't be ticked, its line reads "Can't be imported" and the reason shows in `danger` with the shield ("Has an anti-cheat, so its saves stay with the account that made them." or "Plays only online, so its saves stay with the account that made them."). Such a zip only comes from outside GameSync, as Share leaves those games out.
- The footer counts games and versions and repeats that nothing replaces current saves. After Add, it tells the person to restore from the game's history.
