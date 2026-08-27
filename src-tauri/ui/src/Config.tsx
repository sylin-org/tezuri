// The Configuration surface: where the tunables live, in two scopes.
//
// This space — the characteristics of the publication currently open
// (publication.yaml) and its assistant harness catalog (assistants.md).
// Tezuri itself — the registered spaces. The landing stays the launcher;
// this page is the manager.

import { useEffect, useRef, useState } from "react";
import { invoke } from "./bridge";
import type { Assistant, CatalogEntry, Identity, ThemePreset } from "./bridge";

const EMPTY_ID: Identity = { name: "", byline: "", persona: "" };

/** The event the app listens to when the saved theme changes. */
export const THEME_EVENT = "tezuri:theme-changed";

interface SpaceRow {
  name: string;
  persona: string;
  root: string;
}

export function Config({ spaces, onRemoveSpace }: {
  spaces: SpaceRow[];
  onRemoveSpace: (root: string) => void;
}) {
  return (
    <div className="config">
      <p className="crumb">CONFIGURATION</p>
      <h1 className="detail-title">Tunables</h1>
      <p className="detail-sub">
        Everything here is plain files or a plain list — nothing canonical lives inside Tezuri.
        Space details (identity, theme, template, tags) live in each space's own ribbon.
      </p>

      <AssistantsSection />

      <div className="config-section">
        <h2>Tezuri itself</h2>
        <p className="config-hint">
          Registered spaces. Removing one forgets the path — files on disk are untouched.
        </p>
        {spaces.map((s) => (
          <ManageRow key={s.root} space={s} onRemove={onRemoveSpace} />
        ))}
        {spaces.length === 0 && <p className="config-empty">No spaces registered yet.</p>}
      </div>
    </div>
  );
}

/** One registered-space row. The destructive ask is a two-step inline
 *  confirmation in the row itself — never a dialog over the page. */
function ManageRow({ space, onRemove }: {
  space: SpaceRow;
  onRemove: (root: string) => void;
}) {
  const [confirming, setConfirming] = useState(false);
  const label = space.name || space.root.split(/[\\/]/).pop() || space.root;

  if (!confirming) {
    return (
      <div className="space-manage-row">
        <span className="space-glyph" aria-hidden="true">{label.slice(0, 2).toUpperCase()}</span>
        <span className="space-card-body">
          <span className="space-name">{label}</span>
          <span className="space-mono">{space.root}</span>
        </span>
        <span style={{ flex: 1 }} />
        <button className="small-danger" onClick={() => setConfirming(true)}>Remove</button>
      </div>
    );
  }
  return (
    <div className="space-manage-row confirming">
      <span className="space-glyph" aria-hidden="true">{label.slice(0, 2).toUpperCase()}</span>
      <span className="space-card-body">
        <span className="space-name">Remove “{label}” from this list?</span>
        <span className="space-mono">forgetting the path only — files on disk are untouched</span>
      </span>
      <span style={{ flex: 1 }} />
      <button onClick={() => setConfirming(false)}>Keep</button>
      <button className="danger" onClick={() => { setConfirming(false); onRemove(space.root); }}>
        Remove
      </button>
    </div>
  );
}

// ---- this space: identity ---------------------------------------------------


// ---- this space: assistant catalog --------------------------------------------

function AssistantsSection() {
  const [rows, setRows] = useState<Assistant[] | null>(null);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    let live = true;
    invoke<Assistant[]>("read_assistant_catalog")
      .then((a) => { if (live) setRows(a); })
      .catch((e) => { if (live) setError(e.message ?? String(e)); });
    return () => { live = false; };
  }, []);

  if (rows === null) {
    return (
      <div className="config-section">
        <h2>This space — assistants</h2>
        <p className="config-empty">{error || "Loading catalog…"}</p>
      </div>
    );
  }

  const update = (i: number, patch: Partial<Assistant>) =>
    setRows(rows.map((r, n) => (n === i ? { ...r, ...patch } : r)));

  const addRow = () =>
    setRows([...rows, { id: "", command: "", args: [], note: null, default: rows.length === 0 }]);

  const removeRow = (i: number) => {
    const next = rows.filter((_, n) => n !== i);
    if (next.length > 0 && !next.some((r) => r.default)) next[0].default = true;
    setRows(next);
  };

  const setDefault = (i: number) => setRows(rows.map((r, n) => ({ ...r, default: n === i })));

  const save = async () => {
    setSaved(false);
    setError("");
    const cleaned = rows
      .filter((r) => r.id.trim() && r.command.trim())
      .map((r) => ({ ...r, id: r.id.trim(), command: r.command.trim() }));
    // Exactly one default survives; the first entry absorbs an orphaned flag.
    if (cleaned.length > 0 && !cleaned.some((r) => r.default)) cleaned[0].default = true;
    let seen = false;
    for (const r of cleaned) {
      if (r.default && seen) r.default = false;
      else if (r.default) seen = true;
    }
    try {
      await invoke("save_assistant_catalog", { entries: cleaned });
      setRows(cleaned);
      setSaved(true);
    } catch (e: any) {
      setError(e.message ?? String(e));
    }
  };

  return (
    <div className="config-section">
      <h2>This space — assistants</h2>
      <p className="config-hint">
        Lives in <code>assistants.md</code>. Each assistant is a command on PATH run with these
        arguments — Tezuri stores no keys; auth rides the harness's own configuration.
      </p>
      <div className="asst-head" aria-hidden="true">
        <span>id</span><span>command</span><span>args</span><span />
        <span />
      </div>
      {rows.map((r, i) => (
        <div className="asst-row" key={i}>
          <input value={r.id} placeholder="codex" aria-label="Assistant id"
                 onChange={(e) => update(i, { id: e.target.value })} />
          <input value={r.command} placeholder="command on PATH" aria-label="Command"
                 onChange={(e) => update(i, { command: e.target.value })} />
          <input value={r.args.join(", ")} placeholder="args, comma-separated" aria-label="Arguments"
                 onChange={(e) => update(i, {
                   args: e.target.value.split(",").map((x) => x.trim()).filter(Boolean),
                 })} />
          <label className="asst-default" title="Used when a recipe doesn't pin one">
            <input type="radio" name="asst-default" checked={r.default}
                   onChange={() => setDefault(i)} /> default
          </label>
          <button className="small-danger" aria-label={`Remove ${r.id || "assistant"}`}
                  onClick={() => removeRow(i)}>×</button>
        </div>
      ))}
      {rows.length === 0 && (
        <p className="config-empty">No assistants configured. Add one to use the consult verbs.</p>
      )}
      <div className="row" style={{ marginTop: 10 }}>
        <button onClick={addRow}>+ Add assistant</button>
        <button className="primary" onClick={save}>Save assistants</button>
        {saved && <span className="mono-fact">saved ✓</span>}
        {error && <span className="detail-error">{error}</span>}
      </div>
    </div>
  );
}

// ---- this space: appearance ---------------------------------------------------

const SPECIMEN =
  "The press never touches what you didn't approve. It reads your folder, " +
  "holds your drafts, and proves the site's own build before anything ships. " +
  "Every word stays a plain file you could edit with nothing but a text editor.";

/** Starter packs: presentations copied into the space on pick, then owned
 *  as plain files. Applying overwrites template + theme deliberately, so
 *  the ask is a two-step inline confirmation in the card. */

const GHOST_TEMPLATE = [
  "<!-- Your page. {{ARTICLE}} is the writing; other slots fill themselves.",
  "     Conduct any slot from Write mode; Preview is the exact lens. -->",
  "<body class=\"{{body_class}}\">",
  "  {{ARTICLE | title-banner}}",
  "",
  "  <nav>{{home_link}} · {{prev_link}} · {{next_link}}</nav>",
  "",
  "  <aside>{{toc}}</aside>",
  "  {{footer}}",
  "</body>",
].join("\n");

/** The layout editor: draft on the left, live specimen through the real
 *  pipeline on the right. Saving follows propose→apply and is journaled;
 *  removing returns to the embedded default. */

