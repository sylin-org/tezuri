// The About surface: the deck card (placeholder art until the icon lands),
// live facts assembled from this device, and the reasoning links.

import { useEffect, useState } from "react";
import type React from "react";
import { invoke } from "./bridge";

export function About() {
  const facts = useFacts();

  // Links open through the shell's named command — the webview itself has
  // no opener authority, so a plain href would be a dead control.
  const openLink = (slug: string) => (e: React.MouseEvent) => {
    e.preventDefault();
    invoke("open_about_link", { slug }).catch(() => {});
  };

  return (
    <div className="about">
      <button className="tcg" type="button" aria-label="Tezuri — the local press">
        <span className="card-art" aria-hidden="true">
          <span className="mascot-halo" />
          {/* Placeholder glyph until the real icon lands. */}
          <span className="card-glyph">Tz</span>
        </span>
        <span className="card-divider" aria-hidden="true" />
        <span className="card-disc"><span className="ver">{facts.version}</span></span>
        <span className="card-notch" aria-hidden="true" />
        <span className="card-pane">
          <span className="card-name">Tezuri</span>
          <span className="card-epithet">THE LOCAL PRESS</span>
          <span className="card-bullets">
            <span className="card-bullet">Your publications live as plain files you own.</span>
            <span className="card-bullet">Write, prove, and publish under your own gates.</span>
            <span className="card-bullet">Nothing here ever leaves this machine silently.</span>
          </span>
          <span className="card-quote">“The desk is a lens; the files are the truth.”</span>
        </span>
      </button>

      <div className="about-body">
        <h1>Tezuri</h1>
        <p className="about-lede">
          A desk for an author's entire publishing life. It runs on this machine, over
          folders you already own, and stops exactly where you tell it to.
        </p>
        <dl className="about-facts">
          <div><dt>VERSION</dt><dd>{facts.version}</dd></div>
          <div><dt>SPACES</dt><dd>{facts.spaces} registered on this device</dd></div>
          <div><dt>ARTICLES</dt><dd>{facts.articles} across all spaces</dd></div>
          <div><dt>WORDS</dt><dd>{facts.words.toLocaleString()} written and kept</dd></div>
          <div><dt>ENGINE</dt><dd>Apache-2.0</dd></div>
          <div><dt>NETWORK</dt><dd>only when you explicitly fetch or push</dd></div>
        </dl>
        <p className="about-trust">
          Nothing on this page left this machine. No telemetry, no update pings, no accounts.
          Everything shown was assembled from work this device did.
        </p>

        <div className="about-links">
          <a href="https://github.com/sylin-org/tezuri" onClick={openLink("source")}>
            <b>Source</b><span>Every line of the press, and its decisions beside it.</span>
          </a>
          <a href="https://github.com/sylin-org/tezuri/blob/main/docs/PRODUCT-BRIEF.md" onClick={openLink("brief")}>
            <b>What it promises</b><span>The product brief: scope, invariants, refusals.</span>
          </a>
          <a href="https://github.com/sylin-org/tezuri/blob/main/docs/DECISIONS.md" onClick={openLink("decisions")}>
            <b>Why it is built this way</b><span>Every consequential decision, kept as written.</span>
          </a>
          <a href="https://ghostlight.sylin.org" onClick={openLink("ghostlight")}>
            <b>The rest of the toolkit</b><span>Ghostlight, the guardian this one grew up beside.</span>
          </a>
        </div>
      </div>
    </div>
  );
}

async function collectFacts(): Promise<{
  version: string; spaces: number; articles: number; words: number;
}> {
  const version = await invoke<string>("app_version").catch(() => "0.1");
  const reg = await invoke<{ publications: { root: string }[] }>("registry_load").catch(() => ({ publications: [] }));
  let articles = 0;
  let words = 0;
  for (const p of reg.publications) {
    try {
      const info = await invoke<{ articles: number; words: number }>("open_publication", { path: p.root });
      articles += info.articles;
      words += info.words;
    } catch { /* an unreadable space costs its counts, not the page */ }
  }
  return { version, spaces: reg.publications.length, articles, words };
}

export function useFacts() {
  const [facts, setFacts] = useState({ version: "…", spaces: 0, articles: 0, words: 0 });
  useEffect(() => {
    let live = true;
    collectFacts().then((f) => { if (live) setFacts(f); }).catch(() => {});
    return () => { live = false; };
  }, []);
  return facts;
}
