//! Space Details: the one surface that configures a space.
//!
//! Identity facts (name, byline, persona), the header-style decision
//! (Normal / Banner), whether tags participate and their vocabulary, the
//! space cover, and the theme + template pickers with their history
//! arrows. Form fields save together on Save; picker applications happen
//! immediately (they are deliberate, individually confirmed acts).

import React from "react";
import { invoke } from "./bridge";

interface PickerEntry {
  id: string;
  name: string;
  creator: string;
  source: string;
  description: string;
}

export interface SpaceDetailsData {
  name: string;
  byline: string;
  persona: string;
  cover: string | null;
  header_style: string;
  tags_enabled: boolean;
  tag_vocabulary: string[] | null;
}

export function SpaceDetails({ root, onSaved }: {
  root: string;
  onSaved: () => void;
}) {
  const [d, setD] = React.useState<SpaceDetailsData | null>(null);
  const [themes, setThemes] = React.useState<PickerEntry[]>([]);
  const [templates, setTemplates] = React.useState<PickerEntry[]>([]);
  const [history, setHistory] = React.useState<[number, number]>([0, 0]);
  const [status, setStatus] = React.useState("");
  const [error, setError] = React.useState("");
  const [newTag, setNewTag] = React.useState("");
  const [confirmPick, setConfirmPick] = React.useState<string | null>(null);
  const [downloadUrl, setDownloadUrl] = React.useState("");

  const load = React.useCallback(async () => {
    try {
      const [identity, list, hist] = await Promise.all([
        invoke<any>("read_identity", { path: root }),
        invoke<any>("picker_list"),
        invoke<any>("picker_history", {}) as Promise<[number, number]>,
      ]);
      setD({
        name: identity.name ?? "",
        byline: identity.byline ?? "",
        persona: identity.persona ?? "",
        cover: identity.cover ?? null,
        header_style: identity.header_style || "normal",
        tags_enabled: identity.tags_enabled ?? true,
        tag_vocabulary: identity.tag_vocabulary ?? [],
      });
      setThemes(list.themes ?? []);
      setTemplates(list.templates ?? []);
      setHistory(hist ?? [0, 0]);
    } catch (e: any) {
      setError(e.message ?? String(e));
    }
  }, [root]);

  React.useEffect(() => { void load(); }, [load]);

  const patch = (p: Partial<SpaceDetailsData>) => setD((v) => (v ? { ...v, ...p } : v));

  const save = async () => {
    if (!d) return;
    try {
      await invoke("save_identity", {
        path: root,
        identity: {
          name: d.name,
          byline: d.byline,
          persona: d.persona,
          cover: d.cover,
          header_style: d.header_style,
          tags_enabled: d.tags_enabled,
          tag_vocabulary: d.tags_enabled ? (d.tag_vocabulary ?? []) : [],
        },
      });
      setStatus("Space details saved.");
      onSaved();
    } catch (e: any) {
      setError(e.message ?? String(e));
    }
  };

  const pick = async (kind: "theme" | "template", entry: PickerEntry) => {
    try {
      await invoke("picker_apply", { kind, id: entry.id });
      setStatus(`${entry.name} ${kind} applied — the files are yours now.`);
      setConfirmPick(null);
      const hist = await invoke<any>("picker_history", {});
      setHistory(hist ?? [0, 0]);
    } catch (e: any) {
      setError(e.message ?? String(e));
    }
  };

  const step = async (kind: "theme" | "template", delta: number) => {
    try {
      await invoke("picker_history_step", { kind, delta });
      setStatus("Reverted one step in the presentation history.");
    } catch (e: any) {
      setError(e.message ?? String(e));
    }
  };

  const fetchAsset = async () => {
    if (!downloadUrl.trim()) return;
    try {
      await invoke("download_asset", { url: downloadUrl.trim() });
      setDownloadUrl("");
      const list = await invoke<any>("picker_list");
      setThemes(list.themes ?? []);
      setTemplates(list.templates ?? []);
      setStatus("Asset downloaded — it is now in the picker.");
    } catch (e: any) {
      setError(e.message ?? String(e));
    }
  };

  const pickCover = async () => {
    try {
      const file = await invoke<string | null>("pick_image_file");
      if (!file) return;
      const bytes: number[] = await invoke("read_file_bytes", { path: file });
      const ref = await invoke<string>("add_media", {
        bytes,
        originalName: file.split(/[\\/]/).pop() ?? "cover",
      });
      patch({ cover: ref });
    } catch (e: any) {
      setError(e.message ?? String(e));
    }
  };

  const addTag = () => {
    const t = newTag.trim();
    if (!t || !d) return;
    patch({ tag_vocabulary: Array.from(new Set([...(d.tag_vocabulary ?? []), t])) });
    setNewTag("");
  };

  const removeTag = (t: string) => {
    patch({ tag_vocabulary: (d?.tag_vocabulary ?? []).filter((x) => x !== t) });
  };

  if (error && !d) return <p className="config-error">{error}</p>;
  if (!d) return null;

  const arrow = (kind: "theme" | "template", idx: number) => (
    <span className="picker-arrows">
      <button title="Step back" disabled={history[idx] < 2}
              onClick={() => step(kind, -1)}>‹</button>
      <button title="Step forward" disabled={history[idx] >= 2 && true}
              onClick={() => step(kind, +1)}>›</button>
    </span>
  );

  const pickerList = (kind: "theme" | "template", entries: PickerEntry[]) => (
    <div className="picker-list">
      {entries.map((e) => (
        <div className="picker-row" key={e.id}>
          <div>
            <b>{e.name}</b>
            <span className="config-hint"> — {e.creator} · {e.description}</span>
          </div>
          {!confirmPick || confirmPick !== e.id ? (
            <button onClick={() => { setConfirmPick(e.id); setError(""); }}>Pick</button>
          ) : (
            <span className="row" style={{ gap: 6 }}>
              <button className="primary" onClick={() => pick(kind, e)}>Apply</button>
              <button onClick={() => setConfirmPick(null)}>Keep mine</button>
            </span>
          )}
        </div>
      ))}
    </div>
  );

  return (
    <div className="config-section space-details">
      <h2>Space Details</h2>

      <label className="field">Name
        <input value={d.name} onChange={(e) => patch({ name: e.target.value })} />
      </label>
      <label className="field">Author / byline
        <input value={d.byline} onChange={(e) => patch({ byline: e.target.value })} />
      </label>
      <label className="field">Persona
        <input value={d.persona} onChange={(e) => patch({ persona: e.target.value })} />
      </label>

      <div className="field">Header style
        <span className="row">
          <label><input type="radio" checked={d.header_style !== "banner"}
                        onChange={() => patch({ header_style: "normal" })} /> Normal</label>
          <label><input type="radio" checked={d.header_style === "banner"}
                        onChange={() => patch({ header_style: "banner" })} /> Banner</label>
        </span>
        <span className="config-hint">
          Banner lets the template feed its hero from the article's first two
          lines (title + standfirst) and lifts them out of the body.
        </span>
      </div>

      <div className="field">Tags
        <label className="row">
          <input type="checkbox" checked={d.tags_enabled}
                 onChange={(e) => patch({ tags_enabled: e.target.checked })} />
          Tags participate in this space
        </label>
        {d.tags_enabled && (
          <>
            <div className="tag-vocab">
              {(d.tag_vocabulary ?? []).map((t) => (
                <span className="tagpill" key={t}>
                  #{t}
                  <button title={`Remove ${t}`}
                          onClick={() => removeTag(t)}>
                    ×
                  </button>
                </span>
              ))}
            </div>
            <span className="row">
              <input placeholder="add a tag" value={newTag}
                     onChange={(e) => setNewTag(e.target.value)}
                     onKeyDown={(e) => { if (e.key === "Enter") addTag(); }} />
              <button onClick={addTag}>Add</button>
            </span>
          </>
        )}
      </div>

      <div className="field">Space cover
        <span className="row">
          {d.cover && <span className="mono-fact">{d.cover}</span>}
          <button onClick={pickCover}>{d.cover ? "Replace cover" : "Choose cover…"}</button>
          {d.cover && <button onClick={() => patch({ cover: null })}>Clear</button>}
        </span>
      </div>

      <div className="field">Add an asset from the web
        <span className="row">
          <input placeholder="https://… .css or .html file" value={downloadUrl}
                 onChange={(e) => setDownloadUrl(e.target.value)} />
          <button onClick={fetchAsset} disabled={!downloadUrl.trim()}>Fetch</button>
        </span>
        <span className="config-hint">
          One .css or .html file per URL — it lands in the picker's downloads.
        </span>
      </div>

      <h3>Theme</h3>
      {arrow("theme", 0)}
      {pickerList("theme", themes)}

      <h3>Template</h3>
      {arrow("template", 1)}
      {pickerList("template", templates)}

      <span className="row">
        <button className="primary" onClick={save}>Save</button>
        {status && <span className="receipt">{status}</span>}
        {error && <span className="config-error">{error}</span>}
      </span>
    </div>
  );
}
