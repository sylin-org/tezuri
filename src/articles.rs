//! Articles: the Markdown file is truth.
//!
//! An article is `articles/<slug>/index.md` — YAML frontmatter plus a
//! CommonMark body. Tezuri manages a small set of frontmatter keys and writes
//! them *surgically*: every other line of the file is preserved byte-exactly.
//! Unknown metadata survives untouched by construction, because it is never
//! parsed into a model at all — it is skipped text.

use crate::spine::{atomic_write, confine, content_hash, Event, Journal};
use anyhow::{Context, Result};
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::Path;

/// The keys Tezuri understands. Finite, on purpose.
pub const MANAGED_KEYS: [&str; 5] = ["title", "state", "date", "tags", "standfirst"];

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
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

#[derive(Debug, Clone, Serialize, serde::Deserialize)]
pub struct ArticleMeta {
    pub slug: String,
    pub title: String,
    pub state: State,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub date: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub tags: Option<Vec<String>>,
}

#[derive(Debug, Clone, Serialize)]
pub struct Article {
    pub meta: ArticleMeta,
    /// Raw body text (everything after the closing fence). Never round-tripped
    /// through an AST — what the author wrote is what this holds.
    pub body: String,
    /// Byte length of the original frontmatter block, for surgical writes.
    pub frontmatter_raw: Vec<String>,
}

/// Split a markdown file into (frontmatter lines, body).
fn split_document(text: &str) -> (Vec<String>, String) {
    let mut lines = text.lines();
    let first = lines.next().unwrap_or("");
    if first.trim() != "---" {
        return (vec![], text.to_string());
    }
    let mut fm = vec!["---".to_string()];
    let mut body_start = 1usize;
    for (i, l) in lines.enumerate() {
        if l.trim() == "---" {
            fm.push(l.to_string());
            body_start = i + 2;
            break;
        } else {
            fm.push(l.to_string());
        }
    }
    let rest: Vec<&str> = text.lines().skip(body_start).collect();
    let mut body = rest.join("\n");
    if text.ends_with('\n') {
        body.push('\n');
    }
    (fm, body)
}

fn get_fm<'a>(fm: &'a [String], key: &str) -> Option<String> {
    let prefix = format!("{key}:");
    fm.iter()
        .find(|l| l.starts_with(&prefix))
        .map(|l| l[prefix.len()..].trim().trim_matches('"').to_string())
}

/// Set a managed key in place if present; otherwise insert before the closing
/// fence. All other lines are moved verbatim — that is the whole trick.
fn set_fm(fm: &mut Vec<String>, key: &str, value: &str) {
    let prefix = format!("{key}:");
    let new_line = format!("{key}: {value}");
    if let Some(pos) = fm.iter().position(|l| l.starts_with(&prefix)) {
        fm[pos] = new_line;
    } else {
        // Insert before the closing fence (last "---").
        let insert_at = fm.len().saturating_sub(1);
        fm.insert(insert_at, new_line);
    }
}

impl Article {
    pub fn load(publication_root: &Path, slug: &str) -> Result<Article> {
        let path = confine(
            publication_root,
            &Path::new("articles").join(slug).join("index.md"),
        )?;
        let text =
            fs::read_to_string(&path).with_context(|| format!("article not found: {slug}"))?;
        Self::parse(slug, &text).with_context(|| format!("could not parse article: {slug}"))
    }

    pub fn parse(slug: &str, text: &str) -> Result<Article> {
        let (fm, body) = split_document(text);
        let title = get_fm(&fm, "title").unwrap_or_else(|| slug.to_string());
        let state = match get_fm(&fm, "state").as_deref() {
            Some("published") => State::Published,
            Some("review") => State::Review,
            _ => State::Draft,
        };
        let date = get_fm(&fm, "date");
        let tags =
            get_fm(&fm, "tags").map(|t| t.split(',').map(|s| s.trim().to_string()).collect());
        Ok(Article {
            meta: ArticleMeta {
                slug: slug.to_string(),
                title,
                state,
                date,
                tags,
            },
            body,
            frontmatter_raw: fm,
        })
    }

    /// Render the canonical file text: managed keys updated surgically,
    /// everything else byte-identical to what was loaded.
    fn render(&self) -> String {
        let mut fm = self.frontmatter_raw.clone();
        if fm.is_empty() {
            fm = vec!["---".into(), "---".into()];
        }
        set_fm(&mut fm, "title", &self.meta.title);
        set_fm(&mut fm, "state", self.meta.state.as_str());
        if let Some(d) = &self.meta.date {
            set_fm(&mut fm, "date", d);
        }
        if let Some(t) = &self.meta.tags {
            let v = t.join(", ");
            set_fm(&mut fm, "tags", &v);
        }
        format!("{}\n{}\n", fm.join("\n"), self.body)
    }

    /// Propose -> show -> accept collapses here for the human's own edits:
    /// saving is unremarkable, atomic, journaled.
    pub fn save(&self, publication_root: &Path) -> Result<String> {
        let path = confine(
            publication_root,
            &Path::new("articles").join(&self.meta.slug).join("index.md"),
        )?;
        let bytes = self.render().into_bytes();
        let hash = content_hash(&bytes);
        atomic_write(&path, &bytes)?;
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
                title: title.to_string(),
                state: State::Draft,
                date: Some(today),
                tags: None,
            },
            body: "\n".to_string(),
            frontmatter_raw: vec!["---".into(), "---".into()],
        };
        a.save(publication_root)?;
        Ok(a)
    }

    /// Unknown frontmatter keys, preserved verbatim (for receipts and tests).
    pub fn unknown_frontmatter(&self) -> Vec<String> {
        self.frontmatter_raw
            .iter()
            .filter(|l| {
                l != &"---" && !MANAGED_KEYS.iter().any(|k| l.starts_with(&format!("{k}:")))
            })
            .cloned()
            .collect()
    }

    /// Extract wiki-links ([[target]]) and standard links for the desk graph.
    pub fn links(&self) -> Vec<String> {
        let re = regex::Regex::new(r"\[\[([^\]]+)\]\]").unwrap();
        re.captures_iter(&self.body)
            .map(|c| c[1].to_string())
            .collect()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::tempdir;

    const SAMPLE: &str = "---\ntitle: On Rust\nunknown-key: keep me verbatim\ncustom:\n  nested: true\nstate: draft\ndate: 2026-08-25\ntags: rust, systems\n---\n\nHello [[world]].\n";

    #[test]
    fn parses_managed_and_preserves_unknown() {
        let a = Article::parse("on-rust", SAMPLE).unwrap();
        assert_eq!(a.meta.title, "On Rust");
        assert_eq!(a.meta.state, State::Draft);
        assert_eq!(
            a.unknown_frontmatter(),
            vec![
                "unknown-key: keep me verbatim".to_string(),
                "custom:".to_string(),
                "  nested: true".to_string()
            ]
        );
        assert_eq!(a.links(), vec!["world".to_string()]);
    }

    #[test]
    fn surgical_write_keeps_unknown_bytes() {
        let dir = tempdir().unwrap();
        let root = dir.path();
        fs::create_dir_all(root.join("articles/on-rust")).unwrap();
        fs::write(root.join("articles/on-rust/index.md"), SAMPLE).unwrap();

        let mut a = Article::load(root, "on-rust").unwrap();
        a.meta.state = State::Published;
        a.save(root).unwrap();

        let after = fs::read_to_string(root.join("articles/on-rust/index.md")).unwrap();
        assert!(after.contains("unknown-key: keep me verbatim"));
        assert!(after.contains("nested: true"));
        assert!(after.contains("state: published"));
        assert!(after.contains("[[world]]"));
    }

    #[test]
    fn create_then_load_roundtrip() {
        let dir = tempdir().unwrap();
        let a = Article::create(dir.path(), "fresh", "Fresh Thoughts").unwrap();
        let b = Article::load(dir.path(), "fresh").unwrap();
        assert_eq!(a.meta.title, b.meta.title);
        assert_eq!(b.meta.state, State::Draft);
    }
}
