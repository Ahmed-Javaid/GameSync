Wide last-played banner with cover art, the game's logo, playtime chip, save status and the Continue playing button.

Provide `art` (Steam's library hero, 1920×620 or 3840×1240), `logo` (the game's transparent logo, Steam's `logo.png`), `title`, `eyebrow` ("Last played today, 21:04"), `chip` ("61 hrs played"), `status` and `statusLabel`, `blurb` (one sentence about the save), and `onPlay`, `onSaves`, `onSettings`.

- With a `logo`, the logo replaces the text title (Steam's hero art never contains text, so the two are made to pair); the title stays for screen readers. Without one, the title is set in `hero-title`.
- No `art`: a plain dark glass banner with the title, never an empty box.
- One per screen. The status line is what makes it GameSync rather than a plain launcher: always show it.
- Its text uses `on-art` over the `art-scrim` fade, the same in every theme, because it sits on the game's art; only the Continue playing button takes the theme's `primary`.
