//! Settling: a loaded space is gently made whole.
//!
//! Everything derived — emitted pages, image renditions, the desk — is a
//! cache. A space loaded from a current-state repository may be missing any
//! of them, and that is normal, not an error. `scan_plan` notices what is
//! absent or stale with cheap stat checks (never hashing), `settle` derives
//! exactly that, and both are idempotent: running them twice changes nothing.
//! The desktop shell runs the plan on a background thread after open; previews
//! never wait for it (they compile on demand), and nothing here touches the
//! network.

use crate::articles::State;
use crate::render;
use crate::renditions::{self, Recipe};
use crate::spine::{confine, content_hash};
use anyhow::Result;
use std::path::Path;

/// What a space needs to become whole.
#[derive(Debug, Default, PartialEq)]
pub struct SettlePlan {
    /// Slugs whose emitted page is missing or stale. Published articles
    /// only: drafts are compiled on demand in the preview surface and are
    /// never written behind the author's back.
    pub renders: Vec<String>,
    /// (base media reference, recipe) pairs whose rendition file is missing.
    pub renditions: Vec<(String, Recipe)>,
}

/// Where the last-seen published-set fingerprint lives (a derived cache,
/// rebuilt at will).
fn fingerprint_path(publication_root: &Path) -> Result<std::path::PathBuf> {
    confine(publication_root, Path::new(".tezuri/published-index"))
}

/// A cheap identity of the publishable set: everything pages project about
/// their NEIGHBORS (order for prev/next, tags for similar, titles/dates for
/// lists). When it matches what the emitted pages were built from, no page
/// can be neighbor-stale, whatever its own mtimes say.
pub fn published_fingerprint(publication_root: &Path) -> Result<String> {
    let desk = crate::desk::Desk::rebuild(publication_root)?;
    let mut lines: Vec<String> = desk
        .entries
        .iter()
        .filter(|e| e.state == State::Published)
        .map(|e| {
            format!(
                "{}\u{1f}{}\u{1f}{}\u{1f}{}\u{1f}{}",
                e.slug,
                e.state.as_str(),
                e.date.as_deref().unwrap_or(""),
                e.title,
                e.tags.join("\u{1e}")
            )
        })
        .collect();
    lines.sort();
    Ok(content_hash(lines.join("\n").as_bytes()))
}

impl SettlePlan {
    pub fn is_empty(&self) -> bool {
        self.renders.is_empty() && self.renditions.is_empty()
    }

    pub fn total(&self) -> usize {
        self.renders.len() + self.renditions.len()
    }
}

/// Renditions the settler pre-warms. The rest derive on demand at display
/// time, exactly as before.
const PREWARM: [Recipe; 2] = [Recipe::Thumb, Recipe::Width(1024)];

/// Notice what is missing or stale. Stat-only: cheap enough to run on every
/// open, and on every theme or identity save.
pub fn scan_plan(publication_root: &Path) -> Result<SettlePlan> {
    let mut plan = SettlePlan::default();

    let desk = crate::desk::Desk::rebuild(publication_root)?;
    let publishable: Vec<_> = desk
        .entries
        .iter()
        .filter(|e| e.state == State::Published)
        .cloned()
        .collect();

    // Neighbor staleness: the emitted pages were built from a published set.
    // Any change in that set can move prev/next links and lists on pages
    // whose own inputs never changed, so the whole publishable set re-plans.
    let current_fp = published_fingerprint(publication_root)?;
    let stored_fp =
        std::fs::read_to_string(fingerprint_path(publication_root)?).unwrap_or_default();
    let set_changed = stored_fp != current_fp;

    for e in &publishable {
        let rel = format!("{}/{}.html", render::RENDER_DIR, e.slug);
        let page = confine(publication_root, Path::new(&rel))?;
        if set_changed || page_stale(&page, publication_root, &e.slug) {
            plan.renders.push(e.slug.clone());
        }
    }

    let media_dir = confine(publication_root, Path::new("media"))?;
    if media_dir.is_dir() {
        for entry in std::fs::read_dir(&media_dir)? {
            let p = entry?.path();
            let name = match p.file_name().and_then(|s| s.to_str()) {
                Some(n) => n.to_string(),
                None => continue,
            };
            if name.contains('_') {
                continue; // already a rendition, never a source
            }
            for recipe in PREWARM {
                if let Some(target) = renditions::rendition_target(&p, &recipe) {
                    if !target.exists() {
                        plan.renditions
                            .push((format!("media/{name}"), recipe.clone()));
                    }
                }
            }
        }
    }

    Ok(plan)
}

/// A page is stale when missing, or when any of its inputs was modified
/// after it was written.
fn page_stale(page: &Path, root: &Path, slug: &str) -> bool {
    let page_mtime = match modified(page) {
        Some(t) => t,
        None => return true,
    };
    let inputs = [
        root.join("articles").join(slug).join("article.md"),
        root.join("articles").join(slug).join("meta.yaml"),
        root.join("theme.css"),
        root.join("templates").join("article.html"),
        root.join("publication.yaml"),
    ];
    inputs
        .iter()
        .any(|i| modified(i).is_some_and(|t| t > page_mtime))
}

fn modified(p: &Path) -> Option<std::time::SystemTime> {
    std::fs::metadata(p).and_then(|m| m.modified()).ok()
}

/// Execute a plan. Progress reports `(kind, done, total)` with kinds
/// `"render"` and `"rendition"`. Failures fall back gracefully: a rendition
/// that cannot derive simply re-derives on demand later; a page error stops
/// that plan (the next scan will retry it).
pub fn settle(
    publication_root: &Path,
    plan: &SettlePlan,
    progress: &mut dyn FnMut(&str, usize, usize),
) -> Result<usize> {
    let total = plan.total();
    let mut done = 0usize;

    for slug in &plan.renders {
        render::write_page(publication_root, slug)?;
        done += 1;
        progress("render", done, total);
    }
    if !plan.renders.is_empty() {
        render::write_index(publication_root)?;
        crate::spine::Journal::open(publication_root)?.record(crate::spine::Event::Rendered {
            pages: plan.renders.len(),
        })?;
    }
    // Remember the set these pages were built from; the next scan compares
    // against it. Cache maintenance, not user content: silent by design.
    let fp = published_fingerprint(publication_root)?;
    if let Some(p) = fingerprint_path(publication_root)?.parent() {
        std::fs::create_dir_all(p).ok();
    }
    crate::spine::atomic_write(&fingerprint_path(publication_root)?, fp.as_bytes())?;

    for (base, recipe) in &plan.renditions {
        let _ = renditions::resolve_for_display(publication_root, base, recipe.clone());
        done += 1;
        progress("rendition", done, total);
    }

    Ok(total)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::articles::Article;
    use tempfile::tempdir;

    fn png_bytes(width: u32, height: u32) -> Vec<u8> {
        let img = image::RgbaImage::from_pixel(width, height, image::Rgba([10, 20, 30, 255]));
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("t.png");
        img.save(&p).unwrap();
        std::fs::read(&p).unwrap()
    }

    fn publish_article(root: &Path, slug: &str) {
        let mut a = Article::create(root, slug, slug).unwrap();
        a.meta.state = State::Published;
        a.document = format!("# {slug}\n\n## A heading\n\nSome words.\n");
        a.save(root).unwrap();
    }

    #[test]
    fn set_changes_replan_neighbors_without_input_touches() {
        let dir = tempdir().unwrap();
        let mut a = Article::create(dir.path(), "alpha", "Alpha").unwrap();
        a.meta.state = State::Published;
        a.meta.date = Some("2026-01-01".into());
        a.save(dir.path()).unwrap();
        let mut b = Article::create(dir.path(), "beta", "Beta").unwrap();
        b.meta.state = State::Draft; // invisible to pages
        b.save(dir.path()).unwrap();

        settle(
            dir.path(),
            &scan_plan(dir.path()).unwrap(),
            &mut |_, _, _| {},
        )
        .unwrap();
        assert!(scan_plan(dir.path()).unwrap().is_empty());

        // Publishing beta moves alpha's next-link — alpha's own files never
        // changed, and the fingerprint alone must catch it.
        b.meta.state = State::Published;
        b.meta.date = Some("2026-02-01".into());
        b.save(dir.path()).unwrap();

        let plan = scan_plan(dir.path()).unwrap();
        assert!(plan.renders.contains(&"alpha".to_string()), "{plan:?}");
        assert!(plan.renders.contains(&"beta".to_string()));

        settle(dir.path(), &plan, &mut |_, _, _| {}).unwrap();
        assert!(scan_plan(dir.path()).unwrap().is_empty(), "idempotent");
    }

    #[test]
    fn missing_pages_are_planned_then_settled() {
        let dir = tempdir().unwrap();
        publish_article(dir.path(), "alpha");
        let mut draft = Article::create(dir.path(), "secret-draft", "Secret").unwrap();
        draft.meta.state = State::Draft;
        draft.save(dir.path()).unwrap();

        let plan = scan_plan(dir.path()).unwrap();
        assert_eq!(
            plan.renders,
            vec!["alpha".to_string()],
            "drafts are never planned"
        );

        let n = settle(dir.path(), &plan, &mut |_, _, _| {}).unwrap();
        assert_eq!(n, 1);
        assert!(dir.path().join("render/alpha.html").exists());

        // The index lists the publishable set only.
        let index = std::fs::read_to_string(dir.path().join("render/index.html")).unwrap();
        assert!(index.contains("alpha.html"));
        assert!(!index.contains("secret-draft"));

        // Settled is settled: the scan comes back empty.
        assert!(scan_plan(dir.path()).unwrap().is_empty());
    }

    #[test]
    fn stale_pages_are_replanned() {
        let dir = tempdir().unwrap();
        publish_article(dir.path(), "beta");
        settle(
            dir.path(),
            &scan_plan(dir.path()).unwrap(),
            &mut |_, _, _| {},
        )
        .unwrap();

        // The article moves forward in time; the page does not.
        let older = std::time::SystemTime::now() - std::time::Duration::from_secs(3600);
        let page = dir.path().join("render/beta.html");
        let f = std::fs::OpenOptions::new()
            .append(true)
            .open(&page)
            .unwrap();
        f.set_times(std::fs::FileTimes::new().set_modified(older))
            .unwrap();

        let plan = scan_plan(dir.path()).unwrap();
        assert_eq!(plan.renders, vec!["beta".to_string()]);
    }

    #[test]
    fn missing_renditions_are_planned_and_derived() {
        let dir = tempdir().unwrap();
        crate::media::store_identified(dir.path(), &png_bytes(800, 400), "shot.png").unwrap();

        let plan = scan_plan(dir.path()).unwrap();
        assert_eq!(plan.renditions.len(), 2, "thumb + 1024 planned");
        assert!(plan.renders.is_empty());

        settle(dir.path(), &plan, &mut |_, _, _| {}).unwrap();
        let media = dir.path().join("media");
        let count = std::fs::read_dir(&media).unwrap().count();
        assert_eq!(count, 3, "original + two renditions");
        assert!(scan_plan(dir.path()).unwrap().is_empty());
    }

    #[test]
    fn progress_walks_the_whole_plan() {
        let dir = tempdir().unwrap();
        publish_article(dir.path(), "gamma");
        crate::media::store_identified(dir.path(), &png_bytes(640, 320), "pic.png").unwrap();

        let plan = scan_plan(dir.path()).unwrap();
        let mut seen = Vec::new();
        settle(dir.path(), &plan, &mut |kind, done, total| {
            seen.push((kind.to_string(), done, total));
        })
        .unwrap();
        assert_eq!(
            seen.last().map(|(_, d, t)| (*d, *t)),
            Some((plan.total(), plan.total()))
        );
    }
}
