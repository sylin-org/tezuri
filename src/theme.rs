//! Theme: the space's own reading-surface dialect.
//!
//! `theme.css` inside the publication styles Tezuri's editor plane — the one
//! derived view the author stares at for hours, and therefore theirs to tune.
//! Presets are embedded in the binary (nothing is ever fetched); applying one
//! composes its CSS into the editable draft, and saving writes the file
//! atomically with a journal entry. An empty write clears the theme: the
//! absence of the file is itself the "built-in" look.

use crate::spine::{atomic_write, confine, Event, Journal};
use anyhow::Result;
use serde::Serialize;
use std::path::Path;

pub const FILE_NAME: &str = "theme.css";

/// Every preset styles the same scope class, so the specimen preview and the
/// live editor plane consume identical rules.
pub const SCOPE_CLASS: &str = ".theme-scope";

#[derive(Debug, Clone, Serialize)]
pub struct Preset {
    pub id: String,
    pub name: String,
    pub description: String,
    pub css: String,
}

pub fn presets() -> Vec<Preset> {
    vec![
        Preset {
            id: "garden".into(),
            name: "Night garden".into(),
            description: "The built-in look, made explicit: warm dark ground, serif prose, \
                          moonlit accents."
                .into(),
            css: include_str!("themes/garden.css").to_string(),
        },
        Preset {
            id: "sepia".into(),
            name: "Sepia press".into(),
            description: "Paper-warm reading surface, brown-black ink — long sessions, \
                          print-flavored."
                .into(),
            css: include_str!("themes/sepia.css").to_string(),
        },
        Preset {
            id: "ink".into(),
            name: "Working ink".into(),
            description: "Maximum contrast, sans-serif, no ornament — for drafting, not \
                          admiring."
                .into(),
            css: include_str!("themes/ink.css").to_string(),
        },
    ]
}

pub fn path(publication_root: &Path) -> Result<std::path::PathBuf> {
    confine(publication_root, Path::new(FILE_NAME))
}

/// The current theme CSS, or empty when the space uses the built-in look.
pub fn read(publication_root: &Path) -> Result<String> {
    let p = path(publication_root)?;
    if !p.exists() {
        return Ok(String::new());
    }
    Ok(std::fs::read_to_string(&p)?)
}

/// Persist the theme. An empty `css` removes the file: absence is the
/// built-in look. Always journaled.
pub fn write(publication_root: &Path, css: &str) -> Result<()> {
    let p = path(publication_root)?;
    if css.trim().is_empty() {
        if p.exists() {
            std::fs::remove_file(&p)?;
        }
    } else {
        atomic_write(&p, css.as_bytes())?;
    }
    Journal::open(publication_root)?.record(Event::ThemeWritten)?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::tempdir;

    #[test]
    fn presets_exist_and_share_the_scope() {
        let ps = presets();
        assert!(ps.len() >= 3);
        for p in &ps {
            assert!(p.css.contains(SCOPE_CLASS), "{} must style the scope", p.id);
            assert!(
                !p.css.contains("http"),
                "{} must not reference the network",
                p.id
            );
        }
    }

    #[test]
    fn write_read_clear_roundtrip() {
        let dir = tempdir().unwrap();
        assert_eq!(read(dir.path()).unwrap(), "");

        write(dir.path(), ".theme-scope { --paper: #111; }").unwrap();
        assert_eq!(read(dir.path()).unwrap(), ".theme-scope { --paper: #111; }");
        assert!(path(dir.path()).unwrap().exists());

        // Empty write clears: absence is the built-in look.
        write(dir.path(), "   ").unwrap();
        assert!(!path(dir.path()).unwrap().exists());
        assert_eq!(read(dir.path()).unwrap(), "");
    }
}
