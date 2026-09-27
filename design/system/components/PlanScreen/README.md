Showcase of the Plan view in the save manager: what the next sync would do for each game and why, without doing anything until you run it. Built only from the components above.

- Plan is a tab of the save manager (Saves, Plan, Versions, Log). Its top bar swaps Sync now for **Run N changes** (primary, counting the ticked games) and Check again (secondary).
- The stat strip counts what will run, what needs you, what waits and what's already in sync, plus how much will move.
- The first `ConsoleTable` lists the changes: game, action (Upload, Download, Back up, each with its icon), why in one plain sentence, and size. Every row starts ticked; unticking one skips that game this time only.
- The second lists games that won't run and why: needs you, being played, saves not found, drive not connected. Each keeps its status's button (Resolve, Learn mode, Add a place); it can't be ticked.
- Games already in sync stay folded away behind one ghost button.
- Running does exactly what the plan showed, and the log records it line by line.
