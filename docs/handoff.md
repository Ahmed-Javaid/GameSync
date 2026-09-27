# Build handoff

Everything needed to start building is in this repo; start with Milestone 1, the sync engine as a command-line tool. Read `CLAUDE.md` first, then this page.

## What to build first

The owner chose to start with Milestone 1 on 27 Sep 2026, as `docs/design.md` → Milestones recommends: riskiest part first, no UI.

- **Scope**: versions, three-way decisions, guardrails and crash safety (design.md → Sync engine), behind `IBlobStore` and `IVersionLog` (→ Storage backends), with a local folder standing in for Google Drive, which arrives in Milestone 2.
- **Layout**: `GameSync.sln` with `src/GameSync.Core`, `src/GameSync.App` (command-line verbs for now; the Avalonia UI joins it in Milestone 5) and `tests/GameSync.Core.Tests`, as in design.md → Tech stack, layout and packaging.
- **Done when**: every decision and crash test passes on copies of the owner's real saves. The owner's old Ludusavi backups (about 1 GB, 46 games) are test data from day one; where they are is in `notes/owner-setup.md`, which is private and git-ignored. Never point tests at the real save folders.
- **Requirements it covers** (`docs/requirements.md`): SYNC-01 to SYNC-07, SYNC-09, SYNC-11 to SYNC-14; BAK-02 to BAK-05, BAK-07 to BAK-09, BAK-11 to BAK-13, BAK-16; FIND-09 and FIND-11; R1 to R3 and R5 to R8. Until the process watcher exists (Milestone 4), play sessions are passed in to the rules that need them (SYNC-07, BAK-11).
- **Later milestones**, per design.md's table: Google Drive, device IDs and portable paths (2), game detection and save discovery (3), sessions, launching and the daily backup (4), the UI (5), learn mode and release (6).

## Set up the owner's PC

The owner has no .NET tools installed yet.

1. Install the .NET 10 SDK: run `winget install Microsoft.DotNet.SDK.10` in a terminal, then check that `dotnet --version` prints 10.0 or later.
2. Install Git: `winget install --id Git.Git -e`. This folder isn't a Git repository yet, so run `git init` in it before the first commit; `.gitignore` already covers build output and `notes/`.
3. Optional editor: VS Code with the C# Dev Kit extension (`winget install Microsoft.VisualStudioCode`), or Visual Studio Community with the .NET desktop development workload.

## Where the design is

The UI is Milestone 5, but the design is final enough to build against.

| What | Where |
| --- | --- |
| Brand book: voice, colour, type, layout, iconography, cover art | `design/system/README.md` |
| Every colour per theme, the type styles, spacing and radii | `design/system/tokens.json`, also compiled as `tokens.css` |
| How themes are generated from a mode, a preset and two colours | `design/system/Theming.md`; reference code: `theme.build()` in `design/system/components/bundle.js` |
| One spec per component, with its states | `design/system/components/<Name>/README.md` |
| Every component and screen, clickable, in all eight themes, offline | `design/system/viewer.html` |
| The owner's library with real cover art in this look | `design/real-art-preview.html` |
| Online originals, where designs are edited | links in `CLAUDE.md` |

- `components/bundle.js` and `bundle.css` are a React reference that renders the previews: the behaviour and look to match, not code to ship.
- In Avalonia: turn `tokens.json` into one resource dictionary per theme; port `theme.build()` to C# so presets, swatches and the Windows accent colour produce the same values; keep status colours fixed; ship Onest and JetBrains Mono as font files (both are open-source fonts from Google Fonts).
- Designed: Launcher home, Save manager with the Share and Import dialogs, and Settings (Appearance, Storage and folders, Backup and sync, Cloud, Devices, Notifications, Safety). Still only wireframes in `design/mockups/`: Game detail, Conflict, Plan and Onboarding. Design those before Milestone 5.

## Rules and tests

- The rules in `CLAUDE.md` and R1 to R21 in design.md never break; each gets an automated test.
- Name tests and issues after requirement IDs, for example `FOLD-04`. The owner records Pass, Fail or Blocked in the live requirements doc (link in `CLAUDE.md`); `docs/requirements.md` is a copy.

## Still open

None of these blocks Milestone 1.

- **Encrypted mode on Drive**: decided 27 Sep 2026: off. It may come later as an opt-in, so keep `IBlobStore` able to take an encrypting wrapper.
- The other open questions in design.md: the name, the installer, screenshots, friends' setups, the server, sharing between friends, what Share all includes, tray icon colours and the SteamGridDB key.
- The Gaps section of `docs/requirements.md`: Windows 10 support, performance targets, uninstall, diagnostics for bug reports, more than one Google account, and language.
