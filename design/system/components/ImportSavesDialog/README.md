Dialog that brings saves from a shared zip into GameSync as pinned versions.

Provide `zipName`, `from` (when it was packed), `items` (`{id, name, art, match: matched, not-installed or unknown, versions, size, warn?, removed?}`), `onImport(ids)` and `onClose`.

- Matched games add their versions as pinned, never current; not-installed games wait until the game is found; unknown games can't be ticked.
- `warn` shows in `warn` under the game (a save made on another account may not load); `removed` shows in `danger` (a program file was left out).
- The footer counts games and versions and repeats that nothing replaces current saves. After Add, it tells the person to restore from the game's history.
