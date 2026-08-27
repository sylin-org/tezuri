//! Render: the article compiler.
//!
//! One pipeline serves everything the ADR names: Gather (article, identity,
//! desk, neighbors) → evaluate context against the slot registry
//! (`slots::compose`) → substitute into the template → apply Tezuri's theme
//! and behaviors. Templates are the space's own files under `templates/`;
//! the embedded defaults are deliberately dumb — `{{ARTICLE}}` over a calm
//! baseline — and gorgeousness arrives as starter packs owned by the space.
//!
//! Deterministic Rust-side compilation (pulldown-cmark) keeps the CLI, the
//! tests, and the preview byte-identical: the preview IS the final result.
//! Nothing is ever fetched.

use crate::articles::{Article, State};
use crate::slots::{self, Ctx, Output};
use crate::spine::{atomic_write, confine, Event, Journal};
use anyhow::Result;
use pulldown_cmark::{html, Options, Parser};
use std::path::Path;

/// Emitted pages live here, inside the publication. Flat: `render/<slug>.html`
/// next to `render/index.html`, so relative media references reach the
/// publication's own `media/` with one `../`.
pub const RENDER_DIR: &str = "render";

const ARTICLE_TEMPLATE: &str = include_str!("templates/article.html");
const INDEX_TEMPLATE: &str = include_str!("templates/index.html");
const FEED_TEMPLATE: &str = include_str!("templates/feed.xml");
const CARD_TEMPLATE: &str = include_str!("templates/card.html");
const BASELINE_CSS: &str = include_str!("templates/calm.css");

// ---------------------------------------------------------------------------
// Markdown → article flow, with TOC headings and galleries
// ---------------------------------------------------------------------------

/// Compile the entire document — H1 title and standfirst line included,
/// because `{{ARTICLE}}` IS the live editing surface in Write mode and must
/// carry them. Body sections feed the TOC.
fn compile_flow(document: &str) -> (String, Vec<slots::Heading>) {
    let mut opts = Options::empty();
    opts.insert(Options::ENABLE_TABLES);
    opts.insert(Options::ENABLE_STRIKETHROUGH);
    let mut out = String::new();
    html::push_html(&mut out, Parser::new_ext(document, opts));

    let out = wrap_galleries(&out);
    let (out, headings) = tag_headings(&out);
    let out = rewrite_paths(&out);
    (out, headings)
}

/// Runs of two or more consecutive image-only paragraphs become a gallery —
/// and so does a single paragraph holding two or more images (markdown keeps
/// adjacent image lines in one paragraph, split by soft breaks). Chunks may
/// carry leading non-paragraph content (the H1 frame, list closers); those
/// pass through as-is.
fn wrap_galleries(html_str: &str) -> String {
    let imgs_of = |para: &str| -> Option<Vec<String>> {
        let inner = para.strip_prefix("<p>")?.strip_suffix("</p>\n")?;
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

    let flush = |out: &mut String, run: &mut Vec<String>| {
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
    };

    let mut out = String::with_capacity(html_str.len());
    let mut run: Vec<String> = Vec::new();
    let mut rest = html_str;
    while !rest.is_empty() {
        let Some(end) = rest.find("</p>\n") else {
            flush(&mut out, &mut run);
            out.push_str(rest);
            break;
        };
        let chunk = &rest[..end + 5];
        rest = &rest[end + 5..];

        let popen = chunk.rfind("<p>");
        let Some(popen) = popen else {
            flush(&mut out, &mut run);
            out.push_str(chunk);
            continue;
        };
        let (prelude, para) = (&chunk[..popen], &chunk[popen..]);
        if !prelude.is_empty() {
            flush(&mut out, &mut run);
            out.push_str(prelude);
        }
        match imgs_of(para) {
            Some(imgs) => run.extend(imgs),
            None => {
                flush(&mut out, &mut run);
                out.push_str(para);
            }
        }
    }
    out
}

/// Give every h2/h3 a stable id and collect them for the TOC. The H1 is the
/// article's title and stays untouched.
fn tag_headings(html_in: &str) -> (String, Vec<slots::Heading>) {
    let mut out = String::with_capacity(html_in.len());
    let mut headings = Vec::new();
    let mut rest = html_in;
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
        headings.push(slots::Heading { level, text, id });
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
fn rewrite_paths(html_in: &str) -> String {
    html_in
        .replace("src=\"media/", "src=\"../media/")
        .replace("href=\"media/", "href=\"../media/")
        .replace("href=\"articles/", "href=\"")
        .replace(".md\"", ".html\"")
}

/// The embedded default template's bytes, so a conduct session can seed its
/// draft before the space owns a file.
pub fn embedded_article_template() -> &'static str {
    ARTICLE_TEMPLATE
}

// ---------------------------------------------------------------------------
// Gather → compose → decorate
// ---------------------------------------------------------------------------

/// The display-ready cover reference: the width-1024 rendition when it has
/// already been derived (the settler prewarms it), otherwise the original.
/// Stat-only — rendering never derives renditions.
fn cover_src(publication_root: &Path, cover: &Option<String>) -> Option<String> {
    let c = cover
        .as_deref()?
        .trim()
        .trim_start_matches("./")
        .to_string();
    if c.is_empty() || !c.contains('/') {
        return None;
    }
    let abs = confine(publication_root, Path::new(&c)).ok()?;
    if !abs.exists() {
        return None;
    }
    if let (Some(stem), Some(ext)) = (abs.file_stem(), abs.extension()) {
        let rendition = format!("{}_1024.{}", stem.to_string_lossy(), ext.to_string_lossy());
        let rel_dir = Path::new(&c).parent().map(|p| p.to_path_buf());
        if let Some(dir) = rel_dir {
            let cand = dir.join(rendition);
            if confine(publication_root, &cand)
                .ok()
                .is_some_and(|p| p.exists())
            {
                return Some(format!("../{}", cand.to_string_lossy().replace('\\', "/")));
            }
        }
    }
    Some(format!("../{c}"))
}

pub(crate) fn load_template(
    publication_root: &Path,
    name: &str,
    fallback: &'static str,
) -> Result<String> {
    let rel = Path::new("templates").join(name);
    let p = confine(publication_root, &rel)?;
    if p.exists() {
        Ok(std::fs::read_to_string(&p)?)
    } else {
        Ok(fallback.to_string())
    }
}

/// Tezuri's behaviors, bound to the classes Tezuri itself emits. Self-contained:
/// the lightbox overlay is created here, never demanded from the template.
const BEHAVIOR_JS: &str = r##"<script>
(function () {
  var lb = null;
  function open(src) {
    if (!lb) {
      lb = document.createElement('div');
      lb.className = 'lightbox';
      lb.innerHTML = '<img alt="">';
      document.body.appendChild(lb);
      lb.addEventListener('click', function () { this.classList.remove('on'); });
      document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') lb.classList.remove('on');
      });
    }
    lb.querySelector('img').src = src;
    lb.classList.add('on');
  }
  document.querySelectorAll('.gallery img, .article-prose > p > img')
    .forEach(function (img) { img.addEventListener('click', function () { open(img.src); }); });

  // Scroll-spy: the toc's current section follows the reader.
  var links = [].slice.call(document.querySelectorAll('.toc a[href^="#"]'));
  if (!links.length || !('IntersectionObserver' in window)) return;
  var byId = {};
  links.forEach(function (l) { byId[l.getAttribute('href').slice(1)] = l; });
  var obs = new IntersectionObserver(function (entries) {
    entries.forEach(function (en) {
      if (!en.isIntersecting) return;
      links.forEach(function (l) { l.classList.remove('current'); });
      var link = byId[en.target.id];
      if (link) link.classList.add('current');
    });
  }, { rootMargin: '-80px 0px -70% 0px', threshold: 0 });
  Object.keys(byId).forEach(function (id) {
    var el = document.getElementById(id);
    if (el) obs.observe(el);
  });
})();
</script>"##;

/// Insert rendered CSS early in `<head>` so authored template styles and the
/// space's theme.css always override the baseline. Graceful when a template
/// has no head: try `<body>`, then append at the end — CSS still applies.
fn style_injection_point(doc: &str) -> usize {
    if let Some(h) = doc.find("<head") {
        if let Some(gt) = doc[h..].find('>') {
            return h + gt + 1;
        }
    }
    if let Some(b) = doc.find("<body") {
        if let Some(gt) = doc[b..].find('>') {
            return b + gt + 1;
        }
    }
    doc.len()
}

/// Compose one gathered context through its template, then inject theme and
/// behaviors. Returns HTML plus editor notes.
fn render_template(template: &str, ctx: &Ctx, theme_css: &str) -> (String, Vec<String>) {
    let parts = slots::parse_template(template);
    let (composed, notes) = slots::compose(&parts, ctx);

    let styles = format!(
        "<style id=\"tezuri-baseline\">{}</style><style id=\"tezuri-theme\">{}</style>",
        BASELINE_CSS,
        esc_style(theme_css)
    );
    let insert_at = style_injection_point(&composed);
    let mut with_styles = String::with_capacity(composed.len() + styles.len() + BEHAVIOR_JS.len());
    with_styles.push_str(&composed[..insert_at]);
    with_styles.push_str(&styles);
    with_styles.push_str(&composed[insert_at..]);

    match with_styles.rfind("</body>") {
        Some(p) => with_styles.insert_str(p, BEHAVIOR_JS),
        None => with_styles.push_str(BEHAVIOR_JS),
    }

    (with_styles, notes)
}

/// CSS is author content injected verbatim; only close-tag sequences could
/// escape the style element, so those are neutralized defensively.
fn esc_style(css: &str) -> String {
    css.replace("</style", "<\\/style")
}

// ---------------------------------------------------------------------------
// Write-mode composition: parse the template, project every slot live
// ---------------------------------------------------------------------------

/// One ordered piece of the Write-mode page. The editor mounts at the first
/// `ArticleFlow`; later instances mirror it. Slots carry their evaluated
/// value plus the raw expression so the desk can re-conduct them.
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
fn head_dress(template: &str, publication_root: &Path) -> Result<String> {
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

    let theme = crate::theme::read(publication_root).unwrap_or_default();
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
fn extract_blocks(doc: &str, open: &str, close: &str) -> Vec<String> {
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
fn split_article_flow(html: String, mirror: bool, raw: String, hints: Vec<String>) -> Seg {
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
enum Token {
    Article(usize),
    Slot(usize),
    Junk,
}

fn parse_token(inner: &str) -> Token {
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
fn carve_to_body(page: &str) -> String {
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
fn strip_blocks(s: &str) -> String {
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
fn site_display_name(publication_root: &Path, name: &str) -> String {
    if !name.is_empty() {
        return name.to_string();
    }
    publication_root
        .file_name()
        .and_then(|s| s.to_str())
        .unwrap_or("A space")
        .to_string()
}

/// Gather article context: files → identity → publishable set → neighbors.
pub fn gather_article_ctx(publication_root: &Path, slug: &str) -> Result<Ctx> {
    let a = Article::load(publication_root, slug)?;
    let identity = crate::identity::Identity::load(publication_root)?;
    let (flow_html, headings) = compile_flow(&a.document);

    // Excerpt works from prose without the frame.
    let body_md = strip_frame(&a.document).to_string();

    let publishable = publishable_entries(publication_root)?;
    let neighbors = slots::Ctx::neighbors_for(&publishable, slug);

    let cta = site_cta_of(&identity);
    let site_url = extras_str(&identity.extra, &["site_url", "url"]);

    Ok(Ctx {
        output: Output::Article,
        slug: slug.to_string(),
        title: a.title(),
        standfirst: a.standfirst(),
        raw_date: a.meta.date.clone(),
        words: a.word_count(),
        state: a.meta.state,
        tags: a.meta.tags.clone(),
        cover_src: cover_src(publication_root, &a.meta.cover),
        body_md,
        flow_html,
        headings,
        neighbors,
        site_name: site_display_name(publication_root, &identity.name),
        byline: a
            .meta
            .author
            .clone()
            .filter(|s| !s.trim().is_empty())
            .map(|s| s.trim().to_string())
            .unwrap_or_else(|| {
                if identity.byline.is_empty() {
                    identity.persona.clone()
                } else {
                    identity.byline.clone()
                }
            }),
        banner: identity.header_style() == crate::identity::HeaderStyle::Banner,
        cta,
        site_url,
        footer_md: extras_str(&identity.extra, &["footer"]),
        publishable,
        require_article: true,
    })
}

fn strip_frame(document: &str) -> &str {
    let mut rest = document.trim_start();
    if let Some(after) = rest.strip_prefix("# ") {
        // Skip the title line, then blanks, then the positional standfirst
        // paragraph (a heading is never a standfirst).
        rest = after.split_once('\n').map(|(_, r)| r).unwrap_or("");
        rest = rest.trim_start();
        if !rest.is_empty() && !rest.starts_with('#') {
            rest = match rest.split_once("\n\n") {
                Some((_, r)) => r,
                None => "",
            };
        }
    }
    rest.trim_start()
}

fn extras_str(
    extra: &std::collections::BTreeMap<String, serde_yaml::Value>,
    keys: &[&str],
) -> String {
    for k in keys {
        if let Some(v) = extra.get(*k).and_then(|v| v.as_str()) {
            return v.trim().to_string();
        }
    }
    String::new()
}

/// A call-to-action from the space's own publication.yaml: modeled first,
/// the earlier discord-specific key kept working.
fn site_cta_of(identity: &crate::identity::Identity) -> Option<(String, String)> {
    if let Some(url) = extras_str(&identity.extra, &["site_cta_url"]).into_option() {
        let label = extras_str(&identity.extra, &["site_cta_label"]);
        return Some((
            if label.is_empty() {
                "Read more".into()
            } else {
                label
            },
            url,
        ));
    }
    let discord = extras_str(&identity.extra, &["discord"]);
    (!discord.is_empty()).then(|| ("Discuss on Discord".into(), discord))
}

trait IntoOption {
    fn into_option(self) -> Option<String>;
}
impl IntoOption for String {
    fn into_option(self) -> Option<String> {
        (!self.is_empty()).then_some(self)
    }
}

/// Publishable set, newest first, undated last — the desk's own ordering.
fn publishable_entries(publication_root: &Path) -> Result<Vec<crate::desk::DeskEntry>> {
    Ok(crate::desk::Desk::rebuild(publication_root)?
        .entries
        .into_iter()
        .filter(|e| e.state == State::Published)
        .collect())
}

// ---------------------------------------------------------------------------
// Public surface
// ---------------------------------------------------------------------------

/// Compile one article into a complete page (CSS and behaviors applied).
pub fn render_article(publication_root: &Path, slug: &str) -> Result<String> {
    Ok(render_article_warned(publication_root, slug)?.0)
}

/// Same compilation, surfacing editor notes alongside — the Write-mode seam.
pub fn render_article_warned(publication_root: &Path, slug: &str) -> Result<(String, Vec<String>)> {
    let tpl = load_template(publication_root, "article.html", ARTICLE_TEMPLATE)?;
    render_article_with(publication_root, slug, &tpl)
}

/// The full page compiled against a draft template — the template editor's
/// live specimen, byte-shaped like the eventual artifact.
pub fn render_article_with(
    publication_root: &Path,
    slug: &str,
    template: &str,
) -> Result<(String, Vec<String>)> {
    let ctx = gather_article_ctx(publication_root, slug)?;
    let theme_css = crate::theme::read(publication_root)?;
    Ok(render_template(template, &ctx, &theme_css))
}

/// Compile one article and write its page. Returns the written path
/// (publication-relative).
pub fn write_page(publication_root: &Path, slug: &str) -> Result<String> {
    let page = render_article(publication_root, slug)?;
    let rel = format!("{RENDER_DIR}/{slug}.html");
    atomic_write(
        &confine(publication_root, Path::new(&rel))?,
        page.as_bytes(),
    )?;
    Ok(rel)
}

/// Site-level context shared by the index and feed outputs.
fn site_ctx(publication_root: &Path) -> Result<(crate::identity::Identity, Ctx)> {
    let identity = crate::identity::Identity::load(publication_root)?;
    let publishable = publishable_entries(publication_root)?;
    let name = site_display_name(publication_root, &identity.name);
    let byline = if identity.byline.is_empty() {
        identity.persona.clone()
    } else {
        identity.byline.clone()
    };
    let ctx = Ctx {
        output: Output::Index,
        slug: String::new(),
        title: name.clone(),
        standfirst: None,
        raw_date: None,
        words: 0,
        state: State::Published,
        tags: vec![],
        banner: false,
        cover_src: None,
        body_md: String::new(),
        flow_html: String::new(),
        headings: vec![],
        neighbors: Default::default(),
        site_name: name,
        byline,
        cta: site_cta_of(&identity),
        site_url: extras_str(&identity.extra, &["site_url", "url"]),
        footer_md: extras_str(&identity.extra, &["footer"]),
        publishable,
        require_article: false,
    };
    Ok((identity, ctx))
}

/// (Re)write the index page from the current publishable set.
pub fn write_index(publication_root: &Path) -> Result<String> {
    let (_, ctx) = site_ctx(publication_root)?;
    let index_rel = format!("{RENDER_DIR}/index.html");
    let bytes = composed_bytes(publication_root, "index.html", INDEX_TEMPLATE, &ctx)?;
    atomic_write(&confine(publication_root, Path::new(&index_rel))?, &bytes)?;
    Ok(index_rel)
}

/// (Re)write the RSS feed from the current publishable set. The space may
/// own templates/feed.xml; an embedded channel ships otherwise.
pub fn write_feed(publication_root: &Path) -> Result<String> {
    let (_, mut ctx) = site_ctx(publication_root)?;
    ctx.output = Output::Feed;
    let rel = format!("{RENDER_DIR}/feed.xml");
    let bytes = composed_bytes(publication_root, "feed.xml", FEED_TEMPLATE, &ctx)?;
    atomic_write(&confine(publication_root, Path::new(&rel))?, &bytes)?;
    Ok(rel)
}

/// Compile one article into its embeddable card: `render/<slug>.card.html`.
/// Bare composition — no Tezuri chrome, no theme injection; the template is
/// self-contained because embeds leave home.
pub fn write_card(publication_root: &Path, slug: &str) -> Result<String> {
    let mut ctx = gather_article_ctx(publication_root, slug)?;
    ctx.output = Output::Card;
    ctx.require_article = false;
    let tpl = load_template(publication_root, "card.html", CARD_TEMPLATE)?;
    let parts = slots::parse_template(&tpl);
    let (html, _) = slots::compose(&parts, &ctx);
    let rel = format!("{RENDER_DIR}/{slug}.card.html");
    atomic_write(
        &confine(publication_root, Path::new(&rel))?,
        html.as_bytes(),
    )?;
    Ok(rel)
}

fn composed_bytes(
    publication_root: &Path,
    name: &str,
    fallback: &'static str,
    ctx: &Ctx,
) -> Result<Vec<u8>> {
    let tpl = load_template(publication_root, name, fallback)?;
    let theme_css = crate::theme::read(publication_root)?;
    let (page, _) = render_template(&tpl, ctx, &theme_css);
    Ok(page.into_bytes())
}

/// Compile the publishable set and write `render/<slug>.html` +
/// `render/index.html`. Idempotent; v1 never deletes files it does not
/// recognize. Returns the written paths (publication-relative).
pub fn emit_render(publication_root: &Path) -> Result<Vec<String>> {
    let mut written = Vec::new();
    for e in &publishable_entries(publication_root)? {
        written.push(write_page(publication_root, &e.slug)?);
        written.push(write_card(publication_root, &e.slug)?);
    }
    written.push(write_index(publication_root)?);
    written.push(write_feed(publication_root)?);
    // The event counts article pages; cards, index and feed are furniture.
    Journal::open(publication_root)?.record(Event::Rendered {
        pages: written.len().saturating_sub(2) / 2,
    })?;
    Ok(written)
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::tempdir;

    const DOC: &str = "# On Rust\n\n_A meditation on ownership._\n\n## First part\n\nHello \
                       world.\n\n## Second part\n\nMore words.\n\n### Deep\n\nDeeper.\n";

    fn setup(dir: &Path, slug: &str, doc: &str) {
        Article::create(dir, slug, slug).unwrap();
        let mut a = Article::load(dir, slug).unwrap();
        a.document = doc.into();
        a.save(dir).unwrap();
    }

    #[test]
    fn flow_keeps_title_and_standfirst_and_ids_sections() {
        let (flow, headings) = compile_flow(DOC);
        assert!(flow.contains("<h1>On Rust</h1>"), "H1 lives in the flow");
        assert!(flow.contains("<em>A meditation on ownership.</em>"));
        assert!(flow.contains("<h2 id=\"sec-1-first-part\">First part</h2>"));
        assert!(flow.contains("<h3 id=\"sec-3-deep\">Deep</h3>"));
        assert_eq!(headings.len(), 3);
    }

    #[test]
    fn galleries_wrap_adjacent_images() {
        let md = "# T\n\n![a](media/a.png)\n![b](media/b.png)\n\nSolo:\n\n![c](media/c.png)\n";
        let (flow, _) = compile_flow(md);
        assert!(flow.contains("<div class=\"gallery\">"));
        assert!(flow.contains("src=\"../media/a.png\""));
        let gallery_end = flow.find("</div>").unwrap();
        assert!(flow[gallery_end..].contains("<p><img"), "{flow}");
    }

    #[test]
    fn article_links_become_sibling_pages() {
        let (flow, _) = compile_flow("T\n\nsee [that](articles/other.md)\n");
        assert!(flow.contains("href=\"other.html\""));
    }

    #[test]
    fn dumb_default_renders_calm_full_document_page() {
        let dir = tempdir().unwrap();
        setup(dir.path(), "on-rust", DOC);

        let (page, notes) = render_article_warned(dir.path(), "on-rust").unwrap();
        assert!(notes.is_empty());
        assert!(
            page.contains("<div class=\"article-prose\"><h1>On Rust</h1>"),
            "{page}"
        );
        // Navigation exists iff the template says {{toc}}; sections are still
        // anchorable by id.
        assert!(page.contains("<h2 id=\"sec-1-first-part\">"));
        assert!(page.contains("<style id=\"tezuri-baseline\">"), "{page}");
        assert!(page.contains(".lightbox.on"), "behaviors ship");
        assert!(page.contains("is-article is-draft"), "{page}");
        let tail = page.trim_end().replace('\n', "");
        assert!(tail.ends_with("</body></html>"), "{page}");
    }

    #[test]
    fn emit_writes_pages_and_index_and_journals() {
        let dir = tempdir().unwrap();
        let mut alpha = Article::create(dir.path(), "alpha", "Alpha").unwrap();
        alpha.meta.state = State::Published;
        alpha.save(dir.path()).unwrap();
        let mut beta = Article::create(dir.path(), "beta", "Beta").unwrap();
        beta.meta.state = State::Published;
        beta.save(dir.path()).unwrap();
        Article::create(dir.path(), "secret-draft", "Secret").unwrap();

        let written = emit_render(dir.path()).unwrap();
        assert_eq!(
            written.len(),
            6,
            "two pages + two cards + index + feed; drafts never emit"
        );
        assert!(dir.path().join("render/alpha.html").exists());
        assert!(dir.path().join("render/beta.html").exists());
        assert!(!dir.path().join("render/secret-draft.html").exists());
        let card = std::fs::read_to_string(dir.path().join("render/alpha.card.html")).unwrap();
        assert!(card.contains("tezuri-card"), "{card}");
        assert!(card.contains("Alpha"), "{card}");
        assert!(
            !card.contains("tezuri-baseline"),
            "cards carry no chrome: {card}"
        );
        let index = std::fs::read_to_string(dir.path().join("render/index.html")).unwrap();
        assert!(index.contains("href=\"alpha.html\""));
        assert!(index.contains("href=\"beta.html\""));
        assert!(!index.contains("secret-draft"));
        let feed = std::fs::read_to_string(dir.path().join("render/feed.xml")).unwrap();
        assert!(feed.contains("<rss version=\"2.0\">"), "{feed}");
        assert!(feed.contains("<link>alpha.html</link>"), "{feed}");
        assert!(feed.contains("<link>beta.html</link>"), "{feed}");
        assert!(!feed.contains("secret-draft"), "{feed}");

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
        std::fs::write(
            tpl_dir.join("article.html"),
            "<html><head>{{title}}</head><body class=\"mine\">{{ARTICLE}}</body></html>",
        )
        .unwrap();

        let page = render_article(dir.path(), "gamma").unwrap();
        // Baseline lands immediately inside head, so any later style block
        // in a template's own head always wins over it.
        assert!(
            page.starts_with("<html><head><style id=\"tezuri-baseline\">"),
            "{page}"
        );
        assert!(
            page.contains("</style><style id=\"tezuri-theme\"></style>Gamma</head>"),
            "{page}"
        );
        assert!(
            page.contains("<div class=\"article-prose\"><h1>Gamma</h1>"),
            "{page}"
        );
        let tail = page.trim_end().replace('\n', "");
        assert!(tail.ends_with("</body></html>"), "{page}");
        assert_eq!(page.matches("(function () {").count(), 1, "behaviors once");
    }

    #[test]
    fn unknown_slot_whispers_into_notes_not_breakage() {
        let dir = tempdir().unwrap();
        Article::create(dir.path(), "g2", "G2").unwrap();
        let tpl_dir = dir.path().join("templates");
        std::fs::create_dir_all(&tpl_dir).unwrap();
        std::fs::write(
            tpl_dir.join("article.html"),
            "{{sparkle}}<body>{{ARTICLE}}</body>",
        )
        .unwrap();

        let (page, notes) = render_article_warned(dir.path(), "g2").unwrap();
        assert!(page.contains("G2"), "{page}");
        assert_eq!(notes.len(), 1);
        assert!(notes[0].starts_with("unknown slot {{sparkle}}"));
    }

    #[test]
    fn theme_css_is_injected_for_the_render_plane() {
        let dir = tempdir().unwrap();
        Article::create(dir.path(), "themed", "Themed").unwrap();
        crate::theme::write(dir.path(), ".article-prose { letter-spacing: .5px; }").unwrap();

        let page = render_article(dir.path(), "themed").unwrap();
        assert!(
            page.contains("<style id=\"tezuri-theme\">.article-prose"),
            "{page}"
        );
    }

    #[test]
    fn cover_prefers_derived_1024_when_present() {
        let dir = tempdir().unwrap();
        let media = dir.path().join("media");
        std::fs::create_dir_all(&media).unwrap();
        std::fs::write(media.join("ab-plug.png"), b"x").unwrap();
        std::fs::write(media.join("ab-plug_1024.png"), b"x").unwrap();
        let _ = std::fs::write(media.join("cd-other.jpg"), b"x");

        assert_eq!(
            cover_src(dir.path(), &Some("media/ab-plug.png".into())).unwrap(),
            "../media/ab-plug_1024.png"
        );
        assert_eq!(
            cover_src(dir.path(), &Some("media/cd-other.jpg".into())).unwrap(),
            "../media/cd-other.jpg"
        );
        assert!(cover_src(dir.path(), &Some("media/missing.png".into())).is_none());
        assert!(cover_src(dir.path(), &Some("bare-name.png".into())).is_none());
    }

    #[test]
    fn compose_carries_the_artifacts_dress() {
        let dir = tempdir().unwrap();
        setup(dir.path(), "dressed", DOC);
        let tpl_dir = dir.path().join("templates");
        std::fs::create_dir_all(&tpl_dir).unwrap();
        std::fs::write(
            tpl_dir.join("article.html"),
            concat!(
                "<html><head>",
                "<link href=\"https://fonts.googleapis.com/css2?family=X\" rel=\"stylesheet\">",
                "<style>.title-banner--title { font-style: italic; }</style>",
                "</head><body>{{ARTICLE | title-banner}}</body></html>"
            ),
        )
        .unwrap();

        let c = compose_write_view(dir.path(), "dressed").unwrap();
        assert!(
            c.css
                .contains("@import url('https://fonts.googleapis.com/css2?family=X');"),
            "{}",
            c.css
        );
        assert!(c
            .css
            .contains(".title-banner--title { font-style: italic; }"));
        // The artifact's cascade, mirrored: the calm baseline lands early so
        // the template's own styles override it, as on the emitted page.
        let baseline_at = c.css.find("Calm baseline").expect("baseline present");
        let authored_at = c
            .css
            .find(".title-banner--title { font-style: italic; }")
            .expect("template styles present");
        assert!(baseline_at < authored_at, "baseline early, authored wins");
    }

    #[test]
    fn draft_compose_and_specimen_render_through_supplied_bytes() {
        let dir = tempdir().unwrap();
        setup(dir.path(), "drafty", DOC);
        std::fs::create_dir_all(dir.path().join("media")).unwrap();
        std::fs::write(dir.path().join("media").join("drafty.png"), b"x").unwrap();
        let mut a = Article::load(dir.path(), "drafty").unwrap();
        a.meta.cover = Some("media/drafty.png".into());
        a.save(dir.path()).unwrap();
        // Banner is the space's decision; the draft only carries the hint.
        std::fs::write(
            dir.path().join("publication.yaml"),
            b"header_style: banner\n",
        )
        .unwrap();

        let draft = "<html><body>{{ARTICLE | title-banner, cover:fill}}</body></html>";

        // The specimen renders the mode through the whole pipeline.
        let (page, notes) = render_article_with(dir.path(), "drafty", draft).unwrap();
        assert!(notes.is_empty());
        assert!(page.contains("<section class=\"title-banner\">"), "{page}");
        assert!(page.contains("cover-fill"), "{page}");
        // The banner owns title + standfirst: the flow sheds them.
        assert!(page.contains("title-banner--title"), "{page}");
        assert!(
            !page.contains("<div class=\"article-prose\"><h1>"),
            "{page}"
        );
    }

    #[test]
    fn compose_write_view_keeps_order_and_marks_the_flow() {
        let dir = tempdir().unwrap();
        setup(dir.path(), "wv", DOC);

        let c = compose_write_view(dir.path(), "wv").unwrap();
        assert!(!c.space_template, "default template: nothing in the space");
        assert!(c.notes.is_empty());
        let kinds: Vec<&str> = c
            .segments
            .iter()
            .map(|s| match s {
                Seg::Text { .. } => "text",
                Seg::ArticleFlow { .. } => "flow",
                Seg::Slot(_) => "slot",
            })
            .collect();
        assert_eq!(
            kinds,
            vec![
                "text", // body opener + page + article wrappers
                "flow", "text", // closing wrappers before </body>
            ]
        );
        // The head never leaks: no doctype, no injected baseline styles.
        let joined = c
            .segments
            .iter()
            .map(|s| match s {
                Seg::Text { html } => html.clone(),
                _ => String::new(),
            })
            .collect::<Vec<_>>()
            .join("");
        assert!(joined.contains("<div class=\"page\">"), "{joined}");
        assert!(!joined.contains("<!DOCTYPE") && !joined.contains("tezuri-baseline"));
        assert!(c.segments[0..].iter().all(|s| !matches!(s, Seg::Slot(_))));
    }

    #[test]
    fn compose_write_view_projects_slots_in_order() {
        let dir = tempdir().unwrap();
        Article::create(dir.path(), "proj", "Proj").unwrap();
        let mut a = Article::load(dir.path(), "proj").unwrap();
        a.document = "# Proj\n\n_Standfast._\n\nBody.\n".into();
        a.meta.tags = vec!["rust".into()];
        a.save(dir.path()).unwrap();

        let tpl_dir = dir.path().join("templates");
        std::fs::create_dir_all(&tpl_dir).unwrap();
        std::fs::write(
            tpl_dir.join("article.html"),
            concat!(
                "<html><head><style>s{}</style><title>{{title}}</title></head>\n",
                "<body class=\"{{body_class}}\">\n",
                "<header>{{site_name}}</header>{{standfirst}}\n",
                "{{ARTICLE}}\n",
                "<footer>{{tags | pills}}{{toc}}{{sparkle}}</footer>",
                "<script>void</script></body></html>"
            ),
        )
        .unwrap();

        let c = compose_write_view(dir.path(), "proj").unwrap();
        assert!(c.space_template);
        let one_unknown = c.notes.iter().any(|n| n.contains("{{sparkle}}"));
        assert!(one_unknown);

        let mut order: Vec<String> = Vec::new();
        for s in &c.segments {
            match s {
                Seg::Text { html } => order.push(format!("text:{html:?}")),
                Seg::ArticleFlow { mirror, .. } => order.push(format!("flow:{mirror}")),
                Seg::Slot(sl) => order.push(format!("slot:{}:{}", sl.name, sl.mirror)),
            }
        }
        // Head is cut (title lives there), attributes before <body>'s `>` too.
        assert_eq!(order.len(), 10, "{order:?}");
        assert!(order[0].starts_with("text:")); // "\n<header>"
        assert_eq!(order[1], "slot:site_name:false");
        assert!(order[2].contains("</header>"));
        assert_eq!(order[3], "slot:standfirst:false");
        assert_eq!(order[4], "text:\"\\n\"");
        assert_eq!(order[5], "flow:false");
        assert_eq!(order[6], "text:\"\\n<footer>\"");
        assert_eq!(order[7], "slot:tags:false");
        assert_eq!(order[8], "slot:toc:false");
        assert_eq!(order[9], "text:\"</footer>\"");

        let standfirst_html = match &c.segments[3] {
            Seg::Slot(sl) => sl.html.clone(),
            _ => unreachable!(),
        };
        assert_eq!(
            standfirst_html,
            "<p class=\"standfirst\"><em>Standfast.</em></p>"
        );
        let tags_html = match &c.segments[7] {
            Seg::Slot(sl) => sl.html.clone(),
            _ => unreachable!("{order:?}"),
        };
        assert_eq!(tags_html, "<span class=\"tagpill\">#rust</span>");

        // Scripts after </body> are stripped from the plane.
        let joined = c
            .segments
            .iter()
            .filter_map(|s| match s {
                Seg::Text { html } => Some(html.as_str()),
                _ => None,
            })
            .collect::<String>();
        assert!(!joined.contains("<script"), "{joined}");
    }

    #[test]
    fn missing_article_marker_still_hands_the_editor_over() {
        let dir = tempdir().unwrap();
        setup(dir.path(), "noart", "# No art\n\nplain.\n");
        // A deliberately flow-less template: the editor must still mount.
        let tpl_dir = dir.path().join("templates");
        std::fs::create_dir_all(&tpl_dir).unwrap();
        std::fs::write(
            tpl_dir.join("article.html"),
            "<html><body><p>layout without a flow</p></body></html>",
        )
        .unwrap();

        let c = compose_write_view(dir.path(), "noart").unwrap();
        let flows: Vec<&Seg> = c
            .segments
            .iter()
            .filter(|s| matches!(s, Seg::ArticleFlow { .. }))
            .collect();
        assert_eq!(flows.len(), 1, "the editor still mounts exactly once");
        assert!(c.notes.iter().any(|n| n.contains("{{ARTICLE}}")));
    }

    #[test]
    fn duplicate_slots_mirror_the_first() {
        let dir = tempdir().unwrap();
        Article::create(dir.path(), "dup", "Dup").unwrap();
        let tpl_dir = dir.path().join("templates");
        std::fs::create_dir_all(&tpl_dir).unwrap();
        std::fs::write(
            tpl_dir.join("article.html"),
            "<html><body><h1>{{title}}</h1>{{ARTICLE}}<small>{{title}}</small></body></html>",
        )
        .unwrap();

        let c = compose_write_view(dir.path(), "dup").unwrap();
        let titles: Vec<bool> = c
            .segments
            .iter()
            .filter_map(|s| match s {
                Seg::Slot(sl) if sl.name == "title" => Some(sl.mirror),
                _ => None,
            })
            .collect();
        assert_eq!(titles, vec![false, true], "second instance mirrors first");
    }

    #[test]
    fn site_cta_supports_modeled_key_and_legacy_discord() {
        use crate::identity::Identity;
        let mut id = Identity {
            name: "K".into(),
            ..Default::default()
        };
        id.extra
            .insert("discord".into(), "https://discord.gg/x".into());

        let cta = site_cta_of(&id).unwrap();
        assert_eq!(cta.0, "Discuss on Discord");
        assert_eq!(cta.1, "https://discord.gg/x");

        id.extra.insert(
            "site_cta_url".into(),
            serde_yaml::Value::String("https://ko-fi.com/k".into()),
        );
        let cta = site_cta_of(&id).unwrap();
        assert_eq!(cta.0, "Read more");
        assert_eq!(cta.1, "https://ko-fi.com/k");
    }

    #[test]
    fn banner_header_style_consumes_the_frame() {
        let dir = tempdir().unwrap();
        setup(dir.path(), "hero", DOC);
        std::fs::write(
            dir.path().join("publication.yaml"),
            b"header_style: banner\n",
        )
        .unwrap();

        // The default template is dumb — the banner only appears when a
        // template asks for it. Through a banner template, title and
        // standfirst feed the hero and leave the flow.
        let tpl = concat!("<html><body>{{ARTICLE | title-banner, cover:none}}</body></html>");
        let (page, _) = render_article_with(dir.path(), "hero", tpl).unwrap();
        assert!(page.contains("title-banner--title"), "{page}");
        assert!(page.contains("title-banner--standfirst"), "{page}");
        assert!(
            !page.contains("<div class=\"article-prose\"><h1>"),
            "flow sheds its frame: {page}"
        );
        assert!(page.contains("Deeper.</p>"), "body survives: {page}");
    }

    #[test]
    fn normal_header_style_keeps_the_flow_whole() {
        let dir = tempdir().unwrap();
        setup(dir.path(), "plain", DOC);
        // No header_style: Normal. Even a banner-carrying template renders
        // the raw flow — the document is king, dressing is a space decision.
        let tpl = concat!("<html><body>{{ARTICLE | title-banner, cover:none}}</body></html>");
        let (page, _) = render_article_with(dir.path(), "plain", tpl).unwrap();
        assert!(!page.contains("<section class=\"title-banner\">"), "{page}");
        assert!(page.contains("<h1>On Rust</h1>"), "{page}");
        assert!(page.contains("A meditation on ownership."), "{page}");
    }
}
