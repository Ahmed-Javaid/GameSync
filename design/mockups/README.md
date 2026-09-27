# UI mockups

The live canvas is https://claude.ai/artifact/XCwnRi7H7rAU3cN326dXGY (private to the owner until shared). The files here are a snapshot of it from 2026-09-27; the canvas is the source of truth.

## What's on the canvas

| Artboard | File | What it shows |
| --- | --- | --- |
| A · Console | `Main.dc.html` | Library as a dense dark status table (Chakra Petch, IBM Plex) |
| B · Shelf | `Shelf.dc.html` | Library as a cover-art grid with a "Needs attention" strip (Bricolage Grotesque, Atkinson Hyperlegible) |
| C · Logbook | `Logbook.dc.html` | Light, editorial library with a two-week version timeline per game (Instrument Serif, Instrument Sans) |
| Game detail | `GameDetail.dc.html` | Wireframe: save locations, version history with a pinned "before game update" version, activity log |
| Conflict | `Conflict.dc.html` | Wireframe: DESKTOP vs LAPTOP side by side, suggested choice, why automatic resolution was skipped |
| First run | `Onboarding.dc.html` | Wireframe: scan results in groups, Ludusavi import, four setup steps |
| Plan | `Plan.dc.html` | Wireframe: what the next sync would do per game, and why |
| Settings | `Settings.dc.html` | Wireframe: daily backup time with catch-up, save folders, cloud, devices, safety |

Row 1 is the three candidate looks for the library. Rows 2 and 3 are unstyled wireframes of the other screens. Covers are placeholders; statuses are sample states using the owner's games.

## Status

The look was chosen on 2026-09-27 and now lives in the design system (`design/system/`). These files are kept as history, and as the only drafts of Game detail, Conflict, Plan and Onboarding until those are designed in the new look. The Settings wireframe is replaced by the design system's Settings screen.

## Editing the canvas

- It is an Artifact made from the "Design" type. Read it with the Artifact tool first (`action: "read"`, `path: "project/canvas.json"`, then each artboard you'll change); a publish to an artifact the conversation hasn't read is refused.
- Publish changed files to the same URL with `root` set to a local folder that holds them under `project/`. Send `project/canvas.json` only when artboards are added, removed or moved.
- Each `.dc.html` needs the `<script src="./support.js"></script>` line exactly, a fixed-size root element matching its board, inline styles, and the `data-dc-script` block. The Design type's own instructions arrive when the canvas is read.
