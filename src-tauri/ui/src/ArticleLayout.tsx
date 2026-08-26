// ArticleLayout: the reading surface as a first-class composition.
//
// Borrowed anatomy (gposingway/Substack prior art):
//   - hero banner from `cover` frontmatter, gradient-faded into the page
//   - title block: standfirst, date · read-time · tag pills
//   - body column + sticky right sidebar: TOC with scroll-spy
//
// Separation of concerns:
//   content  = Markdown + frontmatter (files)
//   derived  = read time, TOC anchors, word count (computed here, never stored)
//   theme    = CSS custom properties loaded from <publication>/theme.css

import { useEffect, useMemo, useState } from "react";
import type { ReactNode } from "react";

export interface LayoutMeta {
  slug: string;
  title: string;
  standfirst?: string | null;
  cover?: string | null;
  date?: string | null;
  tags?: string[] | null;
}

/** Read time at 220 wpm — the standard Substack/medium heuristic. */
export function readTime(words: number): number {
  return Math.max(1, Math.ceil(words / 220));
}

/** Extract H2/H3 headings with stable anchor ids for the TOC. */
export function useToc(html: string): { id: string; text: string; level: number }[] {
  return useMemo(() => {
    const out: { id: string; text: string; level: number }[] = [];
    if (!html) return out;
    const doc = new DOMParser().parseFromString(html, "text/html");
    doc.querySelectorAll("h2, h3").forEach((h, i) => {
      const id = `sec-${i}-${h.textContent?.toLowerCase().replace(/[^a-z0-9]+/g, "-").slice(0, 40)}`;
      h.id = id;
      out.push({ id, text: h.textContent ?? "", level: h.tagName === "H2" ? 2 : 3 });
    });
    // Re-serialize so the ids land in the rendered output.
    html.replace(/x/, "x"); // no-op; caller uses processedHtml below
    return out;
  }, [html]);
}

/** Scroll-spy: returns the id of the heading nearest the viewport top. */
export function useScrollSpy(ids: string[]): string | null {
  const [active, setActive] = useState<string | null>(null);
  useEffect(() => {
    if (ids.length === 0) return;
    const obs = new IntersectionObserver(
      (entries) => {
        for (const e of entries) {
          if (e.isIntersecting) setActive(e.target.id);
        }
      },
      { rootMargin: "-80px 0px -70% 0px", threshold: 0 }
    );
    ids.forEach((id) => {
      const el = document.getElementById(id);
      if (el) obs.observe(el);
    });
    return () => obs.disconnect();
  }, [ids.join("|")]);
  return active;
}

interface Props {
  meta: LayoutMeta;
  words: number;
  /** Rendered body HTML with TOC ids injected (from useToc's processing). */
  children: ReactNode;
}

export function ArticleLayout({ meta, words, children }: Props) {
  const minutes = readTime(words);
  const dateStr = meta.date
    ? new Date(meta.date + "T00:00:00").toLocaleDateString(undefined, {
        year: "numeric", month: "short", day: "numeric",
      })
    : null;

  return (
    <div className="article-layout">
      {meta.cover && (
        <div className="hero" style={{ backgroundImage: `url(${meta.cover})` }}>
          <div className="hero-fade" />
        </div>
      )}

      <div className="article-columns">
        <article className="article-main">
          <header className="title-block">
            <h1 className="doc-title">{meta.title}</h1>
            {meta.standfirst && <p className="standfirst">{meta.standfirst}</p>}
            <div className="metaline">
              {dateStr && <span>{dateStr}</span>}
              <span className="dot">·</span>
              <span>{minutes} min read</span>
              {(meta.tags?.length ?? 0) > 0 && (
                <>
                  <span className="dot">·</span>
                  {meta.tags!.map((t) => (
                    <span key={t} className="tag-pill">#{t}</span>
                  ))}
                </>
              )}
            </div>
          </header>
          {children}
        </article>

        <TocSidebar />
      </div>
    </div>
  );
}

function TocSidebar() {
  const [toc, setToc] = useState<{ id: string; text: string; level: number }[]>([]);
  const active = useScrollSpy(toc.map((t) => t.id));

  useEffect(() => {
    // Collect headings after the body renders.
    const t = setTimeout(() => {
      const heads = document.querySelectorAll(".article-main h2, .article-main h3");
      const list: { id: string; text: string; level: number }[] = [];
      heads.forEach((h, i) => {
        if (!h.id) {
          h.id = `sec-${i}-${h.textContent?.toLowerCase().replace(/[^a-z0-9]+/g, "-").slice(0, 40)}`;
        }
        list.push({
          id: h.id,
          text: h.textContent ?? "",
          level: h.tagName === "H2" ? 2 : 3,
        });
      });
      setToc(list);
    }, 300);
    return () => clearTimeout(t);
  });

  if (toc.length === 0) return null;

  return (
    <aside className="toc-sidebar">
      <div className="toc-inner">
        <h2>In this article</h2>
        <nav>
          {toc.map((t) => (
            <a key={t.id} href={`#${t.id}`}
               className={`toc-link toc-l${t.level}${active === t.id ? " current" : ""}`}>
              {t.text}
            </a>
          ))}
        </nav>
      </div>
    </aside>
  );
}
