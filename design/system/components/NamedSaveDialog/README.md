Dialog that keeps a game's save as it is now under a name, like "Before Lady Maria", on every PC (BAK-18). It replaced the small box New named save… opened (the owner, 1 Oct 2026: like Add a place, not just a grey box).

Provide `game` (its name), `save` (`{path, files, bytes, newest, more?}`: the save as it is now), `names` (its named saves, so no name is used twice), `keepLine` for a game not syncing yet, `onKeep(name)`, `onOpen` and `onClose`; `stage: "keeping"` while it's kept, and `error` when it couldn't be.

- **What's kept**: a `FolderField` of the game's save place on this PC (`fixed`: Open in Explorer, no Change…), with its files, size and newest save, and how many more places when the save takes in several.
- **Name**: a wide field, focused when the dialog opens, with the placeholder "Before Lady Maria"; Enter keeps it. A name the game has already is turned away in `warn`, with Keep this save off: two saves under one name couldn't be told apart.
- **A game not syncing yet** (`keepLine`): a `shield` note that GameSync starts keeping its saves, with the files and size it starts with, backed up but not synced between PCs until Sync these saves (KAN-63).
- The foot says it's kept on every PC and never thinned, and that restoring it later keeps the files there first. Keep this save (`pin`, the primary) is off until there's a name; while it's kept it reads Keeping…, then the dialog closes and the named save leads its card. A failure stays in the dialog in `danger`, saying why.
- Opens from New named save… on a game's Named saves card (GameSavesScreen, `#named`) and on its page's Saves card (GameDetailScreen). Esc, the close button and Cancel close it.
