//! Starter packs: presentations an author can pick, then own as plain files.
//!
//! A pack is template + css copied INTO the space on pick — after that the
//! files belong to the author like any other. Nothing is fetched; assets
//! ride in the binary. Picking applies through the ordinary journaled paths
//! (templates::write, theme::write) so propose→apply stays true.

use crate::templates;
use crate::theme;
use anyhow::Result;
use serde::Serialize;
use std::path::Path;

pub struct Pack {
    pub id: &'static str,
    pub name: &'static str,
    pub description: &'static str,
    pub article_template: &'static str,
    pub theme_css: &'static str,
}

pub fn catalog() -> Vec<Pack> {
    vec![
        Pack {
            id: "vanilla",
            name: "Vanilla",
            description: "The calm default, as a pickable: {{ARTICLE}} over the built-in baseline.",
            article_template: include_str!("templates/article.html"),
            theme_css: "",
        },
        Pack {
            id: "gposingway",
            name: "GPosingway",
            description: "Dark showcase frame for screenshot guides: full-bleed title banner, \
                          sticky table of contents, tag rail.",
            article_template: include_str!("packs/gposingway/article.html"),
            theme_css: include_str!("packs/gposingway/theme.css"),
        },
    ]
}

#[derive(Serialize)]
pub struct PackView {
    pub id: String,
    pub name: String,
    pub description: String,
}

/// What the picker sees: identity only, never bytes.
pub fn view() -> Vec<PackView> {
    catalog()
        .into_iter()
        .map(|p| PackView {
            id: p.id.to_string(),
            name: p.name.to_string(),
            description: p.description.to_string(),
        })
        .collect()
}

/// Apply a pack to a space: copies become the space's own files through the
/// one journaled write path each. Existing template/css are overwritten —
/// picking a presentation is deliberate.
pub fn apply(publication_root: &Path, id: &str) -> Result<()> {
    let pack = catalog()
        .into_iter()
        .find(|p| p.id == id)
        .ok_or_else(|| anyhow::anyhow!("unknown pack: {id}"))?;
    templates::write(publication_root, pack.article_template)?;
    theme::write(publication_root, pack.theme_css)?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::tempdir;

    #[test]
    fn catalog_carries_the_named_pair() {
        let v = view();
        assert!(v.iter().any(|p| p.id == "vanilla"));
        assert!(v.iter().any(|p| p.id == "gposingway"));
    }

    #[test]
    fn vanilla_applies_the_embedded_default_and_no_theme() {
        let dir = tempdir().unwrap();
        apply(dir.path(), "vanilla").unwrap();
        assert!(templates::read(dir.path()).unwrap().is_some());
        // Empty css removed nothing that existed; theme stays absent.
        assert_eq!(crate::theme::read(dir.path()).unwrap(), "");
    }

    #[test]
    fn apply_copies_bytes_through_journaled_paths() {
        let dir = tempdir().unwrap();
        apply(dir.path(), "gposingway").unwrap();

        let tpl = templates::read(dir.path()).unwrap().unwrap();
        assert!(tpl.contains("{{ARTICLE | title-banner"), "{tpl}");
        assert!(!tpl.contains("kicker_line") && !tpl.contains("{{css}}"));

        let css = crate::theme::read(dir.path()).unwrap();
        // Pack css carries the prose dress; layout chrome lives in the
        // template itself.
        assert!(css.contains(".article-prose"), "{css}");

        let events = crate::spine::Journal::open(dir.path())
            .unwrap()
            .events()
            .unwrap();
        assert!(events.iter().any(|(_, e)| e.kind() == "template-written"));
        assert!(events.iter().any(|(_, e)| e.kind() == "theme-written"));
    }

    #[test]
    fn unknown_packs_are_refused() {
        let dir = tempdir().unwrap();
        assert!(apply(dir.path(), "nope").is_err());
    }
}
