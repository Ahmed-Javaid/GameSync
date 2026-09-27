# GameSync design system

> Snapshot of the [online design system](https://claude.ai/code/artifact/6a117b31-dc9a-4cd7-be46-037c91f1f074), 27 Sep 2026. Designs are edited there; copy changed files here afterwards, as with `design/mockups/`. Open `viewer.html` in a browser to see every component and screen offline, in all eight themes.
>
> For building: `tokens.json` holds every colour, type style, spacing and radius, per theme; `Theming.md` says how themes are generated; each `components/<Name>/README.md` is that component's spec. `components/bundle.js` and `bundle.css` are a React reference implementation for the previews, not app code; the app itself is Avalonia.

GameSync is a Windows game launcher that also keeps every game's saves backed up and synced across the owner's PCs. The system has two registers that share one palette: the **Launcher**, soft and art-led, for picking a game and playing it; and the **Console**, dense and monospaced, for managing saves, where the extra detail is the point. Dark first, with Light, Match Windows and pure black modes and six preset themes; the Theming section has the rules.

## Voice and copy

- Plain words, specific to one game. "Sekiro changed on two PCs", never "Sync conflict detected".
- Say what happened, then what GameSync did, then what the person can do: "DESKTOP saved 21:04, LAPTOP saved 19:30. Newest kept, LAPTOP copy pinned."
- Buttons are verbs that name the result: "Continue playing", "Create zip", "Package everything", "Show in folder". No "OK", no "Submit".
- Sentence case everywhere, except `overline` labels and console column headers, which are uppercase.
- Recommend one action rather than offering a menu. The primary button is that recommendation.
- Times are 24-hour (`21:04`), dates short (`20 Sep`), sizes in `KB`/`MB`/`GB` with one decimal from MB up. PC names are uppercase (`DESKTOP`, `LAPTOP`) and set in `mono`.
- No emoji. Status carries an icon and a word, never colour alone.

## Colour

- The ground is `bg-100`; everything sits on `bg-200` cards with `radius-lg` corners. Cards are told apart by surface, not borders or shadows.
- Controls inside a card sit on `bg-300`; hover goes to `bg-400`.
- Every theme has two colours. `primary` (cyan in the default Arcade theme) is for the one primary button per view, progress fills, the busiest activity days, checkbox and switch fills, the console prompt, the logo and focus rings. Text on a primary fill is always `on-primary`.
- `secondary` marks where you are and what you picked: `secondary-soft` fills selected rows, ticked share and import items, the current rail button, the selected tab and settings section; `secondary` itself tints their icons, the tab count chip and 2 to 4 hour activity days. Never use it for an action.
- Status colours are fixed meanings and never change with the theme: `ok` = fine or moving (Synced, Upload pending, Newer in cloud); `warn` = needs you (Conflict, Held for review, Files in use, Saves not found); `play` = a game is running; `danger` = Blocked only; `neutral` = Backup only, Not available. Each has a `-soft` background for chips and banners.
- The Console panes (save table, log) use `bg-000`, so they read as a terminal set into the app.
- Text: `ink` for content, `ink-muted` for secondary, `ink-faint` for timestamps and labels. All three pass 4.5:1 on `bg-000` to `bg-300`, `secondary-soft` and `primary-soft` in every theme.
- Anything on cover art (the hero's title, eyebrow, chip and glass buttons) uses `on-art`, `glass` and `art-scrim`, which stay the same in every theme.

## Type

- `sans` (Onest) for the Launcher and all prose; `mono` (JetBrains Mono) for the Console, paths, sizes, versions, times in tables and big numbers (`mono-stat`).
- One `hero-title` per screen. Card headers use `card-title`. Body copy is `body`; metadata under a title is `caption`.
- Console tables use `mono-body` cells and uppercase `mono-small` headers with 0.08em tracking. Right-align numbers with tabular figures.
- Both faces are Google Fonts; ship them inside the app as files. Fallbacks are Segoe UI Variable and Cascadia Mono, which Windows 11 has.

## Shape and space

- Everything clickable that isn't a row is a pill (`radius-pill`): buttons, tabs, chips, status badges. Icon buttons are 40px circles.
- Cards, tiles, the hero and dialogs use `radius-lg`; activity cells and inputs `radius-md`; checkboxes and console selection `radius-sm`.
- Page gutters `space-6`, card padding `space-5`, gaps between cards `space-4`. Buttons are `control-height` (40px); console rows `row-height` (36px).
- Only dialogs and tooltips cast `shadow-dialog`. Everything else is flat.
- Hatching (a 135° stripe of `bg-400`) means "some, but not much": light activity days. Never use it as decoration or for missing art.

## Layout

- **Launcher home**: `SideRail` on the left; a top bar with a greeting, `PillTabs` for the library view and icon buttons on the right; a `HeroBanner` for the last-played game; then a row of three cards: Needs you, Jump back in (`GameTile`s), Activity (`ActivityGrid`).
- **Save manager**: same rail and top bar; a strip of `mono-stat` numbers; a selectable `ConsoleTable` of every game's saves; a live `ConsoleLog` beneath it. The top bar holds **Share N selected** (secondary, disabled until a row is ticked), **Share all** (secondary) and **Sync now** (primary).
- **Sharing**: both share buttons open `ShareSavesDialog`. From a selection it opens on "Choose saves" with those games ticked; Share all opens on "Share all", which packages the whole save folder, optionally latest saves only. Games with an anti-cheat or an online mode are shown locked, with the reason, and left out. The result is one zip with a path the person can copy or show in Explorer.
- **Importing**: the download icon button in the save manager opens `ImportSavesDialog`. Imported saves become pinned versions, never current ones; unknown games can't be imported.
- **Settings**: the rail's Settings button opens a two-column screen: `SettingsNav` on the left (Appearance, Storage and folders, Backup and sync, Cloud, Devices, Notifications, Safety), `Card`s of `SettingsRow`s on the right. Appearance holds `ThemePicker` and `ColorSwatchPicker`; Storage and folders holds the backup folder (`FolderField`), history to keep, game folders to scan and extra save folders (`FolderList`), and where shared zips go.

## Iconography

- Outlined icons on a 24px grid at a 1.75 stroke with round caps and joins, drawn at 20px (rail), 18px (buttons) and 14px (badges). `Icon` holds the full set; use its names, not other icon fonts.
- The only filled icon is `play`.
- The set is drawn for GameSync in a Lucide-like style; no icon library is bundled. Add new icons on the same grid and stroke.
- Logo: `assets/Logos/gamesync-mark.svg`, drawn in the default `primary`; in the app it takes the theme's `primary`. No wordmark.

## Imagery

- Every image slot is filled from one source, first match wins: Steam's official art for the game's Steam app ID (from the store's own records, or from the save list's Steam ID for Epic and loose copies of the same game); then SteamGridDB, only if the person has added their own free API key; then an image file the person picked; then a title cover. Never a stock image and never an empty box.
- Which Steam image goes where: `GameTile` takes the 600×900 library capsule (`library_capsule_2x`). `HeroBanner` takes the library hero (1920×620, or 3840×1240 on high-DPI screens) with the game's transparent logo (`logo.png`) in place of the text title, because Steam's hero art never contains text; the title stays for screen readers. Share, import and list rows take the 300×450 capsule (`library_capsule`). A landscape slot takes `header` (920×430).
- Get the file names from Steam's store API (`IStoreBrowseService/GetItems` with `include_assets`, no key needed): newer games keep their art under hashed paths that can't be guessed from the app ID.
- Tiles are 2:3 with `radius-lg`. The hero is a wide crop with an `art-scrim` fade at the bottom so `on-art` text reads in every theme; keep controls out of the hero's centre, where Steam keeps its key art.
- No art found: a title cover, the game's name in `ink` on `bg-300` with its first letter large in `bg-400`. Small covers in rows show just the letter.
- Art is cached on the PC and shown offline. It is decoration, not data: it never goes to the cloud or into a shared zip.

## States and focus

- Focus is a 2px `primary` ring offset by 2px on every control; it passes 3:1 on every surface in every theme.
- Selected rows, ticked share and import items, the current page and the selected tab fill with `secondary-soft`.
- Switches and checkboxes: off shows a `line-200` edge, on fills with `primary`.
- Disabled controls drop to 45% opacity and keep their label.
- Motion is short (120–200ms) and only on colour and progress width.
