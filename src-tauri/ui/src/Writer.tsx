// The document-first writing surface: Substack-style.
//
// Toolbar and bubble menu are built from official TipTap primitives
// (@tiptap/react-menus, extension exports) — no bespoke formatting logic.
// The Markdown file remains the source of truth via tiptap-markdown.

import React from "react";
import { EditorProvider, EditorContent, useCurrentEditor } from "@tiptap/react";
import type { Editor } from "@tiptap/react";
import StarterKit from "@tiptap/starter-kit";
import Link from "@tiptap/extension-link";
import Image from "@tiptap/extension-image";
import Placeholder from "@tiptap/extension-placeholder";
import TaskList from "@tiptap/extension-task-list";
import Focus from "@tiptap/extension-focus";
import Dropcursor from "@tiptap/extension-dropcursor";
import Gapcursor from "@tiptap/extension-gapcursor";
import CodeBlockLowlight from "@tiptap/extension-code-block-lowlight";
import { common, createLowlight } from "lowlight";
import TaskItem from "@tiptap/extension-task-item";
import Highlight from "@tiptap/extension-highlight";
import Underline from "@tiptap/extension-underline";
import CharacterCount from "@tiptap/extension-character-count";
import { Markdown } from "tiptap-markdown";
import { BubbleMenu } from "@tiptap/react/menus";
import {
  Bold, Italic, Strikethrough, Code, Underline as UnderlineIcon, Highlighter,
  List, ListOrdered, Quote, Link2, Minus, Image as ImageIcon,
  CheckSquare,
} from "lucide-react";

export interface WriterProps {
  initialMarkdown: string;
  slug: string;
  onChange: (md: string) => void;
  onReady?: (editor: Editor) => void;
}

// ---- toolbar button built on the editor context ----------------------------

function TB({ label, hint, active, onClick, children }: {
  label: string; hint?: string; active?: boolean;
  onClick: (editor: Editor) => void; children: React.ReactNode;
}) {
  const { editor } = useCurrentEditor();
  return (
    <button
      title={hint ? `${label} (${hint})` : label}
      className={`tool-btn${active ? " active" : ""}`}
      disabled={!editor}
      onMouseDown={(e) => e.preventDefault()}
      onClick={() => editor && onClick(editor)}
    >
      {children}
    </button>
  );
}

const sep = <span className="bubble-sep" />;

// ---- static toolbar --------------------------------------------------------

export function FormattingToolbar({ focusMode, setFocusMode }: { focusMode: boolean; setFocusMode: (v:boolean)=>void }) {
  const { editor } = useCurrentEditor();
  if (!editor) return null;
  const chain = () => editor.chain().focus();
  return (
    <div className="toolbar" role="toolbar" aria-label="Formatting">
      <TB label="Bold" hint="Ctrl+B" active={editor.isActive("bold")}
          onClick={(e) => chain().toggleBold().run()}><Bold size={14} /></TB>
      <TB label="Italic" hint="Ctrl+I" active={editor.isActive("italic")}
          onClick={(e) => chain().toggleItalic().run()}><Italic size={14} /></TB>
      <TB label="Strikethrough" hint="Ctrl+Shift+S" active={editor.isActive("strike")}
          onClick={(e) => chain().toggleStrike().run()}><Strikethrough size={14} /></TB>
      <TB label="Underline" hint="Ctrl+U" active={editor.isActive("underline")}
          onClick={(e) => chain().setUnderline().run()}><UnderlineIcon size={14} /></TB>
      <TB label="Highlight" active={editor.isActive("highlight")}
          onClick={(e) => chain().toggleHighlight().run()}><Highlighter size={14} /></TB>
      <TB label="Code" active={editor.isActive("code")}
          onClick={(e) => chain().toggleCode().run()}><Code size={14} /></TB>
      {sep}
      <TB label="Heading 2" active={editor.isActive("heading", { level: 2 })}
          onClick={(e) => chain().toggleHeading({ level: 2 }).run()}><b>H2</b></TB>
      <TB label="Heading 3" active={editor.isActive("heading", { level: 3 })}
          onClick={(e) => chain().toggleHeading({ level: 3 }).run()}><b>H3</b></TB>
      {sep}
      <TB label="Bullet list" active={editor.isActive("bulletList")}
          onClick={(e) => chain().toggleBulletList().run()}><List size={14} /></TB>
      <TB label="Numbered list" active={editor.isActive("orderedList")}
          onClick={(e) => chain().toggleOrderedList().run()}><ListOrdered size={14} /></TB>
      <TB label="Checklist" active={editor.isActive("taskList")}
          onClick={(e) => chain().toggleTaskList().run()}><CheckSquare size={14} /></TB>
      <TB label="Quote" active={editor.isActive("blockquote")}
          onClick={(e) => chain().toggleBlockquote().run()}><Quote size={14} /></TB>
      {sep}
      <TB label="Link" active={editor.isActive("link")}
          onClick={(e) => {
            const prev = e.getAttributes("link").href ?? "";
            const url = prompt("URL:", prev);
            if (url === null) return;
            url === "" ? e.chain().focus().unsetLink().run()
                       : e.chain().focus().setLink({ href: url }).run();
          }}><Link2 size={14} /></TB>
      <TB label="Image"
          onClick={(e) => {
            const url = prompt("Image URL or media/ path:");
            if (url) e.chain().focus().setImage({ src: url }).run();
          }}><ImageIcon size={14} /></TB>
      <TB label="Divider"
          onClick={(e) => e.chain().focus().setHorizontalRule().run()}><Minus size={14} /></TB>
    </div>
  );
}

// ---- selection bubble ------------------------------------------------------

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
                url === "" ? editor.chain().focus().unsetLink().run()
                           : editor.chain().focus().setLink({ href: url }).run();
              }}><Link2 size={14} /></button>
    </BubbleMenu>
  );
}

// ---- writer ----------------------------------------------------------------

export function Writer({ initialMarkdown, slug, onChange, onReady }: WriterProps) {
  const [focusMode, setFocusMode] = React.useState(false);
  return (
    <EditorProvider
      key={slug}
      immediatelyRender={false}
      extensions={[
        StarterKit.configure({ heading: { levels: [2, 3] } }),
        Link.configure({ openOnClick: false, autolink: true }),
        Image.configure({ inline: false, allowBase64: true }),
        Placeholder.configure({ placeholder: "Start writing…" }),
        TaskList,
        TaskItem.configure({ nested: true }),
        Highlight,
        Underline,
        CodeBlockLowlight.configure({ lowlight: createLowlight(common) }),
        Focus.configure({ className: "has-focus", mode: "shallowest" }),
        Dropcursor,
        Gapcursor,
        CharacterCount.configure({ limit: null }),
        Markdown.configure({
          html: false,
          tightLists: true,
          linkify: true,
          breaks: false,
        }),
      ]}
      content={initialMarkdown}
      onUpdate={({ editor }) => onChange((editor.storage as any).markdown.getMarkdown())}
      onCreate={({ editor }) => onReady?.(editor)}
      editorProps={{ attributes: { class: "writer", spellcheck: "true" } }}
      slotBefore={<FormattingToolbar focusMode={focusMode} setFocusMode={setFocusMode} />}
    >
      <SelectionBubble />
      <CharCount />
    </EditorProvider>
  );
}

function CharCount() {
  const { editor } = useCurrentEditor();
  if (!editor) return null;
  const words = editor.storage.characterCount.words();
  return (
    <div className="charcount" aria-live="polite">
      {words} words{editor.storage.characterCount.characters() ? "" : ""}
    </div>
  );
}
