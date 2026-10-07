Collapsed 64px navigation rail with icon buttons, separators and tooltips.

Provide `items` (`{id, icon, label, sep?, bottom?, dot?, dotLabel?}`), `current` and `onNavigate`. The current item fills with `secondary-soft` and its icon turns `secondary`. `dot` is a status token name: `play` when a game is running, `warn` when a save needs the person. A `warn` dot is drawn as a caution mark (`alert` in warn on the rail's own ring) and the icon itself turns warn, so what needs the person shows from every page (the owner, 3 Oct 2026). Always give a dot its `dotLabel` ("2 conflicts"): it goes in the tooltip and the screen-reader name, so the dot never means something by colour alone. Put Settings at the bottom.

The items, top to bottom: Search; Home; Game library and Achievements (the trophy, version 33: every game's achievements and Zeniths, `TrophyRoomScreen`); Save manager and Console; Settings at the bottom.
