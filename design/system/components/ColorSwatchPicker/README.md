Row of round swatches for one theme colour, each shown in the tone it will actually take in the current mode.

Provide `role` (`primary` or `secondary`), `value` (a swatch id, or a hex code for a custom colour), `onChange`, and `theme` (the rest of the current theme, so tones are computed correctly; defaults to the page's theme). The chosen swatch gets an `ink` ring and a tick in its `on-` colour.

- v1: the eleven curated swatches only.
- Later: `allowCustom` adds a dashed + swatch that opens a hex field. The note under it says whether the colour reads as it is or what it was adjusted to, with its contrast, and warns when it sits near a status colour.
