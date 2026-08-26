// The space view: identity rail on the left, workspace as the main plane.
// The rail carries the space's characteristics, its momentum, and the
// searchable body of work. Selecting detail/article swaps the workspace,
// never the whole window.

import { useMemo, useState } from "react";
import type { DeskEntry, Identity } from "./bridge";

export type Workspace =
  | { kind: "detail" }
  | { kind: "article"; slug: string | null } // null = new, unsaved
  | null;

export function SpaceRail({
  identity, desk, activeSlug, workspace,
  onOpenArticle, onNewArticle, onShowDetail, onSearch,
}: {
  identity: Identity | null;
  desk: { entries: DeskEntry[] };
  activeSlug: string | null;
  workspace: Workspace;
  onOpenArticle: (slug: string) => void;
  onNewArticle: () => void;
  onShowDetail: () => void;
  onSearch: (q: string) => void;
}) {
  const [q, setQ] = useState("");
  const entries = useMemo(() => {
    const needle = q.trim().toLowerCase();
    if (!needle) return desk.entries;
    return desk.entries.filter(
      (e) => e.title.toLowerCase().includes(needle) || e.slug.includes(needle)
    );
  }, [desk.entries, q]);

  const counts = useMemo(() => ({
    draft: desk.entries.filter((e) => e.state === "draft").length,
    review: desk.entries.filter((e) => e.state === "review").length,
    published: desk.entries.filter((e) => e.state === "published").length,
    words: desk.entries.reduce((n, e) => n + e.words, 0),
  }), [desk.entries]);

  const name = identity?.name || "This space";
  const detailActive = workspace?.kind === "detail";

  return (
    <section id="desk" aria-label="Space rail">
      <div className="rail-identity">
        <span className="space-glyph big" aria-hidden="true">{name.slice(0, 2).toUpperCase()}</span>
        <div className="rail-id-text">
          <span className="space-name">{name}</span>
          {(identity?.byline || identity?.persona) &&
            <span className="space-byline">{identity.byline || identity.persona}</span>}
        </div>
        <button className={`rail-tab${detailActive ? " active" : ""}`} onClick={onShowDetail}>
          {detailActive ? "Viewing details" : "Details"}
        </button>
      </div>

      <div className="rail-momentum" role="group" aria-label="Momentum">
        <span className="mono-fact"><b>{counts.draft}</b> drafts</span>
        <span className="mono-fact"><b>{counts.review}</b> review</span>
        <span className="mono-fact"><b>{counts.published}</b> live</span>
        <span className="mono-fact"><b>{counts.words.toLocaleString()}</b> words</span>
      </div>

      <div className="row" style={{ margin: "10px 0 8px" }}>
        <input
          className="rail-search"
          placeholder="Search this space…"
          value={q}
          onChange={(e) => { setQ(e.target.value); onSearch(e.target.value); }}
          aria-label="Search articles"
        />
      </div>

      <div className="rail-list">
        {entries.map((e) => (
          <button
            key={e.slug}
            className={`entry ${activeSlug === e.slug ? "active" : ""}`}
            onClick={() => onOpenArticle(e.slug)}
          >
            <span className="t">{e.title}</span>
            <span className="m">
              <span className={`state-${e.state}`}>{e.state}</span> · {e.words}w
              {e.dangling_links.length > 0 && <> · ⚠ {e.dangling_links.join(", ")}</>}
            </span>
          </button>
        ))}
        {entries.length === 0 && (
          <p className="rail-empty">{q ? "nothing matches" : "no articles yet"}</p>
        )}
      </div>

      <button className="rail-new" onClick={onNewArticle}>+ New article</button>
    </section>
  );
}

export function SpaceDetail({
  identity, onSave, busy, error,
}: {
  identity: Identity | null;
  onSave: (next: Identity) => void;
  busy: boolean;
  error: string;
}) {
  const [form, setForm] = useState<Identity>(
    () => identity ?? { name: "", byline: "", persona: "" }
  );
  const set = (k: keyof Identity, v: string) => setForm({ ...form, [k]: v });

  return (
    <div className="detail-plane" role="form" aria-label="Space details">
      <p className="crumb">SPACE DETAILS</p>
      <h1 className="detail-title">{identity?.name || "Name this space"}</h1>
      <p className="detail-sub">
        These characteristics live in <code>publication.yaml</code> inside the space — plain
        files, yours. Anything else you keep in that file is preserved untouched.
      </p>
      <div className="detail-form">
        <label>Name
          <input value={form.name} onChange={(e) => set("name", e.target.value)}
                 placeholder="Kintsugi" />
        </label>
        <label>Byline
          <input value={form.byline} onChange={(e) => set("byline", e.target.value)}
                 placeholder="words and photographs by…" />
        </label>
        <label>Persona
          <input value={form.persona} onChange={(e) => set("persona", e.target.value)}
                 placeholder="who writes here" />
        </label>
        <div className="row" style={{ marginTop: 14 }}>
          <button className="primary" disabled={busy}
                  onClick={() => onSave(form)}>
            {busy ? "Saving…" : "Save details"}
          </button>
          {error && <span className="detail-error">{error}</span>}
        </div>
      </div>
    </div>
  );
}
