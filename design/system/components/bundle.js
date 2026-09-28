/* @ds-bundle: {"format":4,"namespace":"GameSync","components":[{"name":"Icon"},{"name":"Button"},{"name":"IconButton"},{"name":"PillTabs"},{"name":"SideRail"},{"name":"Card"},{"name":"HeroBanner"},{"name":"StatusBadge"},{"name":"GameTile"},{"name":"ProgressBar"},{"name":"ActivityGrid"},{"name":"Checkbox"},{"name":"Switch"},{"name":"ConsoleTable"},{"name":"ConsoleLog"},{"name":"ShareSavesDialog"},{"name":"ImportSavesDialog"},{"name":"SettingsNav"},{"name":"SettingsRow"},{"name":"FolderField"},{"name":"FolderList"},{"name":"ThemeScope"},{"name":"ThemePicker"},{"name":"ColorSwatchPicker"}]} */
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
      dark: { ok: "#7fe6f2", "ok-soft": "#0f3035", warn: "#f2b544", "warn-soft": "#2a2213", danger: "#ff8a7d", "danger-soft": "#2e1917", play: "#b9a3ff", "play-soft": "#231d38", neutral: "#9aa1a9" },
      light: { ok: "#006b77", "ok-soft": "#d8f3f6", warn: "#855600", "warn-soft": "#fbeed3", danger: "#b3261e", "danger-soft": "#fde4e1", play: "#6547d1", "play-soft": "#ece7ff", neutral: "#58616b" }
    };
    var FIXED = { "on-art": "#f5f7f9", "art-scrim": "rgba(5, 6, 8, 0.72)", glass: "rgba(12, 14, 17, 0.58)", "glass-edge": "rgba(255, 255, 255, 0.16)" };

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
        var tl = Math.min(ts * 1.6, 28);
        v["bg-000"] = hsl(th, tl, 92.6);
        v["bg-100"] = hsl(th, tl, 95.6);
        v["bg-200"] = hsl(th, tl * 0.6, 99.6);
        v["bg-300"] = hsl(th, tl, 91.8);
        v["bg-400"] = hsl(th, tl * 0.9, 86.8);
        v["line-100"] = hsl(th, tl, 87.5);
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
      // The surfaces Glossy makes see-through (glass() below). Solid has no edges, lift or ring: its surfaces are the plain
      // ones, and its edges are clear, so a hovered control shows no ring (the background runs under the border, as in CSS).
      var clear = rgba("#000000", 0);
      ["edge-card", "edge-control", "edge-console", "edge-well", "edge-dialog", "edge-art"].forEach(function (k) { v[k] = clear; });
      v["lift-card"] = "none";
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
      return v;
    }

    /* Glossy (LOOK-17, LOOK-18): what a page's surfaces become so a blurred, darkened copy of a game's art shows
       through, in three strengths: "glass" on game detail, conflict and the save manager; "home", a step more solid;
       "glow" on first run and settings, a soft glow at the top over nearly solid cards. Returns the tokens to lay over
       build()'s and the backdrop: its base colour, then the art, then the scrim colour at alpha stops [position,
       alpha] from top to bottom. Dark mode only, and never with pure black: then it's null and the page stays Solid.
       The app darkens bright art further, until text keeps 4.5:1 on every surface. */
    var STRENGTHS = ["glass", "home", "glow"];
    function glass(opts, strength) {
      opts = opts || {};
      if (opts.mode === "light" || opts.pureBlack) return null;
      var p = preset(opts.preset || "arcade");
      var accent = opts.accent || "#0078d4";
      var th = p.dynamic ? hueOf(accent) : p.tint[0];
      var ts = p.dynamic ? 10 : p.tint[1];
      var v = build(opts);
      function white(a) { return "rgba(255, 255, 255, " + a + ")"; }
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
    logo: ["M3 3h12v12H3z", "M9 9h12v12H9z"]
  };
  function Icon(props) {
    var name = props.name, size = props.size || 20;
    var d = P[name] || P.info;
    var filled = name === "play";
    return h("svg", {
      className: cx("gs-icon", props.className), width: size, height: size, viewBox: "0 0 24 24",
      fill: filled ? "currentColor" : "none", stroke: "currentColor", strokeWidth: props.strokeWidth || 1.75,
      strokeLinecap: "round", strokeLinejoin: "round", "aria-hidden": props.label ? undefined : "true",
      role: props.label ? "img" : undefined, "aria-label": props.label
    }, d.map(function (p, i) { return h("path", { key: i, d: p }); }));
  }
  Icon.names = Object.keys(P);

  /* ---------- Button ---------- */
  function Button(props) {
    var variant = props.variant || "secondary";
    var rest = Object.assign({}, props);
    ["variant", "icon", "size", "className", "children", "iconAfter"].forEach(function (k) { delete rest[k]; });
    return h("button", Object.assign({ type: "button" }, rest, {
      className: cx("gs-btn", "gs-btn-" + variant, props.size === "sm" && "gs-btn-sm", props.className)
    }),
      props.icon ? h(Icon, { name: props.icon, size: props.size === "sm" ? 16 : 18 }) : null,
      props.children,
      props.iconAfter ? h(Icon, { name: props.iconAfter, size: 16 }) : null);
  }

  function IconButton(props) {
    var rest = Object.assign({}, props);
    ["icon", "label", "glass", "className", "badge", "size"].forEach(function (k) { delete rest[k]; });
    return h("button", Object.assign({ type: "button", "aria-label": props.label, title: props.label }, rest, {
      className: cx("gs-iconbtn", props.glass && "gs-iconbtn-glass", props.size === "sm" && "gs-iconbtn-sm", props.className)
    }), h(Icon, { name: props.icon, size: props.size === "sm" ? 16 : 18 }));
  }

  /* ---------- PillTabs ---------- */
  function PillTabs(props) {
    var items = props.items || [];
    var st = useState(props.value != null ? props.value : (items[0] && items[0].id));
    var value = props.value != null ? props.value : st[0];
    function pick(id) { st[1](id); if (props.onChange) props.onChange(id); }
    return h("div", { className: "gs-tabs", role: "tablist", "aria-label": props.label },
      items.map(function (it) {
        return h("button", {
          key: it.id, type: "button", role: "tab", className: "gs-tab",
          "aria-selected": String(it.id === value), onClick: function () { pick(it.id); }
        }, it.icon ? h(Icon, { name: it.icon, size: 16 }) : null, it.label,
          it.count != null ? h("span", { className: "gs-tab-count" }, it.count) : null);
      }));
  }

  /* ---------- SideRail ---------- */
  function SideRail(props) {
    var items = props.items || [];
    var hov = useState(null);
    function btn(it) {
      var full = it.dotLabel ? it.label + ": " + it.dotLabel : it.label;
      return h("button", {
        key: it.id, type: "button", className: "gs-rail-btn", "aria-label": full,
        "aria-current": it.id === props.current ? "page" : undefined,
        onMouseEnter: function () { hov[1](it.id); }, onMouseLeave: function () { hov[1](null); },
        onFocus: function () { hov[1](it.id); }, onBlur: function () { hov[1](null); },
        onClick: function () { if (props.onNavigate) props.onNavigate(it.id); }
      }, h(Icon, { name: it.icon, size: 20 }),
        it.dot ? h("span", { className: "gs-dot", style: { background: "var(--" + it.dot + ")" } }) : null,
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
  function Card(props) {
    return h("section", { className: cx("gs-card", props.className), style: props.style, "aria-label": props.title },
      (props.title || props.action) ? h("div", { className: "gs-card-head" },
        props.title ? h("div", null, h("h3", { className: "gs-card-title" }, props.title),
          props.subtitle ? h("p", { className: "gs-card-sub" }, props.subtitle) : null) : h("span"),
        props.action || null) : null,
      props.children);
  }

  /* ---------- StatusBadge ---------- */
  var STATUS = {
    synced: ["ok", "check", "Synced"],
    playing: ["play", "play", "Playing"],
    "upload-pending": ["ok", "upload", "Upload pending"],
    "newer-in-cloud": ["ok", "download", "Newer in cloud"],
    conflict: ["warn", "alert", "Conflict"],
    held: ["warn", "pause", "Held for review"],
    "in-use": ["warn", "lock", "Files in use"],
    "not-found": ["warn", "search", "Saves not found"],
    "not-available": ["neutral", "unplug", "Not available"],
    blocked: ["danger", "block", "Blocked"],
    "backup-only": ["neutral", "archive", "Synced by its store"]
  };
  function StatusBadge(props) {
    var s = STATUS[props.status] || STATUS.synced;
    return h("span", { className: cx("gs-status", "gs-status-" + s[0], props.plain && "gs-status-plain", props.className) },
      h(Icon, { name: s[1], size: 14, strokeWidth: 2 }), props.label || s[2]);
  }
  StatusBadge.statuses = Object.keys(STATUS);

  /* ---------- HeroBanner ---------- */
  function HeroBanner(props) {
    var bg = props.art ? { backgroundImage: "url(" + props.art + ")" } : null;
    return h("section", { className: cx("gs-hero", !props.art && "gs-hero-empty"), style: Object.assign({}, bg, props.style), "aria-label": props.title },
      h("div", { className: "gs-hero-top" }, props.eyebrow),
      h("div", { className: "gs-hero-bottom" },
        h("div", { className: "gs-hero-text" },
          props.chip ? h("span", { className: "gs-chip" }, h(Icon, { name: "clock", size: 14 }), props.chip) : null,
          props.logo ? h("img", { className: "gs-hero-logo", src: props.logo, alt: "" }) : null,
          h("h2", { className: cx("gs-hero-title", props.logo && "gs-sr-only") }, props.title),
          props.status ? h("div", null, h(StatusBadge, { status: props.status, label: props.statusLabel })) : null,
          props.blurb ? h("p", { className: "gs-hero-blurb" }, props.blurb) : null),
        h("div", { className: "gs-hero-actions" },
          h(IconButton, { icon: "saves", label: "Manage saves", glass: true, onClick: props.onSaves }),
          h(IconButton, { icon: "settings", label: "Game settings", glass: true, onClick: props.onSettings }),
          h(Button, { variant: "primary", icon: "play", onClick: props.onPlay }, props.playLabel || "Continue playing"))));
  }

  /* ---------- GameTile ---------- */
  function GameTile(props) {
    var bg = props.art ? { backgroundImage: "url(" + props.art + ")" } : null;
    return h("button", { type: "button", className: "gs-tile", style: props.width ? { width: props.width } : null, onClick: props.onClick, "aria-label": props.name + (props.status ? ", " + (STATUS[props.status] || [])[2] : "") },
      h("div", { className: cx("gs-tile-art", !props.art && "gs-title-cover"), style: bg, "data-initial": props.art ? undefined : initialOf(props.name) },
        !props.art ? h("span", { className: "gs-title-cover-name", "aria-hidden": "true" }, props.name) : null,
        props.status && props.status !== "synced" ? h(StatusBadge, { status: props.status }) : null),
      h("div", { className: "gs-tile-name" }, props.name),
      props.meta ? h("div", { className: "gs-tile-meta" }, props.meta) : null);
  }

  /* ---------- ProgressBar ---------- */
  function ProgressBar(props) {
    var pct = Math.max(0, Math.min(100, props.value || 0));
    return h("div", { className: "gs-progress" },
      (props.label || props.right) ? h("div", { className: "gs-progress-label" }, h("span", null, props.label), h("span", null, props.right != null ? props.right : Math.round(pct) + "%")) : null,
      h("div", { className: "gs-progress-track", role: "progressbar", "aria-valuemin": 0, "aria-valuemax": 100, "aria-valuenow": Math.round(pct), "aria-label": props.ariaLabel || props.label },
        h("div", { className: "gs-progress-fill", style: { width: pct + "%" } })));
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
        h("tbody", null, rows.map(function (r) {
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
    return h("div", { className: cx("gs-log", props.grow && "gs-log-grow"), role: "log", "aria-label": props.label || "Activity log", style: props.height ? { maxHeight: props.height } : null },
      lines.map(function (l, i) {
        return h("div", { key: i, className: "gs-log-line gs-log-" + (l.level || "info") },
          h("time", null, l.time), h("span", { className: "gs-log-lvl" }, (l.tag || l.level || "info").toUpperCase()), h("span", { className: "gs-log-msg" }, l.msg));
      }),
      props.live ? h("div", { className: "gs-log-line" }, h("time", null, ""), h("span", { className: "gs-log-lvl gs-console-prompt" }, ">"), h("span", null, h("span", { className: "gs-log-cursor" }))) : null);
  }

  /* ---------- sizes ---------- */
  function fmt(bytes) {
    if (bytes >= 1073741824) return (bytes / 1073741824).toFixed(2) + " GB";
    if (bytes >= 1048576) return (bytes / 1048576).toFixed(1) + " MB";
    return Math.max(1, Math.round(bytes / 1024)) + " KB";
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
    items.forEach(function (it) { if (it.match !== "unknown") init[it.id] = true; });
    var ps = useState(init), picked = ps[0];
    var st = useState(props.stage || "review"), stage = st[0];
    var chosen = items.filter(function (it) { return picked[it.id] && it.match !== "unknown"; });
    var versions = chosen.reduce(function (n, it) { return n + (it.versions || 1); }, 0);
    var bytes = chosen.reduce(function (n, it) { return n + (it.size || 0); }, 0);
    function toggle(it, on) { if (it.match === "unknown") return; var n = Object.assign({}, picked); if (on) n[it.id] = true; else delete n[it.id]; ps[1](n); }
    var rows = items.map(function (it) {
      var disabled = it.match === "unknown";
      var on = !!picked[it.id] && !disabled;
      var line = it.match === "matched" ? (it.versions || 1) + ((it.versions || 1) === 1 ? " version" : " versions") + " · added as pinned, not current"
        : it.match === "not-installed" ? "Not installed here · waits until the game is found"
        : "Not in your library · can't be imported";
      return h("div", { key: it.id, className: cx("gs-share-item", on && "is-on", disabled && "is-blocked"), onClick: function () { toggle(it, !on); } },
        h(Checkbox, { label: "Import " + it.name, checked: on, disabled: disabled, onChange: function (v) { toggle(it, v); } }),
        h("div", { className: cx("gs-share-cover", !it.art && "gs-cover-empty"), style: it.art ? { backgroundImage: "url(" + it.art + ")" } : null, "aria-hidden": "true" }, it.art ? null : initialOf(it.name)),
        h("div", null,
          h("div", { className: "gs-share-name" }, it.name),
          h("div", { className: "gs-share-meta" }, line),
          it.warn ? h("div", { className: "gs-import-note gs-import-warn" }, h(Icon, { name: "alert", size: 14, strokeWidth: 2 }), it.warn) : null,
          it.removed ? h("div", { className: "gs-import-note gs-import-removed" }, h(Icon, { name: "block", size: 14, strokeWidth: 2 }), it.removed) : null),
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
          h(Button, { size: "sm", variant: "secondary", onClick: props.onChange, disabled: state === "moving" }, props.changeLabel || "Change…"),
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
    var glass = surface === "glossy" && props.art ? Theme.glass(theme, props.strength || "home") : null;
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

  var api = {
    Icon: Icon, Button: Button, IconButton: IconButton, PillTabs: PillTabs, SideRail: SideRail, Card: Card, HeroBanner: HeroBanner,
    StatusBadge: StatusBadge, GameTile: GameTile, ProgressBar: ProgressBar, ActivityGrid: ActivityGrid, Checkbox: Checkbox, Switch: Switch,
    ConsoleTable: ConsoleTable, ConsoleLog: ConsoleLog, ShareSavesDialog: ShareSavesDialog, ImportSavesDialog: ImportSavesDialog,
    SettingsNav: SettingsNav, SettingsRow: SettingsRow, FolderField: FolderField, FolderList: FolderList,
    ThemeScope: ThemeScope, Backdrop: Backdrop, ThemePicker: ThemePicker, ColorSwatchPicker: ColorSwatchPicker,
    surface: { get: getSurface, set: setSurface, use: useSurface },
    formatBytes: fmt, theme: Theme
  };
  window.GameSync = Object.assign(window.GameSync || {}, api);
})();
