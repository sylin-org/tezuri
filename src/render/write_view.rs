//!  The Write-mode composition: template order, editable boundaries.
use super::*;
use crate::slots;
use crate::spine::confine;
use anyhow::Result;
use std::path::Path;
#[derive(Debug, Clone, serde::Serialize)]
#[serde(tag = "kind", rename_all = "snake_case")]
pub enum Seg {
    /// A literal run of the template, shown as inert content.
    Text {
        html: String,
    },
    /// The article flow. `frame` carries declared presentation around it
    /// (title-banner modes); `prose` is what the editor mounts at. Raw and
    /// hints ride along so conduct can re-splice the expression in place.
    ArticleFlow {
        mirror: bool,
        frame: String,
        prose: String,
        raw: String,
        hints: Vec<String>,
    },
    Slot(SlotInstance),
}

#[derive(Debug, Clone, serde::Serialize)]
pub struct SlotInstance {
    pub name: String,
    pub raw: String,
    pub hints: Vec<String>,
    /// Current projection for non-editable slots (safe HTML from evaluation).
    pub html: String,
    /// True when a real inline editor replaces the static projection.
    pub editable: bool,
    /// The second occurrence onward of the same slot mirrors the first.
    pub mirror: bool,
}

/// The Write-mode page: template order preserved, {{ARTICLE}} marked.
#[derive(Debug, serde::Serialize)]
pub struct WriteCompose {
    pub slug: String,
    pub segments: Vec<Seg>,
    pub notes: Vec<String>,
    /// Template came from templates/article.html rather than the built-in.
    pub space_template: bool,
    /// The artifact's head dress: font imports and the template's own style
    /// blocks. The desk scopes this to the Write plane so Write wears what
    /// the artifact wears.
    pub css: String,
}

/// Compose the Write-mode view of one article: template segments in order,
/// every slot carrying its live projection. Whispers ride along as notes;
/// the page never breaks here either.
///
/// Mechanism: the page is composed once with unique invisible sentinels at
/// every editable boundary, then the document shell (head, styles, script,
/// pre-body noise) is sliced away from the full result. Whatever the author
/// wrote between slots survives byte-honest, whatever brace or shell trick
/// the template holds cannot misplace a segment.
pub fn compose_write_view(publication_root: &Path, slug: &str) -> Result<WriteCompose> {
    let tpl_raw = load_template(publication_root, "article.html", ARTICLE_TEMPLATE)?;
    compose_write_view_with(publication_root, slug, &tpl_raw)
}

/// Compose the Write-mode view against a *draft* template rather than the
/// space's file: conduct happens here first, bytes only move on acceptance.
pub fn compose_write_view_with(
    publication_root: &Path,
    slug: &str,
    template: &str,
) -> Result<WriteCompose> {
    let ctx = gather_article_ctx(publication_root, slug)?;
    let space_template = confine(publication_root, Path::new("templates/article.html"))?.exists();
    let parts = slots::parse_template(template);
    let css = head_dress(template, publication_root)?;

    let (page, notes, toks) = slots::compose_marked(&parts, &ctx);
    let body = carve_to_body(&page);

    let mut segments: Vec<Seg> = Vec::new();
    let mut seen_counts: std::collections::BTreeMap<String, usize> = Default::default();
    let mut saw_article = false;
    let mut rest = body.as_str();

    while let Some(open_at) = rest.find(slots::MARK_OPEN) {
        let Some(close_rel) = rest[open_at..].find(slots::MARK_CLOSE) else {
            break; // stray open: the tail is inert text
        };
        let token_end_rel = open_at + close_rel + slots::MARK_CLOSE_LEN;
        let inner = &rest[open_at + slots::MARK_OPEN.len()..open_at + close_rel];
        if !rest[..open_at].is_empty() {
            segments.push(Seg::Text {
                html: rest[..open_at].to_string(),
            });
        }
        match parse_token(inner) {
            Token::Article(idx) => {
                let (html, raw, hints) = match toks.get(idx) {
                    Some(t) => (t.html.clone(), t.raw.clone(), t.hints.clone()),
                    None => (String::new(), String::new(), vec![]),
                };
                segments.push(split_article_flow(html, saw_article, raw, hints));
                saw_article = true;
            }
            Token::Slot(idx) => {
                if let Some(tok) = toks.get(idx).cloned() {
                    let mirror = *seen_counts.entry(tok.name.clone()).or_default() > 0;
                    *seen_counts.entry(tok.name.clone()).or_default() += 1;
                    let editable = matches!(tok.name.as_str(), "date" | "tags" | "cover_img");
                    segments.push(Seg::Slot(SlotInstance {
                        name: tok.name,
                        raw: tok.raw,
                        hints: tok.hints,
                        html: tok.html,
                        editable: !mirror && editable,
                        mirror,
                    }));
                }
            }
            Token::Junk => {} // never surface malformed markers as text
        }
        rest = &rest[token_end_rel..];
    }
    if !rest.is_empty() {
        segments.push(Seg::Text {
            html: rest.to_string(),
        });
    }
    Ok(WriteCompose {
        slug: slug.to_string(),
        segments,
        notes,
        space_template,
        css,
    })
}

/// The artifact's head dress, for the Write plane to wear: font imports
/// from `<link rel=stylesheet>` targets (the author's own template chose
/// them), then the artifact's cascade — calm baseline first, the space's
/// theme.css, and the template's own `<style>` blocks last, so the same
/// rules win here as on the emitted page.
pub(crate) fn head_dress(template: &str, publication_root: &Path) -> Result<String> {
    let mut imports = String::new();
    let mut styles = String::new();

    // Stylesheet links and style blocks from the whole template: the shell
    // carve ignores heads, but the dress is exactly what heads are for.
    let mut rest = template;
    while let Some(p) = rest.find("<link") {
        rest = &rest[p..];
        let Some(tag_end) = rest.find('>') else { break };
        let tag = &rest[..tag_end];
        if tag.contains("stylesheet") {
            if let Some(h0) = tag.find("href=\"") {
                let href = &tag[h0 + 6..];
                let href = &href[..href.find('"').unwrap_or(0)];
                imports.push_str(&format!("@import url('{href}');\n", href = href));
            }
        }
        rest = &rest[tag_end + 1..];
    }
    for block in extract_blocks(template, "<style", "</style>") {
        styles.push_str(&block);
        styles.push('\n');
    }

    let theme = crate::render::read_theme(publication_root).unwrap_or_default();
    let mut css = String::new();
    if !imports.is_empty() {
        css.push_str(&imports);
    }
    css.push_str(BASELINE_CSS);
    if !theme.is_empty() {
        css.push_str(&theme);
        css.push('\n');
    }
    if !styles.is_empty() {
        css.push_str(&styles);
    }
    Ok(css)
}

/// Complete `<style>…</style>` bodies from a document, tolerating missing
/// closers by dropping the unterminated tail (whisper, never break).
pub(crate) fn extract_blocks(doc: &str, open: &str, close: &str) -> Vec<String> {
    let mut out = Vec::new();
    let mut rest = doc;
    while let Some(p) = rest.find(open) {
        let after_open = p + open.len();
        let Some(gt_rel) = rest[after_open..].find('>') else {
            break;
        };
        let start = after_open + gt_rel + 1;
        let Some(c) = rest[start..].find(close) else {
            break;
        };
        out.push(rest[start..start + c].to_string());
        rest = &rest[start + c + close.len()..];
    }
    out
}

/// Split one ARTICLE token's evaluated HTML into its declared frame
/// (title-banner modes) and the bare prose wrapper the editor mounts at.
/// Plain mode has no frame; bytes never disappear in either shape.
pub(crate) fn split_article_flow(
    html: String,
    mirror: bool,
    raw: String,
    hints: Vec<String>,
) -> Seg {
    const WRAP: &str = "<div class=\"article-prose\">";
    match html.find(WRAP) {
        Some(p) => {
            let prose_start = p + WRAP.len();
            let prose_end = html.rfind("</div>").filter(|e| *e >= prose_start);
            let (frame, prose) = match prose_end {
                Some(e) => (html[..p].to_string(), html[prose_start..e].to_string()),
                None => (String::new(), html.clone()),
            };
            Seg::ArticleFlow {
                mirror,
                frame,
                prose,
                raw,
                hints,
            }
        }
        None => Seg::ArticleFlow {
            mirror,
            frame: String::new(),
            prose: html,
            raw,
            hints,
        },
    }
}

#[derive(Debug, Clone)]
pub(crate) enum Token {
    Article(usize),
    Slot(usize),
    Junk,
}

pub(crate) fn parse_token(inner: &str) -> Token {
    let parse_num = |s: &str| s.parse::<usize>().ok();
    if let Some(n) = inner.strip_prefix('A') {
        return parse_num(n).map(Token::Article).unwrap_or(Token::Junk);
    }
    if let Some(n) = inner.strip_prefix('S') {
        return parse_num(n).map(Token::Slot).unwrap_or(Token::Junk);
    }
    Token::Junk
}

/// The written body region of a composed page: after the <body …> opener's
/// `>`, before `</body>`; whole-document fallbacks for templates that skip
/// conventional shells.
pub(crate) fn carve_to_body(page: &str) -> String {
    let start = page
        .find("<body")
        .and_then(|b| page[b..].find('>').map(|g| b + g + 1))
        .or_else(|| page.find("</head>").map(|h| h + "</head>".len()))
        .unwrap_or(0);
    let end = page.rfind("</body>").unwrap_or(page.len());
    end.checked_sub(start)
        .map(|n| strip_blocks(&page[start..start + n]))
        .unwrap_or_default()
}

/// Remove complete style/script blocks from a run; Tezuri owns behaviors and
/// themes ride globally on the desk, so neither belongs in Write mode.
pub(crate) fn strip_blocks(s: &str) -> String {
    let mut out = String::with_capacity(s.len());
    let mut rest = s;
    while let Some(p) = rest.find("<script").or_else(|| rest.find("<style")) {
        let tag_len = 7; // "<script" and "<style" both span 7 bytes
        let close_tag = if rest[p..].starts_with("<script") {
            "</script>"
        } else {
            "</style>"
        };
        out.push_str(&rest[..p]);
        match rest[p + tag_len..].find(close_tag) {
            Some(c) => rest = &rest[p + tag_len + c + close_tag.len()..],
            None => return out, // unterminated block: drop the remainder
        }
    }
    out.push_str(rest);
    out
}
