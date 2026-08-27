//! Article Details: the article's facts, editable in one place — id,
//! state, cover, tags, date, author. Write mode shows content only.

import React from "react";
import { invoke } from "./bridge";

export function ArticleDetails({ slug, id, state, date, tags, cover, author, byline, mediaBase, onChange }: {
  slug: string;
  id: string | null;
  state: string;
  date: string | null;
  tags: string[] | null;
  cover: string | null;
  author: string | null;
  byline: string;
  mediaBase: string;
  onChange: (patch: {
    state?: string;
    date?: string | null;
    tags?: string[];
    cover?: string | null;
    author?: string | null;
  }) => void;
}) {
  const [newTag, setNewTag] = React.useState("");
  const tagsEnabled = true; // vocabulary participation is a Space Details decision

  const pickCover = async () => {
    try {
      const file = await invoke<string | null>("pick_image_file");
      if (!file) return;
      const bytes: number[] = await invoke("read_file_bytes", { path: file });
      const ref = await invoke<string>("add_media", {
        bytes,
        originalName: file.split(/[\\/]/).pop() ?? "cover",
      });
      onChange({ cover: ref });
    } catch (e: any) {
      /* the rail chirps through save status; refusals are logged by the desk */
    }
  };

  const addTag = () => {
    const t = newTag.trim();
    if (t && !(tags ?? []).includes(t)) onChange({ tags: [...(tags ?? []), t] });
    setNewTag("");
  };

  return (
    <div className="config-section article-details">
      <h2>Article Details</h2>

      <div className="field"><span className="mono-fact">id</span>
        <span className="mono-fact">{id ?? "(minted on first save)"}</span>
      </div>
      <div className="field"><span className="mono-fact">slug</span>
        <span className="mono-fact">{slug}</span>
      </div>

      <div className="field">State
        <span className="row">
          <button className={state === "draft" ? "active" : ""}
                  onClick={() => onChange({ state: "draft" })}>Draft</button>
          <button className={state === "published" ? "active" : ""}
                  onClick={() => onChange({ state: "published" })}>Published</button>
        </span>
      </div>

      <label className="field">Publication date
        <input type="date" value={date ?? ""}
               onChange={(e) => onChange({ date: e.target.value || null })} />
      </label>

      <label className="field">Author
        <input value={author ?? ""} placeholder={byline ? `space default: ${byline}` : "space byline"}
               onChange={(e) => onChange({ author: e.target.value || null })} />
        <span className="config-hint">Empty uses the space's author.</span>
      </label>

      {tagsEnabled && (
        <div className="field">Tags
          <div className="tag-vocab">
            {(tags ?? []).map((t) => (
              <span className="tagpill" key={t}>
                #{t}
                <button title={`Remove ${t}`}
                        onClick={() => onChange({ tags: (tags ?? []).filter((x) => x !== t) })}>
                  ×
                </button>
              </span>
            ))}
          </div>
          <span className="row">
            <input placeholder="add a tag" value={newTag}
                   onChange={(e) => setNewTag(e.target.value)}
                   onKeyDown={(e) => { if (e.key === "Enter") addTag(); }} />
            <button onClick={addTag}>Add</button>
          </span>
        </div>
      )}

      <div className="field">Cover
        <span className="row">
          {cover
            ? <img className="cover-thumb" src={mediaBase + cover.replace(/^media\//, "")} alt="cover" />
            : <span className="config-hint">no cover</span>}
          <button onClick={pickCover}>{cover ? "Replace cover" : "Choose cover…"}</button>
          {cover && <button onClick={() => onChange({ cover: null })}>Clear</button>}
        </span>
      </div>
    </div>
  );
}
