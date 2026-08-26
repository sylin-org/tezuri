// The landing surface: every registered authoring space, last-used featured.
// A space card is an invitation, not a form — open it, or manage it from its
// detail view later.

import { useEffect, useState } from "react";
import { invoke } from "./bridge";
import type { Identity } from "./bridge";

interface SpaceCard {
  root: string;
  cachedName: string;
  identity: Identity | null;
  info: { articles: number; words: number } | null;
}

export function Landing({
  pubs, lastOpened, onOpen, onAdd, onRemove, error,
}: {
  pubs: { name: string; persona: string; root: string }[];
  lastOpened: string | null;
  onOpen: (root: string) => void;
  onAdd: () => void;
  onRemove: (root: string) => void;
  error: string;
}) {
  const [cards, setCards] = useState<SpaceCard[]>([]);

  useEffect(() => {
    let live = true;
    (async () => {
      const enriched = await Promise.all(pubs.map(async (p) => {
        let identity: Identity | null = null;
        let info: SpaceCard["info"] = null;
        try {
          identity = await invoke<Identity>("read_identity", { path: p.root });
          // Cheap stats for the card: open_publication binds nothing, but it
          // rebuilds a desk. Acceptable at landing scale; cards show counts
          // from the last open when this feels slow.
        } catch { /* card renders from the registry cache */ }
        return { root: p.root, cachedName: p.name, identity, info };
      }));
      if (live) setCards(enriched);
    })();
    return () => { live = false; };
  }, [pubs]);

  const featured = cards.find((c) => c.root === lastOpened) ?? cards[0] ?? null;
  const rest = cards.filter((c) => c !== featured);
  const nameOf = (c: SpaceCard) => c.identity?.name || c.cachedName || c.root.split(/[\\/]/).pop() || c.root;
  const bylineOf = (c: SpaceCard) => c.identity?.byline || c.identity?.persona || "";

  return (
    <div className="landing">
      <p className="crumb">AUTHORING SPACES</p>
      <h1 className="landing-title">Your spaces</h1>
      <p className="landing-sub">Plain files, your voice, a press that never touches what you didn't approve.</p>
      {error && <p className="receipt" style={{ maxWidth: 480 }}>{error}</p>}

      {featured && (
        <>
          <p className="crumb" style={{ marginTop: 28 }}>LAST HERE</p>
          <button className="space-card featured" onClick={() => onOpen(featured.root)}>
            <span className="space-glyph" aria-hidden="true">{nameOf(featured).slice(0, 2).toUpperCase()}</span>
            <span className="space-card-body">
              <span className="space-name">{nameOf(featured)}</span>
              {bylineOf(featured) && <span className="space-byline">{bylineOf(featured)}</span>}
              <span className="space-mono">{featured.root}</span>
            </span>
            <span className="space-open-hint">Open →</span>
          </button>
        </>
      )}

      {rest.length > 0 && <p className="crumb" style={{ marginTop: 28 }}>EVERYWHERE ELSE</p>}
      <div className="space-grid">
        {rest.map((c) => (
          <button key={c.root} className="space-card" onClick={() => onOpen(c.root)}>
            <span className="space-glyph" aria-hidden="true">{nameOf(c).slice(0, 2).toUpperCase()}</span>
            <span className="space-card-body">
              <span className="space-name">{nameOf(c)}</span>
              {bylineOf(c) && <span className="space-byline">{bylineOf(c)}</span>}
              <span className="space-mono">{c.root}</span>
            </span>
          </button>
        ))}
        <button className="space-card ghost" onClick={onAdd}>
          <span className="space-glyph plus" aria-hidden="true">+</span>
          <span className="space-card-body">
            <span className="space-name">Add a space…</span>
            <span className="space-byline">a folder of Markdown articles</span>
          </span>
        </button>
      </div>

      {featured && (
        <button className="link-danger" onClick={() => onRemove(featured.root)}>
          Remove “{nameOf(featured)}” from this list…
        </button>
      )}
    </div>
  );
}
