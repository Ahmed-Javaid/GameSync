One setting: its title and a one-line description on the left, its control on the right.

Provide `title`, `description`, the control as children (a `Switch`, `PillTabs`, `ColorSwatchPicker`, a value with a small button), optional `tag` ("Later") and `disabled`. Rows inside a `Card` are split by `line-100` hairlines. Use `stack` when the control needs the full width.

- The description says what happens, not what the control is: "Shown after you quit, so nothing pops up mid-game."
- Features that aren't in v1 show a `Later` tag and a disabled control, never a working-looking one.
