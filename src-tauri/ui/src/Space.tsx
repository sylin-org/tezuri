// The space view: identity rail on the left, workspace as the main plane.
// The rail carries the space's identity, its momentum, and the searchable
// body of work. Characteristics and assistants live in Configuration; this
// rail reads and navigates.

import { useMemo, useState } from "react";
import type { DeskEntry, Identity } from "./bridge";

export type Workspace = { kind: "article"; slug: string } | null;

export function SpaceRail({
  identity, desk, activeSlug,
  onOpenArticle, onNewArticle,
}: {
  identity: Identity | null;
  desk: { entries: DeskEntry[] };
  activeSlug: string | null;
  onOpenArticle: (slug: string) => void;
  onNewArticle: () => void;
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

  return (
    <section id="desk" aria-label="Space rail">
      <div className="rail-identity">
        <span className="space-glyph big" aria-hidden="true">{name.slice(0, 2).toUpperCase()}</span>
        <div className="rail-id-text">
          <span className="space-name">{name}</span>
          {(identity?.byline || identity?.persona) &&
            <span className="space-byline">{identity.byline || identity.persona}</span>}
        </div>
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
          onChange={(e) => setQ(e.target.value)}
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
