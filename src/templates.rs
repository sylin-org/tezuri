//! Templates: the space's own presentation files.
//!
//! `templates/article.html` (and later the family: index, feed, card) are
//! the author's bytes — written here only through this one path, atomically,
//! journaled as TemplateWritten. An empty write removes the file so the
//! embedded default speaks again. Conduct writes drafts first; the file
//! only moves when the author accepts.

use crate::spine::{atomic_write, confine, Event, Journal};
use anyhow::Result;
use std::path::Path;

pub const FILE_NAME: &str = "article.html";

pub fn path(publication_root: &Path) -> Result<std::path::PathBuf> {
    confine(
        publication_root,
        Path::new("templates").join(FILE_NAME).as_path(),
    )
}

/// The space's template text, or None when none exists (the embedded
/// default is the presentation until the author owns a copy).
pub fn read(publication_root: &Path) -> Result<Option<String>> {
    let p = path(publication_root)?;
    if !p.exists() {
        return Ok(None);
    }
    Ok(Some(std::fs::read_to_string(&p)?))
}

/// Persist an accepted draft; empty text removes the file back to the
/// embedded default. Always journaled.
pub fn write(publication_root: &Path, text: &str) -> Result<()> {
    let p = path(publication_root)?;
    if text.trim().is_empty() {
        if p.exists() {
            std::fs::remove_file(&p)?;
        }
    } else {
        if let Some(parent) = p.parent() {
            std::fs::create_dir_all(parent)?;
        }
        atomic_write(&p, text.as_bytes())?;
    }
    Journal::open(publication_root)?.record(Event::TemplateWritten {
        name: FILE_NAME.into(),
    })?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::tempdir;

    #[test]
    fn absent_then_written_then_cleared_with_journal() {
        let dir = tempdir().unwrap();
        assert_eq!(read(dir.path()).unwrap(), None);

        write(dir.path(), "<body>{{ARTICLE}}</body>").unwrap();
        assert_eq!(
            read(dir.path()).unwrap().as_deref(),
            Some("<body>{{ARTICLE}}</body>")
        );
        let events = crate::spine::Journal::open(dir.path())
            .unwrap()
            .events()
            .unwrap();
        assert!(events.iter().any(|(_, e)| e.kind() == "template-written"));

        // Empty write is the embedded default again.
        write(dir.path(), "   ").unwrap();
        assert_eq!(read(dir.path()).unwrap(), None);
    }

    #[test]
    fn writes_stay_inside_the_space_templates_dir() {
        let dir = tempdir().unwrap();
        let p = path(dir.path()).unwrap();
        assert!(p.starts_with(dir.path().join("templates")));
        assert_eq!(p.file_name().and_then(|s| s.to_str()), Some(FILE_NAME));
    }

    #[test]
    fn md_inline_lives_in_the_slot_engine() {
        assert_eq!(
            crate::slots::md_inline("_a_ & <b>"),
            "<em>a</em> &amp; &lt;b&gt;"
        );
    }
}
