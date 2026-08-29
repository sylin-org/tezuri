import React, { useCallback, useEffect, useRef, useState } from "react";
import CodeMirror from "@uiw/react-codemirror";
import { markdown, markdownLanguage } from "@codemirror/lang-markdown";
import { languages } from "@codemirror/language-data";
import { Landing } from "./Landing";
import { SpaceRail, type Workspace } from "./Space";
import { About } from "./About";
import { Config } from "./Config";
import { SpaceDetails } from "./SpaceDetails";
import { WritePane } from "./WritePane";
import { ArticleDetails } from "./ArticleDetails";
import { invoke, onSettle, spliceSlot, insertSlotAt, nextHintsFor } from "./bridge";
import type { Identity, PublicationInfo, CatalogEntry } from "./bridge";

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
    id: string | null; author: string | null;
  } | null>(null);
  const [text, setText] = useState("");
  // The canonical article.md — the diff baseline for the unsaved wash.
  const [canonicalText, setCanonicalText] = useState("");
  // The space's template, projected live for Write mode.
  // Conduct: the working copy of templates/article.html (seeded from the
  // file when one exists, else from the embedded default), the bytes on
  // disk it sprang from, and the catalog menus read from.
  const [templateDraft, setTemplateDraft] = useState<string | null>(null);
  const [templateFile, setTemplateFile] = useState<string | null>(null);
  const [catalog, setCatalog] = useState<CatalogEntry[]>([]);
  // saved: canonical file is current. dirty: unsaved edits — autosaved
  // into the space's dirty copy, but the article.md is untouched until an
  // explicit Save. saving: a write is in flight.
  const [saveStatus, setSaveStatus] = useState<"saved" | "saving" | "dirty">("saved");
  // A banner mode claimed the article frame: the H1 (and standfirst) live in
  // ---- shared chrome state ----------------------------------------------------
  const [assistOpen, setAssistOpen] = useState(false);
  const [consultOut, setConsultOut] = useState("advisory only — nothing enters the document until you accept it");
  const [proof, setProof] = useState<{ verdict: string; evidence: string } | null>(null);
  const [changes, setChanges] = useState<any[]>([]);
  const [selPaths, setSelPaths] = useState<Set<string>>(new Set());

  const autosaveTimer = useRef<number | null>(null);
  const dirtyRef = useRef(false);
  const docRef = useRef(doc);
  docRef.current = doc;
  const textRef = useRef(text);
  textRef.current = text;
  const slugRef = useRef<string | null>(null);
  textRef.current = text;

  const refreshDesk = useCallback(async () => {
    const d = await invoke<{ entries: any[] }>("desk");
    setEntries(d.entries);
  }, []);

  /** Compose projections from the working template copy when conducting,
   *  else straight from the space's file. */
  // Autosave: the editing copy lands in the space's dirty drafts. The
  // canonical article.md is never touched here — only Save writes it.
  const flush = useCallback(async () => {
    const d = docRef.current;
    if (!d) return;
    setSaveStatus("dirty");
    try {
      await invoke("save_dirty", { slug: d.slug, document: textRef.current });
    } catch (e: any) {
      setSaveStatus("dirty");
      setNote(e.message ?? String(e));
    }
  }, []);

  const flushRef = useRef(flush);
  flushRef.current = flush;

  // Discard: drop the dirty copy, the canonical file speaks again.
  const [discardAsk, setDiscardAsk] = useState(false);
  const discardChanges = useCallback(async () => {
    const d = docRef.current;
    if (!d) return;
    try {
      await invoke("discard_dirty", { slug: d.slug });
      setText(canonicalText);
      setSaveStatus("saved");
      dirtyRef.current = false;
      setDiscardAsk(false);
      setResetToken((t) => t + 1);
      await refreshDesk();
    } catch (e: any) {
      setNote(e.message ?? String(e));
    }
  }, [canonicalText, refreshDesk]);

  // Explicit Save: the editing text becomes the canonical article.md.
  const saveCanonical = useCallback(async () => {
    const d = docRef.current;
    if (!d) return;
    if (autosaveTimer.current) window.clearTimeout(autosaveTimer.current);
    setSaveStatus("saving");
    try {
      await invoke("save_document", { slug: d.slug, document: textRef.current });
      setCanonicalText(textRef.current);
      dirtyRef.current = false;
      setSaveStatus("saved");
      await refreshDesk();
    } catch (e: any) {
      setSaveStatus("dirty");
      setNote(e.message ?? String(e));
    }
  }, [refreshDesk]);
  const saveCanonicalRef = useRef(saveCanonical);
  saveCanonicalRef.current = saveCanonical;
  const [resetToken, setResetToken] = useState(0);

  // Conduct: the frame picked an option value for one slot occurrence;
  // compute the resulting hints and splice the working template copy.
  const conduct = useCallback(
    (raw: string, occurrence: number, current: string[], optKey: string, value: string) => {
      setTemplateDraft((prev) => {
        if (prev === null) return prev;
        const hints = nextHintsFor(current, optKey, value || null);
        return spliceSlot(prev, raw, occurrence, hints);
      });
    },
    [],
  );

  // v1 insertion anchors beside the article flow; finer anchors come with
  // in-frame insertion affordances.
  const insertAfterArticle = useCallback((name: string) => {
    setTemplateDraft((prev) => {
      if (prev === null) return prev;
      return insertSlotAt(prev, "{{ARTICLE}}", 0, "after", name);
    });
  }, []);

  // Insertion: same contract — draft bytes move, the frame recomposes.
  const insertSlot = useCallback(
    (anchorRaw: string, anchorOcc: number, where: "before" | "after", name: string) => {
      setTemplateDraft((prev) => {
        if (prev === null) return prev;
        return insertSlotAt(prev, anchorRaw, anchorOcc, where, name);
      });
    },
    []
  );

  const touch = useCallback(() => {
    if (!docRef.current) return;
    dirtyRef.current = true;
    setSaveStatus("dirty");
    if (autosaveTimer.current) window.clearTimeout(autosaveTimer.current);
    autosaveTimer.current = window.setTimeout(() => void flushRef.current(), 800);
  }, []);

  useEffect(() => {
    const h = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "s") {
        e.preventDefault();
        void saveCanonicalRef.current();
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

  // The space view has its own ribbon: Space Details vs the Article work
  // surface. Assistant remains a toggle of the side rail.
  const [spaceTab, setSpaceTab] = useState<"details" | "article">("article");

  // State toggle from Article Details / the pinbar select.
  const applyState = useCallback(async (state: string) => {
    const slug = slugRef.current;
    if (!slug) return;
    try {
      const r = await invoke<any>("set_article_state", { slug, state });
      setDoc((d0) => (d0 ? { ...d0, state: r.state ?? state } : d0));
    } catch (e: any) {
      setNote(e.message ?? String(e));
    }
  }, []);

  // Adding a space needs nothing but a folder: the folder name names the
  // space; everything else is editable later in Space Details.
  async function addPublication() {
    try {
      const path = await invoke<string | null>("pick_folder");
      if (!path) return;
      const folderName = path.split(/[\\/]/).pop() || "publication";
      const reg = await invoke<{ publications: any[] }>("registry_add", {
        pubData: { name: folderName, persona: "", path },
      });
      setPubList(reg.publications);
      await openSpace(path);
    } catch (e: any) {
      setLandingError(e.message ?? String(e));
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
      slugRef.current = slug;
      setSpaceTab("article");
      const a = await invoke<any>("read_article", { slug });
      setDoc({
        slug: a.article.meta.slug,
        title: (a.raw.match(/^# (.+)$/m) ?? [])[1] ?? slug.replace(/-/g, " "),
        state: String(a.article.meta.state).toLowerCase(),
        cover: a.article.meta.cover ?? null,
        date: a.article.meta.date ?? null,
        tags: a.article.meta.tags ?? [],
        id: a.article.meta.id ?? null,
        author: a.article.meta.author ?? null,
      });
      setCanonicalText(a.canonical_raw ?? "");
      setText(a.raw);
      // Conduct seed: the space's file when it owns one, else the embedded
      // default. templateFile records disk truth; the draft starts equal.
      let source: string | null;
      try {
        const t = await invoke<string | null>("read_template");
        if (t === null) {
          source = await invoke<string>("default_template");
          setTemplateFile(null);
        } else {
          source = t;
          setTemplateFile(t);
        }
      } catch {
        source = null;
      }
      setTemplateDraft(source);
      if (catalog.length === 0) {
        invoke<CatalogEntry[]>("slot_catalog").then(setCatalog).catch(() => {});
      }
      // Unsaved edits on disk (the dirty copy) reopen as the editing text.
      try {
        const r = await invoke<any>("read_article", { slug });
        setSaveStatus(r.dirty ? "dirty" : "saved");
      } catch { setSaveStatus("saved"); }
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

  // ---- surfaces: Write (the artifact's dress, editing in place), Source
  // (raw markdown bytes). The proof of what emits lives in render/ itself —
  // surfaced through the ship rail's render step, never a competing lens.
  const [mode, setMode] = useState<"details" | "write" | "source">("write");
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
        />
      )}

      {surface === "about" && <About />}

      {surface === "config" && (
        <Config spaces={pubList} onRemoveSpace={removePublication} />
      )}

      {surface === "space" && open && (
        <main>          <SpaceRail
            identity={identity} desk={{ entries }}
            activeSlug={activeSlug}
            onOpenArticle={loadArticle} onNewArticle={newDoc}
          />

          <section id="editor">
            <div className="space-ribbon">
              <button className={spaceTab === "article" ? "active" : ""}
                      onClick={() => setSpaceTab("article")}>Article</button>
              <button className={spaceTab === "details" ? "active" : ""}
                      onClick={() => { setSpaceTab("details"); setWorkspace(null); setDoc(null); setActiveSlug(null); setAssistOpen(false); }}>
                Space Details
              </button>
              <button onClick={() => setAssistOpen(!assistOpen)}>Assistant</button>
            </div>
            {spaceTab === "details" && open && (
              <SpaceDetails root={open.path} onSaved={async () => {
                try {
                  const id = await invoke<Identity>("read_identity", { path: open.path });
                  setIdentity(id);
                  await refreshDesk();
                } catch { /* the form already showed the error */ }
              }} />
            )}
            {spaceTab === "article" && workspace?.kind === "article" && doc && (
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
                    <option value="published">published</option>
                  </select>
                  <SaveDot status={saveStatus} />
                  <span style={{ flex: 1 }} />
                  <div className="mode-switch" role="tablist" aria-label="View">
                    <button role="tab" aria-selected={mode === "write"}
                            className={mode === "write" ? "active" : ""}
                            onClick={() => setMode("write")}>Write</button>
                    <button role="tab" aria-selected={mode === "details"}
                            className={mode === "details" ? "active" : ""}
                            onClick={() => setMode("details")}>Details</button>
                    <button role="tab" aria-selected={mode === "source"}
                            className={mode === "source" ? "active" : ""}
                            onClick={() => setMode("source")}>Source</button>
                  </div>
                  <button onClick={() => setAssistOpen(!assistOpen)}
                          title="Advisory help: polish, voice, facts">Assistant</button>
                  <select
                    aria-label="Insert an element"
                    value=""
                    onChange={(e2) => {
                      const name = e2.target.value;
                      if (!name) return;
                      insertSlot("{{ARTICLE}}", 0, "after", name);
                      e2.target.value = "";
                    }}
                  >
                    <option value="">Insert element…</option>
                    {catalog.filter((c) => c.name !== "ARTICLE").map((c) => (
                      <option key={c.name} value={c.name}>{c.name}</option>
                    ))}
                  </select>
                  {templateDraft !== templateFile && templateDraft !== null && (
                    <span className="conduct-bar" role="group" aria-label="Layout changes">
                      <span className="mono-fact">layout changed</span>
                      <button
                        className="primary"
                        onClick={async () => {
                          try {
                            await invoke("write_template", { text: templateDraft });
                            setTemplateFile(templateDraft);
                            setNote("layout saved to templates/article.html");
                          } catch (e: any) { setNote(e.message ?? String(e)); }
                        }}
                      >Save layout</button>
                      <button
                        onClick={() => { setTemplateDraft(templateFile ?? ""); }}
                      >Discard layout</button>
                    </span>
                  )}
                  {saveStatus === "dirty" && !discardAsk && (
                    <button onClick={() => { setDiscardAsk(true); }}
                            title="Drop unsaved edits — the saved file speaks again">Discard</button>
                  )}
                  {discardAsk && (
                    <span className="conduct-bar" role="group" aria-label="Discard changes">
                      <span className="mono-fact">drop unsaved edits?</span>
                      <button className="small-danger"
                              onClick={() => void discardChanges()}>Discard</button>
                      <button onClick={() => setDiscardAsk(false)}>Keep editing</button>
                    </span>
                  )}
                  <button
                    className={saveStatus === "dirty" ? "primary" : ""}
                    onClick={() => void saveCanonicalRef.current()}
                    disabled={saveStatus === "saving"}
                  >{saveStatus === "dirty" ? "Save" : saveStatus === "saving" ? "Saving…" : "Saved"}</button>
                </div>
                {mode === "details" && (
                  <ArticleDetails
                    slug={doc.slug}
                    id={doc.id ?? null}
                    state={doc.state}
                    date={doc.date}
                    tags={doc.tags}
                    cover={doc.cover}
                    author={doc.author ?? null}
                    byline={identity?.byline ?? ""}
                    mediaBase={mediaBase}
                    onChange={(patch) => {
                      setDoc((d0) => (d0 ? { ...d0, ...patch } : d0));
                      if (patch.state) { void applyState(patch.state); }
                      touch();
                    }}
                  />
                )}
                {mode === "source" && (
                  <div className="cm-host">
                    <CodeMirror
                      value={text} height="100%"
                      extensions={[markdown({ base: markdownLanguage, codeLanguages: languages })]}
                      onChange={(v) => { setText(v); touch(); }}
                      theme="dark" basicSetup={{ foldGutter: false }}
                    />
                  </div>
                )}
                {mode === "write" && (
                  <WritePane
                    slug={doc.slug}
                    template={templateDraft}
                    markdown={text}
                    canonical={canonicalText}
                    mediaBase={mediaBase}
                    resetToken={resetToken}
                    catalog={catalog}
                    onConduct={(raw, occurrence, current, optKey, value) => {
                      const hints = nextHintsFor(current, optKey, value || null);
                      conduct(raw, occurrence, current, optKey, value);
                    }}
                    onMarkdown={(md) => { setText(md); touch(); }}
                    onRefusal={(m) => setNote(m)}
                  />
                )}
              </>
            )}
            {spaceTab === "article" && !workspace && (
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

/** The cover control for the post-settings strip: thumbnail + replace/clear.
 *  Files store through the session's media command; the doc keeps the ref. */
function CoverStrip({ cover, mediaBase, onPick, onClear }: {
  cover: string | null;
  mediaBase: string;
  onPick: (ref: string) => void;
  onClear: () => void;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  return (
    <span className="cover-strip">
      <input
        ref={inputRef}
        type="file"
        accept="image/*"
        hidden
        onChange={async (e) => {
          const f = e.target.files?.[0];
          if (!f) return;
          setBusy(true);
          try {
            const buf = new Uint8Array(await f.arrayBuffer());
            const ref = await invoke<string>("add_media", {
              bytes: Array.from(buf),
              originalName: f.name || "cover.png",
            });
            onPick(ref);
          } finally {
            setBusy(false);
            e.target.value = "";
          }
        }}
      />
      {cover && (
        <img
          className="cover-strip-thumb"
          src={`${mediaBase}${(cover ?? "").replace(/^media\//, "")}`}
          alt=""
        />
      )}
      <button type="button" disabled={busy} onClick={() => inputRef.current?.click()}>
        {busy ? "storing…" : cover ? "replace cover" : "set cover"}
      </button>
      {cover && (
        <button type="button" onClick={onClear} title="Remove the cover">clear</button>
      )}
    </span>
  );
}

function SaveDot({ status }: { status: "saved" | "saving" | "dirty" }) {
  const label = status === "saved" ? "Saved" : status === "saving" ? "Saving…" : "Unsaved";
  const hint =
    status === "saved"
      ? "The article file is current."
      : status === "saving"
        ? "Writing…"
        : "Unsaved edits are protected in a draft copy — Save writes the article file.";
  return (
    <span className="save-dot-wrap" title={hint}>
      <span className={`saved-dot ${status}`} />
      <span className="save-label">{label}</span>
    </span>
  );
}
