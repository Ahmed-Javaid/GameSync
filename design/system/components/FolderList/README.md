A list of folders the person has added, each removable, with an Add folder button.

Provide `items` (`{path, meta?, tag?, icon?}`), `onRemove(item)`, `onAdd` and `addLabel`. Use a `tag` for the folder's kind ("By game ID" for folders whose subfolders are Steam app IDs).

- Game folders to scan and extra save folders live here. Removing a folder never deletes saves: its games become Not installed and their history stays.
- Paths the person adds stay in this PC's settings, never in the public repo.
