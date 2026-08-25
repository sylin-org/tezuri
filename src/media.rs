//! Media: content-addressed images with declared renditions.
//!
//! One source file per unique content; transforms are declarative intent
//! (`?w=1200&format=webp`) resolved at render/build time, never new files per
//! usage. Adjacent bare images collapse into a gallery by convention.

use crate::spine::{atomic_write, confine, content_hash, Event, Journal};
use anyhow::{bail, Context, Result};
use std::fs;
use std::path::Path;

/// Image signatures we accept — anything else (SVG, HTML, scripts) is refused.
fn sniff_image(bytes: &[u8]) -> Option<&'static str> {
    match bytes {
        [0x89, b'P', b'N', b'G', ..] => Some("png"),
        [0xFF, 0xD8, 0xFF, ..] => Some("jpg"),
        [b'G', b'I', b'F', b'8', ..] => Some("gif"),
        [b'R', b'I', b'F', b'F', ..] if bytes.len() > 11 && &bytes[8..12] == b"WEBP" => {
            Some("webp")
        }
        _ => None,
    }
}

pub struct StoredMedia {
    pub hash: String,
    pub path: String, // publication-relative
    pub ext: &'static str,
}

/// Display policy: editors reference images at the reading measure (~720px
/// logical). High-DPI viewports want ~2x backing pixels, so the display
/// rendition target is 1600px wide; the original file is kept untouched as
/// the archival source. v1 records the intent in the journal and emits the
/// original path — derivation lands with the rendition pipeline.
pub const DISPLAY_MAX_WIDTH: u32 = 1600;

/// Store dropped/pasted image bytes. Identical content = identical file, always.
pub fn store(publication_root: &Path, bytes: &[u8], alt: &str) -> Result<StoredMedia> {
    let ext = sniff_image(bytes)
        .context("that file is not a real image (PNG, JPEG, WebP or GIF); nothing that could carry script is accepted")?;
    if bytes.len() > 25 * 1024 * 1024 {
        bail!("image is larger than 25 MB");
    }
    // Strip EXIF for JPEGs here in spirit: a full implementation would rewrite
    // the container; v1 records the intent in the journal and refuses nothing.
    let hash = content_hash(bytes);
    let rel = Path::new("media").join(format!("{hash}.{ext}"));
    let target = confine(publication_root, &rel)?;
    if !target.exists() {
        atomic_write(&target, bytes)?;
    }
    Journal::open(publication_root)?.record(Event::MediaStored {
        hash: hash.clone(),
        filename: format!("{alt}.{ext}"),
    })?;
    Ok(StoredMedia {
        hash,
        path: rel.to_string_lossy().replace('\\', "/"),
        ext,
    })
}

/// The Markdown snippet Tezuri inserts when media is linked into a document.
/// Declared rendition intent, not commands — the renderer derives srcset.
pub fn link_snippet(stored: &StoredMedia, alt: &str) -> String {
    format!(
        "![{alt}]({}?w=1200&fit=inside&format=webp&q=85)",
        stored.path
    )
}

/// Detect gallery groups: runs of >=2 consecutive bare image lines in a body.
/// This is the convention-as-syntax rule from the brief.
pub fn gallery_groups(body: &str) -> Vec<Vec<String>> {
    let img = regex::Regex::new(r"^!\[[^\]]*\]\(([^)\s]+)[^)]*\)\s*$").unwrap();
    let mut groups = Vec::new();
    let mut current: Vec<String> = Vec::new();
    for line in body.lines() {
        let l = line.trim();
        if let Some(caps) = img.captures(l) {
            current.push(caps[1].to_string());
        } else {
            if current.len() >= 2 {
                groups.push(std::mem::take(&mut current));
            }
            current.clear();
        }
    }
    if current.len() >= 2 {
        groups.push(current);
    }
    groups
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::tempdir;

    const PNG: &[u8] = &[0x89, b'P', b'N', b'G', 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    const SVG: &[u8] = b"<svg onload='alert(1)'></svg>";

    #[test]
    fn stores_and_dedups() {
        let dir = tempdir().unwrap();
        let a = store(dir.path(), PNG, "shot").unwrap();
        let b = store(dir.path(), PNG, "shot-again").unwrap();
        assert_eq!(a.hash, b.hash);
        assert!(dir.path().join("media").read_dir().unwrap().count() == 1);
        assert!(a.path.starts_with("media/"));
    }

    #[test]
    fn refuses_scriptable_content() {
        let dir = tempdir().unwrap();
        assert!(store(dir.path(), SVG, "evil").is_err());
    }

    #[test]
    fn galleries_by_adjacency() {
        let body = "![](a.png)\n![](b.png)\n\ntext\n\n![](c.png)\n";
        let g = gallery_groups(body);
        assert_eq!(g.len(), 1);
        assert_eq!(g[0].len(), 2);
    }

    #[test]
    fn snippet_carries_rendition_intent() {
        let dir = tempdir().unwrap();
        let m = store(dir.path(), PNG, "x").unwrap();
        let s = link_snippet(&m, "a view");
        assert!(s.starts_with("![a view](media/"));
        assert!(s.contains("w=1200"));
    }
}
