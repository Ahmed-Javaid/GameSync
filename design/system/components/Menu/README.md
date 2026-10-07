A short list of choices over the page: a sort order, or what can be done to one game.

Provide `items` and `onPick`. An item is `{id, label}` with an optional `icon`; `checked` makes the menu a choice of one (the sort), with a `check` in `secondary` on the chosen item; `{sep: true}` divides it and `{heading}` labels it in `overline`.

- It floats like a dialog, so it takes the dialog's surface: `surface-dialog`, `edge-dialog`, `shadow-dialog`, `radius-md`. Items are 36px rows on `radius-sm`, hovered on `bg-300`. A small ask that opens from its button (Swap's question, a named save's Rename) floats on the same surface.
- A game's right-click menu: Play first (left out when the game isn't installed), then Add to favourites (the star, filled for Remove from favourites) and Hide from the launcher. Keep it that short; everything else is on the game's page.
- The app shows the same menu for a right-click, the Menu key and Shift+F10; Esc or a click elsewhere closes it.
