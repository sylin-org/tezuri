//! Articles: the document is prose; metadata is a sibling record.
//!
//! `articles/<slug>/article.md` — H1 title, optional standfirst line, body.
//! One Markdown flow, nothing else. `articles/<slug>/meta.yaml` — state,
//! date, tags, cover reference, provenance. Content and data never mix.
//!
//! Title is derived from the first `# ` heading; standfirst from the first
//! block after it when it is a standalone `_…_` line.

use crate::spine::{atomic_write, confine, content_hash, Event, Journal};
use anyhow::{Context, Result};
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::Path;

#[derive(Debug, Clone, Copy, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "lowercase")]
pub enum State {
    Draft,
    Review,
    Published,
}

impl State {
    pub fn as_str(&self) -> &'static str {
        match self {
            State::Draft => "draft",
            State::Review => "review",
            State::Published => "published",
        }
    }
}

/// The sidecar record. Deliberately tiny; unknown fields survive via
/// serde's ignore-by-default and are preserved by surgical text handling
/// in save (we only rewrite keys we know).
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct ArticleMeta {
    pub slug: String,
    #[serde(default = "default_state")]
    pub state: State,
    #[serde(default)]
    pub date: Option<String>,
    #[serde(default)]
    pub tags: Vec<String>,
    /// Reference to a media base id used as the hero image.
    #[serde(default)]
    pub cover: Option<String>,
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

/// Split a document into (title, standfirst, body-start-offset).
/// Title = first `# ` heading. Standfirst = first block after it that is a
/// single-line `_…_`. Body starts right after whichever came last.
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
    // Skip one blank between title and possible standfirst.
    if let Some(l) = lines.peek() {
        if l.trim().is_empty() {
            lines.next();
        }
    }
    if let Some(l) = lines.peek() {
        let trimmed = l.trim();
        if trimmed.starts_with('_') && trimmed.ends_with('_') && trimmed.len() > 2 {
            standfirst = Some(trimmed[1..trimmed.len() - 1].to_string());
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

impl Article {
    pub fn dir(publication_root: &Path, slug: &str) -> Result<std::path::PathBuf> {
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
                state: State::Draft,
                date: None,
                tags: vec![],
                cover: None,
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
        if meta_path.exists() {
            if let Ok(existing) =
                serde_yaml::from_str::<serde_yaml::Value>(&fs::read_to_string(&meta_path)?)
            {
                if let Some(map) = existing.as_mapping() {
                    for (k, v) in map {
                        let key = k.as_str().unwrap_or_default().to_string();
                        if !matches!(
                            key.as_str(),
                            "slug" | "state" | "date" | "tags" | "cover" | "standfirst"
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

    /// Create a fresh article with conventional defaults.
    pub fn create(publication_root: &Path, slug: &str, title: &str) -> Result<Article> {
        let today = chrono::Utc::now().format("%Y-%m-%d").to_string();
        let a = Article {
            meta: ArticleMeta {
                slug: slug.to_string(),
                state: State::Draft,
                date: Some(today),
                tags: vec![],
                cover: None,
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
    fn parses_title_and_standfirst() {
        let (title, sf) = parse_flow(SAMPLE);
        assert_eq!(title.as_deref(), Some("On Rust"));
        assert_eq!(sf.as_deref(), Some("A meditation on ownership."));
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
        assert_eq!(b.standfirst().as_deref(), Some("It begins."));

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
        a.meta.state = State::Review;
        a.save(dir.path()).unwrap();

        let after = fs::read_to_string(&meta_path).unwrap();
        assert!(after.contains("source-url"));
        assert!(after.contains("custom"));
        assert!(after.contains("state: review"));
    }
}
