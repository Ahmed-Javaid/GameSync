Pill button for every action that isn't a row click.

Provide `children` (a verb naming the result), `variant` (`primary` | `secondary` | `ghost` | `danger`), optional `icon`, `size="sm"` for dense spots, and the usual button props.

- One `primary` per view: the recommended action (Continue playing, Create zip, Sync now).
- `secondary` for the other actions on the bar (Share selected, Share all).
- `ghost` for Cancel and Done. `danger` only to delete a version.
