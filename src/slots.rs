//! Slots: the presentation language's grammar and evaluation.
//!
//! One pipeline serves every output (article page, index, feed, card, and
//! Write mode alike): parse a template into text and slots, evaluate each
//! slot against a gathered context, substitute. The five rules live here:
//! English slots, no logic, an empty slot renders zero bytes, one optional
//! hint after `|`, and mistakes whisper — an unknown slot renders empty plus
//! a note, and a missing `{{ARTICLE}}` falls back to plain flow. The page
//! never breaks.
//!
//! UPPERCASE marks the required slot (`{{ARTICLE}}`); lowercase slots are
//! optional. The registry below is the frozen v1 vocabulary and the single
//! source of truth for autocomplete, palettes, menus, and this renderer.

use crate::articles::State;
use crate::desk::DeskEntry;
use std::fmt::Write as _;

// ---------------------------------------------------------------------------
// Grammar
// ---------------------------------------------------------------------------

/// A parsed template: literal runs and slot references in original order.
#[derive(Debug, Clone, PartialEq)]
pub enum Part {
    Text(String),
    /// `{{name}}`, `{{name | hint}}`.
    Slot(RawSlot),
}

#[derive(Debug, Clone, PartialEq)]
pub struct RawSlot {
    /// The whole braces expression, byte-exact, for echo and menus.
    pub raw: String,
    pub name: String,
    /// Tokens between `|` and `}}`, trimmed (`["count:8"]`, `["pills"]`).
    pub hints: Vec<String>,
}

/// Parse a template. Stray or invalid brace expressions pass through as
/// literal text: parsing never destroys bytes.
pub fn parse_template(src: &str) -> Vec<Part> {
    let mut parts = Vec::new();
    let mut rest = src;
    while let Some(start) = rest.find("{{") {
        if start > 0 {
            parts.push(Part::Text(rest[..start].to_string()));
        }
        let after = &rest[start..];
        let Some(end_rel) = after.find("}}") else {
            parts.push(Part::Text(after.to_string()));
            return parts;
        };
        let raw = after[..end_rel + 2].to_string();
        match slot_of_raw(&raw) {
            Some(s) => parts.push(Part::Slot(s)),
            None => parts.push(Part::Text(raw)),
        }
        rest = &after[end_rel + 2..];
    }
    if !rest.is_empty() {
        parts.push(Part::Text(rest.to_string()));
    }
    parts
}

fn slot_of_raw(raw: &str) -> Option<RawSlot> {
    let inner = raw.strip_prefix("{{")?.strip_suffix("}}")?;
    let (name, hints) = match inner.split_once('|') {
        Some((n, h)) => (n.trim(), tokenize_hints(h)),
        None => (inner.trim(), Vec::new()),
    };
    valid_name(name)?;
    Some(RawSlot {
        raw: raw.to_string(),
        name: name.to_string(),
        hints,
    })
}

fn tokenize_hints(hint: &str) -> Vec<String> {
    hint.split(',')
        .map(|t| t.trim().to_string())
        .filter(|t| !t.is_empty())
        .collect()
}

/// English slots, one word shape: a leading letter, then letters, digits,
/// underscores, hyphens (as in `article-list`). Case is significant.
fn valid_name(name: &str) -> Option<()> {
    let ok = !name.is_empty()
        && name.chars().next().is_some_and(|c| c.is_ascii_alphabetic())
        && name
            .chars()
            .all(|c| c.is_ascii_alphanumeric() || c == '_' || c == '-');
    if ok {
        Some(())
    } else {
        None
    }
}

// ---------------------------------------------------------------------------
// Catalog: characterized entries — one schema drives everything
// ---------------------------------------------------------------------------
//
// The catalog is typed Rust tables, never configuration files. Every entry
// declares the menus it offers, each control's accepted values and default,
// where the element may be inserted, and one line of documentation. Parse
// validation, evaluation, Write-mode menus, insertion palettes, and
// autocomplete are all views over these tables.

/// Where an element may live. Insertion palettes filter by host.
#[derive(Debug, Clone, Copy, PartialEq)]
pub enum Host {
    /// In the prose flow beside {{ARTICLE}} or in body containers.
    Flow,
    /// Aside regions: rails, footers, navigation bands.
    Rail,
}

/// One menu control on an entry.
#[derive(Debug, Clone, PartialEq)]
pub struct OptionSpec {
    pub key: &'static str,
    pub label: &'static str,
    pub control: Control,
    /// The value implied when the hint is absent (or unrecognized).
    pub default: &'static str,
}

/// Menu controls are finite on purpose: options select content, CSS
/// selects appearance.
#[derive(Debug, Clone, PartialEq)]
pub enum Control {
    /// On/off (`author:on`, `author:off`).
    Toggle,
    /// One named value from a fixed list.
    Choice(&'static [&'static str]),
    /// A bounded count.
    Count { min: usize, max: usize },
}

#[derive(Debug, Clone)]
pub struct SlotDef {
    pub name: &'static str,
    /// One-line doc, shown by autocomplete and menus. Is documentation.
    pub doc: &'static str,
    /// Hosts where insertion may offer this entry.
    pub hosts: &'static [Host],
    /// Menu controls; empty for leaf projections that conduct nothing yet.
    pub options: &'static [OptionSpec],
}

const FLOW_ONLY: &[Host] = &[Host::Flow];
const RAIL_ONLY: &[Host] = &[Host::Rail];
const ANYWHERE: &[Host] = &[Host::Flow, Host::Rail];

/// Value aliases kept legal so every example the v1 ADR published still
/// parses exactly as signed.
fn canonicalize(key: &str, value: &str) -> String {
    match (key, value) {
        // date | iso
        ("date", "iso") => "format:iso".into(),
        ("date", "long") => "format:long".into(),
        // tags | text / pills
        ("tags", "text") => "style:text".into(),
        ("tags", "pills") => "style:pills".into(),
        // article-list | newest / around / similar (positional modes)
        ("article-list", "newest") => "list:newest".into(),
        ("article-list", "around") => "list:around".into(),
        ("article-list", "similar") => "list:similar".into(),
        _ => format!("{key}:{value}"),
    }
}

/// Canonicalize against an entry's catalog. ARTICLE is mode-bearing: its
/// first positional token may be a frame mode name; key:value tokens pass
/// through for the mode's own consumption.
pub fn canonical_hints(hints: &[String], entry_name: &str) -> Vec<String> {
    if entry_name == "ARTICLE" {
        return hints.to_vec();
    }
    let def_opts = options_of(entry_name);
    hints
        .iter()
        .map(|h| match h.split_once(':') {
            Some((k, v)) => canonicalize(k, v),
            None => {
                // Bare token: find the option whose value set contains it.
                match def_opts.iter().find(|o| match &o.control {
                    Control::Choice(vs) => vs.contains(&h.as_str()),
                    Control::Toggle => h == "on" || h == "off",
                    Control::Count { .. } => h.parse::<usize>().is_ok(),
                }) {
                    Some(o) => canonicalize(o.key, h),
                    None => h.clone(),
                }
            }
        })
        .collect()
}

/// The new raw expression when one slot's options are re-conducted: splices
/// the old expression's bytes for the composed new ones inside the template.
/// All other bytes stay untouched.
pub fn rewrite_slot_raw(template: &str, old_raw: &str, next_hints: &[String]) -> Option<String> {
    let start = template.find(old_raw)?;
    let inner_old = old_raw.strip_prefix("{{")?.strip_suffix("}}")?;
    let name = inner_old.split('|').next()?.trim().to_string();
    let mut raw = String::new();
    raw.push_str("{{");
    raw.push_str(&name);
    let mut canon = canonical_hints(next_hints, &name);
    if name == "ARTICLE" {
        // Keep mode token first, unmangled: it is positional vocabulary.
        canon = next_hints.to_vec();
    }
    if !canon.is_empty() {
        raw.push_str(&format!(" | {}", canon.join(", ")));
    }
    raw.push_str("}}");
    Some(format!(
        "{}{}{}",
        &template[..start],
        raw,
        &template[start + old_raw.len()..]
    ))
}

pub fn options_of(entry_name: &str) -> &'static [OptionSpec] {
    registry()
        .into_iter()
        .find(|d| d.name == entry_name)
        .map(|d| d.options)
        .unwrap_or(&[])
}

const DATE_OPTS: &[OptionSpec] = &[OptionSpec {
    key: "format",
    label: "Format",
    control: Control::Choice(&["long", "iso"]),
    default: "long",
}];

const TAGS_OPTS: &[OptionSpec] = &[OptionSpec {
    key: "style",
    label: "Style",
    control: Control::Choice(&["pills", "text"]),
    default: "pills",
}];

const COVER_OPTS: &[OptionSpec] = &[OptionSpec {
    key: "fit",
    label: "Image fit",
    control: Control::Choice(&["natural", "fill", "contain"]),
    default: "natural",
}];

const EXCERPT_OPTS: &[OptionSpec] = &[OptionSpec {
    key: "count",
    label: "Word count",
    control: Control::Count { min: 1, max: 200 },
    default: "40",
}];

const LIST_OPTS: &[OptionSpec] = &[
    OptionSpec {
        key: "list",
        label: "Selection",
        control: Control::Choice(&["newest", "around", "similar"]),
        default: "newest",
    },
    OptionSpec {
        key: "count",
        label: "How many",
        control: Control::Count { min: 1, max: 50 },
        default: "8",
    },
];

const FOOTER_OPTS: &[OptionSpec] = &[OptionSpec {
    key: "sticky",
    label: "Stick to viewport bottom",
    control: Control::Toggle,
    default: "on",
}];

/// {{ARTICLE}} carries frame modes and, when a mode declares them, the
/// fields that presentation shows. Conduct reads this table.
const ARTICLE_OPTS: &[OptionSpec] = &[
    OptionSpec {
        key: "mode",
        label: "Frame",
        control: Control::Choice(&["plain", "title-banner"]),
        default: "plain",
    },
    OptionSpec {
        key: "cover",
        label: "Cover treatment",
        control: Control::Choice(&["natural", "fill", "contain", "none"]),
        default: "natural",
    },
    OptionSpec {
        key: "author",
        label: "Author line",
        control: Control::Toggle,
        default: "on",
    },
    OptionSpec {
        key: "style",
        label: "Tags",
        control: Control::Choice(&["pills", "text", "off"]),
        default: "pills",
    },
    OptionSpec {
        key: "format",
        label: "Date",
        control: Control::Choice(&["long", "iso", "off"]),
        default: "long",
    },
];

/// ARTICLE's frame modes. `plain` is the absence of any mode hint.
const ARTICLE_MODES: &[&str] = &["plain", "title-banner"];

pub fn article_modes() -> &'static [&'static str] {
    ARTICLE_MODES
}

pub fn registry() -> Vec<SlotDef> {
    vec![
        SlotDef {
            name: "ARTICLE",
            doc: "Your writing. Modes dress its frame: title-banner.",
            hosts: FLOW_ONLY,
            options: ARTICLE_OPTS,
        },
        SlotDef {
            name: "title",
            doc: "The article title.",
            hosts: ANYWHERE,
            options: &[],
        },
        SlotDef {
            name: "standfirst",
            doc: "The standfirst line under the title, if any.",
            hosts: ANYWHERE,
            options: &[],
        },
        SlotDef {
            name: "date",
            doc: "Publish date.",
            hosts: ANYWHERE,
            options: DATE_OPTS,
        },
        SlotDef {
            name: "reading_time",
            doc: "Minutes to read, at least one.",
            hosts: ANYWHERE,
            options: &[],
        },
        SlotDef {
            name: "tags",
            doc: "The article's tags.",
            hosts: ANYWHERE,
            options: TAGS_OPTS,
        },
        SlotDef {
            name: "cover_img",
            doc: "The cover image as an img tag, if set.",
            hosts: ANYWHERE,
            options: COVER_OPTS,
        },
        SlotDef {
            name: "excerpt",
            doc: "First words of the prose, plain text.",
            hosts: ANYWHERE,
            options: EXCERPT_OPTS,
        },
        SlotDef {
            name: "toc",
            doc: "Section navigation; empty without sections.",
            hosts: RAIL_ONLY,
            options: &[],
        },
        SlotDef {
            name: "prev_link",
            doc: "Link to the previous (older) article.",
            hosts: ANYWHERE,
            options: &[],
        },
        SlotDef {
            name: "prev_title",
            doc: "Previous article's title.",
            hosts: ANYWHERE,
            options: &[],
        },
        SlotDef {
            name: "next_link",
            doc: "Link to the next (newer) article.",
            hosts: ANYWHERE,
            options: &[],
        },
        SlotDef {
            name: "next_title",
            doc: "Next article's title.",
            hosts: ANYWHERE,
            options: &[],
        },
        SlotDef {
            name: "home_link",
            doc: "Link back to the index.",
            hosts: ANYWHERE,
            options: &[],
        },
        SlotDef {
            name: "body_class",
            doc: "Context classes for the body tag, like is-article is-published.",
            hosts: FLOW_ONLY,
            options: &[],
        },
        SlotDef {
            name: "site_name",
            doc: "The space's display name.",
            hosts: ANYWHERE,
            options: &[],
        },
        SlotDef {
            name: "byline",
            doc: "Byline as readers see it.",
            hosts: ANYWHERE,
            options: &[],
        },
        SlotDef {
            name: "site_cta",
            doc: "Call-to-action anchor from publication.yaml (site_cta_url).",
            hosts: ANYWHERE,
            options: &[],
        },
        SlotDef {
            name: "article-list",
            doc: "Other published articles.",
            hosts: RAIL_ONLY,
            options: LIST_OPTS,
        },
        SlotDef {
            name: "items",
            doc: "The page's full item list (index outputs).",
            hosts: FLOW_ONLY,
            options: &[],
        },
        SlotDef {
            name: "site_url",
            doc: "Canonical site URL from publication.yaml (site_url).",
            hosts: FLOW_ONLY,
            options: &[],
        },
        SlotDef {
            name: "self_url",
            doc: "Canonical link to this article (site_url + slug, else relative).",
            hosts: FLOW_ONLY,
            options: &[],
        },
        SlotDef {
            name: "updated",
            doc: "Most recent publish date among listed items.",
            hosts: FLOW_ONLY,
            options: &[],
        },
        SlotDef {
            name: "footer",
            doc: "Space furniture from publication.yaml (footer: markdown text).",
            hosts: RAIL_ONLY,
            options: FOOTER_OPTS,
        },
    ]
}

pub fn known(name: &str) -> bool {
    registry().iter().any(|d| d.name == name)
}

/// ARTice-vs-component check used by compose: only ARTICLE carries modes.
pub fn is_article(name: &str) -> bool {
    name == "ARTICLE"
}

// ---------------------------------------------------------------------------
// Context
// ---------------------------------------------------------------------------

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
pub const MARK_OPEN_LEN: usize = 3;
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

/// The {{ARTICLE}} slot: its frame mode decides the projection. `plain`
/// emits the flow; `title-banner` re-projects the article's own frame into
/// a banner block and consumes H1/standfirst out of the flow, so nothing
/// double-renders. First banner wins; repeats fall back to plain flow.
fn article_value(slot: &RawSlot, ctx: &Ctx, banner_used: &mut bool) -> String {
    let hints = canonical_hints(&slot.hints, "ARTICLE");
    // A frame mode is either explicit (`mode:title-banner`) or a bare
    // vocabulary word from ARTICLE's own list.
    let mode = hints
        .iter()
        .rev()
        .find(|h| h.starts_with("mode:"))
        .and_then(|h| h.strip_prefix("mode:"))
        .map_or_else(
            || {
                let _ = &hints;
                let mode = hints
                    .iter()
                    .find(|h| article_modes().contains(&h.as_str()))
                    .cloned()
                    .unwrap_or_else(|| "plain".into());
                mode
            },
            str::to_string,
        );
    if mode != "title-banner" || *banner_used {
        return format!("<div class=\"article-prose\">{}</div>", ctx.flow_html);
    }
    *banner_used = true;

    // Canonicalize banner options with their catalog defaults; unknown
    // tokens render defaults — rule five — and are noted upstream.
    let fit = hint_value(&hints, "cover:")
        .or_else(|| hint_value(&hints, "fit:"))
        .unwrap_or("natural");
    if fit == "none" {
        // Mode without a cover treatment: plain presentation, frame still claimed.
        *banner_used = true;
        return format!(
            "<div class=\"article-prose\">{}</div>",
            strip_flow_frame(&ctx.flow_html)
        );
    }
    let tags_style = hint_value(&hints, "style:").unwrap_or("pills");
    let date_fmt = hint_value(&hints, "format:").unwrap_or("long");
    let show_author = hint_value(&hints, "author:")
        .map(|v| v == "on")
        .unwrap_or(true);

    let date_str = if date_fmt == "off" {
        None
    } else {
        ctx.raw_date.as_deref().map(|raw| {
            match (
                chrono::NaiveDate::parse_from_str(raw.trim(), "%Y-%m-%d"),
                date_fmt,
            ) {
                (Ok(_d), "iso") => raw.to_string(),
                (Ok(d), _) => d.format("%B %-d, %Y").to_string(),
                (_, _) => raw.to_string(),
            }
        })
    };

    let mut meta_bits: Vec<String> = Vec::new();
    if show_author && !ctx.byline.is_empty() {
        meta_bits.push(format!(
            "<span class=\"title-banner--author\">{}</span>",
            esc(&ctx.byline)
        ));
    }
    if let Some(d) = &date_str {
        meta_bits.push(format!(
            "<span class=\"title-banner--date\">{}</span>",
            esc(d)
        ));
    }
    if !ctx.tags.is_empty() && tags_style != "off" {
        let rendered = match tags_style {
            "text" => ctx
                .tags
                .iter()
                .map(|t| format!("#{t}"))
                .collect::<Vec<_>>()
                .join(", "),
            _ => ctx
                .tags
                .iter()
                .map(|t| format!("<span class=\"tagpill\">#{}</span>", esc(t)))
                .collect::<Vec<_>>()
                .join(" "),
        };
        meta_bits.push(format!(
            "<span class=\"title-banner--tags\">{rendered}</span>"
        ));
    }

    let cover_css = match (&ctx.cover_src, fit) {
        (Some(src), f) => {
            let fit_cls = match f {
                "fill" => " cover-fill",
                "contain" => " cover-contain",
                _ => "",
            };
            format!(
                "<div class=\"title-banner--cover{fit_cls}\" style=\"background-image:url('{src}')\" role=\"img\" aria-label=\"\"></div>",
                src = esc(src)
            )
        }
        _ => String::new(),
    };

    let title_html = esc(&ctx.title);
    let standfirst_html = match &ctx.standfirst {
        Some(sf) => format!("<p class=\"title-banner--standfirst\">{}</p>", esc(sf)),
        None => String::new(),
    };
    let meta_html = if meta_bits.is_empty() {
        String::new()
    } else {
        format!(
            "<div class=\"title-banner--meta\">{}</div>",
            meta_bits.join(" ")
        )
    };

    // The banner carries the frame; the flow below sheds its own.
    let body_only = strip_flow_frame(&ctx.flow_html);
    format!(
        "<section class=\"title-banner\">{cover_css}\
         <div class=\"title-banner--inner\"><h1 class=\"title-banner--title\">{title_html}</h1>\
         {standfirst_html}{meta_html}</div></section>\
         <div class=\"article-prose\">{body}</div>",
        body = body_only
    )
}

/// Remove a leading <h1>…</h1> and one following standfirst-shaped run from
/// compiled flow HTML. Used when the title banner has claimed the frame.
fn strip_flow_frame(flow: &str) -> String {
    let mut rest = flow.to_string();
    if let Some(p) = rest.find("<h1>") {
        if let Some(c) = rest[p..].find("</h1>") {
            rest = format!("{}{}", &rest[..p], &rest[p + c + 5..]);
        }
    }
    // The standfirst compiles to an emphasized paragraph right after the H1
    // only when it was a standalone _…_ line; consume at most that.
    let t = rest.trim_start();
    if let Some(after_p) = t.strip_prefix("<p><em>") {
        if let Some(end) = after_p.find("</em></p>") {
            return after_p[end + 9..].trim_start_matches('\n').to_string();
        }
    }
    rest
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

fn evaluate(slot: &RawSlot, ctx: &Ctx) -> (String, Vec<String>) {
    // Canonicalize against the entry's own catalog controls first; unknown
    // tokens pass through untouched and are the recognized-set's leftovers.
    let hints = canonical_hints(&slot.hints, &slot.name);
    let unknown = |recognized: &[&str]| -> Vec<String> {
        hints
            .iter()
            .filter(|h| !recognized.iter().any(|r| h.starts_with(r)))
            .map(|h| format!("hint \"{h}\" is not recognized"))
            .collect()
    };
    match slot.name.as_str() {
        "title" => (esc(&ctx.title), vec![]),
        "standfirst" => match &ctx.standfirst {
            Some(sf) => (format!("<p class=\"standfirst\">{}</p>", esc(sf)), vec![]),
            None => (String::new(), vec![]),
        },
        "date" => date_value(&hints, ctx.raw_date.as_deref()),
        "reading_time" => ((ctx.words / 220).max(1).to_string(), vec![]),
        "tags" => tags_value(&hints, &ctx.tags),
        "cover_img" => cover_value(&hints, ctx),
        "excerpt" => (excerpt_value(&hints, &ctx.body_md), vec![]),
        "toc" => (toc_value(&ctx.headings), vec![]),
        "prev_link" => neighbor_link(ctx.neighbors.prev.as_ref()),
        "prev_title" => neighbor_title(ctx.neighbors.prev.as_ref()),
        "next_link" => neighbor_link(ctx.neighbors.next.as_ref()),
        "next_title" => neighbor_title(ctx.neighbors.next.as_ref()),
        "home_link" => home_value(ctx),
        "body_class" => (body_class_value(ctx), vec![]),
        "site_name" => (esc(&ctx.site_name), vec![]),
        "byline" => (esc(&ctx.byline), vec![]),
        "site_cta" => cta_value(ctx),
        "article-list" => (list_value(&hints, ctx), vec![]),
        "items" => (
            match ctx.output {
                Output::Feed => feed_items(ctx),
                _ => list_markup(&ctx.publishable),
            },
            vec![],
        ),
        "site_url" => (esc(&ctx.site_url), vec![]),
        "self_url" => {
            let base = ctx.site_url.trim_end_matches('/');
            let link = if base.is_empty() {
                format!("{}.html", esc(&ctx.slug))
            } else {
                format!("{}/{}.html", esc(base), esc(&ctx.slug))
            };
            (link, vec![])
        }
        "updated" => updated_value(ctx),
        "footer" => footer_value(&hints, ctx, &unknown(&["sticky:"])),
        other => (String::new(), vec![format!("unknown slot {{{{{other}}}}}")]),
    }
}

/// First `key:`-prefixed value among canonical hints.
fn hint_value<'a>(hints: &'a [String], key: &str) -> Option<&'a str> {
    hints
        .iter()
        .find_map(|h| h.strip_prefix(key))
        .filter(|v| !v.is_empty())
}

/// Toggle semantics default on for footer (sticky is the designed baseline).
fn toggle_on(hints: &[String], key: &str) -> bool {
    !hints.iter().any(|h| h == &format!("{key}:off"))
}

fn footer_value(hints: &[String], ctx: &Ctx, unrecognized: &[String]) -> (String, Vec<String>) {
    let text = md_inline(if ctx.footer_md.is_empty() {
        return (String::new(), unrecognized.to_vec());
    } else {
        &ctx.footer_md
    });
    let sticky = toggle_on(hints, "sticky");
    let cls = if sticky {
        "site-footer site-footer--sticky"
    } else {
        "site-footer"
    };
    (
        format!("<div class=\"{cls}\">{text}</div>"),
        unrecognized.to_vec(),
    )
}

/// One inline-markdown run reduced to safe HTML: esc first, then the two
/// typographic marks footers actually use (emphasis + links stay plain text —
/// furniture carries words, not navigation).
pub fn md_inline(md: &str) -> String {
    let mut out = esc(md.trim());
    // _em_ and *em*
    for marker in ["_", "*"] {
        let mut rebuilt = String::with_capacity(out.len());
        let mut parts = out.split(marker);
        if let Some(first) = parts.next() {
            rebuilt.push_str(first);
        }
        for (i, part) in parts.enumerate() {
            if i % 2 == 0 && !part.is_empty() {
                rebuilt.push_str(&format!("<em>{part}</em>"));
            } else {
                rebuilt.push_str(part);
            }
        }
        out = rebuilt;
    }
    out
}

fn cover_value(hints: &[String], ctx: &Ctx) -> (String, Vec<String>) {
    const RECOGNIZED: [&str; 2] = ["fit:", "style:"];
    let un = hints
        .iter()
        .filter(|h| !RECOGNIZED.iter().any(|r| h.starts_with(r)))
        .map(|h| format!("hint \"{h}\" is not recognized"))
        .collect::<Vec<_>>();
    let Some(src) = &ctx.cover_src else {
        return (String::new(), un);
    };
    let fit = hint_value(hints, "fit:").unwrap_or("natural");
    let value = match fit {
        "fill" => format!("<img class=\"cover-img cover-img--fill\" src=\"{src}\" alt=\"\">"),
        "contain" => format!("<img class=\"cover-img cover-img--contain\" src=\"{src}\" alt=\"\">"),
        _ => format!("<img class=\"cover-img\" src=\"{src}\" alt=\"\">"),
    };
    (value, un)
}

fn neighbor_link(n: Option<&NeighborRef>) -> (String, Vec<String>) {
    match n {
        Some(r) => (
            format!(
                "<a class=\"neighbor-link\" href=\"{}.html\">{}</a>",
                esc(&r.slug),
                esc(&r.title)
            ),
            vec![],
        ),
        None => (String::new(), vec![]),
    }
}

fn neighbor_title(n: Option<&NeighborRef>) -> (String, Vec<String>) {
    match n {
        Some(r) => (esc(&r.title), vec![]),
        None => (String::new(), vec![]),
    }
}

fn home_value(ctx: &Ctx) -> (String, Vec<String>) {
    let label = if ctx.site_name.is_empty() {
        "Home".to_string()
    } else {
        ctx.site_name.clone()
    };
    (
        format!(
            "<a class=\"home-link\" href=\"index.html\">{}</a>",
            esc(&label)
        ),
        vec![],
    )
}

fn body_class_value(ctx: &Ctx) -> String {
    match ctx.output {
        Output::Article => {
            let mut classes = format!("is-article is-{}", ctx.state.as_str());
            if !ctx.headings.is_empty() {
                classes.push_str(" has-toc");
            }
            classes
        }
        Output::Index => "is-index".to_string(),
        Output::Feed => "is-feed".to_string(),
        Output::Card => "is-card".to_string(),
    }
}

fn cta_value(ctx: &Ctx) -> (String, Vec<String>) {
    match &ctx.cta {
        Some((label, url)) => (
            format!(
                "<section class=\"site-cta\"><a href=\"{}\" target=\"_blank\" \
                 rel=\"noopener noreferrer\">{} <span aria-hidden=\"true\">\u{2192}</span></a>\
                 </section>",
                esc(url),
                esc(label)
            ),
            vec![],
        ),
        None => (String::new(), vec![]),
    }
}

fn date_value(hints: &[String], raw: Option<&str>) -> (String, Vec<String>) {
    let wants_iso = hint_value(hints, "format:").is_some_and(|v| v == "iso");
    let Some(raw) = raw else {
        return (String::new(), vec![]);
    };
    let d = chrono::NaiveDate::parse_from_str(raw.trim(), "%Y-%m-%d");
    let value = match d {
        Ok(d) if !wants_iso => d.format("%B %-d, %Y").to_string(),
        _ => raw.to_string(),
    };
    (esc(&value), vec![])
}

fn tags_value(hints: &[String], tags: &[String]) -> (String, Vec<String>) {
    if tags.is_empty() {
        return (String::new(), vec![]);
    }
    let unrecognized: Vec<String> = hints
        .iter()
        .filter(|h| !h.starts_with("style:"))
        .map(|h| format!("hint \"{h}\" is not recognized"))
        .collect();
    let value = if hint_value(hints, "style:") == Some("text") {
        tags.iter()
            .map(|t| format!("#{t}"))
            .collect::<Vec<_>>()
            .join(", ")
    } else {
        tags.iter()
            .map(|t| format!("<span class=\"tagpill\">#{}</span>", esc(t)))
            .collect::<Vec<_>>()
            .join(" ")
    };
    (value, unrecognized)
}

fn toc_value(headings: &[Heading]) -> String {
    if headings.is_empty() {
        return String::new();
    }
    let mut out = String::from("<nav class=\"toc\">");
    for h in headings {
        let cls = if h.level == 3 { " class=\"l3\"" } else { "" };
        let _ = write!(
            out,
            "<a href=\"#{id}\"{cls}>{text}</a>",
            id = esc(&h.id),
            text = esc(&h.text)
        );
    }
    out.push_str("</nav>");
    out
}

fn excerpt_value(hints: &[String], body_md: &str) -> String {
    let mut want = hint_value(hints, "count:")
        .and_then(|v| v.parse().ok())
        .unwrap_or(40usize);
    for h in hints {
        if let Ok(v) = h.parse::<usize>() {
            want = v;
        }
    }
    let plain = md_plain(body_md);
    let words: Vec<&str> = plain.split_whitespace().collect();
    words
        .into_iter()
        .take(want.max(1))
        .collect::<Vec<_>>()
        .join(" ")
}

/// Reduce Markdown to readable plain text: drop images, keep link text,
/// shed emphasis markers.
fn md_plain(md: &str) -> String {
    let mut out = String::with_capacity(md.len());
    let b: Vec<char> = md.chars().collect();
    let mut i = 0;
    while i < b.len() {
        if b[i] == '!' && i + 1 < b.len() && b[i + 1] == '[' {
            i += 2;
            i = skip_balanced(&b, i);
            continue;
        }
        if b[i] == '[' {
            if let Some((text_start, text_end, after)) = scan_link(&b, i) {
                out.extend(b[text_start..text_end].iter());
                i = after;
                continue;
            }
        }
        out.push(b[i]);
        i += 1;
    }
    out.split_whitespace()
        .map(|w| {
            w.chars()
                .filter(|c| !matches!(c, '_' | '*' | '`' | '#' | '>' | '\\' | '[' | ']'))
                .collect::<String>()
        })
        .filter(|w| !w.is_empty())
        .collect::<Vec<_>>()
        .join(" ")
}

fn scan_link(b: &[char], start: usize) -> Option<(usize, usize, usize)> {
    let close_b = b[start..].iter().position(|&c| c == ']')? + start;
    if b.get(close_b + 1) != Some(&'(') {
        return None;
    }
    let close_p = b[close_b + 2..].iter().position(|&c| c == ')')? + close_b + 2;
    if b[start + 1..close_b].contains(&'[') || b[close_b + 2..close_p].contains(&')') {
        return None;
    }
    Some((start + 1, close_b, close_p + 1))
}

fn skip_balanced(b: &[char], after_bracket: usize) -> usize {
    let Some(close_b) = b[after_bracket..]
        .iter()
        .position(|&c| c == ']')
        .map(|p| p + after_bracket)
    else {
        return b.len();
    };
    if b.get(close_b + 1) != Some(&'(') {
        return close_b + 1;
    }
    b[close_b + 2..]
        .iter()
        .position(|&c| c == ')')
        .map_or(b.len(), |p| close_b + p + 3)
}

const LIST_CAP_DEFAULT: usize = 8;

fn list_count(hints: &[String]) -> Option<usize> {
    hints.iter().find_map(|h| {
        h.strip_prefix("count:")
            .and_then(|n| n.parse().ok())
            .or_else(|| h.parse().ok())
    })
}

/// `article-list`: other published articles, newest first. Ordered
/// selection: list:newest / around / similar, count:N.
fn list_value(hints: &[String], ctx: &Ctx) -> String {
    let mode = hint_value(hints, "list:").unwrap_or("newest");
    let entries: Vec<DeskEntry> = if mode == "similar" {
        similar_to(
            ctx.publishable.iter().filter(|e| e.slug != ctx.slug),
            &ctx.tags,
        )
    } else if mode == "around" {
        around(ctx)
    } else {
        let take = list_count(hints).unwrap_or(LIST_CAP_DEFAULT);
        ctx.publishable
            .iter()
            .filter(|e| e.slug != ctx.slug)
            .take(take)
            .cloned()
            .collect()
    };
    list_markup(&entries)
}

fn list_markup(entries: &[DeskEntry]) -> String {
    if entries.is_empty() {
        return String::new();
    }
    let mut out = String::from("<ul class=\"article-list\">");
    for e in entries {
        let date = e.date.clone().unwrap_or_default();
        let date_cell = if date.is_empty() {
            String::new()
        } else {
            format!("<span class=\"item-date\">{}</span>", esc(&date))
        };
        let _ = write!(
            out,
            "<li class=\"article-list-item\"><a href=\"{slug}.html\">{title}</a>{date_cell}</li>",
            slug = esc(&e.slug),
            title = esc(&e.title),
        );
    }
    out.push_str("</ul>");
    out
}

/// Ranked by shared-tag count with the current article, date breaking ties.
fn similar_to<'a>(
    others: impl Iterator<Item = &'a DeskEntry>,
    my_tags: &[String],
) -> Vec<DeskEntry> {
    let mut scored: Vec<(usize, DeskEntry)> = others
        .map(|e| {
            let shared = e.tags.iter().filter(|t| my_tags.contains(t)).count();
            (shared, e.clone())
        })
        .filter(|(shared, _)| *shared > 0)
        .collect();
    scored.sort_by(|a, b| b.0.cmp(&a.0).then(b.1.date.cmp(&a.1.date)));
    scored.truncate(LIST_CAP_DEFAULT);
    scored.into_iter().map(|(_, e)| e).collect()
}

/// A timeline window centered on the current article: up to two newer and
/// two older others.
fn around(ctx: &Ctx) -> Vec<DeskEntry> {
    let Some(i) = ctx.publishable.iter().position(|e| e.slug == ctx.slug) else {
        return vec![];
    };
    let lower = i.saturating_sub(2);
    let upper = (i + 3).min(ctx.publishable.len());
    ctx.publishable[lower..upper]
        .iter()
        .filter(|e| e.slug != ctx.slug)
        .cloned()
        .collect()
}

fn updated_value(ctx: &Ctx) -> (String, Vec<String>) {
    let latest = ctx.publishable.iter().filter_map(|e| e.date.clone()).max();
    match latest {
        Some(d) => (esc(&d), vec![]),
        None => (String::new(), vec![]),
    }
}

/// RSS <item> blocks, newest first. Links absolutize against site_url when
/// the space declares one; dates render RFC-2822 at noon UTC (date-only
/// sources, deterministic bytes). Everything escapes through esc.
fn feed_items(ctx: &Ctx) -> String {
    let rfc2822 = |date: &str| -> String {
        chrono::NaiveDate::parse_from_str(date.trim(), "%Y-%m-%d")
            .ok()
            .and_then(|d| d.and_hms_opt(12, 0, 0))
            .map(|dt| {
                use chrono::TimeZone;
                chrono::Utc.from_utc_datetime(&dt).to_rfc2822()
            })
            .unwrap_or_default()
    };

    let base = ctx.site_url.trim_end_matches('/');
    let mut out = String::new();
    for e in &ctx.publishable {
        let link = if base.is_empty() {
            format!("{}.html", esc(&e.slug))
        } else {
            format!("{}/{}.html", esc(base), esc(&e.slug))
        };
        let pub_date = e.date.as_deref().map(&rfc2822).unwrap_or_default();
        let pub_tag = if pub_date.is_empty() {
            String::new()
        } else {
            format!("<pubDate>{pub_date}</pubDate>")
        };
        let _ = write!(
            out,
            "<item><title>{t}</title><link>{l}</link>\
             <guid isPermaLink=\"true\">{l}</guid>{p}\
             <description></description></item>",
            t = esc(&e.title),
            l = link,
            p = pub_tag
        );
        out.push('\n');
    }
    out
}

fn esc(s: &str) -> String {
    s.replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::articles::State;

    fn entry(slug: &str, title: &str, date: Option<&str>, tags: &[&str]) -> DeskEntry {
        DeskEntry {
            slug: slug.into(),
            title: title.into(),
            state: State::Published,
            date: date.map(|d| d.into()),
            words: 100,
            links: vec![],
            dangling_links: vec![],
            tags: tags.iter().map(|t| t.to_string()).collect(),
        }
    }

    pub(crate) fn ctx_parts(flow: &str, publishable: Vec<DeskEntry>, slug: &str) -> Ctx {
        Ctx {
            output: Output::Article,
            slug: slug.into(),
            title: "Alpha".into(),
            standfirst: Some("An opening.".into()),
            raw_date: Some("2026-08-26".into()),
            words: 450,
            state: State::Published,
            tags: vec!["rust".into()],
            cover_src: Some("../media/c.png".into()),
            body_md: "Body words here.".into(),
            flow_html: flow.into(),
            headings: vec![],
            neighbors: Ctx::neighbors_for(&publishable, slug),
            site_name: "Field Notes".into(),
            byline: String::new(),
            cta: None,
            site_url: "https://example.com/".into(),
            footer_md: "\u{a9} 2026 Field Notes".into(),
            publishable,
            require_article: false,
        }
    }

    #[test]
    fn parses_slots_hints_and_literals() {
        let parts = parse_template("A {{title}} B\n{{article-list | count:8}}\n");
        assert_eq!(parts.len(), 5);
        assert_eq!(parts[0], Part::Text("A ".into()));
        assert_eq!(
            parts[1],
            Part::Slot(RawSlot {
                raw: "{{title}}".into(),
                name: "title".into(),
                hints: vec![],
            })
        );
        assert_eq!(
            parts[2],
            Part::Text(" B\n".into()),
            "newline between slots is literal"
        );
        let s = match &parts[3] {
            Part::Slot(s) => s.clone(),
            _ => panic!("expected slot"),
        };
        assert_eq!(s.name, "article-list");
        assert_eq!(s.hints, vec!["count:8"]);
        assert_eq!(parts[4], Part::Text("\n".into()));
    }

    #[test]
    fn stray_braces_pass_through_untouched() {
        let parts = parse_template("hello {{ world {{weird");
        assert!(parts.iter().all(|p| matches!(p, Part::Text(_))));
        let joined: String = parts
            .iter()
            .map(|p| match p {
                Part::Text(t) => t.clone(),
                _ => unreachable!(),
            })
            .collect();
        assert_eq!(joined, "hello {{ world {{weird");
    }

    #[test]
    fn unrecognized_inner_is_literal() {
        let parts = parse_template("x {{1bad}} y {{with space}} z");
        let texts: Vec<&str> = parts
            .iter()
            .filter_map(|p| match p {
                Part::Text(t) => Some(t.as_str()),
                _ => None,
            })
            .collect();
        assert_eq!(texts, vec!["x ", "{{1bad}}", " y ", "{{with space}}", " z"]);
    }

    #[test]
    fn empty_data_renders_zero_bytes() {
        let ctx = Ctx {
            standfirst: None,
            raw_date: None,
            tags: vec![],
            cover_src: None,
            neighbors: Neighbors::default(),
            cta: None,
            ..ctx_parts(
                "flow",
                vec![entry("alpha", "Alpha", Some("2026-08-01"), &[])],
                "alpha",
            )
        };
        let parts = parse_template("[{{standfirst}}{{date}}{{tags}}{{cover_img}}{{prev_link}}{{next_link}}{{site_cta}}{{toc}}]");
        let (html, notes) = compose(&parts, &ctx);
        assert_eq!(html, "[]");
        assert_eq!(notes, vec![] as Vec<String>);
    }

    #[test]
    fn unknown_slot_whispers_but_never_breaks_the_page() {
        let ctx = ctx_parts("flow", vec![], "alpha");
        let (html, notes) = compose(&parse_template("keep {{sparkle}} visible"), &ctx);
        assert_eq!(html, "keep  visible");
        assert_eq!(notes.len(), 1);
        assert!(notes[0].starts_with("unknown slot"));
    }

    #[test]
    fn missing_article_appends_flow_with_note() {
        let mut ctx = ctx_parts("FLOWBYTES", vec![], "alpha");
        ctx.require_article = true;
        let (html, notes) = compose(&parse_template("<p>frame</p>"), &ctx);
        assert!(html.contains("frame"));
        assert!(html.ends_with("FLOWBYTES</div>\n"), "{html}");
        assert_eq!(notes.len(), 1);
        assert!(notes[0].contains("no {{ARTICLE}}"));
    }

    #[test]
    fn required_slot_absence_is_accepted_on_index_output() {
        let mut ctx = ctx_parts("", vec![], "alpha");
        ctx.output = Output::Index;
        let (html, notes) = compose(&parse_template("plain"), &ctx);
        assert_eq!(html, "plain");
        assert_eq!(notes.len(), 0);
    }

    #[test]
    fn dates_format_long_by_default_and_iso_on_hint() {
        let ctx = ctx_parts("", vec![], "alpha");
        let (long, _) = compose(&parse_template("{{date}}"), &ctx);
        assert_eq!(long, "August 26, 2026");
        let (iso, _) = compose(&parse_template("{{date | iso}}"), &ctx);
        assert_eq!(iso, "2026-08-26");
    }

    #[test]
    fn unparsable_dates_pass_through_verbatim() {
        let mut ctx = ctx_parts("", vec![], "alpha");
        ctx.raw_date = Some("sometime last year".into());
        let (v, _) = compose(&parse_template("{{date}}"), &ctx);
        assert_eq!(v, "sometime last year");
    }

    #[test]
    fn tags_render_pills_by_default_text_on_hint() {
        let ctx = ctx_parts("", vec![], "alpha");
        let (pills, _) = compose(&parse_template("{{tags}}"), &ctx);
        assert_eq!(pills, "<span class=\"tagpill\">#rust</span>");
        let (text, _) = compose(&parse_template("{{tags | text}}"), &ctx);
        assert_eq!(text, "#rust");
    }

    #[test]
    fn unrecognised_tag_hint_is_noted_once() {
        let mut ctx = ctx_parts("", vec![], "alpha");
        ctx.tags = vec!["a".into(), "b".into()];
        let (v, notes) = compose(&parse_template("{{tags | sparkly}}"), &ctx);
        assert_eq!(
            v,
            "<span class=\"tagpill\">#a</span> <span class=\"tagpill\">#b</span>"
        );
        assert_eq!(
            notes,
            vec!["hint \"sparkly\" is not recognized".to_string()]
        );
    }

    #[test]
    fn toc_renders_nav_chain_and_ids_only_with_headings() {
        let mut ctx = ctx_parts("", vec![], "alpha");
        ctx.headings = vec![
            Heading {
                level: 2,
                text: "Part one".into(),
                id: "sec-1-part-one".into(),
            },
            Heading {
                level: 3,
                text: "Deep dive".into(),
                id: "sec-2-deep-dive".into(),
            },
        ];
        let (toc, _) = compose(&parse_template("{{toc}}"), &ctx);
        assert_eq!(
            toc,
            "<nav class=\"toc\"><a href=\"#sec-1-part-one\">Part one</a>\
             <a href=\"#sec-2-deep-dive\" class=\"l3\">Deep dive</a></nav>"
        );
        ctx.headings = vec![];
        let (none, _) = compose(&parse_template("{{toc}}"), &ctx);
        assert_eq!(none, "");
    }

    #[test]
    fn neighbors_run_older_prev_newer_next() {
        let pub_set = vec![
            entry("c-newest", "C", Some("2026-03-01"), &[]),
            entry("b-mid", "B", Some("2026-02-01"), &[]),
            entry("a-oldest", "A", Some("2026-01-01"), &[]),
        ];
        let mid = ctx_parts("", pub_set.clone(), "b-mid");
        let (link, _) = compose(&parse_template("{{prev_link}}"), &mid);
        assert_eq!(
            link,
            "<a class=\"neighbor-link\" href=\"a-oldest.html\">A</a>"
        );
        let (link, _) = compose(&parse_template("{{next_link}}"), &mid);
        assert_eq!(
            link,
            "<a class=\"neighbor-link\" href=\"c-newest.html\">C</a>"
        );

        let newest = ctx_parts("", pub_set.clone(), "c-newest");
        let (none, _) = compose(&parse_template("[{{next_link}}]"), &newest);
        assert_eq!(none, "[]", "the newest page has no next link");

        let oldest = ctx_parts("", pub_set, "a-oldest");
        let (none, _) = compose(&parse_template("[{{prev_link}}]"), &oldest);
        assert_eq!(none, "[]", "the oldest page has no prev link");
    }

    #[test]
    fn undated_articles_sink_in_chronology() {
        // Desk contract: newest first, undated last.
        let pub_set = vec![
            entry("dated", "D", Some("2026-01-01"), &[]),
            entry("undated", "U", None, &[]),
        ];
        let ctx = ctx_parts("", pub_set, "dated");
        assert!(ctx.neighbors.next.is_none(), "undated cannot be newer");
        assert_eq!(ctx.neighbors.prev.as_ref().unwrap().slug, "undated");
    }

    #[test]
    fn body_class_carries_output_state_and_toc_fact() {
        let mut ctx = ctx_parts("", vec![], "alpha");
        assert_eq!(
            compose(&parse_template("{{body_class}}"), &ctx).0,
            "is-article is-published"
        );
        ctx.state = State::Draft;
        assert_eq!(
            compose(&parse_template("{{body_class}}"), &ctx).0,
            "is-article is-draft"
        );
        ctx.headings = vec![Heading {
            level: 2,
            text: "P".into(),
            id: "s".into(),
        }];
        assert_eq!(
            compose(&parse_template("{{body_class}}"), &ctx).0,
            "is-article is-draft has-toc"
        );
        ctx.output = Output::Index;
        assert_eq!(
            compose(&parse_template("{{body_class}}"), &ctx).0,
            "is-index"
        );
    }

    #[test]
    fn article_list_excludes_self_newest_first_pinned_markup() {
        let pub_set = vec![
            entry("newest", "Newest", Some("2026-03-01"), &[]),
            entry("current", "Current", Some("2026-02-01"), &[]),
            entry("older", "Older", Some("2026-01-01"), &[]),
        ];
        let ctx = ctx_parts("", pub_set, "current");
        let (list, _) = compose(&parse_template("{{article-list | count:5}}"), &ctx);
        assert_eq!(
            list,
            "<ul class=\"article-list\"><li class=\"article-list-item\">\
             <a href=\"newest.html\">Newest</a><span class=\"item-date\">2026-03-01</span></li>\
             <li class=\"article-list-item\"><a href=\"older.html\">Older</a>\
             <span class=\"item-date\">2026-01-01</span></li></ul>"
        );
    }

    #[test]
    fn article_list_defaults_to_eight() {
        let pub_set: Vec<DeskEntry> = (0..12)
            .rev()
            .map(|i| {
                entry(
                    &format!("p{i:02}"),
                    &format!("P{i}"),
                    Some(&format!("2026-{i:02}-01")),
                    &[],
                )
            })
            .collect();
        let ctx = ctx_parts("", pub_set, "zz-current");
        let (list, _) = compose(&parse_template("{{article-list}}"), &ctx);
        assert_eq!(list.matches("<li ").count(), 8);
    }

    #[test]
    fn similar_ranks_shared_tags_then_date() {
        let mut rich = ctx_parts(
            "",
            vec![entry("seed", "S", Some("2026-01-01"), &[])],
            "seed",
        );
        rich.tags = vec!["rust".into(), "prose".into()];
        // Desk order: newest first.
        rich.publishable = vec![
            entry("unrelated", "Four", Some("2026-01-07"), &["gardening"]),
            entry("one-share-new", "Three", Some("2026-01-06"), &["rust"]),
            entry("two-shares", "Two", Some("2026-01-04"), &["rust", "prose"]),
            entry("one-share-old", "One", Some("2026-01-05"), &["rust"]),
        ];
        let (list, _) = compose(&parse_template("{{article-list | similar}}"), &rich);
        let pos = |slug: &str| list.find(&format!("{slug}.html")).expect(slug);
        let (two, newer, older) = (
            pos("two-shares"),
            pos("one-share-new"),
            pos("one-share-old"),
        );
        assert!(two < newer && newer < older, "rank order: {list}");
        assert!(!list.contains("unrelated.html"));
    }

    #[test]
    fn around_centers_a_window_on_the_current_article() {
        let mk = |i: u32| {
            entry(
                &format!("p{i:02}"),
                &format!("P{i}"),
                Some(&format!("2026-{:02}-01", i)),
                &[],
            )
        };
        let ctx = ctx_parts("", (1u32..=7).rev().map(mk).collect(), "p04");
        let (list, _) = compose(&parse_template("{{article-list | around}}"), &ctx);
        let got: Vec<&str> = ["p06", "p05", "p03", "p02"]
            .iter()
            .filter(|s| list.contains(&format!("{s}.html")))
            .copied()
            .collect();
        assert_eq!(got, vec!["p06", "p05", "p03", "p02"], "{list}");
        assert!(
            !list.contains("p04.html"),
            "never lists the current article"
        );
        assert!(
            !list.contains("p01.html") && !list.contains("p07.html"),
            "{list}"
        );
    }

    #[test]
    fn items_lists_everything_and_updated_takes_the_max() {
        let mut ctx = ctx_parts("", vec![], "index");
        ctx.output = Output::Index;
        ctx.publishable = vec![
            entry("b", "B", Some("2026-02-14"), &[]),
            entry("a", "A", Some("2026-05-30"), &[]),
        ];
        let (items, _) = compose(&parse_template("{{items}}"), &ctx);
        assert_eq!(
            items,
            "<ul class=\"article-list\"><li class=\"article-list-item\"><a href=\"b.html\">B</a>\
             <span class=\"item-date\">2026-02-14</span></li><li class=\"article-list-item\">\
             <a href=\"a.html\">A</a><span class=\"item-date\">2026-05-30</span></li></ul>"
        );
        let (upd, _) = compose(&parse_template("{{updated}}"), &ctx);
        assert_eq!(upd, "2026-05-30");
    }

    #[test]
    fn excerpt_plain_text_keeps_links_sheds_images_and_markers() {
        let mut ctx = ctx_parts("", vec![], "alpha");
        ctx.body_md = "Start **bold** and _soft_, see [the guide](https://x.io/a). \
                       ![shot](media/p.png)\n\nMore.\n"
            .to_string();
        let (v, _) = compose(&parse_template("{{excerpt | 10}}"), &ctx);
        assert_eq!(v, "Start bold and soft, see the guide. More.");
    }

    #[test]
    fn escaping_never_leaks_angle_brackets() {
        let mut ctx = ctx_parts("", vec![], "alpha");
        ctx.title = "<script>alert(1)</script>".to_string();
        ctx.site_name = "Tom & Jerry".to_string();
        let (t, _) = compose(&parse_template("{{title}} {{site_name}}"), &ctx);
        assert_eq!(t, "&lt;script&gt;alert(1)&lt;/script&gt; Tom &amp; Jerry");
    }

    #[test]
    fn feed_items_absolutize_and_date_rfc2822() {
        let mut ctx = ctx_parts(
            "",
            vec![
                entry("b", "B", Some("2026-02-14"), &[]),
                entry("a", "A", Some("2026-05-30"), &[]),
            ],
            "index",
        );
        ctx.output = Output::Feed;
        ctx.site_url = "https://example.com/".into();
        let (items, notes) = compose(&parse_template("{{items}}"), &ctx);
        assert_eq!(notes.len(), 0);
        assert!(
            items.contains("<item><title>B</title><link>https://example.com/b.html</link>"),
            "{items}"
        );
        assert!(items.contains("<guid isPermaLink=\"true\">https://example.com/b.html</guid>"));
        // 2026-02-14 noon UTC → Sat, 14 Feb 2026 12:00:00 +0000
        assert!(
            items.contains("<pubDate>Sat, 14 Feb 2026 12:00:00 +0000</pubDate>"),
            "{items}"
        );
    }

    #[test]
    fn feed_without_site_url_stays_relative_not_broken() {
        let mut ctx = ctx_parts("", vec![entry("b", "B", None, &[])], "index");
        ctx.output = Output::Feed;
        ctx.site_url = String::new();
        let (items, _) = compose(&parse_template("{{items}}"), &ctx);
        assert!(items.contains("<link>b.html</link>"), "{items}");
    }

    #[test]
    fn multi_instance_slots_each_resolve() {
        let ctx = ctx_parts("flow", vec![], "alpha");
        let (html, notes) = compose(&parse_template("{{title}} — {{title}}"), &ctx);
        assert_eq!(html, "Alpha — Alpha");
        assert_eq!(notes.len(), 0);
    }

    // -- catalog-era behaviors ------------------------------------------------

    #[test]
    fn bare_hint_aliases_canonicalize_to_key_value() {
        let ctx = ctx_parts("", vec![], "alpha");
        let (bare, _) = compose(&parse_template("{{date | iso}}"), &ctx);
        let (full, _) = compose(&parse_template("{{date | format:iso}}"), &ctx);
        assert_eq!(bare, full);
        assert_eq!(bare, "2026-08-26");

        let (bare, _) = compose(&parse_template("{{tags | text}}"), &ctx);
        let (full, _) = compose(&parse_template("{{tags | style:text}}"), &ctx);
        assert_eq!(bare, full);
        assert_eq!(bare, "#rust");

        let (bare, _) = compose(&parse_template("{{article-list | similar}}"), &ctx);
        let (full, _) = compose(&parse_template("{{article-list | list:similar}}"), &ctx);
        assert_eq!(bare, full);
    }

    #[test]
    fn footer_renders_space_yaml_text_sticky_by_default() {
        let ctx = ctx_parts("", vec![], "alpha");
        let (html, notes) = compose(&parse_template("{{footer}}"), &ctx);
        assert!(
            html.contains("<div class=\"site-footer site-footer--sticky\">"),
            "{html}"
        );
        assert!(
            !html.contains("&copy;") && html.contains("\u{a9}"),
            "{html}"
        );
        assert_eq!(notes.len(), 0);

        let (plain, _) = compose(&parse_template("{{footer | sticky:off}}"), &ctx);
        assert!(plain.contains("class=\"site-footer\""), "{plain}");
        assert!(!plain.contains("--sticky"));
    }

    #[test]
    fn empty_footer_is_zero_bytes_not_a_broken_page() {
        let mut ctx = ctx_parts("", vec![], "alpha");
        ctx.footer_md = String::new();
        let (html, notes) = compose(&parse_template("[{{footer}}]"), &ctx);
        assert_eq!(html, "[]");
        assert_eq!(notes.len(), 0);
    }

    #[test]
    fn cover_fit_choice_selects_named_markup_shapes() {
        let ctx = ctx_parts("", vec![], "alpha");
        let (natural, _) = compose(&parse_template("{{cover_img}}"), &ctx);
        assert!(natural.contains("<img class=\"cover-img\" "), "{natural}");
        let (fill, _) = compose(&parse_template("{{cover_img | fill}}"), &ctx);
        assert!(fill.contains("cover-img--fill"), "{fill}");
        let (contain, _) = compose(&parse_template("{{cover_img | fit:contain}}"), &ctx);
        assert!(contain.contains("cover-img--contain"), "{contain}");
    }

    #[test]
    fn title_banner_mode_takes_over_the_frame_once() {
        let flow = "<h1>On Rust</h1>\n<p><em>A meditation.</em></p>\n<h2 id=\"sec-1-x\">X</h2>\n";
        let ctx = ctx_parts(flow, vec![], "alpha");
        let template = "{{ARTICLE | title-banner}}{{ARTICLE}}";
        let (html, notes) = compose(&parse_template(template), &ctx);

        // Exactly one banner; it owns title and standfirst.
        assert_eq!(html.matches("class=\"title-banner\"").count(), 1, "{html}");
        assert!(
            html.contains("<h1 class=\"title-banner--title\">Alpha</h1>"),
            "{html}"
        );
        assert!(
            html.contains("title-banner--standfirst\">An opening.</p>"),
            "{html}"
        );
        assert_eq!(notes.len(), 0, "{notes:?}");

        // Exactly two prose wrappers (one per ARTICLE instance).
        let wrap = "<div class=\"article-prose\">";
        let first_at = html.find(wrap).expect("first wrapper");
        let second_at = html[first_at + wrap.len()..]
            .find(wrap)
            .map(|p| p + first_at + wrap.len())
            .expect("second wrapper");

        // The first instance's flow shed the frame entirely.
        let first_body = &html[first_at..second_at];
        assert!(!first_body.contains("<h1>"), "frame leaked: {first_body}");
        assert!(first_body.contains("sec-1-x"), "{first_body}");

        // The mirror instance keeps plain prose — including its H1.
        assert!(html[second_at..].contains("<h1>On Rust</h1>\n"), "{html}");
    }

    #[test]
    fn banner_without_cover_or_tags_stays_whole() {
        let mut ctx = ctx_parts("<h1>On Rust</h1>\n", vec![], "alpha");
        ctx.cover_src = None;
        ctx.tags = vec![];
        ctx.standfirst = None;
        ctx.raw_date = None; // nothing known: no meta row at all
        let (html, notes) = compose(&parse_template("{{ARTICLE | title-banner}}"), &ctx);
        assert!(
            html.contains("<h1 class=\"title-banner--title\">Alpha</h1>"),
            "{html}"
        );
        assert!(!html.contains("title-banner--cover"), "{html}");
        assert!(!html.contains("--standfirst"), "{html}");
        assert!(!html.contains("--meta"), "{html}");
        assert_eq!(notes.len(), 0);
        assert!(
            !html.contains("article-prose\"><h1>"),
            "flow sheds its H1 too"
        );
    }

    #[test]
    fn conducted_choice_splices_exact_bytes_in_the_draft() {
        let tpl = "<body>{{tags}}</body>";
        let next =
            rewrite_slot_raw(tpl, "{{tags}}", &["style:text".to_string()]).expect("raw found");
        assert_eq!(next, "<body>{{tags | style:text}}</body>");

        // Bare alias input canonicalizes on write-back.
        let next2 =
            rewrite_slot_raw(&next, "{{tags | style:text}}", &["text".to_string()]).unwrap();
        assert_eq!(next2, "<body>{{tags | style:text}}</body>");

        // A second instance stays where it is; only the matched raw changes.
        let two = "{{date}}, {{date}}";
        let one_changed = rewrite_slot_raw(two, "{{date}}", &["iso".to_string()]).unwrap();
        assert_eq!(one_changed, "{{date | format:iso}}, {{date}}");

        let missing = rewrite_slot_raw(tpl, "{{sparkle}}", &["x".into()]);
        assert!(missing.is_none());
    }
}
