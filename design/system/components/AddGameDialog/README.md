Dialog that adds a game or folder of the person's own (LIB-13): a game GameSync didn't find, or any folder to keep in step between PCs, like a game server's world. It opens from the library's Add game and from first run's Something missing?.

Provide `name`, `folder` once one is picked (`{path, files, bytes, newest, programs?, state?, message?}`), `program` once one is picked (`{path}`), `setup` over first run, and `onName`, `onPickFolder` and `onPickProgram` (Windows' folder and file pickers), `onRemoveProgram`, `onOpen`, `onAdd({name})` and `onClose`.

- **Name**: what the library calls it. Picking a folder fills it in from the folder's name when it's empty; the person can change it.
- **Folder**: nothing picked shows Choose a folder…; picked, the `FolderField` with this PC's full path and what's there (files, size, when it last changed), Change… and Open in Explorer.
- **Program that uses it**, optional: with one (an old game's `.exe`), GameSync syncs when it closes, and Play starts it; without one, once the folder has been quiet for 5 minutes, so a server's world syncs between its autosaves and after the server stops. A chosen program shows in a well with Remove.
- Program files in the folder are never copied (R1). A `shield` note says so and, once a folder is picked, how many it holds (`#programs`): the save or world folder is the one to pick, not the whole game or server.
- `state: "warn"` adds a `warn` line when another game keeps saves there too (FOLD-11). `state: "refused"` (`#refused`): the `FolderField` turns refused and says why and what to pick instead, for the same folders Add a place refuses: a drive's root, a whole Windows folder, Windows or Program Files themselves, GameSync's own folders.
- The primary is **Add and sync**: the game is backed up at once and syncs from then on. It stays off until there's a name and a folder that can be added, and its tooltip says which is missing. Over first run (`setup`, `#setup`) it reads **Add**: the game joins Choose games' Sync group, ticked, and nothing syncs until Start using GameSync.
- The foot tells the person that their other PC joins by adding its own folder under the same name.
