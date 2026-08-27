//! The desk: a rebuildable lens over the publication.
//!
//! Files are truth. The desk is derived — scan articles, derive states,
//! word counts, link graph, momentum. Delete the cache and it rebuilds
//! identically; that property is what makes it a lens and not a database.

use crate::articles::{Article, State};
use anyhow::Result;
use serde::Serialize;
use std::collections::BTreeMap;
use std::path::Path;

#[derive(Debug, Clone, Serialize)]
pub struct DeskEntry {
    pub slug: String,
    pub title: String,
    pub state: State,
    pub date: Option<String>,
    pub words: usize,
    pub links: Vec<String>,
    /// Referenced by other published work but missing from the desk.
    pub dangling_links: Vec<String>,
    pub tags: Vec<String>,
}

#[derive(Debug, Default, Serialize)]
pub struct Desk {
    pub entries: Vec<DeskEntry>,
    /// slug -> inbound link count (corpus weight).
    pub inbound: BTreeMap<String, usize>,
}

impl Desk {
    /// Rebuild from files at any time. Idempotent by construction.
    pub fn rebuild(publication_root: &Path) -> Result<Desk> {
        let mut entries = Vec::new();
        let articles_dir = publication_root.join("articles");
        if articles_dir.is_dir() {
            for entry in std::fs::read_dir(&articles_dir)? {
                let p = entry?.path();
                let slug = match p.file_name().and_then(|s| s.to_str()) {
                    Some(s) => s.to_string(),
                    None => continue,
                };
                let index = p.join("article.md");
                if !index.is_file() {
                    continue;
                }
                let a = Article::load(publication_root, &slug)?;
                entries.push(DeskEntry {
                    words: a.word_count(),
                    links: a.links(),
                    slug: a.meta.slug.clone(),
                    title: a.title(),
                    state: a.meta.state,
                    date: a.meta.date.clone(),
                    dangling_links: vec![],
                    tags: a.meta.tags.clone(),
                });
            }
        }
        // Derive corpus weights and dangling links in one pass.
        let known: Vec<String> = entries.iter().map(|e| e.slug.clone()).collect();
        let mut inbound: BTreeMap<String, usize> = BTreeMap::new();
        for e in &mut entries {
            e.dangling_links = e
                .links
                .iter()
                .filter(|l| !known.contains(l))
                .cloned()
                .collect();
            for l in &e.links {
                *inbound.entry(l.clone()).or_default() += 1;
            }
        }
        // Most recently dated first; undated last.
        entries.sort_by(|a, b| b.date.cmp(&a.date).then(a.slug.cmp(&b.slug)));
        Ok(Desk { entries, inbound })
    }

    /// Honest momentum signal — no badges, just evidence.
    pub fn momentum(&self) -> Momentum {
        Momentum {
            drafts: self
                .entries
                .iter()
                .filter(|e| e.state == State::Draft)
                .count(),
            published: self
                .entries
                .iter()
                .filter(|e| e.state == State::Published)
                .count(),
            total_words: self.entries.iter().map(|e| e.words).sum(),
            orphans: self
                .entries
                .iter()
                .filter(|e| self.inbound_weight(&e.slug) == 0 && e.state == State::Published)
                .count(),
        }
    }

    fn inbound_weight(&self, slug: &str) -> usize {
        self.inbound.get(slug).copied().unwrap_or(0)
    }

    pub fn search(&self, q: &str) -> Vec<&DeskEntry> {
        let q = q.to_lowercase();
        self.entries
            .iter()
            .filter(|e| e.title.to_lowercase().contains(&q) || e.tags_like(q.as_str()))
            .collect()
    }
}

impl DeskEntry {
    fn tags_like(&self, q: &str) -> bool {
        self.tags.iter().any(|t| t.to_lowercase().contains(q))
    }
}

#[derive(Debug, Serialize)]
pub struct Momentum {
    pub drafts: usize,
    pub published: usize,
    pub total_words: usize,
    pub orphans: usize,
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::articles::Article;
    use tempfile::tempdir;

    #[test]
    fn rebuilds_and_derives_graph() {
        let dir = tempdir().unwrap();
        Article::create(dir.path(), "alpha", "Alpha").unwrap();
        Article::create(dir.path(), "beta", "Beta").unwrap();
        let mut b = Article::load(dir.path(), "beta").unwrap();
        b.document = "# Beta\n\nsee [[alpha]] and [[ghost]]\n".into();
        b.save(dir.path()).unwrap();

        let desk = Desk::rebuild(dir.path()).unwrap();
        assert_eq!(desk.entries.len(), 2);
        assert_eq!(desk.inbound.get("alpha"), Some(&1));
        let beta = desk.entries.iter().find(|e| e.slug == "beta").unwrap();
        assert_eq!(beta.dangling_links, vec!["ghost".to_string()]);
    }

    #[test]
    fn momentum_is_evidence_not_ceremony() {
        let dir = tempdir().unwrap();
        Article::create(dir.path(), "a", "A").unwrap();
        let desk = Desk::rebuild(dir.path()).unwrap();
        let m = desk.momentum();
        assert_eq!(m.drafts, 1);
        assert_eq!(m.published, 0);
    }
}
