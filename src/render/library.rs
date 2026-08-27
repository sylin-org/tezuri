//! The asset library: official themes and templates, user downloads, and
//! the per-space selection history.
//!
//! Two sources feed the picker. **Official** assets ship embedded in the
//! binary with a manifest (name, creator). **Downloaded** assets arrive
//! only by an explicit user command, are bounded in time and size, and
//! live under the app-state home (`~/.tezuri/downloads/`), never inside a
//! publication. Either way, applying an asset copies its bytes into the
//! space's own files — the space owns the result; the library only
//! remembers where it came from.
//!
//! History: the last ten applied selections per space per kind (theme,
//! template) are snapshotted with their bytes in app state. Stepping back
//! and forward re-applies; the canonical bytes always live in the space,
//! so losing this file costs nothing but the ability to revert.

use crate::render::{write_template, write_theme};
use crate::spine::{atomic_write, home};
use anyhow::{bail, Context, Result};
use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;
use std::io::Read;
use std::path::{Path, PathBuf};

const HISTORY_CAP: usize = 10;
const MAX_DOWNLOAD: usize = 2 * 1024 * 1024;

// -- official catalog -------------------------------------------------------

pub struct OfficialAsset {
    pub id: &'static str,
    pub name: &'static str,
    pub creator: &'static str,
    pub description: &'static str,
    pub bytes: &'static str,
}

/// Embedded themes. Vanilla is the calm built-in look; applying it clears
/// the space's theme.css (bytes are empty).
pub fn official_themes() -> Vec<OfficialAsset> {
    vec![
        OfficialAsset {
            id: "vanilla",
            name: "Vanilla",
            creator: "Tezuri",
            description: "The calm built-in look: GitHub-calm prose, no ornament.",
            bytes: "",
        },
        OfficialAsset {
            id: "garden",
            name: "Night garden",
            creator: "Tezuri",
            description: "Warm dark ground, serif prose, moonlit accents.",
            bytes: include_str!("../themes/garden.css"),
        },
        OfficialAsset {
            id: "sepia",
            name: "Sepia press",
            creator: "Tezuri",
            description: "Paper-warm surface, brown-black ink. Long sessions.",
            bytes: include_str!("../themes/sepia.css"),
        },
        OfficialAsset {
            id: "ink",
            name: "Working ink",
            creator: "Tezuri",
            description: "Maximum contrast, sans-serif, drafting-first.",
            bytes: include_str!("../themes/ink.css"),
        },
        OfficialAsset {
            id: "gposingway",
            name: "GPosingway",
            creator: "GPosingway",
            description: "The FFXIV photography look: warm dark hero, mono rails.",
            bytes: include_str!("../packs/gposingway/theme.css"),
        },
    ]
}

/// Embedded templates.
pub fn official_templates() -> Vec<OfficialAsset> {
    vec![
        OfficialAsset {
            id: "vanilla",
            name: "Vanilla",
            creator: "Tezuri",
            description: "One dumb slot over a calm baseline.",
            bytes: include_str!("../templates/article.html"),
        },
        OfficialAsset {
            id: "gposingway",
            name: "GPosingway",
            creator: "GPosingway",
            description: "Full-bleed hero, sticky TOC rail, two-column grid.",
            bytes: include_str!("../packs/gposingway/article.html"),
        },
    ]
}

// -- downloads --------------------------------------------------------------

fn downloads_dir(kind: &str) -> Result<PathBuf> {
    let base = home().ok_or_else(|| anyhow::anyhow!("no home directory"))?;
    Ok(base.join(".tezuri").join("downloads").join(kind))
}

fn safe_stem(name: &str) -> Result<String> {
    let safe: String = name
        .chars()
        .map(|c| c.to_ascii_lowercase())
        .map(|c| if c.is_ascii_alphanumeric() { c } else { '-' })
        .collect();
    let trimmed = safe.trim_matches('-');
    if trimmed.is_empty() {
        bail!("asset name is empty");
    }
    Ok(trimmed.to_string())
}

/// Store a user-fetched asset under the app-state home. Returns its id.
fn store_download(kind: &str, stem: &str, bytes: &[u8]) -> Result<String> {
    let dir = downloads_dir(kind)?;
    std::fs::create_dir_all(&dir)?;
    let ext = if kind == "themes" { "css" } else { "html" };
    let file = dir.join(format!("{stem}.{ext}"));
    atomic_write(&file, bytes)?;
    Ok(format!("downloaded:{kind}:{stem}"))
}

/// Fetch one asset over the network, on the user's explicit command.
/// Bounded in time and size; the URL must name a `.css` or `.html` file.
pub fn download_asset(url: &str) -> Result<String> {
    let lower = url.to_ascii_lowercase();
    let kind = if lower.ends_with(".css") {
        "themes"
    } else if lower.ends_with(".html") || lower.ends_with(".htm") {
        "templates"
    } else {
        bail!("the URL must point to a .css or .html file");
    };
    let response = ureq::get(url)
        .timeout(std::time::Duration::from_secs(15))
        .call()
        .map_err(|e| anyhow::anyhow!("download failed: {e}"))?;
    let mut bytes = Vec::new();
    response
        .into_reader()
        .take(MAX_DOWNLOAD as u64 + 1)
        .read_to_end(&mut bytes)?;
    if bytes.len() > MAX_DOWNLOAD {
        bail!("asset exceeds the 2 MB limit");
    }
    let stem = url
        .rsplit('/')
        .next()
        .unwrap_or("downloaded")
        .trim_end_matches(".css")
        .trim_end_matches(".html")
        .trim_end_matches(".htm");
    store_download(kind, &safe_stem(stem)?, &bytes)
}

// -- picker -----------------------------------------------------------------

#[derive(Debug, Clone, Serialize)]
pub struct PickerEntry {
    pub id: String,
    pub kind: String,   // "theme" | "template"
    pub source: String, // "official" | "downloaded"
    pub name: String,
    pub creator: String,
    pub description: String,
}

fn downloaded_entries(kind: &str) -> Result<Vec<PickerEntry>> {
    let mut out = Vec::new();
    let dir = downloads_dir(kind)?;
    if dir.exists() {
        for f in std::fs::read_dir(&dir)? {
            let path = f?.path();
            let stem = path
                .file_stem()
                .and_then(|s| s.to_str())
                .unwrap_or_default()
                .to_string();
            if stem.is_empty() {
                continue;
            }
            out.push(PickerEntry {
                id: format!("downloaded:{kind}:{stem}"),
                kind: kind.trim_end_matches('s').into(),
                source: "downloaded".into(),
                name: stem,
                creator: "downloaded".into(),
                description: path.to_string_lossy().to_string(),
            });
        }
    }
    Ok(out)
}

/// Everything the picker offers: official first, downloads after.
/// Returns (themes, templates).
pub fn picker_list() -> Result<(Vec<PickerEntry>, Vec<PickerEntry>)> {
    let entry = |kind: &str, a: &OfficialAsset| PickerEntry {
        id: format!("official:{}", a.id),
        kind: kind.into(),
        source: "official".into(),
        name: a.name.into(),
        creator: a.creator.into(),
        description: a.description.into(),
    };
    let mut themes: Vec<PickerEntry> = official_themes()
        .iter()
        .map(|a| entry("theme", a))
        .collect();
    themes.append(&mut downloaded_entries("themes")?);
    let mut templates: Vec<PickerEntry> = official_templates()
        .iter()
        .map(|a| entry("template", a))
        .collect();
    templates.append(&mut downloaded_entries("templates")?);
    Ok((themes, templates))
}

fn theme_bytes(id: &str) -> Result<Vec<u8>> {
    if let Some(rest) = id.strip_prefix("official:") {
        return Ok(official_themes()
            .into_iter()
            .find(|a| a.id == rest)
            .ok_or_else(|| anyhow::anyhow!("unknown theme: {rest}"))?
            .bytes
            .as_bytes()
            .to_vec());
    }
    if let Some(rest) = id.strip_prefix("downloaded:themes:") {
        let p = downloads_dir("themes")?.join(format!("{rest}.css"));
        return std::fs::read(&p).context("downloaded theme is missing");
    }
    bail!("unknown theme id: {id}")
}

fn template_bytes(id: &str) -> Result<Vec<u8>> {
    if let Some(rest) = id.strip_prefix("official:") {
        return Ok(official_templates()
            .into_iter()
            .find(|a| a.id == rest)
            .ok_or_else(|| anyhow::anyhow!("unknown template: {rest}"))?
            .bytes
            .as_bytes()
            .to_vec());
    }
    if let Some(rest) = id.strip_prefix("downloaded:templates:") {
        let p = downloads_dir("templates")?.join(format!("{rest}.html"));
        return std::fs::read(&p).context("downloaded template is missing");
    }
    bail!("unknown template id: {id}")
}

/// Write applied asset bytes into the space's own files (journaled).
fn write_applied(publication_root: &Path, kind: &str, bytes: &[u8]) -> Result<()> {
    let text = std::str::from_utf8(bytes).context("asset is not valid UTF-8")?;
    match kind {
        "theme" => write_theme(publication_root, text)?,
        _ => write_template(publication_root, text)?,
    }
    Ok(())
}

// -- history ----------------------------------------------------------------

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HistoryEntry {
    pub source_id: String,
    pub applied_at: String,
    pub bytes: String,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct SpaceHistory {
    pub theme: Vec<HistoryEntry>,
    pub theme_pos: i64,
    pub template: Vec<HistoryEntry>,
    pub template_pos: i64,
}

type HistoryFile = BTreeMap<String, SpaceHistory>;

fn history_file() -> Result<PathBuf> {
    let base = home().ok_or_else(|| anyhow::anyhow!("no home directory"))?;
    Ok(base.join(".tezuri").join("presentation-history.json"))
}

fn history_load() -> Result<HistoryFile> {
    let p = history_file()?;
    if !p.exists() {
        return Ok(BTreeMap::new());
    }
    Ok(serde_json::from_str(&std::fs::read_to_string(&p)?).unwrap_or_default())
}

fn history_save(file: &HistoryFile) -> Result<()> {
    let p = history_file()?;
    if let Some(parent) = p.parent() {
        std::fs::create_dir_all(parent)?;
    }
    atomic_write(&p, serde_json::to_string_pretty(file)?.as_bytes())?;
    Ok(())
}

/// Apply an asset from the library into the space and record it. Applying
/// truncates any redo tail and becomes the ring's newest entry.
pub fn picker_apply(publication_root: &Path, kind: &str, id: &str) -> Result<String> {
    let bytes = if kind == "theme" {
        theme_bytes(id)?
    } else {
        template_bytes(id)?
    };
    write_applied(publication_root, kind, &bytes)?;

    let mut file = history_load()?;
    let key = publication_root.to_string_lossy().to_string();
    let space = file.entry(key).or_default();
    let (ring, pos) = if kind == "theme" {
        (&mut space.theme, &mut space.theme_pos)
    } else {
        (&mut space.template, &mut space.template_pos)
    };
    ring.truncate((*pos).clamp(0, ring.len() as i64) as usize);
    ring.push(HistoryEntry {
        source_id: id.to_string(),
        applied_at: chrono::Utc::now().to_rfc3339(),
        bytes: String::from_utf8_lossy(&bytes).to_string(),
    });
    if ring.len() > HISTORY_CAP {
        let excess = ring.len() - HISTORY_CAP;
        ring.drain(0..excess);
    }
    *pos = ring.len() as i64 - 1;
    history_save(&file)?;
    Ok(kind.to_string())
}

/// Step the history ring for one kind (-1 back, +1 forward) and re-apply
/// the selection at the new position. Returns the new position.
pub fn picker_history_step(publication_root: &Path, kind: &str, delta: i32) -> Result<i64> {
    let mut file = history_load()?;
    let key = publication_root.to_string_lossy().to_string();
    let space = file
        .get_mut(&key)
        .ok_or_else(|| anyhow::anyhow!("no history for this space yet"))?;
    let (ring, pos) = if kind == "theme" {
        (&space.theme, &mut space.theme_pos)
    } else {
        (&space.template, &mut space.template_pos)
    };
    if ring.is_empty() {
        bail!("no history yet");
    }
    let next = (*pos + delta as i64).clamp(0, ring.len() as i64 - 1);
    if next == *pos {
        return Ok(*pos); // already at that end of the ring
    }
    *pos = next;
    let entry = &ring[next as usize];
    let bytes = entry.bytes.clone();
    let bytes = bytes.as_bytes();
    write_applied(publication_root, kind, bytes)?;
    history_save(&file)?;
    Ok(next)
}

/// The current history depth for both kinds, for the picker's arrows.
pub fn picker_history(publication_root: &Path) -> Result<(usize, usize)> {
    let file = history_load()?;
    let key = publication_root.to_string_lossy().to_string();
    let space = file.get(&key);
    Ok((
        space.map(|s| s.theme.len()).unwrap_or(0),
        space.map(|s| s.template.len()).unwrap_or(0),
    ))
}
