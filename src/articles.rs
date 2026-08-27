//! Articles: the document is prose; metadata is a sibling record.
//!
//! `articles/<slug>/article.md` — H1 title, optional standfirst line, body.
//! One Markdown flow, nothing else. `articles/<slug>/meta.yaml` — state,
//! date, tags, cover reference, provenance. Content and data never mix.
//!
//! Title is derived from the first `# ` heading; the standfirst is the
//! first paragraph after it, positionally — dressing it differently is a
//! render-time concern (space Header Style), never markdown syntax.

use crate::spine::{atomic_write, confine, content_hash, Event, Journal};
use anyhow::{bail, Context, Result};
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::Path;

#[derive(Debug, Clone, Copy, Serialize, PartialEq)]
#[serde(rename_all = "lowercase")]
pub enum State {
    Draft,
    Published,
}

impl State {
    pub fn as_str(&self) -> &'static str {
        match self {
            State::Draft => "draft",
            State::Published => "published",
        }
    }
}

impl<'de> Deserialize<'de> for State {
    fn deserialize<D: serde::Deserializer<'de>>(d: D) -> Result<Self, D::Error> {
        let s = String::deserialize(d)?;
        // Legacy three-state files carried "review": those articles were
        // already in the render set, so they surface as published.
        Ok(match s.as_str() {
            "published" | "review" => State::Published,
            _ => State::Draft,
        })
    }
}

/// The sidecar record. Deliberately tiny; unknown fields survive via
/// serde's ignore-by-default and are preserved by surgical text handling
/// in save (we only rewrite keys we know).
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct ArticleMeta {
    pub slug: String,
    /// Stable uuidv7, minted at creation — survives slug renames.
    #[serde(default)]
    pub id: Option<String>,
    #[serde(default = "default_state")]
    pub state: State,
    #[serde(default)]
    pub date: Option<String>,
    #[serde(default)]
    pub tags: Vec<String>,
    /// Reference to a media base id used as the hero image.
    #[serde(default)]
    pub cover: Option<String>,
    /// Author override; the space byline is the fallback.
    #[serde(default)]
    pub author: Option<String>,
    #[serde(default)]
    pub standfirst: Option<String>,
    /// Provenance: where this article came from (e.g. substack-import).
    #[serde(flatten)]
    pub extra: std::collections::BTreeMap<String, serde_yaml::Value>,
}

fn default_state() -> State {
    State::Draft
}

#[derive(Debug, Clone, Serialize)]
pub struct Article {
    pub meta: ArticleMeta,
    /// The full document flow: H1 + standfirst line + body. This IS the file.
    pub document: String,
}

// ---------------------------------------------------------------------------
// Parsing the dialect.
// ---------------------------------------------------------------------------

/// Split a document into (title, standfirst). Title = first `# ` heading.
/// Standfirst = the first paragraph that follows it, positionally — no
/// special syntax. Whether that line is dressed as a standfirst or left as
/// ordinary flow is a space-level Header Style decision, never a property
/// of the markdown.
pub fn parse_flow(document: &str) -> (Option<String>, Option<String>) {
    let mut lines = document.lines().peekable();
    let mut title = None;
    let mut standfirst = None;

    // Skip leading blanks.
    while let Some(l) = lines.peek() {
        if l.trim().is_empty() {
            lines.next();
        } else {
            break;
        }
    }
    if let Some(l) = lines.peek() {
        if let Some(rest) = l.strip_prefix("# ") {
            title = Some(rest.trim().to_string());
            lines.next();
        }
    }
    // Skip blanks between title and the standfirst paragraph.
    while let Some(l) = lines.peek() {
        if l.trim().is_empty() {
            lines.next();
        } else {
            break;
        }
    }
    // The standfirst is the whole first paragraph: collect its lines until
    // the blank line that ends it.
    let mut paragraph: Vec<&str> = Vec::new();
    while let Some(l) = lines.peek() {
        if l.trim().is_empty() {
            break;
        }
        paragraph.push(l);
        lines.next();
    }
    if !paragraph.is_empty() {
        // A heading is never a standfirst.
        if !paragraph[0].trim_start().starts_with('#') {
            standfirst = Some(paragraph.join(" ").trim().to_string());
        }
    }
    (title, standfirst)
}

/// Derive the display title of a document, with fallback to the slug.
pub fn title_of(document: &str, slug: &str) -> String {
    parse_flow(document)
        .0
        .unwrap_or_else(|| slug.replace('-', " "))
}

/// A slug is the article's folder name and its identity everywhere. Keep it
/// boring on purpose: lowercase-kebab, one path component, bounded length.
pub fn validate_slug(slug: &str) -> Result<()> {
    let ok = !slug.is_empty()
        && slug.len() <= 80
        && slug
            .chars()
            .all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || c == '-')
        && !slug.starts_with('-')
        && !slug.ends_with('-')
        && !slug.contains("--");
    if ok {
        Ok(())
    } else {
        bail!(
            "invalid slug \"{slug}\": use lowercase letters, digits, and \
             single dashes (like \"on-rust\"), at most 80 characters"
        );
    }
}

impl Article {
    pub fn dir(publication_root: &Path, slug: &str) -> Result<std::path::PathBuf> {
        validate_slug(slug)?;
        confine(publication_root, &Path::new("articles").join(slug))
    }

    pub fn doc_path(publication_root: &Path, slug: &str) -> Result<std::path::PathBuf> {
        Ok(Self::dir(publication_root, slug)?.join("article.md"))
    }

    pub fn meta_path(publication_root: &Path, slug: &str) -> Result<std::path::PathBuf> {
        Ok(Self::dir(publication_root, slug)?.join("meta.yaml"))
    }

    pub fn load(publication_root: &Path, slug: &str) -> Result<Article> {
        let doc_path = Self::doc_path(publication_root, slug)?;
        let document =
            fs::read_to_string(&doc_path).with_context(|| format!("article not found: {slug}"))?;
        let meta_path = Self::meta_path(publication_root, slug)?;
        let meta: ArticleMeta = if meta_path.exists() {
            serde_yaml::from_str(&fs::read_to_string(&meta_path)?)
                .with_context(|| format!("malformed meta.yaml for {slug}"))?
        } else {
            ArticleMeta {
                slug: slug.to_string(),
                id: None,
                state: State::Draft,
                date: None,
                tags: vec![],
                cover: None,
                author: None,
                standfirst: None,
                extra: Default::default(),
            }
        };
        Ok(Article { meta, document })
    }

    pub fn title(&self) -> String {
        title_of(&self.document, &self.meta.slug)
    }

    pub fn standfirst(&self) -> Option<String> {
        parse_flow(&self.document)
            .1
            .or_else(|| self.meta.standfirst.clone())
    }

    /// Persist both files atomically and journal the write.
    pub fn save(&self, publication_root: &Path) -> Result<String> {
        let dir = Self::dir(publication_root, &self.meta.slug)?;
        fs::create_dir_all(&dir)?;
        let doc_path = dir.join("article.md");
        atomic_write(&doc_path, self.document.as_bytes())?;

        // Preserve unknown extra keys: merge our knowns over the existing file.
        let meta_path = dir.join("meta.yaml");
        let mut out_meta = self.meta.clone();
        // A stable id is part of the contract: legacy articles mint one the
        // first time they are saved.
        if out_meta.id.is_none() {
            out_meta.id = Some(uuid::Uuid::now_v7().to_string());
        }
        if meta_path.exists() {
            if let Ok(existing) =
                serde_yaml::from_str::<serde_yaml::Value>(&fs::read_to_string(&meta_path)?)
            {
                if let Some(map) = existing.as_mapping() {
                    for (k, v) in map {
                        let key = k.as_str().unwrap_or_default().to_string();
                        if !matches!(
                            key.as_str(),
                            "slug"
                                | "id"
                                | "state"
                                | "date"
                                | "tags"
                                | "cover"
                                | "author"
                                | "standfirst"
                        ) {
                            out_meta.extra.entry(key).or_insert_with(|| v.clone());
                        }
                    }
                }
            }
        }
        let yaml = serde_yaml::to_string(&out_meta)?;
        atomic_write(&meta_path, yaml.as_bytes())?;

        let hash = content_hash(self.document.as_bytes());
        Journal::open(publication_root)?.record(Event::ArticleWritten {
            slug: self.meta.slug.clone(),
            content_hash: hash.clone(),
        })?;
        Ok(hash)
    }

    // -- the dirty copy ------------------------------------------------------
    //
    // Editing lands in `.tezuri/drafts/<slug>.md` — inside the publication,
    // outside the article folder, so the manuscript stays pristine until an
    // explicit Save. The dirty copy is user work, not a cache: it survives
    // restarts and is only cleared by a canonical save or an explicit
    // discard.

    pub fn dirty_path(publication_root: &Path, slug: &str) -> Result<std::path::PathBuf> {
        confine(
            publication_root,
            Path::new(".tezuri")
                .join("drafts")
                .join(format!("{slug}.md"))
                .as_path(),
        )
    }

    /// Persist the editing copy. Atomic, journaled as DraftSaved — the
    /// canonical article.md is never touched here.
    pub fn write_dirty(publication_root: &Path, slug: &str, document: &str) -> Result<()> {
        let p = Self::dirty_path(publication_root, slug)?;
        if let Some(parent) = p.parent() {
            fs::create_dir_all(parent)?;
        }
        atomic_write(&p, document.as_bytes())?;
        Journal::open(publication_root)?.record(Event::DraftSaved { slug: slug.into() })?;
        Ok(())
    }

    /// The editing copy, when unsaved edits exist.
    pub fn read_dirty(publication_root: &Path, slug: &str) -> Result<Option<String>> {
        let p = Self::dirty_path(publication_root, slug)?;
        if !p.exists() {
            return Ok(None);
        }
        Ok(Some(fs::read_to_string(&p)?))
    }

    /// The dirty copy is gone: either a canonical Save absorbed it or the
    /// author discarded it.
    pub fn clear_dirty(publication_root: &Path, slug: &str) -> Result<()> {
        let p = Self::dirty_path(publication_root, slug)?;
        if p.exists() {
            fs::remove_file(&p)?;
        }
        Ok(())
    }

    /// Persist only the fact fields (meta.yaml). The document flow is
    /// never touched: facts are explicit form edits, content is Write.
    pub fn save_meta_only(&self, publication_root: &Path) -> Result<()> {
        let dir = Self::dir(publication_root, &self.meta.slug)?;
        fs::create_dir_all(&dir)?;
        let meta_path = Self::meta_path(publication_root, &self.meta.slug)?;
        let mut out_meta = self.meta.clone();
        if meta_path.exists() {
            if let Ok(existing) =
                serde_yaml::from_str::<serde_yaml::Value>(&fs::read_to_string(&meta_path)?)
            {
                if let Some(map) = existing.as_mapping() {
                    for (k, v) in map {
                        let key = k.as_str().unwrap_or_default().to_string();
                        if !matches!(
                            key.as_str(),
                            "slug"
                                | "id"
                                | "state"
                                | "date"
                                | "tags"
                                | "cover"
                                | "author"
                                | "standfirst"
                        ) {
                            out_meta.extra.entry(key).or_insert_with(|| v.clone());
                        }
                    }
                }
            }
        }
        if out_meta.id.is_none() {
            out_meta.id = Some(uuid::Uuid::now_v7().to_string());
        }
        let yaml = serde_yaml::to_string(&out_meta)?;
        atomic_write(&meta_path, yaml.as_bytes())?;
        Journal::open(publication_root)?.record(Event::ArticleWritten {
            slug: self.meta.slug.clone(),
            content_hash: String::new(),
        })?;
        Ok(())
    }

    /// Create a fresh article with conventional defaults.
    pub fn create(publication_root: &Path, slug: &str, title: &str) -> Result<Article> {
        let today = chrono::Utc::now().format("%Y-%m-%d").to_string();
        let a = Article {
            meta: ArticleMeta {
                slug: slug.to_string(),
                id: Some(uuid::Uuid::now_v7().to_string()),
                state: State::Draft,
                date: Some(today),
                tags: vec![],
                cover: None,
                author: None,
                standfirst: None,
                extra: Default::default(),
            },
            document: format!("# {title}\n\n"),
        };
        a.save(publication_root)?;
        Ok(a)
    }

    /// Wiki-links for the desk graph.
    pub fn links(&self) -> Vec<String> {
        let re = regex::Regex::new(r"\[\[([^\]]+)\]\]").unwrap();
        re.captures_iter(&self.document)
            .map(|c| c[1].to_string())
            .collect()
    }

    pub fn word_count(&self) -> usize {
        self.document.split_whitespace().count()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::tempdir;

    const SAMPLE: &str = "# On Rust\n\n_A meditation on ownership._\n\nHello world.\n";

    #[test]
    fn slugs_are_boring_by_design() {
        assert!(validate_slug("on-rust").is_ok());
        assert!(validate_slug("post2026").is_ok());
        for bad in [
            "",
            "UPPER",
            "a/b",
            "../escape",
            "-lead",
            "trail-",
            "dou--ble",
            "under_score",
            "space inside",
            "spàce",
        ] {
            assert!(validate_slug(bad).is_err(), "expected refusal: {bad:?}");
        }
        // The path gate must flow through dir()/load()/create(), not just
        // this validator.
        let dir = tempdir().unwrap();
        assert!(Article::dir(dir.path(), "a/b").is_err());
    }

    #[test]
    fn parses_title_and_standfirst() {
        let (title, sf) = parse_flow(SAMPLE);
        assert_eq!(title.as_deref(), Some("On Rust"));
        assert_eq!(sf.as_deref(), Some("_A meditation on ownership._"));
    }

    #[test]
    fn load_save_roundtrip_with_sidecar() {
        let dir = tempdir().unwrap();
        Article::create(dir.path(), "fresh", "Fresh Thoughts").unwrap();
        let mut a = Article::load(dir.path(), "fresh").unwrap();
        assert_eq!(a.title(), "Fresh Thoughts");

        a.meta.state = State::Published;
        a.meta.tags = vec!["rust".into()];
        a.document = "# Fresh Thoughts\n\n_It begins._\n\nBody here.\n".to_string();
        a.save(dir.path()).unwrap();

        let b = Article::load(dir.path(), "fresh").unwrap();
        assert_eq!(b.meta.state, State::Published);
        assert_eq!(b.meta.tags, vec!["rust".to_string()]);
        assert_eq!(b.standfirst().as_deref(), Some("_It begins._"));

        // meta.yaml exists as a sibling and holds no prose
        let meta_text =
            fs::read_to_string(Article::meta_path(dir.path(), "fresh").unwrap()).unwrap();
        assert!(meta_text.contains("state: published"));
        assert!(!meta_text.contains("Body here"));
    }

    #[test]
    fn unknown_extra_keys_survive_save() {
        let dir = tempdir().unwrap();
        Article::create(dir.path(), "x", "X").unwrap();
        let meta_path = Article::meta_path(dir.path(), "x").unwrap();
        fs::write(
            &meta_path,
            "slug: x\nsource-url: https://example.com\ncustom:\n  nested: 1\n",
        )
        .unwrap();

        let mut a = Article::load(dir.path(), "x").unwrap();
        a.meta.state = State::Published;
        a.save(dir.path()).unwrap();

        let after = fs::read_to_string(&meta_path).unwrap();
        assert!(after.contains("source-url"));
        assert!(after.contains("custom"));
        assert!(after.contains("state: published"));
    }

    #[test]
    fn legacy_review_state_surfaces_as_published() {
        let dir = tempdir().unwrap();
        Article::create(dir.path(), "old", "Old").unwrap();
        let meta_path = Article::meta_path(dir.path(), "old").unwrap();
        fs::write(&meta_path, "slug: old\nstate: review\n").unwrap();

        let a = Article::load(dir.path(), "old").unwrap();
        assert_eq!(a.meta.state, State::Published);
    }

    #[test]
    fn legacy_articles_gain_an_id_on_first_save() {
        let dir = tempdir().unwrap();
        Article::create(dir.path(), "heritage", "Heritage").unwrap();
        let meta_path = Article::meta_path(dir.path(), "heritage").unwrap();
        // A pre-uuid sidecar: no id, and the document carries the frame.
        fs::write(
            &meta_path,
            "slug: heritage\nstate: published\ndate: 2024-01-01\n",
        )
        .unwrap();

        let mut a = Article::load(dir.path(), "heritage").unwrap();
        assert!(a.meta.id.is_none());
        a.meta.author = Some("Guest Hand".into());
        a.save(dir.path()).unwrap();

        let b = Article::load(dir.path(), "heritage").unwrap();
        let id = b.meta.id.as_deref().expect("id minted on save");
        assert_eq!(id.len(), 36, "uuidv7 shape");
        assert_eq!(b.meta.author.as_deref(), Some("Guest Hand"));
    }

    #[test]
    fn ids_are_stable_across_saves() {
        let dir = tempdir().unwrap();
        Article::create(dir.path(), "stable", "Stable").unwrap();
        let first = Article::load(dir.path(), "stable").unwrap().meta.id;
        let mut a = Article::load(dir.path(), "stable").unwrap();
        a.document = "# Stable\n\nChanged.\n".into();
        a.save(dir.path()).unwrap();
        assert_eq!(first, a.meta.id);
    }
}
