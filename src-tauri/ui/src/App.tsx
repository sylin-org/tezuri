import React, { useCallback, useEffect, useRef, useState } from "react";
import CodeMirror from "@uiw/react-codemirror";
import { markdown, markdownLanguage } from "@codemirror/lang-markdown";
import { languages } from "@codemirror/language-data";
import { Writer } from "./Writer";
import { WriteComposePlane } from "./Compose";
import { Landing } from "./Landing";
import { SpaceRail, type Workspace } from "./Space";
import { About } from "./About";
import { Config } from "./Config";
import { invoke, onSettle } from "./bridge";
import type { Identity, PublicationInfo } from "./bridge";

type Surface = "landing" | "space" | "config" | "about";

const EMPTY_ID: Identity = { name: "", byline: "", persona: "" };

export default function App() {
  // ---- top-level surfaces ---------------------------------------------------
  const [surface, setSurface] = useState<Surface>("landing");
  const [pubList, setPubList] = useState<{ name: string; persona: string; root: string }[]>([]);
  const [lastOpened, setLastOpened] = useState<string | null>(null);
  const [open, setOpen] = useState<PublicationInfo | null>(null);
  const [landingError, setLandingError] = useState("");
  const [note, setNote] = useState("");
  // The settler's quiet progress: "healing this space" counts in the band.
  const [settle, setSettle] = useState<{ done: number; total: number } | null>(null);

  useEffect(() => {
    let unlisten: (() => void) | undefined;
    onSettle((p) => {
      setSettle(p.done < p.total ? { done: p.done, total: p.total } : null);
    })
      .then((u) => { unlisten = u; })
      .catch(() => {});
    return () => { unlisten?.(); };
  }, []);
  // ---- space state ----------------------------------------------------------
  const [identity, setIdentity] = useState<Identity | null>(null);
  const [entries, setEntries] = useState<any[]>([]);
  const [workspace, setWorkspace] = useState<Workspace>(null);
  const [detailBusy, setDetailBusy] = useState(false);
  const [detailError, setDetailError] = useState("");
  const [activeSlug, setActiveSlug] = useState<string | null>(null);

  // ---- article state --------------------------------------------------------
  const [doc, setDoc] = useState<{
    slug: string; title: string; state: string;
    cover: string | null; date: string | null; tags: string[] | null;
  } | null>(null);
  const [text, setText] = useState("");
  // The space's template, projected live for Write mode.
  const [compose, setCompose] = useState<any | null>(null);
  const [saveStatus, setSaveStatus] = useState<"saved" | "saving" | "dirty">("saved");

  // ---- shared chrome state ----------------------------------------------------
  const [assistOpen, setAssistOpen] = useState(false);
  const [consultOut, setConsultOut] = useState("advisory only — nothing enters the document until you accept it");
  const [proof, setProof] = useState<{ verdict: string; evidence: string } | null>(null);
  const [changes, setChanges] = useState<any[]>([]);
  const [selPaths, setSelPaths] = useState<Set<string>>(new Set());
  const [settingsOpen, setSettingsOpen] = useState(false);

  const autosaveTimer = useRef<number | null>(null);
  const dirtyRef = useRef(false);
  const docRef = useRef(doc);
  docRef.current = doc;
  const textRef = useRef(text);
  textRef.current = text;

  const refreshDesk = useCallback(async () => {
    const d = await invoke<{ entries: any[] }>("desk");
    setEntries(d.entries);
  }, []);

  const flush = useCallback(async () => {
    const d = docRef.current;
    if (!d) return;
    setSaveStatus("saving");
    try {
      await invoke("save_article", {
        article: {
          meta: {
            slug: d.slug, state: d.state, date: d.date ?? null,
            tags: d.tags ?? [], cover: d.cover ?? null, standfirst: null,
          },
          document: textRef.current,
        },
      });
      dirtyRef.current = false;
      setSaveStatus("saved");
      await refreshDesk();
      // Projections follow the file: the composer re-reads what just saved.
      invoke("write_compose", { slug: d.slug })
        .then(setCompose)
        .catch(() => {});
    } catch (e: any) {
      setSaveStatus("dirty");
      setNote(e.message ?? String(e));
    }
  }, [refreshDesk]);

  const flushRef = useRef(flush);
  flushRef.current = flush;

  const touch = useCallback(() => {
    if (!docRef.current) return;
    dirtyRef.current = true;
    setSaveStatus("dirty");
    if (autosaveTimer.current) window.clearTimeout(autosaveTimer.current);
    autosaveTimer.current = window.setTimeout(() => void flushRef.current(), 2000);
  }, []);

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

  useEffect(() => {
    const h = (e: Event) => setNote((e as CustomEvent<string>).detail);
    window.addEventListener("tezuri:media-error", h);
    return () => window.removeEventListener("tezuri:media-error", h);
  }, []);

  useEffect(() => {
    const h = () => setSettingsOpen((v) => !v);
    window.addEventListener("tezuri:settings", h);
    return () => window.removeEventListener("tezuri:settings", h);
  }, []);

  // The lamp speaks only real state: saving breathes, an unsaved draft holds.
  useEffect(() => {
    document.body.classList.toggle("tezuri-working", saveStatus === "saving");
    document.body.classList.toggle("tezuri-dirty", saveStatus === "dirty");
  }, [saveStatus]);

  // ---- session ------------------------------------------------------------
  useEffect(() => {
    (async () => {
      try {
        const reg = await invoke<{ publications: any[] }>("registry_load");
        setPubList(reg.publications);
        const last = await invoke<string | null>("get_last_opened");
        setLastOpened(last);
      } catch (e: any) {
        setLandingError(e.message ?? String(e));
      }
    })();
  }, []);

  const openSpace = useCallback(async (root: string) => {
    try {
      const info = await invoke<PublicationInfo>("open_publication", { path: root });
      setOpen(info);
      setIdentity(await invoke<Identity>("read_identity", { path: root }).catch(() => EMPTY_ID));
      await refreshDesk();
      setAssistantList(await invoke<string[]>("list_assistants").catch(() => []));
      await invoke("set_last_opened", { path: root }).catch(() => {});
      setLastOpened(root);
      setSurface("space");
      setWorkspace(null);
    } catch (e: any) {
      setLandingError(e.message ?? String(e));
    }
  }, [refreshDesk]);

  const [assistantList, setAssistantList] = useState<string[]>([]);

  // A picked folder awaiting its inline naming card on the landing grid.
  const [newSpacePath, setNewSpacePath] = useState<string | null>(null);

  async function addPublication() {
    try {
      const path = await invoke<string | null>("pick_folder");
      if (!path) return;
      // No dialog: the landing names the space in place.
      setNewSpacePath(path);
    } catch (e: any) {
      setLandingError(e.message ?? String(e));
    }
  }

  async function createSpace(vals: { name: string; persona: string; byline: string }) {
    const path = newSpacePath;
    if (!path) return;
    try {
      const folderName = path.split(/[\\/]/).pop() || "publication";
      const reg = await invoke<{ publications: any[] }>("registry_add", {
        pubData: { name: vals.name.trim() || folderName, persona: vals.persona.trim(), path },
      });
      setPubList(reg.publications);
      setNewSpacePath(null);
      await openSpace(path);
      // Persist what the author typed; the file carries the truth.
      const id = await invoke<Identity>("read_identity", { path }).catch(() => EMPTY_ID);
      await invoke("save_identity", {
        path,
        identity: {
          ...id,
          name: vals.name.trim() || id.name,
          persona: vals.persona.trim(),
          byline: vals.byline.trim(),
        },
      }).catch(() => {});
    } catch (e: any) {
      setLandingError(e.message ?? String(e));
      setNewSpacePath(null);
    }
  }

  async function removePublication(root: string) {
    try {
      const reg = await invoke<{ publications: any[] }>("registry_remove", { path: root });
      setPubList(reg.publications);
      if (open?.path.startsWith(root)) { setOpen(null); setSurface("landing"); }
    } catch (e: any) {
      setNote(e.message ?? String(e));
    }
  }

  // ---- articles -------------------------------------------------------------
  async function loadArticle(slug: string) {
    try {
      const a = await invoke<any>("read_article", { slug });
      setDoc({
        slug: a.article.meta.slug,
        title: (a.raw.match(/^# (.+)$/m) ?? [])[1] ?? slug.replace(/-/g, " "),
        state: String(a.article.meta.state).toLowerCase(),
        cover: a.article.meta.cover ?? null,
        date: a.article.meta.date ?? null,
        tags: a.article.meta.tags ?? [],
      });
      setText(a.raw);
      invoke<any>("write_compose", { slug }).then(setCompose).catch(() => setCompose(null));
      setSaveStatus("saved");
      dirtyRef.current = false;
      setActiveSlug(slug);
      setWorkspace({ kind: "article", slug });
      setMode("write");
    } catch (e: any) {
      setNote(e.message ?? String(e));
    }
  }

  async function newDoc(vals: { slug: string; title: string }) {
    const slug = vals.slug;
    const title = vals.title || slug;
    try {
      await invoke("create_article", { slug, title });
      await refreshDesk();
      await loadArticle(slug);
    } catch (e: any) {
      setNote(e.message ?? String(e));
    }
  }

  async function saveIdentity(next: Identity) {
    if (!open) return;
    setDetailBusy(true);
    setDetailError("");
    try {
      await invoke("save_identity", { path: open.path, identity: next });
      setIdentity(next);
      const reg = await invoke<{ publications: any[] }>("registry_load");
      setPubList(reg.publications);
    } catch (e: any) {
      setDetailError(e.message ?? String(e));
    } finally {
      setDetailBusy(false);
    }
  }

  // ---- consult / ship ---------------------------------------------------------
  useEffect(() => {
    if (assistOpen) {
      invoke<string[]>("list_assistants").then(setAssistantList).catch(() => {});
      reviewChanges();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [assistOpen]);

  async function runRecipe() {
    if (!doc) return;
    const recipe = (document.getElementById("recipe") as HTMLSelectElement).value;
    const assistant = (document.getElementById("assistant") as HTMLSelectElement).value || null;
    setConsultOut("thinking…");
    try {
      const r = await invoke<any>("consult_recipe", { recipe, slug: doc.slug, assistant });
      setConsultOut(`[${r.recipe} via ${r.assistant}]\n\n${r.output}`);
    } catch (e: any) { setConsultOut(e.message ?? String(e)); }
  }

  async function doProve() {
    try { setProof(await invoke("prove")); setNote(""); }
    catch (e: any) { setProof({ verdict: "failed", evidence: e.message ?? String(e) }); }
  }

  async function reviewChanges() {
    try { setChanges(await invoke("review_changes")); setSelPaths(new Set()); }
    catch (e: any) { setNote(e.message ?? String(e)); }
  }

  async function commitSel() {
    const msgEl = document.getElementById("msg") as HTMLInputElement;
    const msg = msgEl.value.trim();
    if ([...selPaths].length === 0) { setNote("select the changed paths you want in this commit"); return; }
    if (!msg) { setNote("a commit needs your message"); return; }
    try {
      const r = await invoke<any>("commit_selected", { paths: [...selPaths], message: msg });
      setNote(`committed ${r.hash} — ${[...selPaths].length} path(s)`);
      msgEl.value = "";
      await reviewChanges();
    } catch (e: any) { setNote(e.message ?? String(e)); }
  }

  async function doPush() {
    try {
      const expected = await invoke<string | null>("remote_head");
      await invoke("push_published", { expected });
      setNote("pushed — the remote now holds your reviewed state.");
    } catch (e: any) { setNote(e.message ?? String(e)); }
  }

  // ---- render mode: Write (WYSIWYG), Source (raw md), Preview (the artifact)
  // The preview is the compiled page itself — same bytes emit_render writes.
  const [mode, setMode] = useState<"write" | "source" | "preview">("write");
  const [previewHtml, setPreviewHtml] = useState("");
  const [emitted, setEmitted] = useState<string[] | null>(null);
  // The app-side origin of the open session's media (custom protocol), so
  // article images resolve inside the webview. Disk artifacts stay relative.
  const [mediaBase, setMediaBase] = useState("");
  useEffect(() => {
    invoke<string>("media_base").then(setMediaBase).catch(() => {});
  }, []);

  // The space's theme.css dresses the editor's prose surface too — the same
  // rules the artifact gets. Tezuri's chrome (band, rail) stays neutral:
  // selectors are namespaced under .article-prose, which only the writing
  // surface carries.
  const [themeCss, setThemeCss] = useState("");
  useEffect(() => {
    if (!open) { setThemeCss(""); return; }
    invoke<string>("read_theme").then(setThemeCss).catch(() => setThemeCss(""));
  }, [open]);

  const showPreview = useCallback(async () => {
    const d = docRef.current;
    if (!d) return;
    // Preview always shows the saved state; flush a pending autosave first.
    if (autosaveTimer.current) window.clearTimeout(autosaveTimer.current);
    await flushRef.current();
    try {
      const html = await invoke<string>("render_article", { slug: d.slug });
      // View-side seam: point ../media/ at the session's media origin.
      setPreviewHtml(mediaBase ? html.replaceAll('../media/', mediaBase) : html);
      setMode("preview");
    } catch (e: any) {
      setNote(e.message ?? String(e));
    }
  }, [mediaBase]);

  async function doEmit() {
    try {
      const files = await invoke<string[]>("emit_render");
      setEmitted(files);
      setNote(`rendered ${files.length} page(s) into render/ — ready for review`);
    } catch (e: any) {
      setNote(e.message ?? String(e));
    }
  }

  // ---- render -----------------------------------------------------------------
  const bandTabs = (
    <nav className="band-tabs" aria-label="Tezuri">
      <button className={`tab${surface === "landing" ? " active" : ""}`}
              onClick={() => { setSurface("landing"); setWorkspace(null); setActiveSlug(null); }}>Spaces</button>
      <button className={`tab${surface === "config" ? " active" : ""}`}
              onClick={() => setSurface("config")}>Configuration</button>
      <button className={`tab${surface === "about" ? " active" : ""}`}
              onClick={() => setSurface("about")}>About</button>
    </nav>
  );

  return (
    <>
      {themeCss && <style>{themeCss}</style>}
      <header className="app-band">
        <span className="lamp" aria-hidden="true">
          <span className="lamp-halo" />
          <span className="lamp-core" />
        </span>
        <span className="state">
          <span className="state-word">
            {saveStatus === "saving" ? "Saving" : saveStatus === "dirty" ? "Drafting" : "Ready"}
          </span>
          <span className="state-facts" title={open?.path}>
            {open
              ? <><b>{identity?.name || pubList.find(p2 => open.path.startsWith(p2.root))?.name || open.path}</b>
                 &nbsp;· {open.articles} articles · {open.words.toLocaleString()} words</>
              : <>{pubList.length} {pubList.length === 1 ? "space" : "spaces"} registered</>}
            {settle && <>&nbsp;· settling {settle.done}/{settle.total}</>}
          </span>
        </span>
        <span style={{ flex: 1 }} />
        {bandTabs}
      </header>

      {surface === "landing" && (
        <Landing
          pubs={pubList} lastOpened={lastOpened} error={landingError}
          onOpen={openSpace} onAdd={addPublication}
          newSpacePath={newSpacePath} onCreateSpace={createSpace}
          onCancelNewSpace={() => setNewSpacePath(null)}
        />
      )}

      {surface === "about" && <About />}

      {surface === "config" && (
        <Config
          spaceOpen={!!open}
          spacePath={open?.path ?? null}
          identity={identity}
          spaces={pubList}
          onSaveIdentity={saveIdentity}
          saveBusy={detailBusy}
          saveError={detailError}
          onRemoveSpace={removePublication}
        />
      )}

      {surface === "space" && open && (
        <main>          <SpaceRail
            identity={identity} desk={{ entries }}
            activeSlug={activeSlug}
            onOpenArticle={loadArticle} onNewArticle={newDoc}
          />

          <section id="editor">
            {workspace?.kind === "article" && doc && (
              <>
                <div className="pinbar">
                  <button title="Back to the rail" className="tool-btn"
                          onClick={() => { if (saveStatus !== "saving") { setWorkspace(null); setDoc(null); setActiveSlug(null); } }}>
                    ←
                  </button>
                  <select
                    value={doc.state}
                    onChange={(e2) => { setDoc({ ...doc, state: e2.target.value }); touch(); }}
                    aria-label="Publication state" className="state-select"
                  >
                    <option value="draft">draft</option>
                    <option value="review">review</option>
                    <option value="published">published</option>
                  </select>
                  <SaveDot status={saveStatus} />
                  <span style={{ flex: 1 }} />
                  <div className="mode-switch" role="tablist" aria-label="View">
                    <button role="tab" aria-selected={mode === "write"}
                            className={mode === "write" ? "active" : ""}
                            onClick={() => setMode("write")}>Write</button>
                    <button role="tab" aria-selected={mode === "source"}
                            className={mode === "source" ? "active" : ""}
                            onClick={() => setMode("source")}>Source</button>
                    <button role="tab" aria-selected={mode === "preview"}
                            className={mode === "preview" ? "active" : ""}
                            onClick={() => void showPreview()}>Preview</button>
                  </div>
                  <button onClick={() => setAssistOpen(!assistOpen)}
                          title="Advisory help: polish, voice, facts">Assistant</button>
                  <button
                    className={saveStatus === "dirty" ? "primary" : ""}
                    onClick={() => { if (autosaveTimer.current) window.clearTimeout(autosaveTimer.current); void flush(); }}
                    disabled={saveStatus === "saving"}
                  >{saveStatus === "saving" ? "Saving…" : "Save"}</button>
                </div>
                {settingsOpen && (
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
                {mode === "preview" ? (
                  <iframe
                    className="preview-frame"
                    title="Rendered article — exactly what emits"
                    srcDoc={previewHtml}
                    sandbox="allow-scripts"
                  />
                ) : mode === "source" ? (
                  <div className="cm-host">
                    <CodeMirror
                      value={text} height="100%"
                      extensions={[markdown({ base: markdownLanguage, codeLanguages: languages })]}
                      onChange={(v) => { setText(v); touch(); }}
                      theme="dark" basicSetup={{ foldGutter: false }}
                    />
                  </div>
                ) : (
                  <div className="writer-wrap">
                    <WriteComposePlane
                      compose={compose}
                      markdown={text}
                      slug={doc.slug}
                      mediaBase={mediaBase}
                      date={doc.date}
                      tags={doc.tags}
                      cover={doc.cover}
                      tagVocabulary={[
                        ...new Set(
                          entries.flatMap((e) => e.tags ?? [])
                        ),
                      ]}
                      onMarkdown={(md) => { setText(md); touch(); }}
                      words={text.split(/\s+/).filter(Boolean).length}
                      onMetaChange={(patch) => {
                        setDoc((d0) => d0 ? { ...d0, ...patch } : d0);
                        touch();
                      }}
                    />
                  </div>
                )}
              </>
            )}
            {!workspace && (
              <div className="detail-plane">
                <p className="crumb">WORKSPACE</p>
                <h1 className="detail-title">{identity?.name || "This space"}</h1>
                <p className="detail-sub">Open an article from the rail, or start a new one.</p>
                <div className="row" style={{ marginTop: 18 }}>
                  <button onClick={() => setAssistOpen(!assistOpen)}>
                    {assistOpen ? "Hide assistant" : "Assistant"}
                  </button>
                  <span className="mono-fact">advisory help — polish, voice, facts; nothing enters the document unaccepted</span>
                </div>
              </div>
            )}
          </section>

          <aside id="assist" className={assistOpen ? "on" : ""}>
            <h2>Assistant</h2>
            <div className="row">
              <select id="recipe" aria-label="Recipe">
                <option>polish</option><option>align-to-voice</option><option>fact-check</option>
                <option>suggest-tags</option><option>summarize-scratch</option>
              </select>
              <select id="assistant" aria-label="Assistant">
                {assistantList.map(a => <option key={a}>{a}</option>)}
              </select>
              <button onClick={runRecipe}>Ask</button>
            </div>
            <pre className="out">{consultOut}</pre>
            <h2>Ship</h2>
            <div className="row"><button onClick={doProve}>Prove build</button>
              {proof && <span className={`verdict-${proof.verdict}`}>{proof.verdict}</span>}</div>
            <pre className="out" style={{ maxHeight: "20vh" }}>{proof?.evidence ?? ""}</pre>
            <div className="row">
              <button onClick={doEmit} title="Compile every article into render/">Render pages</button>
              {emitted && <span className="mono-fact">{emitted.length} pages → render/</span>}
            </div>
            <h2>Changes</h2>
            {changes.length === 0 ? <p style={{ color: "var(--muted)" }}>working tree clean</p> :
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
      )}
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
