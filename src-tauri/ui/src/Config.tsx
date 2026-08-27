// The Configuration surface: where the tunables live, in two scopes.
//
// This space — the characteristics of the publication currently open
// (publication.yaml) and its assistant harness catalog (assistants.md).
// Tezuri itself — the registered spaces. The landing stays the launcher;
// this page is the manager.

import { useEffect, useState } from "react";
import { invoke } from "./bridge";
import type { Assistant, Identity, ThemePreset } from "./bridge";

const EMPTY_ID: Identity = { name: "", byline: "", persona: "" };

/** The event the app listens to when the saved theme changes. */
export const THEME_EVENT = "tezuri:theme-changed";

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
          <PresentationSection />
          <LayoutSection />
          <AssistantsSection />
          <AppearanceSection />
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

// ---- this space: appearance ---------------------------------------------------

const SPECIMEN =
  "The press never touches what you didn't approve. It reads your folder, " +
  "holds your drafts, and proves the site's own build before anything ships. " +
  "Every word stays a plain file you could edit with nothing but a text editor.";

/** Starter packs: presentations copied into the space on pick, then owned
 *  as plain files. Applying overwrites template + theme deliberately, so
 *  the ask is a two-step inline confirmation in the card. */
function PresentationSection() {
  const [packs, setPacks] = useState<{ id: string; name: string; description: string }[] | null>(null);
  const [confirming, setConfirming] = useState<string | null>(null);
  const [receipt, setReceipt] = useState("");
  const [error, setError] = useState("");

  useEffect(() => {
    invoke<any[]>("packs_list").then(setPacks).catch((e) => setError(e.message ?? String(e)));
  }, []);

  if (!packs) return null;
  return (
    <div className="config-section">
      <h2>Presentation</h2>
      <p className="config-hint">
        A starter pack copies its layout and dress into this space — after that the
        files are yours. Picking replaces the current template and theme.
      </p>
      {packs.map((p) => (
        <div className="pack-row" key={p.id}>
          <div>
            <b>{p.name}</b>
            <span className="config-hint"> — {p.description}</span>
          </div>
          {!confirming || confirming !== p.id ? (
            <button onClick={() => { setConfirming(p.id); setReceipt(""); setError(""); }}>
              Pick
            </button>
          ) : (
            <span className="row" style={{ gap: 6 }}>
              <button className="primary"
                      onClick={async () => {
                        try {
                          await invoke("pack_apply", { id: p.id });
                          setReceipt(`${p.name} applied — your space now owns its files.`);
                        } catch (e: any) {
                          setError(e.message ?? String(e));
                        }
                        setConfirming(null);
                      }}>
                Replace layout &amp; theme
              </button>
              <button onClick={() => setConfirming(null)}>Keep mine</button>
            </span>
          )}
        </div>
      ))}
      {receipt && <p className="receipt">{receipt}</p>}
      {error && <p className="config-error">{error}</p>}
    </div>
  );
}

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
function LayoutSection() {
  const [fileText, setFileText] = useState<string | null>(null); // disk truth
  const [draft, setDraft] = useState<string | null>(null);
  const [slugs, setSlugs] = useState<string[]>([]);
  const [specimenSlug, setSpecimenSlug] = useState("");
  const [specimen, setSpecimen] = useState("");
  const [notes, setNotes] = useState<string[]>([]);
  const [error, setError] = useState("");

  useEffect(() => {
    (async () => {
      try {
        const t = await invoke<string | null>("read_template");
        setFileText(t);
        setDraft(t ?? GHOST_TEMPLATE);
        const d = await invoke<{ entries: { slug: string }[] }>("desk");
        setSlugs(d.entries.map((e) => e.slug));
        setSpecimenSlug(d.entries[0]?.slug ?? "");
      } catch (e: any) {
        setError(e.message ?? String(e));
      }
    })();
  }, []);

  // Live specimen: debounced render through the one pipeline.
  useEffect(() => {
    if (draft === null || !specimenSlug) return;
    const t = setTimeout(async () => {
      try {
        const media = await invoke<string>("media_base");
        const [html, n] = await invoke<[string, string[]]>("render_specimen", {
          slug: specimenSlug,
          template: draft,
        });
        setSpecimen(html.replaceAll("../media/", media));
        setNotes(n ?? []);
      } catch (e: any) {
        setError(e.message ?? String(e));
      }
    }, 400);
    return () => clearTimeout(t);
  }, [draft, specimenSlug]);

  const dirty = draft !== fileText && !(fileText === null && draft === GHOST_TEMPLATE);

  return (
    <div className="config-section">
      <h2>Layout</h2>
      <p className="config-hint">
        The page template — plain HTML with slot vocabulary. The specimen renders a real
        article through the real pipeline.
      </p>
      <div className="row" style={{ gap: 8, alignItems: "center" }}>
        <select value={specimenSlug} onChange={(e) => setSpecimenSlug(e.target.value)}
                aria-label="Specimen article">
          {slugs.length === 0 && <option value="">(no articles)</option>}
          {slugs.map((s) => <option key={s} value={s}>{s}</option>)}
        </select>
        {dirty && (
          <>
            <button className="primary" onClick={async () => {
              try {
                await invoke("write_template", { text: draft ?? "" });
                setFileText(draft);
              } catch (e: any) { setError(e.message ?? String(e)); }
            }}>Save layout</button>
            <button onClick={() => setDraft(fileText)}>Revert</button>
          </>
        )}
        {fileText !== null && (
          <button title="Remove templates/article.html — the embedded default speaks again"
                  onClick={async () => {
                    try {
                      await invoke("write_template", { text: "" });
                      setFileText(null);
                      setDraft(GHOST_TEMPLATE);
                    } catch (e: any) { setError(e.message ?? String(e)); }
                  }}>Remove template</button>
        )}
        {dirty && fileText === null && <span className="mono-fact">unsaved</span>}
      </div>
      <textarea
        className="layout-editor"
        spellCheck={false}
        value={draft ?? ""}
        onChange={(e) => setDraft(e.target.value)}
        rows={18}
        aria-label="Template draft"
      />
      {notes.length > 0 && (
        <p className="wc-whispers" style={{ margin: "4px 0" }}>{notes.join(" · ")}</p>
      )}
      {error && <p className="config-error">{error}</p>}
      <p className="config-hint">Specimen — exactly what would emit:</p>
      <iframe className="preview-frame" title="Template specimen"
              srcDoc={specimen} sandbox="allow-scripts" />
    </div>
  );
}

function AppearanceSection() {
  const [presets, setPresets] = useState<ThemePreset[]>([]);
  const [draft, setDraft] = useState<string | null>(null); // null until loaded
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    let live = true;
    Promise.all([
      invoke<ThemePreset[]>("theme_presets"),
      invoke<string>("read_theme"),
    ])
      .then(([ps, current]) => { if (live) { setPresets(ps); setDraft(current); } })
      .catch((e) => { if (live) setError(e.message ?? String(e)); });
    return () => { live = false; };
  }, []);

  if (draft === null) {
    return (
      <div className="config-section">
        <h2>This space — appearance</h2>
        <p className="config-empty">{error || "Loading theme…"}</p>
      </div>
    );
  }

  const save = async (css: string) => {
    setSaved(false);
    setError("");
    try {
      await invoke("write_theme", { css });
      setDraft(css);
      setSaved(true);
      window.dispatchEvent(new CustomEvent(THEME_EVENT, { detail: css }));
    } catch (e: any) {
      setError(e.message ?? String(e));
    }
  };

  return (
    <div className="config-section">
      <h2>This space — appearance</h2>
      <p className="config-hint">
        Lives in <code>theme.css</code> and styles how this space's <b>articles are rendered</b> —
        the emitted pages in <code>render/</code> and the editor's Preview. Tezuri's own chrome
        stays out of it. Presets propose; the specimen shows; saving writes the file. Or compose
        the CSS yourself — it is your file.
      </p>

      <div className="preset-row">
        <button className="preset-card" onClick={() => setDraft("")}>
          <span className="preset-name">Built-in</span>
          <span className="preset-desc">No theme file — the template's own look.</span>
        </button>
        {presets.map((p) => (
          <button key={p.id} className="preset-card" onClick={() => setDraft(p.css)}>
            <span className="preset-name">{p.name}</span>
            <span className="preset-desc">{p.description}</span>
          </button>
        ))}
      </div>

      <div className="appearance-grid">
        <label className="appearance-editor">
          theme.css draft
          <textarea
            className="theme-text"
            value={draft}
            spellCheck={false}
            onChange={(e) => setDraft(e.target.value)}
            rows={10}
          />
        </label>
        <div className="theme-specimen theme-scope" aria-label="Specimen of the rendered article">
          <style>{draft}</style>
          <div className="specimen-page">
            <span className="kicker">SPECIMEN</span>
            <h1 className="art-title">On Rust</h1>
            <p className="standfirst">A meditation on ownership.</p>
            <div className="metaline"><span>2026-08-26</span><span className="dot">·</span><span>5 min read</span></div>
            <div className="art-body">{SPECIMEN}</div>
          </div>
        </div>
      </div>

      <div className="row" style={{ marginTop: 10 }}>
        <button className="primary" onClick={() => save(draft)}>Save appearance</button>
        <button onClick={() => save("")}>Clear theme</button>
        {saved && <span className="mono-fact">saved ✓</span>}
        {error && <span className="detail-error">{error}</span>}
      </div>
    </div>
  );
}
