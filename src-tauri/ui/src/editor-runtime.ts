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

window.addEventListener("message", (event) => {
  const msg = event.data ?? {};
  if (msg.type === "tz-init") {
    if (booted) return; // idempotent: a booted editor never re-boots
    mediaBase = msg.mediaBase ?? "";
    canonical = msg.canonical ?? "";
    const markdown = msg.markdown ?? "";
    void boot(markdown).then(() => {
      booted = true;
      lastKnown = markdown;
      post({ type: "tz-booted" });
    });
  } else if (msg.type === "tz-canonical") {
    // The host saved (or loaded): the baseline moved, so the wash moves.
    canonical = msg.canonical ?? "";
    refreshUnsavedMarks();
  } else if (msg.type === "tz-media") {
    const resolve = pending.get(msg.token);
    if (resolve) {
      pending.delete(msg.token);
      resolve(msg.ref);
    }
  }
});

// Announce readiness the moment the listener exists. The host retries
// tz-init until the boot ack arrives, so a lost message is harmless.
post({ type: "tz-ready" });


// The runtime announces itself the moment it can listen: the host answers
// with tz-init (markdown + media base), and boot mounts the editor.
post({ type: "tz-ready" });
