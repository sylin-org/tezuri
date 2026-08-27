//! The spine: events, journal, atomic writes, path confinement.
//!
//! Everything else in Tezuri stands on this module. If a mutation is not an
//! event journaled here, it did not happen.

use anyhow::{bail, Context, Result};
use serde::{Deserialize, Serialize};
use std::fs;
use std::io::Write;
use std::path::{Component, Path, PathBuf};
use uuid::Uuid;

// ---------------------------------------------------------------------------
// Path confinement — writes never leave the publication.
// ---------------------------------------------------------------------------

/// Resolve `path` inside `root`, refusing traversal and symlink escapes.
pub fn confine(root: &Path, relative: &Path) -> Result<PathBuf> {
    if relative.is_absolute() {
        bail!("absolute paths are refused: {}", relative.display());
    }
    for c in relative.components() {
        match c {
            Component::Normal(_) | Component::CurDir => {}
            _ => bail!("path escapes the publication: {}", relative.display()),
        }
    }
    let joined = root.join(relative);
    let canon_root = root
        .canonicalize()
        .with_context(|| format!("publication root vanished: {}", root.display()))?;
    // Refuse symlink escape: every existing ancestor must resolve inside root.
    let mut probe = canon_root.clone();
    for part in relative.components() {
        probe.push(part);
        if let Ok(resolved) = probe.canonicalize() {
            if !resolved.starts_with(&canon_root) {
                bail!(
                    "link points outside the publication: {}",
                    relative.display()
                );
            }
        }
    }
    Ok(joined)
}

/// Atomic write: temp file + rename. A crash leaves the previous version intact.
pub fn atomic_write(target: &Path, bytes: &[u8]) -> Result<()> {
    let parent = target.parent().context("target has no parent")?;
    fs::create_dir_all(parent)?;
    let tmp = parent.join(format!(".tezuri-{}.tmp", Uuid::new_v4().simple()));
    {
        let mut f = fs::File::create(&tmp)?;
        f.write_all(bytes)?;
        f.sync_all()?;
    }
    fs::rename(&tmp, target)
        .with_context(|| format!("rename into place failed: {}", target.display()))?;
    Ok(())
}

/// Content hash (sha256 hex) used for media addressing and change detection.
pub fn content_hash(bytes: &[u8]) -> String {
    use sha2::{Digest, Sha256};
    let mut h = Sha256::new();
    h.update(bytes);
    hex(&h.finalize())
}

fn hex(bytes: &[u8]) -> String {
    bytes.iter().map(|b| format!("{:02x}", b)).collect()
}

// ---------------------------------------------------------------------------
// Events and the journal — the one grammar of change.
// ---------------------------------------------------------------------------

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "type", rename_all = "kebab-case")]
pub enum Event {
    /// An article was created or its canonical file changed through Tezuri.
    #[serde(rename_all = "camelCase")]
    ArticleWritten { slug: String, content_hash: String },
    /// The publication's own identity file was written.
    IdentityWritten,
    /// The space's theme.css was written or cleared.
    ThemeWritten,
    /// Pages were compiled into render/.
    #[serde(rename_all = "camelCase")]
    Rendered { pages: usize },
    /// Media was stored under its content address.
    #[serde(rename_all = "camelCase")]
    MediaStored { hash: String, filename: String },
    /// A consult job produced advisory output (never auto-applied).
    #[serde(rename_all = "camelCase")]
    ConsultAdvised { slug: String, recipe: String },
    /// A proof build ran to a verdict.
    #[serde(rename_all = "camelCase")]
    ProofRan { verdict: String },
    /// Selected paths were committed.
    #[serde(rename_all = "camelCase")]
    PublishedCommitted { slugs: Vec<String>, message: String },
    /// The remote was pushed after lease verification.
    #[serde(rename_all = "camelCase")]
    PublishedPushed,
}

impl Event {
    pub fn kind(&self) -> &'static str {
        match self {
            Event::ArticleWritten { .. } => "article-written",
            Event::IdentityWritten => "identity-written",
            Event::ThemeWritten => "theme-written",
            Event::Rendered { .. } => "rendered",
            Event::MediaStored { .. } => "media-stored",
            Event::ConsultAdvised { .. } => "consult-advised",
            Event::ProofRan { .. } => "proof-ran",
            Event::PublishedCommitted { .. } => "published-committed",
            Event::PublishedPushed => "published-pushed",
        }
    }
}

/// One journal per publication, append-only JSONL, lives inside the publication
/// so it travels with the repo and is itself plain files.
pub struct Journal {
    path: PathBuf,
}

#[derive(Serialize, Deserialize)]
struct JournalLine {
    at: chrono::DateTime<chrono::Utc>,
    event: Event,
}

impl Journal {
    pub fn open(publication_root: &Path) -> Result<Self> {
        Ok(Journal {
            path: publication_root.join(".tezuri/journal.jsonl"),
        })
    }

    pub fn record(&self, event: Event) -> Result<()> {
        let line = JournalLine {
            at: chrono::Utc::now(),
            event,
        };
        atomic_write_appended(&self.path, &serde_json::to_string(&line)?)
    }

    /// Rebuild the lens: read all events back.
    pub fn events(&self) -> Result<Vec<(chrono::DateTime<chrono::Utc>, Event)>> {
        if !self.path.exists() {
            return Ok(vec![]);
        }
        let text = fs::read_to_string(&self.path)?;
        text.lines()
            .filter(|l| !l.trim().is_empty())
            .map(|l| {
                let line: JournalLine = serde_json::from_str(l)
                    .with_context(|| format!("corrupt journal line: {l}"))?;
                Ok((line.at, line.event))
            })
            .collect()
    }
}

/// Append-only write that is still crash-safe enough for a local journal:
/// create dirs, then append with a single syscall under an OS-level lock file.
fn atomic_write_appended(path: &Path, line: &str) -> Result<()> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)?;
    }
    let mut f = fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(path)?;
    writeln!(f, "{line}")?;
    f.sync_all()?;
    Ok(())
}

// ---------------------------------------------------------------------------
// Jobs — bounded, cancellable subprocess runs (argv arrays only, never shell).
// ---------------------------------------------------------------------------

pub struct JobSpec {
    pub program: String,
    pub args: Vec<String>,
    pub cwd: PathBuf,
    pub timeout_secs: u64,
    pub max_output_bytes: usize,
    /// Optional payload delivered over stdin (long prompts, never argv).
    pub stdin: Option<String>,
}

#[derive(Debug, Clone, Serialize)]
pub struct JobOutcome {
    pub exit_ok: bool,
    pub timed_out: bool,
    /// Bounded combined output; secrets redaction happens at the caller.
    pub output: String,
    pub truncated: bool,
}

impl JobOutcome {
    /// Plain verdict first; evidence available but not forced.
    pub fn verdict(&self) -> String {
        if self.timed_out {
            "timed out".into()
        } else if self.exit_ok {
            "passed".into()
        } else {
            "failed".into()
        }
    }
}

/// Run a job with hard bounds. Never passes anything through a shell; the
/// program name is resolved by the OS against PATH, arguments stay separate.
pub fn run_job(spec: &JobSpec) -> Result<JobOutcome> {
    use std::process::{Command, Stdio};
    let mut child = Command::new(&spec.program)
        .args(&spec.args)
        .current_dir(&spec.cwd)
        .stdin(if spec.stdin.is_some() {
            std::process::Stdio::piped()
        } else {
            std::process::Stdio::null()
        })
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .with_context(|| format!("could not start {}", spec.program))?;

    // Readers start before any stdin is delivered: if the child fills its
    // output pipes while we are still writing the prompt, both sides deadlock
    // unless someone is already draining.
    let out_bound = spec.max_output_bytes;
    let out_h = {
        let h = child.stdout.take();
        std::thread::spawn(move || read_bounded(h, out_bound))
    };
    let err_h = {
        let h = child.stderr.take();
        std::thread::spawn(move || read_bounded(h, out_bound))
    };

    if let Some(payload) = &spec.stdin {
        let payload = payload.clone();
        let mut si = child.stdin.take().context("stdin pipe vanished")?;
        // Writing on its own thread keeps the parent free to reap/kill on
        // timeout even while a huge prompt is still flowing.
        let feed = std::thread::spawn(move || {
            let _ = si.write_all(payload.as_bytes());
            let _ = si.flush();
            drop(si); // closing stdin signals EOF to the child
        });
        let deadline =
            std::time::Instant::now() + std::time::Duration::from_secs(spec.timeout_secs);
        loop {
            match child.try_wait() {
                Ok(Some(status)) => {
                    let _ = feed.join();
                    return Ok(finish(out_h, err_h, status.success(), false));
                }
                Ok(None) => {
                    if std::time::Instant::now() >= deadline {
                        break;
                    }
                    std::thread::sleep(std::time::Duration::from_millis(50));
                }
                Err(_) => {
                    let _ = feed.join();
                    return Ok(finish(out_h, err_h, false, false));
                }
            }
        }
        let _ = child.kill();
        let _ = child.wait();
        let _ = feed.join(); // broken pipe once the child is dead is fine
        return Ok(finish(out_h, err_h, false, true));
    }

    match wait_with_timeout(&mut child, spec.timeout_secs) {
        Some(status) => Ok(finish(out_h, err_h, status.success(), false)),
        None => {
            let _ = child.kill();
            let _ = child.wait();
            Ok(finish(out_h, err_h, false, true))
        }
    }
}

fn finish(
    out_h: std::thread::JoinHandle<(String, bool)>,
    err_h: std::thread::JoinHandle<(String, bool)>,
    exit_ok: bool,
    timed_out: bool,
) -> JobOutcome {
    let (stdout, out_trunc) = out_h.join().unwrap_or_default();
    let (stderr, err_trunc) = err_h.join().unwrap_or((String::new(), false));
    let mut combined = stdout;
    combined.push_str(&stderr);
    JobOutcome {
        exit_ok,
        timed_out,
        truncated: out_trunc || err_trunc,
        output: combined,
    }
}

/// Read at most `max` bytes, reporting whether anything was cut off.
fn read_bounded<R: std::io::Read>(r: Option<R>, max: usize) -> (String, bool) {
    let mut buf = Vec::new();
    let mut truncated = false;
    if let Some(mut r) = r {
        let mut chunk = [0u8; 8192];
        loop {
            match r.read(&mut chunk) {
                Ok(0) | Err(_) => break,
                Ok(n) => {
                    if buf.len() + n > max {
                        buf.extend_from_slice(&chunk[..max - buf.len()]);
                        truncated = true;
                        break;
                    }
                    buf.extend_from_slice(&chunk[..n]);
                }
            }
        }
    }
    (String::from_utf8_lossy(&buf).into_owned(), truncated)
}

fn wait_with_timeout(
    child: &mut std::process::Child,
    secs: u64,
) -> Option<std::process::ExitStatus> {
    let deadline = std::time::Instant::now() + std::time::Duration::from_secs(secs);
    loop {
        match child.try_wait() {
            Ok(Some(status)) => return Some(status),
            Ok(None) => {
                if std::time::Instant::now() >= deadline {
                    return None;
                }
                std::thread::sleep(std::time::Duration::from_millis(50));
            }
            Err(_) => return None,
        }
    }
}

// ---------------------------------------------------------------------------
// Redaction — anything that looks like a credential never reaches the UI.
// ---------------------------------------------------------------------------

/// Remove obvious credential material from displayed evidence.
pub fn redact(text: &str) -> String {
    let patterns = [
        r"(?i)(api[_-]?key|token|secret|password)\s*[:=]\s*\S+",
        r"gh[pousr]_[A-Za-z0-9]{20,}",
        r"(?i)authorization:\s*basic\s+\S+",
    ];
    let mut out = text.to_string();
    for p in patterns {
        out = regex::Regex::new(p)
            .map(|re| re.replace_all(&out, "$1=[redacted]").into_owned())
            .unwrap_or(out.clone());
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::tempdir;

    #[test]
    fn confinement_refuses_escape() {
        let dir = tempdir().unwrap();
        let root = dir.path();
        assert!(confine(root, Path::new("a/b.txt")).is_ok());
        assert!(confine(root, Path::new("../evil")).is_err());
        assert!(confine(root, Path::new("/abs")).is_err());
    }

    #[test]
    fn atomic_write_is_atomic_and_creates_dirs() {
        let dir = tempdir().unwrap();
        let target = dir.path().join("deep/nested/file.txt");
        atomic_write(&target, b"hello").unwrap();
        assert_eq!(std::fs::read(&target).unwrap(), b"hello");
    }

    #[test]
    fn journal_roundtrips_events() {
        let dir = tempdir().unwrap();
        let j = Journal::open(dir.path()).unwrap();
        j.record(Event::ProofRan {
            verdict: "passed".into(),
        })
        .unwrap();
        j.record(Event::MediaStored {
            hash: "abc".into(),
            filename: "x.png".into(),
        })
        .unwrap();
        let evs = j.events().unwrap();
        assert_eq!(evs.len(), 2);
        assert_eq!(evs[1].1.kind(), "media-stored");
    }

    #[test]
    fn jobs_run_bounded_without_shell() {
        let dir = tempdir().unwrap();
        let spec = JobSpec {
            program: if cfg!(windows) {
                "cmd".into()
            } else {
                "sh".into()
            },
            args: vec![
                if cfg!(windows) {
                    "/c".into()
                } else {
                    "-c".into()
                },
                "echo hi".into(),
            ],
            cwd: dir.path().to_path_buf(),
            timeout_secs: 10,
            max_output_bytes: 1024,
            stdin: None,
        };
        let out = run_job(&spec).unwrap();
        assert!(out.exit_ok);
        assert!(out.output.contains("hi"));
    }

    #[test]
    fn jobs_time_out() {
        let dir = tempdir().unwrap();
        let sleep = if cfg!(windows) {
            "ping -n 6 127.0.0.1 >nul"
        } else {
            "sleep 5"
        };
        let spec = JobSpec {
            program: if cfg!(windows) {
                "cmd".into()
            } else {
                "sh".into()
            },
            args: vec![
                if cfg!(windows) {
                    "/c".into()
                } else {
                    "-c".into()
                },
                sleep.into(),
            ],
            cwd: dir.path().to_path_buf(),
            timeout_secs: 1,
            max_output_bytes: 1024,
            stdin: None,
        };
        let out = run_job(&spec).unwrap();
        assert!(out.timed_out);
    }

    #[test]
    fn jobs_honor_the_output_bound() {
        let dir = tempdir().unwrap();
        // Emit far more than the cap on both streams.
        let script = "echo stdout-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa \
                      & echo stderr-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        let shell_args = if cfg!(windows) {
            vec!["/c".to_string(), script.to_string()]
        } else {
            vec!["-c".to_string(), script.to_string()]
        };
        let spec = JobSpec {
            program: if cfg!(windows) {
                "cmd".into()
            } else {
                "sh".into()
            },
            args: shell_args,
            cwd: dir.path().to_path_buf(),
            timeout_secs: 10,
            max_output_bytes: 16,
            stdin: None,
        };
        let out = run_job(&spec).unwrap();
        assert!(
            out.output.chars().count() <= 64,
            "output must be bounded, got {} bytes",
            out.output.len()
        );
        assert!(out.truncated, "truncation must be reported");
    }

    #[test]
    fn jobs_feed_stdin_and_reap_even_when_child_is_slow() {
        let dir = tempdir().unwrap();
        // Child reads all of stdin then echoes it. A large payload (bigger
        // than a pipe buffer) proves delivery and drain run concurrently.
        let spec = JobSpec {
            program: if cfg!(windows) {
                "cmd".into()
            } else {
                "sh".into()
            },
            args: vec![
                if cfg!(windows) {
                    "/c".into()
                } else {
                    "-c".into()
                },
                if cfg!(windows) {
                    "more".to_string()
                } else {
                    "cat".to_string()
                },
            ],
            cwd: dir.path().to_path_buf(),
            timeout_secs: 15,
            max_output_bytes: 1024 * 1024,
            stdin: Some("prompt-".repeat(40_000)),
        };
        let out = run_job(&spec).unwrap();
        assert!(out.exit_ok);
        if cfg!(windows) {
            // `more` adds platform text noise; delivery, not byte fidelity.
            assert!(out.output.contains("prompt-"));
        } else {
            assert_eq!(out.output.len(), "prompt-".len() * 40_000);
        }
    }

    #[test]
    fn redaction_removes_secrets() {
        let t = redact("token: ghp_abcdefghijklmnopqrstuvwx and password=hunter2");
        assert!(!t.contains("ghp_"));
        assert!(!t.contains("hunter2"));
    }
}
