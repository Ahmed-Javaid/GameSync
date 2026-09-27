A folder the person can change: its full path in mono, a meta line, Change… and Open in Explorer.

Provide `path`, optional `label` and `meta` ("1.04 GB · 214 versions · 312 GB free on E:"), `onChange` (opens the folder picker), `onOpen`, and a `state`:

- `ok`: the normal row.
- `moving`: after a new backup folder is picked; a progress bar with `message` ("Moving 1,340 files, checking each hash"). The old folder stays in use until every hash matches.
- `missing`: the drive is unplugged; a `warn` message says backups are paused and nothing was deleted.
- `refused`: the picked folder isn't allowed (inside a game's install folder, a cloud-synced folder, Program Files or Windows); a `danger` message says why and what to pick instead.

Use it for the backup folder and the shared-zip folder.
