Dialog that adds a place a game keeps saves, set by hand (FOLD-01): a folder or one save file GameSync didn't find, shown as every PC will read it before anything is added.

Provide `game` (its name), `place` once one is picked (`{kind, path, portable, portableNote?, files, bytes, newest, programs?, state?, message?}`), `onPick(kind)` (opens Windows' folder or file picker), `onOpen`, `onAdd({path, category})` and `onClose`.

- **Nothing picked**: Choose a folder… (the usual case) or Choose a file… (a game that keeps one save file among others).
- **Picked**: the `FolderField` with this PC's full path and what's there (files, size, newest), Change… and Open in Explorer; then **Every PC reads it as**, the portable form in `mono` (`<localLow>/Team Cherry/Hollow Knight`), so the other PCs find it in their own folders. A folder under none of Windows' own folders stays a full path, and `portableNote` says the other PCs need the same drive and folder.
- **What it holds**, a `Select`: Game saves (synced between PCs), Settings (each PC keeps its own) or Screenshots (backed up on this PC), as the game's other places do.
- Program files in it are never taken (R1); a `shield` note says how many there are. `state: "warn"` adds a `warn` line (another game keeps saves there too: a file both claim shows as an error on both games, FOLD-11).
- `state: "refused"`: the `FolderField` turns refused and says why and what to pick instead: a drive's root, a whole Windows folder (Documents, AppData, the user folder), Windows or Program Files themselves, or GameSync's own folders. Add this place stays off.
- The foot says it's backed up at the next sync, that other PCs are asked before they take a new place (R8), and that nothing in it is ever deleted. Opens from Add a place on a game's saves, from its Saves not found action, and from Properties, on Saves.
