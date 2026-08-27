//! WritePane: the iframe hosting the artifact page with the editor runtime
//! injected. The artifact's own cascade and layout render untouched; the
//! runtime mounts the caret inside `.article-prose` and round-trips
//! markdown with this host over postMessage.

import React from "react";
import { invoke } from "./bridge";

export function WritePane({ slug, template, markdown, mediaBase, onMarkdown }: {
  slug: string;
  /** The working template (space file or conduct draft). */
  template: string | null;
  markdown: string;
  mediaBase: string;
  /** Content changed inside the frame; the host autosaves. */
  onMarkdown: (md: string) => void;
}) {
  const [page, setPage] = React.useState<string | null>(null);
  const frameRef = React.useRef<HTMLIFrameElement | null>(null);
  // Latest values for the message handler without re-binding it mid-typing.
  const latest = React.useRef({ markdown, mediaBase, slug, template });
  latest.current = { markdown, mediaBase, slug, template };

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
    void reload();
  }, [reload]);

  React.useEffect(() => {
    const handler = (event: MessageEvent) => {
      const frame = frameRef.current;
      if (!frame || event.source !== frame.contentWindow) return;
      const msg: any = event.data ?? {};
      if (msg.type === "tz-ready") {
        frame.contentWindow?.postMessage(
          {
            type: "tz-init",
            markdown: latest.current.markdown,
            mediaBase: latest.current.mediaBase,
          },
          "*",
        );
      } else if (msg.type === "tz-change") {
        onMarkdown(String(msg.markdown ?? ""));
      } else if (msg.type === "tz-image") {
        (async () => {
          try {
            const ref = await invoke<string>("add_media_from_base64", {
              base64: String(msg.base64),
              originalName: String(msg.name ?? "pasted"),
            });
            frame.contentWindow?.postMessage(
              { type: "tz-media", token: msg.token, ref },
              "*",
            );
          } catch (e: any) {
            // A refused image simply never inserts; the refusal is logged
            // by the desk's media machinery.
          }
        })();
      }
    };
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
