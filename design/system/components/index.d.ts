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

/** Pill button. One primary per view. */
export function Button(props: ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: "primary" | "secondary" | "ghost" | "danger";
  size?: "sm";
  icon?: IconName;
  iconAfter?: IconName;
  children?: ReactNode;
}): JSX.Element;

/** 40px round icon button; label is required. `pressed` makes it a toggle (the favourite star, filled while on). */
export function IconButton(props: ButtonHTMLAttributes<HTMLButtonElement> & { icon: IconName; label: string; glass?: boolean; size?: "sm"; pressed?: boolean }): JSX.Element;

/** Pill tabs that switch a view in place. */
export function PillTabs(props: {
  items: { id: string; label: string; icon?: IconName; count?: number }[];
  value?: string;
  onChange?: (id: string) => void;
  label?: string;
}): JSX.Element;

/** Collapsed navigation rail. `dot` is a status token name; `dotLabel` says what it means (tooltip and screen readers). */
export function SideRail(props: {
  items: { id: string; icon: IconName; label: string; sep?: boolean; bottom?: boolean; dot?: "play" | "warn" | "ok" | "danger"; dotLabel?: string }[];
  current?: string;
  onNavigate?: (id: string) => void;
}): JSX.Element;

/** Rounded bg-200 panel. */
export function Card(props: { title?: string; subtitle?: string; action?: ReactNode; children?: ReactNode; className?: string; style?: CSSProperties }): JSX.Element;

/** Last-played banner with art, status and Continue playing. `art`: Steam's library hero (1920x620 or 3840x1240). `logo`: the game's transparent logo (Steam's logo.png), shown instead of the text title, which stays for screen readers. `actions` replaces Manage saves and Properties (a game's page: the favourite star and More); `noPlay` leaves Play out (a game not installed here). */
export function HeroBanner(props: {
  art?: string; logo?: string; title: string; eyebrow?: string; chip?: string; blurb?: string;
  status?: Status; statusLabel?: string; playLabel?: string; playIcon?: IconName; noPlay?: boolean; actions?: ReactNode; style?: CSSProperties;
  onPlay?: () => void; onSaves?: () => void; onSettings?: () => void;
}): JSX.Element;

/** One game status as icon + word. On cover art (a tile, the hero) it takes a deep tint that reads over any art. `backup-only` reads "Synced by its store": pass `label` "Synced by Steam" (or Epic, Xbox) when the store is known. */
export function StatusBadge(props: { status: Status; label?: string; plain?: boolean; className?: string }): JSX.Element;

/** 2:3 cover tile (Steam's 600x900 library capsule). No art: a title cover with the game's name. No badge when synced. `statusLabel` names the store for a game its store syncs ("Synced by Steam"). */
export function GameTile(props: { name: string; art?: string; status?: Status; statusLabel?: string; meta?: string; width?: number | string; onClick?: () => void }): JSX.Element;

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

export function ProgressBar(props: { value: number; label?: string; right?: ReactNode; ariaLabel?: string }): JSX.Element;

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
}

/** Bring saves from a shared zip in as pinned versions; never replaces current saves. */
export function ImportSavesDialog(props: { zipName?: string; from?: string; items: ImportItem[]; stage?: "review" | "done"; onImport?: (ids: string[]) => void; onClose?: () => void }): JSX.Element;

/** Left column of the Settings screen. `badge` flags a section that needs the person. */
export function SettingsNav(props: { items: { id: string; label: string; icon: IconName; badge?: string }[]; current?: string; onChange?: (id: string) => void; label?: string }): JSX.Element;

/** One setting: title and description on the left, the control (children) on the right. */
export function SettingsRow(props: { title: ReactNode; description?: ReactNode; tag?: string; disabled?: boolean; stack?: boolean; children?: ReactNode }): JSX.Element;

/** A folder the person can change. `state`: ok, moving (with progress), missing (drive unplugged) or refused (not allowed, with the reason). */
export function FolderField(props: {
  path: string; label?: string; meta?: string; icon?: IconName;
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
  mode?: "sync" | "backup"; conflict?: "newest" | "ask" | "this-pc";
}
/** One game's Properties, like Steam's: General (name, favourite, shown, its ID), Launch (how it starts, launch options), Installed files, Saves (which files are backed up, in a FileTree, with settings files, screenshots and the usual skips) and Sync (between PCs or backup only, and who wins when both changed it). Changes wait for Save changes; Cancel drops them. */
export function GamePropertiesDialog(props: { game: PropertiesGame; section?: "general" | "launch" | "files" | "saves" | "sync"; onSave?: (values: object) => void; onClose?: () => void }): JSX.Element;

/** 1536 → "2 KB", 2202009 → "2.1 MB". */
export function formatBytes(bytes: number): string;
