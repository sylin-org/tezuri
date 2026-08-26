// The typed seam between the interface and the desktop shell. The frontend
// reaches native power only through the named product operations gathered
// here — never through window.__TAURI__ elsewhere, and never through any
// generic filesystem, shell, process, or network authority.

export interface PublicationInfo {
  path: string;
  articles: number;
  words: number;
}

export interface Identity {
  name: string;
  byline: string;
  persona: string;
  [key: string]: unknown;
}

export interface DeskEntry {
  slug: string;
  title: string;
  state: "draft" | "review" | "published";
  date: string | null;
  words: number;
  links: string[];
  dangling_links: string[];
}

export interface Desk {
  entries: DeskEntry[];
  inbound: Record<string, number>;
}

export interface Change {
  status: string;
  path: string;
}

export interface LoadedArticle {
  article: {
    meta: {
      slug: string;
      state: string;
      date: string | null;
      tags: string[] | null;
      cover: string | null;
    };
  };
  raw: string;
}

export interface AdviceResult {
  recipe: string;
  assistant: string;
  output: string;
}

export interface Assistant {
  id: string;
  command: string;
  args: string[];
  note: string | null;
  default: boolean;
}

export interface ProofResult {
  verdict: string;
  evidence: string;
}

/** Invoke one named product operation. Throws when the bridge is absent. */
export function invoke<T>(cmd: string, args?: Record<string, unknown>): Promise<T> {
  const t = (window as any).__TAURI__;
  if (!t?.core?.invoke) throw new Error("Tauri bridge not ready");
  return t.core.invoke(cmd, args) as Promise<T>;
}
