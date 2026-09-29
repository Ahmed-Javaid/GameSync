Showcase of the Log tab of the save manager: everything GameSync did, per game, newest first, kept for good, so a problem can be traced to the game and the moment it happened. Built only from the components above.

- It's a tab of the save manager (Saves, Plan, Versions, Log). The Saves tab keeps its short live log at the bottom; this is the whole of it. The rail's Console is the agent's own output as it happens, for the curious; this is the record.
- Above the log: a `SearchField` ("Search the log"; matching as the library's search does), a `Select` for the game, and a `Select` for what to show: Everything, Only what needs you (warnings and errors), or Uploads, downloads and restores. On the right, how many lines match of how many there are.
- One `ConsoleLog` filling the rest: when (a day and the time to the second), the tag (upload, download, backup, restore, named, conflict, held, in use, session, daily), and the line, in the same plain, per-game voice as the rest of the app. Errors get their `danger-soft` row, warnings their `warn` tag.
- Nothing matching says so where the lines would be (`#search` with a search that finds nothing, say), and how to widen it.
- Copy the log, in the top bar, puts the lines shown on the clipboard for a bug report, never sign-in tokens or save files (SET-05's rule).
