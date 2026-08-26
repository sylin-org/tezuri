//! Media identity: `{uuidv7}-{plug}[_{recipe}].{ext}`.
//!
//! The original is archival and never modified. Renditions are derived
//! lazily from the original, are disposable, and are named by recipe:
//! `thumb`, `hero`, or a quantized width (`640` `800` `1024` `1600` `2048`).
//! Documents reference only the base ID — rendition selection is a display
//! concern, never embedded in content.

use anyhow::{bail, Context, Result};
use std::path::{Path, PathBuf};

/// Widths recognized as recipes. Anything else numeric is rejected so typos
/// don't silently generate junk files.
pub const WIDTH_RECIPES: [u32; 5] = [640, 800, 1024, 1600, 2048];

#[derive(Debug, Clone, PartialEq)]
pub enum Recipe {
    Original,
    Thumb,
    Hero,
    Width(u32),
}

impl Recipe {
    pub fn suffix(&self) -> String {
        match self {
            Recipe::Original => String::new(),
            Recipe::Thumb => "_thumb".into(),
            Recipe::Hero => "_hero".into(),
            Recipe::Width(w) => format!("_{w}"),
        }
    }

    pub fn target_width(&self, original_w: u32) -> u32 {
        match self {
            Recipe::Original => original_w,
            Recipe::Thumb => 320,
            Recipe::Hero => 2400,
            Recipe::Width(w) => (*w).min(original_w),
        }
    }

    pub fn parse(s: &str) -> Option<Recipe> {
        match s {
            "" | "original" => Some(Recipe::Original),
            "thumb" => Some(Recipe::Thumb),
            "hero" => Some(Recipe::Hero),
            other => other
                .parse::<u32>()
                .ok()
                .filter(|w| WIDTH_RECIPES.contains(w))
                .map(Recipe::Width),
        }
    }
}

/// Sanitize an original filename into a plug: lowercase, alphanumerics and
/// dashes, collapsed, trimmed to 48 chars. The plug is for humans; uniqueness
/// comes from the UUID, not the plug.
pub fn plug_of(original_name: &str) -> String {
    let stem = Path::new(original_name)
        .file_stem()
        .and_then(|s| s.to_str())
        .unwrap_or("image");
    let mut plug = String::new();
    let mut last_dash = false;
    for c in stem.chars() {
        if c.is_ascii_alphanumeric() {
            plug.push(c.to_ascii_lowercase());
            last_dash = false;
        } else if !last_dash && !plug.is_empty() {
            plug.push('-');
            last_dash = true;
        }
        if plug.len() >= 48 {
            break;
        }
    }
    let trimmed = plug.trim_end_matches('-').to_string();
    if trimmed.is_empty() {
        "image".into()
    } else {
        trimmed
    }
}

/// A media identity in the publication's dialect.
#[derive(Debug, Clone)]
pub struct MediaId {
    pub uuid: uuid::Uuid,
    pub plug: String,
    pub ext: String,
}

impl MediaId {
    pub fn new(original_name: &str) -> Self {
        MediaId {
            uuid: uuid::Uuid::now_v7(),
            plug: plug_of(original_name),
            ext: "png".into(), // set precisely by sniffing at store time
        }
    }

    /// Base reference as it appears in documents: `{uuid}-{plug}.{ext}`
    pub fn base_filename(&self) -> String {
        format!("{}-{}.{}", self.uuid.simple(), self.plug, self.ext)
    }

    pub fn filename_for(&self, recipe: &Recipe) -> String {
        format!(
            "{}-{}{}.{}",
            self.uuid.simple(),
            self.plug,
            recipe.suffix(),
            self.ext
        )
    }

    /// Parse a base filename back (no recipe suffix allowed).
    pub fn parse_base(filename: &str) -> Option<MediaId> {
        let stem = Path::new(filename).extension()?.to_str()?; // guard
        let _ = stem;
        let (name, ext) = filename.rsplit_once('.')?;
        let (uuid_part, plug) = name.split_once('-')?;
        if uuid_part.len() != 32 || !uuid_part.chars().all(|c| c.is_ascii_hexdigit()) {
            return None;
        }
        let uuid = uuid::Uuid::parse_str(uuid_part).ok()?;
        Some(MediaId {
            uuid,
            plug: plug.to_string(),
            ext: ext.to_string(),
        })
    }
}

/// Split a stored filename into base id + optional recipe.
pub fn split_recipe(filename: &str) -> Option<(MediaId, Recipe)> {
    let (name, ext) = filename.rsplit_once('.')?;
    // Plugs never contain underscores (see plug_of), so an underscore in the
    // name always marks a recipe suffix. Try that first; fall back to base.
    if let Some(idx) = name.rfind('_') {
        let (base_name, recipe_s) = (&name[..idx], &name[idx + 1..]);
        if let Some(recipe) = Recipe::parse(recipe_s) {
            if let Some(id) = MediaId::parse_base(&format!("{base_name}.{ext}")) {
                return Some((id, recipe));
            }
        }
    }
    MediaId::parse_base(filename).map(|id| (id, Recipe::Original))
}

/// Derive a rendition from an original on disk. Idempotent: skips when the
/// rendition already exists (cache semantics), overwrites never.
pub fn derive_rendition(original: &Path, rendition: &Path, recipe: &Recipe) -> Result<()> {
    if rendition.exists() {
        return Ok(());
    }
    let img =
        image::open(original).with_context(|| format!("cannot decode {}", original.display()))?;
    let (ow, oh) = (img.width(), img.height());
    let tw = recipe.target_width(ow);
    let scaled = if tw < ow {
        let nh = ((oh as u64) * (tw as u64) / ow as u64).max(1) as u32;
        img.resize(tw, nh, image::imageops::FilterType::Lanczos3)
    } else {
        img
    };
    // Encode by extension of the rendition path.
    match rendition.extension().and_then(|e| e.to_str()).unwrap_or("") {
        "jpg" | "jpeg" => scaled.save_with_format(rendition, image::ImageFormat::Jpeg)?,
        "png" => scaled.save_with_format(rendition, image::ImageFormat::Png)?,
        "webp" => bail!("webp encoding is not available in v1; use jpg/png renditions"),
        other => bail!("unknown rendition format: {other}"),
    }
    Ok(())
}

/// Resolve a document reference (`media/<base>`) to the path that should be
/// displayed at a given recipe, deriving it if missing. Falls back to the
/// original when derivation fails or no shrink is needed.
pub fn resolve_for_display(
    publication_root: &Path,
    base_ref: &str,
    recipe: Recipe,
) -> Result<PathBuf> {
    let base_path = crate::spine::confine(publication_root, Path::new(base_ref))?;
    if matches!(recipe, Recipe::Original) || !base_path.exists() {
        return Ok(base_path);
    }
    let filename = base_path
        .file_name()
        .and_then(|s| s.to_str())
        .context("bad media ref")?;
    let (id, _) = split_recipe(filename).context("media ref is not a base id")?;

    // Renditions keep the original's container extension for now (jpg/png);
    // webp sources fall back to png renditions.
    let mut disp = id.clone();
    if disp.ext == "webp" {
        disp.ext = "png".into();
    } else if disp.ext == "gif" {
        return Ok(base_path); // animated: never resize
    }
    let rendition_rel = Path::new("media").join(disp.filename_for(&recipe));
    let rendition_path = crate::spine::confine(publication_root, &rendition_rel)?;
    derive_rendition(&base_path, &rendition_path, &recipe)?;
    Ok(rendition_path)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn plugs_are_sanitized() {
        assert_eq!(
            plug_of("My Wedding Suit (final).JPEG"),
            "my-wedding-suit-final"
        );
        assert_eq!(plug_of("!!!"), "image");
        assert_eq!(plug_of("").len(), 5); // "image"
    }

    #[test]
    fn filenames_roundtrip() {
        let id = MediaId {
            uuid: uuid::Uuid::now_v7(),
            plug: "suit".into(),
            ext: "jpg".into(),
        };
        let base = id.base_filename();
        let (parsed, recipe) = split_recipe(&base).unwrap();
        assert_eq!(recipe, Recipe::Original);
        assert_eq!(parsed.uuid, id.uuid);
        assert_eq!(parsed.plug, "suit");

        let rend = id.filename_for(&Recipe::Width(1024));
        let (_, r2) = split_recipe(&rend).unwrap();
        assert_eq!(r2, Recipe::Width(1024));
    }

    #[test]
    fn recipes_parse_narrowly() {
        assert_eq!(Recipe::parse("640"), Some(Recipe::Width(640)));
        assert_eq!(Recipe::parse("999"), None);
        assert_eq!(Recipe::parse("thumb"), Some(Recipe::Thumb));
    }

    #[test]
    fn derivation_produces_smaller_image() {
        let dir = tempfile::tempdir().unwrap();
        let orig = dir.path().join("0198c7a2aaaaaaaa78901234abcdefab_test.png");
        // 200x100 red PNG
        let img = image::RgbaImage::from_pixel(200, 100, image::Rgba([255, 0, 0, 255]));
        img.save(&orig).unwrap();

        let rend = dir
            .path()
            .join("0198c7a2aaaaaaaa78901234abcdefab_test_64.png");
        derive_rendition(&orig, &rend, &Recipe::Width(64)).unwrap();
        let out = image::open(&rend).unwrap();
        assert_eq!(out.width(), 64);
        // idempotent
        derive_rendition(&orig, &rend, &Recipe::Width(64)).unwrap();
    }
}
