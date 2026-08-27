use super::*;
use crate::articles::State;
use crate::desk::DeskEntry;
use std::fmt::Write as _;
trait Fallback {
    fn insert_fallback(&mut self, flow: &str);
}

impl Fallback for String {
    fn insert_fallback(&mut self, flow: &str) {
        let block = format!("\n<div class=\"article-prose\">{flow}</div>\n");
        if let Some(p) = self.rfind("</body>") {
            self.insert_str(p, &block);
        } else {
            self.push_str(&block);
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub enum Output {
    Article,
    Index,
    /// RSS channel: items compose as pre-built <item> blocks; links
    /// absolutize against site_url.
    Feed,
    /// Per-article embeddable snippet (render/<slug>.card.html).
    Card,
}

/// A section heading bound to its emitted id.
#[derive(Debug, Clone, PartialEq)]
pub struct Heading {
    pub level: u8,
    pub text: String,
    pub id: String,
}

#[derive(Debug, Clone, Default)]
pub struct Neighbors {
    pub prev: Option<NeighborRef>,
    pub next: Option<NeighborRef>,
}

#[derive(Debug, Clone)]
pub struct NeighborRef {
    pub slug: String,
    pub title: String,
}

/// Everything a template could ask about, gathered once.
#[derive(Debug, Clone)]
pub struct Ctx {
    pub output: Output,
    pub slug: String,
    pub title: String,
    pub standfirst: Option<String>,
    pub raw_date: Option<String>,
    pub words: usize,
    pub state: State,
    pub tags: Vec<String>,
    /// Ready-to-use src attribute value for the cover, resolved for display.
    pub cover_src: Option<String>,
    /// Body prose without frame, for excerpts.
    pub body_md: String,
    /// The compiled article flow, H1 through end: what {{ARTICLE}} emits.
    pub flow_html: String,
    pub headings: Vec<Heading>,
    pub neighbors: Neighbors,
    pub site_name: String,
    pub byline: String,
    pub cta: Option<(String, String)>, // (label, url)
    pub site_url: String,
    /// Space furniture from publication.yaml `footer:` — unmodeled key,
    /// preserved verbatim by the identity extras machinery.
    pub footer_md: String,
    /// Publishable set, newest first, undated last.
    pub publishable: Vec<DeskEntry>,
    /// Only true for real article-page assembly: there a template without
    /// `{{ARTICLE}}` gains its flow appended plus an editor note. Pure
    /// slot evaluation contexts leave this off.
    pub require_article: bool,
    /// The space's Header Style is Banner: a `title-banner` hint renders
    /// the hero and consumes title + standfirst from the flow. Normal
    /// keeps the raw flow regardless of template hints.
    pub banner: bool,
}

impl Ctx {
    /// Surrounding published articles, chronologically ascending (oldest
    /// first, undated last): previous means older, next means newer.
    pub fn neighbors_for(entries_desc: &[DeskEntry], slug: &str) -> Neighbors {
        let mut asc = entries_desc.to_vec();
        asc.reverse();
        let pos = asc.iter().position(|e| e.slug == slug);
        let Some(i) = pos else {
            return Neighbors::default();
        };
        Neighbors {
            prev: i.checked_sub(1).map(|p| NeighborRef {
                slug: asc[p].slug.clone(),
                title: asc[p].title.clone(),
            }),
            next: asc.get(i + 1).map(|n| NeighborRef {
                slug: n.slug.clone(),
                title: n.title.clone(),
            }),
        }
    }
}

// ---------------------------------------------------------------------------
// Evaluation
// ---------------------------------------------------------------------------

/// Invisible marker characters wrapping Write-mode tokens. Chosen from the
/// Unicode punctuation block so authored bytes never collide.
pub const MARK_OPEN: &str = "\u{2063}";

pub const MARK_CLOSE: &str = "\u{2064}";

pub const MARK_CLOSE_LEN: usize = 3;

/// One evaluated slot occurrence recorded for the Write plane.
#[derive(Debug, Clone)]
pub struct SlotTok {
    pub name: String,
    pub raw: String,
    pub hints: Vec<String>,
    pub html: String,
}

/// Compose with invisible tokens where `{{ARTICLE}}` and every known slot
/// would land, returning the token registry in emit order. The shell stays
/// byte-honest; the Write plane slices later. Unknown slots whisper as
/// usual but leave no token — they render empty everywhere by rule two.
pub fn compose_marked(parts: &[Part], ctx: &Ctx) -> (String, Vec<String>, Vec<SlotTok>) {
    let mut out = String::new();
    let mut notes = Vec::new();
    let mut toks: Vec<SlotTok> = Vec::new();
    let mut saw_article = false;
    let mut banner_used = false;

    for part in parts {
        match part {
            Part::Text(t) => out.push_str(t),
            Part::Slot(slot) => {
                if slot.name == "ARTICLE" {
                    saw_article = true;
                    let _ = write!(
                        out,
                        "{MARK_OPEN}A{toks_len}{MARK_CLOSE}",
                        toks_len = toks.len()
                    );
                    toks.push(SlotTok {
                        name: slot.name.clone(),
                        raw: slot.raw.clone(),
                        hints: slot.hints.clone(),
                        html: article_value(slot, ctx, &mut banner_used),
                    });
                    continue;
                }
                if !known(&slot.name) {
                    let note = format!("unknown slot {} rendered empty", slot.raw);
                    if !notes.contains(&note) {
                        notes.push(note);
                    }
                    continue;
                }
                let (value, noted) = evaluate(slot, ctx);
                for n in noted {
                    if !notes.contains(&n) {
                        notes.push(n);
                    }
                }
                let _ = write!(
                    out,
                    "{MARK_OPEN}S{toks_len}{MARK_CLOSE}",
                    toks_len = toks.len()
                );
                toks.push(SlotTok {
                    name: slot.name.clone(),
                    raw: slot.raw.clone(),
                    hints: slot.hints.clone(),
                    html: value,
                });
            }
        }
    }

    if ctx.require_article && !saw_article {
        let token = format!("{MARK_OPEN}A{}{MARK_CLOSE}", toks.len());
        // Before </body>, never after: downstream slicing keeps only the
        // written body region.
        match out.rfind("</body>") {
            Some(p) => out.insert_str(p, &token),
            None => out.push_str(&token),
        }
        toks.push(SlotTok {
            name: "ARTICLE".into(),
            raw: "{{ARTICLE}}".into(),
            hints: vec![],
            html: ctx.flow_html.clone(),
        });
        notes.insert(
            0,
            "template has no {{ARTICLE}}; the article flow was appended \
             at the end"
                .into(),
        );
    }
    (out, notes, toks)
}

/// Substitute every slot, returning composed HTML plus editor notes. Empty
/// data renders zero bytes; unknowns render empty and note themselves once.
pub fn compose(parts: &[Part], ctx: &Ctx) -> (String, Vec<String>) {
    let mut out = String::new();
    let mut notes = Vec::new();
    let mut saw_article = false;
    let mut banner_used = false;

    for part in parts {
        match part {
            Part::Text(t) => out.push_str(t),
            Part::Slot(slot) => {
                if slot.name == "ARTICLE" {
                    saw_article = true;
                    out.push_str(&article_value(slot, ctx, &mut banner_used));
                    continue;
                }
                if !known(&slot.name) {
                    out.push_str("");
                    let note = format!("unknown slot {} rendered empty", slot.raw);
                    if !notes.contains(&note) {
                        notes.push(note);
                    }
                    continue;
                }
                let (value, noted) = evaluate(slot, ctx);
                out.push_str(&value);
                for n in noted {
                    if !notes.contains(&n) {
                        notes.push(n);
                    }
                }
            }
        }
    }

    if ctx.require_article && !saw_article {
        out.insert_fallback(&ctx.flow_html);
        notes.insert(
            0,
            "template has no {{ARTICLE}}; the article flow was appended \
             at the end"
                .into(),
        );
    }
    (out, notes)
}
