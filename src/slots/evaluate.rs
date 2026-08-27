use super::*;
use crate::desk::DeskEntry;
use std::fmt::Write as _;
pub(crate) fn evaluate(slot: &RawSlot, ctx: &Ctx) -> (String, Vec<String>) {
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
            // The standfirst is raw markdown (positional first paragraph);
            // inline-render it so emphasis and links carry into the class.
            Some(sf) => (
                format!("<p class=\"standfirst\">{}</p>", md_inline(sf)),
                vec![],
            ),
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

pub(crate) fn footer_value(
    hints: &[String],
    ctx: &Ctx,
    unrecognized: &[String],
) -> (String, Vec<String>) {
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

pub(crate) fn cover_value(hints: &[String], ctx: &Ctx) -> (String, Vec<String>) {
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

pub(crate) fn neighbor_link(n: Option<&NeighborRef>) -> (String, Vec<String>) {
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

pub(crate) fn neighbor_title(n: Option<&NeighborRef>) -> (String, Vec<String>) {
    match n {
        Some(r) => (esc(&r.title), vec![]),
        None => (String::new(), vec![]),
    }
}

pub(crate) fn home_value(ctx: &Ctx) -> (String, Vec<String>) {
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

pub(crate) fn body_class_value(ctx: &Ctx) -> String {
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

pub(crate) fn cta_value(ctx: &Ctx) -> (String, Vec<String>) {
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

pub(crate) fn date_value(hints: &[String], raw: Option<&str>) -> (String, Vec<String>) {
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

pub(crate) fn tags_value(hints: &[String], tags: &[String]) -> (String, Vec<String>) {
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

pub(crate) fn toc_value(headings: &[Heading]) -> String {
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

pub(crate) fn excerpt_value(hints: &[String], body_md: &str) -> String {
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
pub(crate) fn md_plain(md: &str) -> String {
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

pub(crate) fn scan_link(b: &[char], start: usize) -> Option<(usize, usize, usize)> {
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

pub(crate) fn skip_balanced(b: &[char], after_bracket: usize) -> usize {
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

pub(crate) const LIST_CAP_DEFAULT: usize = 8;

pub(crate) fn list_count(hints: &[String]) -> Option<usize> {
    hints.iter().find_map(|h| {
        h.strip_prefix("count:")
            .and_then(|n| n.parse().ok())
            .or_else(|| h.parse().ok())
    })
}

/// `article-list`: other published articles, newest first. Ordered
/// selection: list:newest / around / similar, count:N.
pub(crate) fn list_value(hints: &[String], ctx: &Ctx) -> String {
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

pub(crate) fn list_markup(entries: &[DeskEntry]) -> String {
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
pub(crate) fn similar_to<'a>(
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
pub(crate) fn around(ctx: &Ctx) -> Vec<DeskEntry> {
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

pub(crate) fn updated_value(ctx: &Ctx) -> (String, Vec<String>) {
    let latest = ctx.publishable.iter().filter_map(|e| e.date.clone()).max();
    match latest {
        Some(d) => (esc(&d), vec![]),
        None => (String::new(), vec![]),
    }
}

/// RSS <item> blocks, newest first. Links absolutize against site_url when
/// the space declares one; dates render RFC-2822 at noon UTC (date-only
/// sources, deterministic bytes). Everything escapes through esc.
pub(crate) fn feed_items(ctx: &Ctx) -> String {
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

pub(crate) fn esc(s: &str) -> String {
    s.replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
}
