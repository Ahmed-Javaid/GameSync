The bar under a game's hero, like Steam's: Play, then the facts a player looks for, then the game's own buttons.

Provide `primary`, `stats` and `actions`.

- `primary`: Play (a primary `Button` with `play`); Playing, disabled, while the game runs. When the game needs you, the status's own action is the primary (Resolve, Review, See where, See why; Add a place and Retry once their screens are built) with Play as an `IconButton` beside it. A Steam game not installed on this PC offers Install through Steam (secondary); any other game that isn't installed has none.
- `stats`, in this order: Last played, Play time, Achievements (only for a game whose store tracks them), Saves (its status, given as `status`, shows as a plain `StatusBadge` so the icon and colour come with the word). Labels are `overline`, values 14px `ink`.
- `actions`, in this order: the favourite star (`IconButton` with `pressed`), Properties (`settings`), More (`more`).
- It's a `Card`, so Glossy gives it the card's edge and lit top. In a narrow page the facts wrap under each other rather than squeezing.
