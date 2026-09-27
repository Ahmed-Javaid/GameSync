// GameSync design system — window.GameSync (React 18, classic script)
import type { ReactNode, ButtonHTMLAttributes, CSSProperties } from "react";

export type IconName =
  | "home" | "library" | "search" | "saves" | "terminal" | "activity" | "settings" | "play" | "sync"
  | "share" | "zip" | "folder" | "cloud" | "upload" | "download" | "check" | "alert" | "pause" | "lock"
  | "unplug" | "block" | "archive" | "pin" | "clock" | "chevronRight" | "chevronDown" | "chevronsRight"
  | "x" | "info" | "copy" | "monitor" | "shield" | "palette" | "drive" | "bell" | "plus" | "sun" | "moon"
  | "pencil" | "reset" | "logo";

export type Status =
  | "synced" | "playing" | "upload-pending" | "newer-in-cloud" | "conflict" | "held"
  | "in-use" | "not-found" | "not-available" | "blocked" | "backup-only";

/** Outlined 24px icon. */
export function Icon(props: { name: IconName; size?: number; strokeWidth?: number; label?: string; className?: string }): JSX.Element;

/** Pill button. One primary per view. */
export function Button(props: ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: "primary" | "secondary" | "ghost" | "danger";
  size?: "sm";
  icon?: IconName;
  iconAfter?: IconName;
  children?: ReactNode;
}): JSX.Element;

/** 40px round icon button; label is required. */
export function IconButton(props: ButtonHTMLAttributes<HTMLButtonElement> & { icon: IconName; label: string; glass?: boolean; size?: "sm" }): JSX.Element;

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

/** Last-played banner with art, status and Continue playing. `art`: Steam's library hero (1920x620 or 3840x1240). `logo`: the game's transparent logo (Steam's logo.png), shown instead of the text title, which stays for screen readers. */
export function HeroBanner(props: {
  art?: string; logo?: string; title: string; eyebrow?: string; chip?: string; blurb?: string;
  status?: Status; statusLabel?: string; playLabel?: string; style?: CSSProperties;
  onPlay?: () => void; onSaves?: () => void; onSettings?: () => void;
}): JSX.Element;

/** One game status as icon + word. */
export function StatusBadge(props: { status: Status; label?: string; plain?: boolean; className?: string }): JSX.Element;

/** 2:3 cover tile (Steam's 600x900 library capsule). No art: a title cover with the game's name. No badge when synced. */
export function GameTile(props: { name: string; art?: string; status?: Status; meta?: string; width?: number | string; onClick?: () => void }): JSX.Element;

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

/** Sets every colour token for its children. */
export function ThemeScope(props: { theme: ThemeChoice; className?: string; style?: CSSProperties; children?: ReactNode }): JSX.Element;

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
  /** What a custom colour becomes in this theme: the value used, whether it was adjusted, its contrast on cards, and a nearby status colour if any. */
  check(hex: string, role: "primary" | "secondary", choice?: ThemeChoice): { valid: boolean; message?: string; input?: string; used?: string; adjusted?: boolean; ratio?: number; near?: "warn" | "danger" | "play" | null };
  apply(el: HTMLElement, vars: Record<string, string>): void;
  contrast(a: string, b: string): number;
  fromThemeId(id: string): ThemeChoice;
  preset(id: PresetId): object;
  swatch(id: SwatchId): object | null;
};

/** 1536 → "2 KB", 2202009 → "2.1 MB". */
export function formatBytes(bytes: number): string;
