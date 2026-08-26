import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import CodeMirror from "@uiw/react-codemirror";
import { markdown, markdownLanguage } from "@codemirror/lang-markdown";
import { languages } from "@codemirror/language-data";
import { Writer } from "./Writer";

function tauri(): any {
  const t = (window as any).__TAURI__;
  if (!t?.core?.invoke) throw new Error("Tauri bridge not ready");
  return t.core;
}

type Doc = { slug: string; title: string; state: string; body: string;
  standfirst?: string | null; cover?: string | null; date?: string | null; tags?: string[] | null };

class BridgeError extends Error {}
export default function App() {
  const [opened, setOpened] = useState(false);
  const [pubInfo, setPubInfo] = useState("");
  const [pubError, setPubError] = useState("");
  const [assistantList, setAssistantList] = useState<string[]>([]);
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
  const autosaveTimer = useRef<number | null>(null);
  const dirtyRef = useRef(false);

  // Mark dirty on any content/meta change; debounce autosave at 2s idle.
  useEffect(() => {
    if (!doc) return;
    dirtyRef.current = true;
    setSaveStatus("dirty");
    if (autosaveTimer.current) window.clearTimeout(autosaveTimer.current);
    autosaveTimer.current = window.setTimeout(async () => {
      setSaveStatus("saving");
      await saveDoc();
      dirtyRef.current = false;
      setSaveStatus("saved");
    }, 2000);
    return () => { if (autosaveTimer.current) window.clearTimeout(autosaveTimer.current); };
  });


  useEffect(() => {
    const h = () => setSettingsOpen((v) => !v);
    window.addEventListener("tezuri:settings", h);
    return () => window.removeEventListener("tezuri:settings", h);
  }, []);

  useEffect(() => {
    const h = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "s") {
        e.preventDefault(); saveDoc();
      }
    };
    window.addEventListener("keydown", h);
    return () => window.removeEventListener("keydown", h);
  });

  // ---- desk ---------------------------------------------------------------
  const refreshDesk = useCallback(async () => {
    const d = await tauri().invoke("desk");
    setEntries(d.entries);
  }, []);

  // ---- session ------------------------------------------------------------
  async function openPub() {
    const path = (document.getElementById("pubPath") as HTMLInputElement).value.trim();
    try {
      const info = await tauri().invoke("open_publication", { path });
      setPubInfo(`${info.path} — ${info.articles} articles · ${info.words} words`);
      // Publication-owned theme: plain CSS file in the repo, loaded as tokens.
      try {
        const css = await tauri().invoke("read_theme", { path });
        let el = document.getElementById("pub-theme") as HTMLStyleElement | null;
        if (!el) {
          el = document.createElement("style");
          el.id = "pub-theme";
          document.head.appendChild(el);
        }
        el.textContent = css;
      } catch { /* no theme.css — defaults apply */ }
      setOpened(true);
      await refreshDesk();
      setAssistantList(await tauri().invoke("list_assistants"));
    } catch (e: any) {
      setPubError(e.message ?? String(e));
    }
  }

  // ---- articles -------------------------------------------------------------
  async function loadArticle(slug: string) {
    const a = await tauri().invoke("read_article", { slug });
    setDoc({
      slug: a.meta.slug,
      title: a.meta.title,
      state: String(a.meta.state).toLowerCase(),
      body: a.body,
      standfirst: a.meta.standfirst ?? null,
      cover: a.meta.cover ?? null,
      date: a.meta.date ?? null,
      tags: a.meta.tags ?? null,
    });
    setText(a.body);
  }

  async function saveDoc() {
    if (!doc) return;
    await tauri().invoke("save_article", {
      article: {
        meta: {
          slug: doc.slug,
          title: doc.title,
          state: doc.state,
          date: doc.date ?? null,
          tags: doc.tags ?? null,
          standfirst: doc.standfirst ?? null,
          cover: doc.cover ?? null,
        },
        body: text,
      },
    });
    await refreshDesk();
  }

  async function newDoc() {
    const slug = prompt("Slug:");
    if (!slug) return;
    const title = prompt("Title:") || slug;
    await tauri().invoke("create_article", { slug, title });
    await refreshDesk();
    await loadArticle(slug);
  }


  useEffect(() => { if (assistOpen) { refreshAssistants(); reviewChanges(); } }, [assistOpen]);

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
    try { setProof(await tauri().invoke("prove")); }
    catch (e: any) { setProof({ verdict: "failed", evidence: e.message ?? String(e) }); }
  }

  async function reviewChanges() {
    try { setChanges(await tauri().invoke("review_changes")); selPaths.clear(); }
    catch {}
  }

  async function commitSel() {
    const msg = (document.getElementById("msg") as HTMLInputElement).value.trim();
    if (!msg) return alert("A commit needs your message.");
    await tauri().invoke("commit_selected", { paths: [...selPaths], message: msg });
    await reviewChanges();
  }

  async function doPush() {
    const expected = await tauri().invoke("remote_head");
    try { await tauri().invoke("push_published", { expected }); alert("pushed."); }
    catch (e: any) { alert(e.message ?? String(e)); }
  }

  // ---- render ------------------------------------------------------------------
  if (!opened) {
    return (
      <div id="opener">
        <h1>Tezuri</h1>
        <p>A desk for your publishing life. Point Tezuri at a publication folder.</p>
        <div className="row">
          <input id="pubPath" size={52} placeholder="F:\path\to\publication" defaultValue="F:\Replica\NAS\Files\repo\github\lbotinelly\kintsugi-architecture" />
          <button className="primary" onClick={openPub}>Open</button>
        </div>
        <p style={{ color: "#e06c75" }}>{pubError}</p>
        <p style={{ color: "#9a8bb0", fontSize: 12 }}>If the Open button does nothing, the Tauri bridge failed to load.</p>
      </div>
    );
  }

  return (
    <>
      <header>
        <h1>Tezuri</h1>
        <span className="path">{pubInfo}</span>
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
                        onClick={() => setDoc(null)}>
                  ←
                </button>
                <select
                  value={doc.state}
                  onChange={(e2) => setDoc({ ...doc, state: e2.target.value })}
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
                <button className="primary" onClick={saveDoc}>Save</button>
              </div>
              {settingsOpen && doc && (
                <div className="settings-pop">
                  <h3>Post settings</h3>
                  <label>Cover image URL or media/ path
                    <input value={doc.cover ?? ""} size={44}
                      onChange={(e2) => setDoc({ ...doc, cover: e2.target.value || null })} />
                  </label>
                  <label>Date
                    <input type="date" value={doc.date ?? ""}
                      onChange={(e2) => setDoc({ ...doc, date: e2.target.value || null })} />
                  </label>
                  <label>Tags (comma-separated)
                    <input value={(doc.tags ?? []).join(", ")} size={44}
                      onChange={(e2) => setDoc({ ...doc, tags: e2.target.value.split(",").map(x => x.trim()).filter(Boolean) })} />
                  </label>
                </div>
              )}
              {sourceMode ? (
                <div className="cm-host">
                  <CodeMirror
                    value={text}
                    height="100%"
                    extensions={[markdown({ base: markdownLanguage, codeLanguages: languages })]}
                    onChange={setText}
                    theme="dark"
                    basicSetup={{ foldGutter: false }}
                  />
                </div>
              ) : (
                <Writer
                  key={doc.slug}
                  initialMarkdown={text}
                  slug={doc.slug}
                  onChange={(md) => setText(md)}
                  meta={{
                    title: doc.title,
                    standfirst: doc.standfirst ?? null,
                    cover: doc.cover ?? null,
                    date: doc.date ?? null,
                    tags: doc.tags ?? null,
                  }}
                  onMetaChange={(patch) => setDoc({ ...doc, ...patch })}
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
        </aside>
      </main>
    </>
  );
}


function CodeMirrorView({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  return (
    <div className="cm-host">
      <CodeMirror
        value={value}
        height="100%"
        extensions={[markdown({ base: markdownLanguage, codeLanguages: languages })]}
        onChange={onChange}
        theme="dark"
        basicSetup={{ foldGutter: false }}
      />
    </div>
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
