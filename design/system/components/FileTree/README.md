A game's save files, each with a checkbox: ticked files are backed up and synced, unticked ones stay on this PC only. It's how a person opts files in or out, in a game's Properties under Saves.

Provide `items` (places, folders with `children`, files, registry keys) and `onToggle(id, checked)`. `fileTree.toggle(items, id, checked)` returns the tree with that item and everything under it changed and its folders' boxes updated; `fileTree.count(items)` adds up what's backed up, for the line above the tree.

- A top-level item is a place: the folder as this PC has it, in `mono`, with how many files it holds. A folder's box is ticked, empty, or mixed when some of it is in.
- `tag` marks settings files (Settings) and registry keys (Registry). `note` says why something is out: "Skipped: logs" (the defaults), "You left it out".
- `locked` items can't be ticked and show a lock with the reason: a program file is never backed up (R1).
- Sizes and counts sit at the right in `mono`. The tree sits in a well (`surface-well`, `edge-well`), rows hover on `bg-300`.
- Unticking never deletes: the files stay on the PC, and older versions keep them.
