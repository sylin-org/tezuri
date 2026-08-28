// Cycle B verification: tiptap parse -> serialize round-trip on real
// corpus markdown. Headings must stay headings; galleries stay galleries;
// a second round-trip must be byte-stable.
import { readFileSync } from "node:fs";
import { JSDOM } from "jsdom";

const dom = new JSDOM("<!doctype html><body id='ed'></body>");
globalThis.window = dom.window;
globalThis.document = dom.window.document;
globalThis.DOMParser = dom.window.DOMParser;
Object.defineProperty(globalThis, "navigator", { value: dom.window.navigator, configurable: true });
globalThis.getComputedStyle = dom.window.getComputedStyle;
globalThis.Node = dom.window.Node;
globalThis.NodeFilter = dom.window.NodeFilter;
globalThis.HTMLElement = dom.window.HTMLElement;
globalThis.Element = dom.window.Element;
globalThis.Text = dom.window.Text;

const file = process.argv[2];
const md = readFileSync(file, "utf-8");

const { Editor } = await import("@tiptap/core");
const { default: StarterKit } = await import("@tiptap/starter-kit");
const { default: Link } = await import("@tiptap/extension-link");
const { default: Image } = await import("@tiptap/extension-image");
const InlineImage = Image.configure({ inline: true });
const { default: Underline } = await import("@tiptap/extension-underline");
const { Markdown } = await import("tiptap-markdown");

const editor = new Editor({
  extensions: [
    StarterKit.configure({ heading: { levels: [1, 2, 3] }, link: false, codeBlock: false }),
    Link,
    InlineImage.configure({ allowBase64: false }),
    Underline,
    Markdown.configure({ html: true, tightLists: true, linkify: true, breaks: false }),
  ],
  content: md,
  element: dom.window.document.getElementById("ed"),
});

const r1 = editor.storage.markdown.getMarkdown();
editor.commands.setContent(r1);
const r2 = editor.storage.markdown.getMarkdown();
editor.commands.setContent(r2);
const r3 = editor.storage.markdown.getMarkdown();
editor.commands.setContent(r3);
const r4 = editor.storage.markdown.getMarkdown();

const count = (t, re) => (t.match(re) ?? []).length;
const around = (t, needle) => {
  const i = t.indexOf(needle);
  return i < 0 ? "(absent)" : JSON.stringify(t.slice(Math.max(0, i - 150), i + 200));
};

console.log("SRC around Presets:", around(md, "## Presets"));
console.log("R1  around Presets:", around(r1, "## Presets"));
console.log("R1  around gallery:", around(r1, "![](media/0cf"));
console.log(JSON.stringify({
  file,
  srcLen: md.length, r1Len: r1.length, r2Len: r2.length,
  stable: r1 === r2,
  converged: r2 === r3 && r3 === r4,
  headingsInSrc: count(md, /^## .+$/gm),
  headingsInR1: count(r1, /^## .+$/gm),
  headingsInR2: count(r2, /^## .+$/gm),
}, null, 2));
editor.destroy();
