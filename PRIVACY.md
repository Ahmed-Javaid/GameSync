# GameSync privacy policy

Last updated 18 October 2026.

GameSync is a free, open-source Windows app that backs up your game saves and syncs them between your own PCs, through your own Google Drive or a folder you choose. It has no servers of its own, no accounts, no ads and no analytics.

## On your PC

- To find your games and where they save, GameSync reads your game stores' records (Steam, Epic and EA), the game folders you add, and the usual places games keep saves. It skips folders that hold passwords, keys or other apps' sign-ins.
- To show when you last played each Steam game and for how long, it reads Steam's own record of that on your PC. It reads nothing else from that record, and this stays on your PC.
- For cover art, it first uses the art Steam has already saved on your PC.
- To notice when you play, it sees which programs are running, by their names and folders. For a game whose saves it hasn't found, it watches which files that game writes the first time you play it (never for a game with an anti-cheat), and offers you the places it found. Both stay on your PC.
- For achievements, it reads Steam's own record of what you've unlocked on your PC, and the records some copies of games keep in their own folders, and keeps its own record of your unlocks.
- It backs up the save folders and registry keys of the games you confirm, and nothing else. It never backs up programs.
- It keeps its settings, an activity log and a backup copy of your saves in `%LOCALAPPDATA%\GameSync`, or in the backup folder you pick.

## In your Google Drive

- GameSync asks Google for one permission, `drive.file`. With it, GameSync can see and change only the files it created itself, in a folder called GameSync. Your other Drive files stay invisible to it.
- In that folder it keeps your game saves and their history, a plain copy of each game's newest save, a guide to restoring saves without GameSync, and a list of your PCs: the name you gave each one, its GameSync version and when it last synced.
- Unless you turn it off in Settings, it also keeps the record of the achievements you've unlocked there, one file per PC (which achievement of which game, and when), so your other PCs show them too.
- Each saved version also records where the game keeps its saves, so your other PCs can find them. Those are folder names like `<documents>/My Games/Terraria`; a folder you chose outside Windows' usual folders is recorded as its full path.
- It reads your Google account's email address and how full your Drive is, to show them to you and to warn you before your Drive fills up. Neither leaves your PC.
- If you choose a folder every PC can reach instead (a NAS, a USB drive or another disk), the same files go there, and GameSync doesn't use Google at all.

## Anywhere else

- GameSync talks to Google, to sign in and to use your Drive.
- About once a week it downloads the list of where games keep their saves, the [Ludusavi manifest](https://github.com/mtkennerly/ludusavi-manifest), from GitHub (`raw.githubusercontent.com`), and only when the list has changed. The request carries nothing about you or your games.
- Once for each game it finds, it asks Steam's public store API (`api.steampowered.com`) whether the app is a game or software, which images belong to it, and its store page's basics (its short description, developers, publishers, release date and top tags), by its Steam app ID; and about once a month it fetches Steam's list of tag names. It downloads from Steam's image server (`shared.steamstatic.com`) only the images Steam hasn't already saved on your PC, and checks art it downloaded about once a month in case Steam changed it.
- For a game with achievements, it asks the same API for the game's list of achievements and how many of Steam's players have each, and downloads their icons from Steam's image servers (`shared.akamai.steamstatic.com`, `cdn.akamai.steamstatic.com`). Steam's requests carry app IDs and nothing else, and use no Steam account. What they bring stays on your PC and never goes to your Drive.
- Once a day it asks GitHub (`api.github.com`) whether there's a newer GameSync, and downloads it from GitHub when there is; it installs only a release signed with GameSync's own release key. The request carries nothing about you.
- Report a problem, in Settings, opens a new issue on GameSync's GitHub page in your browser, with GameSync's diagnostics on your clipboard for you to paste in. Nothing is sent unless you submit the issue there yourself.
- It talks to nothing else, and sends no usage data or crash reports anywhere.

## Your Google sign-in

- You sign in in your browser, on Google's own page. GameSync never sees your password.
- The access Google gives GameSync is stored on your PC, encrypted so that only your Windows user can read it.
- `gamesync signout` cancels GameSync's access at Google and deletes it from your PC. You can also remove GameSync's access at any time at https://myaccount.google.com/permissions.

## Sharing and deleting

- GameSync never shares your data with anyone.
- To remove everything, delete `%LOCALAPPDATA%\GameSync` (and your backup folder, if you moved it) from your PC, and the GameSync folder from your Drive. Your games' own save folders stay as they are.

## Google API data

GameSync's use and transfer of information received from Google APIs adheres to the [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy), including the Limited Use requirements.

## Contact and changes

Questions go to https://github.com/Ahmed-Javaid/GameSync/issues. Changes to this policy show in this file's history on GitHub.
