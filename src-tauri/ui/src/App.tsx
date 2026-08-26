import React, { useCallback, useEffect, useRef, useState } from "react";
import CodeMirror from "@uiw/react-codemirror";
import { markdown, markdownLanguage } from "@codemirror/lang-markdown";
import { languages } from "@codemirror/language-data";
import { Writer } from "./Writer";

function tauri(): any {
  const t = (window as any).__TAURI__;
  if (!t?.core?.invoke) throw new Error("Tauri bridge not ready");
  return t.core;
}

type Doc = { slug: string; title: string; state: string; standfirst?: string | null;
  cover?: string | null; date?: string | null; tags?: string[] | null };

export default function App() {
  const [opened, setOpened] = useState(false);
  const [pubInfo, setPubInfo] = useState("");
  const [pubError, setPubError] = useState("");
  const [assistantList, setAssistantList] = useState<string[]>([]);
  const [showFirstRun, setShowFirstRun] = useState(false);
  const [entries, setEntries] = useState<any[]>([]);
  const [doc, setDoc] = useState<Doc | null>(null);
  const [text, setText] = useState("");
  const [assistOpen, setAssistOpen] = useState(false);
  const [consultOut, setConsultOut] = useState("advisory only — nothing enters the document until you accept it");
  const [proof, setProof] = useState<{ verdict: string; evidence: string } | null>(null);
  const [changes, setChanges] = useState<any[]>([]);
  const [selPaths, setSelPaths] = useState<Set<string>>(new Set());
  const [sourceMode, setSourceMode] = useState(false);
  const [saveStatus, setSaveStatus] = useState<"saved" | "saving" | "dirty">("saved");
  const [settingsOpen, setSettingsOpen] = useState(false);
  // Durable receipts for ship actions — never a vanishing toast (contract).
  const [note, setNote] = useState("");

  const autosaveTimer = useRef<number | null>(null);
  const dirtyRef = useRef(false);

  // Latest values, reachable from stable callbacks without effect churn.
  const docRef = useRef(doc);
  docRef.current = doc;
  const textRef = useRef(text);
  textRef.current = text;

  const refreshDesk = useCallback(async () => {
    const d = await tauri().invoke("desk");
    setEntries(d.entries);
  }, []);

  /** Persist the article verbatim (the flow IS the file). */
  const flush = useCallback(async () => {
    const d = docRef.current;
    if (!d) return;
    setSaveStatus("saving");
    try {
      await tauri().invoke("save_article", {
        article: {
          meta: {
            slug: d.slug,
            state: d.state,
            date: d.date ?? null,
            tags: d.tags ?? [],
            cover: d.cover ?? null,
            standfirst: null,
          },
          document: textRef.current,
        },
      });
      dirtyRef.current = false;
      setSaveStatus("saved");
      await refreshDesk();
    } catch (e: any) {
      // A failed save leaves the draft open and marked unsaved — never lost.
      setSaveStatus("dirty");
      setNote(e.message ?? String(e));
    }
  }, [refreshDesk]);

  const flushRef = useRef(flush);
  flushRef.current = flush;

  /** Mark dirty and schedule the idle autosave. Call from user edits only. */
  const touch = useCallback(() => {
    if (!docRef.current) return;
    dirtyRef.current = true;
    setSaveStatus("dirty");
    if (autosaveTimer.current) window.clearTimeout(autosaveTimer.current);
    autosaveTimer.current = window.setTimeout(() => void flushRef.current(), 2000);
  }, []);

  // Ctrl/Cmd+S forces the flush; mounted once, reads the live callback by ref.
  useEffect(() => {
    const h = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "s") {
        e.preventDefault();
        if (autosaveTimer.current) window.clearTimeout(autosaveTimer.current);
        void flushRef.current();
      }
    };
    window.addEventListener("keydown", h);
    return () => window.removeEventListener("keydown", h);
  }, []);

  // Media import failures surface here too (from Writer paste/drop).
  useEffect(() => {
    const h = (e: Event) => setNote((e as CustomEvent<string>).detail);
    window.addEventListener("tezuri:media-error", h);
    return () => window.removeEventListener("tezuri:media-error", h);
  }, []);

  // ---- session ----------------------------------------------------------
  // Launch opens the last publication automatically. Adding publications
  // happens through the native folder picker.
  const [pubList, setPubList] = useState<{name:string; persona:string; root:string}[]>([]);

  const openByPath = useCallback(async (path: string) => {
    try {
      const info = await tauri().invoke("open_publication", { path });
      setPubInfo(`${info.path} — ${info.articles} articles · ${info.words} words`);
      setOpened(true);
      await refreshDesk();
      setAssistantList(await tauri().invoke("list_assistants"));
      return true;
    } catch (e: any) {
      setPubError(e.message ?? String(e));
      return false;
    }
  }, [refreshDesk]);

  useEffect(() => {
    (async () => {
      try {
        const reg = await tauri().invoke("registry_load");
        setPubList(reg.publications.map((p2: any) => ({ name: p2.name, persona: p2.persona, root: p2.root })));
        if (reg.publications.length === 0) { setShowFirstRun(true); return; }
        const last = await tauri().invoke("get_last_opened");
        const target = reg.publications.find((p2: any) => p2.root === last) ?? reg.publications[0];
        await openByPath(target.root);
      } catch (e: any) {
        setShowFirstRun(true);
        setPubError(e.message ?? String(e));
      }
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function addPublication() {
    try {
      const path = await tauri().invoke("pick_folder");
      if (!path) return;
      const name = prompt("Publication name:", path.split(/[\\/]/).pop() || "publication");
      if (!name) return;
      const persona = prompt("Persona / author name:", "") || "";
      const reg = await tauri().invoke("registry_add", { pubData: { name, persona, path } });
      setPubList(reg.publications.map((p2: any) => ({ name: p2.name, persona: p2.persona, root: p2.root })));
      await openByPath(path);
      await tauri().invoke("set_last_opened", { path });
    } catch (e: any) {
      setPubError(e.message ?? String(e));
      setShowFirstRun(true);
    }
  }

  async function switchTo(root: string) {
    setOpened(false);
    setDoc(null);
    if (await openByPath(root)) {
      await tauri().invoke("set_last_opened", { path: root });
    }
  }

  async function removePublication(root: string) {
    if (!confirm(`Remove "${root}" from Tezuri? Files on disk are untouched.`)) return;
    try {
      const reg = await tauri().invoke("registry_remove", { path: root });
      setPubList(reg.publications.map((p2: any) => ({ name: p2.name, persona: p2.persona, root: p2.root })));
      if (doc && pubInfo.startsWith(root)) { setDoc(null); setOpened(false); }
    } catch (e: any) {
      setNote(e.message ?? String(e));
    }
  }

  // ---- articles -------------------------------------------------------------
  async function loadArticle(slug: string) {
    try {
      const a = await tauri().invoke("read_article", { slug });
      setDoc({
        slug: a.article.meta.slug,
        title: titleOfDocument(a.raw, slug),
        state: String(a.article.meta.state).toLowerCase(),
        standfirst: null,
        cover: a.article.meta.cover ?? null,
        date: a.article.meta.date ?? null,
        tags: a.article.meta.tags ?? [],
      });
      setText(a.raw);
      setSaveStatus("saved");
      dirtyRef.current = false;
    } catch (e: any) {
      setNote(e.message ?? String(e));
    }
  }

  // Title is the first H1 of the document (the dialect's rule).
  function titleOfDocument(docText: string, slug: string): string {
    const m = docText.match(/^# (.+)$/m);
    return m ? m[1] : slug.replace(/-/g, " ");
  }

  async function newDoc() {
    const slug = prompt("Slug: lowercase-kebab, like \"on-rust\"");
    if (!slug) return;
    const title = prompt("Title:") || slug;
    try {
      await tauri().invoke("create_article", { slug, title });
      await refreshDesk();
      await loadArticle(slug.replace(/[^a-z0-9-]/g, ""));
    } catch (e: any) {
      setNote(e.message ?? String(e));
    }
  }

  useEffect(() => { if (assistOpen) { refreshAssistants(); reviewChanges(); }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [assistOpen]);

  async function refreshAssistants() {
    try { setAssistantList(await tauri().invoke("list_assistants")); } catch {}
  }

  // ---- consult / ship --------------------------------------------------------
  async function runRecipe() {
    if (!doc) return;
    const recipe = (document.getElementById("recipe") as HTMLSelectElement).value;
    const assistant = (document.getElementById("assistant") as HTMLSelectElement).value || null;
    setConsultOut("thinking…");
    try {
      const r = await tauri().invoke("consult_recipe", { recipe, slug: doc.slug, assistant });
      setConsultOut(`[${r.recipe} via ${r.assistant}]\n\n${r.output}`);
    } catch (e: any) { setConsultOut(e.message ?? String(e)); }
  }

  async function doProve() {
    try { setProof(await tauri().invoke("prove")); setNote(""); }
    catch (e: any) { setProof({ verdict: "failed", evidence: e.message ?? String(e) }); }
  }

  async function reviewChanges() {
    try { setChanges(await tauri().invoke("review_changes")); setSelPaths(new Set()); }
    catch (e: any) { setNote(e.message ?? String(e)); }
  }

  async function commitSel() {
    const msgEl = document.getElementById("msg") as HTMLInputElement;
    const msg = msgEl.value.trim();
    if ([...selPaths].length === 0) { setNote("select the changed paths you want in this commit"); return; }
    if (!msg) { setNote("a commit needs your message"); return; }
    try {
      const r = await tauri().invoke("commit_selected", { paths: [...selPaths], message: msg });
      setNote(`committed ${r.hash} — ${[...selPaths].length} path(s)`);
      msgEl.value = "";
      await reviewChanges();
    } catch (e: any) {
      setNote(e.message ?? String(e));
    }
  }

  async function doPush() {
    try {
      const expected = await tauri().invoke("remote_head");
      await tauri().invoke("push_published", { expected });
      setNote("pushed — the remote now holds your reviewed state.");
    } catch (e: any) {
      setNote(e.message ?? String(e));
    }
  }

  // ---- render ------------------------------------------------------------------
  if (!opened) {
    return (
      <div id="firstrun">
        <h1>Tezuri</h1>
        <p>Point Tezuri at a publication folder — a folder of Markdown articles.</p>
        <button className="primary" onClick={addPublication}>Choose a folder…</button>
        {pubError && <p style={{ color: "#f87171" }}>{pubError}</p>}
      </div>
    );
  }


  return (
    <>
      <header className="app-band">
        <h1>Tezuri</h1>
        <select
          className="pub-switch"
          value={pubList.find(p2 => pubInfo.startsWith(p2.root))?.root ?? ""}
          onChange={(e2) => switchTo(e2.target.value)}
          aria-label="Publication"
        >
          {pubList.map((p2) => (
            <option key={p2.root} value={p2.root}>{p2.name}</option>
          ))}
        </select>
        <span className="path">{pubInfo.split(" — ").slice(1).join(" — ")}</span>
        <button onClick={() => {
          const root = pubList.find(p2 => pubInfo.startsWith(p2.root))?.root;
          if (root) removePublication(root);
        }}>Remove</button>
        <button onClick={addPublication}>+ Add</button>
        <span style={{ flex: 1 }} />
        <button onClick={() => setAssistOpen(!assistOpen)}>Consult / Ship</button>
      </header>
      <main>
        <section id="desk">
          <h2>Desk</h2>
          {entries.map((e) => (
            <div key={e.slug} className={`entry ${doc?.slug === e.slug ? "active" : ""}`} onClick={() => loadArticle(e.slug)}>
              <div className="t">{e.title}</div>
              <div className="m"><span className={`state-${e.state}`}>{e.state}</span> · {e.words}w
                {e.dangling_links.length > 0 && <> · ⚠ {e.dangling_links.join(", ")}</>}</div>
            </div>
          ))}
          <button onClick={newDoc}>+ New article</button>
        </section>

        <section id="editor">
          {doc && (
            <>
              <div className="pinbar">
                <button title="Back to desk" className="tool-btn"
                        onClick={() => { if (saveStatus !== "saving") setDoc(null); }}>
                  ←
                </button>
                <select
                  value={doc.state}
                  onChange={(e2) => { setDoc({ ...doc, state: e2.target.value }); touch(); }}
                  aria-label="Publication state"
                  className="state-select"
                >
                  <option value="draft">draft</option>
                  <option value="review">review</option>
                  <option value="published">published</option>
                </select>
                <SaveDot status={saveStatus} />
                <span style={{ flex: 1 }} />
                <button onClick={() => setSourceMode(!sourceMode)}>
                  {sourceMode ? "Write" : "Source"}
                </button>
                <button
                  className={saveStatus === "dirty" ? "primary" : ""}
                  onClick={() => { if (autosaveTimer.current) window.clearTimeout(autosaveTimer.current); void flush(); }}
                  disabled={saveStatus === "saving"}
                >{saveStatus === "saving" ? "Saving…" : "Save"}</button>
              </div>
              {settingsOpen && doc && (
                <div className="settings-pop">
                  <h3>Post settings</h3>
                  <label>Cover image URL or media/ path
                    <input value={doc.cover ?? ""} size={44}
                      onChange={(e2) => { setDoc({ ...doc, cover: e2.target.value || null }); touch(); }} />
                  </label>
                  <label>Date
                    <input type="date" value={doc.date ?? ""}
                      onChange={(e2) => { setDoc({ ...doc, date: e2.target.value || null }); touch(); }} />
                  </label>
                  <label>Tags (comma-separated)
                    <input value={(doc.tags ?? []).join(", ")} size={44}
                      onChange={(e2) => { setDoc({ ...doc, tags: e2.target.value.split(",").map(x => x.trim()).filter(Boolean) }); touch(); }} />
                  </label>
                </div>
              )}
              {sourceMode ? (
                <div className="cm-host">
                  <CodeMirror
                    value={text}
                    height="100%"
                    extensions={[markdown({ base: markdownLanguage, codeLanguages: languages })]}
                    onChange={(v) => { setText(v); touch(); }}
                    theme="dark"
                    basicSetup={{ foldGutter: false }}
                  />
                </div>
              ) : (
                <Writer
                  key={doc.slug}
                  initialMarkdown={text}
                  slug={doc.slug}
                  onChange={(md) => { setText(md); touch(); }}
                  words={text.split(/\s+/).filter(Boolean).length}
                />
              )}
            </>
          )}
        </section>

        <aside id="assist" className={assistOpen ? "on" : ""}>
          <h2>Consult</h2>
          <div className="row">
            <select id="recipe">
              <option>polish</option><option>align-to-voice</option><option>fact-check</option>
              <option>suggest-tags</option><option>summarize-scratch</option>
            </select>
            <select id="assistant">{assistantList.map(a => <option key={a}>{a}</option>)}</select>
            <button onClick={runRecipe}>Ask</button>
          </div>
          <pre className="out">{consultOut}</pre>
          <h2>Ship</h2>
          <div className="row"><button onClick={doProve}>Prove build</button>
            {proof && <span className={`verdict-${proof.verdict}`}>{proof.verdict}</span>}</div>
          <pre className="out" style={{ maxHeight: "20vh" }}>{proof?.evidence ?? ""}</pre>
          <h2>Changes</h2>
          {changes.length === 0 ? <p style={{ color: "var(--dim)" }}>working tree clean</p> :
            changes.map((c) => (
              <label key={c.path} className="entry">
                <input type="checkbox" checked={selPaths.has(c.path)}
                       onChange={(e2) => {
                         const s = new Set(selPaths);
                         e2.target.checked ? s.add(c.path) : s.delete(c.path);
                         setSelPaths(s);
                       }} />
                {c.status} {c.path}
              </label>
            ))}
          <div className="row"><input id="msg" placeholder="commit message" size={22} />
            <button onClick={commitSel}>Commit</button><button onClick={doPush}>Push</button></div>
          {note && <p className="receipt">{note}</p>}
        </aside>
      </main>
    </>
  );
}

function SaveDot({ status }: { status: "saved" | "saving" | "dirty" }) {
  const label = status === "saved" ? "Saved" : status === "saving" ? "Saving…" : "Unsaved";
  return (
    <span className="save-dot-wrap" title={`Autosaves 2s after you stop typing — ${label}`}>
      <span className={`saved-dot ${status}`} />
      <span className="save-label">{label}</span>
    </span>
  );
}
