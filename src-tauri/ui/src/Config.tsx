// The Configuration surface: where the tunables live, in two scopes.
//
// This space — the characteristics of the publication currently open
// (publication.yaml) and its assistant harness catalog (assistants.md).
// Tezuri itself — the registered spaces. The landing stays the launcher;
// this page is the manager.

import { useEffect, useState } from "react";
import { invoke } from "./bridge";
import type { Assistant, Identity } from "./bridge";

const EMPTY_ID: Identity = { name: "", byline: "", persona: "" };

interface SpaceRow {
  name: string;
  persona: string;
  root: string;
}

export function Config({
  spaceOpen, spacePath, identity, spaces,
  onSaveIdentity, saveBusy, saveError, onRemoveSpace,
}: {
  spaceOpen: boolean;
  spacePath: string | null;
  identity: Identity | null;
  spaces: SpaceRow[];
  onSaveIdentity: (next: Identity) => void;
  saveBusy: boolean;
  saveError: string;
  onRemoveSpace: (root: string) => void;
}) {
  return (
    <div className="config">
      <p className="crumb">CONFIGURATION</p>
      <h1 className="detail-title">Tunables</h1>
      <p className="detail-sub">
        Everything here is plain files or a plain list — nothing canonical lives inside Tezuri.
      </p>

      {spaceOpen ? (
        <>
          <IdentitySection
            identity={identity} spacePath={spacePath}
            onSave={onSaveIdentity} busy={saveBusy} error={saveError} />
          <AssistantsSection />
        </>
      ) : (
        <div className="config-section">
          <h2>This space</h2>
          <p className="config-empty">Open a space to tune its characteristics and assistants.</p>
        </div>
      )}

      <div className="config-section">
        <h2>Tezuri itself</h2>
        <p className="config-hint">
          Registered spaces. Removing one forgets the path — files on disk are untouched.
        </p>
        {spaces.map((s) => (
          <div className="space-manage-row" key={s.root}>
            <span className="space-glyph" aria-hidden="true">
              {(s.name || s.root.split(/[\\/]/).pop() || "?").slice(0, 2).toUpperCase()}
            </span>
            <span className="space-card-body">
              <span className="space-name">{s.name || s.root.split(/[\\/]/).pop()}</span>
              <span className="space-mono">{s.root}</span>
            </span>
            <span style={{ flex: 1 }} />
            <button className="small-danger" onClick={() => onRemoveSpace(s.root)}>Remove</button>
          </div>
        ))}
        {spaces.length === 0 && <p className="config-empty">No spaces registered yet.</p>}
      </div>
    </div>
  );
}

// ---- this space: identity ---------------------------------------------------

function IdentitySection({ identity, spacePath, onSave, busy, error }: {
  identity: Identity | null;
  spacePath: string | null;
  onSave: (next: Identity) => void;
  busy: boolean;
  error: string;
}) {
  const [form, setForm] = useState<Identity>(identity ?? EMPTY_ID);

  // Resync when the underlying file changes (space switch, external save).
  useEffect(() => { setForm(identity ?? EMPTY_ID); }, [identity]);

  const set = (k: keyof Identity, v: string) => setForm({ ...form, [k]: v });

  return (
    <div className="config-section">
      <h2>This space — characteristics</h2>
      <p className="config-hint">
        Lives in <code>publication.yaml</code> inside the space
        {spacePath && <> · <span className="space-mono">{spacePath}</span></>}.
        Anything else you keep in that file is preserved untouched.
      </p>
      <div className="detail-form" style={{ marginTop: 10 }}>
        <label>Name
          <input value={form.name} onChange={(e) => set("name", e.target.value)} placeholder="Kintsugi" />
        </label>
        <label>Byline
          <input value={form.byline} onChange={(e) => set("byline", e.target.value)} placeholder="words and photographs by…" />
        </label>
        <label>Persona
          <input value={form.persona} onChange={(e) => set("persona", e.target.value)} placeholder="who writes here" />
        </label>
        <div className="row" style={{ marginTop: 8 }}>
          <button className="primary" disabled={busy} onClick={() => onSave(form)}>
            {busy ? "Saving…" : "Save characteristics"}
          </button>
          {error && <span className="detail-error">{error}</span>}
        </div>
      </div>
    </div>
  );
}

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
