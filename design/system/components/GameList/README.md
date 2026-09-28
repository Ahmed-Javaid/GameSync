Every game by name, in groups, like Steam's list beside its covers: the library's left column.

Provide `groups` (`{id, label, games, collapsed?}`: Favourites first, then the view's other games), `selected` (the game whose page is open), `onSelect` and `onMenu` (a right-click). A game is `{id, name, art, status, statusLabel, installed}`.

- A row is 36px: the small cover (Steam's 300×450 capsule, 20×30 on `radius-sm`; just the letter on `bg-300` when there's no art) and the name in `body-strong` at 13px. The open game's row fills with `secondary-soft`; hover is `bg-300`.
- The status shows under the name, as a plain `StatusBadge`, only when the game needs you (Conflict, Held for review, Files in use, Saves not found, Blocked) or is Playing. Synced, Synced by Steam and the rest stay on the tile and the game's page, so the list stays quiet and what needs you stands out.
- A game not installed on this PC is dimmed: its name in `ink-faint`, its cover at 55%. Its screen-reader name says "not installed on this PC".
- Group headings are `overline` with the count in `mono`; a click opens or closes the group. Empty groups aren't shown.
