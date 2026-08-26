import React, { useCallback, useEffect, useRef, useState } from "react";
import CodeMirror from "@uiw/react-codemirror";
import { markdown, markdownLanguage } from "@codemirror/lang-markdown";
import { languages } from "@codemirror/language-data";
import { Writer } from "./Writer";
import { Landing } from "./Landing";
import { SpaceRail, type Workspace } from "./Space";
import { About } from "./About";
import { Config, THEME_EVENT } from "./Config";
import { ModalHost, askForm, askConfirm } from "./prompts";
import { invoke } from "./bridge";
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
  const [sourceMode, setSourceMode] = useState(false);
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

  async function addPublication() {
    try {
      const path = await invoke<string | null>("pick_folder");
      if (!path) return;
      const folderName = path.split(/[\\/]/).pop() || "publication";
      const vals = await askForm({
        title: "New space",
        hint: "These characteristics live in publication.yaml inside the folder — plain files, yours.",
        confirmLabel: "Add space",
        fields: [
          { key: "name", label: "Name", initial: folderName },
          { key: "persona", label: "Persona", placeholder: "who writes here" },
          { key: "byline", label: "Byline", placeholder: "e.g. words and photographs by…" },
        ],
      });
      if (!vals) return;
      const reg = await invoke<{ publications: any[] }>("registry_add", {
        pubData: { name: vals.name.trim() || folderName, persona: vals.persona.trim(), path },
      });
      setPubList(reg.publications);
      await openSpace(path);
      // Persist what the author just typed; the file carries the truth.
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
    }
  }

  async function removePublication(root: string) {
    const pub = pubList.find((p2) => p2.root === root);
    const ok = await askConfirm({
      title: "Remove space",
      body: `Remove “${pub?.name ?? root}” from Tezuri? Files on disk are untouched.`,
      confirmLabel: "Remove",
      danger: true,
    });
    if (!ok) return;
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
      setSaveStatus("saved");
      dirtyRef.current = false;
      setActiveSlug(slug);
      setWorkspace({ kind: "article", slug });
    } catch (e: any) {
      setNote(e.message ?? String(e));
    }
  }

  async function newDoc() {
    const vals = await askForm({
      title: "New article",
      confirmLabel: "Create",
      fields: [
        { key: "slug", label: "Slug", placeholder: "lowercase-kebab, like on-rust" },
        { key: "title", label: "Title", placeholder: "Working title" },
      ],
    });
    if (!vals) return;
    const slug = vals.slug.trim();
    const title = vals.title.trim() || slug;
    if (!slug) { setNote("an article needs a slug"); return; }
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

  // The space's own theme.css styles the editor plane — a derived view, so a
  // publication file may dress it. Injected under the fixed id; a change
  // event from Configuration replaces it.
  const [themeCss, setThemeCss] = useState("");
  useEffect(() => {
    if (!open) { setThemeCss(""); return; }
    invoke<string>("read_theme").then(setThemeCss).catch(() => setThemeCss(""));
  }, [open]);

  useEffect(() => {
    const h = (e: Event) => setThemeCss((e as CustomEvent<string>).detail);
    window.addEventListener(THEME_EVENT, h);
    return () => window.removeEventListener(THEME_EVENT, h);
  }, []);

  const themeStyle = themeCss
    ? <style>{themeCss}</style>
    : null;

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
      <ModalHost />
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
          </span>
        </span>
        <span style={{ flex: 1 }} />
        {bandTabs}
      </header>

      {surface === "landing" && (
        <Landing
          pubs={pubList} lastOpened={lastOpened} error={landingError}
          onOpen={openSpace} onAdd={addPublication}
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
        <main>
          <SpaceRail
            identity={identity} desk={{ entries }}
            activeSlug={activeSlug}
            onOpenArticle={loadArticle} onNewArticle={newDoc}
          />

          <section id="editor" className={themeCss.trim() ? "theme-scope" : undefined}>
            {themeStyle}
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
                  <button onClick={() => setAssistOpen(!assistOpen)}
                          title="Advisory help: polish, voice, facts">Assistant</button>
                  <button onClick={() => setSourceMode(!sourceMode)}>
                    {sourceMode ? "Write" : "Source"}
                  </button>
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
                {sourceMode ? (
                  <div className="cm-host">
                    <CodeMirror
                      value={text} height="100%"
                      extensions={[markdown({ base: markdownLanguage, codeLanguages: languages })]}
                      onChange={(v) => { setText(v); touch(); }}
                      theme="dark" basicSetup={{ foldGutter: false }}
                    />
                  </div>
                ) : (
                  <Writer
                    key={doc.slug} initialMarkdown={text} slug={doc.slug}
                    onChange={(md) => { setText(md); touch(); }}
                    words={text.split(/\s+/).filter(Boolean).length}
                  />
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
