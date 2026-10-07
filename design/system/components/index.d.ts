// GameSync design system — window.GameSync (React 18, classic script)
import type { ReactNode, ButtonHTMLAttributes, CSSProperties } from "react";

export type IconName =
  | "home" | "library" | "search" | "saves" | "terminal" | "activity" | "settings" | "play" | "sync"
  | "share" | "zip" | "folder" | "cloud" | "upload" | "download" | "check" | "alert" | "pause" | "lock"
  | "unplug" | "block" | "archive" | "pin" | "clock" | "chevronRight" | "chevronDown" | "chevronsRight"
  | "x" | "info" | "copy" | "monitor" | "shield" | "palette" | "drive" | "bell" | "plus" | "sun" | "moon"
  | "pencil" | "reset" | "star" | "sort" | "chevronLeft" | "more" | "logo" | "arrowLeft" | "trophy" | "external" | "file" | "key";

export type Status =
  | "synced" | "playing" | "upload-pending" | "newer-in-cloud" | "conflict" | "held"
  | "in-use" | "not-found" | "not-available" | "blocked" | "backup-only" | "not-syncing";

/** Outlined 24px icon. `play` is always filled; `filled` fills the star, for a favourite. */
export function Icon(props: { name: IconName; size?: number; strokeWidth?: number; label?: string; filled?: boolean; className?: string }): JSX.Element;

/**
 * Pill button. One primary per view. `busy`: it's doing its job: it keeps its colour, a spinner takes the icon's place,
 * `busyLabel` ("Syncing") replaces its words with dots that count up after them, and clicks do nothing (KAN-80).
 */
export function Button(props: ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: "primary" | "secondary" | "ghost" | "danger";
  size?: "sm";
  icon?: IconName;
  iconAfter?: IconName;
  busy?: boolean;
  busyLabel?: string;
  children?: ReactNode;
}): JSX.Element;

/**
 * 40px round icon button; label is required. `pressed` makes it a toggle (the favourite star, filled while on). `busy`
 * turns its icon (Sync now's arrows while GameSync syncs), and `busyLabel` ("Syncing") is its tooltip meanwhile.
 */
export function IconButton(props: ButtonHTMLAttributes<HTMLButtonElement> & { icon: IconName; label: string; glass?: boolean; size?: "sm"; pressed?: boolean; busy?: boolean; busyLabel?: string }): JSX.Element;

/** A ring with a quarter lit, turning once a second (still with reduced motion). Buttons, JobProgress and BusyLine use it. */
export function Spinner(props: { size?: number; strokeWidth?: number; className?: string }): JSX.Element;

/** A wait with nothing to count: a spinner and its words ("Looking at what's there"), with dots that count up after them. */
export function BusyLine(props: { text: string; className?: string }): JSX.Element;

/** Pill tabs that switch a view in place. */
export function PillTabs(props: {
  /** tone "warn": Conflicts (Needs you until version 41) while something needs the person, in the warn colours with a caution mark. */
  items: { id: string; label: string; icon?: IconName; count?: number; tone?: "warn" }[];
  value?: string;
  onChange?: (id: string) => void;
  label?: string;
}): JSX.Element;

/** Collapsed navigation rail. `dot` is a status token name; `dotLabel` says what it means (tooltip and screen readers). A `warn` dot is a caution mark, with the icon in warn. */
export function SideRail(props: {
  items: { id: string; icon: IconName; label: string; sep?: boolean; bottom?: boolean; dot?: "play" | "warn" | "ok" | "danger"; dotLabel?: string }[];
  current?: string;
  onNavigate?: (id: string) => void;
}): JSX.Element;

/** Rounded bg-200 panel. */
/** `lead` goes before the title (the Zenith's mark); `label` names a card whose title isn't text. */
export function Card(props: { title?: string | ReactNode; subtitle?: string; action?: ReactNode; lead?: ReactNode; label?: string; children?: ReactNode; className?: string; style?: CSSProperties }): JSX.Element;

/** Last-played banner with art, status and Continue playing. `art`: Steam's library hero (1920x620 or 3840x1240). `logo`: the game's transparent logo (Steam's logo.png), shown instead of the text title, which stays for screen readers. `actions` replaces Manage saves and Properties (a game's page: the favourite star and More); `noPlay` leaves Play out (a game not installed here). */
export function HeroBanner(props: {
  art?: string; logo?: string; title: string; eyebrow?: string; chip?: string; blurb?: string;
  status?: Status; statusLabel?: string; playLabel?: string; playIcon?: IconName; noPlay?: boolean; actions?: ReactNode; style?: CSSProperties;
  onPlay?: () => void; onSaves?: () => void; onSettings?: () => void;
  /** Home (version 37): the games the banner can show, at its top right; the wheel and Left and Right move between them too. */
  pager?: { count: number; index: number; names?: string[]; onPick: (index: number) => void; onPrev: () => void; onNext: () => void };
  /** Home (version 37): a click on the banner, anywhere but its buttons, or Enter with it focused, opens the game's page. */
  onOpen?: () => void;
}): JSX.Element;

/** One game status as icon + word. On cover art (a tile, the hero) it takes a deep tint that reads over any art. `backup-only` reads "Synced by its store": pass `label` "Synced by Steam" (or Epic, Xbox) when the store is known. */
/** Synced shows a cloud with a check, and Synced by its store (or Backed up) a shield with a check (version 35): never a bare tick, which could mean anything. */
export function StatusBadge(props: { status: Status; label?: string; plain?: boolean; className?: string }): JSX.Element;

/** 2:3 cover tile (Steam's 600x900 library capsule). No art: a title cover with the game's name. No badge when synced. `statusLabel` names the store for a game its store syncs ("Synced by Steam"). */
/** A game whose saves are fine (Synced, or Backed up / Synced by its store) gets a round mark on its art, a cloud with a check or a shield with a check, its words in its tooltip; any other status shows its badge. */
/** `achievements` (version 39, ACH-02): how many are unlocked of how many; a ring at the cover's bottom right once one is, the Zenith medal once every one is. */
export function GameTile(props: { name: string; art?: string; status?: Status; statusLabel?: string; meta?: string; width?: number | string; achievements?: { done: number; total: number }; onClick?: () => void }): JSX.Element;

/** A text field that filters as you type, with its search icon. Esc clears it; Enter calls `onSubmit` (open the first match). `hint` shows the shortcut that focuses it while it's empty. */
export function SearchField(props: {
  value?: string; onChange?: (value: string) => void; onSubmit?: (value: string) => void;
  placeholder?: string; label?: string; hint?: string; inputRef?: unknown; className?: string; style?: CSSProperties;
}): JSX.Element;

export interface MenuItem { id?: string; label?: string; icon?: IconName; filled?: boolean; hint?: string; checked?: boolean; disabled?: boolean; sep?: boolean; heading?: string }
/** A short list of choices over the page: a sort (items with `checked`, one of them true) or a game's right-click menu (items with icons). `sep` and `heading` items divide it. */
export function Menu(props: { items: MenuItem[]; onPick?: (id: string) => void; label?: string; className?: string; style?: CSSProperties }): JSX.Element;

export interface GameListGame { id: string; name: string; art?: string; status?: Status; statusLabel?: string; installed?: boolean }
/** Every game by name, in groups (Favourites, then the rest), like Steam's list beside the covers. A row shows the small cover (Steam's 300x450 capsule) and the name, and the status under it only when it needs the person or the game is running; games not installed on this PC are dimmed. `selected` is the game whose page is open; `collapsed` is where a group starts, and its heading opens and closes it. */
export function GameList(props: {
  groups: { id: string; label: string; games: GameListGame[]; collapsed?: boolean }[];
  selected?: string; onSelect?: (id: string) => void; onToggle?: (groupId: string) => void;
  onMenu?: (game: GameListGame, event: MouseEvent) => void; label?: string; empty?: ReactNode;
}): JSX.Element;

/** No `value`: how much there is isn't known yet, and a short stripe slides along the track. `tone` colours the fill. */
export function ProgressBar(props: { value?: number | null; label?: string; right?: ReactNode; ariaLabel?: string; tone?: "ok" | "warn" | "neutral" }): JSX.Element;

/**
 * A job under way, where its result will show (KAN-80): a spinner and what it's doing, a bar, how far it is (`detail`),
 * how fast (`speed`) and how long is left (`left`). No `value` while how much there is isn't known yet. `state` paused,
 * done or failed stops it, with `note` saying why or how it ended. `boxed` sets it on a card of its own, as on a game's
 * saves; in a dialog it sits in the foot.
 */
export function JobProgress(props: {
  title: string; value?: number | null; detail?: string; speed?: string; left?: string;
  state?: "running" | "paused" | "done" | "failed"; note?: string; boxed?: boolean; className?: string;
}): JSX.Element;

/** Play-time calendar. Levels: 0 none, 1 under 2h, 2 2–4h, 3 over 4h. startWeekday 0 = Monday. */
export function ActivityGrid(props: { days: (0 | 1 | 2 | 3)[]; startWeekday?: number }): JSX.Element;

export function Checkbox(props: { checked?: boolean; indeterminate?: boolean; disabled?: boolean; onChange?: (checked: boolean) => void; label?: string; showLabel?: boolean; className?: string }): JSX.Element;

/** On/off switch for settings that apply straight away. */
export function Switch(props: { checked?: boolean; onChange?: (checked: boolean) => void; label: string; disabled?: boolean; className?: string }): JSX.Element;

export interface ConsoleColumn<R> { key: string; label: string; align?: "right"; path?: boolean; render?: (row: R) => ReactNode }

/** Dense mono table for the save manager. Pass selected + onSelect to make rows selectable. */
export function ConsoleTable<R extends { id: string; name?: string }>(props: {
  columns: ConsoleColumn<R>[];
  rows: R[];
  selected?: string[];
  onSelect?: (ids: string[]) => void;
  onRowClick?: (row: R) => void;
  command?: string;
  toolbar?: ReactNode;
  /** Said in the table's one row while it has none ("No versions yet…"). */
  empty?: ReactNode;
}): JSX.Element;

export function ConsoleLog(props: {
  lines: { time: string; level?: "ok" | "info" | "warn" | "error"; tag?: string; msg: string }[];
  live?: boolean; grow?: boolean; height?: number | string; label?: string;
  /** A day and a time in each line's time ("27 Sep 12:00:31"): the time column widens for them. */
  dated?: boolean;
}): JSX.Element;

export interface ShareVersion { id: string; label: string; when: string; pc?: string; size: number; pinned?: boolean }
/** `blocked`: the reason a game can't be shared (anti-cheat or online mode); it shows locked and Share all leaves it out. */
export interface ShareGame { id: string; name: string; art?: string; size: number; totalSize?: number; versionCount?: number; meta?: string; blocked?: string; versions: ShareVersion[] }

/** Package picked saves, or the whole save folder, into one zip. */
export function ShareSavesDialog(props: {
  games: ShareGame[];
  mode?: "pick" | "all";
  initialSelection?: string[];
  expanded?: string[];
  saveRoot?: string;
  outDir?: string;
  date?: string;
  latestOnly?: boolean;
  onCreate?: (req: { mode: "all"; latestOnly: boolean } | { mode: "pick"; selection: Record<string, string[]> }) => void;
  onClose?: () => void;
}): JSX.Element;

export interface ImportItem {
  id: string; name: string; art?: string; versions?: number; size?: number;
  /** matched: added as pinned versions; not-installed: waits for the game; unknown: can't be imported. */
  match: "matched" | "not-installed" | "unknown";
  /** A warning shown under the game, e.g. a save made on another account. */
  warn?: string;
  /** Files left out by the safety checks, e.g. "1 program file (launcher.dll) was left out." */
  removed?: string;
  /** Version 51 (R16): why it can't be imported: a game with an anti-cheat, or that plays only online. Can't be ticked. */
  blocked?: string;
}

/** Bring saves from a shared zip in as pinned versions; never replaces current saves. */
export function ImportSavesDialog(props: { zipName?: string; from?: string; items: ImportItem[]; stage?: "review" | "done"; onImport?: (ids: string[]) => void; onClose?: () => void }): JSX.Element;

/** Left column of the Settings screen. `badge` flags a section that needs the person. */
export function SettingsNav(props: { items: { id: string; label: string; icon: IconName; badge?: string }[]; current?: string; onChange?: (id: string) => void; label?: string }): JSX.Element;

/** One setting: title and description on the left, the control (children) on the right. */
export function SettingsRow(props: { title: ReactNode; description?: ReactNode; tag?: string; disabled?: boolean; stack?: boolean; children?: ReactNode }): JSX.Element;

/** A folder the person can change. `state`: ok, moving (with progress), missing (drive unplugged) or refused (not allowed, with the reason). `fixed`: a folder shown, not changed here, with Open in Explorer only. */
export function FolderField(props: {
  path: string; label?: string; meta?: string; icon?: IconName; fixed?: boolean;
  state?: "ok" | "moving" | "missing" | "refused"; message?: string; progress?: number;
  changeLabel?: string; onChange?: () => void; onOpen?: () => void;
}): JSX.Element;

/** Folders the person added, each removable. */
export function FolderList(props: { items: { path: string; meta?: string; tag?: string; icon?: IconName }[]; onRemove?: (item: object, index: number) => void; onAdd?: () => void; addLabel?: string; empty?: string }): JSX.Element;

/** The four theme choices. primary / secondary: a swatch id or, later, any hex code. accent: the Windows accent colour. */
export interface ThemeChoice { preset?: PresetId; mode?: "dark" | "light"; pureBlack?: boolean; primary?: string | null; secondary?: string | null; accent?: string }
export type PresetId = "arcade" | "moss" | "tidal" | "sakura" | "citrus" | "mono" | "windows";
export type SwatchId = "cyan" | "aqua" | "sky" | "blue" | "green" | "mint" | "lime" | "pink" | "steel" | "grey" | "white";

export type Surface = "glossy" | "solid";
/** How much of Glossy's backdrop a page lets through: glass (game detail, conflict, the save manager and its tabs, the console), home (Home and the library), glow (first run, settings). */
export type Strength = "glass" | "home" | "glow";

/** Sets every colour token for its children: `theme`, or the page's theme, followed live. With `art` and the Glossy surface (`surface`, else the one picked in Settings; Glossy by default), the page sits on a Backdrop of that art at `strength`'s see-through surfaces; light mode and pure black stay Solid. */
export function ThemeScope(props: { theme?: ThemeChoice; surface?: Surface; strength?: Strength; art?: string; className?: string; style?: CSSProperties; children?: ReactNode }): JSX.Element;

/** The art blurred behind a Glossy page (64px, saturated 1.3, reaching 140px past the edges), under its strength's scrim. ThemeScope draws it; `backdrop` comes from theme.glass(). */
export function Backdrop(props: { art: string; backdrop: GlassBackdrop }): JSX.Element;

export interface GlassBackdrop { base: string; scrim: string; stops: [number, number][] }

/** The Surface choice, one for every screen, as the Settings screen picks it: kept in this browser, and followed live by every ThemeScope. */
export const surface: { get(): Surface; set(value: Surface): void; use(): Surface };

/** Preset cards with a live mini preview each, in the current mode. */
export function ThemePicker(props: { value: PresetId; onChange?: (id: PresetId) => void; mode?: "dark" | "light"; pureBlack?: boolean; accent?: string; label?: string }): JSX.Element;

/** Swatches for one theme colour, shown in the tone they take in this mode. `allowCustom` (later) adds a hex field. */
export function ColorSwatchPicker(props: {
  role: "primary" | "secondary"; value?: SwatchId | string; onChange?: (value: SwatchId | string) => void;
  theme?: ThemeChoice; allowCustom?: boolean; customOpen?: boolean; customValue?: string; label?: string;
}): JSX.Element;

/** The theme engine. build() returns every colour token (and shadow-dialog) as CSS values, keyed by token name. */
export const theme: {
  presets: { id: PresetId; name: string; primary: SwatchId | null; secondary: SwatchId | null; tint: [number, number] | null; dynamic?: boolean; note: string }[];
  swatches: { id: SwatchId; name: string; hex: string }[];
  build(choice: ThemeChoice): Record<string, string>;
  strengths: Strength[];
  /** Glossy: the tokens a strength makes see-through (they override build()'s in its scope) and its backdrop. Null in light mode and with pure black, which stay Solid. */
  glass(choice: ThemeChoice, strength: Strength): { tokens: Record<string, string>; backdrop: GlassBackdrop } | null;
  hsl(h: number, s: number, l: number): string;
  rgba(hex: string, alpha: number): string;
  /** What a custom colour becomes in this theme: the value used, whether it was adjusted, its contrast on cards, and a nearby status colour if any. */
  check(hex: string, role: "primary" | "secondary", choice?: ThemeChoice): { valid: boolean; message?: string; input?: string; used?: string; adjusted?: boolean; ratio?: number; near?: "warn" | "danger" | "play" | null };
  apply(el: HTMLElement, vars: Record<string, string>): void;
  contrast(a: string, b: string): number;
  fromThemeId(id: string): ThemeChoice;
  preset(id: PresetId): object;
  swatch(id: SwatchId): object | null;
};

/** The bar under a game's hero, like Steam's: `primary` is Play (or the status's own action when the game needs you), `stats` the facts a player looks for (Last played, Play time, Achievements, Saves: a `status` shows as a plain StatusBadge), `actions` the game's icon buttons (favourite, Properties, More). */
export function PlayBar(props: { primary?: ReactNode; stats: { label: string; value: ReactNode; status?: Status }[]; actions?: ReactNode; label?: string; className?: string }): JSX.Element;

/** Short labelled facts: an overline label beside each value (Developer, Released, Installed in). Items with no value are left out. `mono` sets a path or build in the mono face. */
export function Facts(props: { items: ({ label: string; value?: ReactNode; mono?: boolean; title?: string } | null)[]; className?: string; style?: CSSProperties }): JSX.Element;

/** A choice of a few: the current one on a small secondary button, the others in a `Menu` under it. */
export function Select(props: { value: string; options: { id: string; label: string }[]; onChange?: (id: string) => void; label?: string; disabled?: boolean; className?: string }): JSX.Element;

export interface FileTreeItem {
  id: string; name: string; kind?: "folder" | "file" | "registry";
  /** Ticked: backed up. `mixed`: a folder with some of its files in. */
  checked?: boolean; mixed?: boolean;
  /** Never backed up whatever the person picks (a program file, R1); `note` says why. */
  locked?: boolean;
  meta?: string; tag?: string; note?: string; open?: boolean;
  /** A file's size, and how many files it stands for (a folder shown closed), for the count. */
  bytes?: number; count?: number;
  children?: FileTreeItem[];
}
/** A game's save files, folders first, each with a checkbox: ticked files are backed up and synced, unticked ones stay on this PC. A folder's box shows whether all, some or none of it is in. */
export function FileTree(props: { items: FileTreeItem[]; onToggle?: (id: string, checked: boolean) => void; label?: string; className?: string }): JSX.Element;
/** `toggle` ticks or unticks an item and everything under it, and updates its folders' boxes; `count` adds up what's backed up. */
export const fileTree: { toggle(items: FileTreeItem[], id: string, checked: boolean): FileTreeItem[]; count(items: FileTreeItem[]): { files: number; bytes: number; all: number; allBytes: number } };

export interface PropertiesGame {
  id: string; name: string; favourite?: boolean; hidden?: boolean; installed?: boolean;
  store?: string; storeId?: string; installDir?: string; size?: string; build?: string; engine?: string; foundBy?: string; antiCheat?: string;
  launch?: { url?: string; program?: string; steamOptions?: string; args?: string };
  files?: FileTreeItem[]; settings?: "sync" | "this-pc" | "off"; screenshots?: "this-pc" | "off"; skip?: boolean;
  /** Steam's pictures (cover, hero, logo) and the person's own (`mine`), which win; either may be missing. */
  art?: { cover?: string; hero?: string; logo?: string; mine?: { cover?: string; hero?: string; logo?: string } };
  mode?: "sync" | "backup"; conflict?: "newest" | "ask" | "this-pc";
}
/** One game's Properties, like Steam's: General (name, favourite, shown, its ID), Art (its cover, banner and logo: Steam's or your own), Launch (how it starts, launch options), Installed files, Saves (which files are backed up, in a FileTree, with settings files, screenshots and the usual skips) and Sync (between PCs or backup only, and who wins when both changed it). Changes wait for Save changes; Cancel drops them. */
export function GamePropertiesDialog(props: { game: PropertiesGame; section?: "general" | "art" | "launch" | "files" | "saves" | "sync"; onSave?: (values: object) => void; onClose?: () => void }): JSX.Element;

export interface NewPlace {
  kind: "folder" | "file"; path: string;
  /** How every PC reads it: "<localLow>/Team Cherry/Hollow Knight"; a full path when it's under no folder every PC has. */
  portable: string; portableNote?: string;
  files?: number; bytes?: number; newest?: string;
  /** Program files in it, never taken (R1). */
  programs?: number;
  /** warn: it can be added, with `message` (another game saves there too); refused: it can't, with `message` saying why and what to pick. */
  state?: "ok" | "warn" | "refused"; message?: string;
}
/** A place a game keeps saves, set by hand (FOLD-01): pick a folder or one file, see how every PC reads it and what's there, pick what it holds, Add this place. Program files in it are never taken. */
export function AddPlaceDialog(props: { game: string; place?: NewPlace | null; category?: "save" | "config" | "screenshots"; onPick?: (kind: "folder" | "file") => void; onOpen?: () => void; onAdd?: (choice: { path: string; category: string }) => void; onClose?: () => void }): JSX.Element;

/** A game's save as it is now, for NamedSaveDialog: its main place on this PC and what's there. */
export interface SaveNow {
  path: string; files: number; bytes: number;
  /** When its newest file was saved: "7 Sep 23:27". */
  newest: string;
  /** How many more places the save takes in (a second folder, its settings). */
  more?: number;
}
/** New named save (BAK-18, KAN-77): the game's save as it is now, kept under a name on every PC. What's kept (where, how many files, how big, how new), the name, and Keep this save; a name the game has already is turned away, and a game not syncing yet says first that GameSync starts keeping its saves (`keepLine`). From New named save… on a game's Named saves card and on its page's Saves card. */
export function NamedSaveDialog(props: { game: string; save?: SaveNow | null; names?: string[]; keepLine?: string; name?: string; stage?: "name" | "keeping"; error?: string; onOpen?: () => void; onKeep?: (name: string) => void; onClose?: () => void }): JSX.Element;

export interface OwnFolder {
  path: string; files?: number; bytes?: number; newest?: string;
  /** Program files in it, which are never copied (R1). */
  programs?: number;
  /** "warn": another game keeps saves there too; "refused": it can't be added, and `message` says why. */
  state?: "ok" | "warn" | "refused"; message?: string;
}
/** Add a game or folder of the person's own (LIB-13): a game GameSync didn't find, or any folder kept in step between PCs, like a game server's world. A name, its folder and, optionally, the program that uses it: with one it syncs when that program closes, without one once the folder has been quiet for 5 minutes. From the library's Add game (Add and sync) and first run's Something missing? (`setup`: Add, and nothing syncs until first run ends). */
export function AddGameDialog(props: { name?: string; folder?: OwnFolder | null; program?: { path: string } | null; setup?: boolean; onName?: (name: string) => void; onPickFolder?: () => void; onPickProgram?: () => void; onRemoveProgram?: () => void; onOpen?: () => void; onAdd?: (choice: { name: string }) => void; onClose?: () => void }): JSX.Element;

export interface KeptSave {
  id: string; name: string; saved: string; files: number; bytes?: number;
  /** The name of an identical copy here, or a named save: it isn't named again. */
  same?: string;
  /** Its files are already in the history: that version takes this name, and nothing is stored again. */
  kept?: boolean;
}
/** Save folders kept by hand (BAK-19), as named saves: pick the folder holding them, see each copy by name, Import. The folders are never changed; each copy is a named save on every PC, never current by itself. */
export function ImportKeptSavesDialog(props: { game: string; folder?: string; items?: KeptSave[]; skipped?: string[]; roots?: { id: string; label: string }[]; root?: string; stage?: "pick" | "review" | "done"; onPick?: () => void; onRoot?: (id: string) => void; onOpen?: () => void; onImport?: () => void; onClose?: () => void }): JSX.Element;

/** KAN-61: Sync these saves on a game whose save folder holds copies kept by hand beside its live save (Bloodborne's "Before Orphan\SPRJ0005" beside "SPRJ0005"): only the live save syncs, and each copy comes in as a named save, in one step; or the whole folder as it is. `mode: "backup"` is Back up now, New named save… or Import kept saves… on a game not syncing yet: the live save is backed up, not synced between PCs. The folders are never changed. */
export function KeptCopiesDialog(props: { game: string; live: { path: string; saved: string; files: number; bytes: number }; items: KeptSave[]; skipped?: string[]; wholeBytes?: number; mode?: "sync" | "backup"; stage?: "looking" | "review" | "keeping" | "done"; bring?: boolean; look?: { done: number; total: number; readBytes?: number; totalBytes?: number; speed?: string; left?: string }; progress?: { value?: number | null; detail?: string; speed?: string; left?: string }; onSync?: (bring: boolean) => void; onWhole?: () => void; onClose?: () => void }): JSX.Element;

/** 1536 → "2 KB", 2202009 → "2.1 MB". */
export function formatBytes(bytes: number): string;

/** Achievements (design system version 33). A tier from how many of Steam's players have one: Gold under 5%, Silver under 20%, Bronze the rest; a Zenith is a game's 100% (Platinum until 3 Oct 2026), drawn in platinum. */
export type AchievementTier = "bronze" | "silver" | "gold";

/** A ring of progress, `value` 0 to 100, with `label` in its middle (the percent by default) and `sub` under it, inside the ring from 96px and under the ring, outside it, below that (or as `subBelow` says); `tone` "zenith" for a game's 100%. */
/** `tone: "zenith"`: a game's 100%, gold to red with its glow, glint and rays (version 49); `moment` plays the first time it's seen. */
export function ProgressRing(props: { value: number; size?: number; stroke?: number; label?: string; sub?: string; subBelow?: boolean; tone?: "zenith"; moment?: boolean; ariaLabel?: string; className?: string }): JSX.Element;

/** An achievement's icon in its metal: unlocked (ringed by its tier; Gold glows), locked (Steam's grey icon, dimmed, with a lock) or hidden (a "?": nothing about it shows until it's unlocked). */
export function AchievementBadge(props: { icon?: string; name?: string; state?: "unlocked" | "locked" | "hidden"; tier?: AchievementTier | null; size?: "sm" | "md" | "lg"; label?: string }): JSX.Element;

/** A tier in words with its metal dot: "Gold", or a count of them ("12 Gold"). */
export function TierChip(props: { tier: AchievementTier | "zenith"; count?: number; label?: string; className?: string }): JSX.Element;

/** A game's Zenith medal: earned, the Zenith badge with its sheen and glow; not yet, an outline. */
/** Earned: the Zenith's banner (version 49); `unfurl` drops it open from its bar as it shows. */
export function ZenithMedal(props: { earned?: boolean; size?: number; label?: string; unfurl?: boolean }): JSX.Element;
/** A tier's banner (version 49): Bronze Fuji, Silver the Matterhorn, Gold K2, the Zenith Everest; `size` is its height. */
export function TierBanner(props: { tier?: "bronze" | "silver" | "gold" | "zenith"; size?: number; detail?: "full" | "min"; label?: string; className?: string; style?: CSSProperties }): JSX.Element;

/** How far a game is: its ring, its unlocked tiers and its Zenith (earned on `completedOn`, or how many to go). `size` lg on a game's page, sm on Home. */
export function AchievementsOverview(props: { done: number; total: number; tiers?: { gold?: number; silver?: number; bronze?: number }; completedOn?: string; size?: "lg" | "sm"; moment?: boolean }): JSX.Element;

/** A game's rarest unlocked achievement in its own light: its badge large, what it is, its tier and how few players have it. */
export function AchievementSpotlight(props: { item: { name: string; desc?: string; icon?: string; pct?: number }; eyebrow?: string; size?: "sm" }): JSX.Element;

/** A row of every achievement: badge, name and description, when it was unlocked or how many players have it; a hidden one keeps its name and description secret. */
export function AchievementRow(props: { item: { name: string; desc?: string; icon?: string; pct?: number; when?: string; state?: "unlocked" | "locked" | "hidden" }; selected?: boolean; onClick?: () => void }): JSX.Element;

/** ACH-09 (version 35): the popup when an achievement unlocks while you play, GameSync's own window in a corner over the game, never taking focus or clicks; dark glass in every theme. `item` the achievement (a `hidden` one says "Hidden achievement unlocked"); `game` its game; `progress` with it unlocked; `kind` "zenith" for a game's 100%; `phase` "in" or "out" draws its motion; `corner` where it shows. */
export function AchievementPopup(props: { item?: { name: string; icon?: string; pct?: number; hidden?: boolean }; game?: string; progress?: { done: number; total: number }; kind?: "achievement" | "zenith"; phase?: "in" | "out"; corner?: "top-right" | "top-left" | "bottom-right" | "bottom-left"; className?: string; style?: CSSProperties }): JSX.Element;

/** A trophy in its metal, lit from the top left (120 × 150 at `size` 120). */
export function TrophyFigure(props: { metal?: "bronze" | "silver" | "gold" | "platinum"; size?: number; className?: string; style?: CSSProperties }): JSX.Element;

/** The Achievements page's band: trophies rising from its right edge, the Zenith's in platinum at the back; decoration only. */
export function TrophyRise(props: { className?: string }): JSX.Element;

/** The Zenith badge (version 45): a red sun in halftone rings of dots behind pointy mountains, pines and pale grass, in a dark teal ring; `label` makes it an image a screen reader names. */
export function ZenithBadge(props: { size?: number; label?: string; className?: string; style?: CSSProperties }): JSX.Element;

/** The Zenith's scene (version 47): a big sun in rings of dots behind a tall pointy peak and a smaller one either side, the peaks in the page's ink, in a 300 x 340 box; the Zeniths card's corner, faint. Decoration only. */
export function ZenithScene(props: { width?: number; height?: number; className?: string; style?: CSSProperties }): JSX.Element;

/** The Zeniths monument (version 40; the badge since 45): the Zenith badge large, light fanning out from behind it; `earned` false draws it faint, without its light. */
export function ZenithMonument(props: { earned?: boolean; width?: number; height?: number; label?: string; className?: string; style?: CSSProperties }): JSX.Element;
/** A game in Achievements by game: its count, whether it counts and whether GameSync shows its popup. `launcher` is the launcher that runs it and shows its own popup, so GameSync's starts off. */
export interface AchievementGame { id: string; name: string; art?: string; count: string; zenith?: boolean; launcher?: string | null; counted: boolean; popup: boolean; /** Version 51 (R13): no popup over it, ever. */ antiCheat?: boolean }
/** Achievements by game (version 41): every game GameSync reads achievements for, each counted or left out and with GameSync's popup on or off; from Settings, Achievements. */
export function AchievementGamesDialog(props: { games: AchievementGame[]; onCount?: (id: string, on: boolean) => void; onPopup?: (id: string, on: boolean) => void; onClose?: () => void }): JSX.Element;

/** A place learn mode saw a game write to (version 43): its portable path, this PC's path, what was written, and why GameSync won't take it, if it won't. */
export interface LearnFind { id: string; path: string; here?: string; files: number; bytes: number; newest: string; examples?: string[]; tags?: string[]; file?: boolean; refused?: string }
/** Where a game saves, from one session learn mode watched (version 43; FIND-04): the places it wrote to, the likeliest ticked; Sync these saves, Watch again, Not these. No finds: it says so, with Add a place…. */
export function LearnModeDialog(props: { game: string; session: string; finds: LearnFind[]; picked?: string[]; onPick?: (ids: string[]) => void; stage?: "adding" | null; overflowed?: boolean; onSync?: () => void; onAgain?: () => void; onNotThese?: () => void; onAddPlace?: () => void; onClose?: () => void }): JSX.Element;
