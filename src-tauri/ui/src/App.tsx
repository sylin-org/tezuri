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

  // ---- session ----------------------------------------------------------
  // Launch opens the last publication automatically. The typed-path opener
  // is gone; adding publications happens through the native folder picker.
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
    const reg = await tauri().invoke("registry_remove", { path: root });
    setPubList(reg.publications.map((p2: any) => ({ name: p2.name, persona: p2.persona, root: p2.root })));
    if (doc && pubInfo.includes(root)) { setDoc(null); setOpened(false); }
  }

  // ---- articles -------------------------------------------------------------
  async function loadArticle(slug: string) {
    const a = await tauri().invoke("read_article", { slug });
    setDoc({
      slug: a.article.meta.slug,
      title: titleOfDocument(a.raw, slug),
      state: String(a.article.meta.state).toLowerCase(),
      body: a.raw,
      standfirst: null,
      cover: a.article.meta.cover ?? null,
      date: a.article.meta.date ?? null,
      tags: a.article.meta.tags ?? [],
    });
    setText(a.raw);
  }

  // Title is the first H1 of the document (the dialect's rule).
  function titleOfDocument(docText: string, slug: string): string {
    const m = docText.match(/^# (.+)$/m);
    return m ? m[1] : slug.replace(/-/g, " ");
  }

  async function saveDoc() {
    if (!doc) return;
    // Title/standfirst live in the flow (H1 + italic line) — the document is
    // saved verbatim. Meta sidecar keeps state/date/tags/cover.
    const m = text.match(/^# (.+)$/m);
    await tauri().invoke("save_article", {
      article: {
        meta: {
          slug: doc.slug,
          state: doc.state,
          date: doc.date ?? null,
          tags: doc.tags ?? [],
          cover: doc.cover ?? null,
          standfirst: null,
        },
        document: text,
      },
    });
    setDoc({ ...doc, title: m ? m[1] : doc.title });
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
                <button
                  className={saveStatus === "dirty" ? "primary" : ""}
                  onClick={saveDoc}
                  disabled={saveStatus === "saving"}
                >{saveStatus === "saving" ? "Saving…" : "Save"}</button>
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
