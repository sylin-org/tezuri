// The Write-mode composition: the space's template, projected live.
//
// Segments arrive from the desktop's composer in template order. Write mode
// shows CONTENT ONLY: literal template runs are invisible structure (they
// carry the page layout Preview owns), while slot projections, the frame,
// and the editor at {{ARTICLE}} render in order. The toolbar lives above
// the plane via WriterProvider; the editable surface mounts wherever the
// composer said {{ARTICLE}} sits.

import React from "react";
import type { CatalogEntry, SlotInstance, WriteCompose } from "./bridge";
import { nextHintsFor } from "./bridge";
import { invoke } from "./bridge";

export interface ComposePlaneProps {
  compose: WriteCompose;
  markdown: string;
  mediaBase: string;
  date: string | null;
  tags: string[] | null;
  cover: string | null;
  tagVocabulary: string[];
  catalog: CatalogEntry[];
  /** The editable surface, mounted at the first {{ARTICLE}}. */
  editorSlot: React.ReactNode;
  /** Conduct one slot occurrence: draft splice + recompose, upstream. */
  onConduct: (raw: string, occurrence: number, hints: string[]) => void;
  /** Insert a new slot element beside rendered blocks (draft splice). */
  onInsert: (anchorRaw: string, anchorOcc: number, where: "before" | "after", name: string) => void;
  onMetaChange: (patch: { date?: string | null; tags?: string[]; cover?: string | null }) => void;
}

export function WriteComposePlane(p: ComposePlaneProps) {
  // Occurrence ordinals: identical raw expressions are menued individually,
  // so each render pass counts prior same-raw instances.
  const ordinal = React.useRef(new Map<string, number>());
  const ordOf = (raw: string): number => {
    const n = ordinal.current.get(raw) ?? 0;
    ordinal.current.set(raw, n + 1);
    return n;
  };
  const entryOf = (name: string): CatalogEntry | undefined =>
    p.catalog.find((e) => e.name === name);

  // The composer arrives one invoke after the article opens; until then the
  // bare editor stands in. A failed composition degrades the same way — the
  // page never breaks while the desk thinks.
  if (!p.compose || !Array.isArray(p.compose.segments)) {
    return <div className="write-composition">{p.editorSlot}</div>;
  }
  ordinal.current = new Map(); // re-seed per segment walk

  // Visible walk: content and projections with insertion rows between them.
  // Each row anchors to the nearest raw-bearing segment, so splicing stays
  // byte-honest even though scaffold text is invisible here.
  const out: React.ReactNode[] = [];
  let last: { raw: string; occ: number } | null = null;
  const pushInsert = (key: string, fallbackNext?: { raw: string; occ: number }) => {
    out.push(
      <InsertRow
        key={key}
        catalog={p.catalog}
        before={last}
        after={fallbackNext ?? null}
        onInsert={p.onInsert}
      />
    );
  };
  let pendingKey = 0;
  for (let i = 0; i < p.compose.segments.length; i++) {
    const seg = p.compose.segments[i];
    if (seg.kind === "text") continue;
    const occ =
      seg.kind === "slot" || seg.kind === "article_flow" ? ordOf(seg.raw) : -1;
    pushInsert(`ins-${pendingKey++}`, { raw: seg.raw, occ });
    last = { raw: seg.raw, occ };

    if (seg.kind === "article_flow") {
      const entry = entryOf("ARTICLE");
      const frame = seg.frame ? (
        <div
          key={`${i}-frame`}
          className="wc-frame"
          dangerouslySetInnerHTML={{ __html: seg.frame }}
        />
      ) : null;
      const chip =
        entry && entry.options.length > 0 && !seg.mirror ? (
          <SlotMenu
            key={`${i}-chip`}
            raw={seg.raw}
            hints={seg.hints}
            occurrence={occ}
            entry={entry}
            onApply={p.onConduct}
          />
        ) : null;
      if (seg.mirror) {
        out.push(
          <div key={i} className="wc-mirror-flow"
               title="A second {{ARTICLE}} — a mirror of your writing"
               aria-hidden="true">
            <p className="mono-fact">mirror of your article</p>
            <pre className="wc-mirror-pre">{p.markdown}</pre>
          </div>
        );
        continue;
      }
      out.push(
        <div key={i} className="wc-editor-wrap">
          <span className="wc-editor-tools">{chip}</span>
          {frame}
          <div className="wc-editor-host">{p.editorSlot}</div>
        </div>
      );
      continue;
    }
    out.push(
      seg.editable
        ? <EditableSlot key={i} instance={seg} {...editPropsOf(p)} />
        : <StaticSlot key={i} instance={seg} mediaBase={p.mediaBase}
                      catalogEntry={entryOf(seg.name)}
                      occurrence={occ} onConduct={p.onConduct} />
    );
  }
  pushInsert(`ins-${pendingKey++}`);

  return (
    <div className="write-composition">
      {out}
      {p.compose.notes.length > 0 && (
        <p className="wc-whispers" title="Editor notes from this template">
          {p.compose.notes.join(" · ")}
        </p>
      )}
    </div>
  );
}

/** A quiet band between blocks: reveals a + on approach, opening the
 *  insert palette grouped by the catalog's declared hosts. Each pick states
 *  its position relative to the nearest rendered neighbors. */
function InsertRow({ catalog, before, after, onInsert }: {
  catalog: CatalogEntry[];
  before: { raw: string; occ: number } | null;
  after: { raw: string; occ: number } | null;
  onInsert: (anchorRaw: string, anchorOcc: number, where: "before" | "after", name: string) => void;
}) {
  const [open, setOpen] = React.useState(false);
  const insertWith = (anchor: { raw: string; occ: number } | null, where: "before" | "after", name: string) => {
    if (!anchor) return;
    onInsert(anchor.raw, anchor.occ, where, name);
    setOpen(false);
  };
  const groups: [string, string][] = [["flow", "Flow"], ["rail", "Rail & furniture"]];
  return (
    <div className={`wc-insert${open ? " open" : ""}`}>
      <button type="button" className="wc-insert-btn"
              title="Insert an element here" aria-label="Insert an element here"
              onClick={() => setOpen((v) => !v)}>+</button>
      {open && (
        <div className="slot-menu wc-insert-menu" role="menu" aria-label="Insert an element">
          <span className="slot-menu-doc">
            Elements land beside your rendered blocks; saved only from the pinbar.
          </span>
          {!before && !after && (
            <span className="slot-menu-none">nothing to anchor to yet</span>
          )}
          {groups.map(([host, label]) => {
            const entries = catalog.filter((e) => e.name !== "ARTICLE" && e.hosts.includes(host));
            if (entries.length === 0) return null;
            return (
              <span className="insert-group" key={host}>
                <span className="slot-menu-label">{label}</span>
                {entries.map((e) => (
                  <span key={`${host}-${e.name}`} className="insert-pick-row">
                    <span className="insert-name">{e.name}</span>
                    {before && (
                      <button type="button" title={`Insert ${e.name} after ${before.raw}`}
                              onClick={() => insertWith(before, "after", e.name)}>
                        after ↑
                      </button>
                    )}
                    {after && (
                      <button type="button" title={`Insert ${e.name} before ${after.raw}`}
                              onClick={() => insertWith(after, "before", e.name)}>
                        before ↓
                      </button>
                    )}
                  </span>
                ))}
              </span>
            );
          })}
        </div>
      )}
    </div>
  );
}

/** Small anchored popover listing an entry's controls. Try-first: changes
 *  fire onConduct; nothing touches files until the desk saves the draft. */
function SlotMenu({ raw, hints, occurrence, entry, onApply }: {
  raw: string;
  hints: string[];
  occurrence: number;
  entry: CatalogEntry;
  onApply: (raw: string, occurrence: number, hints: string[]) => void;
}) {
  const [open, setOpen] = React.useState(false);
  const btnId = `conduct-${entry.name}-${occurrence}`;
  return (
    <span className="slot-menu-host">
      <button type="button" className="slot-menu-btn" id={btnId}
              title={`Conduct ${entry.name}`}
              onClick={() => setOpen((v) => !v)}>
        ⋯
      </button>
      {open && (
        <span className="slot-menu" role="menu" aria-label={`Options for ${entry.name}`}>
          <span className="slot-menu-doc">{entry.doc}</span>
          <SlotMenuControls raw={raw} hints={hints} occurrence={occurrence} entry={entry} onPick={(nh) => { onApply(raw, occurrence, nh); }} onClose={() => setOpen(false)} />
        </span>
      )}
    </span>
  );
}

function SlotMenuControls({ raw, hints, occurrence, entry, onPick, onClose }: {
  raw: string;
  hints: string[];
  occurrence: number;
  entry: CatalogEntry;
  onPick: (nextHints: string[]) => void;
  onClose: () => void;
}) {
  void raw; // hints fully determine next value; raw is identity upstream
  return (
    <>
      {entry.options.map((o) => (
        <span className="slot-menu-opt" key={o.key}>
          <span className="slot-menu-label">{o.label}</span>
          {o.control.kind === "toggle" ? (
            o.control.values.map((v) => (
              <button type="button" key={v}
                      className={hintValue(hints, o.key) === v ? "active" : ""}
                      onClick={() => { onPick(nextHintsFor(hints, o.key, v)); }}>
                {v}
              </button>
            ))
          ) : (
            o.control.values.map((v) => (
              <button type="button" key={v}
                      className={hintValue(hints, o.key) === v ? "active" : ""}
                      onClick={() => {
                        onPick(nextHintsFor(hints, o.key, v));
                        onClose();
                      }}>
                {o.key === "count" ? `${v} words` : v}
              </button>
            ))
          )}
        </span>
      ))}
      {entry.options.length === 0 && (
        <span className="slot-menu-none">no variations yet</span>
      )}
    </>
  );
}

function hintValue(hints: string[], key: string): string | undefined {
  for (const h of hints) {
    if (h.startsWith(`${key}:`)) return h.slice(key.length + 1);
    if (!h.includes(":")) return h; // bare form carries the single value
  }
  return undefined;
}

function editPropsOf(p: ComposePlaneProps) {
  return {
    date: p.date,
    tags: p.tags,
    cover: p.cover,
    mediaBase: p.mediaBase,
    tagVocabulary: p.tagVocabulary,
    onMetaChange: p.onMetaChange,
  };
}

type EditableCommon = {
  instance: SlotInstance;
  date: string | null;
  tags: string[] | null;
  cover: string | null;
  mediaBase: string;
  tagVocabulary: string[];
  onMetaChange: ComposePlaneProps["onMetaChange"];
};

/** Slots whose truth is a first-class meta field get real controls. */
function EditableSlot({ instance, ...v }: EditableCommon) {
  switch (instance.name) {
    case "date":
      return (
        <span className={`wc-slot ${instance.mirror ? "wc-mirror" : ""}`} data-slot="date">
          {!instance.mirror ? (
            <input
              type="date"
              value={v.date ?? ""}
              onChange={(e) => v.onMetaChange({ date: e.target.value || null })}
              aria-label="Publish date"
            />
          ) : (
            <StaticSlot innerHtml={instance.html} />
          )}
        </span>
      );
    case "tags": {
      if (instance.mirror)
        return (
          <span className="wc-slot wc-mirror" data-slot="tags">
            <TagView html={instance.html} />
          </span>
        );
      return (
        <span className="wc-slot" data-slot="tags">
          <TagEditor
            tags={v.tags ?? []}
            vocabulary={v.tagVocabulary}
            onChange={(next) => v.onMetaChange({ tags: next })}
          />
        </span>
      );
    }
    case "cover_img": {
      const preview = instance.html.replaceAll("../media/", v.mediaBase);
      return (
        <span className={`wc-slot ${instance.mirror ? "wc-mirror" : ""}`} data-slot="cover_img">
          <CoverEditor html={preview} hasCover={!!(v.cover && v.cover.trim())}
                       mediaBase={v.mediaBase}
                       onPick={(ref) => v.onMetaChange({ cover: ref })}
                       onClear={() => v.onMetaChange({ cover: null })} />
        </span>
      );
    }
    default:
      return <StaticSlot innerHtml={instance.html} />;
  }
}

async function storeMedia(file: File): Promise<string> {
  const buf = new Uint8Array(await file.arrayBuffer());
  return invoke<string>("add_media", { bytes: Array.from(buf), originalName: file.name });
}

/** The tag pill editor as its own component: hooks live here unconditionally. */
export function TagEditor({ tags, vocabulary, onChange }: {
  tags: string[];
  vocabulary: string[];
  onChange: (next: string[]) => void;
}) {
  const [draft, setDraft] = React.useState("");
  const suggestions = vocabulary.filter((t) => !tags.includes(t));
  return (
    <span className="tag-editor">
      {tags.map((t) => (
        <span className="tagpill" key={t}>
          #{t}
          <button
            type="button"
            className="tag-x"
            title={`Remove ${t}`}
            onClick={() => onChange(tags.filter((x) => x !== t))}
          >
            ×
          </button>
        </span>
      ))}
      <input
        list="tezuri-tag-vocab"
        value={draft}
        size={6}
        placeholder="+ tag"
        onChange={(e) => setDraft(e.target.value)}
        onKeyDown={(e) => {
          if ((e.key === "Enter" || e.key === ",") && draft.trim()) {
            e.preventDefault();
            onChange([...new Set([...tags, draft.trim().toLowerCase()])]);
            setDraft("");
          }
        }}
        aria-label="Add a tag"
      />
      <datalist id="tezuri-tag-vocab">
        {suggestions.map((s) => (
          <option key={s} value={s} />
        ))}
      </datalist>
    </span>
  );
}

/** Cover picker: replace via file chooser, clear with one click. Files go
 *  into the session's media store; the doc keeps only the reference. */
function CoverEditor({ html, hasCover, mediaBase, onPick, onClear }: {
  html: string;
  hasCover: boolean;
  mediaBase: string;
  onPick: (ref: string) => void;
  onClear: () => void;
}) {
  const inputRef = React.useRef<HTMLInputElement>(null);
  const [busy, setBusy] = React.useState(false);
  return (
    <span className="cover-editor">
      <span dangerouslySetInnerHTML={{ __html: html || "" }} />
      <input
        ref={inputRef}
        type="file"
        accept="image/*"
        hidden
        onChange={async (e) => {
          const f = e.target.files?.[0];
          if (!f) return;
          setBusy(true);
          try { onPick(await storeMedia(f)); } finally { setBusy(false); e.target.value = ""; }
        }}
      />
      <span className="row" style={{ gap: 6 }}>
        <button type="button" disabled={busy} onClick={() => inputRef.current?.click()}>
          {busy ? "storing…" : "choose cover…"}
        </button>
        {hasCover && (
          <button type="button" onClick={onClear} title="Remove the cover">clear</button>
        )}
      </span>
    </span>
  );
}

function TagView({ html }: { html: string }) {
  return <span dangerouslySetInnerHTML={{ __html: html }} />;
}

/** Everything else shows its evaluated projection in place — and, when the
 *  catalog declares controls, a conduct affordance beside it. */
function StaticSlot({ instance, mediaBase, innerHtml, catalogEntry, occurrence, onConduct }: {
  instance?: SlotInstance;
  mediaBase?: string;
  innerHtml?: string;
  catalogEntry?: CatalogEntry;
  occurrence?: number;
  onConduct?: ComposePlaneProps["onConduct"];
}) {
  const raw = innerHtml ?? instance?.html ?? "";
  const html = mediaBase && !innerHtml ? raw.replaceAll("../media/", mediaBase) : raw;
  const name = innerHtml ? undefined : instance?.name;
  const menu =
    instance && !instance.mirror && catalogEntry && occurrence !== undefined && onConduct ? (
      <SlotMenu
        raw={instance.raw}
        hints={instance.hints}
        occurrence={occurrence}
        entry={catalogEntry}
        onApply={onConduct}
      />
    ) : null;
  return (
    <span className={`wc-slot${instance?.mirror ? " wc-mirror" : ""}`} data-slot={name}>
      <span dangerouslySetInnerHTML={{ __html: html }} />
      {menu}
      {!innerHtml && !html.trim() && (
        <span className="wc-ghost">{`{{${name}}}`}</span>
      )}
    </span>
  );
}

export type { WriteCompose };
