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

declare global {
  interface Window {
    __TZ_BOOTED__?: boolean;
  }
}

const parent = window.parent !== window ? window.parent : null;
const pending = new Map<string, (ref: string) => void>();
let editor: Editor | null = null;
let mediaBase = "";

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

function injectCaretStyle() {
  const style = document.createElement("style");
  style.textContent = `
.ProseMirror { outline: none; min-height: 40vh; padding-bottom: 30vh; }
.ProseMirror p.is-editor-empty:first-child::before {
  content: attr(data-placeholder); float: left; height: 0; pointer-events: none;
  opacity: 0.4;
}
.ProseMirror img { cursor: pointer; }
`;
  document.head.appendChild(style);
}

function resolveSrc(src: string): string {
  return mediaBase && src.startsWith("../media/")
    ? mediaBase + src.slice("../media/".length)
    : src;
}

async function importImage(file: File) {
  const token = crypto.randomUUID();
  pending.set(token, (ref) => {
    editor
      ?.chain()
      .focus()
      .setImage({ src: resolveSrc(`../media/${ref}`) })
      .run();
  });
  const base64 = toBase64(await file.arrayBuffer());
  post({ type: "tz-image", token, name: file.name, base64 });
}

async function boot(markdown: string) {
  injectCaretStyle();
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
      Image.configure({ allowBase64: false, inline: false }),
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

  const dom = editor.view.dom as HTMLElement;
  dom.addEventListener("dragover", (e) => e.preventDefault());
}

window.addEventListener("message", (event) => {
  const msg = event.data ?? {};
  if (msg.type === "tz-init") {
    mediaBase = msg.mediaBase ?? "";
    void boot(msg.markdown ?? "");
    post({ type: "tz-ready" });
  } else if (msg.type === "tz-media") {
    const resolve = pending.get(msg.token);
    if (resolve) {
      pending.delete(msg.token);
      resolve(msg.ref);
    }
  }
});


// main() is invoked by the tz-init message; the flag guards double boots.
void (function guard() {
  if (!parent) {
    // Opened standalone: nothing to bridge.
    log("standalone");
  }
})();
