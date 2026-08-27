// The Write-mode composition: the space's template, projected live.
//
// Segments arrive from the desktop's composer in template order. Write mode
// shows CONTENT ONLY: literal template runs are invisible structure (they
// carry the page layout Preview owns), while slot projections, the frame,
// and the editor at {{ARTICLE}} render in order. The toolbar lives above
// the plane via WriterProvider; the editable surface mounts wherever the
// composer said {{ARTICLE}} sits.

import React from "react";
import { createPortal } from "react-dom";
import type { CatalogEntry, Seg, SlotInstance, WriteCompose } from "./bridge";
import { nextHintsFor, scopeCss } from "./bridge";
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

  return <StructuredPlane {...p} ordOf={ordOf} entryOf={entryOf} />;
}

/** The artifact's body, worn for real: the composed structure renders once
 *  (grid, rails, literal labels) and each slot occurrence portals its widget
 *  into the mount point the composer left in the structure. Re-conduct or
 *  save swaps the structure only when its bytes change, so the editor's
 *  mount survives ordinary saves. */
function StructuredPlane(p: ComposePlaneProps & {
  ordOf: (raw: string) => number;
  entryOf: (name: string) => CatalogEntry | undefined;
}) {
  const hostRef = React.useRef<HTMLDivElement | null>(null);
  const [tick, setTick] = React.useState(0);
  const [dbg, setDbg] = React.useState("");
  const bodyHtml = p.compose.body_html;
  // Portals may only query mounts after the structure for THIS body_html is
  // committed; the tick re-renders once per structure change.
  React.useEffect(() => {
    setTick((v) => v + 1);
  }, [bodyHtml]);
  // TEMP diagnostics — remove before commit.
  React.useEffect(() => {
    const t = window.setInterval(() => {
      const g = hostRef.current?.querySelector(".pagegrid");
      const a = hostRef.current?.querySelector(".toc-rail");
      if (g && a) {
        const gs = getComputedStyle(g);
        const r = a.getBoundingClientRect();
        const kids = Array.from(g.children).map((c) => {
          const cs2 = getComputedStyle(c as Element);
          const cr = (c as Element).getBoundingClientRect();
          return `${(c as Element).className || c.nodeName}[${cs2.display}] col=${cs2.gridColumn} row=${cs2.gridRow} y=${Math.round(cr.y)} h=${Math.round(cr.height)}`;
        });
        setDbg(
          `DBG grid=${gs.display} cols=${gs.gridTemplateColumns} aside=${Math.round(r.x)},${Math.round(r.y)} ${Math.round(r.width)}x${Math.round(r.height)} vw=${window.innerWidth} kids=${kids.join(" ;; ")}`
        );
        window.clearInterval(t);
      }
    }, 400);
    return () => window.clearInterval(t);
  }, [tick]);

  const interceptClicks = (e: React.MouseEvent) => {
    const a = (e.target as HTMLElement).closest("a");
    if (!a) return;
    const href = a.getAttribute("href") ?? "";
    if (href.startsWith("#")) {
      // The artifact's own anchor (toc) scrolls; it never navigates the app.
      e.preventDefault();
      hostRef.current
        ?.querySelector(href)
        ?.scrollIntoView({ behavior: "smooth", block: "start" });
    } else if (href.endsWith(".html")) {
      e.preventDefault(); // cross-article links stay inert in the plane
    }
  };

  const widget = (seg: Exclude<Seg, { kind: "text" }>, occ: number): React.ReactNode => {
    if (seg.kind === "article_flow") {
      const entry = p.entryOf("ARTICLE");
      const frame = seg.frame ? (
        <div
          className="wc-frame"
          dangerouslySetInnerHTML={{
            __html: p.mediaBase
              ? seg.frame.replaceAll("../media/", p.mediaBase)
              : seg.frame,
          }}
        />
      ) : null;
      const chip =
        entry && entry.options.length > 0 && !seg.mirror ? (
          <SlotMenu
            raw={seg.raw}
            hints={seg.hints}
            occurrence={occ}
            entry={entry}
            onApply={p.onConduct}
          />
        ) : null;
      if (seg.mirror) {
        return (
          <div className="wc-mirror-flow"
               title="A second {{ARTICLE}} — a mirror of your writing"
               aria-hidden="true">
            <p className="mono-fact">mirror of your article</p>
            <pre className="wc-mirror-pre">{p.markdown}</pre>
          </div>
        );
      }
      return (
        <div className="wc-editor-wrap">
          <span className="wc-editor-tools">{chip}</span>
          {frame}
          <div className="wc-editor-host">{p.editorSlot}</div>
        </div>
      );
    }
    return seg.editable
      ? <EditableSlot instance={seg} {...editPropsOf(p)} />
      : <StaticSlot instance={seg} mediaBase={p.mediaBase}
                    catalogEntry={p.entryOf(seg.name)}
                    occurrence={occ} onConduct={p.onConduct} />;
  };

  // Widget manifests in order, portaled into their mounts. A mount that the
  // structure lacks (attribute-nested slots and other shell tricks) falls
  // back to flat rendering — the page never breaks.
  const portals: React.ReactNode[] = [];
  const fallbacks: React.ReactNode[] = [];
  let last: { raw: string; occ: number } | null = null;
  if (tick > 0 && hostRef.current) {
    const host = hostRef.current;
    for (let i = 0; i < p.compose.segments.length; i++) {
      const seg = p.compose.segments[i];
      if (seg.kind === "text") continue;
      const occ = p.ordOf(seg.raw);
      const w = widget(seg, occ);
      // First raw-bearing segment anchors a "before" row at the top; the
      // rest anchor "after" the previous segment — same splice positions
      // the flat walk used.
      const row = last ? (
        <InsertRow key={`ins-${i}`} catalog={p.catalog} before={last}
                   after={null} onInsert={p.onInsert} />
      ) : (
        <InsertRow key={`ins-${i}`} catalog={p.catalog} before={null}
                   after={{ raw: seg.raw, occ }} onInsert={p.onInsert} />
      );
      last = { raw: seg.raw, occ };
      const target = host.querySelector(`[data-tz="${seg.mount}"]`);
      if (target) {
        portals.push(createPortal(<>{row}{w}</>, target, `tz-${seg.mount}`));
      } else {
        fallbacks.push(<React.Fragment key={`fb-${i}`}>{row}{w}</React.Fragment>);
      }
    }
  }

  const planeClass = p.compose.body_class
    ? `write-composition ${p.compose.body_class}`
    : "write-composition";

  return (
    <div className={planeClass}>
      {p.compose.css && (
        <style
          key={p.compose.css.length}
          dangerouslySetInnerHTML={{
            __html: scopeCss(p.compose.css, ".write-composition"),
          }}
        />
      )}
      <div
        ref={hostRef}
        className="wc-structure"
        onClick={interceptClicks}
        dangerouslySetInnerHTML={{
          __html: p.mediaBase
            ? bodyHtml.replaceAll("../media/", p.mediaBase)
            : bodyHtml,
        }}
      />
      {portals}
      {fallbacks.length > 0 && <div className="wc-fallback">{fallbacks}</div>}
      {dbg && <p className="mono-fact">{dbg}</p>}
      <InsertRow key="ins-tail" catalog={p.catalog} before={last} after={null}
                 onInsert={p.onInsert} />
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
