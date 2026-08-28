//!  Template substitution, style injection, article page assembly.
use super::*;
use crate::slots::{self, Ctx};
use anyhow::Result;
use std::path::Path;
pub(crate) fn style_injection_point(doc: &str) -> usize {
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
pub(crate) fn render_template(template: &str, ctx: &Ctx, theme_css: &str) -> (String, Vec<String>) {
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
pub(crate) fn esc_style(css: &str) -> String {
    css.replace("</style", "<\\/style")
}

// ---------------------------------------------------------------------------
// Write-mode composition: parse the template, project every slot live
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
    let theme_css = read_theme(publication_root)?;
    Ok(render_template(template, &ctx, &theme_css))
}

/// The Write-plane page: the artifact as readers will see it, with the
/// header forced to the plain flow (the markdown stays whole — H1 and the
/// standfirst line are ordinary content here) and the editor runtime
/// injected. Every slot is wrapped in a `data-tz-slot` marker so the
/// runtime can hang its conduct affordances on real elements.
pub fn write_page_html(
    publication_root: &Path,
    slug: &str,
    template: &str,
    media_base: &str,
) -> Result<(String, Vec<String>)> {
    let mut ctx = gather_article_ctx(publication_root, slug)?;
    // The Write plane shows the editing copy: unsaved edits are the thing
    // being composed. The desk, proof, and render/ stay canonical.
    if let Some(dirty) = crate::articles::Article::read_dirty(publication_root, slug)? {
        ctx.title = crate::articles::title_of(&dirty, slug);
        ctx.standfirst = crate::articles::parse_flow(&dirty).1;
        let (flow, headings) = compile_flow(&dirty);
        ctx.flow_html = flow;
        ctx.headings = headings;
        ctx.body_md = strip_frame(&dirty).to_string();
        ctx.publishable = publishable_entries(publication_root)?;
        ctx.neighbors = crate::slots::Ctx::neighbors_for(&ctx.publishable, slug);
    }
    // Write always edits the document as written; banner consumption is a
    // reader-facing projection, never an editor state.
    ctx.banner = false;
    let parts = crate::slots::parse_template(template);
    let (body, notes) = crate::slots::compose_wrapped(&parts, &ctx);

    // The artifact's style-injection contract, on the composed body.
    let theme_css = read_theme(publication_root)?;
    let styles = format!(
        "<style id=\"tezuri-baseline\">{}</style><style id=\"tezuri-theme\">{}</style>",
        esc_style(crate::render::assets::BASELINE_CSS),
        esc_style(&theme_css)
    );
    let insert_at = style_injection_point(&body);
    let page = format!("{}{}{}", &body[..insert_at], styles, &body[insert_at..]);

    // The srcdoc iframe's base URL is the app root, not render/: artifact
    // references like ../media/<hash>.png would 404. They ride the media
    // protocol instead, like every other image the desk shows.
    let page = page.replace("../media/", media_base);

    let with_script = page.replace(
        "</body>",
        "<script src=\"/tezuri-editor.js\" defer></script>
</body>",
    );
    Ok((with_script, notes))
}

pub(crate) fn composed_bytes(
    publication_root: &Path,
    name: &str,
    fallback: &'static str,
    ctx: &Ctx,
) -> Result<Vec<u8>> {
    let tpl = load_template(publication_root, name, fallback)?;
    let theme_css = read_theme(publication_root)?;
    let (page, _) = render_template(&tpl, ctx, &theme_css);
    Ok(page.into_bytes())
}
