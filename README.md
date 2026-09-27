# GameSync

A Windows app that backs up your game saves, syncs them between your PCs, launches your games, and shows every problem on the game it belongs to. Built to replace Ludusavi and Playnite for its author and a few friends.

## Status

Milestone 1 of 6 is done: the sync engine. Milestone 2, Google Drive and syncing between two PCs, is built and being tried out, along with named saves ("Before Lady Maria") you can restore in a step. Milestone 3 is built: GameSync finds your Steam, Epic, EA and loose games and where each keeps its saves (the PCGamingWiki list, engine rules, a name search, and the registry), and takes over from Ludusavi. It's all command line for now; there's no app to install yet.

## What it promises

- Every version of a save is kept forever, unless you thin a game's history yourself.
- Between PCs, the newest save wins and the other one is pinned, ready to swap back. It never picks by itself on a PC's first sync, when a save lost more than half its files or size, or when a save changed while the game wasn't running.
- Only save data moves. Program files are never backed up or restored, and restores write only inside the game's own save folders.
- It never touches a running game beyond checking that it runs: no injection, overlays or drivers.

## Privacy

GameSync keeps your saves on your PC and in your own Google Drive, and it can see only the files it made there. Apart from a weekly check for a new save list on GitHub, it talks to nothing else. The details are in the [privacy policy](PRIVACY.md).

## Build and test

You need Windows and the .NET 10 SDK.

```powershell
dotnet build GameSync.sln
dotnet test GameSync.sln
```

`docs/milestone-1.md` shows how to try the command line, and how to run every test on copies of real saves.

## Docs

- `docs/design.md`: the full design
- `docs/requirements.md`: every requirement, each with an ID and a test
- `docs/milestone-1.md`, `docs/milestone-2.md` and `docs/milestone-3.md`: what's built so far, how to try it, and its known limits
- `design/system/`: the UI design system (open `design/system/viewer.html`)
