// The typed seam between the interface and the desktop shell. The frontend
// reaches native power only through the named product operations gathered
// here — never through window.__TAURI__ elsewhere, and never through any
// generic filesystem, shell, process, or network authority.

export interface PublicationInfo {
  path: string;
  articles: number;
  words: number;
}

export interface Identity {
  name: string;
  byline: string;
  persona: string;
  [key: string]: unknown;
}

export interface DeskEntry {
  slug: string;
  title: string;
  state: "draft" | "review" | "published";
  date: string | null;
  words: number;
  links: string[];
  dangling_links: string[];
  tags?: string[] | null;
}

export interface Desk {
  entries: DeskEntry[];
  inbound: Record<string, number>;
}

export interface Change {
  status: string;
  path: string;
}

export interface LoadedArticle {
  article: {
    meta: {
      slug: string;
      state: string;
      date: string | null;
      tags: string[] | null;
      cover: string | null;
    };
  };
  raw: string;
}

export interface AdviceResult {
  recipe: string;
  assistant: string;
  output: string;
}

export interface Assistant {
  id: string;
  command: string;
  args: string[];
  note: string | null;
  default: boolean;
}

export interface ThemePreset {
  id: string;
  name: string;
  description: string;
  css: string;
}

export interface SlotInstance {
  name: string;
  raw: string;
  hints: string[];
  html: string;
  editable: boolean;
  mirror: boolean;
}

/** One ordered piece of the Write-mode page, from the desktop's composer. */
export type Seg =
  | { kind: "text"; html: string }
  | {
      kind: "article_flow";
      mirror: boolean;
      /** Declared presentation around the flow (title-banner modes). */
      frame: string;
      /** The bare article prose — where the editor mounts. */
      prose: string;
      /** The exact slot expression, for conduct splicing. */
      raw: string;
      hints: string[];
    }
  | ({ kind: "slot" } & SlotInstance);

export interface WriteCompose {
  slug: string;
  segments: Seg[];
  notes: string[];
  space_template: boolean;
  /** The artifact's head dress (font imports, template styles, baseline) —
   *  the desk scopes it to the Write plane. */
  css: string;
}

/** Rewrite artifact CSS so it wears the Write plane, not the desk.
 *  `html` / `body` / `:root` rules ARE the page's dress — background, base
 *  font, ink — so they map onto the scope element itself; a selector that
 *  merely starts with one of those (`body .hero`) folds whole onto the
 *  plane. Everything else is prefixed as a descendant. At-rule headers
 *  pass; inner rules get scoped by the same pass. Nothing escapes the
 *  scope, so the desk chrome stays neutral. */
export function scopeCss(css: string, scope: string): string {
  const stripped = css.replace(/\/\*[\s\S]*?\*\//g, "");
  const pageLevel = /^(html|body|:root)\b/;
  const scopeSel = (s: string) => (pageLevel.test(s) ? scope : `${scope} ${s}`);
  return stripped.replace(/([^{}]+)\{/g, (m, sel: string) => {
    const t = sel.trim();
    if (!t.startsWith("@")) {
      const list = t
        .split(",")
        .map((s) => s.trim())
        .filter(Boolean)
        .map(scopeSel)
        .join(", ");
      return `${list}{`;
    }
    // At-statements without a block (@import, @charset) end in `;` and may
    // have swallowed the next rule's selectors into this match — scope that
    // remainder instead of leaking it. Block at-rules (@media…) pass.
    const semi = t.lastIndexOf(";");
    if (semi === -1 || semi === t.length - 1) return m;
    const head = t.slice(0, semi + 1);
    const rest = t
      .slice(semi + 1)
      .split(",")
      .map((s) => s.trim())
      .filter(Boolean);
    if (rest.length === 0) return m;
    return `${head}\n${rest.map(scopeSel).join(", ")}{`;
  });
}

// -- slot catalog ------------------------------------------------------------

export interface CatalogControl {
  kind: "toggle" | "choice" | "count";
  values: string[];
  default: string;
}

export interface CatalogOption {
  key: string;
  label: string;
  control: CatalogControl;
}

export interface CatalogEntry {
  name: string;
  doc: string;
  hosts: string[];
  options: CatalogOption[];
}

/** Re-splice one slot's raw expression in template bytes. The parse holds
 *  each occurrence; identical raws are disambiguated by occurrence index. */
export function spliceSlot(
  template: string,
  oldRaw: string,
  occurrence: number,
  nextHints: string[]
): string {
  const name = oldRaw
    .replace(/^\{\{/, "")
    .replace(/\}\}$/, "")
    .split("|")[0]
    .trim();

  // Occurrence counting must consider exact-byte matches of the raw text.
  let at = -1;
  for (let k = 0; k <= occurrence; k++) {
    at = template.indexOf(oldRaw, at + 1);
    if (at < 0) return template; // draft drifted; do not guess
  }

  const hints = nextHints.slice();
  let raw = `{{${name}`;
  if (hints.length > 0) {
    // ARTICLE keeps mode tokens first — positional vocabulary reads best.
    if (name === "ARTICLE") hints.sort((a, b) => Number(isModeToken(b)) - Number(isModeToken(a)));
    raw += ` | ${hints.join(", ")}`;
  }
  raw += "}}";
  return template.slice(0, at) + raw + template.slice(at + oldRaw.length);
}

function isModeToken(hint: string): boolean {
  return !hint.includes(":");
}

/** Compute the hint list after conducting one option to a new value. */
export function nextHintsFor(
  current: string[],
  optKey: string,
  value: string | null
): string[] {
  const kept = current.filter((h) => {
    if (optKey) return !h.startsWith(`${optKey}:`);
    return true;
  });
  if (value === null || value === "") return kept;
  const token = optKey ? `${optKey}:${value}` : value;
  return kept.includes(token) ? kept : [...kept, token];
}

/** Insert `{{name}}` beside an existing slot occurrence in the draft.
 *  Anchoring by exact bytes keeps insertion honest: no offsets, no guesses;
 *  a drifted draft simply refuses to change rather than corrupting. */
export function insertSlotAt(
  template: string,
  anchorRaw: string,
  anchorOccurrence: number,
  where: "before" | "after",
  name: string
): string {
  let at = -1;
  for (let k = 0; k <= anchorOccurrence; k++) {
    at = template.indexOf(anchorRaw, at + 1);
    if (at < 0) return template; // drifted: refuse rather than guess
  }
  const pos = where === "after" ? at + anchorRaw.length : at;
  // Keep lines tidy: never glue a slot onto existing text.
  const beforeChar = pos > 0 ? template[pos - 1] : "\n";
  const afterChar = template[pos] ?? "\n";
  let ins = `{{${name}}}`;
  if (beforeChar !== "\n") ins = `\n${ins}`;
  if (afterChar !== "\n") ins = `${ins}\n`;
  return template.slice(0, pos) + ins + template.slice(pos);
}

export interface ProofResult {
  verdict: string;
  evidence: string;
}

/** Invoke one named product operation. Throws when the bridge is absent. */
export function invoke<T>(cmd: string, args?: Record<string, unknown>): Promise<T> {
  const t = (window as any).__TAURI__;
  if (!t?.core?.invoke) throw new Error("Tauri bridge not ready");
  return t.core.invoke(cmd, args) as Promise<T>;
}

/** Subscribe to the settler's progress (named events from the shell). */
export function onSettle(
  cb: (p: { kind: string; done: number; total: number }) => void
): Promise<() => void> {
  const t = (window as any).__TAURI__;
  if (!t?.event?.listen) return Promise.resolve(() => {});
  return t.event.listen("tezuri:settle", (e: any) => cb(e.payload)) as Promise<() => void>;
}
