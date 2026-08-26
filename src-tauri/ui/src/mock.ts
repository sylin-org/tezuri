// Synthetic bridge for browser-side styling work. See main.tsx for the guard
// that keeps this out of the packaged application. Nothing here touches disk.
type Handler = (args: any) => any;

const article = (slug: string, title: string, state: string, words: number) => ({
  slug, title, state, words, links: [], dangling_links: [],
});

const desk = {
  entries: [
    article("night-garden", "The Night Garden", "published", 4123),
    article("on-rust", "On Rust", "draft", 876),
    article("local-press", "A Local Press", "review", 1540),
  ],
  inbound: {},
};

const raw = "# On Rust\n\n_A meditation on ownership._\n\nHello world.\n\nSecond paragraph.\n";

const handlers: Record<string, Handler> = {
  registry_load: () => ({ publications: [
    { name: "Kintsugi", persona: "L. Botinelly", root: "C:/writing/kintsugi" },
    { name: "Field Notes", persona: "Anonymous", root: "C:/writing/field-notes" },
  ] }),
  get_last_opened: () => "C:/writing/kintsugi",
  set_last_opened: () => null,
  open_publication: () => ({ path: "C:/writing/kintsugi", articles: 3, words: 6539 }),
  read_identity: (args: any) =>
    String(args?.path ?? "").includes("field")
      ? { name: "Field Notes", byline: "written afield", persona: "Anonymous" }
      : { name: "Kintsugi", byline: "on repair and reassembly", persona: "L. Botinelly" },
  save_identity: () => null,
  app_version: () => "0.1.0",
  desk: () => desk,
  list_assistants: () => ["codex", "claude"],
  read_article: () => ({
    article: { meta: { slug: "on-rust", state: "draft", date: "2026-08-26", tags: ["rust"], cover: null } },
    raw,
  }),
  save_article: () => "deadbeef",
  create_article: () => null,
  review_changes: () => [
    { status: "M", path: "articles/on-rust/article.md" },
    { status: "A", path: "media/0198c7a2-morning-light.png" },
  ],
  remote_head: () => null,
  prove: () => ({ verdict: "passed", evidence: "vite v6.4.3 building for production...\n✓ built in 6.86s" }),
  consult_recipe: () => ({ recipe: "polish", assistant: "codex", output: "Verdict: two sentences tighten well.\n\n1. ..." }),
  read_theme: () => "",
};

export function installMock() {
  (window as any).__TAURI__ = {
    core: {
      invoke: (cmd: string, args?: any) => {
        const h = handlers[cmd];
        if (!h) return Promise.reject(new Error(`mock: no handler for ${cmd}`));
        return Promise.resolve(h(args));
      },
    },
  };
}
