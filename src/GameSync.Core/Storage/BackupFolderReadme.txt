GameSync's backups on this PC

Every version GameSync has kept of your games' saves is in this folder. Leave the files in it as they are: to put
the folder somewhere else, use GameSync (Settings, Storage and folders, Backup folder, Change...), which moves all of
it and checks every file on the way.

games\<game>
    One game's saves: every version from every PC.
    versions   a small record of each version: when it was saved, on which PC, and which files.
    blobs      the files themselves, compressed and named by their contents, so a file that's the same in many
               versions is kept once.
    pins       named saves, and the other versions kept for good, which are never thinned.
    outbox     versions made on this PC that are waiting to go up to your cloud.

games\<game>--pc-<8 letters and digits>
    The same game's settings files on one PC (graphics, controls), backed up but never brought to your other PCs, as
    each PC keeps its own. That's why a game can have two folders here.

devices
    Your PCs, with when each was last seen.

To get a save back, open GameSync: the save manager, the game's saves, then Restore on a version or a named save.
Your files there now are kept as a version first, so you can always go back.

Without GameSync, the copy in your cloud (the GameSync folder in your Google Drive, or your cloud folder) has each
game's newest save as plain files, and restore.ps1 for older ones; its HOW-TO-RESTORE.txt says how.
