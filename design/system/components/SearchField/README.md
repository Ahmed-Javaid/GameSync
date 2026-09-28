A text field that filters as you type, with the search icon in front; the library's search.

Provide `value` and `onChange`, a `placeholder` that says what it searches ("Search your games"), and `onSubmit` for Enter (open the first match). `hint` shows the shortcut that puts the cursor there ("Ctrl F") while the field is empty; a clear button takes its place once there's text.

- It's a text field: `surface-field` inside a `line-200` border, `radius-md`, 40px high; the focus ring goes round the whole field.
- Esc clears it. Matching ignores case, accents and punctuation, and the first letters of the words count too ("sts" finds Slay the Spire 2).
- Say how many games match next to it ("3 of 58 games"), and when none do, say so where the results would be.
