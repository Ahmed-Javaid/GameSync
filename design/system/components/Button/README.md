Pill button for every action that isn't a row click.

Provide `children` (a verb naming the result), `variant` (`primary` | `secondary` | `ghost` | `danger`), optional `icon`, `size="sm"` for dense spots, and the usual button props.

- One `primary` per view: the recommended action (Continue playing, Create zip, Sync now).
- `secondary` for the other actions on the bar (Share selected, Share all).
- `ghost` for Cancel and Done. `danger` only to delete a version.
- **Busy** (`busy`, `busyLabel`; KAN-80): from the moment it's pressed until its job is done, it says what it's doing ("Syncing", "Backing up", "Restoring", "Keeping", "Starting") with three dots that count up after the words, and a spinner takes its icon's place. It keeps its colour and isn't disabled: a greyed-out button looks broken (the owner, 1 Oct 2026). It takes no clicks meanwhile, keeps at least its width so nothing beside it jumps, shows at once and stays at least 0.4 s, so a quick job doesn't flicker. A screen reader hears it busy, with its new words. A job that takes longer than that, or moves files, also shows a `JobProgress` where its result will show.
