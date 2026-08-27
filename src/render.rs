//! Render: the article compiler.
//!
//! Markdown flow + meta + the space's `theme.css` + a layout template become
//! complete, self-contained HTML pages written under `render/` inside the
//! publication. Deterministic Rust-side compilation (pulldown-cmark) so the
//! CLI, the tests, and the app's preview surface all produce byte-identical
//! artifacts: the preview IS the final result. A publication may override the
//! layout with `templates/article.html` / `templates/index.html` — plain HTML
//! with a small `{{placeholder}}` contract; embedded defaults ship in the
//! binary and nothing is ever fetched.

use crate::articles::Article;
use crate::spine::{atomic_write, confine, Event, Journal};
use crate::theme;
use anyhow::Result;
use pulldown_cmark::{html, Options, Parser};
use std::path::Path;

/// Emitted pages live here, inside the publication. Flat: `render/<slug>.html`
/// next to `render/index.html`, so relative media references reach the
/// publication's own `media/` with one `../`.
pub const RENDER_DIR: &str = "render";

const ARTICLE_TEMPLATE: &str = include_str!("templates/article.html");
const INDEX_TEMPLATE: &str = include_str!("templates/index.html");

// ---------------------------------------------------------------------------
// Markdown → body HTML, with TOC and galleries
// ---------------------------------------------------------------------------

struct Heading {
    level: u8,
    text: String,
    id: String,
}

/// Compile the article's body Markdown. The document's H1 and standfirst line
/// belong to the page frame, not the body, so they are stripped here.
fn compile_body(document: &str) -> (String, Vec<Heading>) {
    let body_md = strip_frame(document);
    let mut opts = Options::empty();
    opts.insert(Options::ENABLE_TABLES);
    opts.insert(Options::ENABLE_STRIKETHROUGH);
    let mut html = String::new();
    html::push_html(&mut html, Parser::new_ext(body_md, opts));

    let html = wrap_galleries(&html);
    let (html, headings) = tag_headings(&html);
    let html = rewrite_paths(&html);
    (html, headings)
}

/// Drop the leading H1 and optional `_standfirst_` line, keeping the body.
fn strip_frame(document: &str) -> &str {
    let mut rest = document.trim_start();
    if let Some(after) = rest.strip_prefix("# ") {
        rest = after.split_once('\n').map(|(_, r)| r).unwrap_or("");
        rest = rest.trim_start();
        // Optional standfirst line.
        if rest.starts_with('_') {
            if let Some(after) = rest.split_once('\n') {
                rest = after.1;
            }
        }
    }
    rest.trim_start()
}

/// Runs of two or more consecutive image-only paragraphs become a gallery —
/// and so does a single paragraph holding two or more images (markdown keeps
/// adjacent image lines in one paragraph, split by soft breaks).
fn wrap_galleries(html: &str) -> String {
    let imgs_of = |p: &str| -> Option<Vec<String>> {
        let inner = p.strip_prefix("<p>")?.strip_suffix("</p>\n")?;
        if inner.contains("</") {
            return None; // any real closing tag means mixed content
        }
        let imgs: Vec<String> = inner
            .split("<img")
            .skip(1)
            .map(|frag| format!("<img{}", frag.trim_end()))
            .collect();
        (imgs.len() >= 2).then_some(imgs)
    };
    let mut out = String::with_capacity(html.len());
    let mut run: Vec<String> = Vec::new();
    let paras: Vec<&str> = html.split_inclusive("</p>\n").collect();
    for para in paras {
        match imgs_of(para) {
            Some(imgs) => run.extend(imgs),
            None => {
                flush_gallery(&mut out, &mut run);
                out.push_str(para);
            }
        }
    }
    flush_gallery(&mut out, &mut run);
    out
}

fn flush_gallery(out: &mut String, run: &mut Vec<String>) {
    if run.len() >= 2 {
        out.push_str("<div class=\"gallery\">");
        for img in run.drain(..) {
            out.push_str(&img);
        }
        out.push_str("</div>\n");
    } else {
        for img in run.drain(..) {
            out.push_str("<p>");
            out.push_str(&img);
            out.push_str("</p>\n");
        }
    }
}

/// Give every h2/h3 a stable id and collect them for the TOC.
fn tag_headings(html: &str) -> (String, Vec<Heading>) {
    let mut out = String::with_capacity(html.len());
    let mut headings = Vec::new();
    let mut rest = html;
    while let Some(pos) = rest.find("<h2>").or_else(|| rest.find("<h3>")) {
        let level: u8 = if rest[pos..].starts_with("<h2>") {
            2
        } else {
            3
        };
        let close = format!("</h{level}>");
        let after_tag = pos + 4; // "<h2>" and "<h3>" are both 4 chars
        let end = rest[after_tag..].find(&close).map(|e| after_tag + e);
        let Some(end) = end else { break };
        let text = strip_tags(&rest[after_tag..end]);
        let n = headings.len() + 1;
        let id = format!("sec-{n}-{}", slug_of(&text));
        out.push_str(&rest[..pos]);
        out.push_str(&format!("<h{level} id=\"{id}\">"));
        out.push_str(&rest[after_tag..end]);
        out.push_str(&close);
        headings.push(Heading { level, text, id });
        rest = &rest[end + close.len()..];
    }
    out.push_str(rest);
    (out, headings)
}

fn strip_tags(fragment: &str) -> String {
    let mut s = String::new();
    let mut in_tag = false;
    for c in fragment.chars() {
        match c {
            '<' => in_tag = true,
            '>' => in_tag = false,
            c if !in_tag => s.push(c),
            _ => {}
        }
    }
    s.trim().to_string()
}

fn slug_of(text: &str) -> String {
    let mut s = String::new();
    for c in text.chars() {
        if c.is_ascii_alphanumeric() {
            s.push(c.to_ascii_lowercase());
        } else if (c == ' ' || c == '-' || c == '_') && !s.ends_with('-') && !s.is_empty() {
            s.push('-');
        }
    }
    s.trim_matches('-').chars().take(40).collect()
}

/// Rewrite publication-relative references to emitted ones: images and links
/// pointing at `media/` gain the `../` hop out of `render/`; article links
/// become sibling pages.
fn rewrite_paths(html: &str) -> String {
    html.replace("src=\"media/", "src=\"../media/")
        .replace("href=\"media/", "href=\"../media/")
        .replace("href=\"articles/", "href=\"")
        .replace(".md\"", ".html\"")
}

fn toc_html(headings: &[Heading]) -> String {
    if headings.is_empty() {
        return String::new();
    }
    let mut out = String::from("<nav class=\"toc\">");
    for h in headings {
        let cls = if h.level == 3 { " class=\"l3\"" } else { "" };
        out.push_str(&format!(
            "<a href=\"#{id}\"{cls}>{text}</a>",
            id = h.id,
            text = esc(&h.text)
        ));
    }
    out.push_str("</nav>");
    out
}

fn esc(s: &str) -> String {
    s.replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
}

// ---------------------------------------------------------------------------
// Page assembly
// ---------------------------------------------------------------------------

fn read_time(words: usize) -> usize {
    (words / 220).max(1)
}

fn load_template(publication_root: &Path, name: &str, fallback: &'static str) -> Result<String> {
    let rel = Path::new("templates").join(name);
    let p = confine(publication_root, &rel)?;
    if p.exists() {
        Ok(std::fs::read_to_string(&p)?)
    } else {
        Ok(fallback.to_string())
    }
}

fn subst(template: &str, pairs: &[(&str, String)]) -> String {
    let mut out = template.to_string();
    for (k, v) in pairs {
        out = out.replace(&format!("{{{{{k}}}}}"), v);
    }
    out
}

/// Compile one article into a complete, self-contained page.
pub fn render_article(publication_root: &Path, slug: &str) -> Result<String> {
    let a = Article::load(publication_root, slug)?;
    let (body, headings) = compile_body(&a.document);
    let title = a.title();
    let standfirst = a.standfirst();
    let words = a.word_count();

    let identity = crate::identity::Identity::load(publication_root)?;
    let site_name = if identity.name.is_empty() {
        publication_root
            .file_name()
            .and_then(|s| s.to_str())
            .unwrap_or("A space")
            .to_string()
    } else {
        identity.name.clone()
    };
    let byline = if identity.byline.is_empty() {
        identity.persona.clone()
    } else {
        identity.byline.clone()
    };

    let css = theme::read(publication_root).unwrap_or_default();
    let tags_inline = if a.meta.tags.is_empty() {
        String::new()
    } else {
        let pills: Vec<String> = a
            .meta
            .tags
            .iter()
            .map(|t| format!("<span class=\"tagpill\">#{}</span>", esc(t)))
            .collect();
        format!("<span class=\"dot\">·</span> {}", pills.join(" "))
    };
    let tags_block = a
        .meta
        .tags
        .iter()
        .map(|t| format!("<span class=\"tagpill\">#{}</span>", esc(t)))
        .collect::<Vec<_>>()
        .join(" ");

    let cover_section = match &a.meta.cover {
        Some(c) if !c.is_empty() => format!(
            "<div class=\"hero\" style=\"background-image:url('../{c}')\"></div>",
            c = esc(c)
        ),
        _ => String::new(),
    };

    let tpl = load_template(publication_root, "article.html", ARTICLE_TEMPLATE)?;
    Ok(subst(
        &tpl,
        &[
            ("title", esc(&title)),
            ("site_name", esc(&site_name)),
            ("byline", esc(&byline)),
            (
                "standfirst_line",
                match &standfirst {
                    Some(sf) => format!("<p class=\"standfirst\">{}</p>", esc(sf)),
                    None => String::new(),
                },
            ),
            ("date", a.meta.date.clone().unwrap_or_default()),
            ("read_time", read_time(words).to_string()),
            ("tags_inline", tags_inline),
            ("tags_block", tags_block),
            ("body", body),
            ("toc", toc_html(&headings)),
            ("css", css),
            ("cover_section", cover_section),
        ],
    ))
}

/// Compile every article and write `render/<slug>.html` + `render/index.html`.
/// Idempotent: current pages are written; v1 never deletes files it does not
/// recognize. Returns the written paths (publication-relative).
pub fn emit_render(publication_root: &Path) -> Result<Vec<String>> {
    let desk = crate::desk::Desk::rebuild(publication_root)?;
    let mut written = Vec::new();

    let mut index_rows = String::new();
    for e in &desk.entries {
        let page = render_article(publication_root, &e.slug)?;
        let rel = format!("{RENDER_DIR}/{}.html", e.slug);
        let target = confine(publication_root, Path::new(&rel))?;
        atomic_write(&target, page.as_bytes())?;
        written.push(rel.clone());
        index_rows.push_str(&format!(
            "<div class=\"entry\"><a href=\"{slug}.html\">{title}</a><br>\
             <span class=\"meta\">{date} · {words} words</span></div>\n",
            slug = e.slug,
            title = esc(&e.title),
            date = e.date.as_deref().unwrap_or("undated"),
            words = e.words,
        ));
    }

    let identity = crate::identity::Identity::load(publication_root)?;
    let site_name = if identity.name.is_empty() {
        publication_root
            .file_name()
            .and_then(|s| s.to_str())
            .unwrap_or("A space")
            .to_string()
    } else {
        identity.name.clone()
    };
    let index = subst(
        &load_template(publication_root, "index.html", INDEX_TEMPLATE)?,
        &[("site_name", esc(&site_name)), ("entries", index_rows)],
    );
    let index_rel = format!("{RENDER_DIR}/index.html");
    atomic_write(
        &confine(publication_root, Path::new(&index_rel))?,
        index.as_bytes(),
    )?;
    written.push(index_rel);

    Journal::open(publication_root)?.record(Event::Rendered {
        pages: written.len(),
    })?;
    Ok(written)
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::tempdir;

    const DOC: &str = "# On Rust\n\n_A meditation on ownership._\n\n## First part\n\nHello \
                       world.\n\n## Second part\n\nMore words.\n\n### Deep\n\nDeeper.\n";

    #[test]
    fn heading_ids_land_cleanly() {
        let (body, _) = compile_body(DOC);
        // The full open-tag shape, text with no prefix garbage.
        assert!(body.contains("<h2 id=\"sec-1-first-part\">First part</h2>"));
        assert!(body.contains("<h3 id=\"sec-3-deep\">Deep</h3>"));
        assert!(!body.contains(">>"), "no doubled angle brackets");
    }

    #[test]
    fn strips_frame_and_compiles_toc() {
        let (body, headings) = compile_body(DOC);
        assert!(!body.contains("<h1>"), "the frame's H1 must not double up");
        assert!(body.contains("<h2 id=\"sec-1-first-part\">"));
        assert!(body.contains("id=\"sec-3-deep\""));
        assert_eq!(headings.len(), 3);
        assert_eq!(headings[0].text, "First part");
    }

    #[test]
    fn galleries_wrap_adjacent_images() {
        let md = "# T\n\n![a](media/a.png)\n![b](media/b.png)\n\nSolo:\n\n![c](media/c.png)\n";
        let (body, _) = compile_body(md);
        assert!(body.contains("<div class=\"gallery\">"));
        assert!(body.contains("src=\"../media/a.png\""));
        // The lone image stays a plain paragraph.
        let gallery_end = body.find("</div>").unwrap();
        assert!(body[gallery_end..].contains("<p><img"));
    }

    #[test]
    fn article_links_become_sibling_pages() {
        let (body, _) = compile_body("T\n\nsee [that](articles/other.md)\n");
        assert!(body.contains("href=\"other.html\""));
    }

    #[test]
    fn renders_complete_page_from_template() {
        let dir = tempdir().unwrap();
        Article::create(dir.path(), "on-rust", "On Rust").unwrap();
        let mut a = Article::load(dir.path(), "on-rust").unwrap();
        a.document = DOC.into();
        a.meta.tags = vec!["rust".into()];
        a.save(dir.path()).unwrap();

        let page = render_article(dir.path(), "on-rust").unwrap();
        assert!(page.contains("<h1 class=\"art-title\">On Rust</h1>"));
        assert!(page.contains("A meditation on ownership."));
        assert!(page.contains("href=\"#sec-1-first-part\""));
        assert!(page.contains("#rust"));
        assert!(page.contains("IN THIS ARTICLE"));
    }

    #[test]
    fn emit_writes_pages_and_index_and_journals() {
        let dir = tempdir().unwrap();
        Article::create(dir.path(), "alpha", "Alpha").unwrap();
        Article::create(dir.path(), "beta", "Beta").unwrap();

        let written = emit_render(dir.path()).unwrap();
        assert_eq!(written.len(), 3);
        assert!(dir.path().join("render/alpha.html").exists());
        assert!(dir.path().join("render/beta.html").exists());
        let index = std::fs::read_to_string(dir.path().join("render/index.html")).unwrap();
        assert!(index.contains("href=\"alpha.html\""));
        assert!(index.contains("href=\"beta.html\""));

        let events = crate::spine::Journal::open(dir.path())
            .unwrap()
            .events()
            .unwrap();
        assert!(events.iter().any(|(_, e)| e.kind() == "rendered"));
    }

    #[test]
    fn publication_template_overrides_the_embedded_default() {
        let dir = tempdir().unwrap();
        Article::create(dir.path(), "gamma", "Gamma").unwrap();
        let tpl_dir = dir.path().join("templates");
        std::fs::create_dir_all(&tpl_dir).unwrap();
        std::fs::write(tpl_dir.join("article.html"), "<html>{{title}}</html>").unwrap();

        let page = render_article(dir.path(), "gamma").unwrap();
        assert_eq!(page, "<html>Gamma</html>");
    }
}
