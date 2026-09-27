Dense monospaced table set on `bg-000`, the heart of the save manager.

Provide `columns` (`{key, label, align?, path?, render?}`), `rows` (each with an `id`), and for selection `selected` + `onSelect(ids)`; a header checkbox selects all. `command` prints a prompt line above the table (`> saves --pc DESKTOP`), `toolbar` goes on its right. A `status` column renders `StatusBadge` in plain form; `path` columns truncate with the full path in the tooltip.

- Show every fact that helps: path, versions, size, last backup. This is where the extra detail belongs, not in the launcher.
- Clicking a row toggles it; selected rows fill with `secondary-soft`, and every status colour still reads on it.
