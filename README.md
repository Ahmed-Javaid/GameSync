# GameSync

A Windows app that backs up your game saves, syncs them between your PCs, launches your games, and shows every problem on the game it belongs to. Built to replace Ludusavi and Playnite for its author and a few friends.

## Download

Get **GameSync-Setup-*version*.exe** from the [latest release](https://github.com/Ahmed-Javaid/GameSync/releases/latest) and run it. It installs for your Windows account only, with no admin prompt, on 64-bit Windows 11 or Windows 10 22H2.

- GameSync isn't code-signed, so Windows SmartScreen may say it protected your PC: choose **More info**, then **Run anyway**. Where Smart App Control is on, Windows blocks unsigned apps, GameSync included.
- First run finds your games and their saves, asks where to keep them (your Google Drive, a folder every PC can reach, or only this PC for now), then which games to back up.
- GameSync updates itself: once a day it checks for a newer release here and installs only one signed with GameSync's own release key. Settings, Updates shows what's new.
- Rather not install? The release's zip holds the same app: unzip it somewhere it can stay and run `GameSync.Tray.exe`.
- Uninstalling it (Windows Settings, Apps) removes the app and keeps your saves and GameSync's backups.

## What it does

- **Finds your games and their saves**: Steam, Epic and EA app games by themselves, others in game folders you add; saves from PCGamingWiki's list, engine rules and a name search, and for a game it still can't place, by watching where it writes the first time you play it.
- **Keeps every version**: a copy each time you stop playing, a daily backup, and named saves ("Before Lady Maria") you restore in a step.
- **Syncs between your PCs**, game by game, through your Google Drive or a folder they share. The newest save wins, and the other is kept, ready to swap back.
- **Launches your games** from a library like Steam's, with each game's page, play time and achievements: Steam's, and those of copies Steam doesn't run, with a popup when you unlock one.
- **Shares saves with friends** as a zip.
- **Says what's wrong on the game it's about**, in plain words, never as one conflict for a whole folder.

## Status

GameSync 0.6.1 is the first release for friends. The sync engine, Google Drive, finding games and saves, the background app and the app's window are built (Milestones 1 to 5). Milestone 6, learn mode and the release, is under way; v1.0 follows a week of use on two PCs.

## What it promises

- Every version of a save is kept forever, unless you thin a game's history yourself.
- Between PCs, the newest save wins and the other one is pinned, ready to swap back. It never picks by itself on a PC's first sync, when a save lost more than half its files or size, or when a save changed while the game wasn't running.
- Only save data moves. Program files are never backed up or restored, and restores write only inside the game's own save folders.
- It never touches a running game beyond checking that it runs: no injection, overlays or drivers. Games with an anti-cheat start only through their store or their anti-cheat's own launcher, and GameSync never watches what they write.

## Privacy

GameSync keeps your saves on your PC and in your own Google Drive or a folder you choose, and in Drive it can see only the files it made. Beyond that it asks GitHub for a newer GameSync once a day and for a new save list once a week, and asks Steam for your games' art, store details and achievements by their app IDs. It talks to nothing else. The details are in the [privacy policy](PRIVACY.md).

## Build and test

You need Windows and the .NET 10 SDK.

```powershell
dotnet build GameSync.sln
dotnet test GameSync.sln
```

`tools/release/pack.ps1` makes a release: the zip, the installer (with Inno Setup 6) and its signature with GameSync's release key, which only the author holds. `docs/handoff.md` says how to try a build on a scratch data folder and render its screens.

## Docs

- `docs/design.md`: the full design
- `docs/requirements.md`: every requirement, each with an ID and a test
- `docs/milestone-1.md` to `docs/milestone-6.md`: what's built, how to try it, and its known limits
- `docs/handoff.md`: where things stand and what's next
- `design/system/`: the UI design system (open `design/system/viewer.html`)
