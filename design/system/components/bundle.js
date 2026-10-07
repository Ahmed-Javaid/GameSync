/* @ds-bundle: {"format":4,"namespace":"GameSync","components":[{"name":"Icon"},{"name":"Button"},{"name":"IconButton"},{"name":"PillTabs"},{"name":"SideRail"},{"name":"Card"},{"name":"HeroBanner"},{"name":"StatusBadge"},{"name":"GameTile"},{"name":"SearchField"},{"name":"Menu"},{"name":"GameList"},{"name":"ProgressBar"},{"name":"JobProgress"},{"name":"ActivityGrid"},{"name":"Checkbox"},{"name":"Switch"},{"name":"ConsoleTable"},{"name":"ConsoleLog"},{"name":"ShareSavesDialog"},{"name":"ImportSavesDialog"},{"name":"SettingsNav"},{"name":"SettingsRow"},{"name":"FolderField"},{"name":"FolderList"},{"name":"ThemeScope"},{"name":"ThemePicker"},{"name":"ColorSwatchPicker"},{"name":"ProgressRing"},{"name":"AchievementBadge"},{"name":"TierChip"},{"name":"ZenithMedal"},{"name":"AchievementRow"},{"name":"AchievementPopup"}]} */
(function () {
  var React = window.React;
  var h = React.createElement;
  var useState = React.useState, useRef = React.useRef, useEffect = React.useEffect;

  /* ---------- Theme engine: presets, swatches, build(), apply() ---------- */
  var Theme = (function () {
    function clamp(x, a, b) { return Math.min(b, Math.max(a, x)); }
    function hexToRgb(hex) {
      hex = String(hex).replace("#", "").trim();
      if (hex.length === 3) hex = hex.split("").map(function (c) { return c + c; }).join("");
      if (!/^[0-9a-fA-F]{6}$/.test(hex)) return null;
      var n = parseInt(hex, 16);
      return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
    }
    function rgbToHex(r, g, b) {
      return "#" + [r, g, b].map(function (v) { v = Math.round(clamp(v, 0, 255)); return (v < 16 ? "0" : "") + v.toString(16); }).join("");
    }
    function rgbToHsl(r, g, b) {
      r /= 255; g /= 255; b /= 255;
      var max = Math.max(r, g, b), min = Math.min(r, g, b), h = 0, s = 0, l = (max + min) / 2;
      if (max !== min) {
        var d = max - min;
        s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        if (max === r) h = (g - b) / d + (g < b ? 6 : 0); else if (max === g) h = (b - r) / d + 2; else h = (r - g) / d + 4;
        h *= 60;
      }
      return [h, s * 100, l * 100];
    }
    function hsl(h, s, l) {
      h = ((h % 360) + 360) % 360; s = clamp(s, 0, 100) / 100; l = clamp(l, 0, 100) / 100;
      var a = s * Math.min(l, 1 - l);
      function f(n) { var k = (n + h / 30) % 12; return l - a * Math.max(-1, Math.min(k - 3, Math.min(9 - k, 1))); }
      return rgbToHex(f(0) * 255, f(8) * 255, f(4) * 255);
    }
    function toHsl(hex) { var c = hexToRgb(hex); return rgbToHsl(c[0], c[1], c[2]); }
    function lum(hex) {
      var c = hexToRgb(hex).map(function (v) { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); });
      return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
    }
    function contrast(a, b) { var x = lum(a), y = lum(b); return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05); }
    // Move a colour's lightness (keeping hue and saturation) until it reaches `min` contrast on every ground.
    function reach(hex, grounds, min, dir) {
      var p = toHsl(hex), l = p[2], c = hex;
      for (var i = 0; i < 220; i++) {
        c = hsl(p[0], p[1], l);
        if (grounds.every(function (g) { return contrast(c, g) >= min; })) return c;
        l += dir === "up" ? 0.5 : -0.5;
        if (l < 0 || l > 100) break;
      }
      return c;
    }
    // Light mode starts from a calmer version of the colour: pastel seeds are 100% saturated in HSL terms,
    // and near-greys (Mono) go straight to a dark slate instead of a mid grey.
    function lightStart(hex) {
      var p = toHsl(hex);
      return p[1] < 20 && p[2] > 84 ? hsl(p[0], p[1], 24) : hsl(p[0], Math.min(p[1], 72), p[2]);
    }
    function hueOf(hex) { return toHsl(hex)[0]; }
    function satOf(hex) { return toHsl(hex)[1]; }
    function rgba(hex, a) { var c = hexToRgb(hex); return "rgba(" + c[0] + ", " + c[1] + ", " + c[2] + ", " + a + ")"; }

    var STATUS = {
      dark: { ok: "#7fe6f2", "ok-soft": "#0f3035", warn: "#f2b544", "warn-soft": "#2a2213", danger: "#ff8a7d", "danger-soft": "#2e1917", play: "#1ed760", "play-soft": "#0f2a1b", neutral: "#9aa1a9" },
      light: { ok: "#006b77", "ok-soft": "#d8f3f6", warn: "#855600", "warn-soft": "#fbeed3", danger: "#b3261e", "danger-soft": "#fde4e1", play: "#0f6e35", "play-soft": "#dcf5e5", neutral: "#58616b" }
    };
    var FIXED = { "on-art": "#f5f7f9", "art-scrim": "rgba(5, 6, 8, 0.72)", glass: "rgba(12, 14, 17, 0.58)", "glass-edge": "rgba(255, 255, 255, 0.16)",
      bronze: "#d7955c", "bronze-deep": "#8a5530", silver: "#e3e8ee", "silver-deep": "#8d99a6", gold: "#ffd76e", "gold-deep": "#c08a2a",
      platinum: "#eef7ff", "platinum-deep": "#9bb6cd", "on-metal": "#1b2129" };

    var SWATCHES = [
      { id: "cyan", name: "Cyan", hex: "#7fe6f2" },
      { id: "aqua", name: "Aqua", hex: "#7fe3cf" },
      { id: "sky", name: "Sky", hex: "#9fd3ff" },
      { id: "blue", name: "Blue", hex: "#8fb4ff" },
      { id: "green", name: "Green", hex: "#8ee3a0" },
      { id: "mint", name: "Mint", hex: "#a6ecc9" },
      { id: "lime", name: "Lime", hex: "#cde77a" },
      { id: "pink", name: "Pink", hex: "#ff9fcb" },
      { id: "steel", name: "Steel", hex: "#aebfd3" },
      { id: "grey", name: "Grey", hex: "#a9aeb4" },
      { id: "white", name: "White", hex: "#e6e9ed" }
    ];
    function swatch(id) { for (var i = 0; i < SWATCHES.length; i++) if (SWATCHES[i].id === id) return SWATCHES[i]; return null; }

    var PRESETS = [
      { id: "arcade", name: "Arcade", primary: "cyan", secondary: "steel", tint: [214, 12], note: "The default: cyan on cool greys." },
      { id: "moss", name: "Moss", primary: "green", secondary: "sky", tint: [150, 10], note: "Green with a sky-blue second colour." },
      { id: "tidal", name: "Tidal", primary: "blue", secondary: "aqua", tint: [222, 16], note: "Blue with aqua, on navy greys." },
      { id: "sakura", name: "Sakura", primary: "pink", secondary: "mint", tint: [330, 9], note: "Pink with mint." },
      { id: "citrus", name: "Citrus", primary: "lime", secondary: "cyan", tint: [80, 7], note: "Lime with cyan." },
      { id: "mono", name: "Mono", primary: "white", secondary: "grey", tint: [0, 0], note: "White and grey, no colour at all." },
      { id: "windows", name: "Windows accent", primary: null, secondary: null, tint: null, dynamic: true, note: "Takes its primary from your Windows accent colour." }
    ];
    function preset(id) { for (var i = 0; i < PRESETS.length; i++) if (PRESETS[i].id === id) return PRESETS[i]; return PRESETS[0]; }

    function hexOf(v) { if (!v) return null; var s = swatch(v); return s ? s.hex : v; }

    /* opts: { preset, mode: "dark" | "light", pureBlack, primary, secondary, accent }
       primary / secondary: a swatch id or any hex (custom colours, later). accent: the Windows accent hex. */
    function build(opts) {
      opts = opts || {};
      var p = preset(opts.preset || "arcade");
      var mode = opts.mode === "light" ? "light" : "dark";
      var black = mode === "dark" && !!opts.pureBlack;
      var accent = opts.accent || "#0078d4";
      var seedP = hexOf(opts.primary) || (p.dynamic ? accent : hexOf(p.primary));
      var seedS = hexOf(opts.secondary) || (p.dynamic ? hsl(hueOf(accent), Math.min(satOf(accent), 100) * 0.3, 78) : hexOf(p.secondary));
      var th = p.dynamic ? hueOf(accent) : p.tint[0];
      var ts = p.dynamic ? 10 : p.tint[1];
      var v = {};
      var hp = toHsl(seedP), hs = toHsl(seedS);

      if (mode === "dark") {
        v["bg-000"] = black ? hsl(th, ts, 3.2) : hsl(th, ts * 1.2, 5.1);
        v["bg-100"] = black ? "#000000" : hsl(th, ts * 1.1, 7.3);
        v["bg-200"] = black ? hsl(th, ts, 5.8) : hsl(th, ts, 9.8);
        v["bg-300"] = black ? hsl(th, ts * 0.9, 10) : hsl(th, ts * 0.85, 13.5);
        v["bg-400"] = black ? hsl(th, ts * 0.85, 14.5) : hsl(th, ts * 0.8, 18.2);
        v["line-100"] = black ? hsl(th, ts, 11.5) : hsl(th, ts, 15.2);
        v.primary = reach(seedP, [v["bg-200"], v["bg-300"]], 7.5, "up");
        var pp = toHsl(v.primary);
        v["primary-strong"] = hsl(pp[0], pp[1], pp[2] > 84 ? pp[2] - 10 : Math.min(pp[2] + 9, 93));
        v["on-primary"] = reach(hsl(pp[0], Math.min(pp[1], 70), 11), [v.primary, v["primary-strong"]], 7, "down");
        v["primary-soft"] = reach(hsl(pp[0], Math.min(pp[1], 45), 14), [v.primary], 6, "down");
        v.secondary = reach(seedS, [v["bg-200"], v["bg-300"]], 6.5, "up");
        var sp = toHsl(v.secondary);
        v["on-secondary"] = reach(hsl(sp[0], Math.min(sp[1], 60), 11), [v.secondary], 7, "down");
        v["secondary-soft"] = hsl(sp[0], Math.min(sp[1] * 0.6, 34), black ? 13 : 17);
        var grounds = [v["bg-000"], v["bg-100"], v["bg-200"], v["bg-300"], v["secondary-soft"], v["primary-soft"]];
        v.ink = hsl(th, ts * 0.8, 94.5);
        v["ink-muted"] = reach(hsl(th, ts * 0.7, 67), grounds, 6, "up");
        v["ink-faint"] = reach(hsl(th, ts * 0.6, 54), grounds, 4.6, "up");
        v["line-200"] = reach(hsl(th, ts * 0.6, 40), [v["bg-000"], v["bg-200"], v["bg-300"], v["secondary-soft"]], 3.05, "up");
        Object.assign(v, STATUS.dark);
        v.scrim = "rgba(5, 6, 8, 0.72)";
        v["shadow-dialog"] = "0 24px 64px rgba(0, 0, 0, 0.6)";
      } else {
        // Light mode, redone in version 35 (the owner, 3 Oct 2026: "it just looks white"): the page a clear cool grey,
        // cards white with a hairline edge and a soft lift, console panes a near-white step between them, so page, cards
        // and what's on them read apart at a glance.
        var tl = Math.min(ts * 1.6, 28);
        v["bg-000"] = hsl(th, tl * 0.7, 97.2);
        v["bg-100"] = hsl(th, tl, 91.2);
        v["bg-200"] = hsl(th, tl * 0.4, 99.7);
        v["bg-300"] = hsl(th, tl, 94.4);
        v["bg-400"] = hsl(th, tl * 0.9, 87.8);
        v["line-100"] = hsl(th, tl, 88.4);
        v.primary = reach(lightStart(seedP), [v["bg-300"], v["bg-000"], v["bg-100"]], 4.6, "down");
        var lp = toHsl(v.primary);
        v["primary-strong"] = hsl(lp[0], lp[1], Math.max(lp[2] - 7, 6));
        v["on-primary"] = contrast("#ffffff", v.primary) >= 4.5 ? "#ffffff" : hsl(lp[0], 20, 8);
        v["primary-soft"] = reach(hsl(lp[0], Math.min(lp[1], 80), 93.5), [v.primary], 4.5, "up");
        v.secondary = reach(lightStart(seedS), [v["bg-300"], v["bg-000"], v["bg-100"]], 4.6, "down");
        var ls = toHsl(v.secondary);
        v["on-secondary"] = contrast("#ffffff", v.secondary) >= 4.5 ? "#ffffff" : hsl(ls[0], 20, 8);
        v["secondary-soft"] = hsl(ls[0], Math.min(ls[1], 55), 89.5);
        var lg = [v["bg-000"], v["bg-100"], v["bg-200"], v["bg-300"], v["secondary-soft"], v["primary-soft"]];
        v.ink = hsl(th, Math.min(tl, 18), 10);
        v["ink-muted"] = reach(hsl(th, tl * 0.8, 34), lg, 6, "down");
        v["ink-faint"] = reach(hsl(th, tl * 0.6, 44), lg, 4.6, "down");
        v["line-200"] = reach(hsl(th, tl * 0.5, 62), [v["bg-000"], v["bg-200"], v["bg-300"], v["bg-100"], v["secondary-soft"]], 3.05, "down");
        Object.assign(v, STATUS.light);
        v.scrim = "rgba(16, 20, 26, 0.45)";
        v["shadow-dialog"] = "0 24px 64px rgba(16, 24, 40, 0.18)";
      }
      // The surfaces Glossy makes see-through (glass() below). Dark Solid has no edges, lift or ring: its surfaces are the
      // plain ones and its edges clear. Light Solid gives cards, consoles, wells, dialogs and art a hairline in the theme's
      // deep ink and cards a soft lift, as a white card on a light page needs them (version 35). A control's edge stays clear
      // in both, so a hovered control shows no ring (the background runs under the border, as in CSS).
      var clear = rgba("#000000", 0);
      ["edge-card", "edge-control", "edge-console", "edge-well", "edge-dialog", "edge-art"].forEach(function (k) { v[k] = clear; });
      v["lift-card"] = "none";
      if (mode === "light") {
        var deep = hsl(th, Math.min(30, ts * 2.5), 14);
        v["edge-card"] = rgba(deep, 0.1);
        v["edge-console"] = rgba(deep, 0.08);
        v["edge-well"] = rgba(deep, 0.06);
        v["edge-dialog"] = rgba(deep, 0.1);
        v["edge-art"] = rgba(deep, 0.12);
        v["lift-card"] = "0 1px 3px " + rgba(deep, 0.08);
      }
      v["surface-dialog"] = v["bg-200"];
      v["surface-well"] = v["bg-300"];
      v["surface-field"] = v["bg-200"];
      v["dot-ring"] = v["bg-100"];
      // A status badge on cover art: in dark mode a deep tint of its status at 82% (neutral 88%), so it keeps 4.5:1 over
      // any art in both surfaces; light mode keeps its opaque tints.
      ["ok", "warn", "danger", "play"].forEach(function (k) {
        v[k + "-art"] = mode === "dark" ? rgba(hsl(hueOf(STATUS.dark[k + "-soft"]), 65, 10), 0.82) : v[k + "-soft"];
      });
      v["neutral-art"] = mode === "dark" ? rgba(hsl(th, ts, 10), 0.88) : v["bg-300"];
      Object.assign(v, FIXED);
      // Zenith's word on a page: since version 49 gold running to red, the Zenith's colours (version 35's was platinum);
      // in light a deep gold to red, each 4.5:1 or better on light cards.
      v["zenith-ink"] = mode === "dark" ? "#ffd76e" : "#8f6214";
      v["zenith-ink-deep"] = mode === "dark" ? "#ef6a5a" : "#a51d32";
      return v;
    }

    /* Glossy (LOOK-17, LOOK-18): what a page's surfaces become so a blurred, darkened copy of a game's art shows
       through, in three strengths: "glass" on Home, the game library, game detail, conflict and the save manager;
       "home", a step more solid, which no screen takes since 1 Oct 2026; "glow" on first run and settings, a soft glow
       at the top over nearly solid cards. Returns the tokens to lay over
       build()'s and the backdrop: its base colour, then the art, then the scrim colour at alpha stops [position,
       alpha] from top to bottom. Never with pure black: then it's null and the page stays Solid. The app darkens bright
       art further, until text keeps 4.5:1 on every surface.
       Light mode has its own Glossy since version 35 (the owner, 3 Oct 2026: "there isn't even any difference in
       glossy/solid in light mode"): frosted white over the art, which a light scrim lightens instead of darkening; cards
       white at 60% with a bright edge and a soft lift; the app lightens dark art further until dark text keeps 4.5:1. */
    var STRENGTHS = ["glass", "home", "glow"];
    function glass(opts, strength) {
      opts = opts || {};
      var light = opts.mode === "light";
      if (!light && opts.pureBlack) return null;
      var p = preset(opts.preset || "arcade");
      var accent = opts.accent || "#0078d4";
      var th = p.dynamic ? hueOf(accent) : p.tint[0];
      var ts = p.dynamic ? 10 : p.tint[1];
      var v = build(opts);
      function white(a) { return "rgba(255, 255, 255, " + a + ")"; }
      if (light) {
        var tl = Math.min(ts * 1.6, 28), deep = hsl(th, Math.min(30, ts * 2.5), 14);
        function shade(a) { return rgba(deep, a); }
        if (strength === "glow") {
          return {
            tokens: {
              "bg-000": rgba(v["bg-000"], 0.9), "bg-100": rgba(v["bg-100"], 0.55), "bg-200": rgba(v["bg-200"], 0.86),
              "bg-300": shade(0.05), "bg-400": shade(0.1), "line-100": shade(0.09), "secondary-soft": rgba(v.secondary, 0.14),
              "edge-card": white(0.7), "edge-control": shade(0.06), "edge-console": white(0.6),
              "edge-well": shade(0.05), "edge-dialog": white(0.8), "edge-art": shade(0.1),
              "lift-card": "0 2px 10px " + shade(0.06), "surface-dialog": rgba(v["bg-200"], 0.97),
              "surface-well": shade(0.05), "surface-field": white(0.8), "dot-ring": rgba(v["bg-100"], 0.95)
            },
            backdrop: { base: v["bg-100"], scrim: v["bg-100"], stops: [[0, 0.6], [0.36, 0.86], [0.6, 0.94], [1, 0.96]] }
          };
        }
        var homeLight = strength === "home";
        return {
          tokens: {
            "bg-000": white(0.5),
            "bg-100": homeLight ? rgba(v["bg-100"], 0.35) : white(0.12),
            "bg-200": homeLight ? white(0.72) : white(0.6),
            // Status chips keep their opaque light grounds: see-through, Needs-you amber can't keep 4.5:1 over every art.
            "bg-300": shade(0.05), "bg-400": shade(0.1), "line-100": shade(0.09), "secondary-soft": rgba(v.secondary, 0.14),
            "edge-card": white(0.65), "edge-control": shade(0.07), "edge-console": white(0.55),
            "edge-well": shade(0.06), "edge-dialog": white(0.8), "edge-art": shade(0.12),
            "lift-card": "0 2px 12px " + shade(0.07), "surface-dialog": rgba(v["bg-200"], 0.96),
            "surface-well": shade(0.05), "surface-field": white(0.7), "dot-ring": rgba(v["bg-100"], 0.95)
          },
          backdrop: homeLight
            ? { base: v["bg-100"], scrim: hsl(th, tl, 94), stops: [[0, 0.52], [0.4, 0.74], [0.68, 0.84], [1, 0.88]] }
            : { base: v["bg-100"], scrim: hsl(th, tl, 94), stops: [[0, 0.46], [0.4, 0.68], [1, 0.82]] }
        };
      }
      if (strength === "glow") {
        return {
          tokens: {
            "bg-000": rgba(v["bg-000"], 0.88), "bg-100": rgba(v["bg-100"], 0.5), "bg-200": rgba(v["bg-200"], 0.82),
            "bg-300": white(0.07), "bg-400": white(0.13), "line-100": white(0.07), "secondary-soft": rgba(v.secondary, 0.15),
            "edge-card": white(0.05), "edge-control": white(0.04), "edge-console": white(0.05),
            "edge-well": white(0.04), "edge-dialog": white(0.07), "edge-art": white(0.06),
            "lift-card": "inset 0 1px 0 " + white(0.04), "surface-dialog": rgba(hsl(th, ts, 11.6), 0.96),
            "surface-well": rgba("#000000", 0.28), "surface-field": rgba("#000000", 0.3), "dot-ring": rgba(v["bg-100"], 0.95)
          },
          backdrop: { base: v["bg-100"], scrim: v["bg-100"], stops: [[0, 0.56], [0.36, 0.84], [0.6, 0.93], [1, 0.95]] }
        };
      }
      var home = strength === "home";
      return {
        tokens: {
          "bg-000": rgba(hsl(th, ts * 2.4, 2.7), 0.6),
          "bg-100": home ? rgba(v["bg-100"], 0.25) : white(0.03),
          "bg-200": home ? rgba(hsl(th, ts * 1.15, 12.75), 0.6) : white(0.055),
          "bg-300": white(0.08), "bg-400": white(0.14), "line-100": white(0.07), "secondary-soft": rgba(v.secondary, 0.16),
          "ok-soft": rgba(v.ok, 0.13), "warn-soft": rgba(v.warn, 0.15), "play-soft": rgba(v.play, 0.16), "danger-soft": rgba(v.danger, 0.15),
          "edge-card": white(0.075), "edge-control": white(0.06), "edge-console": white(0.06),
          "edge-well": white(0.05), "edge-dialog": white(0.09), "edge-art": white(0.09),
          "lift-card": "inset 0 1px 0 " + white(0.05), "surface-dialog": rgba(hsl(th, ts, 11.6), 0.94),
          "surface-well": rgba("#000000", 0.26), "surface-field": rgba("#000000", 0.3), "dot-ring": rgba(hsl(th, ts * 1.15, 6.5), 0.95)
        },
        backdrop: home
          ? { base: v["bg-000"], scrim: hsl(th, ts * 1.2, 5.5), stops: [[0, 0.48], [0.4, 0.77], [0.68, 0.86], [1, 0.88]] }
          : { base: v["bg-000"], scrim: hsl(th, ts * 1.7, 3.9), stops: [[0, 0.44], [0.4, 0.7], [1, 0.84]] }
      };
    }

    /* Custom colours (later): report what a picked colour becomes and why. */
    function check(hex, role, opts) {
      var seed = hexToRgb(hex) ? (hex[0] === "#" ? hex : "#" + hex) : null;
      if (!seed) return { valid: false, message: "Use a hex code like #7FE6F2." };
      var o = Object.assign({}, opts || {}); o[role] = seed;
      var v = build(o);
      var used = v[role];
      var ground = v["bg-200"];
      var ratio = contrast(used, ground);
      var near = null;
      ["warn", "danger", "play"].forEach(function (k) {
        var a = toHsl(used)[0], b = toHsl(v[k])[0], d = Math.abs(a - b); d = Math.min(d, 360 - d);
        if (!near && d < 22 && satOf(used) > 30) near = k;
      });
      return { valid: true, input: seed, used: used, adjusted: used.toLowerCase() !== seed.toLowerCase(), ratio: Math.round(ratio * 10) / 10, near: near };
    }

    function apply(el, vars) {
      if (!el) return;
      Object.keys(vars).forEach(function (k) { el.style.setProperty("--" + k, vars[k]); });
    }

    // Map a design-system theme id back to picker state.
    var THEME_IDS = {
      dark: { preset: "arcade", mode: "dark" }, light: { preset: "arcade", mode: "light" }, black: { preset: "arcade", mode: "dark", pureBlack: true },
      moss: { preset: "moss", mode: "dark" }, tidal: { preset: "tidal", mode: "dark" }, sakura: { preset: "sakura", mode: "dark" },
      citrus: { preset: "citrus", mode: "dark" }, mono: { preset: "mono", mode: "dark" }
    };
    function fromThemeId(id) { return Object.assign({ preset: "arcade", mode: "dark", pureBlack: false }, THEME_IDS[id] || {}); }

    return { presets: PRESETS, swatches: SWATCHES, strengths: STRENGTHS, build: build, glass: glass, apply: apply, check: check, contrast: contrast, hsl: hsl, rgba: rgba, fromThemeId: fromThemeId, preset: preset, swatch: swatch };
  })();

  function cx() { return Array.prototype.filter.call(arguments, Boolean).join(" "); }
  // The theme the page is showing, for components used without an explicit theme.
  function docTheme() {
    var id = typeof document !== "undefined" && document.documentElement ? document.documentElement.getAttribute("data-theme") : null;
    return Theme.fromThemeId(id || "dark");
  }
  // First letter or digit of a title, for title covers.
  function initialOf(name) { var m = String(name || "").match(/[A-Za-z0-9]/); return m ? m[0].toUpperCase() : "?"; }
  function varsStyle(vars) { var s = {}; Object.keys(vars).forEach(function (k) { s["--" + k] = vars[k]; }); return s; }
  // The page's theme, followed when the page switches it.
  function useDocTheme() {
    var st = useState(docTheme);
    useEffect(function () {
      if (typeof MutationObserver === "undefined") return undefined;
      var mo = new MutationObserver(function () { st[1](docTheme()); });
      mo.observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
      return function () { mo.disconnect(); };
    }, []);
    return st[0];
  }

  /* ---------- Surface: Glossy (the default) or Solid, one choice for every screen, as in Settings ---------- */
  var SURFACE_KEY = "gamesync.surface";
  function getSurface() { try { return window.localStorage.getItem(SURFACE_KEY) === "solid" ? "solid" : "glossy"; } catch (e) { return "glossy"; } }
  function setSurface(value) {
    try { window.localStorage.setItem(SURFACE_KEY, value === "solid" ? "solid" : "glossy"); } catch (e) { /* previews without storage stay Glossy */ }
    try { window.dispatchEvent(new Event("gamesync-surface")); } catch (e) { /* old browsers */ }
  }
  function useSurface() {
    var st = useState(getSurface);
    useEffect(function () {
      function on() { st[1](getSurface()); }
      window.addEventListener("storage", on);
      window.addEventListener("gamesync-surface", on);
      return function () { window.removeEventListener("storage", on); window.removeEventListener("gamesync-surface", on); };
    }, []);
    return st[0];
  }

  /* ---------- Icon: 24px grid, 1.75 stroke, round caps ---------- */
  var P = {
    home: ["M3 11l9-7 9 7", "M5 10v10h14V10", "M10 20v-6h4v6"],
    library: ["M4 4h7v7H4z", "M13 4h7v7h-7z", "M4 13h7v7H4z", "M13 13h7v7h-7z"],
    search: ["M11 4a7 7 0 1 0 0 14a7 7 0 1 0 0-14", "M20 20l-4-4"],
    saves: ["M5 3h11l3 3v15H5z", "M8 3v6h8V3", "M8 21v-7h8v7"],
    terminal: ["M3 5h18v14H3z", "M7 10l3 2-3 2", "M12 15h5"],
    activity: ["M3 12h4l3-8 4 16 3-8h4"],
    settings: ["M12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6", "M19 12a7 7 0 0 0-.1-1.2l2-1.6-2-3.4-2.4 1a7 7 0 0 0-2-1.2L14 3h-4l-.5 2.6a7 7 0 0 0-2 1.2l-2.4-1-2 3.4 2 1.6A7 7 0 0 0 5 12c0 .4 0 .8.1 1.2l-2 1.6 2 3.4 2.4-1a7 7 0 0 0 2 1.2L10 21h4l.5-2.6a7 7 0 0 0 2-1.2l2.4 1 2-3.4-2-1.6c.1-.4.1-.8.1-1.2z"],
    play: ["M7 4.5v15l12-7.5z"],
    sync: ["M4 12a8 8 0 0 1 14-5.3L20 9", "M20 4v5h-5", "M20 12a8 8 0 0 1-14 5.3L4 15", "M4 20v-5h5"],
    share: ["M12 3v12", "M7 8l5-5 5 5", "M5 14v6h14v-6"],
    zip: ["M5 3h10l4 4v14H5z", "M10 3v2h2v2h-2v2h2v2h-2v2", "M9 15h4v4H9z"],
    folder: ["M3 6h6l2 2h10v11H3z"],
    cloud: ["M7 18h11a4 4 0 0 0 .5-8A6 6 0 0 0 7 9a4.5 4.5 0 0 0 0 9z"],
    upload: ["M12 16V5", "M7 10l5-5 5 5", "M5 19h14"],
    download: ["M12 5v11", "M7 11l5 5 5-5", "M5 19h14"],
    check: ["M5 12.5l4.5 4.5L19 7.5"],
    alert: ["M12 3l9.5 17h-19z", "M12 10v4", "M12 17h.01"],
    pause: ["M8 5v14", "M16 5v14"],
    lock: ["M6 11h12v10H6z", "M8.5 11V8a3.5 3.5 0 0 1 7 0v3"],
    unplug: ["M9 3v5", "M15 3v5", "M6 8h12v3a6 6 0 0 1-12 0z", "M12 17v4"],
    block: ["M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18", "M5.6 5.6l12.8 12.8"],
    archive: ["M3 4h18v5H3z", "M5 9v11h14V9", "M10 13h4"],
    pin: ["M9 3h6l-1 6 4 4H6l4-4z", "M12 13v8"],
    clock: ["M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18", "M12 7v5l3 2"],
    chevronRight: ["M9 5l7 7-7 7"],
    chevronDown: ["M5 9l7 7 7-7"],
    chevronsRight: ["M6 6l6 6-6 6", "M13 6l6 6-6 6"],
    x: ["M6 6l12 12", "M18 6L6 18"],
    info: ["M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18", "M12 11v6", "M12 7.5h.01"],
    copy: ["M8 8h12v12H8z", "M4 16V4h12"],
    monitor: ["M3 4h18v12H3z", "M8 20h8", "M12 16v4"],
    shield: ["M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z", "M8.5 12l2.5 2.5 4.5-5"],
    palette: ["M12 3a9 9 0 1 0 0 18c1.1 0 1.8-.8 1.8-1.8 0-.5-.2-.9-.5-1.2-.3-.3-.5-.8-.5-1.2 0-1 .8-1.8 1.8-1.8H17a4 4 0 0 0 4-4c0-4.4-4-8-9-8z", "M7.5 11.5h.01", "M9.5 7.5h.01", "M14.5 7.5h.01"],
    drive: ["M3 13h18v6H3z", "M5 13l2.5-8h9L19 13", "M17 16h.01"],
    bell: ["M6 16v-5a6 6 0 0 1 12 0v5l1.5 2h-15z", "M10 20.5a2 2 0 0 0 4 0"],
    plus: ["M12 5v14", "M5 12h14"],
    sun: ["M12 8a4 4 0 1 0 0 8a4 4 0 1 0 0-8", "M12 2.5v2", "M12 19.5v2", "M2.5 12h2", "M19.5 12h2", "M5.3 5.3l1.4 1.4", "M17.3 17.3l1.4 1.4", "M5.3 18.7l1.4-1.4", "M17.3 6.7l1.4-1.4"],
    moon: ["M20 14.5A8.5 8.5 0 1 1 9.5 4a6.5 6.5 0 0 0 10.5 10.5z"],
    pencil: ["M4 20h4L19 9l-4-4L4 16z", "M13.5 6.5l4 4"],
    reset: ["M4 12a8 8 0 1 0 2.3-5.7L4 8.6", "M4 3.5v5.1h5.1"],
    star: ["M12 3.6l2.6 5.3 5.8.8-4.2 4.1 1 5.8-5.2-2.7-5.2 2.7 1-5.8-4.2-4.1 5.8-.8z"],
    sort: ["M7 4v16", "M3.5 7.5L7 4l3.5 3.5", "M17 20V4", "M13.5 16.5L17 20l3.5-3.5"],
    chevronLeft: ["M15 5l-7 7 7 7"],
    more: ["M6 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2", "M12 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2", "M18 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2"],
    logo: ["M3 3h12v12H3z", "M9 9h12v12H9z"],
    arrowLeft: ["M19 12H5", "M11 18l-6-6 6-6"],
    trophy: ["M7 4h10v5a5 5 0 0 1-10 0z", "M7 5.5H4V7a3.5 3.5 0 0 0 3.6 3.5", "M17 5.5h3V7a3.5 3.5 0 0 1-3.6 3.5", "M12 14v4", "M8 20.5h8", "M9.5 18h5"],
    external: ["M14 4h6v6", "M20 4l-9 9", "M18 13.5V20H4V6h6.5"],
    file: ["M6 3h8l4 4v14H6z", "M14 3v4h4"],
    key: ["M8 10a4 4 0 1 0 0 8a4 4 0 1 0 0-8", "M10.9 11.1L19 3", "M16 6l2.5 2.5", "M13.5 8.5l2 2"],
    // Version 35: Synced (a game's saves in the cloud and on your PCs; the owner, 3 Oct 2026: "tick could mean anything") and
    // the popup's sound. Version 38: the Zenith's mark, a flag planted on a mountain's summit (version 37's sword read as a cross).
    cloudCheck: ["M7 18h11a4 4 0 0 0 .5-8A6 6 0 0 0 7 9a4.5 4.5 0 0 0 0 9z", "M9 13.2l2.2 2.2 4.3-4.4"],
    zenith: ["M2.5 21.5L12 10.5l3 3.6 1.7-2 4.8 9.4z", "M12 1.4l.91 2.35 2.51.14-1.95 1.59.65 2.43L12 6.55 9.88 7.91l.65-2.43-1.95-1.59 2.51-.14z"],
    volume: ["M4 9.5h3.5L12 6v12l-4.5-3.5H4z", "M15.5 9a4 4 0 0 1 0 6", "M18 6.5a7.5 7.5 0 0 1 0 11"]
  };
  // Filled: play always; the star only when it's on (a favourite).
  var FILLABLE = { star: true };
  // A path drawn solid in an outlined icon: the Zenith's star (version 41).
  var SOLID = { zenith: 1 };
  function Icon(props) {
    var name = props.name, size = props.size || 20;
    var d = P[name] || P.info;
    var filled = name === "play" || (!!props.filled && !!FILLABLE[name]);
    return h("svg", {
      className: cx("gs-icon", props.className), width: size, height: size, viewBox: "0 0 24 24",
      fill: filled ? "currentColor" : "none", stroke: "currentColor", strokeWidth: props.strokeWidth || 1.75,
      strokeLinecap: "round", strokeLinejoin: "round", "aria-hidden": props.label ? undefined : "true",
      role: props.label ? "img" : undefined, "aria-label": props.label
    }, d.map(function (p, i) { return h("path", { key: i, d: p, fill: SOLID[name] === i ? "currentColor" : undefined, strokeWidth: SOLID[name] === i ? 1 : undefined }); }));
  }
  Icon.names = Object.keys(P);

  /* ---------- Spinner and counting dots: every wait shows movement (KAN-80) ---------- */
  // A ring with a quarter of it lit, turning once a second; with reduced motion it stands still.
  function Spinner(props) {
    var size = props.size || 18;
    return h("svg", { className: cx("gs-spinner", props.className), width: size, height: size, viewBox: "0 0 24 24", fill: "none", stroke: "currentColor",
      strokeWidth: props.strokeWidth || 2.25, strokeLinecap: "round", "aria-hidden": "true" },
      h("circle", { cx: 12, cy: 12, r: 9, opacity: 0.28 }),
      h("path", { d: "M12 3a9 9 0 0 1 9 9" }));
  }

  // Three dots that count up after the words ("Syncing", "Syncing.", "Syncing..", "Syncing..."), in a box as wide as
  // all three, so nothing beside them moves. With reduced motion all three stay.
  function Dots() {
    return h("span", { className: "gs-dots", "aria-hidden": "true" }, h("span", null, "."), h("span", null, "."), h("span", null, "."));
  }

  // A wait with nothing to count: a spinner and its words, with the dots.
  function BusyLine(props) {
    return h("div", { className: cx("gs-note", "gs-busyline", props.className), role: "status" },
      h(Spinner, { size: 16 }), h("span", null, props.text, h(Dots)));
  }

  /* ---------- Button ---------- */
  // `busy`: it's doing its job. It keeps its colour (a greyed-out button looks broken), the spinner takes the icon's
  // place, `busyLabel` ("Syncing") replaces the words with the dots after them, and clicks do nothing.
  function Button(props) {
    var variant = props.variant || "secondary";
    var busy = !!props.busy;
    var rest = Object.assign({}, props);
    ["variant", "icon", "size", "className", "children", "iconAfter", "busy", "busyLabel"].forEach(function (k) { delete rest[k]; });
    if (busy) delete rest.onClick;
    var size = props.size === "sm" ? 16 : 18;
    return h("button", Object.assign({ type: "button" }, rest, {
      className: cx("gs-btn", "gs-btn-" + variant, props.size === "sm" && "gs-btn-sm", busy && "is-busy", props.className),
      "aria-busy": busy ? "true" : undefined, "aria-disabled": busy ? "true" : rest["aria-disabled"]
    }),
      busy ? h(Spinner, { size: size }) : props.icon ? h(Icon, { name: props.icon, size: size }) : null,
      busy ? h("span", { className: "gs-busy-words" }, props.busyLabel || props.children, h(Dots)) : props.children,
      !busy && props.iconAfter ? h(Icon, { name: props.iconAfter, size: 16 }) : null);
  }

  // `pressed` makes it a toggle (aria-pressed), such as the favourite star, which fills while it's on. `busy` turns its
  // icon (Sync now's arrows while GameSync syncs) and says `busyLabel` ("Syncing") in the tooltip.
  function IconButton(props) {
    var rest = Object.assign({}, props);
    ["icon", "label", "glass", "className", "badge", "size", "pressed", "busy", "busyLabel"].forEach(function (k) { delete rest[k]; });
    var label = props.busy && props.busyLabel ? props.busyLabel + "…" : props.label;
    return h("button", Object.assign({ type: "button", "aria-label": label, title: label, "aria-pressed": props.pressed != null ? String(!!props.pressed) : undefined,
      "aria-busy": props.busy ? "true" : undefined }, rest, {
      className: cx("gs-iconbtn", props.glass && "gs-iconbtn-glass", props.size === "sm" && "gs-iconbtn-sm", props.busy && "is-busy", props.className)
    }), h(Icon, { name: props.icon, size: props.size === "sm" ? 16 : 18, filled: !!props.pressed }));
  }

  /* ---------- PillTabs ---------- */
  function PillTabs(props) {
    var items = props.items || [];
    var st = useState(props.value != null ? props.value : (items[0] && items[0].id));
    var value = props.value != null ? props.value : st[0];
    function pick(id) { st[1](id); if (props.onChange) props.onChange(id); }
    return h("div", { className: "gs-tabs", role: "tablist", "aria-label": props.label },
      items.map(function (it) {
        // tone "warn": Needs you while something does, in the warn colours with a caution mark (A11Y-01: the count and
        // the words say it too).
        var warn = it.tone === "warn";
        return h("button", {
          key: it.id, type: "button", role: "tab", className: cx("gs-tab", warn && "gs-tab--warn"),
          "aria-selected": String(it.id === value), onClick: function () { pick(it.id); }
        }, warn ? h(Icon, { name: "alert", size: 16 }) : it.icon ? h(Icon, { name: it.icon, size: 16 }) : null, it.label,
          it.count != null ? h("span", { className: "gs-tab-count" }, it.count) : null);
      }));
  }

  /* ---------- SideRail ---------- */
  function SideRail(props) {
    var items = props.items || [];
    var hov = useState(null);
    function btn(it) {
      var full = it.dotLabel ? it.label + ": " + it.dotLabel : it.label;
      // A warn dot is a caution mark, and the icon takes the warn colour: what needs the person shows from any page.
      var warn = it.dot === "warn";
      return h("button", {
        key: it.id, type: "button", className: cx("gs-rail-btn", warn && "gs-rail-btn--warn"), "aria-label": full,
        "aria-current": it.id === props.current ? "page" : undefined,
        onMouseEnter: function () { hov[1](it.id); }, onMouseLeave: function () { hov[1](null); },
        onFocus: function () { hov[1](it.id); }, onBlur: function () { hov[1](null); },
        onClick: function () { if (props.onNavigate) props.onNavigate(it.id); }
      }, h(Icon, { name: it.icon, size: 20 }),
        warn ? h("span", { className: "gs-rail-caution", "aria-hidden": "true" }, h(Icon, { name: "alert", size: 12, strokeWidth: 2.5 }))
          : it.dot ? h("span", { className: "gs-dot", style: { background: "var(--" + it.dot + ")" } }) : null,
        hov[0] === it.id ? h("span", { className: "gs-tip", role: "tooltip" }, it.label, it.dotLabel ? h("span", { className: "gs-tip-sub" }, it.dotLabel) : null) : null);
    }
    var top = items.filter(function (i) { return !i.bottom; });
    var bottom = items.filter(function (i) { return i.bottom; });
    var out = [];
    top.forEach(function (it, i) {
      if (it.sep && i > 0) out.push(h("span", { key: "s" + i, className: "gs-rail-sep", "aria-hidden": "true" }));
      out.push(btn(it));
    });
    return h("nav", { className: "gs-rail", "aria-label": "Main" },
      h("div", { className: "gs-rail-logo", title: "GameSync" }, h(Icon, { name: "logo", size: 22, strokeWidth: 2 })),
      out, h("div", { className: "gs-rail-spacer" }), bottom.map(btn));
  }

  /* ---------- Card ---------- */
  // `lead` goes before the title (the Zenith's diamond on the Achievements page); `label` names a card whose title isn't text.
  function Card(props) {
    return h("section", { className: cx("gs-card", props.className), style: props.style, "aria-label": props.label || (typeof props.title === "string" ? props.title : undefined) },
      (props.title || props.action) ? h("div", { className: "gs-card-head" },
        props.title ? h("div", { className: props.lead ? "gs-card-lead" : undefined }, props.lead || null,
          h("div", null, h("h3", { className: "gs-card-title" }, props.title),
            props.subtitle ? h("p", { className: "gs-card-sub" }, props.subtitle) : null)) : h("span"),
        props.action || null) : null,
      props.children);
  }

  /* ---------- StatusBadge ---------- */
  var STATUS = {
    synced: ["ok", "cloudCheck", "Synced"],
    playing: ["play", "play", "Playing"],
    "upload-pending": ["ok", "upload", "Upload pending"],
    "newer-in-cloud": ["ok", "download", "Newer in cloud"],
    conflict: ["warn", "alert", "Conflict"],
    held: ["warn", "pause", "Held for review"],
    "in-use": ["warn", "lock", "Files in use"],
    "not-found": ["warn", "search", "Saves not found"],
    "not-available": ["neutral", "unplug", "Not available"],
    blocked: ["danger", "block", "Blocked"],
    "backup-only": ["neutral", "shield", "Synced by its store"],
    "not-syncing": ["neutral", "cloud", "Not syncing yet"]
  };
  function StatusBadge(props) {
    var s = STATUS[props.status] || STATUS.synced;
    return h("span", { className: cx("gs-status", "gs-status-" + s[0], props.plain && "gs-status-plain", props.className) },
      h(Icon, { name: s[1], size: 14, strokeWidth: 2 }), props.label || s[2]);
  }
  StatusBadge.statuses = Object.keys(STATUS);

  // The mark a game whose saves are fine gets instead of a badge (KAN-48; version 35: the owner, "tick could mean
  // anything"): a cloud with a check for Synced, on your PCs and in the cloud; a shield with a check for a game kept safe
  // but not synced between PCs, by choice ("Backed up") or by its store ("Synced by Steam"). Null for any other status.
  function fineMark(status, label) {
    if (status === "synced") return { icon: "cloudCheck", tone: "ok", words: label || "Synced: in your cloud and on your PCs" };
    if (status === "backup-only") return { icon: "shield", tone: "ok", words: label || "Backed up" };
    return null;
  }

  /* ---------- HeroBanner ---------- */
  // `pager` (version 37, Home): { count, index, names, onPick(i), onPrev, onNext } for the games the banner can show, at its
  // top right; the wheel and Left and Right with it focused move between them too. `onOpen` (version 37): a click on the
  // banner anywhere but its buttons, or Enter with it focused, opens the game's page.
  var heroWheel = 0;
  function HeroBanner(props) {
    var bg = props.art ? { backgroundImage: "url(" + props.art + ")" } : null;
    var pager = props.pager && props.pager.count > 1 ? props.pager : null;
    function open(e) { if (props.onOpen && !(e.target.closest && e.target.closest("button"))) props.onOpen(); }
    function key(e) {
      if (e.target !== e.currentTarget) return;
      if ((e.key === "Enter" || e.key === " ") && props.onOpen) { e.preventDefault(); props.onOpen(); }
      else if (e.key === "ArrowRight" && pager) { e.preventDefault(); pager.onNext(); }
      else if (e.key === "ArrowLeft" && pager) { e.preventDefault(); pager.onPrev(); }
    }
    function wheel(e) {
      if (!pager) return;
      var now = Date.now(), d = Math.abs(e.deltaX) > Math.abs(e.deltaY) ? e.deltaX : e.deltaY;
      if (!d || now - heroWheel < 350) return;
      heroWheel = now;
      (d > 0 ? pager.onNext : pager.onPrev)();
    }
    return h("section", { className: cx("gs-hero", !props.art && "gs-hero-empty", props.onOpen && "gs-hero--openable"), style: Object.assign({}, bg, props.style),
        "aria-label": props.title + (pager ? ", " + (pager.index + 1) + " of " + pager.count : "") + (props.onOpen ? ". Enter opens its page" : ""),
        tabIndex: props.onOpen ? 0 : undefined, onClick: props.onOpen ? open : undefined, onKeyDown: props.onOpen || pager ? key : undefined, onWheel: pager ? wheel : undefined,
        title: props.onOpen ? "Open its page" : undefined },
      h("div", { className: "gs-hero-top" },
        h("span", { className: "gs-hero-eyebrow" }, props.eyebrow),
        pager ? h("div", { className: "gs-hero-pager", role: "group", "aria-label": "Games you played last" },
          h(IconButton, { icon: "chevronLeft", label: "The game before", glass: true, size: "sm", onClick: pager.onPrev }),
          h("span", { className: "gs-hero-dots" }, Array.apply(null, Array(pager.count)).map(function (_, i) {
            var name = (pager.names && pager.names[i] ? pager.names[i] + ", " : "") + (i + 1) + " of " + pager.count;
            return h("button", { key: i, type: "button", className: cx("gs-hero-dot", i === pager.index && "is-current"), "aria-label": name, title: name,
              "aria-current": i === pager.index ? "true" : undefined, onClick: function () { pager.onPick(i); } });
          })),
          h(IconButton, { icon: "chevronRight", label: "The next game", glass: true, size: "sm", onClick: pager.onNext })) : null),
      h("div", { className: "gs-hero-bottom" },
        h("div", { className: "gs-hero-text" },
          props.chip ? h("span", { className: "gs-chip" }, h(Icon, { name: "clock", size: 14 }), props.chip) : null,
          props.logo ? h("img", { className: "gs-hero-logo", src: props.logo, alt: "" }) : null,
          h("h2", { className: cx("gs-hero-title", props.logo && "gs-sr-only") }, props.title),
          props.status ? h("div", null, h(StatusBadge, { status: props.status, label: props.statusLabel })) : null,
          props.blurb ? h("p", { className: "gs-hero-blurb" }, props.blurb) : null),
        h("div", { className: "gs-hero-actions" },
          props.actions != null ? props.actions : [
            h(IconButton, { key: "saves", icon: "saves", label: "Manage saves", glass: true, onClick: props.onSaves }),
            h(IconButton, { key: "settings", icon: "settings", label: "Properties", glass: true, onClick: props.onSettings })],
          props.noPlay ? null : h(Button, { variant: "primary", icon: props.playIcon || "play", onClick: props.onPlay }, props.playLabel || "Continue playing"))));
  }

  /* ---------- GameTile ---------- */
  // `achievements` (version 39, ACH-02): { done, total } as Steam on this PC keeps them: once one is unlocked, a small ring at
  // the cover's bottom right fills with the share unlocked round a trophy, opposite the save mark; once every one is, the
  // Zenith medal. Not a bar along the bottom: Steam draws downloads that way.
  function tileAchievements(a) {
    if (!a || !a.total || !a.done) return null;
    var pct = Math.floor(100 * Math.min(a.done, a.total) / a.total);
    if (a.done >= a.total) {
      var zenith = "Zenith: every achievement, " + a.total + " of " + a.total;
      return { words: zenith, el: h("span", { className: "gs-tile-ach is-zenith", title: zenith, "aria-hidden": "true" }, h(ZenithMedal, { earned: true, size: 26, label: zenith })) };
    }
    var words = "Achievements: " + pct + "% \u00b7 " + a.done + " of " + a.total, r = 10.25, c = 2 * Math.PI * r;
    return { words: words, el: h("span", { className: "gs-tile-ach", title: words, "aria-hidden": "true" },
      h("svg", { className: "gs-tile-ach-ring", width: 26, height: 26, viewBox: "0 0 26 26" },
        h("circle", { cx: 13, cy: 13, r: r, fill: "none", stroke: "rgba(255, 255, 255, 0.22)", strokeWidth: 2.5 }),
        h("circle", { cx: 13, cy: 13, r: r, fill: "none", stroke: "var(--primary)", strokeWidth: 2.5, strokeLinecap: "round",
          strokeDasharray: (c * pct / 100) + " " + c, transform: "rotate(-90 13 13)" })),
      h(Icon, { name: "trophy", size: 11, strokeWidth: 2 })) };
  }
  function GameTile(props) {
    var bg = props.art ? { backgroundImage: "url(" + props.art + ")" } : null;
    var ach = tileAchievements(props.achievements);
    return h("button", { type: "button", className: "gs-tile", style: props.width ? { width: props.width } : null, onClick: props.onClick, "aria-label": props.name + (props.status ? ", " + (STATUS[props.status] || [])[2] : "") + (ach ? ", " + ach.words : "") },
      h("div", { className: cx("gs-tile-art", !props.art && "gs-title-cover"), style: bg, "data-initial": props.art ? undefined : initialOf(props.name) },
        !props.art ? h("span", { className: "gs-title-cover-name", "aria-hidden": "true" }, props.name) : null,
        fineMark(props.status, props.statusLabel) ? h("span", { className: "gs-tile-mark", title: fineMark(props.status, props.statusLabel).words, "aria-hidden": "true" },
            h(Icon, { name: fineMark(props.status, props.statusLabel).icon, size: 14, strokeWidth: 2 }))
          : props.status ? h(StatusBadge, { status: props.status, label: props.statusLabel }) : null,
        ach ? ach.el : null),
      h("div", { className: "gs-tile-name" }, props.name),
      props.meta ? h("div", { className: "gs-tile-meta" }, props.meta) : null);
  }

  /* ---------- SearchField: filters as you type; Esc clears, Enter picks the first match ---------- */
  function SearchField(props) {
    var st = useState(props.value != null ? props.value : "");
    var value = props.value != null ? props.value : st[0];
    function set(v) { st[1](v); if (props.onChange) props.onChange(v); }
    return h("label", { className: cx("gs-search", props.className), style: props.style },
      h(Icon, { name: "search", size: 16 }),
      h("input", {
        type: "search", value: value, placeholder: props.placeholder || "Search", spellCheck: false, autoComplete: "off",
        "aria-label": props.label || props.placeholder || "Search", ref: props.inputRef,
        onChange: function (e) { set(e.target.value); },
        onKeyDown: function (e) {
          if (e.key === "Escape" && value) { e.preventDefault(); set(""); }
          else if (e.key === "Enter" && props.onSubmit) { e.preventDefault(); props.onSubmit(value); }
        }
      }),
      value ? h("button", { type: "button", className: "gs-search-clear", "aria-label": "Clear the search", onClick: function () { set(""); } }, h(Icon, { name: "x", size: 14 }))
        : props.hint ? h("kbd", { className: "gs-search-hint", "aria-hidden": "true" }, props.hint) : null);
  }

  /* ---------- Menu: a short list of choices over the page (a sort, a game's right-click menu) ---------- */
  function Menu(props) {
    var items = props.items || [];
    return h("div", { className: cx("gs-menu", props.className), role: "menu", "aria-label": props.label, style: props.style },
      items.map(function (it, i) {
        if (it.sep) return h("div", { key: "s" + i, className: "gs-menu-sep", role: "separator" });
        if (it.heading) return h("div", { key: "h" + i, className: "gs-menu-heading", role: "presentation" }, it.heading);
        var radio = it.checked != null;
        return h("button", {
          key: it.id, type: "button", role: radio ? "menuitemradio" : "menuitem", "aria-checked": radio ? String(!!it.checked) : undefined,
          className: "gs-menu-item", disabled: it.disabled, onClick: function () { if (props.onPick) props.onPick(it.id); }
        },
          radio ? h("span", { className: "gs-menu-check" }, it.checked ? h(Icon, { name: "check", size: 16, strokeWidth: 2.25 }) : null)
            : h("span", { className: "gs-menu-check" }, it.icon ? h(Icon, { name: it.icon, size: 16, filled: it.filled }) : null),
          h("span", { className: "gs-menu-label" }, it.label),
          it.hint ? h("span", { className: "gs-menu-hint" }, it.hint) : null);
      }));
  }

  /* ---------- GameList: every game by name, in groups, like Steam's list beside the covers ---------- */
  // Statuses a row shows under the name: the ones that need the person, and Playing. The rest show on the game's page.
  var LIST_STATUS = { playing: 1, conflict: 1, held: 1, "in-use": 1, "not-found": 1, blocked: 1 };
  function GameList(props) {
    var groups = props.groups || [];
    // `collapsed` is where a group starts; a click on its heading opens or closes it from then on.
    var closedS = useState({});
    function isClosed(g) { return closedS[0][g.id] != null ? closedS[0][g.id] : !!g.collapsed; }
    function toggle(g) {
      if (props.onToggle) props.onToggle(g.id);
      var n = Object.assign({}, closedS[0]); n[g.id] = !isClosed(g); closedS[1](n);
    }
    var shown = groups.filter(function (g) { return (g.games || []).length > 0; });
    return h("nav", { className: "gs-glist", "aria-label": props.label || "Games" },
      shown.length === 0 ? h("div", { className: "gs-glist-empty" }, props.empty || "No games") : null,
      shown.map(function (g) {
        var closed = isClosed(g);
        return h("section", { key: g.id, className: "gs-glist-group", "aria-label": g.label },
          h("button", { type: "button", className: "gs-glist-head", "aria-expanded": String(!closed), onClick: function () { toggle(g); } },
            h(Icon, { name: closed ? "chevronRight" : "chevronDown", size: 14, strokeWidth: 2 }),
            h("span", null, g.label), h("span", { className: "gs-glist-count" }, g.games.length)),
          closed ? null : g.games.map(function (game) {
            var away = game.installed === false;
            var status = game.status && LIST_STATUS[game.status] ? game.status : null;
            var mark = status ? null : fineMark(game.status, game.statusLabel);
            var word = status ? (game.statusLabel || STATUS[status][2]) : mark ? mark.words : null;
            return h("button", {
              key: game.id, type: "button", className: cx("gs-grow", away && "is-away"),
              "aria-current": game.id === props.selected ? "true" : undefined,
              "aria-label": game.name + (word ? ", " + word : "") + (away ? ", not installed on this PC" : ""),
              title: away ? game.name + " · not installed on this PC" : game.name,
              onClick: function () { if (props.onSelect) props.onSelect(game.id); },
              onContextMenu: props.onMenu ? function (e) { e.preventDefault(); props.onMenu(game, e); } : undefined
            },
              h("span", { className: cx("gs-grow-cover", !game.art && "gs-cover-empty"), style: game.art ? { backgroundImage: "url(" + game.art + ")" } : null, "aria-hidden": "true" }, game.art ? null : initialOf(game.name)),
              h("span", { className: "gs-grow-text" },
                h("span", { className: "gs-grow-name" }, game.name),
                status ? h(StatusBadge, { status: status, label: game.statusLabel, plain: true }) : null),
              mark ? h("span", { className: "gs-grow-mark", title: mark.words, "aria-hidden": "true" }, h(Icon, { name: mark.icon, size: 15, strokeWidth: 2 })) : null);
          }));
      }));
  }

  /* ---------- ProgressBar ---------- */
  // No `value`: how much there is isn't known yet, so a short stripe slides along the track.
  function ProgressBar(props) {
    var known = props.value != null;
    var pct = known ? Math.max(0, Math.min(100, props.value)) : 0;
    return h("div", { className: cx("gs-progress", props.tone && "gs-tone-" + props.tone) },
      (props.label || props.right) ? h("div", { className: "gs-progress-label" }, h("span", null, props.label), h("span", null, props.right != null ? props.right : known ? Math.round(pct) + "%" : "")) : null,
      h("div", { className: "gs-progress-track", role: "progressbar", "aria-valuemin": 0, "aria-valuemax": 100, "aria-valuenow": known ? Math.round(pct) : undefined, "aria-label": props.ariaLabel || props.label },
        h("div", { className: cx("gs-progress-fill", !known && "is-indeterminate"), style: known ? { width: pct + "%" } : null })));
  }

  /* ---------- JobProgress: a job under way, where its result will show (KAN-80) ---------- */
  // state: running (the default), paused, done or failed. `value` 0 to 100, or none while how much there is isn't
  // known yet. `detail` says how far ("42.1 of 113 MB · 412 of 1,108 files"), `speed` how fast ("6.1 MB/s"), `left`
  // how long ("about 12 s left"), `note` why it's paused or what failed. `boxed` sets it on a card of its own.
  function JobProgress(props) {
    var state = props.state || "running";
    var running = state === "running";
    var tone = { paused: "neutral", done: "ok", failed: "warn" }[state];
    var value = state === "done" ? 100 : props.value;
    var known = value != null;
    var pct = known ? Math.max(0, Math.min(100, value)) : 0;
    var right = running ? (props.speed || (known ? Math.round(pct) + "%" : null)) : null;
    return h("div", { className: cx("gs-job", tone && "gs-tone-" + tone, props.boxed && "is-boxed", props.className), role: "group", "aria-label": props.title,
      "aria-busy": running ? "true" : undefined },
      h("div", { className: "gs-job-head" },
        h("span", { className: "gs-job-icon" }, running ? h(Spinner, { size: 16 }) : h(Icon, { name: { paused: "pause", done: "check", failed: "alert" }[state], size: 16, strokeWidth: 2 })),
        h("span", { className: "gs-job-title" }, props.title, running && !known ? h(Dots) : null),
        right ? h("span", { className: "gs-job-speed" }, right) : null),
      h("div", { className: "gs-progress-track", role: "progressbar", "aria-valuemin": 0, "aria-valuemax": 100, "aria-valuenow": known ? Math.round(pct) : undefined,
        "aria-valuetext": props.detail, "aria-label": props.title },
        h("div", { className: cx("gs-progress-fill", !known && "is-indeterminate", running && known && "is-moving"), style: known ? { width: pct + "%" } : null })),
      (props.detail || props.note || props.left) ? h("div", { className: "gs-job-foot", "aria-live": state === "running" ? undefined : "polite" },
        h("span", { className: props.note ? "is-note" : null }, props.note || props.detail),
        running && props.left ? h("span", null, props.left) : null) : null);
  }

  /* ---------- ActivityGrid ---------- */
  var LEVEL_LABEL = ["No play", "Under 2 h", "2 to 4 h", "Over 4 h"];
  function ActivityGrid(props) {
    var days = props.days || [];
    var offset = props.startWeekday || 0;
    var cells = [];
    for (var i = 0; i < offset; i++) cells.push(h("div", { key: "e" + i, className: "gs-cell gs-cell-empty" }));
    days.forEach(function (lvl, i) {
      cells.push(h("div", { key: i, className: "gs-cell gs-cell-" + lvl, title: "Day " + (i + 1) + ": " + LEVEL_LABEL[lvl], role: "img", "aria-label": "Day " + (i + 1) + ", " + LEVEL_LABEL[lvl] }));
    });
    return h("div", { className: "gs-activity" },
      h("div", { className: "gs-activity-legend" },
        [["gs-cell-0", "0h"], ["gs-cell-1", "< 2h"], ["gs-cell-2", "2–4h"], ["gs-cell-3", "> 4h"]].map(function (l) {
          return h("span", { key: l[0] }, h("i", { className: "gs-cell " + l[0], style: { aspectRatio: "1", borderRadius: "50%" } }), l[1]);
        })),
      h("div", { className: "gs-activity-grid" }, cells),
      h("div", { className: "gs-activity-days", "aria-hidden": "true" }, ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"].map(function (d) { return h("span", { key: d }, d); })));
  }

  /* ---------- Checkbox and Switch ---------- */
  function Checkbox(props) {
    var ref = useRef(null);
    useEffect(function () { if (ref.current) ref.current.indeterminate = !!props.indeterminate; }, [props.indeterminate]);
    return h("label", { className: cx("gs-check", props.disabled && "is-disabled", props.className), onClick: function (e) { e.stopPropagation(); } },
      h("input", { ref: ref, type: "checkbox", checked: !!props.checked, disabled: props.disabled, onChange: function (e) { if (props.onChange) props.onChange(e.target.checked); }, "aria-label": props.label && !props.showLabel ? props.label : undefined }),
      props.showLabel ? h("span", null, props.label) : null);
  }

  function Switch(props) {
    var on = !!props.checked;
    return h("button", {
      type: "button", role: "switch", "aria-checked": String(on), "aria-label": props.label, disabled: props.disabled,
      className: cx("gs-switch", on && "is-on", props.className),
      onClick: function () { if (!props.disabled && props.onChange) props.onChange(!on); }
    }, h("span", { className: "gs-switch-knob" }));
  }

  /* ---------- ConsoleTable ---------- */
  function ConsoleTable(props) {
    var cols = props.columns || [];
    var rows = props.rows || [];
    var sel = props.selected || [];
    var selectable = !!props.onSelect;
    var all = rows.length > 0 && rows.every(function (r) { return sel.indexOf(r.id) >= 0; });
    var some = !all && rows.some(function (r) { return sel.indexOf(r.id) >= 0; });
    function toggle(id, on) {
      var next = sel.filter(function (x) { return x !== id; });
      if (on) next.push(id);
      props.onSelect(next);
    }
    return h("div", { className: "gs-console" },
      props.command || props.toolbar ? h("div", { className: "gs-console-bar" },
        h("span", null, h("span", { className: "gs-console-prompt" }, "> "), props.command),
        props.toolbar || null) : null,
      h("table", { className: "gs-ctable" },
        h("thead", null, h("tr", null,
          selectable ? h("th", { className: "gs-w" }, h(Checkbox, { label: "Select all", checked: all, indeterminate: some, onChange: function (on) { props.onSelect(on ? rows.map(function (r) { return r.id; }) : []); } })) : null,
          cols.map(function (c) { return h("th", { key: c.key, className: c.align === "right" ? "gs-num" : null }, c.label); }))),
        h("tbody", null, rows.length === 0 && props.empty ? h("tr", { className: "gs-ctable-empty" }, h("td", { colSpan: cols.length + (selectable ? 1 : 0) }, props.empty)) : null, rows.map(function (r) {
          var on = sel.indexOf(r.id) >= 0;
          return h("tr", { key: r.id, "aria-selected": selectable ? String(on) : undefined, onClick: selectable ? function () { toggle(r.id, !on); } : props.onRowClick ? function () { props.onRowClick(r); } : undefined, style: selectable || props.onRowClick ? { cursor: "pointer" } : null },
            selectable ? h("td", null, h(Checkbox, { label: "Select " + (r.name || r.id), checked: on, onChange: function (v) { toggle(r.id, v); } })) : null,
            cols.map(function (c) {
              var v = c.render ? c.render(r) : r[c.key];
              if (c.key === "status" && typeof v === "string") v = h(StatusBadge, { status: v, plain: true });
              return h("td", { key: c.key, className: cx(c.align === "right" && "gs-num", c.path && "gs-path"), title: c.path ? r[c.key] : undefined }, v);
            }));
        }))));
  }

  /* ---------- ConsoleLog ---------- */
  function ConsoleLog(props) {
    var lines = props.lines || [];
    return h("div", { className: cx("gs-log", props.grow && "gs-log-grow", props.dated && "gs-log-dated"), role: "log", "aria-label": props.label || "Activity log", style: props.height ? { maxHeight: props.height } : null },
      lines.map(function (l, i) {
        return h("div", { key: i, className: "gs-log-line gs-log-" + (l.level || "info") },
          h("time", null, l.time), h("span", { className: "gs-log-lvl" }, (l.tag || l.level || "info").toUpperCase()), h("span", { className: "gs-log-msg" }, l.msg));
      }),
      props.live ? h("div", { className: "gs-log-line" }, h("time", null, ""), h("span", { className: "gs-log-lvl gs-console-prompt" }, ">"), h("span", null, h("span", { className: "gs-log-cursor" }))) : null);
  }

  /* ---------- PlayBar: under a game's hero, Play (or the status's action), a few facts, the game's icon buttons ---------- */
  function PlayBar(props) {
    var stats = (props.stats || []).filter(Boolean);
    return h("section", { className: cx("gs-card", "gs-playbar", props.className), "aria-label": props.label || "Play" },
      props.primary ? h("div", { className: "gs-playbar-lead" }, props.primary) : null,
      h("dl", { className: "gs-playbar-stats" }, stats.map(function (s, i) {
        return h("div", { key: s.label || i, className: "gs-playbar-stat" },
          h("dt", null, s.label),
          h("dd", null, s.status ? h(StatusBadge, { status: s.status, label: s.value, plain: true }) : s.value));
      })),
      props.actions ? h("div", { className: "gs-playbar-actions" }, props.actions) : null);
  }

  /* ---------- Facts: short labelled facts (Developer, Released, Installed in); empty ones are left out ---------- */
  function Facts(props) {
    var items = (props.items || []).filter(function (it) { return it && it.value != null && it.value !== ""; });
    return h("dl", { className: cx("gs-facts", props.className), style: props.style },
      items.map(function (it, i) {
        return h("div", { key: it.label || i, className: "gs-fact" },
          h("dt", null, it.label),
          h("dd", { className: it.mono ? "gs-mono" : undefined, title: it.title }, it.value));
      }));
  }

  /* ---------- Select: the current choice on a small button; the choices in a Menu under it ---------- */
  function Select(props) {
    var open = useState(false);
    var options = props.options || [];
    var cur = options.filter(function (o) { return o.id === props.value; })[0] || options[0] || { label: "" };
    useEffect(function () {
      if (!open[0]) return undefined;
      function close() { open[1](false); }
      function key(e) { if (e.key === "Escape") open[1](false); }
      document.addEventListener("click", close); document.addEventListener("keydown", key);
      return function () { document.removeEventListener("click", close); document.removeEventListener("keydown", key); };
    }, [open[0]]);
    return h("span", { className: cx("gs-select", props.className) },
      h(Button, { size: "sm", variant: "secondary", iconAfter: "chevronDown", disabled: props.disabled, "aria-haspopup": "menu", "aria-expanded": String(open[0]),
        "aria-label": (props.label ? props.label + ": " : "") + cur.label, onClick: function (e) { e.stopPropagation(); open[1](!open[0]); } }, cur.label),
      open[0] ? h(Menu, { className: "gs-select-menu", label: props.label, items: options.map(function (o) { return { id: o.id, label: o.label, checked: o.id === cur.id }; }),
        onPick: function (id) { open[1](false); if (props.onChange) props.onChange(id); } }) : null);
  }

  /* ---------- FileTree: the files a game's saves are made of; ticked ones are backed up ---------- */
  // item: { id, name, kind: "folder" | "file" | "registry", meta, tag, note, checked, mixed, locked, open, children }
  function FileTree(props) {
    var openS = useState({});
    function isOpen(it) { return openS[0][it.id] != null ? openS[0][it.id] : !!it.open; }
    function flip(it) { var n = Object.assign({}, openS[0]); n[it.id] = !isOpen(it); openS[1](n); }
    function rows(list, depth) {
      var out = [];
      list.forEach(function (it) {
        var kids = it.children || [];
        var open = kids.length > 0 && isOpen(it);
        var off = !it.checked && !it.mixed;
        out.push(h("div", { key: it.id, className: cx("gs-ftree-row", off && "is-off", it.locked && "is-locked"), role: "treeitem",
          "aria-level": depth + 1, "aria-expanded": kids.length ? String(open) : undefined, style: { paddingLeft: 4 + depth * 22 } },
          kids.length ? h("button", { type: "button", className: "gs-ftree-twisty", "aria-label": (open ? "Close " : "Open ") + it.name, onClick: function () { flip(it); } },
            h(Icon, { name: open ? "chevronDown" : "chevronRight", size: 14, strokeWidth: 2 })) : h("span", { className: "gs-ftree-twisty", "aria-hidden": "true" }),
          it.locked ? h("span", { className: "gs-ftree-lock", title: it.note }, h(Icon, { name: "lock", size: 14 }))
            : h(Checkbox, { label: (off ? "Back up " : "Leave out ") + it.name, checked: !!it.checked && !it.mixed, indeterminate: !!it.mixed,
              onChange: function (on) { if (props.onToggle) props.onToggle(it.id, on); } }),
          h(Icon, { name: it.kind === "folder" ? "folder" : it.kind === "registry" ? "key" : "file", size: 16, className: "gs-ftree-icon" }),
          h("span", { className: "gs-ftree-name", title: it.name }, it.name),
          it.note ? h("span", { className: "gs-ftree-note" }, it.note) : null,
          it.tag ? h("span", { className: "gs-tag" }, it.tag) : null,
          it.meta ? h("span", { className: "gs-ftree-meta" }, it.meta) : null));
        if (open) out = out.concat(rows(kids, depth + 1));
      });
      return out;
    }
    return h("div", { className: cx("gs-ftree", props.className), role: "tree", "aria-label": props.label || "Files" }, rows(props.items || [], 0));
  }
  // A tree with one item ticked or unticked, and its folder's box showing whether all, some or none of it is in.
  function treeToggle(items, id, on) {
    function mark(it) {
      var n = Object.assign({}, it, { checked: on, mixed: false });
      if (it.children) n.children = it.children.map(function (c) { return c.locked ? c : mark(c); });
      return n;
    }
    function sum(kids) {
      var live = kids.filter(function (c) { return !c.locked; });
      var full = live.filter(function (c) { return c.checked && !c.mixed; }).length;
      var any = live.some(function (c) { return c.checked || c.mixed; });
      return { checked: any, mixed: any && full < live.length };
    }
    return items.map(function (it) {
      if (it.id === id) return mark(it);
      if (!it.children) return it;
      var kids = treeToggle(it.children, id, on);
      return kids === it.children ? it : Object.assign({}, it, { children: kids }, sum(kids));
    });
  }
  // How many files a tree backs up, and their size: { files, bytes, all, allBytes }.
  function treeCount(items) {
    var r = { files: 0, bytes: 0, all: 0, allBytes: 0 };
    (function walk(list) {
      list.forEach(function (it) {
        if (it.children) { walk(it.children); return; }
        if (it.locked) return;
        var n = it.count || 1, b = it.bytes || 0;
        r.all += n; r.allBytes += b;
        if (it.checked) { r.files += n; r.bytes += b; }
      });
    })(items || []);
    return r;
  }

  /* ---------- sizes ---------- */
  function fmt(bytes) {
    if (bytes >= 1073741824) return (bytes / 1073741824).toFixed(2) + " GB";
    if (bytes >= 1048576) return (bytes / 1048576).toFixed(1) + " MB";
    return Math.max(1, Math.round(bytes / 1024)) + " KB";
  }

  /* ---------- GamePropertiesDialog: one game's settings, like Steam's Properties ---------- */
  var PROP_SECTIONS = [
    { id: "general", label: "General", icon: "info" },
    { id: "art", label: "Art", icon: "palette" },
    { id: "launch", label: "Launch", icon: "play" },
    { id: "files", label: "Installed files", icon: "drive" },
    { id: "saves", label: "Saves", icon: "saves" },
    { id: "sync", label: "Sync", icon: "sync" }];
  var SETTINGS_FILES = [{ id: "sync", label: "Sync between PCs" }, { id: "this-pc", label: "Back up on this PC" }, { id: "off", label: "Don\u2019t back up" }];
  var SCREENSHOTS = [{ id: "this-pc", label: "Back up on this PC" }, { id: "off", label: "Don\u2019t back up" }];
  var CONFLICT = [{ id: "newest", label: "Newest wins" }, { id: "ask", label: "Always ask" }, { id: "this-pc", label: "This PC wins" }];
  function GamePropertiesDialog(props) {
    var g = props.game || {};
    var launch = g.launch || {};
    var initial = {
      name: g.name || "", favourite: !!g.favourite, shown: !g.hidden, args: launch.args || "",
      files: g.files || [], settings: g.settings || "this-pc", screenshots: g.screenshots || "off", skip: g.skip !== false,
      mode: g.mode || "sync", conflict: g.conflict || "newest",
      art: { cover: g.art && g.art.mine && g.art.mine.cover ? "mine" : null, hero: g.art && g.art.mine && g.art.mine.hero ? "mine" : null, logo: g.art && g.art.mine && g.art.mine.logo ? "mine" : null }
    };
    var secS = useState(props.section || "general"), sec = secS[0];
    var stS = useState(initial), st = stS[0];
    var copiedS = useState(null);
    function set(k, v) { var n = Object.assign({}, st); n[k] = v; stS[1](n); }
    var changed = Object.keys(initial).filter(function (k) { return JSON.stringify(initial[k]) !== JSON.stringify(st[k]); });
    var count = treeCount(st.files);
    var store = g.store || null;
    function copy(what) { copiedS[1](what); }
    function row(title, description, control, extra) { return h(SettingsRow, Object.assign({ title: title, description: description }, extra || {}), control); }
    var views = {
      general: function () {
        return [
          h("h3", { key: "t", className: "gs-props-title" }, "General"),
          row("Name", "What GameSync calls it. A rescan keeps the name you give it.",
            h("input", { className: "gs-input gs-input-wide", value: st.name, "aria-label": "Name", onChange: function (e) { set("name", e.target.value); } }), { key: "name" }),
          row("Favourite", "First in the library\u2019s list and covers, on this PC.", h(Switch, { label: "Favourite", checked: st.favourite, onChange: function (v) { set("favourite", v); } }), { key: "fav" }),
          row("Show in the library", "A hidden game still backs up and syncs; the library\u2019s Hidden view brings it back.", h(Switch, { label: "Show in the library", checked: st.shown, onChange: function (v) { set("shown", v); } }), { key: "shown" }),
          row("Game ID", h("span", null, "For the command line: ", h("span", { className: "gs-mono" }, "gamesync sync " + g.id)),
            h(Button, { size: "sm", variant: "ghost", icon: copiedS[0] === "id" ? "check" : "copy", onClick: function () { copy("id"); } }, copiedS[0] === "id" ? "Copied" : "Copy"), { key: "id" }),
          g.antiCheat ? h("div", { key: "ac", className: "gs-note" }, h(Icon, { name: "shield", size: 16 }),
            "Ships " + g.antiCheat + ": it starts only through " + (store || "its launcher") + ", learn mode stays off, and its saves are never shared.") : null];
      },
      art: function () {
        var art = g.art || {}, mine = art.mine || {};
        function pick(kind, value) { var n = Object.assign({}, st.art); n[kind] = value; set("art", n); }
        function slot(kind, title, description) {
          var own = st.art[kind] === "mine", src = own ? mine[kind] : art[kind];
          return h(SettingsRow, { key: kind, title: title, description: description },
            h("div", { className: "gs-art-slot" },
              h("span", { className: cx("gs-art-thumb", "is-" + kind, !src && "gs-cover-empty"), style: src ? { backgroundImage: "url(" + src + ")" } : null, "aria-hidden": "true" },
                src ? null : kind === "logo" ? "Its name" : initialOf(g.name)),
              h("div", { className: "gs-art-text" },
                h("div", { className: "gs-art-from" }, own ? "Your image" : src ? "Steam\u2019s" : kind === "logo" ? "None: its name shows" : "None: a title cover shows"),
                h("div", { className: "gs-row" },
                  h(Button, { size: "sm", variant: "secondary", icon: "file", onClick: function () { pick(kind, "mine"); } }, own ? "Choose another\u2026" : "Choose an image\u2026"),
                  own ? h(Button, { size: "sm", variant: "ghost", onClick: function () { pick(kind, null); } }, art[kind] ? "Use Steam\u2019s" : "Remove") : null))));
        }
        return [
          h("h3", { key: "t", className: "gs-props-title" }, "Art"),
          slot("cover", "Cover", "The tile in the library and on Home: tall, like Steam\u2019s 600 \u00d7 900."),
          slot("hero", "Banner", "The wide picture at the top of its page, and on Home when it\u2019s the last played: like 1920 \u00d7 620."),
          slot("logo", "Logo", "Shown over the banner in place of its name: a PNG with a see-through background."),
          h("div", { key: "n", className: "gs-note" }, h(Icon, { name: "info", size: 16 }),
            "Your own images stay on this PC: they\u2019re never synced or shared. JPEG, PNG or WebP, up to 8 MB, each checked like Steam\u2019s art.")];
      },
      launch: function () {
        var line = "\"C:\\Program Files\\GameSync\\GameSync.Tray.exe\" launch " + g.id + " -- %command%";
        return [
          h("h3", { key: "t", className: "gs-props-title" }, "Launch"),
          row(store ? "Starts through " + store : "Program", store ? store + " starts it, so its overlay, its cloud and its own launch options work as usual." : "GameSync starts it in its own folder, never as admin.",
            store ? null : h(Button, { size: "sm", variant: "secondary", icon: "folder" }, "Change\u2026"), { key: "route" }),
          h("div", { key: "route-line", className: "gs-well-line" }, h("span", { title: store ? launch.url : launch.program }, (store ? launch.url : launch.program) || "Not set yet")),
          store ? row("Steam\u2019s launch options", "Steam adds these every time the game starts. Change them in Steam: the game\u2019s Properties, General.",
            h("span", { className: "gs-row", style: { flexWrap: "nowrap" } }, h("span", { className: "gs-mono", style: { fontSize: 13 } }, launch.steamOptions || "None"),
              h(Button, { size: "sm", variant: "ghost", iconAfter: "external" }, "Open in Steam")), { key: "steam" })
            : row("Launch options", "Added after the program\u2019s name when GameSync starts it, such as -windowed.",
              h("input", { className: "gs-input gs-input-wide", value: st.args, placeholder: "None", "aria-label": "Launch options", onChange: function (e) { set("args", e.target.value); } }), { key: "args" }),
          store ? row("Starting from Steam", "To get GameSync\u2019s check for a newer save when you start from Steam too, put this in Steam\u2019s launch options:",
            h(Button, { size: "sm", variant: "secondary", icon: copiedS[0] === "line" ? "check" : "copy", onClick: function () { copy("line"); } }, copiedS[0] === "line" ? "Copied" : "Copy the line"), { key: "line", stack: false }) : null,
          store ? h("div", { key: "l", className: "gs-well-line" }, h("span", { title: line }, line)) : null,
          h("div", { key: "n", className: "gs-note" }, h(Icon, { name: "info", size: 16 }),
            "Play in GameSync always checks for a newer save on your other PC first, and brings it down before the game starts.")];
      },
      files: function () {
        return [
          h("h3", { key: "t", className: "gs-props-title" }, "Installed files"),
          g.installed === false ? h("div", { key: "away", className: "gs-note" }, h(Icon, { name: "info", size: 16 }), "Not installed on this PC. Its saves are still here, and still back up.")
            : h(Facts, { key: "f", items: [
              { label: "Folder", value: g.installDir, mono: true },
              { label: "Size", value: g.size },
              { label: "Store", value: store ? store + (g.storeId ? " \u00b7 app " + g.storeId : "") : "None: a game in its own folder" },
              { label: "Build", value: g.build, mono: true },
              { label: "Engine", value: g.engine },
              { label: "Found", value: g.foundBy }] }),
          g.installed === false ? null : h("div", { key: "b", className: "gs-row" },
            h(Button, { size: "sm", variant: "secondary", icon: "folder" }, "Open the folder"),
            store && g.storeId ? h(Button, { size: "sm", variant: "ghost", iconAfter: "external" }, "Store page") : null)];
      },
      saves: function () {
        return [
          h("h3", { key: "t", className: "gs-props-title" }, "Saves"),
          h("p", { key: "d", className: "gs-muted", style: { margin: 0, fontSize: 13, lineHeight: "19px" } },
            "Ticked files are backed up and synced. Unticked ones stay on this PC only, and older versions keep them; nothing is deleted."),
          h("div", { key: "c", className: "gs-props-count" },
            h("span", null, h("b", null, count.files), " of " + count.all + " files \u00b7 ", h("b", null, fmt(count.bytes)), " backed up"),
            h("span", { className: "gs-row" }, h(Button, { size: "sm", variant: "secondary", icon: "plus" }, "Add a file or folder"),
              h(Button, { size: "sm", variant: "ghost", disabled: !!g.antiCheat, title: g.antiCheat ? "Not for games with an anti-cheat" : "Watch one session to find saves the scan missed" }, "Learn mode\u2026"))),
          h(FileTree, { key: "tree", label: g.name + "\u2019s save files", items: st.files, onToggle: function (id, on) { set("files", treeToggle(st.files, id, on)); } }),
          row("Settings files", "A game\u2019s options and key bindings, tagged Settings.", h(Select, { label: "Settings files", value: st.settings, options: SETTINGS_FILES, onChange: function (v) { set("settings", v); } }), { key: "set" }),
          row("Screenshots", "Pictures the game saves itself.", h(Select, { label: "Screenshots", value: st.screenshots, options: SCREENSHOTS, onChange: function (v) { set("screenshots", v); } }), { key: "shots" }),
          row("Skip logs, crash dumps and caches", "They never hold saves, and some are large.", h(Switch, { label: "Skip logs, crash dumps and caches", checked: st.skip, onChange: function (v) { set("skip", v); } }), { key: "skip" }),
          h("div", { key: "n", className: "gs-note" }, h(Icon, { name: "info", size: 16 }),
            h("span", null, "New games follow your defaults in Settings, Backup and sync. Changes here are this game\u2019s own, and travel with its saves to your other PCs."))];
      },
      sync: function () {
        return [
          h("h3", { key: "t", className: "gs-props-title" }, "Sync"),
          row("Keep it in step", st.mode === "sync" ? "Each PC\u2019s save goes to the cloud after you play, and the newest comes down before you play."
            : "Its store syncs it between PCs; GameSync keeps every version as a backup and never brings one down by itself.",
            h(PillTabs, { label: "Sync", value: st.mode, onChange: function (v) { set("mode", v); }, items: [{ id: "sync", label: "Between your PCs" }, { id: "backup", label: "Back up only" }] }), { key: "mode" }),
          row("When both PCs changed it", "Newest wins keeps the other side pinned, with a Swap button. GameSync always asks on a first sync, a save that shrank by half, a change outside play, or a wrong clock.",
            h(Select, { label: "When both PCs changed it", value: st.conflict, options: CONFLICT, onChange: function (v) { set("conflict", v); } }), { key: "conflict" }),
          row("Stop syncing it", "Its versions stay in the cloud and on this PC. GameSync asks first.", h(Button, { size: "sm", variant: "danger" }, "Stop syncing\u2026"), { key: "stop" })];
      }
    };
    return h("div", { className: "gs-dialog gs-props", role: "dialog", "aria-modal": "true", "aria-label": "Properties of " + (g.name || "the game") },
      h("div", { className: "gs-props-head" },
        h("div", { style: { flex: 1, minWidth: 0 } }, h("h2", { className: "gs-dialog-title" }, "Properties"), h("p", { className: "gs-dialog-sub", style: { margin: 0 } }, g.name)),
        h(IconButton, { icon: "x", label: "Close", size: "sm", onClick: props.onClose })),
      h("div", { className: "gs-props-main" },
        h("div", { className: "gs-props-nav" }, h(SettingsNav, { label: "Properties", items: PROP_SECTIONS, current: sec, onChange: secS[1] })),
        h("div", { className: "gs-props-body" }, views[sec]())),
      h("div", { className: "gs-props-foot" },
        h("span", { className: "gs-faint", style: { fontSize: 12 } }, changed.length === 0 ? "No changes" : changed.length === 1 ? "1 change, not saved yet" : changed.length + " changes, not saved yet"),
        h("span", { className: "gs-row" },
          h(Button, { variant: "ghost", onClick: props.onClose }, "Cancel"),
          h(Button, { variant: "primary", disabled: changed.length === 0, onClick: function () { if (props.onSave) props.onSave(st); } }, "Save changes"))));
  }

  /* ---------- ShareSavesDialog ---------- */
  function ShareSavesDialog(props) {
    var games = props.games || [];
    var shareable = games.filter(function (g) { return !g.blocked; });
    var leftOut = games.filter(function (g) { return g.blocked; });
    var modeS = useState(props.mode || "pick");
    var mode = modeS[0];
    var init = {};
    (props.initialSelection || []).forEach(function (id) {
      var g = shareable.filter(function (x) { return x.id === id; })[0];
      if (g) init[id] = [g.versions && g.versions[0] ? g.versions[0].id : "latest"];
    });
    var pickS = useState(init), picked = pickS[0];
    var openS = useState(props.expanded || []), open = openS[0];
    var stageS = useState(props.stage || "choose"), stage = stageS[0];
    var progS = useState(props.progress || 0);
    var latestS = useState(!!props.latestOnly), latestOnly = latestS[0];

    function setPick(gid, list) { var n = Object.assign({}, picked); if (list && list.length) n[gid] = list; else delete n[gid]; pickS[1](n); }
    function toggleGame(g, on) { if (g.blocked) return; setPick(g.id, on ? [g.versions && g.versions[0] ? g.versions[0].id : "latest"] : null); }
    function toggleVer(g, vid, on) {
      var cur = (picked[g.id] || []).filter(function (x) { return x !== vid; });
      if (on) cur.push(vid);
      setPick(g.id, cur);
    }
    function sizeOf(g, vid) { var v = (g.versions || []).filter(function (x) { return x.id === vid; })[0]; return v ? v.size : g.size; }
    var pickedGames = Object.keys(picked);
    var pickedVersions = pickedGames.reduce(function (n, k) { return n + picked[k].length; }, 0);
    var pickedBytes = pickedGames.reduce(function (n, k) {
      var g = games.filter(function (x) { return x.id === k; })[0];
      return n + picked[k].reduce(function (m, vid) { return m + sizeOf(g, vid); }, 0);
    }, 0);
    var allBytes = shareable.reduce(function (n, g) { return n + (latestOnly ? g.size : (g.totalSize || g.size || 0)); }, 0);
    var allVersions = latestOnly ? shareable.length : shareable.reduce(function (n, g) { return n + (g.versionCount || (g.versions || []).length || 1); }, 0);
    var date = props.date || "2026-09-27";
    var zipName = mode === "all" ? "GameSync-all-saves-" + date + ".zip" : "GameSync-saves-" + date + ".zip";
    var outBytes = mode === "all" ? allBytes : pickedBytes;

    function start() {
      stageS[1]("packing"); progS[1](8);
      var p = 8;
      var t = setInterval(function () {
        p += 17; if (p >= 100) { p = 100; clearInterval(t); stageS[1]("done"); }
        progS[1](p);
      }, 260);
      if (props.onCreate) props.onCreate(mode === "all" ? { mode: "all", latestOnly: latestOnly } : { mode: "pick", selection: picked });
    }

    var body;
    if (mode === "all") {
      body = h("div", { className: "gs-share-all" },
        h("div", null, "save folder"),
        h("div", { className: "gs-mono-stat" }, fmt(allBytes)),
        h("div", null, shareable.length + " games · " + allVersions + (latestOnly ? " latest saves" : " versions kept on this PC")),
        h("div", { className: "gs-faint" }, props.saveRoot || "E:\\Saves and Backups\\Games\\GameSync"),
        h(Checkbox, { label: "Only the latest save of each game", showLabel: true, checked: latestOnly, onChange: latestS[1] }),
        leftOut.length ? h("div", { className: "gs-note" }, h(Icon, { name: "lock", size: 16 }), "Left out: " + leftOut.map(function (g) { return g.name; }).join(", ") + ". " + leftOut[0].blocked) : null);
    } else {
      body = games.map(function (g) {
        var on = !!picked[g.id], isOpen = open.indexOf(g.id) >= 0;
        var n = (picked[g.id] || []).length, total = (g.versions || []).length;
        return h("div", { key: g.id, className: "gs-share-game" },
          h("div", { className: cx("gs-share-item", on && "is-on", g.blocked && "is-blocked"), onClick: function () { toggleGame(g, !on); }, role: "group", "aria-label": g.name },
            h(Checkbox, { label: "Share " + g.name, checked: on, disabled: !!g.blocked, onChange: function (v) { toggleGame(g, v); } }),
            h("div", { className: cx("gs-share-cover", !g.art && "gs-cover-empty"), style: g.art ? { backgroundImage: "url(" + g.art + ")" } : null, "aria-hidden": "true" }, g.art ? null : initialOf(g.name)),
            h("div", null,
              h("div", { className: "gs-share-name" }, g.name),
              g.blocked ? h("div", { className: "gs-share-meta gs-share-why" }, h(Icon, { name: "lock", size: 12, strokeWidth: 2 }), g.blocked)
                : h("div", { className: "gs-share-meta" }, on ? n + " of " + total + " versions" : (g.meta || "latest " + (g.versions && g.versions[0] ? g.versions[0].when : "")))),
            h("div", { className: "gs-share-size" }, g.blocked ? "" : fmt(on ? picked[g.id].reduce(function (m, v) { return m + sizeOf(g, v); }, 0) : g.size)),
            total > 1 && !g.blocked ? h("button", { type: "button", className: "gs-share-toggle", "aria-expanded": String(isOpen), "aria-label": (isOpen ? "Hide" : "Show") + " versions of " + g.name, onClick: function (e) { e.stopPropagation(); openS[1](isOpen ? open.filter(function (x) { return x !== g.id; }) : open.concat([g.id])); } }, h(Icon, { name: isOpen ? "chevronDown" : "chevronRight", size: 16 })) : h("span")),
          isOpen && !g.blocked ? h("div", { className: "gs-share-versions" }, g.versions.map(function (v) {
            var von = (picked[g.id] || []).indexOf(v.id) >= 0;
            return h("div", { key: v.id, className: "gs-share-version", onClick: function () { toggleVer(g, v.id, !von); } },
              h(Checkbox, { label: v.label, checked: von, onChange: function (x) { toggleVer(g, v.id, x); } }),
              h("span", null, h("b", null, v.label), "  " + v.when + (v.pc ? "  " + v.pc : "") + (v.pinned ? "  · pinned" : "")),
              h("span", null, fmt(v.size)));
          })) : null);
      });
    }

    var canCreate = mode === "all" || pickedVersions > 0;
    var foot;
    if (stage === "choose") {
      foot = [
        h("div", { key: "sum", className: "gs-share-summary" },
          mode === "all"
            ? h("span", null, "Packs the save folder into ", h("b", null, zipName))
            : h("span", null, h("b", null, pickedGames.length + " games"), " · " + pickedVersions + " versions → ", h("b", null, zipName)),
          h("span", null, fmt(outBytes))),
        h("div", { key: "note", className: "gs-note" }, h(Icon, { name: "shield", size: 16 }), "Only save data goes in, never game files. A README inside explains how to restore by hand; in GameSync, use Import saves."),
        h("div", { key: "act", className: "gs-dialog-actions" },
          h(Button, { variant: "ghost", onClick: props.onClose }, "Cancel"),
          h(Button, { variant: "primary", icon: "zip", disabled: !canCreate, onClick: start }, mode === "all" ? "Package everything" : "Create zip"))
      ];
    } else if (stage === "packing") {
      foot = [h(ProgressBar, { key: "p", value: progS[0], label: "Packing " + zipName, right: progS[0] + "%" }),
        h("div", { key: "a", className: "gs-dialog-actions" }, h("span", { className: "gs-faint gs-mono", style: { fontSize: 12 } }, "checking files and compressing…"), h(Button, { variant: "ghost" }, "Cancel"))];
    } else {
      foot = [h("div", { key: "d", className: "gs-done" }, h(Icon, { name: "check", size: 20, className: "gs-done-icon" }),
        h("div", null, h("div", null, "Zip ready · " + fmt(outBytes)), h("code", null, (props.outDir || "C:\\Users\\You\\Downloads") + "\\" + zipName))),
        h("div", { key: "a", className: "gs-dialog-actions" },
          h(Button, { variant: "ghost", onClick: props.onClose }, "Done"),
          h("div", { className: "gs-row" }, h(Button, { variant: "secondary", icon: "copy" }, "Copy path"), h(Button, { variant: "primary", icon: "folder" }, "Show in folder")))];
    }

    return h("div", { className: "gs-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "gs-share-title" },
      h("div", { className: "gs-dialog-head" },
        h("div", { className: "gs-card-head" },
          h("div", null, h("h2", { id: "gs-share-title", className: "gs-dialog-title" }, "Share saves"),
            h("p", { className: "gs-dialog-sub" }, mode === "all" ? "Everything in your save folder, packed into one zip." : "Pick games, or open a game to pick exact versions.")),
          h(IconButton, { icon: "x", label: "Close", onClick: props.onClose })),
        stage === "choose" ? h(PillTabs, { label: "What to share", value: mode, onChange: modeS[1], items: [{ id: "pick", label: "Choose saves" }, { id: "all", label: "Share all" }] }) : null),
      h("div", { className: "gs-dialog-body" }, body),
      h("div", { className: "gs-dialog-foot" }, foot));
  }

  /* ---------- ImportSavesDialog ---------- */
  function ImportSavesDialog(props) {
    var items = props.items || [];
    var init = {};
    // Version 51 (R16): a game with an anti-cheat, or that plays only online, is `blocked`: it can't be ticked and says why.
    function closed(it) { return it.match === "unknown" || !!it.blocked; }
    items.forEach(function (it) { if (!closed(it)) init[it.id] = true; });
    var ps = useState(init), picked = ps[0];
    var st = useState(props.stage || "review"), stage = st[0];
    var chosen = items.filter(function (it) { return picked[it.id] && !closed(it); });
    var versions = chosen.reduce(function (n, it) { return n + (it.versions || 1); }, 0);
    var bytes = chosen.reduce(function (n, it) { return n + (it.size || 0); }, 0);
    function toggle(it, on) { if (closed(it)) return; var n = Object.assign({}, picked); if (on) n[it.id] = true; else delete n[it.id]; ps[1](n); }
    var rows = items.map(function (it) {
      var disabled = closed(it);
      var on = !!picked[it.id] && !disabled;
      var line = it.blocked ? "Can't be imported"
        : it.match === "matched" ? (it.versions || 1) + ((it.versions || 1) === 1 ? " version" : " versions") + " · added as pinned, not current"
        : it.match === "not-installed" ? "Not installed here · waits until the game is found"
        : "Not in your library · can't be imported";
      return h("div", { key: it.id, className: cx("gs-share-item", on && "is-on", disabled && "is-blocked"), onClick: function () { toggle(it, !on); } },
        h(Checkbox, { label: "Import " + it.name, checked: on, disabled: disabled, onChange: function (v) { toggle(it, v); } }),
        h("div", { className: cx("gs-share-cover", !it.art && "gs-cover-empty"), style: it.art ? { backgroundImage: "url(" + it.art + ")" } : null, "aria-hidden": "true" }, it.art ? null : initialOf(it.name)),
        h("div", null,
          h("div", { className: "gs-share-name" }, it.name),
          h("div", { className: "gs-share-meta" }, line),
          it.warn ? h("div", { className: "gs-import-note gs-import-warn" }, h(Icon, { name: "alert", size: 14, strokeWidth: 2 }), it.warn) : null,
          it.removed ? h("div", { className: "gs-import-note gs-import-removed" }, h(Icon, { name: "block", size: 14, strokeWidth: 2 }), it.removed) : null,
          it.blocked ? h("div", { className: "gs-import-note gs-import-removed" }, h(Icon, { name: "shield", size: 14, strokeWidth: 2 }), it.blocked) : null),
        h("div", { className: "gs-share-size" }, disabled ? "" : fmt(it.size || 0)),
        h("span"));
    });
    var foot = stage === "review" ? [
      h("div", { key: "s", className: "gs-share-summary" }, h("span", null, h("b", null, chosen.length + " games"), " · " + versions + " versions, added as pinned versions"), h("span", null, fmt(bytes))),
      h("div", { key: "n", className: "gs-note" }, h(Icon, { name: "shield", size: 16 }), "Every file is checked like a restore: only into the game's own folders, scanned by your antivirus, no program files. Nothing replaces your current saves."),
      h("div", { key: "a", className: "gs-dialog-actions" },
        h(Button, { variant: "ghost", onClick: props.onClose }, "Cancel"),
        h(Button, { variant: "primary", icon: "download", disabled: !versions, onClick: function () { st[1]("done"); if (props.onImport) props.onImport(Object.keys(picked)); } }, "Add " + versions + " versions"))
    ] : [
      h("div", { key: "d", className: "gs-done" }, h(Icon, { name: "check", size: 20, className: "gs-done-icon" }),
        h("div", null, h("div", null, "Added " + versions + " versions."), h("div", { className: "gs-muted", style: { fontSize: 12 } }, "Nothing was restored. Open a game's history and choose Restore to use one."))),
      h("div", { key: "a", className: "gs-dialog-actions" }, h("span"), h(Button, { variant: "primary", onClick: props.onClose }, "Done"))
    ];
    return h("div", { className: "gs-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "gs-import-title" },
      h("div", { className: "gs-dialog-head" },
        h("div", { className: "gs-card-head" },
          h("div", null, h("h2", { id: "gs-import-title", className: "gs-dialog-title" }, "Import saves"),
            h("p", { className: "gs-dialog-sub" }, "From ", h("span", { className: "gs-mono" }, props.zipName || "GameSync-saves.zip"), props.from ? " · packed on " + props.from : "")),
          h(IconButton, { icon: "x", label: "Close", onClick: props.onClose }))),
      h("div", { className: "gs-dialog-body" }, rows),
      h("div", { className: "gs-dialog-foot" }, foot));
  }

  /* ---------- AddPlaceDialog: a place a game keeps saves, set by hand (FOLD-01) ---------- */
  // place: { kind: "folder" | "file", path, portable, portableNote, files, bytes, newest, programs, state: "ok" | "warn" | "refused", message }
  function AddPlaceDialog(props) {
    var place = props.place || null;
    var cat = useState(props.category || "save"), category = cat[0];
    var refused = !!place && place.state === "refused";
    var CATS = [
      { id: "save", label: "Game saves: synced between PCs" },
      { id: "config", label: "Settings: each PC keeps its own" },
      { id: "screenshots", label: "Screenshots: backed up on this PC" }];
    function pick(kind) { if (props.onPick) props.onPick(kind); }
    var body = !place
      ? h("div", { className: "gs-place" },
          h("p", { className: "gs-muted", style: { margin: 0 } }, "Pick the folder where " + props.game + " keeps its saves, or one save file. You see what's there before anything is added."),
          h("div", { className: "gs-row" },
            h(Button, { variant: "secondary", icon: "folder", onClick: function () { pick("folder"); } }, "Choose a folder…"),
            h(Button, { variant: "ghost", icon: "file", onClick: function () { pick("file"); } }, "Choose a file…")))
      : h("div", { className: "gs-place" },
          h(FolderField, { path: place.path, icon: place.kind === "file" ? "file" : "folder", state: refused ? "refused" : "ok", message: refused ? place.message : null,
            meta: refused ? null : place.kind === "file" ? fmt(place.bytes || 0) + " · saved " + place.newest : place.files + (place.files === 1 ? " file · " : " files · ") + fmt(place.bytes || 0) + " · newest " + place.newest,
            changeLabel: "Change…", onChange: function () { pick(place.kind); }, onOpen: props.onOpen }),
          refused ? null : h(Facts, { items: [
            { label: "Every PC reads it as", value: place.portable, mono: true, title: place.portableNote || place.portable },
            { label: "What it holds", value: h(Select, { value: category, options: CATS, label: "What it holds", onChange: cat[1] }) }] }),
          !refused && place.portableNote ? h("div", { className: "gs-note" }, h(Icon, { name: "info", size: 16 }), place.portableNote) : null,
          !refused && place.programs ? h("div", { className: "gs-note" }, h(Icon, { name: "shield", size: 16 }),
            (place.programs === 1 ? "1 program file is" : place.programs + " program files are") + " there too, and never taken: only save data moves.") : null,
          !refused && place.state === "warn" ? h("div", { className: "gs-import-note gs-import-warn" }, h(Icon, { name: "alert", size: 14, strokeWidth: 2 }), place.message) : null);
    return h("div", { className: "gs-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "gs-place-title" },
      h("div", { className: "gs-dialog-head" },
        h("div", { className: "gs-card-head" },
          h("div", null, h("h2", { id: "gs-place-title", className: "gs-dialog-title" }, "Add a place"),
            h("p", { className: "gs-dialog-sub" }, "Where " + props.game + " keeps saves GameSync didn't find")),
          h(IconButton, { icon: "x", label: "Close Add a place", onClick: props.onClose }))),
      h("div", { className: "gs-dialog-body gs-dialog-roomy" }, body),
      h("div", { className: "gs-dialog-foot" },
        h("div", { className: "gs-note" }, h(Icon, { name: "info", size: 16 }), "It's backed up at the next sync. Your other PCs are asked before they take a new place, and nothing in it is ever deleted."),
        h("div", { className: "gs-dialog-actions" },
          h(Button, { variant: "ghost", onClick: props.onClose }, "Cancel"),
          h(Button, { variant: "primary", icon: "plus", disabled: !place || refused, onClick: function () { if (props.onAdd) props.onAdd({ path: place.path, category: category }); } }, "Add this place"))));
  }

  /* ---------- NamedSaveDialog: this save kept under a name (BAK-18, KAN-77) ---------- */
  // save: { path, files, bytes, newest, more }: the save as it is now, where it is and what's there
  function NamedSaveDialog(props) {
    var nm = useState(props.name || ""), name = nm[0];
    var wanted = name.trim();
    var taken = !!wanted && (props.names || []).some(function (n) { return n.toLowerCase() === wanted.toLowerCase(); });
    var keeping = props.stage === "keeping";
    var ready = wanted.length > 0 && !taken && !keeping;
    var save = props.save || null;
    function keep() { if (ready && props.onKeep) props.onKeep(wanted); }
    return h("div", { className: "gs-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "gs-named-title" },
      h("div", { className: "gs-dialog-head" },
        h("div", { className: "gs-card-head" },
          h("div", null, h("h2", { id: "gs-named-title", className: "gs-dialog-title" }, "New named save"),
            h("p", { className: "gs-dialog-sub" }, props.game + "'s save as it is now, kept under a name")),
          h(IconButton, { icon: "x", label: "Close New named save", onClick: props.onClose }))),
      h("div", { className: "gs-dialog-body gs-dialog-roomy" },
        h("div", { className: "gs-place" },
          save ? h(FolderField, { path: save.path, icon: "saves", fixed: true, onOpen: props.onOpen,
            meta: save.files + (save.files === 1 ? " file · " : " files · ") + fmt(save.bytes || 0) + " · newest " + save.newest
              + (save.more ? " · and " + (save.more === 1 ? "1 more place" : save.more + " more places") : "") }) : null,
          h("div", { className: "gs-own-field" },
            h("label", { className: "gs-own-label", htmlFor: "gs-named-name" }, "Name"),
            h("p", { className: "gs-own-help" }, "Name it for the moment it's from, so you'll know it later: before a boss, or a big choice."),
            h("input", { id: "gs-named-name", className: "gs-input gs-input-wide", value: name, placeholder: "Before Lady Maria", maxLength: 100, autoFocus: true, disabled: keeping,
              onChange: function (e) { nm[1](e.target.value); },
              onKeyDown: function (e) { if (e.key === "Enter") keep(); } }),
            taken ? h("div", { className: "gs-import-note gs-import-warn", role: "alert" }, h(Icon, { name: "alert", size: 14, strokeWidth: 2 }),
              props.game + " has a named save called “" + wanted + "” already. Give this one another name, so you can tell them apart.") : null),
          props.keepLine ? h("div", { className: "gs-note" }, h(Icon, { name: "shield", size: 16 }), props.keepLine) : null,
          props.error ? h("div", { className: "gs-import-note gs-import-removed", role: "alert" }, h(Icon, { name: "alert", size: 14, strokeWidth: 2 }), props.error) : null)),
      h("div", { className: "gs-dialog-foot" },
        h("div", { className: "gs-note" }, h(Icon, { name: "info", size: 16 }), "It's kept on every PC and never thinned. Restoring it later keeps your files now as a version first."),
        h("div", { className: "gs-dialog-actions" },
          h(Button, { variant: "ghost", onClick: props.onClose }, "Cancel"),
          h(Button, { variant: "primary", icon: "pin", disabled: !ready, onClick: keep,
            title: ready || keeping ? null : taken ? "Give it another name" : "Give it a name first" }, keeping ? "Keeping…" : "Keep this save"))));
  }

  /* ---------- AddGameDialog: a game or folder of the person's own (LIB-13) ---------- */
  // folder: { path, files, bytes, newest, programs, state: "ok" | "warn" | "refused", message }; program: { path }
  function AddGameDialog(props) {
    var nm = useState(props.name || ""), name = nm[0];
    var folder = props.folder || null, program = props.program || null;
    var refused = !!folder && folder.state === "refused";
    var ready = name.trim().length > 0 && !!folder && !refused;
    function field(label, id, help, body) {
      return h("div", { className: "gs-own-field" },
        id ? h("label", { className: "gs-own-label", htmlFor: id }, label) : h("div", { className: "gs-own-label" }, label),
        help ? h("p", { className: "gs-own-help" }, help) : null,
        body);
    }
    // Program files are never copied (R1): say so, with how many the folder holds once it's picked.
    var programs = folder && !refused && folder.programs
      ? (folder.programs === 1 ? "It holds 1 program file" : "It holds " + folder.programs + " program files") + " (.exe, .jar and the like), and they're never copied: pick the save or world folder rather than the whole game or server."
      : "Programs in the folder (.exe, .dll, .jar and the like) are never copied, so pick the save or world folder rather than the whole game or server.";
    return h("div", { className: "gs-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "gs-own-title" },
      h("div", { className: "gs-dialog-head" },
        h("div", { className: "gs-card-head" },
          h("div", null, h("h2", { id: "gs-own-title", className: "gs-dialog-title" }, "Add a game or folder"),
            h("p", { className: "gs-dialog-sub" }, "For a game GameSync didn't find, or any folder you want kept in step between your PCs, like a game server's world.")),
          h(IconButton, { icon: "x", label: "Close Add a game or folder", onClick: props.onClose }))),
      h("div", { className: "gs-dialog-body gs-dialog-roomy" },
        h("div", { className: "gs-place" },
          field("Name", "gs-own-name", null,
            h("input", { id: "gs-own-name", className: "gs-input gs-input-wide", value: name, placeholder: "Minecraft server world", maxLength: 80,
              onChange: function (e) { nm[1](e.target.value); if (props.onName) props.onName(e.target.value); } })),
          field("Folder", null, null, folder
            ? h(FolderField, { path: folder.path, state: refused ? "refused" : "ok", message: refused ? folder.message : null,
                meta: refused ? null : folder.files + (folder.files === 1 ? " file · " : " files · ") + fmt(folder.bytes || 0) + (folder.newest ? " · last changed " + folder.newest : ""),
                changeLabel: "Change…", onChange: props.onPickFolder, onOpen: props.onOpen })
            : h("div", { className: "gs-row" }, h(Button, { variant: "secondary", icon: "folder", onClick: props.onPickFolder }, "Choose a folder…"))),
          field("Program that uses it", null,
            program ? "GameSync syncs it when this program closes, and Play starts it." : "Optional. With one, GameSync syncs when it closes; without one, once the folder has been quiet for 5 minutes.",
            program
              ? h("div", { className: "gs-well-line" }, h(Icon, { name: "play", size: 14 }), h("span", { className: "gs-own-path", title: program.path }, program.path),
                  h(Button, { size: "sm", variant: "ghost", onClick: props.onRemoveProgram }, "Remove"))
              : h("div", { className: "gs-row" }, h(Button, { variant: "ghost", icon: "plus", onClick: props.onPickProgram }, "Choose a program…"))),
          refused ? null : h("div", { className: "gs-note" }, h(Icon, { name: "shield", size: 16 }), programs),
          !refused && folder && folder.state === "warn" ? h("div", { className: "gs-import-note gs-import-warn" }, h(Icon, { name: "alert", size: 14, strokeWidth: 2 }), folder.message) : null)),
      h("div", { className: "gs-dialog-foot" },
        h("div", { className: "gs-note" }, h(Icon, { name: "info", size: 16 }),
          props.setup ? "It joins Choose games, ticked. Nothing syncs until you start using GameSync."
            : "It's backed up now and synced from then on. On your other PC, add its folder under the same name."),
        h("div", { className: "gs-dialog-actions" },
          h(Button, { variant: "ghost", onClick: props.onClose }, "Cancel"),
          h(Button, { variant: "primary", icon: "plus", disabled: !ready,
            title: ready ? null : !folder ? "Choose its folder first" : refused ? "Choose another folder" : "Give it a name first",
            onClick: function () { if (ready && props.onAdd) props.onAdd({ name: name.trim() }); } }, props.setup ? "Add" : "Add and sync"))));
  }

  /* ---------- ImportKeptSavesDialog: save folders kept by hand, as named saves (BAK-19) ---------- */
  // items: { id, name, saved, files, bytes, same, kept }; skipped: strings
  function ImportKeptSavesDialog(props) {
    var items = props.items || [];
    var st = useState(props.stage || (props.folder ? "review" : "pick")), stage = st[0];
    // A copy the same as another here (or as a named save) isn't named again; one already in the history only gets its name.
    var named = items.filter(function (it) { return !it.same; });
    var fresh = named.filter(function (it) { return !it.kept; });
    var bytes = fresh.reduce(function (n, it) { return n + (it.bytes || 0); }, 0);
    function pick() { if (props.onPick) props.onPick(); }
    var body;
    if (stage === "pick") {
      body = h("div", { className: "gs-place" },
        h("p", { className: "gs-muted", style: { margin: 0 } }, "Pick the folder that holds the copies you kept of " + props.game + "'s saves: each copy in a folder named after the moment, beside the live save or anywhere else."),
        h("div", { className: "gs-row" }, h(Button, { variant: "secondary", icon: "folder", onClick: pick }, "Choose the folder…")));
    } else {
      body = h("div", { className: "gs-place" },
        h(FolderField, { path: props.folder, meta: items.length + (items.length === 1 ? " kept copy found" : " kept copies found"), changeLabel: "Change…", onChange: pick, onOpen: props.onOpen }),
        props.roots && props.roots.length > 1 ? h(Facts, { items: [{ label: "They're copies of", value: h(Select, { value: props.root || props.roots[0].id, options: props.roots, label: "They're copies of", onChange: props.onRoot }) }] }) : null,
        h("div", { className: "gs-kept-list", role: "list" }, items.map(function (it) {
          return h("div", { key: it.id, className: "gs-kept-row", role: "listitem" },
            h(Icon, { name: "pin", size: 16 }),
            h("div", { style: { minWidth: 0 } },
              h("div", { className: "gs-kept-name" }, it.name),
              h("div", { className: "gs-kept-meta" }, it.saved + " · " + it.files + (it.files === 1 ? " file" : " files"))),
            it.same ? h("span", { className: "gs-tag", title: "Its files are the same as " + it.same + "'s, so it isn't named again" }, "Same as " + it.same)
              : it.kept ? h("span", { className: "gs-tag", title: "Its files are already in the history; that version takes this name" }, "Already kept")
              : h("span", { className: "gs-kept-size" }, fmt(it.bytes || 0)));
        })),
        (props.skipped || []).map(function (s, i) { return h("div", { key: "s" + i, className: "gs-import-note gs-import-warn" }, h(Icon, { name: "alert", size: 14, strokeWidth: 2 }), s); }));
    }
    var foot = stage === "done" ? [
      h("div", { key: "d", className: "gs-done" }, h(Icon, { name: "check", size: 20, className: "gs-done-icon" }),
        h("div", null, h("div", null, "Imported " + named.length + (named.length === 1 ? " named save." : " named saves.")),
          h("div", { className: "gs-muted", style: { fontSize: 12 } }, "They're under Named saves, on every PC. Restore brings one back, keeping your files now first."))),
      h("div", { key: "a", className: "gs-dialog-actions" }, h("span"), h(Button, { variant: "primary", onClick: props.onClose }, "Done"))
    ] : [
      stage === "review" ? h("div", { key: "s", className: "gs-share-summary" },
        h("span", null, h("b", null, named.length + (named.length === 1 ? " named save" : " named saves")), named.length < items.length ? " · " + (items.length - named.length) + " the same as another, not kept twice" : ""),
        h("span", null, fmt(bytes))) : null,
      h("div", { key: "n", className: "gs-note" }, h(Icon, { name: "shield", size: 16 }), "Your folders are never changed. Each copy becomes a named save on every PC, kept aside: never current by itself."),
      h("div", { key: "a", className: "gs-dialog-actions" },
        h(Button, { variant: "ghost", onClick: props.onClose }, "Cancel"),
        h(Button, { variant: "primary", icon: "download", disabled: stage !== "review" || !named.length, onClick: function () { st[1]("done"); if (props.onImport) props.onImport(); } },
          "Import " + (named.length || "") + (named.length === 1 ? " named save" : " named saves")))
    ];
    return h("div", { className: "gs-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "gs-kept-title" },
      h("div", { className: "gs-dialog-head" },
        h("div", { className: "gs-card-head" },
          h("div", null, h("h2", { id: "gs-kept-title", className: "gs-dialog-title" }, "Import kept saves"),
            h("p", { className: "gs-dialog-sub" }, props.game + ": copies you kept by hand, as named saves")),
          h(IconButton, { icon: "x", label: "Close Import kept saves", onClick: props.onClose }))),
      h("div", { className: "gs-dialog-body gs-dialog-roomy" }, body),
      h("div", { className: "gs-dialog-foot" }, foot));
  }

  /* ---------- KeptCopiesDialog (KAN-61) ---------- */
  // Sync these saves on a game whose save folder holds copies kept by hand beside its live save (Bloodborne's
  // "Before Orphan\SPRJ0005" beside "SPRJ0005"): only the live save syncs, and each copy becomes a named save, in one
  // step. "backup" mode is Back up now, New named save… or Import kept saves… on a game not syncing yet (KAN-63).
  function KeptCopiesDialog(props) {
    var items = props.items || [];
    var live = props.live || {};
    var st = useState(props.stage || "review"), stage = st[0];
    var br = useState(props.bring !== false), bring = br[0];
    var named = items.filter(function (it) { return !it.same; });
    var bytes = named.reduce(function (n, it) { return n + (it.bytes || 0); }, 0);
    var backup = props.mode === "backup";
    var verb = backup ? "Back up" : "Sync";
    var count = named.length + (named.length === 1 ? " named save" : " named saves");
    var look = props.look || {}, job = props.progress || {};
    var liveName = String(live.path || "").replace(/[\\/]+$/, "").split(/[\\/]/).pop() || "the live save";
    function row(it) {
      return h("div", { key: it.id, className: "gs-kept-row", role: "listitem" },
        h(Icon, { name: "copy", size: 16 }),
        h("div", { style: { minWidth: 0 } },
          h("div", { className: "gs-kept-name" }, it.name),
          h("div", { className: "gs-kept-meta" }, it.saved + " · " + it.files + (it.files === 1 ? " file" : " files"))),
        it.same ? h("span", { className: "gs-tag", title: "Its files are the same as " + it.same + "'s, so it isn't named again" }, "Same as " + it.same)
          : h("span", { className: "gs-kept-size" }, fmt(it.bytes || 0)));
    }
    // Looking: each copy's files are read to find the ones the same as another, a folder at a time (KAN-80).
    var body = stage === "looking"
      ? h(JobProgress, { title: "Looking at the copies beside the live save", value: look.total ? look.done * 100 / look.total : null,
          detail: look.total ? look.done + " of " + look.total + " folders · " + fmt(look.readBytes || 0) + " of " + fmt(look.totalBytes || 0) + " read" : null,
          speed: look.speed, left: look.left })
      : stage === "done"
      ? h("div", { className: "gs-stack" },
          h("div", { className: "gs-done" }, h(Icon, { name: "check", size: 20, className: "gs-done-icon" }),
            h("div", null,
              h("div", null, backup ? props.game + "'s live save is backed up." : props.game + " syncs its live save now."),
              h("div", { className: "gs-muted", style: { fontSize: 12 } }, bring
                ? count + " are under Named saves, on every PC. Restore brings one back, keeping the save there now first."
                : "The copies stay in their folders, not backed up. Import kept saves… brings them in whenever you like."))),
          h("div", { className: "gs-note" }, h(Icon, { name: "upload", size: 16 }),
            "Uploading to Google Drive now, beside whatever you do next: the game's saves show how far it is."))
      : h("div", { className: "gs-place" },
          h("div", { className: "gs-note" }, h(Icon, { name: "info", size: 16 }), backup
            ? "Back up keeps every version, on this PC and in your Google Drive; it isn't synced between your PCs."
            : "Sync keeps every version, on this PC and in your Google Drive, and brings the newest to your other PCs."),
          h("div", { className: "gs-kept-head" }, backup ? "Backed up, not synced between your PCs" : "Syncs between your PCs"),
          h("div", { className: "gs-kept-row gs-kept-live" },
            h(Icon, { name: "saves", size: 16 }),
            h("div", { style: { minWidth: 0 } },
              h("div", { className: "gs-kept-name gs-kept-path", title: live.path }, live.path),
              h("div", { className: "gs-kept-meta" }, "Your live save · " + live.saved + " · " + live.files + (live.files === 1 ? " file" : " files"))),
            h("span", { className: "gs-kept-size" }, fmt(live.bytes || 0))),
          h(Checkbox, { checked: bring, onChange: br[1], disabled: stage === "keeping", showLabel: true, label: "Bring the " + items.length + " copies beside it in as named saves" }),
          bring ? h("div", { className: "gs-kept-list is-long", role: "list", "aria-label": "Copies kept by hand" }, items.map(row))
            : h("div", { className: "gs-note" }, h(Icon, { name: "info", size: 16 }), "They stay in their folders, not backed up. Import kept saves… brings them in whenever you like."),
          (props.skipped || []).map(function (s, i) { return h("div", { key: "s" + i, className: "gs-import-note gs-import-warn" }, h(Icon, { name: "alert", size: 14, strokeWidth: 2 }), s); }));
    var keeping = stage === "keeping";
    var foot = stage === "done"
      ? [h("div", { key: "a", className: "gs-dialog-actions" }, h("span"), h(Button, { variant: "primary", onClick: props.onClose }, "Done"))]
      : stage === "looking"
      ? [h("div", { key: "a", className: "gs-dialog-actions" }, h("span"), h(Button, { variant: "ghost", onClick: props.onClose }, "Cancel"))]
      : [
        h("div", { key: "s", className: "gs-share-summary" },
          h("span", null, h("b", null, bring ? "The live save and " + count : "The live save alone"), bring && named.length < items.length ? " · " + (items.length - named.length) + " the same as another, not kept twice" : ""),
          h("span", null, fmt((live.bytes || 0) + (bring ? bytes : 0)))),
        // Keeping: the copies coming in, how far and how fast; closing the dialog doesn't stop it (KAN-80).
        keeping ? h(JobProgress, { key: "p", title: bring && named.length ? "Bringing the copies in" : (backup ? "Backing up the live save" : "Keeping the live save"),
            value: job.value, detail: job.detail, speed: job.speed, left: job.left })
          : h("div", { key: "n", className: "gs-note" }, h(Icon, { name: "shield", size: 16 }),
            "Your folders stay as they are. Copies you make there later aren't synced: New named save… keeps one in a step."),
        h("div", { key: "a", className: "gs-dialog-actions" },
          h(Button, { variant: "ghost", disabled: keeping, title: "Every version would carry all " + (items.length + 1) + " folders, " + fmt(props.wholeBytes || 0) + ": the live save and every copy, as one save", onClick: props.onWhole },
            verb + " the whole folder as one save"),
          h("span", { className: "gs-row", style: { flexWrap: "nowrap" } },
            h(Button, { variant: "ghost", onClick: props.onClose, title: keeping ? "It carries on: the game's saves show how far it is" : null }, keeping ? "Close" : "Cancel"),
            h(Button, { variant: "primary", icon: backup ? "upload" : "sync", busy: keeping, busyLabel: backup ? "Backing up" : "Syncing",
                title: (backup ? "Backs up the live save, " + liveName + ", on this PC and in your Google Drive" : "Syncs the live save, " + liveName + ", between your PCs") +
                  (bring && named.length ? ", and keeps each copy as a named save you can restore" : ""),
                onClick: function () { st[1]("keeping"); if (props.onSync) props.onSync(bring); } },
              bring && named.length ? verb + " " + liveName + " + " + count : verb + " " + liveName)))
      ];
    return h("div", { className: "gs-dialog gs-dialog-wide", role: "dialog", "aria-modal": "true", "aria-labelledby": "gs-copies-title" },
      h("div", { className: "gs-dialog-head" },
        h("div", { className: "gs-card-head" },
          h("div", null, h("h2", { id: "gs-copies-title", className: "gs-dialog-title" }, (backup ? "Back up " : "Sync ") + props.game + "'s saves"),
            h("p", { className: "gs-dialog-sub" }, "Its save folder holds your live save and " + items.length + " copies of it you kept by hand.")),
          h(IconButton, { icon: "x", label: "Close", onClick: props.onClose }))),
      h("div", { className: "gs-dialog-body gs-dialog-roomy" }, body),
      h("div", { className: "gs-dialog-foot" }, foot));
  }

  /* ---------- Settings: nav, rows, folders ---------- */
  function SettingsNav(props) {
    return h("nav", { className: "gs-setnav", "aria-label": props.label || "Settings sections" },
      (props.items || []).map(function (it) {
        var on = it.id === props.current;
        return h("button", { key: it.id, type: "button", className: "gs-setnav-item", "aria-current": on ? "page" : undefined, onClick: function () { if (props.onChange) props.onChange(it.id); } },
          h(Icon, { name: it.icon, size: 18 }), h("span", null, it.label),
          it.badge ? h("span", { className: "gs-setnav-badge" }, it.badge) : null);
      }));
  }

  function SettingsRow(props) {
    return h("div", { className: cx("gs-setrow", props.disabled && "is-disabled", props.stack && "is-stack") },
      h("div", { className: "gs-setrow-text" },
        h("div", { className: "gs-setrow-title" }, props.title, props.tag ? h("span", { className: "gs-tag" }, props.tag) : null),
        props.description ? h("div", { className: "gs-setrow-desc" }, props.description) : null),
      props.children ? h("div", { className: "gs-setrow-control" }, props.children) : null);
  }

  function FolderField(props) {
    var state = props.state || "ok";
    return h("div", { className: cx("gs-folder", "is-" + state) },
      h("div", { className: "gs-folder-main" },
        h("span", { className: "gs-folder-icon", "aria-hidden": "true" }, h(Icon, { name: state === "missing" ? "unplug" : state === "refused" ? "block" : (props.icon || "folder"), size: 18 })),
        h("div", { className: "gs-folder-text" },
          props.label ? h("div", { className: "gs-folder-label" }, props.label) : null,
          h("div", { className: "gs-folder-path", title: props.path }, props.path),
          props.meta ? h("div", { className: "gs-folder-meta" }, props.meta) : null),
        h("div", { className: "gs-folder-actions" },
          props.fixed ? null : h(Button, { size: "sm", variant: "secondary", onClick: props.onChange, disabled: state === "moving" }, props.changeLabel || "Change…"),
          h(IconButton, { icon: "folder", label: "Open in Explorer", size: "sm", onClick: props.onOpen }))),
      state === "moving" ? h(ProgressBar, { value: props.progress || 0, label: props.message || "Moving backups…", right: Math.round(props.progress || 0) + "%" }) : null,
      (state === "missing" || state === "refused") && props.message ? h("div", { className: "gs-folder-msg", role: state === "refused" ? "alert" : undefined }, h(Icon, { name: state === "missing" ? "alert" : "block", size: 14, strokeWidth: 2 }), props.message) : null);
  }

  function FolderList(props) {
    var items = props.items || [];
    return h("div", { className: "gs-flist" },
      items.length ? items.map(function (it, i) {
        return h("div", { key: it.path + i, className: "gs-flist-row" },
          h(Icon, { name: it.icon || "folder", size: 16, className: "gs-faint" }),
          h("div", { className: "gs-flist-text" }, h("div", { className: "gs-flist-path", title: it.path }, it.path),
            it.meta ? h("div", { className: "gs-folder-meta" }, it.meta) : null),
          it.tag ? h("span", { className: "gs-tag" }, it.tag) : null,
          props.onRemove ? h(Button, { size: "sm", variant: "ghost", onClick: function () { props.onRemove(it, i); }, "aria-label": "Remove " + it.path }, "Remove") : null);
      }) : h("div", { className: "gs-flist-empty" }, props.empty || "None yet"),
      props.onAdd ? h("div", null, h(Button, { size: "sm", variant: "secondary", icon: "plus", onClick: props.onAdd }, props.addLabel || "Add folder")) : null);
  }

  /* ---------- Theming: scope, backdrop, picker, swatches ---------- */
  // Every colour token for what's inside: the theme given, or the page's. In Glossy, with art, the strength's
  // see-through surfaces over a Backdrop of that art; light mode, pure black and no art stay Solid.
  function ThemeScope(props) {
    var pageTheme = useDocTheme();
    var storedSurface = useSurface();
    var theme = props.theme || pageTheme;
    var surface = props.surface || storedSurface;
    var vars = Theme.build(theme);
    var glass = surface === "glossy" && props.art ? Theme.glass(theme, props.strength || "glass") : null;
    if (glass) Object.assign(vars, glass.tokens);
    var style = Object.assign(varsStyle(vars), glass ? { background: glass.backdrop.base } : null, props.style);
    return h("div", { className: cx("gs-scope", glass && "gs-glossy", props.className), style: style, "data-gs-theme": theme.preset || "arcade", "data-gs-surface": glass ? "glossy" : "solid" },
      glass ? h(Backdrop, { art: props.art, backdrop: glass.backdrop }) : null,
      props.children);
  }

  // The art blurred behind a Glossy page, with its strength's scrim from top to bottom. The app darkens bright art
  // further, row by row, until text keeps 4.5:1; these previews use art dark enough not to need it.
  function Backdrop(props) {
    var b = props.backdrop;
    var art = /^(url|linear-gradient|radial-gradient|repeating-)/.test(props.art) ? props.art : "url(" + props.art + ")";
    var scrim = "linear-gradient(180deg, " + b.stops.map(function (s) { return Theme.rgba(b.scrim, s[1]) + " " + Math.round(s[0] * 100) + "%"; }).join(", ") + ")";
    return h("div", { className: "gs-backdrop", "aria-hidden": "true" },
      h("div", { className: "gs-backdrop-art", style: { backgroundImage: art } }),
      h("div", { className: "gs-backdrop-scrim", style: { background: scrim } }));
  }

  function MiniApp(props) {
    var v = props.vars;
    return h("div", { className: "gs-mini", style: { background: v["bg-100"], borderColor: v["line-100"] }, "aria-hidden": "true" },
      h("div", { className: "gs-mini-rail", style: { borderColor: v["line-100"] } },
        h("i", { className: "gs-mini-logo", style: { borderColor: v.primary } }),
        h("i", { className: "gs-mini-nav", style: { background: v["secondary-soft"] } }, h("b", { style: { background: v.secondary } })),
        h("i", { className: "gs-mini-nav", style: { background: v["bg-300"] } }),
        h("i", { className: "gs-mini-nav", style: { background: v["bg-300"] } })),
      h("div", { className: "gs-mini-main" },
        h("div", { className: "gs-mini-hero", style: { background: v["bg-300"] } },
          h("i", { className: "gs-mini-title", style: { background: v.ink } }),
          h("i", { className: "gs-mini-btn", style: { background: v.primary } })),
        h("div", { className: "gs-mini-cards" },
          h("div", { className: "gs-mini-card", style: { background: v["bg-200"] } },
            h("i", { className: "gs-mini-line", style: { background: v["ink-muted"] } }),
            h("i", { className: "gs-mini-bar", style: { background: v["bg-400"] } }, h("b", { style: { background: v.primary } }))),
          h("div", { className: "gs-mini-card", style: { background: v["bg-200"] } },
            h("i", { className: "gs-mini-chip", style: { background: v.secondary } }),
            h("i", { className: "gs-mini-line short", style: { background: v["ink-faint"] } })))));
  }

  function ThemePicker(props) {
    var mode = props.mode || docTheme().mode;
    var presets = props.presets || Theme.presets;
    return h("div", { className: "gs-tpick", role: "radiogroup", "aria-label": props.label || "Theme" },
      presets.map(function (p) {
        var on = p.id === props.value;
        var vars = Theme.build({ preset: p.id, mode: mode, pureBlack: props.pureBlack, accent: props.accent });
        return h("button", { key: p.id, type: "button", role: "radio", "aria-checked": String(on), "aria-label": p.name + (p.dynamic ? ", follows your Windows accent colour" : ""), className: cx("gs-tcard", on && "is-on"), onClick: function () { if (props.onChange) props.onChange(p.id); } },
          h(MiniApp, { vars: vars }),
          h("span", { className: "gs-tcard-name" }, on ? h("span", { className: "gs-tcard-check" }, h(Icon, { name: "check", size: 11, strokeWidth: 3 })) : null, p.name));
      }));
  }

  var NEAR = { warn: "Needs-you amber", danger: "Blocked red", play: "Playing violet" };
  function ColorSwatchPicker(props) {
    var role = props.role === "secondary" ? "secondary" : "primary";
    var base = props.theme || docTheme();
    var list = props.swatches || Theme.swatches;
    var open = useState(!!props.customOpen);
    var hx = useState(props.customValue || (props.value && !Theme.swatch(props.value) ? props.value : ""));
    var hex = hx[0];
    function withRole(val) { var o = Object.assign({}, base); o[role] = val; return Theme.build(o); }
    var isCustom = !!props.value && !Theme.swatch(props.value);
    var result = hex ? Theme.check(hex, role, base) : null;
    var note = !result ? "Type a hex code, like #7FE6F2." : !result.valid ? result.message
      : (result.adjusted ? "Adjusted to " + result.used.toUpperCase() + " so text and icons stay readable (" + result.ratio + ":1 on cards)." : "Reads well: " + result.ratio + ":1 on cards.")
        + (result.near ? " It's close to the " + NEAR[result.near] + "; statuses keep their icon and word, so they still read." : "");
    return h("div", { className: "gs-swatches" },
      h("div", { className: "gs-swatch-row", role: "radiogroup", "aria-label": props.label || (role === "primary" ? "Primary colour" : "Secondary colour") },
        list.map(function (s) {
          var on = props.value === s.id;
          var v = withRole(s.id);
          return h("button", { key: s.id, type: "button", role: "radio", "aria-checked": String(on), "aria-label": s.name, title: s.name, className: cx("gs-swatch", on && "is-on"), style: { background: v[role], color: v["on-" + role] }, onClick: function () { if (props.onChange) props.onChange(s.id); } },
            on ? h(Icon, { name: "check", size: 14, strokeWidth: 2.5 }) : null);
        }),
        props.allowCustom ? h("button", { type: "button", className: cx("gs-swatch", "gs-swatch-add", isCustom && "is-on"), "aria-label": "Custom colour", "aria-expanded": String(open[0]), title: "Custom colour", style: isCustom ? { background: withRole(props.value)[role], color: withRole(props.value)["on-" + role], borderStyle: "solid" } : null, onClick: function () { open[1](!open[0]); } },
          h(Icon, { name: isCustom ? "pencil" : "plus", size: 14, strokeWidth: 2 })) : null),
      props.allowCustom && open[0] ? h("div", { className: "gs-custom" },
        h("div", { className: "gs-custom-field" },
          h("span", { className: "gs-custom-dot", style: { background: result && result.valid ? result.used : "transparent" } }),
          h("input", { className: "gs-input", value: hex, placeholder: "#7FE6F2", maxLength: 7, spellCheck: false, "aria-label": (role === "primary" ? "Primary" : "Secondary") + " colour, hex code",
            onChange: function (e) { var val = e.target.value; hx[1](val); var r = Theme.check(val, role, base); if (r.valid && props.onChange) props.onChange(r.input); } })),
        h("div", { className: "gs-custom-note", "aria-live": "polite" }, note)) : null);
  }

  /* ---------- Achievements (design system version 33; the owner, 3 Oct 2026) ---------- */
  // A tier from how many of Steam's players have an achievement: Gold under 5%, Silver under 20%, Bronze the rest; none
  // while its rarity isn't known. A Zenith is a game's 100% (Platinum until 3 Oct 2026), drawn in platinum. The rarity's word follows Steam's bands of players.
  function tierOf(pct) { return pct == null ? null : pct < 5 ? "gold" : pct < 20 ? "silver" : "bronze"; }
  var TIER_WORD = { bronze: "Bronze", silver: "Silver", gold: "Gold", zenith: "Zenith" };
  function rarityWord(pct) { return pct == null ? null : pct < 1 ? "Ultra rare" : pct < 5 ? "Very rare" : pct < 20 ? "Rare" : pct < 50 ? "Uncommon" : "Common"; }
  function pctText(pct) { return (pct < 10 ? (Math.round(pct * 10) / 10).toFixed(1) : String(Math.round(pct))) + "%"; }
  var ringIds = 0;

  // A ring of progress: `value` 0 to 100, `label` in its middle (the percent by default) and `sub` under it. `tone`
  // "zenith" when it's a game's 100%: the arc in platinum, with a glow. A ring under 96px puts `sub` under itself,
  // outside the ring, where it has room (version 35; the owner: Home's "123 / 171" was spilling into the ring).
  function ProgressRing(props) {
    var size = props.size || 96, stroke = props.stroke || Math.max(4, Math.round(size / 11));
    var r = (size - stroke) / 2, c = 2 * Math.PI * r, v = Math.max(0, Math.min(100, props.value || 0));
    var plat = props.tone === "zenith";
    var id = useState(function () { ringIds += 1; return "gs-ring-" + ringIds; })[0];
    var below = !!props.sub && (props.subBelow != null ? props.subBelow : size < 96);
    // A game's 100% since version 49: gold to red, glowing (ZenithRing).
    if (plat) return ZenithRing(props, size, stroke, below);
    var ring = h("div", { className: cx("gs-ring", plat && "gs-ring--zenith", !below && props.className), style: { width: size, height: size }, role: below ? undefined : "img",
        "aria-hidden": below ? "true" : undefined, "aria-label": below ? undefined : props.ariaLabel || (Math.round(v) + "%" + (props.sub ? ", " + props.sub : "")) },
      h("svg", { width: size, height: size, viewBox: "0 0 " + size + " " + size, "aria-hidden": "true" },
        h("defs", null, h("linearGradient", { id: id, x1: "0", y1: "0", x2: "1", y2: "1" },
          h("stop", { offset: "0%", style: { stopColor: plat ? "var(--platinum)" : "var(--primary)" } }),
          h("stop", { offset: "100%", style: { stopColor: plat ? "var(--platinum-deep)" : "var(--primary)" } }))),
        h("circle", { cx: size / 2, cy: size / 2, r: r, fill: "none", stroke: "var(--bg-400)", strokeWidth: stroke }),
        v > 0 ? h("circle", { className: "gs-ring-arc", cx: size / 2, cy: size / 2, r: r, fill: "none", stroke: "url(#" + id + ")", strokeWidth: stroke, strokeLinecap: "round",
          strokeDasharray: c, strokeDashoffset: c * (1 - v / 100), transform: "rotate(-90 " + size / 2 + " " + size / 2 + ")" }) : null),
      h("div", { className: "gs-ring-label" },
        h("span", { className: "gs-ring-value", style: { fontSize: Math.round(size * (props.sub && !below ? 0.22 : 0.26)) } }, props.label != null ? props.label : Math.round(v) + "%"),
        props.sub && !below ? h("span", { className: "gs-ring-sub", style: { fontSize: Math.max(10, Math.round(size * 0.1)) } }, props.sub) : null));
    if (!below) return ring;
    return h("div", { className: cx("gs-ring-wrap", props.className), role: "img", "aria-label": props.ariaLabel || (Math.round(v) + "%, " + props.sub) },
      ring, h("span", { className: "gs-ring-under" }, props.sub));
  }

  // An achievement's icon in its metal: `state` unlocked (the default), locked (Steam's own grey icon, dimmed, with a
  // lock) or hidden (locked and secret: no icon and no name until it's unlocked, a "?" instead). `tier` bronze, silver or
  // gold rings an unlocked one; Gold glows. `size` sm (40), md (64, the default) or lg (104).
  function AchievementBadge(props) {
    var size = props.size || "md", state = props.state || "unlocked", tier = state === "unlocked" ? props.tier : null;
    var px = size === "lg" ? 104 : size === "sm" ? 40 : 64;
    return h("span", { className: cx("gs-badge", "gs-badge--" + size, "gs-badge--" + state, tier && "gs-badge--" + tier), style: { width: px, height: px },
        role: "img", "aria-label": props.label || (state === "hidden" ? "Hidden achievement" : (props.name || "Achievement") + (state === "locked" ? ", locked" : "")) },
      h("span", { className: "gs-badge-face", style: state !== "hidden" && props.icon ? { backgroundImage: "url(\"" + props.icon + "\")" } : null },
        state === "hidden" ? h("span", { className: "gs-badge-q", "aria-hidden": "true" }, "?")
          : !props.icon ? h(Icon, { name: "trophy", size: Math.round(px * 0.38) }) : null),
      state === "locked" ? h("span", { className: "gs-badge-lock", "aria-hidden": "true" }, h(Icon, { name: "lock", size: px >= 64 ? 12 : 10, strokeWidth: 2.4 })) : null);
  }

  // A tier in words with its metal: "Gold", or a count of them ("12 Gold").
  function TierChip(props) {
    return h("span", { className: cx("gs-tier", "gs-tier--" + props.tier, props.className) },
      h("span", { className: "gs-tier-dot", "aria-hidden": "true" }),
      props.count != null ? h("span", { className: "gs-tier-count" }, props.count) : null,
      h("span", { className: "gs-tier-word" }, props.label || TIER_WORD[props.tier]));
  }

  // The pines and the grass of the Zenith medal, generated with seeded randomness (version 44).
  var ZENITH_ART = {"far":"M6 38L6.33 40L6.94 41L6.64 42.4L7.83 43.4L6.71 44.8L8.01 45.8L6.86 47.2L8.44 48.2L7.2 49.6L9.42 50.6L6.5 50L5.5 50L4.8 49.6L2.58 50.6L5.14 47.2L3.56 48.2L5.29 44.8L3.99 45.8L5.36 42.4L4.17 43.4L5.67 40L5.06 41ZM10 34L10.38 35.74L11.08 36.74L10.5 37.89L11.42 38.89L10.72 40.03L12.06 41.03L10.88 42.17L12.51 43.17L10.98 44.31L12.79 45.31L11.17 46.46L13.35 47.46L11.31 48.6L13.75 49.6L10.5 49L9.5 49L8.69 48.6L6.25 49.6L8.83 46.46L6.65 47.46L9.02 44.31L7.21 45.31L9.12 42.17L7.49 43.17L9.28 40.03L7.94 41.03L9.5 37.89L8.58 38.89L9.62 35.74L8.92 36.74ZM15 40L15.4 41.6L16.13 42.6L15.49 43.6L16.41 44.6L15.68 45.6L16.94 46.6L15.93 47.6L17.66 48.6L16.11 49.6L18.18 50.6L15.5 50L14.5 50L13.89 49.6L11.82 50.6L14.07 47.6L12.34 48.6L14.32 45.6L13.06 46.6L14.51 43.6L13.59 44.6L14.6 41.6L13.87 42.6ZM49 39L49.31 40.8L49.89 41.8L49.56 43L50.61 44L49.7 45.2L51 46.2L49.99 47.4L51.83 48.4L50.15 49.6L52.28 50.6L49.5 50L48.5 50L47.85 49.6L45.72 50.6L48.01 47.4L46.17 48.4L48.3 45.2L47 46.2L48.44 43L47.39 44L48.69 40.8L48.11 41.8ZM54 33L54.33 34.89L54.95 35.89L54.52 37.17L55.5 38.17L54.75 39.46L56.16 40.46L54.86 41.74L56.47 42.74L55.01 44.03L56.88 45.03L55.14 46.31L57.25 47.31L55.42 48.6L58.04 49.6L54.5 49L53.5 49L52.58 48.6L49.96 49.6L52.86 46.31L50.75 47.31L52.99 44.03L51.12 45.03L53.14 41.74L51.53 42.74L53.25 39.46L51.84 40.46L53.48 37.17L52.5 38.17L53.67 34.89L53.05 35.89ZM58 39L58.43 41L59.24 42L58.55 43.4L59.56 44.4L58.72 45.8L60.04 46.8L59.03 48.2L60.95 49.2L59.06 50.6L61.04 51.6L58.5 51L57.5 51L56.94 50.6L54.96 51.6L56.97 48.2L55.05 49.2L57.28 45.8L55.96 46.8L57.45 43.4L56.44 44.4L57.57 41L56.76 42Z","near":"M3 36L3.38 37.8L4.08 38.8L3.52 40L4.49 41L3.67 42.2L4.92 43.2L3.77 44.4L5.19 45.4L4.07 46.6L6.07 47.6L4.1 48.8L6.14 49.8L4.3 51L6.71 52L4.5 53.2L7.28 54.2L4.28 55.4L6.66 56.4L4.5 57.6L7.29 58.6L3.5 58L2.5 58L1.5 57.6L-1.29 58.6L1.72 55.4L-0.66 56.4L1.5 53.2L-1.28 54.2L1.7 51L-0.71 52L1.9 48.8L-0.14 49.8L1.93 46.6L-0.07 47.6L2.23 44.4L0.81 45.4L2.33 42.2L1.08 43.2L2.48 40L1.51 41L2.62 37.8L1.92 38.8ZM9 32L9.44 33.77L10.26 34.77L9.56 35.93L10.59 36.93L9.66 38.1L10.88 39.1L9.72 40.27L11.06 41.27L10.02 42.43L11.93 43.43L9.89 44.6L11.56 45.6L10.11 46.77L12.16 47.77L10.21 48.93L12.46 49.93L10.46 51.1L13.17 52.1L10.7 53.27L13.87 54.27L10.64 55.43L13.68 56.43L10.57 57.6L13.5 58.6L9.5 58L8.5 58L7.43 57.6L4.5 58.6L7.36 55.43L4.32 56.43L7.3 53.27L4.13 54.27L7.54 51.1L4.83 52.1L7.79 48.93L5.54 49.93L7.89 46.77L5.84 47.77L8.11 44.6L6.44 45.6L7.98 42.43L6.07 43.43L8.28 40.27L6.94 41.27L8.34 38.1L7.12 39.1L8.44 35.93L7.41 36.93L8.56 33.77L7.74 34.77ZM14.5 40L14.88 41.85L15.6 42.85L15 44.1L15.94 45.1L15.15 46.35L16.37 47.35L15.31 48.6L16.81 49.6L15.42 50.85L17.13 51.85L15.62 53.1L17.71 54.1L15.8 55.35L18.21 56.35L15.79 57.6L18.18 58.6L15 58L14 58L13.21 57.6L10.82 58.6L13.2 55.35L10.79 56.35L13.38 53.1L11.29 54.1L13.58 50.85L11.87 51.85L13.69 48.6L12.19 49.6L13.85 46.35L12.63 47.35L14 44.1L13.06 45.1L14.12 41.85L13.4 42.85ZM50 38L50.45 39.82L51.28 40.82L50.53 42.04L51.5 43.04L50.74 44.27L52.11 45.27L50.83 46.49L52.38 47.49L50.97 48.71L52.78 49.71L51.07 50.93L53.07 51.93L51.2 53.16L53.44 54.16L51.41 55.38L54.03 56.38L51.66 57.6L54.75 58.6L50.5 58L49.5 58L48.34 57.6L45.25 58.6L48.59 55.38L45.97 56.38L48.8 53.16L46.56 54.16L48.93 50.93L46.93 51.93L49.03 48.71L47.22 49.71L49.17 46.49L47.62 47.49L49.26 44.27L47.89 45.27L49.47 42.04L48.5 43.04L49.55 39.82L48.72 40.82ZM55.5 31L55.94 32.85L56.75 33.85L56.11 35.1L57.25 36.1L56.15 37.35L57.35 38.35L56.35 39.6L57.92 40.6L56.41 41.85L58.09 42.85L56.52 44.1L58.41 45.1L56.66 46.35L58.81 47.35L56.94 48.6L59.61 49.6L57.11 50.85L60.1 51.85L56.89 53.1L59.47 54.1L57.36 55.35L60.81 56.35L57.09 57.6L60.04 58.6L56 58L55 58L53.91 57.6L50.96 58.6L53.64 55.35L50.19 56.35L54.11 53.1L51.53 54.1L53.89 50.85L50.9 51.85L54.06 48.6L51.39 49.6L54.34 46.35L52.19 47.35L54.48 44.1L52.59 45.1L54.59 41.85L52.91 42.85L54.65 39.6L53.08 40.6L54.85 37.35L53.65 38.35L54.89 35.1L53.75 36.1L55.06 32.85L54.25 33.85ZM61 37L61.41 38.7L62.17 39.7L61.5 40.8L62.42 41.8L61.6 42.9L62.71 43.9L61.94 45L63.68 46L61.87 47.1L63.49 48.1L62.12 49.2L64.21 50.2L62.08 51.3L64.1 52.3L62.33 53.4L64.8 54.4L62.52 55.5L65.34 56.5L62.44 57.6L65.1 58.6L61.5 58L60.5 58L59.56 57.6L56.9 58.6L59.48 55.5L56.66 56.5L59.67 53.4L57.2 54.4L59.92 51.3L57.9 52.3L59.88 49.2L57.79 50.2L60.13 47.1L58.51 48.1L60.06 45L58.32 46L60.4 42.9L59.29 43.9L60.5 40.8L59.58 41.8L60.59 38.7L59.83 39.7ZM20.2 36L20.58 37.78L21.29 38.78L20.69 39.96L21.59 40.96L20.8 42.15L21.91 43.15L20.91 44.33L22.23 45.33L21.03 46.51L22.56 47.51L21.1 48.69L22.77 49.69L21.07 50.87L22.69 51.87L21.08 53.05L22.72 54.05L21.25 55.24L23.19 56.24L21.27 57.42L23.27 58.42L21.33 59.6L23.43 60.6L20.7 60L19.7 60L19.07 59.6L16.97 60.6L19.13 57.42L17.13 58.42L19.15 55.24L17.21 56.24L19.32 53.05L17.68 54.05L19.33 50.87L17.71 51.87L19.3 48.69L17.63 49.69L19.37 46.51L17.84 47.51L19.49 44.33L18.17 45.33L19.6 42.15L18.49 43.15L19.71 39.96L18.81 40.96L19.82 37.78L19.11 38.78ZM46.4 38L46.74 39.8L47.38 40.8L46.8 42L47.54 43L46.98 44.2L48.04 45.2L47.04 46.4L48.22 47.4L47.19 48.6L48.64 49.6L47.18 50.8L48.63 51.8L47.46 53L49.43 54L47.37 55.2L49.17 56.2L47.51 57.4L49.58 58.4L47.81 59.6L50.43 60.6L46.9 60L45.9 60L44.99 59.6L42.37 60.6L45.29 57.4L43.22 58.4L45.43 55.2L43.63 56.2L45.34 53L43.37 54L45.62 50.8L44.17 51.8L45.61 48.6L44.16 49.6L45.76 46.4L44.58 47.4L45.82 44.2L44.76 45.2L46 42L45.26 43L46.06 39.8L45.42 40.8ZM24.6 49L24.86 50.6L25.33 51.6L25.03 52.6L25.82 53.6L25.08 54.6L25.97 55.6L25.39 56.6L26.85 57.6L25.47 58.6L27.1 59.6L25.1 59L24.1 59L23.73 58.6L22.1 59.6L23.81 56.6L22.35 57.6L24.12 54.6L23.23 55.6L24.17 52.6L23.38 53.6L24.34 50.6L23.87 51.6ZM41.6 48L41.95 49.8L42.61 50.8L42 52L42.73 53L42.28 54.2L43.56 55.2L42.29 56.4L43.57 57.4L42.64 58.6L44.57 59.6L42.1 59L41.1 59L40.56 58.6L38.63 59.6L40.91 56.4L39.63 57.4L40.92 54.2L39.64 55.2L41.2 52L40.47 53L41.25 49.8L40.59 50.8Z","grass":{"fine":[["M1.34 64.5Q1.13 59.36 0.64 55.56M1.02 60.09q1.2 -1 2.2 -2.6M2.07 64.5Q2.06 59.21 2.06 55.29M2.06 59.79q1.2 -1 2.2 -2.6M4.66 64.5Q4.39 60.49 3.76 57.62M4.26 60.12q1.2 -1 2.2 -2.6M5.43 64.5Q5.56 61.47 5.86 59.4M5.62 61.05q1.2 -1 2.2 -2.6M5.81 64.5Q5.76 61.53 5.65 59.5M5.74 61.34q1.2 -1 2.2 -2.6M6.62 64.5Q6.85 60.76 7.37 58.1M6.96 61.05q1.2 -1 2.2 -2.6M8 64.5Q8.42 59.35 9.38 55.55M8.62 58.67q1.2 -1 2.2 -2.6M8.13 64.5Q7.75 61.75 6.87 59.91M9.11 64.5Q8.84 58.19 8.19 53.43M8.7 58.6q-1.2 -1 -2.2 -2.6M10.81 64.5Q10.52 59.7 9.83 56.18M10.37 59.39q-1.2 -1 -2.2 -2.6M11.93 64.5Q11.85 62.02 11.67 60.4M16.38 64.5Q16.71 59.87 17.5 56.49M16.74 64.5Q16.82 61.52 17 59.5M16.86 61.59q1.2 -1 2.2 -2.6M19.01 64.5Q18.71 59.27 18.03 55.4M19.57 64.5Q19.8 59.75 20.32 56.27M19.91 58.67q1.2 -1 2.2 -2.6M21.39 64.5Q21.67 59.58 22.33 55.96M23.47 64.5Q23.67 61.51 24.13 59.48M23.77 60.89q-1.2 -1 -2.2 -2.6M25.18 64.5Q24.78 61.35 23.85 59.19M24.58 61.27q1.2 -1 2.2 -2.6M26.61 64.5Q26.89 59.7 27.52 56.17M27.02 59.21q-1.2 -1 -2.2 -2.6M27.16 64.5Q27.16 60.65 27.14 57.9M28.13 64.5Q28.46 58.67 29.23 54.31M29.01 64.5Q29.28 58.35 29.9 53.73M29.8 64.5Q29.38 60.47 28.4 57.58M29.17 60.71q1.2 -1 2.2 -2.6M30.55 64.5Q30.51 60.95 30.43 58.46M30.5 60.3q1.2 -1 2.2 -2.6M31.78 64.5Q32.11 60.92 32.87 58.41M32.27 60.34q-1.2 -1 -2.2 -2.6M32.48 64.5Q32.07 58.7 31.11 54.37M35.57 64.5Q35.31 61.64 34.7 59.71M35.18 61.13q1.2 -1 2.2 -2.6M36.11 64.5Q36.02 60.82 35.8 58.22M37.33 64.5Q36.99 61.02 36.2 58.57M39.7 64.5Q39.34 62.19 38.51 60.7M39.17 61.92q1.2 -1 2.2 -2.6M39.89 64.5Q40.14 60 40.72 56.72M40.27 59.67q1.2 -1 2.2 -2.6M42.07 64.5Q42.01 59.85 41.88 56.46M41.98 58.77q-1.2 -1 -2.2 -2.6M43.99 64.5Q43.83 60.49 43.45 57.62M45.58 64.5Q45.23 58.65 44.41 54.27M46.37 64.5Q46.61 61.73 47.15 59.87M46.72 62.13q1.2 -1 2.2 -2.6M47.4 64.5Q47.81 58.97 48.76 54.85M48.21 64.5Q48.44 59.64 48.96 56.07M49.25 64.5Q49.37 59.38 49.65 55.59M49.43 58.92q1.2 -1 2.2 -2.6M52.06 64.5Q52 59.55 51.87 55.92M53.59 64.5Q53.25 62.35 52.46 60.99M53.08 62.56q-1.2 -1 -2.2 -2.6M54.18 64.5Q53.95 59.47 53.41 55.76M53.83 58.89q1.2 -1 2.2 -2.6M59.35 64.5Q59.25 60.96 59.01 58.48M61.16 64.5Q61.34 60.5 61.76 57.63M61.94 64.5Q61.5 61.49 60.46 59.44M61.28 61.91q1.2 -1 2.2 -2.6",0.33],["M3.09 64.5Q3.43 59.15 4.23 55.18M4.12 64.5Q3.98 59.89 3.67 56.53M6.99 64.5Q7.13 61.31 7.47 59.11M7.2 60.8q1.2 -1 2.2 -2.6M13.2 64.5Q13.35 58.83 13.69 54.59M14.42 64.5Q14.37 58 14.24 53.09M14.34 58.12q-1.2 -1 -2.2 -2.6M20.4 64.5Q20.15 59.65 19.58 56.09M22.08 64.5Q22.34 60.56 22.96 57.75M22.86 64.5Q23.15 60.15 23.83 56.99M23.3 59.57q1.2 -1 2.2 -2.6M24.37 64.5Q24.34 62.22 24.28 60.76M25.98 64.5Q26.16 60.1 26.58 56.91M26.25 59.45q1.2 -1 2.2 -2.6M27.69 64.5Q27.42 60.83 26.79 58.24M34.95 64.5Q35.35 59.92 36.27 56.59M35.54 59.6q1.2 -1 2.2 -2.6M36.81 64.5Q36.44 60.7 35.58 57.99M36.25 61.15q1.2 -1 2.2 -2.6M38.83 64.5Q39.18 59.11 40.01 55.11M40.58 64.5Q40.79 62.23 41.28 60.77M40.9 62q-1.2 -1 -2.2 -2.6M42.78 64.5Q42.84 58.22 42.99 53.49M43.25 64.5Q42.84 60.45 41.89 57.54M42.64 60.96q-1.2 -1 -2.2 -2.6M44.53 64.5Q44.53 59.56 44.54 55.92M44.54 58.55q1.2 -1 2.2 -2.6M45.19 64.5Q45.63 58.69 46.64 54.34M45.84 59.53q-1.2 -1 -2.2 -2.6M48.09 64.5Q47.89 60.25 47.42 57.19M47.79 59.59q-1.2 -1 -2.2 -2.6M49.53 64.5Q49.23 58.44 48.51 53.89M50.36 64.5Q50.31 58.02 50.21 53.12M50.83 64.5Q51.14 61.54 51.85 59.52M51.29 61.84q-1.2 -1 -2.2 -2.6M54.71 64.5Q54.75 61.13 54.85 58.78M55.58 64.5Q55.9 58.48 56.64 53.97M56.06 57.63q1.2 -1 2.2 -2.6M57.64 64.5Q57.22 59.88 56.24 56.52M58.7 64.5Q58.33 59.27 57.46 55.41M60.25 64.5Q59.85 58.74 58.91 54.44M59.65 59.37q-1.2 -1 -2.2 -2.6M60.66 64.5Q60.46 62.15 59.99 60.64M60.36 61.75q1.2 -1 2.2 -2.6",0.43],["M2.83 64.5Q3.17 62.19 3.98 60.7M3.34 61.89q-1.2 -1 -2.2 -2.6M9.99 64.5Q10.15 60.49 10.51 57.62M10.08 64.5Q10.36 61.34 11 59.16M10.49 61.21q-1.2 -1 -2.2 -2.6M12.23 64.5Q11.99 61.95 11.43 60.27M11.87 61.91q1.2 -1 2.2 -2.6M13.57 64.5Q13.63 60.17 13.77 57.04M13.66 59.6q1.2 -1 2.2 -2.6M14.77 64.5Q14.96 58.47 15.39 53.94M15.05 58.95q-1.2 -1 -2.2 -2.6M15.69 64.5Q16.06 61.89 16.93 60.17M16.25 61.76q-1.2 -1 -2.2 -2.6M17.67 64.5Q17.5 61.84 17.1 60.07M18.03 64.5Q18.2 62.26 18.61 60.83M20.08 64.5Q20.26 58.93 20.69 54.78M23.65 64.5Q24.05 61.2 24.97 58.9M31.01 64.5Q31.3 58.54 31.97 54.07M31.44 57.81q-1.2 -1 -2.2 -2.6M32.99 64.5Q33.11 58.78 33.37 54.5M33.16 59.23q1.2 -1 2.2 -2.6M33.57 64.5Q33.6 60.65 33.68 57.91M34.24 64.5Q34.42 61.27 34.85 59.04M38.18 64.5Q38.41 59.81 38.94 56.38M38.52 59.26q-1.2 -1 -2.2 -2.6M41.47 64.5Q41.78 58.03 42.5 53.15M51.68 64.5Q51.72 62.29 51.81 60.9M53.21 64.5Q53.57 59.18 54.38 55.24M53.74 59.46q1.2 -1 2.2 -2.6M56.04 64.5Q56.42 58.12 57.31 53.31M56.61 56.8q-1.2 -1 -2.2 -2.6M56.83 64.5Q57.07 60.41 57.64 57.47M57.2 61.05q-1.2 -1 -2.2 -2.6M58.4 64.5Q58.45 59.42 58.55 55.66M58.47 60.13q1.2 -1 2.2 -2.6M62.41 64.5Q62.61 61.87 63.08 60.14M62.71 61.35q-1.2 -1 -2.2 -2.6",0.53]],"mid":[["M1.34 64.5Q1.13 60.11 0.64 56.92M9.19 64.5Q9.1 59.44 8.88 55.71M12.05 64.5Q11.98 59.96 11.82 56.65M16.96 64.5Q16.95 59.23 16.91 55.33M20.05 64.5Q20.28 61.15 20.81 58.83M21.48 64.5Q21.68 61.68 22.14 59.78M27.43 64.5Q27.08 62.09 26.26 60.52M29.26 64.5Q28.99 59.23 28.34 55.33M33.69 64.5Q34.12 59.68 35.11 56.15M38.8 64.5Q38.72 62.1 38.54 60.55M39.75 64.5Q39.39 60.99 38.53 58.52M51.17 64.5Q50.91 59.75 50.29 56.28M58.76 64.5Q58.41 59.45 57.61 55.74M59.9 64.5Q59.81 61.92 59.59 60.21",0.45],["M7.8 64.5Q8.03 59.2 8.59 55.27M10.83 64.5Q10.7 60.51 10.39 57.65M15.07 64.5Q14.74 60.22 13.98 57.13M18.15 64.5Q18.17 60.16 18.21 57.02M32.4 64.5Q32.31 59.79 32.09 56.35M35.41 64.5Q35.14 61.95 34.54 60.27M36.7 64.5Q36.83 61.45 37.14 59.36M43.33 64.5Q43.4 60.17 43.55 57.03M52.21 64.5Q52.48 59.33 53.11 55.51M54.02 64.5Q54.36 60.89 55.14 58.34M55.41 64.5Q55.35 60.08 55.21 56.88",0.55],["M2.89 64.5Q3.13 62.18 3.7 60.69M4.4 64.5Q4.05 62.24 3.23 60.8M6.06 64.5Q5.65 59.4 4.67 55.64M13.99 64.5Q14.11 59.07 14.39 55.04M23.1 64.5Q22.71 60.93 21.82 58.42M24.8 64.5Q24.96 59.35 25.34 55.54M26.26 64.5Q26.46 60.37 26.93 57.39M30.85 64.5Q30.82 61.55 30.72 59.54M41.77 64.5Q41.71 59.83 41.58 56.42M44.7 64.5Q44.76 60.72 44.9 58.03M46.33 64.5Q46.68 59.94 47.48 56.63M47.76 64.5Q48.17 60.74 49.13 58.08M49.1 64.5Q49.44 61.22 50.25 58.94M57.15 64.5Q57.51 61.59 58.35 59.61M61.99 64.5Q61.73 61.78 61.12 59.96",0.65]],"small":[]},"rings":{"fine":{"halo":[[15.5,1.7,0.42,0.9],[17.4,1.8,0.38,0.75],[19.3,1.9,0.33,0.6],[21.2,2.0,0.28,0.45],[23.1,2.1,0.24,0.32],[25,2.2,0.2,0.22]],"sun":[[3,1.6,0.32,0.35],[5.4,1.6,0.34,0.32],[7.8,1.6,0.36,0.3],[10.2,1.6,0.38,0.3],[12.4,1.6,0.4,0.45]],"rim":[[30.4,1.25,0.22,0.55]]},"mid":{"halo":[[16,2.2,0.55,0.8],[19,2.4,0.45,0.5],[22,2.6,0.38,0.3]],"sun":[],"rim":[]},"small":{"halo":[],"sun":[],"rim":[]}}};

  // A game's Zenith (Platinum until the owner renamed it, 3 Oct 2026). Since version 45 its symbol everywhere is the Zenith
  // badge (the owner, 4 Oct 2026, of version 44's print: "IN the red badge, make it pointy mountains"; and of the
  // Achievements page, which still had version 41's mountain and star: "you didn't make zenith changes here"): a print
  // inside a ring: a misty sky; a red sun with halftone rings of dots round it and within it; a tall pointy peak in front of
  // the sun, its left face lit, its right in shade, with faint facets; a pointy peak either side behind it and a jagged range
  // behind those; pines either side; pale grass in front; a dark teal ring with a fine line, and a ring of dots round it
  // all. Version 44 had a rounded, craggy summit with two figures on top. The pines and the grass come from ZENITH_ART,
  // drawn once with seeded randomness so the app draws the same ones (tools/design-system/zenith-art.js makes both); the
  // facets and the finest dots from 48px, a simpler halo from 30px, the sun, the peaks and the pines below that.
  var ZENITH_PRINT = {
    range: "M0 45L4 41.5 6.5 43 10.5 37 13.5 40.5 16 38.5 19.5 42.5 23 39 27 43 32 40 36 43.5 40 39.5 44 42 47.5 37.5 51 41 54.5 36 58 40.5 61 38.5 64 41V64H0Z",
    sides: "M2 58L7 47 9.4 44.6 11.4 39.6 13.2 36.4 15.4 31.2 17.4 35 19 37.4 21 41.6 23.2 45 26 52 27 58ZM37 58L39.8 50.6 41.6 46.4 43.6 41.2 45.6 37 47.2 33.4 48.8 29.6 50.6 33.4 52.4 36.2 54.6 41 57 44.8 60.4 52 63 58Z",
    sidesLit: "M2 58L7 47 9.4 44.6 11.4 39.6 13.2 36.4 15.4 31.2 14.8 35.4 13.6 39.8 14.2 44 12.4 49 12.8 58ZM37 58L39.8 50.6 41.6 46.4 43.6 41.2 45.6 37 47.2 33.4 48.8 29.6 48.4 34.2 47.2 38.4 47.8 43 46.2 48.4 46.6 58Z",
    peak: "M32 20.4L30.7 23.1 29.6 24.9 28 28.4 26.7 30.4 24.9 34.5 23.4 36.8 21.6 40.9 19.6 44.2 17.8 48.7 15.5 53.3 13.6 58.4 12.2 64 51.6 64 50.2 58.7 48.5 54.1 46.1 49.5 44.7 45.3 42.5 41.5 41.1 37.7 39.2 34.7 37.9 31.1 36.3 28.7 35 25.5 33.5 23.3Z",
    lit: "M32 20.4L30.7 23.1 29.6 24.9 28 28.4 26.7 30.4 24.9 34.5 23.4 36.8 21.6 40.9 19.6 44.2 17.8 48.7 15.5 53.3 13.6 58.4 12.2 64 27.5 64 27.1 59.6 28.7 54 28 48.4 29.9 43.2 29.2 38.6 30.8 33.8 30.2 29.6 31.4 25Z",
    shade: "M32 20.4L34 26.4 35 32 36.9 37.4 37.6 43.4 39.7 49.4 40.6 55.6 42.2 64 51.6 64 50.2 58.7 48.5 54.1 46.1 49.5 44.7 45.3 42.5 41.5 41.1 37.7 39.2 34.7 37.9 31.1 36.3 28.7 35 25.5 33.5 23.3Z",
    facets: "M30.4 29.2L27.6 33.6M29.6 38.4L26 43.8M28.3 47.8L24.2 54.4M35 31.6L37.2 35.6M37.4 42.2L40.6 47.6M40.2 53.4L43.4 59",
    ground: "M0 57Q16 54.5 32 56T64 56.5V64H0Z",
    sun: [32, 27]
  };

  // Rings of dots as one path: each (radius, spacing, dot radius) round (cx, cy), as the app works them out.
  function dotRing(cx, cy, ring) {
    var n = Math.max(6, Math.round(2 * Math.PI * ring[0] / ring[1])), d = ring[2], out = "";
    for (var i = 0; i < n; i++) {
      var a = i / n * 2 * Math.PI, x = cx + ring[0] * Math.cos(a), y = cy + ring[0] * Math.sin(a);
      out += "M" + (x - d).toFixed(2) + " " + y.toFixed(2) + "a" + d + " " + d + " 0 1 0 " + (2 * d) + " 0a" + d + " " + d + " 0 1 0 " + (-2 * d) + " 0Z";
    }
    return out;
  }

  function gradStop(o, c, a) { return h("stop", { offset: String(o), style: { stopColor: c, stopOpacity: a == null ? 1 : a } }); }

  // The Zenith badge's art alone, `size` square: inside the Zenith medal, as the Zeniths monument, beside the Zeniths
  // card's title and faint in its corner. `label` makes it an image a screen reader names.
  var badgeIds = 0;
  function ZenithBadge(props) {
    var size = props.size || 56, id = useState(function () { badgeIds += 1; return "gs-badge-" + badgeIds; })[0];
    var u = function (k) { return "url(#" + id + k + ")"; };
    var kind = size >= 48 ? "fine" : size >= 30 ? "mid" : "small", fine = kind === "fine", shaded = kind !== "small";
    var rings = ZENITH_ART.rings[kind], P = ZENITH_PRINT;
    return h("svg", { className: cx("gs-zbadge", props.className), width: size, height: size, viewBox: "0 0 64 64", style: props.style,
        role: props.label ? "img" : undefined, "aria-label": props.label, "aria-hidden": props.label ? undefined : "true" },
      h("defs", null,
        h("linearGradient", { id: id + "k", x1: 0, y1: 0, x2: 0, y2: 1 }, gradStop(0, "#9fb6c1"), gradStop(0.55, "#c4d3d9"), gradStop(1, "#dbe4e7")),
        h("radialGradient", { id: id + "s", cx: "45%", cy: "40%", r: "60%" }, gradStop(0, "#e0503f"), gradStop(0.7, "#c73a30"), gradStop(1, "#b02e27")),
        h("linearGradient", { id: id + "p", x1: 0, y1: 0, x2: 0, y2: 1 }, gradStop(0, "#6f8f9b"), gradStop(1, "#4b6b78")),
        h("linearGradient", { id: id + "r", x1: 0, y1: 0, x2: 1, y2: 1 }, gradStop(0, "#466a78"), gradStop(0.5, "#2d4c59"), gradStop(1, "#1b3440")),
        h("linearGradient", { id: id + "h", x1: 0, y1: 0, x2: 0, y2: 1 }, gradStop(0, "#7d98a3", 0), gradStop(1, "#7d98a3", 0.55)),
        h("linearGradient", { id: id + "g", x1: 0, y1: 0, x2: 0, y2: 1 }, gradStop(0, "#2a4d5a"), gradStop(1, "#173039")),
        h("clipPath", { id: id + "c" }, h("circle", { cx: 32, cy: 32, r: 27.6 }))),
      h("circle", { cx: 32, cy: 32, r: 31.4, fill: "#1b3640" }),
      h("circle", { cx: 32, cy: 32, r: 29.4, fill: "none", stroke: "#e8eef0", strokeWidth: 0.7, opacity: 0.9 }),
      h("g", { clipPath: u("c") },
        h("rect", { x: 0, y: 0, width: 64, height: 64, fill: u("k") }),
        rings.halo.map(function (ring, i) { return h("path", { key: "h" + i, d: dotRing(P.sun[0], P.sun[1], ring), fill: "#fff", opacity: ring[3] }); }),
        h("circle", { cx: P.sun[0], cy: P.sun[1], r: 13.4, fill: u("s") }),
        rings.sun.map(function (ring, i) {
          return h("path", { key: "s" + i, d: dotRing(P.sun[0], P.sun[1], ring), fill: i === rings.sun.length - 1 ? "#fff" : "#ffd9d0", opacity: ring[3] });
        }),
        h("path", { d: P.range, fill: "#8aa4ae", opacity: 0.55 }),
        h("path", { d: P.sides, fill: u("p") }),
        shaded ? h("path", { d: P.sidesLit, fill: "#9db3bc", opacity: 0.35 }) : null,
        h("path", { d: P.peak, fill: u("r") }),
        shaded ? h("path", { d: P.lit, fill: "#6b8d99", opacity: 0.5 }) : null,
        shaded ? h("path", { d: P.shade, fill: "#132c36", opacity: 0.45 }) : null,
        fine ? h("path", { d: P.facets, fill: "none", stroke: "#122a33", strokeWidth: 0.4, strokeLinecap: "round", opacity: 0.45 }) : null,
        h("rect", { x: 0, y: 50, width: 64, height: 14, fill: u("h") }),
        h("path", { d: ZENITH_ART.far, fill: "#3e6170" }),
        h("path", { d: ZENITH_ART.near, fill: "#183641" }),
        h("path", { d: P.ground, fill: u("g") }),
        ZENITH_ART.grass[kind].map(function (g, i) {
          return h("path", { key: "g" + i, d: g[0], fill: "none", stroke: "#eef3f4", strokeWidth: g[1], strokeLinecap: "round", opacity: 0.85 });
        })),
      rings.rim.map(function (ring, i) { return h("path", { key: "r" + i, d: dotRing(32, 32, ring), fill: "#e8eef0", opacity: ring[3] }); }));
  }

  // The Zenith's scene (version 47; the owner, 4 Oct 2026, drawing over the Zeniths card's corner: "Make it something like
  // that"): the badge's sun and peaks set free of its ring, large and faint in the Zeniths card's bottom right corner: a big
  // red sun in halftone rings of dots behind a tall pointy peak, a smaller peak either side and a low range, all rising from
  // the card's bottom edge. The peaks are in the page's ink (`color`), lit from the left, so the scene shows on dark and
  // light pages alike; the sun keeps its red. Drawn in a 300 x 340 box anchored at the card's bottom right; the card clips
  // what reaches past it. Decoration only.
  var ZENITH_SCENE = {
    sun: [192, 112, 108],
    halo: [[118, 6.5, 2.2, 0.9], [130, 7, 2, 0.7], [143, 7.5, 1.8, 0.5], [157, 8, 1.6, 0.35]],
    within: [[26, 6, 1.6, 0.35], [48, 6, 1.7, 0.32], [70, 6, 1.8, 0.3], [92, 6, 1.9, 0.45]],
    range: "M0 340L14 306 24 312 36 296 46 306 60 290 72 302 84 296 98 310 110 340ZM226 340L238 300 250 308 262 288 274 300 290 282 304 292V340Z",
    peaks: "M56 240L46 258 38 272 26 302 12 340 110 340 92 302 78 272 66 258ZM286 214L276 234 266 250 254 282 240 340 304 340 304 246 296 230ZM176 96L168 118 160 134 149 164 139 182 124 218 112 240 98 274 84 300 70 340 290 340 276 314 262 290 248 250 234 226 222 192 208 170 198 140 186 120Z",
    shade: "M56 240L66 258 78 272 92 302 110 340 48 340 56 310 52 280ZM286 214L296 230 304 246 304 340 278 340 286 290 282 252ZM176 96L186 120 198 140 208 170 222 192 234 226 248 250 262 290 276 314 290 340 150 340 162 280 158 240 168 200 164 166 172 132Z"
  };
  var sceneIds = 0;
  function ZenithScene(props) {
    var id = useState(function () { sceneIds += 1; return "gs-scene-" + sceneIds; })[0], Z = ZENITH_SCENE, sun = Z.sun;
    return h("svg", { className: cx("gs-zenith-scene", props.className), width: props.width || 300, height: props.height || 340, viewBox: "0 0 300 340",
        style: props.style, "aria-hidden": "true" },
      h("defs", null, h("radialGradient", { id: id + "s", cx: "45%", cy: "40%", r: "60%" }, gradStop(0, "#e0503f"), gradStop(0.7, "#c73a30"), gradStop(1, "#b02e27"))),
      Z.halo.map(function (ring, i) { return h("path", { key: "h" + i, d: dotRing(sun[0], sun[1], ring), fill: "#fff", opacity: ring[3] }); }),
      h("circle", { cx: sun[0], cy: sun[1], r: sun[2], fill: "url(#" + id + "s)" }),
      Z.within.map(function (ring, i) {
        return h("path", { key: "w" + i, d: dotRing(sun[0], sun[1], ring), fill: i === Z.within.length - 1 ? "#fff" : "#ffd9d0", opacity: ring[3] });
      }),
      h("path", { d: Z.range, fill: "currentColor", opacity: 0.55 }),
      h("path", { d: Z.peaks, fill: "currentColor" }),
      h("path", { d: Z.shade, fill: "#000", fillOpacity: 0.28 }));
  }

  /* ---------- Version 49: the tiers as banners, each a harder mountain; the Zenith in red and gold (the owner, 5 Oct 2026) ---------- */
  // The owner: "What if we had flags. I want the color of zenith to be red. Like the main its silver bronze and gold? zenith
  // will be red", "maybe try difficulty of mountains as the tiers", "Keep everest as zenith and K2 as second". Each tier is
  // a banner hanging from its bar, each a harder mountain, each adding a layer: Bronze Fuji on teal cloth, one point, a
  // plain rod; Silver the Matterhorn on blue, two tails, knobs and loops round the bar; Gold K2 on purple, three tails with
  // a gold fringe, a spear tip, cords and tassels, stitching and a lozenge in each top corner; the Zenith Everest on red,
  // its plume of snow, the sun at its height above it, a chief with three stars, a braided gold border, a laurel, a sun on
  // its bar and a tassel on every tail. Drawn in a 120 x 190 box; under 40px a banner keeps its cloth, one border and its
  // mountain (the Zenith's: the sun and Everest with its plume). Its colours are fixed in every theme, like the metals.
  // The app draws the very same paths (GsTierBanner).
  var BANNER = (function () {
    var uid = 0;
    var METAL = {
      gold: { hi: "#ffe39a", mid: "#f0c35a", lo: "#c08a2a", deep: "#8f6214" },
      silver: { hi: "#f7f9fb", mid: "#d5dbe2", lo: "#9aa5b1", deep: "#6d7884" },
      bronze: { hi: "#f2c497", mid: "#d7955c", lo: "#a5683a", deep: "#7a4a24" }
    };
    var CLOTH = {
      teal: { hi: "#2f6a62", mid: "#265a53", lo: "#1d4842", deep: "#12312d" },
      blue: { hi: "#3c4e6d", mid: "#30405b", lo: "#25324a", deep: "#1a2436" },
      purple: { hi: "#58386f", mid: "#482c5e", lo: "#38214b", deep: "#261632" },
      red: { hi: "#bb2a3f", mid: "#a51d32", lo: "#871527", deep: "#5c0d1a" }
    };
    var TIER = {
      bronze: { metal: METAL.bronze, cloth: CLOTH.teal, rank: 0 },
      silver: { metal: METAL.silver, cloth: CLOTH.blue, rank: 1 },
      gold: { metal: METAL.gold, cloth: CLOTH.purple, rank: 2 },
      zenith: { metal: METAL.gold, cloth: CLOTH.red, rank: 3 }
    };
    // Bronze one point, Silver two tails, Gold and the Zenith three.
    var SHAPE = [
      "M22 19H98V150L60 172L22 150Z",
      "M22 19H98V170L60 147L22 170Z",
      "M22 19H98V166L79 150L60 168L41 150L22 166Z",
      "M22 19H98V170L79 152L60 172L41 152L22 170Z"
    ];
    var SNOW = "#fbf7ee";
    function grad(id, c, folds) {
      var stops = folds ? [[0, c.lo], [.14, c.hi], [.3, c.mid], [.46, c.hi], [.62, c.lo], [.8, c.hi], [1, c.mid]] : [[0, c.hi], [.5, c.mid], [1, c.lo]];
      return '<linearGradient id="' + id + '" x1="0" y1="0" x2="' + (folds ? 1 : 0) + '" y2="' + (folds ? 0 : 1) + '">' +
        stops.map(function (s) { return '<stop offset="' + s[0] + '" stop-color="' + s[1] + '"/>'; }).join("") + '</linearGradient>';
    }
    function star(cx, cy, r, fill) {
      var p = [];
      for (var i = 0; i < 10; i++) {
        var a = -Math.PI / 2 + i * Math.PI / 5, rr = i % 2 ? r * 0.45 : r;
        p.push((cx + rr * Math.cos(a)).toFixed(2) + " " + (cy + rr * Math.sin(a)).toFixed(2));
      }
      return '<path d="M' + p.join("L") + 'Z" fill="' + fill + '"/>';
    }
    // The sun in splendour: a disc and rays, long and short in turn.
    function sun(cx, cy, r, rays, fill, ray) {
      var s = "";
      for (var i = 0; i < rays; i++) {
        var a = i / rays * 2 * Math.PI, long = i % 2 === 0, len = long ? r * 1.95 : r * 1.55, w = long ? 0.16 : 0.12;
        s += "M" + (cx + r * 1.08 * Math.cos(a - w)).toFixed(2) + " " + (cy + r * 1.08 * Math.sin(a - w)).toFixed(2) +
          "L" + (cx + len * Math.cos(a)).toFixed(2) + " " + (cy + len * Math.sin(a)).toFixed(2) +
          "L" + (cx + r * 1.08 * Math.cos(a + w)).toFixed(2) + " " + (cy + r * 1.08 * Math.sin(a + w)).toFixed(2) + "Z";
      }
      return (rays ? '<path d="' + s + '" fill="' + (ray || fill) + '"/>' : "") + '<circle cx="' + cx + '" cy="' + cy + '" r="' + r + '" fill="' + fill + '"/>';
    }
    // A laurel: two curved branches meeting under the mountains, their leaves along them in pairs.
    function laurel(fill, lo) {
      var out = "";
      [1, -1].forEach(function (side) {
        var p0 = [60, 150], p1 = [60 + side * 17, 152], p2 = [60 + side * 26, 133];
        out += '<path d="M' + p0 + 'Q' + p1 + ' ' + p2 + '" fill="none" stroke="' + lo + '" stroke-width="1.4" stroke-linecap="round"/>';
        for (var i = 1; i <= 6; i++) {
          var t = i / 6.4, u = 1 - t;
          var x = u * u * p0[0] + 2 * u * t * p1[0] + t * t * p2[0], y = u * u * p0[1] + 2 * u * t * p1[1] + t * t * p2[1];
          var dx = 2 * u * (p1[0] - p0[0]) + 2 * t * (p2[0] - p1[0]), dy = 2 * u * (p1[1] - p0[1]) + 2 * t * (p2[1] - p1[1]), n = Math.sqrt(dx * dx + dy * dy);
          var a = Math.atan2(dy, dx) * 180 / Math.PI;
          [-1, 1].forEach(function (k) {
            var ox = x - k * dy / n * 2.4, oy = y + k * dx / n * 2.4;
            out += '<ellipse cx="' + ox.toFixed(1) + '" cy="' + oy.toFixed(1) + '" rx="3.6" ry="1.6" transform="rotate(' + (a + k * 32).toFixed(0) + ' ' + ox.toFixed(1) + ' ' + oy.toFixed(1) + ')" fill="' + fill + '"/>';
          });
        }
      });
      return out;
    }
    // A tassel hanging from (x, y): a cord, a knot and a skirt of threads.
    function tassel(x, y, len, m) {
      var b = y + len;
      return '<path d="M' + x + ' ' + y + 'V' + b + '" stroke="' + m.lo + '" stroke-width="1.1"/><circle cx="' + x + '" cy="' + (b + 1.5) + '" r="2.1" fill="' + m.mid + '"/>' +
        '<path d="M' + (x - 2.2) + ' ' + (b + 3) + 'L' + (x - 3.4) + ' ' + (b + 12) + 'L' + (x + 3.4) + ' ' + (b + 12) + 'L' + (x + 2.2) + ' ' + (b + 3) + 'Z" fill="' + m.mid + '"/>' +
        '<path d="M' + (x - 1.4) + ' ' + (b + 4) + 'V' + (b + 12) + 'M' + x + ' ' + (b + 4) + 'V' + (b + 12) + 'M' + (x + 1.4) + ' ' + (b + 4) + 'V' + (b + 12) + '" stroke="' + m.lo + '" stroke-width=".5"/>';
    }
    // A fringe of gold threads hanging from the cloth's foot, along each of its edges.
    function fringe(points, m) {
      var s = "";
      for (var i = 0; i + 1 < points.length; i++) {
        var a = points[i], b = points[i + 1], len = Math.sqrt(Math.pow(b[0] - a[0], 2) + Math.pow(b[1] - a[1], 2)), n = Math.floor(len / 1.7);
        for (var k = 0; k <= n; k++) {
          var x = a[0] + (b[0] - a[0]) * k / n, y = a[1] + (b[1] - a[1]) * k / n;
          s += "M" + x.toFixed(1) + " " + y.toFixed(1) + "V" + (y + 4.2).toFixed(1);
        }
      }
      return '<path d="' + s + '" stroke="' + m.mid + '" stroke-width=".9"/>';
    }
    // Each mountain drawn smaller about the middle of the cloth, so it stays clear of the border.
    function fit(s, cy, from, inner) {
      return '<g transform="translate(60 ' + cy + ') scale(' + s + ') translate(-60 -' + from + ')">' + inner + '</g>';
    }
    var MOUNTAIN = {
      // Fuji: a wide cone with a flat top, snow streaming down from it.
      bronze: function (m) {
        return fit(0.8, 92, 108, '<path d="M26 132C40 120 49 100 53 84H67C71 100 80 120 94 132Z" fill="' + m.mid + '"/>' +
          '<path d="M60 84H67C71 100 80 120 94 132H63Z" fill="' + m.lo + '"/>' +
          '<path d="M53 84H67C67.8 88.6 69 93.2 70.4 97.4L68.6 96.2L67.8 102.4L65.8 97.2L64.4 104.6L62.6 97.8L60.6 102.8L58.8 97.4L56.8 104L55.2 97L53.6 101.4L52 97.8L49.8 99.2C51 94.8 52.2 89.6 53 84Z" fill="' + SNOW + '"/>');
      },
      // The Matterhorn: a narrow horn, its tip hooked to the left, the north face in shade.
      silver: function (m) {
        return fit(0.8, 92, 101, '<path d="M28 134L44 110L50 94L52.4 82L53.6 71L56 68.6L58.6 74L61 82L66 92L74 106L84 120L94 134Z" fill="' + m.mid + '"/>' +
          '<path d="M56 68.6L58.6 74L61 82L66 92L74 106L84 120L94 134H62L59 110L57.6 90Z" fill="' + m.lo + '"/>' +
          '<path d="M53.6 71L56 68.6L58.6 74L57.6 80.6L55.8 76.8L54.4 82.4L52.8 78.6Z" fill="' + SNOW + '"/>' +
          '<path d="M63.4 87.6L66 92L64.6 96.4L62.8 92.6ZM70.6 100.6L74 106L71.6 108.6L69.6 104.4ZM46.6 104L50 94L49.8 100.4L48 106Z" fill="' + SNOW + '" opacity=".9"/>');
      },
      // K2: the savage mountain, a steep pyramid with its shoulder on the right, snow on its upper faces, the Karakoram behind.
      gold: function (m) {
        return fit(0.78, 94, 104, '<path d="M26 136L42 108L58 136Z" fill="' + m.deep + '"/><path d="M62 136L79 104L94 136Z" fill="' + m.deep + '"/>' +
          '<path d="M79 104L83.2 112.6L80.4 111.4L78 114.4L76.2 111.2Z" fill="' + SNOW + '" opacity=".8"/>' +
          '<path d="M30 138L38 122L44 108L49 96L54 84L58 74L60 70L62 75L65 82L67.6 88.4L71.6 91L74.6 93.4L78.4 103L84 115L91 138Z" fill="' + m.mid + '"/>' +
          '<path d="M60 70L62 75L65 82L67.6 88.4L71.6 91L74.6 93.4L78.4 103L84 115L91 138H62L61.2 108L60.6 90Z" fill="' + m.lo + '"/>' +
          '<path d="M60 70L62 75L65 82L67.6 88.4L71.6 91L70.4 95.6L67.4 93.6L64.8 100.2L62 94.4L59.4 101.4L56.8 95L54.2 99.2L52.6 94.6L55.4 86L58 76Z" fill="' + SNOW + '"/>' +
          '<path d="M45.6 106L49 96L49.6 101.6L47.4 108.4ZM74.6 106.4L78.4 103L77.6 108.4L75.8 110.6Z" fill="' + SNOW + '" opacity=".85"/>');
      },
      // Everest: the roof of the world, a broad pyramid of rock behind lower ridges, a plume of snow streaming from its
      // summit; the sun at its height above, a laurel below.
      zenith: function (m) {
        var rock = "#2f3036", lit = "#46474e", ridge = "#26272c";
        return sun(60, 60, 8.5, 16, m.hi, m.mid) +
          fit(0.78, 104, 106, '<path d="M24 134L40 112L50 100L57 86L62 78L66 84L74 96L81 104L88 113L96 134Z" fill="' + lit + '" stroke="' + m.mid + '" stroke-width="1.6" stroke-linejoin="round"/>' +
            '<path d="M62 78L66 84L74 96L81 104L88 113L96 134H64L63 104Z" fill="' + rock + '"/>' +
            '<path d="M62 78L64.6 82.2L62.8 81.4L61.2 84L59.6 81.2L58 82.6Z" fill="' + SNOW + '"/>' +
            '<path d="M62 78C71 73.6 83 73.4 94 69.6C86 76 76 78.4 64.6 82Z" fill="' + SNOW + '" opacity=".92"/>' +
            '<path d="M52 98L56 90L57 94.6L54.6 100.6ZM70.4 92L74 96L72.4 99.6L70 96.6Z" fill="' + SNOW + '" opacity=".9"/>' +
            '<path d="M24 134L34 122L42 125L50 117L58 126L68 119L78 126L87 121L96 134Z" fill="' + ridge + '" stroke="' + m.mid + '" stroke-width="1.3" stroke-linejoin="round"/>' +
            '<path d="M50 117L54.4 121.6L52 121L50.4 123.4L48.6 120.8L46.6 121.6ZM87 121L90.4 125.6L88.2 125.2L86.6 127L85.6 124.4Z" fill="' + SNOW + '" opacity=".85"/>') +
          laurel(m.mid, m.lo);
      }
    };
    // Under 40px the Zenith keeps the sun and Everest with its plume; the others their mountain as drawn.
    var MOUNTAIN_MIN = {
      zenith: function (m) {
        return '<circle cx="60" cy="54" r="11" fill="' + m.hi + '"/>' +
          '<path d="M30 140L60 78L92 140Z" fill="#46474e" stroke="' + m.mid + '" stroke-width="3" stroke-linejoin="round"/>' +
          '<path d="M60 78C70 74 79 73 88 70C81 77 72 79.6 63 83Z" fill="' + SNOW + '"/>';
      }
    };
    function svg(h, tier, detail) {
      var t = TIER[tier] || TIER.zenith, m = t.metal, c = t.cloth, r = t.rank, id = "gs-bn-" + (++uid), min = detail === "min", w = h * 120 / 190;
      var cloth = SHAPE[r];
      var s = '<svg width="' + w.toFixed(1) + '" height="' + h + '" viewBox="0 0 120 190" aria-hidden="true"><defs>' + grad(id + "c", c, true) + grad(id + "m", m) +
        '<clipPath id="' + id + 'k"><path d="' + cloth + '"/></clipPath>' +
        '<pattern id="' + id + 'w" width="3" height="3" patternUnits="userSpaceOnUse" patternTransform="rotate(45)"><path d="M0 0V3" stroke="#000" stroke-width=".6" opacity=".07"/></pattern></defs>';
      // The bar: Bronze a plain rod; Silver round knobs; Gold a spear tip, cords and tassels; the Zenith a sun on top.
      if (r >= 2 && !min) s += '<path d="M12 15L60 6L108 15" fill="none" stroke="' + m.lo + '" stroke-width="1.1"/>';
      if (r === 3 && !min) s += sun(60, 5, 3.4, 12, m.hi, m.mid);
      else if (r >= 1 || min) s += '<path d="M60 0L64.5 8.5L60 11.5L55.5 8.5Z" fill="url(#' + id + 'm)"/>';
      s += '<rect x="' + (r === 0 && !min ? 16 : 11) + '" y="12" width="' + (r === 0 && !min ? 88 : 98) + '" height="' + (min ? 7 : 5) + '" rx="' + (r === 0 ? 1 : 2.5) + '" fill="url(#' + id + 'm)"/>';
      if (r >= 1 || min) s += '<circle cx="10" cy="14.5" r="' + (min ? 5 : 4) + '" fill="url(#' + id + 'm)"/><circle cx="110" cy="14.5" r="' + (min ? 5 : 4) + '" fill="url(#' + id + 'm)"/>';
      if (r >= 2 && !min) s += tassel(10, 18, r === 3 ? 30 : 24, m) + tassel(110, 18, r === 3 ? 30 : 24, m);
      // The cloth, in folds, woven; Gold's fringe hangs below its foot.
      if (r === 2 && !min) s += fringe([[22, 166], [41, 150], [60, 168], [79, 150], [98, 166]], m);
      s += '<path d="' + cloth + '" fill="' + (min ? c.mid : 'url(#' + id + 'c)') + '"/>';
      if (!min) s += '<path d="' + cloth + '" fill="url(#' + id + 'w)"/>';
      if (r >= 1 && !min) [32, 55, 78].forEach(function (x) { s += '<rect x="' + x + '" y="9.5" width="10" height="12" rx="2.5" fill="' + c.lo + '"/><rect x="' + x + '" y="9.5" width="10" height="3" rx="1.5" fill="' + c.hi + '" opacity=".6"/>'; });
      // The Zenith: a darker chief across the top with three gold stars.
      if (r === 3 && !min) {
        s += '<g clip-path="url(#' + id + 'k)"><rect x="20" y="19" width="80" height="18" fill="' + c.deep + '" opacity=".55"/></g>' +
          '<path d="M22 37H98" stroke="' + m.mid + '" stroke-width="1"/>' + star(42, 28, 3.6, m.mid) + star(60, 28, 3.6, m.mid) + star(78, 28, 3.6, m.mid);
      }
      // The border: Bronze one band; Silver a band and a line; Gold a band, a line and stitches with a lozenge in each top
      // corner; the Zenith a braided band and a line.
      if (min) {
        s += '<path d="' + cloth + '" fill="none" stroke="' + m.mid + '" stroke-width="6" clip-path="url(#' + id + 'k)"/>';
      } else {
        s += '<g clip-path="url(#' + id + 'k)"><path d="' + cloth + '" fill="none" stroke="url(#' + id + 'm)" stroke-width="' + (r === 3 ? 10 : r === 2 ? 8.4 : 7) + '"/>';
        if (r === 3) s += '<path d="' + cloth + '" fill="none" stroke="' + m.lo + '" stroke-width="6.4" stroke-dasharray=".8 2.2"/>';
        s += '</g>';
        if (r >= 1) {
          var inset = r === 3 ? 0.84 : r === 2 ? 0.86 : 0.88;
          s += '<path d="' + cloth + '" fill="none" stroke="' + m.mid + '" stroke-width="' + (1 / inset).toFixed(2) + '" transform="translate(60 96) scale(' + inset + ' ' + (inset + 0.04) + ') translate(-60 -96)"/>';
        }
        if (r === 2) {
          s += '<path d="' + cloth + '" fill="none" stroke="' + m.mid + '" stroke-width="1" stroke-dasharray="2.4 2.4" opacity=".8" transform="translate(60 96) scale(.78 .83) translate(-60 -96)"/>' +
            '<path d="M36 35.6l3.4 3.4-3.4 3.4-3.4-3.4ZM84 35.6l3.4 3.4-3.4 3.4-3.4-3.4Z" fill="' + m.mid + '"/>';
        }
      }
      s += min && MOUNTAIN_MIN[tier] ? MOUNTAIN_MIN[tier](m) : MOUNTAIN[tier] ? MOUNTAIN[tier](m) : MOUNTAIN.zenith(m);
      if (r === 3 && !min) [[22, 170], [60, 172], [98, 170]].forEach(function (p) { s += tassel(p[0], p[1] - 1, 2, m); });
      return s + '</svg>';
    }
    return { svg: svg, OUTLINE: SHAPE[3] };
  })();

  // A tier's banner (version 49): `tier` bronze, silver, gold or zenith; `size` its height (its width is 120/190 of it);
  // `detail` "min" under 40px unless given. Decoration unless given a `label`.
  function TierBanner(props) {
    var tier = props.tier || "zenith", size = props.size || 120, detail = props.detail || (size < 40 ? "min" : "full");
    return h("span", { className: cx("gs-banner", "gs-banner--" + tier, props.className),
      style: Object.assign({ width: Math.round(size * 120 / 190), height: size }, props.style),
      role: props.label ? "img" : undefined, "aria-label": props.label || undefined, "aria-hidden": props.label ? undefined : "true",
      dangerouslySetInnerHTML: { __html: BANNER.svg(size, tier, detail) } });
  }

  // A game's 100% (version 49; the owner, 5 Oct 2026: "can the 100% be animated? or glowy or something to show its
  // zenith. I really want players to feel awarded when they hit 100%", then "gold and red seems good"): the ring gold at
  // the top running to deep red at the bottom and back, a warm gold glow breathing round it, a glint running round it every
  // six seconds and faint sun rays behind it turning once a minute. `moment`: the first time a game's 100% is seen, the
  // ring fills its last stretch, flashes, and light bursts out with gold sparks; the Zenith banner beside it unfurls
  // (AchievementsOverview). With reduced motion it glows, still.
  function ZenithRing(props, size, stroke, below) {
    var label = props.label != null ? props.label : "100%", sparks = [];
    if (props.moment) {
      for (var i = 0; i < 16; i++) {
        sparks.push(h("i", { key: "s" + i, className: "gs-zring-spark", style: { "--a": (i * 22.5 + (i % 2) * 8) + "deg", "--r0": Math.round(size * 0.46) + "px",
          "--d": Math.round(size * (0.66 + (i * 37 % 40) / 100)) + "px", animationDelay: (0.9 + (i % 3) * 0.04) + "s" } }));
      }
    }
    var ring = h("div", { className: cx("gs-ring", "gs-ring--zenith", size < 96 && "is-small", props.moment && "is-moment", !below && props.className),
        style: { width: size, height: size, "--zstroke": stroke + "px" }, role: below ? undefined : "img", "aria-hidden": below ? "true" : undefined,
        "aria-label": below ? undefined : props.ariaLabel || ("Zenith: 100%" + (props.sub ? ", " + props.sub : "")) },
      h("span", { className: "gs-zring-light", "aria-hidden": "true" }, h("span", { className: "gs-zring-rays" }), h("span", { className: "gs-zring-halo" })),
      h("span", { className: "gs-zring-track", "aria-hidden": "true" }),
      h("span", { className: "gs-zring-arc", "aria-hidden": "true" }),
      h("span", { className: "gs-zring-glint", "aria-hidden": "true" }),
      props.moment ? h("span", { className: "gs-zring-flash", "aria-hidden": "true" }) : null,
      props.moment ? h("span", { className: "gs-zring-burst", "aria-hidden": "true" }) : null,
      sparks,
      h("div", { className: "gs-ring-label" },
        h("span", { className: "gs-ring-value", style: { fontSize: Math.round(size * (props.sub && !below ? 0.22 : 0.26)) } }, label),
        props.sub && !below ? h("span", { className: "gs-ring-sub", style: { fontSize: Math.max(10, Math.round(size * 0.1)) } }, props.sub) : null));
    if (!below) return ring;
    return h("div", { className: cx("gs-ring-wrap", props.className), role: "img", "aria-label": props.ariaLabel || ("Zenith: 100%, " + props.sub) },
      ring, h("span", { className: "gs-ring-under" }, props.sub));
  }

  // A game's Zenith medal (version 49): earned, the Zenith's banner, Everest on red, standing in the medal's box with a
  // warm glow under it; `unfurl` drops it open from its bar as it shows (the Zenith's moment, the popup), still with
  // reduced motion. Not yet: the banner's outline, dashed, round the Zenith's line mark. Before version 49 it was the
  // Zenith badge, a red sun behind pointy mountains in a ring.
  function ZenithMedal(props) {
    var size = props.size || 56, earned = !!props.earned;
    var label = props.label || (earned ? "Zenith: every achievement unlocked" : "Zenith: not yet");
    if (!earned) {
      return h("span", { className: "gs-zenith", style: { width: size, height: size }, role: "img", "aria-label": label },
        h("svg", { className: "gs-zenith-outline", width: Math.round(size * 120 / 190), height: size, viewBox: "0 0 120 190", "aria-hidden": "true" },
          h("path", { d: "M12 14.5H108", fill: "none", stroke: "currentColor", strokeWidth: 6, strokeLinecap: "round" }),
          h("path", { d: BANNER.OUTLINE, fill: "none", stroke: "currentColor", strokeWidth: 6, strokeDasharray: "11 9", strokeLinejoin: "round" })),
        h(Icon, { name: "zenith", size: Math.round(size * 0.34), strokeWidth: 1.8 }));
    }
    return h("span", { className: cx("gs-zenith", "is-earned", props.unfurl && "is-unfurling"), style: { width: size, height: size }, role: "img", "aria-label": label },
      h(TierBanner, { tier: "zenith", size: size, className: "gs-zenith-art" }));
  }

  // How far a game is: its ring, its unlocked tiers, and its Zenith, earned or how many to go. `size` lg (the game's
  // page and its achievements) or sm (Home: a 76px ring with its count under it, the tiers in a column and a small Zenith
  // line beside them). `completedOn` is the day the last one was unlocked, once every one is. Version 41 laid Home's out
  // again (a two-colour ring, the tiers as coins, the Zenith as a night-sky strip); the owner preferred the simpler card
  // (4 Oct 2026: "The home achievement card should be the one before that. Without the zenith card. Where it was more
  // simple"), so since version 46 it's this one again. Since version 52 (the owner, 7 Oct 2026: "have dot version for
  // zenith, not the flag") Home's Zenith is the Zenith's dot in the tiers' column, an empty ring until it's earned; a
  // game's page keeps the banner.
  // `moment` (version 49): the first time this game's 100% is seen, the ring's moment plays and its banner unfurls (on
  // Home, its dot pops in).
  function AchievementsOverview(props) {
    var lg = props.size !== "sm", done = props.done || 0, total = props.total || 0, all = total > 0 && done >= total;
    var pct = total ? 100 * done / total : 0, tiers = props.tiers || {};
    var spoken = done + " of " + total + " achievements unlocked, " + Math.floor(pct) + "%";
    return h("div", { className: cx("gs-achov", lg ? "gs-achov--lg" : "gs-achov--sm") },
      h(ProgressRing, { value: pct, size: lg ? 112 : 76, tone: all ? "zenith" : null, moment: all && props.moment, label: Math.floor(pct) + "%", sub: done + " / " + total, ariaLabel: spoken }),
      h("div", { className: "gs-achov-side" },
        h("div", { className: "gs-achov-tiers" },
          ["gold", "silver", "bronze"].map(function (t) { return h(TierChip, { key: t, tier: t, count: tiers[t] || 0 }); })),
        h("div", { className: cx("gs-achov-zenith", all && "is-earned", all && props.moment && "is-moment") },
          lg ? h(ZenithMedal, { earned: all, size: 48, unfurl: all && props.moment })
            : h("span", { className: cx("gs-tier-dot", "gs-tier--zenith", !all && "is-not-yet"), "aria-hidden": "true" }),
          h("div", { className: "gs-achov-zenith-text" },
            h("span", { className: "gs-achov-zenith-title" }, all ? "Zenith" : (total - done) + " to go for its Zenith"),
            h("span", { className: "gs-achov-zenith-sub" }, all ? "Every achievement, " + (props.completedOn || "unlocked") : "Every achievement unlocked earns it")))));
  }

  // A game's rarest unlocked achievement, in its own light: its badge large, what it is, and how few players have it.
  function AchievementSpotlight(props) {
    var a = props.item, tier = tierOf(a.pct);
    return h("div", { className: cx("gs-spot", tier && "gs-spot--" + tier) },
      h(AchievementBadge, { icon: a.icon, name: a.name, tier: tier, size: props.size === "sm" ? "md" : "lg" }),
      h("div", { className: "gs-spot-text" },
        h("span", { className: "gs-spot-eyebrow" }, props.eyebrow || "Rarest you have"),
        h("span", { className: "gs-spot-name" }, a.name),
        a.desc && props.size !== "sm" ? h("span", { className: "gs-spot-desc" }, a.desc) : null,
        h("span", { className: "gs-spot-meta" }, tier ? h(TierChip, { tier: tier }) : null,
          a.pct != null ? h("span", null, rarityWord(a.pct) + " · " + pctText(a.pct) + " of players") : null)));
  }

  // A row of a game's achievements, as the list of every one shows them: its badge, name and description, and on the
  // right when it was unlocked, or how many players have it while it's locked. A hidden one keeps both secret.
  function AchievementRow(props) {
    var a = props.item, state = a.state || (a.when ? "unlocked" : "locked"), tier = tierOf(a.pct);
    var hidden = state === "hidden";
    return h("button", { type: "button", className: cx("gs-achrow", props.selected && "is-selected", "is-" + state), onClick: props.onClick,
        "aria-pressed": props.selected ? "true" : "false",
        "aria-label": hidden ? "Hidden achievement, locked" : a.name + (state === "unlocked" ? ", unlocked " + a.when : ", locked") + (a.pct != null ? ", " + pctText(a.pct) + " of players" : "") },
      h(AchievementBadge, { icon: a.icon, name: a.name, state: state, tier: tier, size: "sm" }),
      h("span", { className: "gs-achrow-text" },
        h("span", { className: "gs-achrow-name" }, hidden ? "Hidden achievement" : a.name),
        h("span", { className: "gs-achrow-desc" }, hidden ? "Keep playing to reveal it." : a.desc || "")),
      h("span", { className: "gs-achrow-side" },
        state === "unlocked" ? h("span", { className: "gs-achrow-when" }, a.when) : null,
        a.pct != null ? h("span", { className: "gs-achrow-pct" }, tier && state === "unlocked" ? h("span", { className: "gs-tier-dot gs-tier--" + tier, "aria-hidden": "true" }) : null, pctText(a.pct)) : null));
  }

  /* ---------- Version 35: the popup, the band's trophies, the Zenith's diamond (the owner, 3 Oct 2026) ---------- */

  // The popup when an achievement unlocks while you play (ACH-09; the owner: "as someone is playing a game they need to be
  // able to see a popup and a nice sound"). GameSync's own small window in a corner of the screen, over the game, never
  // taking focus or a click. Its look is fixed, dark glass in every theme, as it sits on a game, not on the app.
  // `item` { name, icon, pct, hidden }; `game` its game's name; `progress` { done, total } with it unlocked; `kind`
  // "zenith" for a game's 100%; `phase` "in" or "out" draws a moment of its motion, `corner` the corner it comes from.
  function AchievementPopup(props) {
    var a = props.item || {}, plat = props.kind === "zenith";
    var tier = plat ? "zenith" : tierOf(a.pct);
    var pr = props.progress, pct = pr && pr.total ? Math.floor(100 * pr.done / pr.total) : null;
    // The eyebrow in its metal; the tier's word leads the line under the name, as the player knows the game they're in.
    var eyebrow = plat ? "Zenith reached" : a.hidden ? "Hidden achievement unlocked" : "Achievement unlocked";
    var meta = plat ? "Every achievement" + (pr ? ", " + pr.total + " of " + pr.total : "")
      : a.pct != null ? TIER_WORD[tier] + " · " + rarityWord(a.pct) + " · " + pctText(a.pct) + " of players" : props.game || "";
    return h("div", { className: cx("gs-pop", plat && "gs-pop--zenith", tier && "gs-pop--" + tier, props.phase && "is-" + props.phase,
        "gs-pop--" + (props.corner || "top-right"), props.className), style: props.style, role: "status",
        "aria-label": eyebrow + ": " + (plat ? props.game : a.name) + ". " + meta },
      h("span", { className: "gs-pop-glow", "aria-hidden": "true" }),
      plat ? h(ZenithMedal, { earned: true, size: 76, unfurl: true }) : h(AchievementBadge, { icon: a.icon, name: a.name, tier: tier, size: "md" }),
      h("span", { className: "gs-pop-text" },
        h("span", { className: "gs-pop-eyebrow" }, eyebrow),
        h("span", { className: "gs-pop-name" }, plat ? props.game : a.name),
        h("span", { className: "gs-pop-meta" }, meta)),
      pr && !plat ? h("span", { className: "gs-pop-progress", "aria-hidden": "true" },
        h("span", { className: "gs-pop-count" }, pr.done + " / " + pr.total),
        h("span", { className: "gs-pop-bar" }, h("i", { style: { width: pct + "%" } }))) : null);
  }

  // A trophy in its metal, lit from the top left: the cup, its handles, the stem and the plinth (a 120 × 150 drawing).
  var TROPHY = {
    cup: "M30 14h60v30c0 25-14 42-30 46-16-4-30-21-30-46z",
    handles: "M31 25H16v9c0 14 9 25 22 27 M89 25h15v9c0 14-9 25-22 27",
    stem: "M53 89h14v19H53z",
    base: "M37 108h46l5 12H32z",
    plinth: "M25 120h70a3 3 0 0 1 3 3v15a3 3 0 0 1-3 3H25a3 3 0 0 1-3-3v-15a3 3 0 0 1 3-3z",
    plate: "M44 127h32v7H44z",
    shine: "M43 22c-2 19 2 35 13 47"
  };
  var trophyIds = 0;
  function TrophyFigure(props) {
    var metal = props.metal || "gold", w = props.size || 120;
    var id = useState(function () { trophyIds += 1; return "gs-trophy-" + trophyIds; })[0];
    var fill = "url(#" + id + ")";
    return h("svg", { className: cx("gs-trophy", "gs-trophy--" + metal, props.className), width: w, height: Math.round(w * 1.25), viewBox: "0 0 120 150",
        style: props.style, "aria-hidden": "true" },
      h("defs", null, h("linearGradient", { id: id, x1: "0", y1: "0", x2: "1", y2: "1" },
        h("stop", { offset: "0%", style: { stopColor: "#ffffff" } }),
        h("stop", { offset: "18%", style: { stopColor: "var(--" + metal + ")" } }),
        h("stop", { offset: "100%", style: { stopColor: "var(--" + metal + "-deep)" } }))),
      h("path", { d: TROPHY.handles, fill: "none", stroke: fill, strokeWidth: 7, strokeLinecap: "round" }),
      h("path", { d: TROPHY.cup, fill: fill }),
      h("path", { d: TROPHY.stem, fill: fill }),
      h("path", { d: TROPHY.base, fill: fill }),
      h("path", { d: TROPHY.plinth, fill: fill }),
      h("path", { d: TROPHY.plate, fill: "rgba(27, 33, 41, 0.22)" }),
      h("path", { d: TROPHY.shine, fill: "none", stroke: "rgba(255, 255, 255, 0.6)", strokeWidth: 5, strokeLinecap: "round" }));
  }

  // The tiers' banners rising at the right of the Achievements page's band (the owner: "trophy signs in it, emerging
  // from the right"; banners since version 49): the Zenith's, Everest on red, tallest at the back, Gold's K2 and Silver's
  // Matterhorn either side, Bronze's Fuji in front, in a light of their own. They rise into place as the page opens, one
  // after another, and stay still; with Windows' animation effects off they're simply there. Decoration only: a screen
  // reader hears nothing of it. Version 35 to 48 had cups (TrophyFigure).
  function TrophyRise(props) {
    return h("div", { className: cx("gs-trophies", props.className), "aria-hidden": "true" },
      h("span", { className: "gs-trophies-light" }),
      h(TierBanner, { tier: "silver", size: 118, className: "gs-trophies-silver" }),
      h(TierBanner, { tier: "zenith", size: 166, className: "gs-trophies-zenith" }),
      h(TierBanner, { tier: "gold", size: 138, className: "gs-trophies-gold" }),
      h(TierBanner, { tier: "bronze", size: 100, className: "gs-trophies-bronze" }));
  }

  // The Zeniths monument (version 40; the owner, 3 Oct 2026, of the band's small medal: "I want it to be bigger, take more
  // space. It needs to look mighty, something thats monumental"): since version 45 the Zenith badge large (the owner, 4 Oct
  // 2026, of the Achievements page still showing the mountain and star: "you didn't make zenith changes here"), centred in
  // its box, with light fanning out from behind it all round and a glow, in the metal (its deeper blue in light mode). No
  // Zenith yet: the badge stands faint, without its light. Since version 50 its light moves (the owner, 7 Oct 2026: "Animate the light rays"):
  // the rays turn once a minute, a finer, fainter set between them turns the other way every 90 seconds, so the light
  // shimmers where they cross, and the glow breathes (four seconds each way). Reduced motion keeps it still.
  var monumentIds = 0;
  function ZenithMonument(props) {
    var w = props.width || 132, ht = props.height || 116, earned = !!props.earned;
    var id = useState(function () { monumentIds += 1; return "gs-monument-" + monumentIds; })[0];
    // Since version 49 the Zenith's banner, nearly the box's height, its light gold.
    var bh = Math.round(ht * 0.98), bw = Math.round(bh * 120 / 190), sx = w / 2, sy = ht / 2, reach = Math.max(w, ht) * 0.78, glow = w * 0.46;
    // Twelve rays from the centre: `from` degrees for the first, each `width` degrees either side, `r` long.
    function fan(from, width, r) {
      var d = "";
      for (var i = 0; i < 12; i++) {
        var a = (i * 30 + from) * Math.PI / 180, e = width * Math.PI / 180;
        d += "M" + sx + " " + sy + "L" + (sx + r * Math.cos(a - e)) + " " + (sy + r * Math.sin(a - e)) +
          "L" + (sx + r * Math.cos(a + e)) + " " + (sy + r * Math.sin(a + e)) + "Z";
      }
      return d;
    }
    var about = { transformOrigin: sx + "px " + sy + "px" };
    function light(key, r, inner, middle, at) {
      return h("radialGradient", { id: id + key, cx: sx, cy: sy, r: r, gradientUnits: "userSpaceOnUse" },
        h("stop", { offset: "0", style: { stopColor: "var(--monument-light)", stopOpacity: inner } }),
        h("stop", { offset: String(at), style: { stopColor: "var(--monument-light)", stopOpacity: middle } }),
        h("stop", { offset: "1", style: { stopColor: "var(--monument-light)", stopOpacity: 0 } }));
    }
    return h("span", { className: cx("gs-monument", earned && "is-earned", props.className), style: Object.assign({ width: w, height: ht }, props.style),
        role: "img", "aria-label": props.label || (earned ? "Zeniths" : "No Zenith yet") },
      earned ? h("svg", { className: "gs-monument-light", width: w, height: ht, viewBox: "0 0 " + w + " " + ht, "aria-hidden": "true" },
        h("defs", null, light("r", reach, 0.5, 0.18, 0.5), light("s", reach * 1.1, 0.34, 0.1, 0.5), light("g", glow, 0.45, 0.14, 0.55)),
        h("path", { className: "gs-monument-rays", d: fan(15, 3.4, reach), fill: "url(#" + id + "r)", style: about }),
        h("path", { className: "gs-monument-rays is-fine", d: fan(0, 1.6, reach * 1.1), fill: "url(#" + id + "s)", style: about }),
        h("circle", { className: "gs-monument-glow", cx: sx, cy: sy, r: glow, fill: "url(#" + id + "g)", style: about })) : null,
      h(TierBanner, { tier: "zenith", size: bh, className: "gs-monument-badge", style: { left: (w - bw) / 2, top: (ht - bh) / 2, opacity: earned ? 1 : 0.3 } }));
  }

  /* ---------- AchievementGamesDialog: every game with achievements, each counted or not, its popup on or off (version 41) ---------- */
  // The owner, 4 Oct 2026: "Game in your achievements makes no sense. Also it needs to be either a popup or a new window that
  // shows all the games in the achievement system ... games on steam/epic/official external launchers have their own
  // achievement popups, so those games will have their popups turned off by default on gamesync."
  // games: [{ id, name, art, count ("63 of 156"), zenith, launcher ("Steam", "Epic", "Ubisoft Connect", "EA app", "GOG Galaxy", "Xbox") or null,
  //   counted, popup }]; onCount(id, on), onPopup(id, on), onClose. A game whose launcher shows its own popup starts with GameSync's off.
  function AchievementGamesDialog(props) {
    var q = useState(""), query = q[0].trim().toLowerCase();
    var games = props.games || [];
    var shown = games.filter(function (g) { return !query || g.name.toLowerCase().indexOf(query) >= 0; });
    var counted = games.filter(function (g) { return g.counted; }).length;
    // Version 51 (R13): GameSync draws nothing over a game with an anti-cheat, so its popup is off and can't be turned on.
    var popups = games.filter(function (g) { return g.counted && g.popup && !g.antiCheat; }).length;
    return h("div", { className: "gs-dialog gs-dialog-wide gs-achgames", role: "dialog", "aria-modal": "true", "aria-labelledby": "gs-achgames-title" },
      h("div", { className: "gs-dialog-head" },
        h("div", { className: "gs-card-head" },
          h("div", null, h("h2", { id: "gs-achgames-title", className: "gs-dialog-title" }, "Achievements by game"),
            h("p", { className: "gs-dialog-sub" }, "Every game GameSync reads achievements for on this PC: whether it counts, and whether GameSync shows its popup.")),
          h(IconButton, { icon: "x", label: "Close Achievements by game", onClick: props.onClose })),
        h("div", { className: "gs-achgames-tools" },
          h(SearchField, { placeholder: "Find a game", label: "Find a game", value: q[0], onChange: q[1], style: { flex: 1 } }),
          h("span", { className: "gs-achgames-sum" }, games.length + " games · " + counted + " counted · " + popups + (popups === 1 ? " popup" : " popups")))),
      h("div", { className: "gs-achgames-head", "aria-hidden": "true" },
        h("span", null, "Game"), h("span", null, "Counts"), h("span", null, "GameSync's popup")),
      h("div", { className: "gs-dialog-body gs-achgames-list", role: "list" },
        shown.length ? shown.map(function (g) {
          var line = !g.counted ? "Left out: not on the Achievements page or Home, no popup"
            : g.count + (g.zenith ? " · Zenith" : "") + (g.antiCheat ? " · Has an anti-cheat: GameSync draws nothing over it"
              : g.launcher ? " · " + g.launcher + " shows its own popup" : "");
          return h("div", { key: g.id, className: cx("gs-achgames-row", !g.counted && "is-off"), role: "listitem" },
            h("span", { className: "gs-achgames-game" },
              h("span", { className: "gs-achgames-cover", style: g.art ? { backgroundImage: "url(\"" + g.art + "\")" } : null, "aria-hidden": "true" }),
              h("span", { className: "gs-achgames-text" },
                h("span", { className: "gs-achgames-name" }, g.name, g.zenith ? h(ZenithMedal, { earned: true, size: 18, label: "Zenith" }) : null),
                h("span", { className: "gs-achgames-line" }, line))),
            h(Switch, { label: "Count " + g.name + " in your achievements", checked: g.counted, onChange: function (v) { if (props.onCount) props.onCount(g.id, v); } }),
            h(Switch, { label: "GameSync's popup for " + g.name, checked: g.counted && g.popup && !g.antiCheat, disabled: !g.counted || !!g.antiCheat,
              onChange: function (v) { if (props.onPopup) props.onPopup(g.id, v); } }));
        }) : h("p", { className: "gs-muted", style: { margin: "12px 0" } }, "No game matches “" + q[0].trim() + "”.")),
      h("div", { className: "gs-dialog-foot" },
        h("div", { className: "gs-note" }, h(Icon, { name: "info", size: 16 }),
          "Steam, Epic, Ubisoft Connect, the EA app, GOG Galaxy and Xbox show their own popup, so GameSync's starts off for the games they run. Turn one on to have GameSync's too, and turn the launcher's off in its settings so you don't get two."),
        h("div", { className: "gs-dialog-actions" }, h("span", null), h(Button, { variant: "primary", onClick: props.onClose }, "Done"))));
  }

  /* ---------- LearnModeDialog: where a game saves, from one session watched (version 43; FIND-04, KAN-134) ---------- */
  // Learn mode watches the folders saves usually live in while a game runs, then lists the ones written to. finds:
  // [{ id, path (portable), here (this PC's path), files, bytes, newest, examples: [names], tags: [words], file (one file,
  // not a folder), refused (why GameSync won't take it) }]; picked: ids ticked (the likeliest ticked to begin with);
  // session: "4 Oct, 21:04 to 21:52"; onPick(ids), onSync, onAgain, onNotThese, onAddPlace, onClose; stage "adding".
  function LearnModeDialog(props) {
    var finds = props.finds || [], picked = props.picked || [], adding = props.stage === "adding";
    var takeable = finds.filter(function (f) { return !f.refused; });
    var n = picked.length;
    function toggle(id, on) {
      if (!props.onPick) return;
      props.onPick(on ? picked.concat([id]) : picked.filter(function (x) { return x !== id; }));
    }
    function row(f) {
      var on = picked.indexOf(f.id) >= 0;
      var meta = (f.file ? "One file" : f.files + (f.files === 1 ? " file" : " files")) + " · " + fmt(f.bytes || 0) + " · newest " + f.newest +
        (f.examples && f.examples.length && !f.file ? " · " + f.examples.slice(0, 3).join(", ") : "");
      return h("div", { key: f.id, className: cx("gs-kept-row gs-learn-row", f.refused && "is-refused"), role: "listitem", title: f.here || f.path },
        f.refused ? h(Icon, { name: "lock", size: 16 })
          : h(Checkbox, { checked: on, disabled: adding, onChange: function (v) { toggle(f.id, v); }, label: (on ? "Untick " : "Tick ") + f.path }),
        h("div", { style: { minWidth: 0 } },
          h("div", { className: "gs-kept-name gs-kept-path" }, f.path),
          h("div", { className: "gs-kept-meta gs-learn-meta" }, f.refused ? f.refused : meta)),
        h("span", { className: "gs-learn-tags" }, (f.tags || []).map(function (t) { return h("span", { key: t, className: "gs-tag" }, t); })));
    }
    var none = takeable.length === 0;
    return h("div", { className: "gs-dialog gs-dialog-wide", role: "dialog", "aria-modal": "true", "aria-labelledby": "gs-learn-title" },
      h("div", { className: "gs-dialog-head" },
        h("div", { className: "gs-card-head" },
          h("div", null, h("h2", { id: "gs-learn-title", className: "gs-dialog-title" }, "Where " + props.game + " saves"),
            h("p", { className: "gs-dialog-sub" }, none
              ? "Learn mode watched you play on " + props.session + " and saw nothing written that looks like a save."
              : "Learn mode watched you play on " + props.session + ". These are the places it wrote to while it ran.")),
          h(IconButton, { icon: "x", label: "Close Where " + props.game + " saves", onClick: props.onClose }))),
      h("div", { className: "gs-dialog-body gs-dialog-roomy" },
        none ? h("div", { className: "gs-note" }, h(Icon, { name: "search", size: 16 }),
            "It watches again the next time you play, in case it saves later in a game. If you know where its saves are, Add a place… sets them.")
          : h("div", { className: "gs-kept-list is-long", role: "list", "aria-label": "Places " + props.game + " wrote to" }, finds.map(row)),
        none ? null : h("div", { className: "gs-note" }, h(Icon, { name: "info", size: 16 }),
          "Left out: logs, caches, crash dumps and program files written then, and folders GameSync never takes. A place can be one file when it sits loose in a bigger folder."),
        props.overflowed ? h("div", { className: "gs-import-note gs-import-warn", role: "status" }, h(Icon, { name: "alert", size: 14, strokeWidth: 2 }),
          "Windows wrote faster than GameSync could keep up for a moment, so this list may miss a place. Watch again to be sure.") : null),
      h("div", { className: "gs-dialog-foot" },
        none ? null : h("div", { className: "gs-note" }, h(Icon, { name: "shield", size: 16 }),
          "Nothing syncs until you choose. Sync these saves backs up the ticked places and keeps them in step on your PCs; the rest stay as they are."),
        h("div", { className: "gs-dialog-actions" },
          h("div", { className: "gs-row" },
            h(Button, { variant: "ghost", disabled: adding, onClick: props.onNotThese, title: "Forgets what it found and stops watching " + props.game + "; Add a place… still sets where its saves are" }, none ? "Stop watching" : "Not these"),
            h(Button, { variant: "ghost", icon: "search", disabled: adding, onClick: props.onAgain, title: "Watches the next time you play, and adds what it finds then" }, "Watch again")),
          none ? h("div", { className: "gs-row" },
              h(Button, { variant: "secondary", icon: "plus", "aria-haspopup": "dialog", onClick: props.onAddPlace }, "Add a place…"),
              h(Button, { variant: "primary", onClick: props.onClose }, "Done"))
            : h(Button, { variant: "primary", icon: "sync", disabled: !n || adding, busy: adding, onClick: props.onSync,
                title: n ? null : "Tick a place first" }, adding ? "Syncing" : n > 1 ? "Sync these " + n + " places" : "Sync these saves"))));
  }

  var api = {
    Icon: Icon, Button: Button, IconButton: IconButton, PillTabs: PillTabs, SideRail: SideRail, Card: Card, HeroBanner: HeroBanner,
    StatusBadge: StatusBadge, GameTile: GameTile, SearchField: SearchField, Menu: Menu, GameList: GameList,
    ProgressBar: ProgressBar, JobProgress: JobProgress, BusyLine: BusyLine, Spinner: Spinner, ActivityGrid: ActivityGrid, Checkbox: Checkbox, Switch: Switch,
    ConsoleTable: ConsoleTable, ConsoleLog: ConsoleLog, ShareSavesDialog: ShareSavesDialog, ImportSavesDialog: ImportSavesDialog,
    SettingsNav: SettingsNav, SettingsRow: SettingsRow, FolderField: FolderField, FolderList: FolderList,
    ThemeScope: ThemeScope, Backdrop: Backdrop, ThemePicker: ThemePicker, ColorSwatchPicker: ColorSwatchPicker,
    PlayBar: PlayBar, Facts: Facts, Select: Select, FileTree: FileTree, GamePropertiesDialog: GamePropertiesDialog,
    AddPlaceDialog: AddPlaceDialog, NamedSaveDialog: NamedSaveDialog, AddGameDialog: AddGameDialog, ImportKeptSavesDialog: ImportKeptSavesDialog, KeptCopiesDialog: KeptCopiesDialog,
    ProgressRing: ProgressRing, AchievementBadge: AchievementBadge, TierChip: TierChip, ZenithMedal: ZenithMedal, TierBanner: TierBanner, AchievementsOverview: AchievementsOverview,
    AchievementSpotlight: AchievementSpotlight, AchievementRow: AchievementRow, AchievementPopup: AchievementPopup, TrophyFigure: TrophyFigure, TrophyRise: TrophyRise,
    ZenithBadge: ZenithBadge, ZenithScene: ZenithScene, ZenithMonument: ZenithMonument, AchievementGamesDialog: AchievementGamesDialog, LearnModeDialog: LearnModeDialog,
    achievements: { tierOf: tierOf, rarityWord: rarityWord, percent: pctText },
    fileTree: { toggle: treeToggle, count: treeCount },
    surface: { get: getSurface, set: setSurface, use: useSurface },
    formatBytes: fmt, theme: Theme
  };
  window.GameSync = Object.assign(window.GameSync || {}, api);
})();
