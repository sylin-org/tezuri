//!  The space's own presentation files: theme.css and templates/*.
use crate::spine::confine;
use anyhow::Result;
use std::path::Path;
pub(crate) const ARTICLE_TEMPLATE: &str = include_str!("../templates/article.html");

pub(crate) const INDEX_TEMPLATE: &str = include_str!("../templates/index.html");

pub(crate) const FEED_TEMPLATE: &str = include_str!("../templates/feed.xml");

pub(crate) const CARD_TEMPLATE: &str = include_str!("../templates/card.html");

pub(crate) const BASELINE_CSS: &str = include_str!("../templates/calm.css");

// ---------------------------------------------------------------------------
// Markdown → article flow, with TOC headings and galleries
// ---------------------------------------------------------------------------

/// The embedded default template's bytes, so a conduct session can seed its
/// draft before the space owns a file.
pub fn embedded_article_template() -> &'static str {
    ARTICLE_TEMPLATE
}

// ---------------------------------------------------------------------------
// Gather → compose → decorate
// ---------------------------------------------------------------------------

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
