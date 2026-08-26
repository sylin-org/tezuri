//! Ship: proof, review, publish — the pipeline with human gates exactly where
//! damage is irreversible.
//!
//! Proof runs the site's own build against a disposable copy of the
//! publication (bounded, redacted, verdict-first). Publishing is explicit:
//! review changed paths, select exact paths, commit only the selection, then
//! push only if the reviewed remote state still holds. Saving never touches
//! git.

use crate::spine::{confine, redact, run_job, Event, JobSpec, Journal};
use anyhow::{bail, Context, Result};
use serde::Serialize;
use std::path::Path;
use std::process::Command;

// ---------------------------------------------------------------------------
// Proof — the destination repo's build is the authority; Tezuri has no
// competing renderer.
// ---------------------------------------------------------------------------

#[derive(Debug, Serialize)]
pub struct Proof {
    pub verdict: String,
    pub evidence: String,
    pub truncated: bool,
}

/// Detect the conventional build: package.json `build` script via an installed
/// package manager, or Hugo when a standard config file is present.
pub fn detect_build(publication_root: &Path) -> Option<(String, Vec<String>)> {
    let pkg = publication_root.join("package.json");
    if pkg.is_file() {
        // Windows: npm is a .cmd shim; spawn it directly (no cmd wrapper).
        let pm = if cfg!(windows) { "npm.cmd" } else { "npm" };
        if which(pm) {
            return Some((pm.into(), vec!["run".into(), "build".into()]));
        }
    }
    let hugo_configs = ["config.toml", "config.yaml", "hugo.toml", "hugo.yaml"];
    if hugo_configs
        .iter()
        .any(|c| publication_root.join(c).is_file())
        && which("hugo")
    {
        return Some(("hugo".into(), vec!["--gc".into()]));
    }
    None
}

/// Run the detected build in a disposable copy. Never touches the working tree;
/// a failed build leaves no debris beyond the removed temp dir.
pub fn prove(publication_root: &Path) -> Result<Proof> {
    let (program, args) = detect_build(publication_root).context(
        "no conventional build found here: expected a package.json build \
             script or a Hugo configuration",
    )?;

    let work = copy_to_temp(publication_root).context("could not stage a disposable copy")?;
    let outcome = run_job(&JobSpec {
        program: program.clone(),
        args,
        cwd: work.path().to_path_buf(),
        timeout_secs: 600,
        max_output_bytes: 256 * 1024,
        stdin: None,
    });
    drop(work); // debris removed whether the build passed or failed

    let outcome = outcome?;
    Journal::open(publication_root)?.record(Event::ProofRan {
        verdict: outcome.verdict(),
    })?;
    Ok(Proof {
        verdict: outcome.verdict(),
        evidence: redact(&outcome.output),
        truncated: outcome.truncated,
    })
}

struct TempCopy(tempfile::TempDir);

impl TempCopy {
    fn path(&self) -> &Path {
        self.0.path()
    }
}

fn copy_to_temp(root: &Path) -> Result<TempCopy> {
    let dir = tempfile::tempdir()?;
    copy_tree(root, dir.path())?;
    Ok(TempCopy(dir))
}

fn copy_tree(src: &Path, dst: &Path) -> Result<()> {
    fs_extra_copy(src, dst)
}

// Minimal recursive copier — one small part instead of a dependency.
fn fs_extra_copy(src: &Path, dst: &Path) -> Result<()> {
    std::fs::create_dir_all(dst)?;
    for entry in walkdir::WalkDir::new(src)
        .into_iter()
        .filter_map(|e| e.ok())
    {
        let rel = match entry.path().strip_prefix(src) {
            Ok(r) if !r.as_os_str().is_empty() => r,
            _ => continue,
        };
        // Skip heavy/irrelevant dirs; git internals are never copied.
        let skip = rel.components().any(|c| {
            matches!(
                c.as_os_str().to_string_lossy().as_ref(),
                ".git" | "target" | "node_modules" | ".tezuri"
            )
        });
        if skip {
            continue;
        }
        let target = dst.join(rel);
        if entry.file_type().is_dir() {
            std::fs::create_dir_all(target)?;
        } else {
            std::fs::copy(entry.path(), target)?;
        }
    }
    Ok(())
}

fn which(program: &str) -> bool {
    let path = std::env::var_os("PATH").unwrap_or_default();
    // Windows resolves `x` to x.exe and also honors explicit extensions like
    // npm.cmd; other platforms take the name as-is.
    let candidates: Vec<String> = if cfg!(windows) {
        vec![
            program.to_string(),
            format!("{program}.exe"),
            format!("{program}.cmd"),
        ]
    } else {
        vec![program.to_string()]
    };
    std::env::split_paths(&path).any(|dir| candidates.iter().any(|c| dir.join(c).is_file()))
}

// ---------------------------------------------------------------------------
// Publication — review, select, commit, lease-checked push.
// ---------------------------------------------------------------------------

/// One changed path offered to the author for review.
#[derive(Debug, Clone, serde::Serialize)]
pub struct Change {
    pub path: String,
    pub status: char, // M / A / D / R from porcelain v1
}

/// Parse one porcelain-v1 status line into a (status, path) pair, keeping the
/// *destination* of renames so commits touch the name that now exists.
fn parse_porcelain(line: &str) -> Option<(char, String)> {
    if line.len() < 4 {
        return None;
    }
    let status = line[..2].trim().chars().next().unwrap_or('M');
    let rest = &line[3..];
    let unquoted = |s: &str| s.trim().trim_matches('"').to_string();
    // Rename/copy lines carry both ends: "old -> new".
    let path = match rest.split_once(" -> ") {
        Some((_old, new)) => unquoted(new),
        None => unquoted(rest),
    };
    if path.is_empty() || path.starts_with(".tezuri/") {
        return None;
    }
    Some((status, path))
}

/// Read working-tree changes (porcelain), excluding Tezuri's own journal dir.
pub fn review(publication_root: &Path) -> Result<Vec<Change>> {
    let out = Command::new("git")
        .args(["status", "--porcelain"])
        .current_dir(publication_root)
        .output()
        .context("git is not available or this folder is not a repository")?;
    if !out.status.success() {
        bail!(
            "git status failed: {}",
            redact(&String::from_utf8_lossy(&out.stderr))
        );
    }
    Ok(String::from_utf8_lossy(&out.stdout)
        .lines()
        .filter_map(parse_porcelain)
        .map(|(status, path)| Change { path, status })
        .collect())
}

/// Commit exactly the selected paths. Unrelated work stays untouched and
/// unstaged — including anything the author had already staged for their own
/// reasons; that situation is refused outright rather than swept along.
pub fn commit_selection(
    publication_root: &Path,
    paths: &[String],
    message: &str,
) -> Result<String> {
    if paths.is_empty() {
        bail!("nothing selected to commit");
    }
    if message.trim().is_empty() {
        bail!("a commit needs your message");
    }
    for p in paths {
        confine(publication_root, Path::new(p))?; // never stage outside the repo
    }

    let out = Command::new("git")
        .args(["diff", "--cached", "--name-only"])
        .current_dir(publication_root)
        .output()
        .context("git is not available or this folder is not a repository")?;
    if !out.status.success() {
        bail!("git failed: {}", redact(&String::from_utf8_lossy(&out.stderr)));
    }
    let selected: std::collections::BTreeSet<&str> =
        paths.iter().map(|s| s.as_str()).collect();
    let foreign: Vec<String> = String::from_utf8_lossy(&out.stdout)
        .lines()
        .map(str::trim)
        .filter(|l| !l.is_empty())
        .filter(|l| !selected.contains(*l))
        .map(String::from)
        .collect();
    if !foreign.is_empty() {
        bail!(
            "other changes are already staged ({} and {} more). \
             Unstage them or select them too — I will not sweep them \
             into your commit.",
            foreign.first().unwrap_or(&String::new()),
            foreign.len() - 1
        );
    }

    let mut cmd = Command::new("git");
    cmd.arg("add").arg("--").args(paths);
    let out = cmd.current_dir(publication_root).output()?;
    if !out.status.success() {
        bail!(
            "could not stage selected files: {}",
            redact(&String::from_utf8_lossy(&out.stderr))
        );
    }

    let out = Command::new("git")
        .args(["commit", "-m", message])
        .current_dir(publication_root)
        .output()?;
    if !out.status.success() {
        bail!("commit failed: {}", redact(&String::from_utf8_lossy(&out.stderr)));
    }
    let hash_out = Command::new("git")
        .args(["rev-parse", "--short", "HEAD"])
        .current_dir(publication_root)
        .output()?;
    let hash = String::from_utf8_lossy(&hash_out.stdout).trim().to_string();

    Journal::open(publication_root)?.record(Event::PublishedCommitted {
        slugs: paths.to_vec(),
        message: message.to_string(),
    })?;
    Ok(hash)
}

/// Push only if the remote still matches what was reviewed. If it moved, stop
/// and say so — resolving that is the author's call, not ours.
pub fn push(publication_root: &Path, expected_remote_head: Option<&str>) -> Result<()> {
    let out = Command::new("git")
        .args(["fetch"])
        .current_dir(publication_root)
        .output()
        .context("git fetch failed")?;
    if !out.status.success() {
        bail!("fetch failed: {}", redact(&String::from_utf8_lossy(&out.stderr)));
    }

    if let Some(expected) = expected_remote_head {
        let out = Command::new("git")
            .args(["rev-parse", "@{upstream}"])
            .current_dir(publication_root)
            .output()?;
        let actual = String::from_utf8_lossy(&out.stdout).trim().to_string();
        if !out.status.success() || actual != expected {
            bail!(
                "the remote moved after you reviewed. Expected {}, found {}. \
                 Review the new state and decide.",
                expected,
                if actual.is_empty() {
                    "no upstream"
                } else {
                    &actual
                }
            );
        }
    }

    let out = Command::new("git")
        .args(["push"])
        .current_dir(publication_root)
        .output()?;
    if !out.status.success() {
        bail!("push failed: {}", redact(&String::from_utf8_lossy(&out.stderr)));
    }
    Journal::open(publication_root)?.record(Event::PublishedPushed)?;
    Ok(())
}

/// Current upstream head, captured at review time for the lease check.
pub fn remote_head(publication_root: &Path) -> Result<Option<String>> {
    let out = Command::new("git")
        .args(["rev-parse", "@{upstream}"])
        .current_dir(publication_root)
        .output()?;
    if out.status.success() {
        Ok(Some(
            String::from_utf8_lossy(&out.stdout).trim().to_string(),
        ))
    } else {
        Ok(None) // no upstream yet; push will establish it
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::tempdir;

    fn git(dir: &Path, args: &[&str]) {
        let out = Command::new("git")
            .args(args)
            .current_dir(dir)
            .env("GIT_AUTHOR_NAME", "t")
            .env("GIT_AUTHOR_EMAIL", "t@t")
            .env("GIT_COMMITTER_NAME", "t")
            .env("GIT_COMMITTER_EMAIL", "t@t")
            .output()
            .unwrap();
        assert!(
            out.status.success(),
            "{:?}: {}",
            args,
            String::from_utf8_lossy(&out.stderr)
        );
    }

    #[test]
    fn review_sees_changes_and_excludes_journal() {
        let dir = tempdir().unwrap();
        let root = dir.path();
        git(root, &["init"]);
        std::fs::write(root.join("a.md"), "x").unwrap();
        git(root, &["add", "."]);
        git(root, &["commit", "-m", "init"]);
        std::fs::create_dir_all(root.join(".tezuri")).unwrap();
        std::fs::write(root.join(".tezuri/journal.jsonl"), "{}\n").unwrap();
        std::fs::write(root.join("b.md"), "new").unwrap();

        let changes = review(root).unwrap();
        assert!(changes.iter().any(|c| c.path == "b.md"));
        assert!(!changes.iter().any(|c| c.path.starts_with(".tezuri")));
    }

    #[test]
    fn commits_only_the_selection() {
        let dir = tempdir().unwrap();
        let root = dir.path();
        git(root, &["init"]);
        std::fs::write(root.join("a.md"), "x").unwrap();
        git(root, &["add", "."]);
        git(root, &["commit", "-m", "init"]);

        std::fs::write(root.join("a.md"), "changed").unwrap();
        std::fs::write(root.join("unrelated.md"), "wip").unwrap();

        let hash = commit_selection(root, &["a.md".into()], "fix a").unwrap();
        assert!(!hash.is_empty());

        let staged = Command::new("git")
            .args(["status", "--porcelain"])
            .current_dir(root)
            .output()
            .unwrap();
        let s = String::from_utf8_lossy(&staged.stdout);
        assert!(
            s.contains("unrelated.md"),
            "unrelated work must stay unstaged"
        );
    }

    #[test]
    fn refuses_empty_or_escaping_selections() {
        let dir = tempdir().unwrap();
        assert!(commit_selection(dir.path(), &[], "m").is_err());
        assert!(commit_selection(dir.path(), &["../escape".into()], "m").is_err());
    }

    #[test]
    fn refuses_to_sweep_foreign_staged_work() {
        let dir = tempdir().unwrap();
        let root = dir.path();
        git(root, &["init"]);
        std::fs::write(root.join("a.md"), "x").unwrap();
        std::fs::write(root.join("mine.md"), "author's own work").unwrap();
        git(root, &["add", "."]);
        git(root, &["commit", "-m", "init"]);

        std::fs::write(root.join("a.md"), "changed").unwrap();
        std::fs::write(root.join("staged-by-author.md"), "careful work").unwrap();
        git(root, &["add", "staged-by-author.md"]); // foreign, pre-staged

        let err = commit_selection(root, &["a.md".into()], "fix a").unwrap_err();
        let msg = err.to_string();
        assert!(
            msg.contains("already staged"),
            "refusal must say why: {msg}"
        );
        // Nothing was committed by the refused attempt.
        let s = git_status(root);
        assert!(s.contains("staged-by-author.md"));
        assert!(s.contains(" M a.md") || s.contains("M  a.md") || s.contains("M a.md"));
    }

    #[test]
    fn porcelain_renames_keep_the_destination() {
        assert_eq!(
            parse_porcelain("R  old-name.md -> new-name.md"),
            Some(('R', "new-name.md".into()))
        );
        assert_eq!(
            parse_porcelain(" M modified.md"),
            Some(('M', "modified.md".into()))
        );
        // Tezuri's journal never surfaces for review.
        assert_eq!(parse_porcelain(" M .tezuri/journal.jsonl"), None);
        assert_eq!(parse_porcelain("A? "), None);
    }

    fn git_status(root: &Path) -> String {
        String::from_utf8_lossy(
            &Command::new("git")
                .args(["status", "--porcelain"])
                .current_dir(root)
                .output()
                .unwrap()
                .stdout,
        )
        .to_string()
    }

    #[test]
    fn copy_tree_skips_git_internals() {
        let src = tempdir().unwrap();
        std::fs::create_dir_all(src.path().join(".git")).unwrap();
        std::fs::write(src.path().join(".git/HEAD"), "ref").unwrap();
        std::fs::write(src.path().join("keep.md"), "k").unwrap();
        let dst_dir = tempdir().unwrap();
        let dst = dst_dir.path().join("copy");
        fs_extra_copy(src.path(), &dst).unwrap();
        assert!(dst.join("keep.md").exists());
        assert!(!dst.join(".git").exists());
    }
}
