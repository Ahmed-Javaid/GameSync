# GameSync

A Windows app that keeps game saves backed up and synced across a person's PCs, launches their games, and shows every problem on the game it belongs to. It replaces Ludusavi for the owner, whose main complaint was a folder-wide cloud "conflict" that never said which game. Free and open source (GitHub), for the owner and a few friends.

## Status (2026-09-27)

- **Phase: building.** The design is done and saved in the repo: `docs/design.md`, `docs/requirements.md` and `design/system/`.
- **Milestone 1 is done (2026-09-27):** the sync engine as a command-line tool. Every decision and crash scenario passes on copies of all 44 of the owner's games that have file saves. `docs/milestone-1.md` has what was built, how to try it, the results to record in the live requirements doc, and the known limits. The code is on GitHub (link below).
- **Now: Milestone 2**, Google Drive and the laptop, with Drive unencrypted (decided 2026-09-27). Built, and passing live on the owner's Drive since 2026-09-28 (the whole two-PC scenario, crash recovery included): local-first sync with an outbox, the Drive backend, sign-in, devices, portable folders, the clock check, the restore kit, named saves. Left: publishing the Google app. The week on both PCs waits until the whole app is built (owner's choice, 2026-09-28), so building carries on with Milestone 3. Status, results and the owner's steps: `docs/milestone-2.md`. The owner's OAuth client JSON and sign-in live in `%LOCALAPPDATA%\GameSync` and `notes\`, never in the repo.
- **Look chosen (2026-09-27):** the launcher follows `Design inspirations/`, the save manager uses the Console look, and both live in the GameSync design system with preset themes (Dark, Light, pure black; Arcade, Moss, Tidal, Sakura, Citrus, Mono, Windows accent).
- **Before Milestone 5 (the UI):** design Game detail, Conflict, Plan and Onboarding from the design system's components; they are still wireframes. Include named saves (added 2026-09-27): Save as… with a name prompt and the named-save list with Restore, on the launcher and in Game detail.

## Where things are

| What | Where |
| --- | --- |
| Full design, the source of truth | `docs/design.md` |
| Build handoff: what to build first, PC setup, where the design is | `docs/handoff.md` |
| Code: `GameSync.sln`, engine in `src/GameSync.Core`, Google Drive in `src/GameSync.Storage.Drive`, AMSI, DPAPI and known folders in `src/GameSync.Windows`, command line in `src/GameSync.App` | `src/`, tests in `tests/` |
| GitHub repository (public; commit or push only when the owner asks) | https://github.com/Ahmed-Javaid/GameSync |
| Milestone 1 report: what was built, how to try it, requirement results, known limits | `docs/milestone-1.md` |
| Milestone 2 plan: local-first history, Drive, two PCs, the owner's Google Cloud steps | `docs/milestone-2.md` |
| Requirements with IDs and tests (copy of the live doc) | `docs/requirements.md` |
| Design system copy: tokens, theming, component specs, offline viewer | `design/system/` (open `design/system/viewer.html`) |
| Same design as a Claude Doc with drawn diagrams (snapshot 2026-09-27, not kept in sync) | https://claude.ai/code/artifact/4eae149b-1a37-41b5-a498-cd15c81b6aa1 |
| Design system online, where designs are edited | https://claude.ai/code/artifact/6a117b31-dc9a-4cd7-be46-037c91f1f074 |
| Requirements live doc, where test results go (IDs like FOLD-04, a Result per row) | https://claude.ai/code/artifact/44d3a52e-5ad7-4d90-9eba-2818c484f613 |
| UI mockups, live canvas (the three candidate looks and the old wireframes) | https://claude.ai/artifact/XCwnRi7H7rAU3cN326dXGY |
| Mockup source snapshot and how to edit the canvas | `design/mockups/README.md` |
| Owner's reference images for the look (git-ignored: saved from other sites) | `Design inspirations/` |
| The owner's library with real Steam art in the GameSync look (loads images from Steam when opened) | `design/real-art-preview.html` |
| Save-finder spike (engine rules, name search, learn mode) | `spikes/Find-GameSaves.ps1` |
| Owner's machine and current setup (git-ignored) | `notes/owner-setup.md` |

## Decisions so far

- **Stack:** C# on .NET 10, Avalonia UI in the owner's own style (dark first), SQLite, one self-contained exe, per-machine installer. Chosen over Tauri/Rust because the owner knows .NET and wants a future ASP.NET Core server to share code.
- **Cloud:** Google Drive first with the `drive.file` scope, through Google's official .NET client, not rclone (rclone's errors were opaque). Storage sits behind `IBlobStore` + `IVersionLog`, so S3/R2 and a later server plug in.
- **Encryption on Drive:** off (decided 2026-09-27), so the readable `latest/` folder and `restore.ps1` get saves back without GameSync. It may come later as an opt-in; `IBlobStore` stays able to take an encrypting wrapper.
- **Sync model:** per game, three-way (this PC vs cloud vs last synced base). Immutable versions with parents; a fork is a conflict. Files stored once by hash.
- **Multiple PCs:** yes (desktop and laptop). Portable paths, device IDs, now-playing marker in the cloud.
- **Conflicts:** newest save wins automatically and the loser is pinned with a Swap button. Never automatic on a first sync on a PC, when the newer side shrank by over half or is empty, when it changed outside a play session, or when the clock is off.
- **Retention:** every version is kept forever. Thinning is manual only, and pinned versions are never thinned.
- **Named saves (2026-09-27):** Save as… keeps the save under a name ("Before Lady Maria") as a pinned version on every PC, restorable in a step; folders the owner kept by hand (Bloodborne on shadPS4: `CUSA00207\<name>\SPRJ0005` beside the live `CUSA00207\SPRJ0005`) import as named saves without being touched. BAK-18, BAK-19; design.md → Sync engine → Named saves.
- **Game updates:** when a game's build changes, the current save is snapshotted and pinned before the new build runs.
- **Daily backup:** the user sets the time; a missed run catches up about 10 minutes after the next sign-in.
- **Save discovery:** PCGamingWiki list (Ludusavi manifest), then engine rules, then name search, then learn mode (folder watcher, optional admin ETW tracer).
- **Launcher:** own UI, replacing Playnite.
- **Look and theming:** launcher from the reference images, Console-style save manager; presets with a primary and a secondary colour, Dark, Light or Match Windows, pure black; status colours never follow the theme. Custom colours come later.
- **Folders:** you choose the backup folder (moved with hash checks), how much history stays on the PC, game folders to scan, extra save folders, and where shared zips go.
- **Sharing:** Share selected or Share all into one zip; Import saves adds pinned versions only; online and anti-cheat games can't be shared.
- **Cover art:** Steam's store API (`IStoreBrowseService/GetItems`, no key) by Steam app ID, including IDs from the save list; SteamGridDB only with the user's own key; a title cover when nothing matches. Cached locally, never synced.

## Rules that must never break

- Only save data moves. Never back up or restore program files (check the extension and the Windows program header).
- Restores write only inside the game's approved folders; registry restores only under the game's own key.
- Never touch a running game beyond watching it: `PROCESS_QUERY_LIMITED_INFORMATION` at most, no memory access, injection, hooks, overlays, suspension, or kernel driver. No learn mode for games that ship an anti-cheat, and launch those only through their official route.
- A missing folder or unplugged drive is never treated as deleted; changes made outside a play session are held for review; nothing is overwritten without a snapshot first.
- Keep crack or emulator save-folder lists out of the repo; users add such folders in their own settings.
- The full list is R1–R21 in `docs/design.md` → Safety and security rules. Each rule gets an automated test.

## Working with the owner

- Plain language, specific per-game errors, a recommendation rather than a menu of options.
- They like to see design choices drawn (mockups, diagrams) before building.
- To change the mockup canvas or the design system, read it with the Artifact tool first; publishing to an artifact this conversation hasn't read is refused. The design system keeps its content in `project/` files; its type's instructions arrive with the read.
- Keep the requirements doc in step with `docs/design.md`: a new decision gets a requirement row, and an open question gets a Gaps row. Copy changed rows into `docs/requirements.md`, and changed design-system files into `design/system/`.
- `docs/design.md` is the design's source of truth now; update it when decisions change.
