//! WritePane: the iframe hosting the artifact page with the editor runtime
//! injected. The artifact's own cascade and layout render untouched; the
//! runtime mounts the caret inside `.article-prose` and round-trips
//! markdown with this host over postMessage.

import React from "react";
import { invoke } from "./bridge";

export function WritePane({ slug, template, markdown, canonical, mediaBase, resetToken, catalog, onConduct, onMarkdown, onRefusal }: {
  slug: string;
  /** The working template (space file or conduct draft). */
  template: string | null;
  markdown: string;
  /** The canonical article.md — the baseline the unsaved wash diffs against. */
  canonical: string;
  mediaBase: string;
  /** Bumped when the host discards: refetch the page from canonical. */
  resetToken: number;
  /** The characterized slot catalog, for the in-frame conduct menus. */
  catalog: any[];
  /** The frame picked an option for one slot occurrence. */
  onConduct: (raw: string, occurrence: number, current: string[], optKey: string, value: string) => void;
  /** Content changed inside the frame; the host autosaves. */
  onMarkdown: (md: string) => void;
  /** The desk refused an image (format, size); the message is user-phrased. */
  onRefusal: (message: string) => void;
}) {
  const [page, setPage] = React.useState<string | null>(null);
  const frameRef = React.useRef<HTMLIFrameElement | null>(null);
  const bootedRef = React.useRef(false);
  // Latest values for the message handler without re-binding it mid-typing.
  const latest = React.useRef({ markdown, canonical, mediaBase, slug, template });
  latest.current = { markdown, canonical, mediaBase, slug, template };
  const conductRef = React.useRef(onConduct);
  conductRef.current = onConduct;
  const refusalRef = React.useRef(onRefusal);
  refusalRef.current = onRefusal;

  const reload = React.useCallback(async () => {
    try {
      const tpl = template ?? (await invoke<string>("default_template"));
      const page = await invoke<string>("write_page_draft", { slug, template: tpl });
      setPage(page);
    } catch {
      setPage(null);
    }
  }, [slug, template]);

  React.useEffect(() => {
    bootedRef.current = false;
    void reload();
  }, [reload]);

  // A discard re-fetches the page: the dirty copy is gone, the canonical
  // file speaks again, and the editor boots fresh from it.
  const first = React.useRef(true);
  React.useEffect(() => {
    if (first.current) { first.current = false; return; }
    bootedRef.current = false;
    void reload();
  }, [resetToken]);

  React.useEffect(() => {
    const sendInit = () => {
      frameRef.current?.contentWindow?.postMessage(
        {
          type: "tz-init",
          markdown: latest.current.markdown,
          mediaBase: latest.current.mediaBase,
        },
        "*",
      );
    };
    const handler = (event: MessageEvent) => {
      const frame = frameRef.current;
      if (!frame || event.source !== frame.contentWindow) return;
      const msg: any = event.data ?? {};
      if (msg.type === "tz-booted") {
        // The editor acknowledged the init: stop retrying.
        bootedRef.current = true;
      } else if (msg.type === "tz-ready" || msg.type === "tz-booted") {
        sendInit();
      } else if (msg.type === "tz-change") {
        // Echo guard: a round-trip normalization is not an edit.
        const md = String(msg.markdown ?? "");
        if (md === latest.current.markdown) return;
        onMarkdown(md);
      } else if (msg.type === "tz-conduct") {
        conductRef.current(
          String(msg.raw ?? ""),
          Number(msg.occurrence ?? 0),
          String(msg.current ?? "").split(",").map((x: string) => x.trim()).filter(Boolean),
          String(msg.optKey ?? ""),
          String(msg.value ?? ""),
        );
      } else if (msg.type === "tz-image") {
        (async () => {
          try {
            // The store sniffs the real format from the bytes (PNG, JPEG,
            // GIF, WebP) and dedups by content — no transcoding here, so
            // animated GIFs stay animated.
            const bin = atob(String(msg.base64));
            const bytes = new Uint8Array(bin.length);
            for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
            const ref = await invoke<string>("add_media", {
              bytes: Array.from(bytes),
              originalName: String(msg.name ?? "pasted"),
            });
            frame.contentWindow?.postMessage(
              { type: "tz-media", token: msg.token, ref },
              "*",
            );
          } catch (e: any) {
            refusalRef.current(e?.message ?? String(e));
          }
        })();
      }
    };
    const sendInitUntilBooted = () => {
      sendInit();
      let tries = 0;
      const t = window.setInterval(() => {
        tries += 1;
        if (bootedRef.current || tries >= 12) {
          window.clearInterval(t);
        } else {
          sendInit();
        }
      }, 350);
      window.setTimeout(() => window.clearInterval(t), 6000);
    };
    sendInitUntilBooted();
    window.addEventListener("message", handler);
    return () => window.removeEventListener("message", handler);
  }, [onMarkdown]);

  return (
    <div className="write-frame-host">
      {page !== null ? (
        <iframe
          ref={frameRef}
          className="write-frame"
          title="Write — the article as readers will see it"
          srcDoc={page}
          sandbox="allow-same-origin allow-scripts"
        />
      ) : (
        <p className="mono-fact">composing…</p>
      )}
    </div>
  );
}
