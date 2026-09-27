# GameSync privacy policy

Last updated 28 September 2026.

GameSync is a free, open-source Windows app that backs up your game saves and syncs them between your own PCs, through your own Google Drive. It has no servers of its own, no accounts, no ads and no analytics.

## On your PC

- To find your games and where they save, GameSync reads your game stores' records (Steam, Epic and EA), the game folders you add, and the usual places games keep saves. It skips folders that hold passwords, keys or other apps' sign-ins.
- To show when you last played each Steam game and for how long, it reads Steam's own record of that on your PC. It reads nothing else from that record, and this stays on your PC.
- For cover art, it first uses the art Steam has already saved on your PC.
- It backs up the save folders and registry keys of the games you confirm, and nothing else. It never backs up programs.
- It keeps its settings, an activity log and a backup copy of your saves in `%LOCALAPPDATA%\GameSync`, or in the backup folder you pick.

## In your Google Drive

- GameSync asks Google for one permission, `drive.file`. With it, GameSync can see and change only the files it created itself, in a folder called GameSync. Your other Drive files stay invisible to it.
- In that folder it keeps your game saves and their history, a plain copy of each game's newest save, a guide to restoring saves without GameSync, and a list of your PCs: the name you gave each one, its GameSync version and when it last synced.
- Each saved version also records where the game keeps its saves, so your other PCs can find them. Those are folder names like `<documents>/My Games/Terraria`; a folder you chose outside Windows' usual folders is recorded as its full path.
- It reads your Google account's email address and how full your Drive is, to show them to you and to warn you before your Drive fills up. Neither leaves your PC.

## Anywhere else

- GameSync talks to Google, to sign in and to use your Drive.
- About once a week it downloads the list of where games keep their saves, the [Ludusavi manifest](https://github.com/mtkennerly/ludusavi-manifest), from GitHub (`raw.githubusercontent.com`), and only when the list has changed. The request carries nothing about you or your games.
- Once for each game it finds, it asks Steam's public store API (`api.steampowered.com`) whether the app is a game or software, and which images belong to it, by its Steam app ID. It downloads from Steam's image server (`shared.steamstatic.com`) only the images Steam hasn't already saved on your PC, and checks art it downloaded about once a month in case Steam changed it. The requests carry app IDs and nothing else, and use no Steam account. The images stay on your PC and never go to your Drive.
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
