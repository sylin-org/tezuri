use super::*;
pub(crate) fn article_value(slot: &RawSlot, ctx: &Ctx, banner_used: &mut bool) -> String {
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
    if mode != "title-banner" || *banner_used || !ctx.banner {
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
pub(crate) fn strip_flow_frame(flow: &str) -> String {
    let mut rest = flow.to_string();
    if let Some(p) = rest.find("<h1>") {
        if let Some(c) = rest[p..].find("</h1>") {
            rest = format!("{}{}", &rest[..p], &rest[p + c + 5..]);
        }
    }
    // The positional standfirst is the paragraph that followed the H1:
    // consume the first <p> block after it, whatever it contains.
    let t = rest.trim_start();
    if let Some(after_p) = t.strip_prefix("<p>") {
        if let Some(end) = after_p.find("</p>") {
            return after_p[end + 4..].trim_start_matches('\n').to_string();
        }
    }
    rest
}
