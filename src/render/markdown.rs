//!  Markdown compilation: flow HTML, galleries, heading ids, plain text.
use crate::slots;
use pulldown_cmark::{html, Options, Parser};
pub(crate) fn compile_flow(document: &str) -> (String, Vec<slots::Heading>) {
    let mut opts = Options::empty();
    opts.insert(Options::ENABLE_TABLES);
    opts.insert(Options::ENABLE_STRIKETHROUGH);
    let mut out = String::new();
    html::push_html(&mut out, Parser::new_ext(document, opts));

    let out = wrap_galleries(&out);
    let (out, headings) = tag_headings(&out);
    let out = rewrite_paths(&out);
    (out, headings)
}

/// Runs of two or more consecutive image-only paragraphs become a gallery —
/// and so does a single paragraph holding two or more images (markdown keeps
/// adjacent image lines in one paragraph, split by soft breaks). Chunks may
/// carry leading non-paragraph content (the H1 frame, list closers); those
/// pass through as-is.
pub(crate) fn wrap_galleries(html_str: &str) -> String {
    let imgs_of = |para: &str| -> Option<Vec<String>> {
        let inner = para.strip_prefix("<p>")?.strip_suffix("</p>\n")?;
        if inner.contains("</") {
            return None; // any real closing tag means mixed content
        }
        let imgs: Vec<String> = inner
            .split("<img")
            .skip(1)
            .map(|frag| format!("<img{}", frag.trim_end()))
            .collect();
        (imgs.len() >= 2).then_some(imgs)
    };

    let flush = |out: &mut String, run: &mut Vec<String>| {
        if run.len() >= 2 {
            out.push_str("<div class=\"gallery\">");
            for img in run.drain(..) {
                out.push_str(&img);
            }
            out.push_str("</div>\n");
        } else {
            for img in run.drain(..) {
                out.push_str("<p>");
                out.push_str(&img);
                out.push_str("</p>\n");
            }
        }
    };

    let mut out = String::with_capacity(html_str.len());
    let mut run: Vec<String> = Vec::new();
    let mut rest = html_str;
    while !rest.is_empty() {
        let Some(end) = rest.find("</p>\n") else {
            flush(&mut out, &mut run);
            out.push_str(rest);
            break;
        };
        let chunk = &rest[..end + 5];
        rest = &rest[end + 5..];

        let popen = chunk.rfind("<p>");
        let Some(popen) = popen else {
            flush(&mut out, &mut run);
            out.push_str(chunk);
            continue;
        };
        let (prelude, para) = (&chunk[..popen], &chunk[popen..]);
        if !prelude.is_empty() {
            flush(&mut out, &mut run);
            out.push_str(prelude);
        }
        match imgs_of(para) {
            Some(imgs) => run.extend(imgs),
            None => {
                flush(&mut out, &mut run);
                out.push_str(para);
            }
        }
    }
    out
}

/// Give every h2/h3 a stable id and collect them for the TOC. The H1 is the
/// article's title and stays untouched.
pub(crate) fn tag_headings(html_in: &str) -> (String, Vec<slots::Heading>) {
    let mut out = String::with_capacity(html_in.len());
    let mut headings = Vec::new();
    let mut rest = html_in;
    while let Some(pos) = rest.find("<h2>").or_else(|| rest.find("<h3>")) {
        let level: u8 = if rest[pos..].starts_with("<h2>") {
            2
        } else {
            3
        };
        let close = format!("</h{level}>");
        let after_tag = pos + 4; // "<h2>" and "<h3>" are both 4 chars
        let end = rest[after_tag..].find(&close).map(|e| after_tag + e);
        let Some(end) = end else { break };
        let text = strip_tags(&rest[after_tag..end]);
        let n = headings.len() + 1;
        let id = format!("sec-{n}-{}", slug_of(&text));
        out.push_str(&rest[..pos]);
        out.push_str(&format!("<h{level} id=\"{id}\">"));
        out.push_str(&rest[after_tag..end]);
        out.push_str(&close);
        headings.push(slots::Heading { level, text, id });
        rest = &rest[end + close.len()..];
    }
    out.push_str(rest);
    (out, headings)
}

pub(crate) fn strip_tags(fragment: &str) -> String {
    let mut s = String::new();
    let mut in_tag = false;
    for c in fragment.chars() {
        match c {
            '<' => in_tag = true,
            '>' => in_tag = false,
            c if !in_tag => s.push(c),
            _ => {}
        }
    }
    s.trim().to_string()
}

pub(crate) fn slug_of(text: &str) -> String {
    let mut s = String::new();
    for c in text.chars() {
        if c.is_ascii_alphanumeric() {
            s.push(c.to_ascii_lowercase());
        } else if (c == ' ' || c == '-' || c == '_') && !s.ends_with('-') && !s.is_empty() {
            s.push('-');
        }
    }
    s.trim_matches('-').chars().take(40).collect()
}

/// Rewrite publication-relative references to emitted ones: images and links
/// pointing at `media/` gain the `../` hop out of `render/`; article links
/// become sibling pages.
pub(crate) fn rewrite_paths(html_in: &str) -> String {
    html_in
        .replace("src=\"media/", "src=\"../media/")
        .replace("href=\"media/", "href=\"../media/")
        .replace("href=\"articles/", "href=\"")
        .replace(".md\"", ".html\"")
}

pub(crate) fn strip_frame(document: &str) -> &str {
    let mut rest = document.trim_start();
    if let Some(after) = rest.strip_prefix("# ") {
        // Skip the title line, then blanks, then the positional standfirst
        // paragraph (a heading is never a standfirst).
        rest = after.split_once('\n').map(|(_, r)| r).unwrap_or("");
        rest = rest.trim_start();
        if !rest.is_empty() && !rest.starts_with('#') {
            rest = match rest.split_once("\n\n") {
                Some((_, r)) => r,
                None => "",
            };
        }
    }
    rest.trim_start()
}
