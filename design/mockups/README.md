# UI mockups

The live canvas is https://claude.ai/artifact/XCwnRi7H7rAU3cN326dXGY (private to the owner until shared). The files here are a snapshot of it from 2026-09-28; the canvas is the source of truth.

## What's on the canvas

| Artboard | File | What it shows |
| --- | --- | --- |
| A · Console | `Main.dc.html` | Library as a dense dark status table (Chakra Petch, IBM Plex) |
| B · Shelf | `Shelf.dc.html` | Library as a cover-art grid with a "Needs attention" strip (Bricolage Grotesque, Atkinson Hyperlegible) |
| C · Logbook | `Logbook.dc.html` | Light, editorial library with a two-week version timeline per game (Instrument Serif, Instrument Sans) |
| Home | `Home.dc.html` | Launcher home: the last-played game as the hero, Needs you, Jump back in, Activity |
| Game detail | `GameDetail.dc.html` | Hero with status and Play, named saves, where the saves are, history and the game's log |
| Conflict | `Conflict.dc.html` | DESKTOP and LAPTOP side by side, the suggested choice and why GameSync asked, Compare files |
| First run | `Onboarding.dc.html` | Over a blurred wall of the games found: the four steps, and Choose games at full width, ending with "Something missing? Add a game or folder", with the anti-cheat note beside the buttons. No Ludusavi: that import is command line only |
| Add a game or folder | `AddGame.dc.html` | The dialog over first run: a name, a folder and an optional program, for a game GameSync didn't find or a game server's world |
| Plan | `Plan.dc.html` | The save manager's Plan tab: what the next sync would do per game and why |
| Settings | `Settings.dc.html` | Appearance: the theme picker, the new Surface choice (Glossy or Solid), pure black and colours |

Row 1 is the three candidate looks for the library, kept as history (27 Sep). Below it, the seven boards above in the two surface modes the owner chose (28 Sep), one row each, so each column compares one screen: **Glossy** at the top (`<Screen>.dc.html`) and **Solid** below (`<Screen>-Solid.dc.html`). Every board also has a **Look** tweak that switches it between the two. Art is drawn stand-ins, not Steam's; statuses are sample states using the owner's games.

## Status

The look was chosen on 2026-09-27 and lives in the design system (`design/system/`), where every screen is designed. On 2026-09-28 the owner asked for the see-through look of `Design inspirations/` across the whole app, compared three looks on this canvas (full glass, a middle ground, and today's solid look), and chose two modes:

- **Glossy**: each page sits on a blurred, darkened copy of a game's art, in three strengths by screen. Game detail, Conflict and Plan are full glass (cards white at 5.5% over the backdrop, translucent rail and console panes). Home is a step more solid (cards `rgba(28,32,37,0.6)`, rail `bg-100` at 25%). First run and Settings keep only a soft glow at the top that fades into `bg-100`, over nearly solid cards (`bg-200` at 82%).
- **Solid**: the plain look the app has today.

Bright art gets a darker backdrop, so ink-faint text keeps 4.5:1 on every surface (measured on each board). Decision and requirements: `docs/design.md` → UI, LOOK-17 and LOOK-18; a fresh install starts in Glossy. Next, Glossy goes into the design system's tokens and components, then the app.

The boards were generated from one script so the surfaces stay consistent: each screen is written twice, identical except for the mode it shows until its tweak is changed. Each board's script (`renderVals`) holds both modes' values side by side (`Glossy`, with that screen's strength and backdrop darkening, and `Solid`).

## Editing the canvas

- It is an Artifact made from the "Design" type. Read it with the Artifact tool first (`action: "read"`, `path: "project/canvas.json"`, then each artboard you'll change); a publish to an artifact the conversation hasn't read is refused.
- Publish changed files to the same URL with `root` set to a local folder that holds them under `project/`. Send `project/canvas.json` only when artboards are added, removed or moved.
- Each `.dc.html` needs the `<script src="./support.js"></script>` line exactly, a fixed-size root element matching its board, inline styles, and the `data-dc-script` block. The Design type's own instructions arrive when the canvas is read.
