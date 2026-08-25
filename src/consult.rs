//! Consult: agentic help as bounded local jobs.
//!
//! The repository is the agent's context directory. Recipes are per-
//! publication files (`recipes/<name>.md`); the assistant catalog
//! (`assistants.md`) records *how to invoke* each harness — never credentials.
//! Results are advisory only: they enter documents solely through
//! propose -> show -> accept in articles.rs.

use crate::spine::{confine, redact, run_job, Event, JobOutcome, JobSpec, Journal};
use anyhow::{Context, Result};
use serde::Serialize;
use std::path::{Path, PathBuf};

/// The verbs users invoke. Each maps to a recipe file if present, else a
/// built-in default template.
pub const BUILTIN_RECIPES: [&str; 5] = [
    "polish",
    "align-to-voice",
    "fact-check",
    "suggest-tags",
    "summarize-scratch",
];

// ---------------------------------------------------------------------------
// The assistant catalog — a plain file, curated by the author.
// ---------------------------------------------------------------------------

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct Assistant {
    pub id: String,
    pub command: String,
    #[serde(default)]
    pub args: Vec<String>,
    #[serde(default)]
    pub note: Option<String>,
    /// Recipes may pin this id; otherwise the first entry is the default.
    #[serde(default)]
    pub default: bool,
}

#[derive(Debug, Clone, Default)]
pub struct Catalog {
    pub assistants: Vec<Assistant>,
}

impl Catalog {
    pub fn path(publication_root: &Path) -> PathBuf {
        publication_root.join("assistants.md")
    }

    pub fn load(publication_root: &Path) -> Result<Catalog> {
        let p = Self::path(publication_root);
        if !p.exists() {
            return Ok(Catalog::default());
        }
        let text = std::fs::read_to_string(&p)?;
        // The catalog body is YAML between --- fences; prose around it is
        // ignored so the author can keep notes beside the data.
        let yaml = text
            .split_once("---")
            .and_then(|(_, rest)| rest.split_once("---").map(|(y, _)| y))
            .unwrap_or("");
        let assistants: Vec<Assistant> =
            serde_yaml::from_str(yaml).context("assistants.md catalog is malformed")?;
        Ok(Catalog { assistants })
    }

    pub fn save(&self, publication_root: &Path) -> Result<()> {
        let yaml = serde_yaml::to_string(&self.assistants)?;
        let doc = format!(
            "---\n{yaml}---\n\nEdit freely: `command` must exist on PATH;\n\
             args are passed verbatim as separate arguments (never shell).\n"
        );
        crate::spine::atomic_write(&Self::path(publication_root), doc.as_bytes())?;
        Ok(())
    }

    pub fn pick(&self, pinned: Option<&str>) -> Option<&Assistant> {
        if let Some(id) = pinned {
            return self.assistants.iter().find(|a| a.id == id);
        }
        self.assistants
            .iter()
            .find(|a| a.default)
            .or_else(|| self.assistants.first())
    }
}

/// Seed a catalog from harness shapes discovered on PATH. Curation stays with
/// the author: this only proposes the file, it never runs anything.
pub fn seed_catalog(publication_root: &Path) -> Result<Option<Catalog>> {
    let found: Vec<Assistant> = ["codex", "claude", "gemini", "opencode"]
        .iter()
        .filter(|h| which(h))
        .map(|h| Assistant {
            id: h.to_string(),
            command: h.to_string(),
            args: vec![],
            note: Some("discovered on PATH".into()),
            default: false,
        })
        .collect();
    if found.is_empty() {
        return Ok(None);
    }
    let mut found = found;
    found[0].default = true;
    let cat = Catalog { assistants: found };
    cat.save(publication_root)?;
    Ok(Some(cat))
}

fn which(program: &str) -> bool {
    let path = std::env::var_os("PATH").unwrap_or_default();
    let candidates: Vec<String> = if cfg!(windows) {
        vec![program.to_string(), format!("{program}.exe"), format!("{program}.cmd")]
    } else {
        vec![program.to_string()]
    };
    std::env::split_paths(&path)
        .any(|dir| candidates.iter().any(|c| dir.join(c).is_file()))
}

// ---------------------------------------------------------------------------
// Recipe assembly + invocation.
// ---------------------------------------------------------------------------

#[derive(Debug, Serialize)]
pub struct Advice {
    pub recipe: String,
    pub slug: String,
    pub assistant: String,
    pub verdict_first_output: String,
    pub truncated: bool,
}

/// Assemble the context bundle for a recipe when no custom recipe file exists.
fn assemble_prompt(publication_root: &Path, recipe: &str, slug: &str) -> Result<String> {
    let article = crate::articles::Article::load(publication_root, slug)?;
    let mut p = format!(
        "You are assisting with an article in this repository.\n\
         Read these files before answering:\n\
         - articles/{slug}/index.md (the draft)\n\
         - voice.md (style card, if present)\n\n\
         Recipe: {recipe}\n\
         Rules: advisory only; return suggestions as unified diffs against \
         index.md where applicable; never modify any file.\n\n"
    );
    match recipe {
        "polish" => p.push_str("Polish the prose of the draft. Keep the author's voice."),
        "align-to-voice" => p.push_str(
            "Compare the draft against voice.md and report misalignments \
                 with concrete rewrites.",
        ),
        "fact-check" => p.push_str(
            "List every factual claim in the draft and mark each supported or \
             unsupported, with reasoning.",
        ),
        "suggest-tags" => {
            p.push_str("Suggest 3-7 tags consistent with existing frontmatter conventions.")
        }
        "summarize-scratch" => {
            p.push_str("Read scratch/ and summarize open threads that could feed this article.")
        }
        other => p.push_str(other),
    }
    let _ = article;
    Ok(p)
}

/// Run one consult verb through the configured assistant. Auth rides the
/// harness's own configuration; Tezuri stores no keys. Bounded time, bounded
/// output, redacted evidence.
pub fn advise(
    publication_root: &Path,
    recipe: &str,
    slug: &str,
    pinned_assistant: Option<&str>,
) -> Result<Advice> {
    let catalog = Catalog::load(publication_root)?;
    let assistant = catalog
        .pick(pinned_assistant)
        .context(
            "no assistant is configured. Add one to assistants.md (Tezuri can \
             seed it from what it finds on PATH), then try again.",
        )?
        .clone();

    let recipe_path: PathBuf = confine(
        publication_root,
        &Path::new("recipes").join(format!("{recipe}.md")),
    )?;
    let prompt = if recipe_path.exists() {
        std::fs::read_to_string(&recipe_path)?
    } else {
        assemble_prompt(publication_root, recipe, slug)?
    };

    // The prompt travels over stdin: no command-line length limits, nothing
    // visible in process listings, and no multiline-argv mangling.
    let spec = JobSpec {
        program: assistant.command.clone(),
        args: assistant.args.clone(),
        cwd: confine(publication_root, Path::new(""))?,
        timeout_secs: 300,
        max_output_bytes: 256 * 1024,
        stdin: Some(prompt),
    };
    let outcome: JobOutcome = run_job(&spec)?;
    let output = redact(&outcome.output);
    Journal::open(publication_root)?.record(Event::ConsultAdvised {
        slug: slug.to_string(),
        recipe: recipe.to_string(),
    })?;
    Ok(Advice {
        recipe: recipe.to_string(),
        slug: slug.to_string(),
        assistant: assistant.id,
        verdict_first_output: output,
        truncated: outcome.truncated,
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::tempdir;

    #[test]
    fn recipes_have_defaults() {
        assert!(BUILTIN_RECIPES.contains(&"polish"));
    }

    #[test]
    fn catalog_roundtrips_and_picks_default() {
        let dir = tempdir().unwrap();
        assert!(Catalog::load(dir.path()).unwrap().pick(None).is_none());

        let cat = Catalog {
            assistants: vec![
                Assistant {
                    id: "codex".into(),
                    command: "codex".into(),
                    args: vec!["exec".into()],
                    note: None,
                    default: true,
                },
                Assistant {
                    id: "claude".into(),
                    command: "claude".into(),
                    args: vec!["-p".into()],
                    note: None,
                    default: false,
                },
            ],
        };
        cat.save(dir.path()).unwrap();
        let loaded = Catalog::load(dir.path()).unwrap();
        assert_eq!(loaded.pick(None).unwrap().id, "codex");
        assert_eq!(loaded.pick(Some("claude")).unwrap().id, "claude");
    }

    #[test]
    fn missing_harness_is_reported_not_hidden() {
        assert!(!which("definitely-not-a-harness"));
    }
}
