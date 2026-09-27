Grid of preset cards, each a tiny live preview of GameSync in that theme and the current mode, like the theme picker in Mihon.

Provide `value` (the preset id), `onChange(id)`, `mode`, `pureBlack` and, for the Windows accent card, `accent`. The chosen card gets a `primary` ring and a tick. Put it in the Appearance card with the mode tabs as the card's action and the pure black switch beneath.

- Picking a preset resets any swatch the person chose, so the preset looks as designed.
- Seven cards: Arcade, Moss, Tidal, Sakura, Citrus, Mono, Windows accent.
