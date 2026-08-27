//! Media: content-addressed images under the publication's identity dialect.
//!
//! Every stored image gets one `{uuidv7}-{plug}.{ext}` original inside
//! `media/`; identical content reuses the identical file and id, always.
//! Renditions are declared intent resolved at display time (`thumb`, `hero`,
//! quantized widths). Adjacent bare image lines collapse into a gallery by
//! convention. Nothing that could carry script is ever accepted as an image.

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
    pub path: String, // publication-relative, never carries a recipe
    pub ext: &'static str,
}

/// Display policy: editors reference images at the reading measure (~720px
/// logical). High-DPI viewports want ~2x backing pixels, so the display
/// rendition target is 1600px wide; the original file is kept untouched as
/// the archival source. v1 records the intent in the journal and emits the
/// original path — derivation lands with the rendition pipeline.
pub const DISPLAY_MAX_WIDTH: u32 = 1600;

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

/// Store dropped/pasted image bytes under a fresh `{uuid7}-{plug}.{ext}`
/// identity. Identical content reuses the existing file and id: storing is
/// naturally idempotent, no dedup protocol needed.
pub fn store_identified(
    publication_root: &Path,
    bytes: &[u8],
    original_name: &str,
) -> Result<StoredMedia> {
    let ext =
        sniff_image(bytes).context("that file is not a real image (PNG, JPEG, WebP or GIF)")?;
    if bytes.len() > 25 * 1024 * 1024 {
        bail!("image is larger than 25 MB");
    }
    let hash = content_hash(bytes);

    // Dedup scan over existing originals. Cheap prefilter first: only files
    // whose size matches are read at all.
    let media_dir = confine(publication_root, Path::new("media"))?;
    if media_dir.is_dir() {
        for entry in fs::read_dir(&media_dir)? {
            let p = entry?.path();
            let same_size = p
                .metadata()
                .map(|m| m.len() as usize == bytes.len())
                .unwrap_or(false);
            if !p.is_file() || !same_size {
                continue;
            }
            let name = p
                .file_name()
                .and_then(|s| s.to_str())
                .unwrap_or_default()
                .to_string();
            // Only originals carry no recipe suffix (plugs never contain '_').
            if name.contains('_') {
                continue;
            }
            if let Ok(existing) = fs::read(&p) {
                if content_hash(&existing) == hash {
                    let (id, _) = crate::renditions::split_recipe(&name)
                        .context("stored media has an unrecognized name")?;
                    return Ok(StoredMedia {
                        hash,
                        path: format!("media/{}", id.base_filename()),
                        ext: id.ext.leak() as &'static str,
                    });
                }
            }
        }
    }

    let mut id = crate::renditions::MediaId::new(original_name);
    id.ext = ext.into();
    let rel = Path::new("media").join(id.base_filename());
    let target = confine(publication_root, &rel)?;
    atomic_write(&target, bytes)?;
    Journal::open(publication_root)?.record(Event::MediaStored {
        hash: hash.clone(),
        filename: id.base_filename(),
    })?;
    Ok(StoredMedia {
        hash,
        path: rel.to_string_lossy().replace('\\', "/"),
        ext,
    })
}

/// The stable base reference for documents (never carries a recipe).
pub fn base_ref(stored: &StoredMedia) -> String {
    stored.path.clone()
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::tempdir;

    const PNG: &[u8] = &[0x89, b'P', b'N', b'G', 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    const SVG: &[u8] = b"<svg onload='alert(1)'></svg>";

    #[test]
    fn stores_under_identity_and_dedups_by_content() {
        let dir = tempdir().unwrap();
        let a = store_identified(dir.path(), PNG, "Wedding Shot 01.png").unwrap();
        assert!(a.path.starts_with("media/"));
        assert!(a.path.ends_with(".png"));
        assert!(a.path.contains('-'));

        let b = store_identified(dir.path(), PNG, "renamed-differently.jpg").unwrap();
        assert_eq!(a.path, b.path, "identical content must reuse the file");
        assert!(dir.path().join("media").read_dir().unwrap().count() == 1);

        let c = store_identified(dir.path(), &[0xFF, 0xD8, 0xFF], "other.jpg").unwrap();
        assert_ne!(a.hash, c.hash);
        assert!(dir.path().join("media").read_dir().unwrap().count() == 2);
    }

    #[test]
    fn refuses_scriptable_content() {
        let dir = tempdir().unwrap();
        assert!(store_identified(dir.path(), SVG, "evil").is_err());
    }

    #[test]
    fn refuses_oversized_images() {
        let dir = tempdir().unwrap();
        let mut big = vec![0x89u8, b'P', b'N', b'G'];
        big.resize(26 * 1024 * 1024, 0);
        assert!(store_identified(dir.path(), &big, "huge.png").is_err());
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
        let m = store_identified(dir.path(), PNG, "x").unwrap();
        let s = link_snippet(&m, "a view");
        assert!(s.starts_with("![a view](media/"));
        assert!(s.contains("w=1200"));
    }
}
