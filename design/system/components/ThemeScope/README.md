Applies a theme to everything inside it by setting every colour token on one element.

Provide `theme` (`{preset, mode, pureBlack, primary?, secondary?, accent?}`), plus `style` and `className`; without `theme` it takes the page's theme and follows it. The Settings screen wraps itself in one so a pick re-themes it live; previews use it to show components under several themes at once. In the app, the same `theme` object builds the colour resources.

- **Glossy**: give `art` (an image URL, or a CSS gradient) and a `strength` (`glass`, `home` or `glow`: the Theming section says which screen takes which), and the scope sits on a `Backdrop` of that art with the strength's see-through surfaces. It follows the Surface picked on the Settings screen (Glossy by default); `surface` fixes it for one scope. Light mode and pure black stay Solid, as in the app.
- Every screen wraps its page in one, so each shows both surfaces.
