// The Write-mode composition: the space's template, projected live.
//
// Segments arrive from the desktop's composer in template order. Text runs
// are inert content; {{ARTICLE}} hosts the TipTap surface (first instance)
// or a read-only mirror of the same flow (repeats); each slot renders its
// projection — first-class fields (date, tags, cover) get real inline
// editors, everything else shows its evaluated value. One content state,
// many slots, no dual carets: mirrors are views, editors write to doc.

import React from "react";
import type { SlotInstance, WriteCompose } from "./bridge";
import { invoke } from "./bridge";
import { Writer } from "./Writer";

export interface ComposePlaneProps {
  compose: WriteCompose;
  markdown: string;
  slug: string;
  mediaBase: string;
  date: string | null;
  tags: string[] | null;
  cover: string | null;
  tagVocabulary: string[];
  words: number;
  onMarkdown: (md: string) => void;
  onMetaChange: (patch: { date?: string | null; tags?: string[]; cover?: string | null }) => void;
}

export function WriteComposePlane(p: ComposePlaneProps) {
  // The composer arrives one invoke after the article opens; until then the
  // plain writing surface stands in. A failed composition degrades to the
  // same thing — the page never breaks while the desk thinks.
  if (!p.compose || !Array.isArray(p.compose.segments)) {
    return (
      <div className="wc-editor-host">
        <Writer
          initialMarkdown={p.markdown}
          slug={p.slug}
          mediaBase={p.mediaBase}
          onChange={p.onMarkdown}
          words={p.words}
        />
      </div>
    );
  }
  return (
    <div className="write-composition">
      {p.compose.segments.map((seg, i) => {
        if (seg.kind === "text") {
          // Trusted bytes: the desktop composed them from the space's own
          // template through the slot engine, shell stripped.
          return <div key={i} className="wc-text" dangerouslySetInnerHTML={{ __html: seg.html }} />;
        }
        if (seg.kind === "article_flow") {
          return seg.mirror ? (
            <div key={i} className="wc-mirror-flow wc-text"
                 title="A second {{ARTICLE}} — a mirror of your writing"
                 aria-hidden="true">
              <p className="mono-fact">mirror of your article</p>
              <pre className="wc-mirror-pre">{p.markdown}</pre>
            </div>
          ) : (
            <div key={i} className="wc-editor-host">
              <Writer
                initialMarkdown={p.markdown}
                slug={p.slug}
                mediaBase={p.mediaBase}
                onChange={p.onMarkdown}
                words={p.words}
              />
            </div>
          );
        }
        return seg.editable
          ? <EditableSlot key={i} instance={seg} {...editPropsOf(seg.name, p)} />
          : <StaticSlot key={i} instance={seg} mediaBase={p.mediaBase} />;
      })}
    </div>
  );
}

function editPropsOf(name: string, p: ComposePlaneProps) {
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
function TagEditor({ tags, vocabulary, onChange }: {
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

/** Everything else shows its evaluated projection in place. */
function StaticSlot({ instance, mediaBase, innerHtml }: {
  instance?: SlotInstance;
  mediaBase?: string;
  innerHtml?: string;
}) {
  const raw = innerHtml ?? instance?.html ?? "";
  const html = mediaBase && !innerHtml ? raw.replaceAll("../media/", mediaBase) : raw;
  const name = innerHtml ? undefined : instance?.name;
  return (
    <span className={`wc-slot${instance?.mirror ? " wc-mirror" : ""}`}
          data-slot={name}
          dangerouslySetInnerHTML={{ __html: html }} />
  );
}

export type { WriteCompose };
