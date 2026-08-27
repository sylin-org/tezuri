//! Publication identity: the space's own characteristics file.
//!
//! `publication.yaml` lives inside the publication and carries what makes the
//! space itself: display name, byline, persona, plus anything the author
//! keeps there that Tezuri does not model. Same contract as `meta.yaml`:
//! modeled keys are rewritten on save, unknown keys survive verbatim. Files
//! are truth; the registry entry is a display cache, never the master.

use crate::spine::{atomic_write, confine, Event, Journal};
use anyhow::{Context, Result};
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::Path;

/// How article headers present across the space.
#[derive(Debug, Clone, Copy, PartialEq)]
pub enum HeaderStyle {
    /// No alterations: H1 and the first line are ordinary flow content.
    Normal,
    /// Title + standfirst feed the template's hero and leave the flow.
    Banner,
}

/// The modeled keys. Everything else in the file is preserved untouched.
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct Identity {
    /// Display name of the space ("Kintsugi").
    #[serde(default)]
    pub name: String,
    /// Byline as readers see it ("words and photographs by …").
    #[serde(default)]
    pub byline: String,
    /// Persona writing here; the firewall's subject.
    #[serde(default)]
    pub persona: String,
    /// Space cover image — a media base reference inside this space.
    #[serde(default)]
    pub cover: Option<String>,
    /// How article headers present: `normal` keeps the raw flow; `banner`
    /// consumes title + standfirst into the template's hero.
    #[serde(default)]
    pub header_style: String,
    /// Whether the tag system participates in this space at all.
    #[serde(default = "default_true")]
    pub tags_enabled: bool,
    /// The curated vocabulary offered when tags are enabled.
    #[serde(default)]
    pub tag_vocabulary: Vec<String>,
    /// Unknown keys the author keeps; rewritten byte-identical.
    #[serde(flatten)]
    pub extra: std::collections::BTreeMap<String, serde_yaml::Value>,
}

fn default_true() -> bool {
    true
}

impl Default for Identity {
    fn default() -> Self {
        Self {
            name: String::new(),
            byline: String::new(),
            persona: String::new(),
            cover: None,
            header_style: String::new(),
            tags_enabled: true,
            tag_vocabulary: Vec::new(),
            extra: Default::default(),
        }
    }
}

impl Identity {
    pub fn header_style(&self) -> HeaderStyle {
        match self.header_style.as_str() {
            "banner" => HeaderStyle::Banner,
            _ => HeaderStyle::Normal,
        }
    }
    pub fn path(publication_root: &Path) -> Result<std::path::PathBuf> {
        confine(publication_root, Path::new("publication.yaml"))
    }

    /// Load, falling back to a sensible default identity when absent.
    pub fn load(publication_root: &Path) -> Result<Identity> {
        let p = Self::path(publication_root)?;
        if !p.exists() {
            return Ok(Identity::default());
        }
        let text = fs::read_to_string(&p)?;
        let id: Identity = serde_yaml::from_str(&text).with_context(|| {
            format!(
                "malformed publication.yaml in {}",
                publication_root.display()
            )
        })?;
        Ok(id)
    }

    /// Persist, merging knowns over the existing file so unknown keys survive.
    pub fn save(&self, publication_root: &Path) -> Result<()> {
        let mut out = self.clone();
        let p = Self::path(publication_root)?;
        if p.exists() {
            if let Ok(existing) =
                serde_yaml::from_str::<serde_yaml::Value>(&fs::read_to_string(&p)?)
            {
                if let Some(map) = existing.as_mapping() {
                    for (k, v) in map {
                        let key = k.as_str().unwrap_or_default().to_string();
                        let modeled = matches!(
                            key.as_str(),
                            "name"
                                | "byline"
                                | "persona"
                                | "cover"
                                | "header_style"
                                | "tags_enabled"
                                | "tag_vocabulary"
                        );
                        if !modeled {
                            out.extra.entry(key).or_insert_with(|| v.clone());
                        }
                    }
                }
            }
        }
        let yaml = serde_yaml::to_string(&out)?;
        atomic_write(&p, yaml.as_bytes())?;
        Journal::open(publication_root)?.record(Event::IdentityWritten)?;
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::tempdir;

    #[test]
    fn absent_file_gives_default() {
        let dir = tempdir().unwrap();
        let id = Identity::load(dir.path()).unwrap();
        assert_eq!(id.name, "");
    }

    #[test]
    fn unknown_keys_survive_save() {
        let dir = tempdir().unwrap();
        atomic_write(
            &Identity::path(dir.path()).unwrap(),
            b"name: Kintsugi\npersona: L.\ncustom-note: keep me\n",
        )
        .unwrap();

        let mut id = Identity::load(dir.path()).unwrap();
        id.persona = "L. Botinelly".into();
        id.save(dir.path()).unwrap();

        let text = fs::read_to_string(Identity::path(dir.path()).unwrap()).unwrap();
        assert!(text.contains("custom-note: keep me"));
        assert!(text.contains("persona: L. Botinelly"));
    }

    #[test]
    fn roundtrip_via_load() {
        let dir = tempdir().unwrap();
        let id = Identity {
            name: "Field Notes".into(),
            byline: "written afield".into(),
            ..Default::default()
        };
        id.save(dir.path()).unwrap();
        let loaded = Identity::load(dir.path()).unwrap();
        assert_eq!(loaded, id);
    }

    #[test]
    fn presentation_settings_roundtrip_with_defaults() {
        let dir = tempdir().unwrap();
        let id = Identity::load(dir.path()).unwrap();
        assert_eq!(id.header_style(), HeaderStyle::Normal);
        assert!(id.tags_enabled, "tags participate by default");
        assert!(id.tag_vocabulary.is_empty());

        let id = Identity {
            name: "GPosingway".into(),
            cover: Some("media/hero.png".into()),
            header_style: "banner".into(),
            tags_enabled: true,
            tag_vocabulary: vec!["guide".into(), "gpose".into()],
            ..Default::default()
        };
        id.save(dir.path()).unwrap();
        let loaded = Identity::load(dir.path()).unwrap();
        assert_eq!(loaded.header_style(), HeaderStyle::Banner);
        assert_eq!(loaded.cover.as_deref(), Some("media/hero.png"));
        assert_eq!(loaded.tag_vocabulary.len(), 2);
    }
}
