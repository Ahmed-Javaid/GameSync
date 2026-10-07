A row of a game's achievements, as its list of every one shows them (design system version 33): the badge, the name and description, and on the right when it was unlocked, or how many of Steam's players have it.

Provide `item` (`{name, desc, icon, pct, when, state}`), `selected` and `onClick`: a click shows it in the details beside the list.

- Unlocked: its date ("Today", "26 Sep") and, beside its percent, its tier's dot.
- Locked: dimmed; its percent says how many players have it, so the person sees which are within reach.
- **Hidden**, locked: "Hidden achievement" and "Keep playing to reveal it." in place of its name and description, and no icon. Its percent may show: it says nothing about what it is.
- One Tab stop for the whole list; Up and Down move between rows (A11Y-02). A screen reader hears the name, whether it's unlocked and when, and its percent.
