// The document-first writing surface: one continuous column, Substack-style.
//
// Title and standfirst are TipTap document nodes (docTitle/docStandfirst
// custom nodes) — same flow, same measure, same caret journey as the body.
// The toolbar is pinned above the scroll area; insertion verbs (image,
// divider, gallery hint) live there alongside formatting. Markdown file
// remains the source of truth via tiptap-markdown.

import React from "react";
import { EditorProvider, useCurrentEditor, ReactNodeViewRenderer, NodeViewWrapper } from "@tiptap/react";
import type { NodeViewProps } from "@tiptap/react";
import type { Editor } from "@tiptap/react";
import StarterKit from "@tiptap/starter-kit";
import Link from "@tiptap/extension-link";
import Image from "@tiptap/extension-image";
import Placeholder from "@tiptap/extension-placeholder";
import TaskList from "@tiptap/extension-task-list";
import TaskItem from "@tiptap/extension-task-item";
import Highlight from "@tiptap/extension-highlight";
import Underline from "@tiptap/extension-underline";
import CharacterCount from "@tiptap/extension-character-count";
import CodeBlockLowlight from "@tiptap/extension-code-block-lowlight";
import Focus from "@tiptap/extension-focus";
import Dropcursor from "@tiptap/extension-dropcursor";
import Gapcursor from "@tiptap/extension-gapcursor";
import { common, createLowlight } from "lowlight";
import { Markdown } from "tiptap-markdown";
import GalleryRun from "./Gallery";
import { BubbleMenu } from "@tiptap/react/menus";import {
  Bold, Italic, Strikethrough, Code, Underline as UnderlineIcon, Highlighter,
  List, ListOrdered, Quote, Link2, Minus, Image as ImageIcon,
  CheckSquare, Eye, Undo2, Redo2, Settings,
} from "lucide-react";

// ---------------------------------------------------------------------------
// Document title / standfirst as real TipTap nodes: part of the content flow,
// serialized to frontmatter on save (never into the body markdown).
// ---------------------------------------------------------------------------

export interface WriterProps {
  initialMarkdown: string;
  slug: string;
  onChange: (md: string) => void;
  words: number;
}

// The live editor instance, reachable from editorProps handlers (which fire
// before any hook context exists for a given paste event).
let activeEditor: Editor | null = null;
function EditorBinder() {
  const { editor } = useCurrentEditor();
  React.useEffect(() => {
    activeEditor = (editor as Editor) ?? null;
    return () => { activeEditor = null; };
  }, [editor]);
  return null;
}

function tauri(): any {
  const t = (window as any).__TAURI__;
  if (!t?.core?.invoke) throw new Error("Tauri bridge not ready");
  return t.core;
}

/** Store image files through the app's media command, then insert references
 *  into the document. Data URIs never enter article.md. */
async function importImageFiles(files: File[], editor: Editor | null): Promise<void> {
  if (!editor) {
    window.dispatchEvent(new CustomEvent("tezuri:media-error", {
      detail: "editor is not ready yet — try again",
    }));
    return;
  }
  for (const f of files) {
    try {
      const buf = new Uint8Array(await f.arrayBuffer());
      const ref = await tauri().invoke("add_media", {
        bytes: Array.from(buf),
        originalName: f.name || "pasted-image.png",
      });
      activeEditor?.chain().focus().setImage({ src: ref }).run();
    } catch (e: any) {
      window.dispatchEvent(new CustomEvent("tezuri:media-error", {
        detail: e.message ?? String(e),
      }));
    }
  }
}

export function Writer({ initialMarkdown, slug, onChange, words }: WriterProps) {
  const [focusMode, setFocusMode] = React.useState(false);
  const suppressUpdate = React.useRef(false);

  return (
    <div className="writer-column">
    <EditorProvider
      key={slug}
      immediatelyRender={false}
      extensions={[
        StarterKit.configure({ heading: { levels: [2, 3] } }),
        Link.configure({ openOnClick: false, autolink: true }),
        Image.extend({
          addNodeView() {
            return ReactNodeViewRenderer((props: NodeViewProps) => {
              const doc = props.editor.state.doc;
              const pos = props.getPos();
              if (typeof pos !== "number") return <NodeViewWrapper />;
              const $pos = doc.resolve(pos);
              const node = $pos.parent.childAfter($pos.parentOffset).node;
              if (!node || node.type.name !== "image") return <NodeViewWrapper />;
              const collect: { src: string; alt?: string }[] = [];
              let isFirstOfRun = !$pos.nodeBefore || $pos.nodeBefore.type.name !== "image";
              if (isFirstOfRun) {
                collect.push({ src: node.attrs.src as string, alt: node.attrs.alt as string });
                let scan = pos + node.nodeSize;
                for (;;) {
                  const $scan = doc.resolve(scan);
                  const next = $scan.parent.childAfter($scan.parentOffset);
                  if (next.node?.type.name === "image" && next.offset === 0) {
                    collect.push({ src: next.node.attrs.src as string, alt: next.node.attrs.alt as string });
                    scan += next.node.nodeSize;
                  } else break;
                  if (collect.length > 12) break;
                }
                if (collect.length >= 2) {
                  return (
                    <NodeViewWrapper className="gallery-wrapper">
                      <GalleryRun images={collect} />
                    </NodeViewWrapper>
                  );
                }
              }
              if (!isFirstOfRun) return <NodeViewWrapper style={{ display: "none" }} />;
              // Figure pattern: caption = next paragraph wrapped in _…_
              const $after = doc.resolve(pos + node.nodeSize);
              const afterNode = $after.parent.childAfter($after.parentOffset).node;
              let caption = "";
              const afterText = afterNode?.textContent ?? "";
              const m = afterText.match(/^_([^_]+)_$/);
              if (afterNode?.type.name === "paragraph" && m) caption = m[1];
              return (
                <NodeViewWrapper className="figure-wrapper">
                  <img
                    src={node.attrs.src}
                    alt={caption || node.attrs.alt || ""}
                    className="solo-img"
                    style={{ maxWidth: "100%", maxHeight: "60vh", width: "auto",
                             height: "auto", display: "block", margin: "1.4em auto" }}
                  />
                  <figcaption
                    className="fig-caption"
                    contentEditable
                    suppressContentEditableWarning
                    data-placeholder="Add a caption…"
                    onBlur={(e: any) => {
                      const v = e.currentTarget.textContent?.trim() ?? "";
                      if (v === caption) return;
                      const emd = v ? `_${v}_` : "";
                      if (afterNode?.type.name === "paragraph" && m) {
                        props.editor.chain().focus().insertContentAt(
                          pos + node.nodeSize,
                          emd ? { type: "paragraph", content: [{ type: "text", text: emd, marks: [{ type: "italic" }] }] } : { type: "paragraph" }
                        ).run();
                      } else if (emd) {
                        props.editor.chain().insertContentAt(pos + node.nodeSize, {
                          type: "paragraph",
                          content: [{ type: "text", text: emd, marks: [{ type: "italic" }] }],
                        }).run();
                      }
                    }}
                  >{caption}</figcaption>
                </NodeViewWrapper>
              );
            });
          },
        }).configure({ inline: false, allowBase64: false }),
        Placeholder.configure({
          placeholder: ({ node }: any) =>
            node.type.name === "docTitle" ? "Article title"
            : node.type.name === "docStandfirst" ? "Add a standfirst…"
            : "Start writing…",
        }),
        TaskList,
        TaskItem.configure({ nested: true }),
        Highlight,
        Underline,
        CodeBlockLowlight.configure({ lowlight: createLowlight(common) }),
        Focus.configure({ className: "has-focus", mode: "shallowest" }),
        Dropcursor,
        Gapcursor,
        CharacterCount.configure({ limit: null }),
        Markdown.configure({ html: false, tightLists: true, linkify: true, breaks: false }),
      ]}
      content={initialMarkdown}
      onUpdate={({ editor }) => onChange((editor.storage as any).markdown.getMarkdown())}
      editorProps={{
        attributes: { class: "writer", spellcheck: "true" },
        handlePaste: (_view, event) => {
          const files = Array.from(event.clipboardData?.files ?? [])
            .filter((f) => f.type.startsWith("image/"));
          if (files.length === 0) return false;
          void importImageFiles(files, activeEditor);
          return true; // never let base64 land in the Markdown flow
        },
        handleDrop: (_view, event) => {
          const files = Array.from(event.dataTransfer?.files ?? [])
            .filter((f) => f.type.startsWith("image/"));
          if (files.length === 0) return false;
          void importImageFiles(files, activeEditor);
          return true;
        },
      }}
      slotBefore={<PinnedBar focusMode={focusMode} setFocusMode={setFocusMode} words={words} />}
    >
      <EditorBinder />
      <SelectionBubble />
    </EditorProvider>
    </div>
  );
}

// ---- pinned toolbar --------------------------------------------------------

function PinnedBar({ focusMode, setFocusMode, words }: {
  focusMode: boolean; setFocusMode: (v: boolean) => void; words: number;
}) {
  const { editor } = useCurrentEditor();
  if (!editor) return null;
  const chain = () => editor.chain().focus();
  const tb = (active?: boolean) => `tool-btn${active ? " active" : ""}`;
  const T = ({ label, hint, active, onClick, children }: any) => (
    <button title={hint ? `${label} (${hint})` : label} aria-label={label}
            className={tb(active)} onMouseDown={(e) => e.preventDefault()}
            onClick={onClick}>
      {children}
    </button>
  );

  return (
    <div className="pinbar-tools" role="toolbar" aria-label="Formatting">
      <div className="toolbar">
        <T label="Undo" hint="Ctrl+Z"
           onClick={() => chain().undo().run()}><Undo2 size={14} /></T>
        <T label="Redo" hint="Ctrl+Shift+Z"
           onClick={() => chain().redo().run()}><Redo2 size={14} /></T>
        <span className="bubble-sep" />
        <T label="Bold" hint="Ctrl+B" active={editor.isActive("bold")}
           onClick={() => chain().toggleBold().run()}><Bold size={14} /></T>
        <T label="Italic" hint="Ctrl+I" active={editor.isActive("italic")}
           onClick={() => chain().toggleItalic().run()}><Italic size={14} /></T>
        <T label="Strikethrough" hint="Ctrl+Shift+S" active={editor.isActive("strike")}
           onClick={() => chain().toggleStrike().run()}><Strikethrough size={14} /></T>
        <T label="Underline" hint="Ctrl+U" active={editor.isActive("underline")}
           onClick={() => chain().setUnderline().run()}><UnderlineIcon size={14} /></T>
        <T label="Highlight" active={editor.isActive("highlight")}
           onClick={() => chain().toggleHighlight().run()}><Highlighter size={14} /></T>
        <T label="Code" active={editor.isActive("code")}
           onClick={() => chain().toggleCode().run()}><Code size={14} /></T>
        <span className="bubble-sep" />
        <T label="Heading 2" active={editor.isActive("heading", { level: 2 })}
           onClick={() => chain().toggleHeading({ level: 2 }).run()}><b>H2</b></T>
        <T label="Heading 3" active={editor.isActive("heading", { level: 3 })}
           onClick={() => chain().toggleHeading({ level: 3 }).run()}><b>H3</b></T>
        <span className="bubble-sep" />
        <T label="Bullet list" active={editor.isActive("bulletList")}
           onClick={() => chain().toggleBulletList().run()}><List size={14} /></T>
        <T label="Numbered list" active={editor.isActive("orderedList")}
           onClick={() => chain().toggleOrderedList().run()}><ListOrdered size={14} /></T>
        <T label="Checklist" active={editor.isActive("taskList")}
           onClick={() => chain().toggleTaskList().run()}><CheckSquare size={14} /></T>
        <T label="Quote" active={editor.isActive("blockquote")}
           onClick={() => chain().toggleBlockquote().run()}><Quote size={14} /></T>
        <span className="bubble-sep" />
        <T label="Link" active={editor.isActive("link")}
           onClick={() => {
             const prev = editor.getAttributes("link").href ?? "";
             const url = prompt("URL:", prev);
             if (url === null) return;
             url === "" ? chain().unsetLink().run()
                        : chain().setLink({ href: url }).run();
           }}><Link2 size={14} /></T>
        <T label="Image"
           onClick={() => {
             const url = prompt("Image URL or media/ path:");
             if (url) chain().setImage({ src: url }).run();
           }}><ImageIcon size={14} /></T>
        <T label="Divider"
           onClick={() => chain().setHorizontalRule().run()}><Minus size={14} /></T>
        <span className="bubble-sep" />
        <button title="Focus mode — dim everything but the current paragraph"
                className={`tool-btn${focusMode ? " active" : ""}`}
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => setFocusMode(!focusMode)}>
          <Eye size={14} />
        </button>
        <T label="Post settings — tags, cover, date"
           onClick={() => window.dispatchEvent(new CustomEvent("tezuri:settings"))}>
          <Settings size={14} /></T>
        <span style={{ flex: 1 }} />
        <span className="wordcount">{words} words</span>
      </div>
    </div>
  );
}

function SelectionBubble() {
  const { editor } = useCurrentEditor();
  if (!editor) return null;
  const b = (a: boolean) => `bubble-btn${a ? " active" : ""}`;
  const chain = () => editor.chain().focus();
  return (
    <BubbleMenu options={{ placement: "top", offset: 8 }}>
      <button className={b(editor.isActive("bold"))}
              onClick={() => chain().toggleBold().run()}><Bold size={14} /></button>
      <button className={b(editor.isActive("italic"))}
              onClick={() => chain().toggleItalic().run()}><Italic size={14} /></button>
      <button className={b(editor.isActive("strike"))}
              onClick={() => chain().toggleStrike().run()}><Strikethrough size={14} /></button>
      <span className="bubble-sep" />
      <button className={b(editor.isActive("link"))}
              onClick={() => {
                const prev = editor.getAttributes("link").href ?? "";
                const url = prompt("URL:", prev);
                if (url === null) return;
                url === "" ? chain().unsetLink().run()
                           : chain().setLink({ href: url }).run();
              }}><Link2 size={14} /></button>
    </BubbleMenu>
  );
}
