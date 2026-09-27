Collapsed 64px navigation rail with icon buttons, separators and tooltips.

Provide `items` (`{id, icon, label, sep?, bottom?, dot?, dotLabel?}`), `current` and `onNavigate`. The current item fills with `secondary-soft` and its icon turns `secondary`. `dot` is a status token name: `play` when a game is running, `warn` when a save needs the person. Always give a dot its `dotLabel` ("2 games need you"): it goes in the tooltip and the screen-reader name, so the dot never means something by colour alone. Put Settings at the bottom.
