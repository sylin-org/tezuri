//!  Context gathering: everything a template could ask about, once.
use super::*;
use crate::articles::Article;
use crate::articles::State;
use crate::slots::{self, Ctx, Output};
use crate::spine::confine;
use anyhow::Result;
use serde_yaml;
use std::path::Path;
pub(crate) fn cover_src(publication_root: &Path, cover: &Option<String>) -> Option<String> {
    let c = cover
        .as_deref()?
        .trim()
        .trim_start_matches("./")
        .to_string();
    if c.is_empty() || !c.contains('/') {
        return None;
    }
    let abs = confine(publication_root, Path::new(&c)).ok()?;
    if !abs.exists() {
        return None;
    }
    if let (Some(stem), Some(ext)) = (abs.file_stem(), abs.extension()) {
        let rendition = format!("{}_1024.{}", stem.to_string_lossy(), ext.to_string_lossy());
        let rel_dir = Path::new(&c).parent().map(|p| p.to_path_buf());
        if let Some(dir) = rel_dir {
            let cand = dir.join(rendition);
            if confine(publication_root, &cand)
                .ok()
                .is_some_and(|p| p.exists())
            {
                return Some(format!("../{}", cand.to_string_lossy().replace('\\', "/")));
            }
        }
    }
    Some(format!("../{c}"))
}

pub(crate) fn site_display_name(publication_root: &Path, name: &str) -> String {
    if !name.is_empty() {
        return name.to_string();
    }
    publication_root
        .file_name()
        .and_then(|s| s.to_str())
        .unwrap_or("A space")
        .to_string()
}

/// Gather article context: files → identity → publishable set → neighbors.
pub fn gather_article_ctx(publication_root: &Path, slug: &str) -> Result<Ctx> {
    let a = Article::load(publication_root, slug)?;
    let identity = crate::identity::Identity::load(publication_root)?;
    let (flow_html, headings) = compile_flow(&a.document);

    // Excerpt works from prose without the frame.
    let body_md = strip_frame(&a.document).to_string();

    let publishable = publishable_entries(publication_root)?;
    let neighbors = slots::Ctx::neighbors_for(&publishable, slug);

    let cta = site_cta_of(&identity);
    let site_url = extras_str(&identity.extra, &["site_url", "url"]);

    Ok(Ctx {
        output: Output::Article,
        slug: slug.to_string(),
        title: a.title(),
        standfirst: a.standfirst(),
        raw_date: a.meta.date.clone(),
        words: a.word_count(),
        state: a.meta.state,
        tags: a.meta.tags.clone(),
        cover_src: cover_src(publication_root, &a.meta.cover),
        body_md,
        flow_html,
        headings,
        neighbors,
        site_name: site_display_name(publication_root, &identity.name),
        byline: a
            .meta
            .author
            .clone()
            .filter(|s| !s.trim().is_empty())
            .map(|s| s.trim().to_string())
            .unwrap_or_else(|| {
                if identity.byline.is_empty() {
                    identity.persona.clone()
                } else {
                    identity.byline.clone()
                }
            }),
        banner: identity.header_style() == crate::identity::HeaderStyle::Banner,
        cta,
        site_url,
        footer_md: extras_str(&identity.extra, &["footer"]),
        publishable,
        require_article: true,
    })
}

pub(crate) fn extras_str(
    extra: &std::collections::BTreeMap<String, serde_yaml::Value>,
    keys: &[&str],
) -> String {
    for k in keys {
        if let Some(v) = extra.get(*k).and_then(|v| v.as_str()) {
            return v.trim().to_string();
        }
    }
    String::new()
}

/// A call-to-action from the space's own publication.yaml: modeled first,
/// the earlier discord-specific key kept working.
pub(crate) fn site_cta_of(identity: &crate::identity::Identity) -> Option<(String, String)> {
    if let Some(url) = extras_str(&identity.extra, &["site_cta_url"]).into_option() {
        let label = extras_str(&identity.extra, &["site_cta_label"]);
        return Some((
            if label.is_empty() {
                "Read more".into()
            } else {
                label
            },
            url,
        ));
    }
    let discord = extras_str(&identity.extra, &["discord"]);
    (!discord.is_empty()).then(|| ("Discuss on Discord".into(), discord))
}

trait IntoOption {
    fn into_option(self) -> Option<String>;
}

impl IntoOption for String {
    fn into_option(self) -> Option<String> {
        (!self.is_empty()).then_some(self)
    }
}

/// Publishable set, newest first, undated last — the desk's own ordering.
pub(crate) fn publishable_entries(publication_root: &Path) -> Result<Vec<crate::desk::DeskEntry>> {
    Ok(crate::desk::Desk::rebuild(publication_root)?
        .entries
        .into_iter()
        .filter(|e| e.state == State::Published)
        .collect())
}

// ---------------------------------------------------------------------------
// Public surface
// ---------------------------------------------------------------------------

/// Site-level context shared by the index and feed outputs.
pub(crate) fn site_ctx(publication_root: &Path) -> Result<(crate::identity::Identity, Ctx)> {
    let identity = crate::identity::Identity::load(publication_root)?;
    let publishable = publishable_entries(publication_root)?;
    let name = site_display_name(publication_root, &identity.name);
    let byline = if identity.byline.is_empty() {
        identity.persona.clone()
    } else {
        identity.byline.clone()
    };
    let ctx = Ctx {
        output: Output::Index,
        slug: String::new(),
        title: name.clone(),
        standfirst: None,
        raw_date: None,
        words: 0,
        state: State::Published,
        tags: vec![],
        banner: false,
        cover_src: None,
        body_md: String::new(),
        flow_html: String::new(),
        headings: vec![],
        neighbors: Default::default(),
        site_name: name,
        byline,
        cta: site_cta_of(&identity),
        site_url: extras_str(&identity.extra, &["site_url", "url"]),
        footer_md: extras_str(&identity.extra, &["footer"]),
        publishable,
        require_article: false,
    };
    Ok((identity, ctx))
}
