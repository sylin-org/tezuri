//! Publications: an author owns several; each is a folder with conventions.
//!
//! The registry is the only thing Tezuri keeps outside publications — and it
//! contains no content, only paths and names. Losing it costs a list.

use anyhow::{Context, Result};
use serde::{Deserialize, Serialize};
use std::path::PathBuf;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Publication {
    pub name: String,
    pub persona: String,
    pub root: PathBuf,
}

/// Author-level registry. Lives in the ordinary place applications keep
/// settings; holds nothing canonical.
#[derive(Debug, Default, Serialize, Deserialize)]
pub struct Registry {
    pub publications: Vec<Publication>,
}

impl Registry {
    pub fn path() -> Result<PathBuf> {
        let base = dirs_home().context("cannot locate a home directory")?;
        Ok(base.join(".tezuri").join("registry.json"))
    }

    pub fn load() -> Result<Registry> {
        let p = Self::path()?;
        if !p.exists() {
            return Ok(Registry::default());
        }
        Ok(serde_json::from_str(&std::fs::read_to_string(&p)?)?)
    }

    pub fn save(&self) -> Result<()> {
        let p = Self::path()?;
        if let Some(parent) = p.parent() {
            std::fs::create_dir_all(parent)?;
        }
        crate::spine::atomic_write(&p, serde_json::to_string_pretty(self)?.as_bytes())
    }

    /// Pure domain rule: refuse a root that is already registered. Persisting
    /// is the caller's separate, explicit act — never a side effect.
    pub fn add(&mut self, publication: Publication) -> Result<()> {
        if self.publications.iter().any(|p| p.root == publication.root) {
            anyhow::bail!("this folder is already registered");
        }
        self.publications.push(publication);
        Ok(())
    }
}

fn dirs_home() -> Option<PathBuf> {
    // HOME on unix, USERPROFILE on windows.
    std::env::var_os("HOME")
        .or_else(|| std::env::var_os("USERPROFILE"))
        .map(PathBuf::from)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn registry_refuses_duplicates_without_touching_disk() {
        let p = Registry::path().unwrap();
        let before = std::fs::read(&p).ok();

        let mut r = Registry::default();
        r.add(Publication {
            name: "blog".into(),
            persona: "me".into(),
            root: PathBuf::from("/tmp/blog"),
        })
        .unwrap();
        assert!(r
            .add(Publication {
                name: "again".into(),
                persona: "me".into(),
                root: PathBuf::from("/tmp/blog"),
            })
            .is_err());

        // add() is pure domain logic; persistence stays with callers.
        let after = std::fs::read(&p).ok();
        assert_eq!(before, after);
    }
}
