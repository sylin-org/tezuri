// The Write-plane editor runtime: a plain IIFE script injected into the
// artifact page served inside the Write iframe. It mounts TipTap over the
// article prose — the artifact's own layout, cascade, and theme stay
// exactly as emitted — and round-trips markdown with the host desk over
// postMessage.
//
// Host -> runtime:  {type:"tz-init", markdown, mediaBase}
//                   {type:"tz-media", token, ref}
// Runtime -> host:  {type:"tz-ready"}
//                   {type:"tz-change", markdown}
//                   {type:"tz-image", token, name, base64}
import { Editor } from "@tiptap/core";
import StarterKit from "@tiptap/starter-kit";
import Link from "@tiptap/extension-link";
import Image from "@tiptap/extension-image";
import Underline from "@tiptap/extension-underline";
import TaskList from "@tiptap/extension-task-list";
import TaskItem from "@tiptap/extension-task-item";
import Highlight from "@tiptap/extension-highlight";
import { CodeBlockLowlight } from "@tiptap/extension-code-block-lowlight";
import { common, createLowlight } from "lowlight";
import { Markdown } from "tiptap-markdown";

/// Display-only src mapping: the file stores relative `media/...` refs;
/// the rendered img rides the media protocol. Storage is never rewritten.
const DisplayImage = Image.extend({
  // Inline is the load-bearing option: block-level images are what made
  // tiptap-markdown glue the following heading onto the image paragraph.
  // The default would silently revert through extend unless re-asserted.
  addOptions() {
    const parent = this.parent?.() ?? { HTMLAttributes: {}, resize: { enabled: false } as any };
    return { ...parent, inline: true as const, allowBase64: false as const };
  },
  renderHTML({ node, HTMLAttributes }) {
    let src = HTMLAttributes.src ?? "";
    if (mediaBase && !/^(https?:|data:|blob:|media:)/i.test(src)) {
      const rel = src.replace(/^(\.\.\/)?/, "").replace(/^(media\/)?/, "media/");
      src = mediaBase + rel;
    }
    return ["img", { ...HTMLAttributes, src }];
  },
}).configure({ allowBase64: false });
import { Plugin, PluginKey } from "@tiptap/pm/state";
import { Decoration, DecorationSet } from "@tiptap/pm/view";

declare global {
  interface Window {
    __TZ_BOOTED__?: boolean;
  }
}

const parent = window.parent !== window ? window.parent : null;
const pending = new Map<string, (ref: string) => void>();
let editor: Editor | null = null;
let mediaBase = "";
// Handshake + no-op guard state: init is retried by the host until this
// side acks a boot, and only real content diffs are ever reported back —
// a boot must never autosave a round-trip normalization.
let booted = false;
let lastKnown = "";
let catalog: any[] = [];
// The diff baseline: whatever canonical text the host last confirmed
// (the saved article now; the published artifact later). Blocks that
// differ from it wear a gentle wash — the unsaved areas, made visible.
let canonical = "";

function post(msg: Record<string, unknown>) {
  parent?.postMessage(msg, "*");
}

function log(message: string) {
  post({ type: "tz-log", message });
}

// ---- file-drag landing ------------------------------------------------------
// Module scope, not boot(): the listeners must exist exactly once per
// document, or the enter/leave depth counter breaks and the fade sticks.
let dragDepth = 0;
let dragIdleTimer: number | undefined;
let dropHint: HTMLDivElement | null = null;

function hasFiles(e: DragEvent): boolean {
  return Array.from(e.dataTransfer?.types ?? []).includes("Files");
}

// The artifact page belongs to the space's own template, so "everything
// except the prose" cannot be a fixed selector list: dim the whole tree,
// then re-light the ancestor chain of .article-prose plus the hint chip.
function lightLandingPath() {
  document.querySelectorAll(".tz-lit").forEach((el) => el.classList.remove("tz-lit"));
  let el: Element | null = document.querySelector(".article-prose");
  while (el && el !== document.body) {
    el.classList.add("tz-lit");
    el = el.parentElement;
  }
  document.querySelector(".article-prose")?.classList.add("tz-lit");
  dropHint?.classList.add("tz-lit");
}

function setDragLanding(on: boolean) {
  document.documentElement.classList.toggle("tz-drag", on);
  if (dropHint) dropHint.style.display = on ? "" : "none";
  if (on) {
    if (!dropHint) {
      dropHint = document.createElement("div");
      dropHint.className = "tz-drop-hint tz-lit";
      dropHint.textContent = "release to place an image";
      document.body.appendChild(dropHint);
    }
    lightLandingPath();
  }
}

document.addEventListener("dragenter", (e) => {
  if (!hasFiles(e)) return;
  dragDepth += 1;
  setDragLanding(true);
});
document.addEventListener("dragleave", () => {
  if (dragDepth > 0) dragDepth -= 1;
  if (dragDepth === 0) setDragLanding(false);
});
// A drop that misses the prose must never navigate this frame to the raw
// file — swallowing it is the Dreamweaver-trauma guard. The editor's
// handleDrop is the only place an import can happen.
document.addEventListener("dragover", (e) => {
  e.preventDefault();
  if (!hasFiles(e)) return;
  // A canceled or absorbed drag may never deliver dragleave/drop — the
  // landing state heals itself when dragover stops arriving.
  window.clearTimeout(dragIdleTimer);
  dragIdleTimer = window.setTimeout(() => {
    dragDepth = 0;
    setDragLanding(false);
  }, 350);
});
document.addEventListener("drop", () => {
  dragDepth = 0;
  window.clearTimeout(dragIdleTimer);
  setDragLanding(false);
});

function toBase64(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let binary = "";
  for (let i = 0; i < bytes.length; i += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
  }
  return btoa(binary);
}

const unsavedKey = new PluginKey("tz-unsaved");

function blocksOf(md: string): string[] {
  return md
    .split(/\n\s*\n/)
    .map((b) => b.replace(/\s+/g, " ").trim())
    .filter((b) => b.length > 0);
}

function computeUnsavedDecorations(doc: any): DecorationSet {
  if (!canonical) return DecorationSet.empty;
  const canon = blocksOf(canonical);
  const cur: string[] = [];
  const offsets: number[] = [];
  let off = 0;
  doc.forEach((node: any) => {
    offsets.push(off);
    off += node.nodeSize;
    cur.push(node.textContent.replace(/\s+/g, " ").trim());
  });
  // LCS alignment over blocks: same = quiet, skipped current blocks =
  // unsaved (inserted or rewritten), skipped canonical blocks = deletions
  // (nothing in the document to wash).
  const n = canon.length, m = cur.length;
  const dp: number[][] = Array.from({ length: n + 1 }, () => new Array(m + 1).fill(0));
  for (let i = n - 1; i >= 0; i--)
    for (let j = m - 1; j >= 0; j--)
      dp[i][j] = canon[i] === cur[j] ? dp[i + 1][j + 1] + 1 : Math.max(dp[i + 1][j], dp[i][j + 1]);
  const decos: any[] = [];
  let i = 0, j = 0;
  const mark = (idx: number) => {
    decos.push(Decoration.node(offsets[idx], offsets[idx] + doc.child(idx).nodeSize, { class: "tz-unsaved" }));
  };
  while (i < n && j < m) {
    if (canon[i] === cur[j]) { i++; j++; continue; }
    if (dp[i + 1][j] >= dp[i][j + 1]) { i++; continue; }
    mark(j); j++;
  }
  while (j < m) { mark(j); j++; }
  return DecorationSet.create(doc, decos);
}

const unsavedPlugin = new Plugin({
  key: unsavedKey,
  state: {
    init: (_: any, state: any) => computeUnsavedDecorations(state.doc),
    apply: (tr: any, old: DecorationSet) =>
      tr.docChanged || tr.getMeta(unsavedKey)
        ? computeUnsavedDecorations(tr.doc)
        : old,
  },
  props: { decorations: (state: any) => unsavedKey.getState(state) },
});

function refreshUnsavedMarks() {
  if (!editor) return;
  const tr = editor.state.tr.setMeta(unsavedKey, { recompute: true });
  editor.view.dispatch(tr);
}

function injectCaretStyle() {
  const style = document.createElement("style");
  style.textContent = `
.ProseMirror { outline: none; min-height: 40vh; padding-bottom: 30vh; }
.ProseMirror p.is-editor-empty:first-child::before {
  content: attr(data-placeholder); float: left; height: 0; pointer-events: none;
  opacity: 0.4;
}
.ProseMirror img { cursor: pointer; }
.tz-unsaved { background: rgba(255, 196, 90, 0.09); transition: background 0.4s ease; }
.tz-conduct-chip { position: absolute; top: 4px; right: 4px; z-index: 30;
  border: 1px solid rgba(255,255,255,.25); background: rgba(0,0,0,.55); color: #eee;
  border-radius: 999px; font-size: 10px; line-height: 1; padding: 2px 6px;
  opacity: 0; transition: opacity .2s; cursor: pointer; }
[data-tz-slot]:hover > .tz-conduct-chip { opacity: 1; }
.tz-menu { position: absolute; top: 22px; right: 4px; z-index: 40; min-width: 180px;
  background: rgba(12,12,12,.96); border: 1px solid rgba(255,255,255,.2);
  border-radius: 8px; padding: 6px; display: flex; flex-direction: column; gap: 4px; }
.tz-menu-title { font: 600 11px/1.4 sans-serif; color: #bbb; padding: 2px 4px; }
.tz-menu-opt { text-align: left; background: none; border: none; color: #eee;
  font: 12px/1.6 sans-serif; padding: 3px 6px; border-radius: 5px; cursor: pointer; }
.tz-menu-opt:hover { background: rgba(255,255,255,.12); }
/* File-drag landing: the artifact page fades except the prose, which wears
   the landing outline; the hint chip floats above it all. Instant by
   default — a fade is motion, and motion is opt-in (reduced-motion law). */
html.tz-drag body * { opacity: .35 !important; filter: grayscale(.6); }
html.tz-drag body .tz-lit, html.tz-drag body .tz-lit * { opacity: 1 !important; filter: none; }
html.tz-drag .article-prose { outline: 2px dashed rgba(190, 242, 100, .55); outline-offset: 10px; }
.tz-drop-hint { position: fixed; top: 14px; left: 50%; transform: translateX(-50%);
  z-index: 2147483647; pointer-events: none; padding: 6px 12px; border-radius: 8px;
  background: rgba(12,12,12,.96); border: 1px solid rgba(255,255,255,.2);
  font: 600 12px/1.6 sans-serif; color: #bef264; }
@media (prefers-reduced-motion: no-preference) {
  html.tz-drag body * { transition: opacity .15s ease, filter .15s ease; }
}
`;
  document.head.appendChild(style);
}

async function importImage(file: File) {
  const token = crypto.randomUUID();
  pending.set(token, (ref) => {
    editor
      ?.chain()
      .focus()
      .setImage({ src: `media/${ref.replace(/^media\//, "")}` })
      .run();
  });
  const base64 = toBase64(await file.arrayBuffer());
  post({ type: "tz-image", token, name: file.name, base64 });
}

async function boot(markdown: string) {
  injectCaretStyle();
  // Re-booting (new page in this frame) must retire the old editor first:
  // two live editors over one mount fight over the caret and the state.
  if (editor) {
    editor.destroy();
    editor = null;
  }
  const prose = document.querySelector(".article-prose");
  if (!prose) {
    log("no .article-prose in the artifact — mounting a bare host");
    const host = document.createElement("div");
    host.className = "article-prose";
    document.body.appendChild(host);
  }
  const mount = document.querySelector(".article-prose") as HTMLElement | null;
  if (!mount) return;

  // The artifact's compiled prose is replaced by the editable document:
  // same markdown, so nothing is lost — only the caret arrives.
  mount.innerHTML = "";

  editor = new Editor({
    element: mount,
    extensions: [
      StarterKit.configure({
        heading: { levels: [1, 2, 3] },
        link: false,
        underline: false,
        codeBlock: false,
      }),
      Link.configure({ openOnClick: false }),
      DisplayImage.configure({ allowBase64: false }),
      Underline,
      TaskList,
      TaskItem.configure({ nested: true }),
      Highlight,
      CodeBlockLowlight.configure({ lowlight: createLowlight(common) }),
      Markdown.configure({ html: true, tightLists: true, linkify: true, breaks: false }),
    ],
    content: markdown,
    onUpdate: ({ editor: e }) => {
      const md = (e.storage as any).markdown.getMarkdown();
      if (md === lastKnown) return; // normalization is not an edit
      lastKnown = md;
      post({ type: "tz-change", markdown: md });
    },
    editorProps: {
      attributes: { class: "tz-write-prose", spellcheck: "true" },
      handlePaste: (_view, event) => {
        const files = Array.from(event.clipboardData?.files ?? []).filter((f) =>
          f.type.startsWith("image/"),
        );
        if (files.length === 0) return false;
        event.preventDefault();
        files.forEach((f) => void importImage(f));
        return true;
      },
      handleDrop: (_view, event) => {
        const files = Array.from(event.dataTransfer?.files ?? []).filter((f) =>
          f.type.startsWith("image/"),
        );
        if (files.length === 0) return false;
        event.preventDefault();
        files.forEach((f) => void importImage(f));
        return true;
      },
    },
  });

  editor.registerPlugin(unsavedPlugin);
  refreshUnsavedMarks();

  const dom = editor.view.dom as HTMLElement;
  dom.addEventListener("dragover", (e) => e.preventDefault());
}

// Conduct affordances: every wrapped slot gains a chip that opens a
// small menu of the catalog's options for that slot. A choice posts
// tz-conduct; the host splices the template draft and recomposes.
function attachConductAffordances() {
  const wrappers = document.querySelectorAll<HTMLElement>("[data-tz-slot]");
  const seen = new Map<string, number>();
  wrappers.forEach((wrap) => {
    const raw = wrap.getAttribute("data-tz-slot") ?? "";
    const current = (wrap.getAttribute("data-tz-hints") ?? "")
      .split(",").map((h) => h.trim()).filter(Boolean);
    const occ = seen.get(raw) ?? 0;
    seen.set(raw, occ + 1);
    const name = (/^\{\{\s*([A-Za-z_][A-Za-z0-9_]*)/.exec(raw) ?? [])[1] ?? "";
    const entry = catalog.find((e) => e.name === name);

    const chip = document.createElement("button");
    chip.className = "tz-conduct-chip";
    chip.textContent = "⋯";
    chip.title = entry ? entry.doc : name;
    chip.addEventListener("click", (e) => {
      e.stopPropagation();
      const existing = wrap.querySelector(".tz-menu");
      if (existing) { existing.remove(); return; }
      document.querySelectorAll(".tz-menu").forEach((m) => m.remove());
      const menu = document.createElement("div");
      menu.className = "tz-menu";
      const title = document.createElement("div");
      title.className = "tz-menu-title";
      title.textContent = name;
      menu.appendChild(title);
      for (const opt of entry?.options ?? []) {
        for (const value of opt.values) {
          const b = document.createElement("button");
          b.className = "tz-menu-opt";
          b.textContent = opt.key ? `${opt.key}:${value}` : value;
          b.addEventListener("click", () => {
            menu.remove();
            post({ type: "tz-conduct", raw, occurrence: occ, current, optKey: opt.key, value });
          });
          menu.appendChild(b);
        }
      }
      wrap.appendChild(menu);
    });
    wrap.appendChild(chip);
  });
}

window.addEventListener("message", (event) => {
  const msg = event.data ?? {};
  if (msg.type === "tz-init") {
    if (booted) return; // idempotent: a booted editor never re-boots
    mediaBase = msg.mediaBase ?? "";
    canonical = msg.canonical ?? "";
    catalog = msg.catalog ?? [];
    void boot(msg.markdown ?? "").then(() => {
      booted = true;
      lastKnown = msg.markdown ?? "";
      attachConductAffordances();
      post({ type: "tz-booted" });
    });
    return;
  }
  if (msg.type === "tz-canonical") {
    canonical = msg.canonical ?? '';
    refreshUnsavedMarks();
  } else if (msg.type === 'tz-media') {
    const resolve = pending.get(msg.token);
    if (resolve) {
      pending.delete(msg.token);
      resolve(msg.ref);
    }
  }
});

// Announce readiness the moment the listener exists. The host retries
// tz-init until the boot ack arrives, so a lost message is harmless.
post({ type: 'tz-ready' });
