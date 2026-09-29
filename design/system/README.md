# GameSync design system

> Snapshot of the [online design system](https://claude.ai/code/artifact/6a117b31-dc9a-4cd7-be46-037c91f1f074), 30 Sep 2026, version 21. Designs are edited there; copy changed files here afterwards, as with `design/mockups/`. Open `viewer.html` in a browser to see every component and screen offline, in all eight themes.
>
> For building: `tokens.json` holds every colour, type style, spacing and radius, per theme; `Theming.md` says how themes are generated; each `components/<Name>/README.md` is that component's spec. `components/bundle.js` and `bundle.css` are a React reference implementation for the previews, not app code; the app itself is Avalonia.

GameSync is a Windows game launcher that also keeps every game's saves backed up and synced across the owner's PCs. The system has two registers that share one palette: the **Launcher**, soft and art-led, for picking a game and playing it; and the **Console**, dense and monospaced, for managing saves, where the extra detail is the point. Dark first, with Light, Match Windows and pure black modes and six preset themes, and two surfaces: Glossy, the default, where each page sits on a blurred copy of the game's art, and Solid; the Theming section has the rules.

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
- In **Glossy**, the default surface, the page sits on a blurred, darkened copy of the game's art and the surfaces let it through at the screen's strength; cards, controls and console panes gain a hairline edge (`edge-card`, `edge-control`, `edge-console`), dialogs stay nearly opaque (`surface-dialog`), and rows set into a card become darker wells (`surface-well`). In **Solid** the edges are clear. Theming → Surface has the values.
- Controls inside a card sit on `bg-300`; hover goes to `bg-400`.
- Every theme has two colours. `primary` (cyan in the default Arcade theme) is for the one primary button per view, progress fills, the busiest activity days, checkbox and switch fills, the console prompt, the logo and focus rings. Text on a primary fill is always `on-primary`.
- `secondary` marks where you are and what you picked: `secondary-soft` fills selected rows, ticked share and import items, the current rail button, the selected tab and settings section; `secondary` itself tints their icons, the tab count chip and 2 to 4 hour activity days. Never use it for an action.
- Status colours are fixed meanings and never change with the theme: `ok` = fine or moving (Synced, Upload pending, Newer in cloud); `warn` = needs you (Conflict, Held for review, Files in use, Saves not found); `play` = a game is running; `danger` = Blocked only; `neutral` = Synced by its store (say which: "Synced by Steam"), Not available. Each has a `-soft` background for chips and banners, and an `-art` tint for a badge on cover art.
- The Console panes (save table, log) use `bg-000`, so they read as a terminal set into the app.
- Text: `ink` for content, `ink-muted` for secondary, `ink-faint` for timestamps and labels. All three pass 4.5:1 on `bg-000` to `bg-300`, `secondary-soft` and `primary-soft` in every theme.
- Anything on cover art (the hero's title, eyebrow, chip and glass buttons) uses `on-art`, `glass`, `glass-edge` and `art-scrim`, which stay the same in every theme and surface; a status badge on art takes its `-art` tint. In Glossy, art gets a 1px `edge-art` ring so it stays apart from the backdrop.

## Type

- `sans` (Onest) for the Launcher and all prose; `mono` (JetBrains Mono) for the Console, paths, sizes, versions, times in tables and big numbers (`mono-stat`).
- One `hero-title` per screen. Card headers use `card-title`. Body copy is `body`; metadata under a title is `caption`.
- Console tables use `mono-body` cells and uppercase `mono-small` headers with 0.08em tracking. Right-align numbers with tabular figures.
- Both faces are Google Fonts; ship them inside the app as files. Fallbacks are Segoe UI Variable and Cascadia Mono, which Windows 11 has.

## Shape and space

- Everything clickable that isn't a row is a pill (`radius-pill`): buttons, tabs, chips, status badges. Icon buttons are 40px circles.
- Cards, tiles, the hero and dialogs use `radius-lg`; activity cells and inputs `radius-md`; checkboxes and console selection `radius-sm`.
- Page gutters `space-6`, card padding `space-5`, gaps between cards `space-4`. Buttons are `control-height` (40px); console rows `row-height` (36px).
- Only dialogs and tooltips cast `shadow-dialog`. Everything else is flat; in Glossy, cards add a lit top edge (`lift-card`).
- Hatching (a 135° stripe of `bg-400`) means "some, but not much": light activity days. Never use it as decoration or for missing art.

## Layout

- **Launcher home**: `SideRail` on the left; a top bar with a greeting, `PillTabs` for the library view and icon buttons on the right; a `HeroBanner` for the last-played game; then a row of three cards: Needs you, Jump back in (`GameTile`s), Activity (`ActivityGrid`). A tile opens that game's page in the library; a Needs you row's button (Review, Resolve) opens its saves in the save manager, where that's done. The hero's Manage saves opens its saves, and its gear its Properties.
- **Game library**: laid out like Steam's. A 272px `Card` down the left holds a `SearchField`, the sort (a `Menu`: Recently played, Name, Hours played, Recently added) and a `GameList` with Favourites first; the rest of the page is the covers (a top bar with `PillTabs` for the view, then `GameTile`s, favourites first) or, once a game is picked, that game's page. The view, the search and the sort apply to both sides. A right-click on a game opens Play, Add to favourites, Hide from the library and Properties.
- **Save manager**: same rail and top bar; a strip of `mono-stat` numbers; a selectable `ConsoleTable` of every game's saves; a live `ConsoleLog` beneath it. The top bar holds **Share N selected** (secondary, disabled until a row is ticked), **Share all** (secondary) and **Sync now** (primary). A game's name opens **its saves**: Back and a breadcrumb, its status with its actions, Named saves and Where the saves are as `Card`s, every version as a `ConsoleTable` with Restore and Export, and its `ConsoleLog`. All save work happens here; a game's page only points to it.
- **Sharing**: both share buttons open `ShareSavesDialog`. From a selection it opens on "Choose saves" with those games ticked; Share all opens on "Share all", which packages the whole save folder, optionally latest saves only. Games with an anti-cheat or an online mode are shown locked, with the reason, and left out. The result is one zip with a path the person can copy or show in Explorer.
- **Importing**: the download icon button in the save manager opens `ImportSavesDialog`. Imported saves become pinned versions, never current ones; unknown games can't be imported.
- **Settings**: the rail's Settings button opens a two-column screen: `SettingsNav` on the left (Appearance, Storage and folders, Backup and sync, Cloud, Devices, Notifications, Safety), `Card`s of `SettingsRow`s on the right. Appearance holds the Surface (Glossy or Solid), `ThemePicker` and `ColorSwatchPicker`; Storage and folders holds the backup folder (`FolderField`), history to keep, game folders to scan and extra save folders (`FolderList`), and where shared zips go. Backup and sync holds What to back up: the defaults every new game starts from (settings files and screenshots as `Select`s, the usual skips, patterns to skip).
- **Game detail**: about the game, like Steam's page; it opens in the library beside the list. Back and a breadcrumb (My games › the game) at the top left; a shorter `HeroBanner` of just the art and the logo; a `PlayBar` with Play (or the status's own action when the game needs you), Last played, Play time, Achievements and Saves, the favourite star, Properties and More; then About (`Facts` from the store page) and Achievements on the left, and a small Saves `Card` (status, last backup, the save folder, Open in Saves) and On this PC on the right.
- **Game properties**: a dialog, like Steam's Properties, with a `SettingsNav` of General, Launch, Installed files, Saves and Sync. Saves is where a person picks which files are backed up, in a `FileTree`; changes wait for Save changes.
- **Conflict**: the game's name and what happened as the title, a suggestion `Card` that says why GameSync asked, the two sides as `Card`s with the suggested side's Keep button as the primary, Compare files opening a `ConsoleTable`, and Decide later.
- **Plan**: a tab of the save manager. Run N changes replaces Sync now; one `ConsoleTable` of the changes, each ticked, and one of the games that won't run, each with its status and button.
- **First run**: no rail; a `SettingsNav` of the four steps (Scan this PC, Choose games, Connect the cloud, Backups and startup) with a `check` on finished steps, one primary button per step, and grouped game `Card`s with a checkbox per group and per game. Choose games takes the full width: Sync, Synced by their store, Probably online-only, Saves found but game not installed and No saves found yet, then Something missing? with Add a game or folder; the anti-cheat note sits beside Back and Continue.

## Navigation and feedback

The screens follow Nielsen's ten heuristics and Shneiderman's eight golden rules; in GameSync they come down to these.

- **Always a way back.** Every page below a rail destination (a game's page, a game's saves) has Back at its top left, then a breadcrumb that says where you are. Back returns to where you came from; Esc, Alt+Left and the mouse's back button do the same. A dialog closes with Esc, its close button and Cancel.
- **Say what's happening, where it happens.** Every game shows its one status with an icon and a word, on its row, its tile, its page and its saves. A job shows progress in place and ends with one line saying what it did ("Backed up: 1 changed file, 0.5 MB").
- **Nothing that does nothing.** A button that can't act now is disabled with the reason in its tooltip ("Once you quit, it backs up by itself"); a feature that isn't built isn't shown.
- **Ask before anything that writes into a game's folders**, and say what's kept: "Your files now are kept as a version first". Choices that change what's backed up show their effect before they apply ("12 of 14 files, 5.9 MB") and wait for Save changes.
- **Easy to undo.** Restores keep the files they replace as a version; unticking a file never deletes it; a hidden game has its Hidden view; favourites and sorts change back in one click.
- **Recognition over recall.** Show paths, sizes, dates and PC names instead of asking for them; pick folders with a picker; copy command lines with a button.
- **The same thing in the same place.** Back is always top left; the gear is always Properties; the primary button is the one recommendation; every game's status reads the same everywhere.
- **Shortcuts for people who use it a lot**: Ctrl+F searches, Enter opens the first match, Up and Down move through the list, the Menu key opens a game's menu, Esc goes back.
- **Errors say which game, what happened and what to do**, in plain words, never a code.

## Iconography

- Outlined icons on a 24px grid at a 1.75 stroke with round caps and joins, drawn at 20px (rail), 18px (buttons) and 14px (badges). `Icon` holds the full set; use its names, not other icon fonts.
- The only filled icons are `play`, and the `star` of a favourite (filled while it's on).
- `arrowLeft` is Back, `settings` is Settings and a game's Properties, `trophy` is achievements, and `external` marks a link that leaves GameSync.
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
- Selected rows, ticked share and import items, the current page and the selected tab fill with `secondary-soft`; so does the open game's row in the library's list.
- A toggle icon button that's on (the favourite star) fills its icon, in `secondary` off art and `on-art` on it.
- Switches and checkboxes: off shows a `line-200` edge, on fills with `primary`.
- Disabled controls drop to 45% opacity and keep their label.
- Motion is short (120–200ms) and only on colour and progress width.
