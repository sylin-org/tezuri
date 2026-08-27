//!  Emission of the publishable set into render/.
use super::*;
use crate::slots::{self, Output};
use crate::spine::{atomic_write, confine, Event, Journal};
use anyhow::Result;
use std::path::Path;
pub const RENDER_DIR: &str = "render";

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
