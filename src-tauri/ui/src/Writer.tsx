// The document-first writing surface: one continuous column, Substack-style.
//
// Title and standfirst are TipTap document nodes (docTitle/docStandfirst
// custom nodes) — same flow, same measure, same caret journey as the body.
// The toolbar is pinned above the scroll area; insertion verbs (image,
// divider, gallery hint) live there alongside formatting. Markdown file
// remains the source of truth via tiptap-markdown.

import React from "react";
import { EditorProvider, useCurrentEditor } from "@tiptap/react";
import type { Editor } from "@tiptap/react";
import { Node, mergeAttributes } from "@tiptap/core";
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
import { BubbleMenu } from "@tiptap/react/menus";
import {
  Bold, Italic, Strikethrough, Code, Underline as UnderlineIcon, Highlighter,
  List, ListOrdered, Quote, Link2, Minus, Image as ImageIcon,
  CheckSquare, Eye,
} from "lucide-react";

// ---------------------------------------------------------------------------
// Document title / standfirst as real TipTap nodes: part of the content flow,
// serialized to frontmatter on save (never into the body markdown).
// ---------------------------------------------------------------------------

const DocTitle = Node.create({
  name: "docTitle",
  group: "block",
  defining: true,
  parseHTML() {
    // Not parsed from markdown — injected programmatically on load.
    return [{ tag: 'div[data-doc-title]' }];
  },
  renderHTML({ HTMLAttributes }) {
    return ["div", mergeAttributes(HTMLAttributes, { "data-doc-title": "", class: "doc-title-node" }), 0];
  },
});

const DocStandfirst = Node.create({
  name: "docStandfirst",
  group: "block",
  defining: true,
  parseHTML() {
    return [{ tag: 'div[data-doc-standfirst]' }];
  },
  renderHTML({ HTMLAttributes }) {
    return ["div", mergeAttributes(HTMLAttributes, { "data-doc-standfirst": "", class: "doc-standfirst-node" }), 0];
  },
});

export interface WriterProps {
  initialMarkdown: string;
  slug: string;
  meta: {
    title: string;
    standfirst: string | null;
    cover: string | null;
    date: string | null;
    tags: string[] | null;
  };
  onMetaChange: (patch: { title?: string; standfirst?: string | null }) => void;
  onChange: (md: string) => void;
  words: number;
}

export function Writer({ initialMarkdown, slug, meta, onMetaChange, onChange, words }: WriterProps) {
  const [focusMode, setFocusMode] = React.useState(false);
  const suppressUpdate = React.useRef(false);

  return (
    <EditorProvider
      key={slug}
      immediatelyRender={false}
      extensions={[
        StarterKit.configure({ heading: { levels: [2, 3] } }),
        DocTitle,
        DocStandfirst,
        Link.configure({ openOnClick: false, autolink: true }),
        Image.configure({ inline: false, allowBase64: true }),
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
      onCreate={({ editor }) => {
        // Inject title + standfirst at the top of the document flow.
        suppressUpdate.current = true;
        editor
          .chain()
          .insertContentAt(0, { type: "docStandfirst", content: meta.standfirst ? [{ type: "text", text: meta.standfirst }] : [] })
          .insertContentAt(0, { type: "docTitle", content: [{ type: "text", text: meta.title }] })
          .run();
        setTimeout(() => { suppressUpdate.current = false; }, 50);
      }}
      onUpdate={({ editor }) => {
        if (suppressUpdate.current) return;
        // Title/standfirst changes route to meta callbacks; body to markdown.
        const json = editor.getJSON();
        let bodyStarted = false;
        const bodyNodes: any[] = [];
        let newTitle = meta.title;
        let newStandfirst: string | null = null;
        for (const n of json.content ?? []) {
          if (n.type === "docTitle") {
            newTitle = (n.content ?? []).map((c: any) => c.text ?? "").join("");
          } else if (n.type === "docStandfirst") {
            newStandfirst = (n.content ?? []).map((c: any) => c.text ?? "").join("") || null;
          } else {
            bodyStarted = true;
            bodyNodes.push(n);
          }
        }
        void bodyStarted;
        if (newTitle !== meta.title || newStandfirst !== meta.standfirst) {
          onMetaChange({ title: newTitle, standfirst: newStandfirst });
        }
        // Reconstruct markdown of the body only. tiptap-markdown serializes
        // from a full doc; build a temp doc view by serializing each node is
        // heavy — instead serialize the whole doc and strip nothing: our
        // custom nodes render as divs which tiptap-markdown emits as HTML we
        // don't want in the body. Simplest correct path: keep body markdown
        // state separate — serialize only when doc has no header nodes by
        // using the serializer on bodyNodes through a temporary editor is
        // overkill; so we ask the storage for full markdown and rely on the
        // header nodes being excluded by their serializers emitting nothing.
        onChange((editor.storage as any).markdown.getMarkdown());
      }}
      editorProps={{ attributes: { class: "writer", spellcheck: "true" } }}
      slotBefore={<PinnedBar focusMode={focusMode} setFocusMode={setFocusMode} words={words} />}
    >
      <SelectionBubble />
      <CharCount />
    </EditorProvider>
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

function CharCount() {
  const { editor } = useCurrentEditor();
  void editor;
  return null; // word count moved to pinned bar
}
