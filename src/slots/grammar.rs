use super::*;
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
    // Author regions — <style> blocks and HTML comments — never yield
    // slots: a `{{slot}}` there is prose, not an occurrence. A CSS comment
    // documenting the frame once consumed the "first banner wins" rule and
    // silently retired the hero on every page (found in the shipped pack).
    // The mask is equal-length, so text slices out of the original bytes.
    let masked = mask_author_regions(src);
    let mut parts = Vec::new();
    let mut scan = masked.as_str();
    let mut lit = src;
    while let Some(start) = scan.find("{{") {
        if start > 0 {
            parts.push(Part::Text(lit[..start].to_string()));
        }
        let after_mask = &scan[start..];
        let after_lit = &lit[start..];
        let Some(end_rel) = after_mask.find("}}") else {
            parts.push(Part::Text(after_lit.to_string()));
            return parts;
        };
        let raw = after_lit[..end_rel + 2].to_string();
        match slot_of_raw(&raw) {
            Some(s) => parts.push(Part::Slot(s)),
            None => parts.push(Part::Text(raw)),
        }
        scan = &after_mask[end_rel + 2..];
        lit = &after_lit[end_rel + 2..];
    }
    if !lit.is_empty() {
        parts.push(Part::Text(lit.to_string()));
    }
    parts
}

/// Blank out `<style>…</style>` and `<!--…-->` regions with spaces. Same
/// byte length as the source, so offsets stay interchangeable. Unterminated
/// regions mask to the end — a whisper, never a break.
pub(crate) fn mask_author_regions(src: &str) -> String {
    let bytes = src.as_bytes();
    let mut masked = bytes.to_vec();
    let mut i = 0;
    while i < bytes.len() {
        let (open, close) = if bytes[i..].starts_with(b"<style") {
            ("<style", "</style>".as_bytes())
        } else if bytes[i..].starts_with(b"<!--") {
            ("<!--", "-->".as_bytes())
        } else {
            i += 1;
            continue;
        };
        let from = i + open.len();
        let end = bytes[from..]
            .windows(close.len())
            .position(|w| w == close)
            .map_or(bytes.len(), |p| from + p + close.len());
        for b in &mut masked[i..end] {
            *b = b' ';
        }
        i = end;
    }
    String::from_utf8(masked).unwrap_or_else(|_| src.to_string())
}

pub(crate) fn slot_of_raw(raw: &str) -> Option<RawSlot> {
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

pub(crate) fn tokenize_hints(hint: &str) -> Vec<String> {
    hint.split(',')
        .map(|t| t.trim().to_string())
        .filter(|t| !t.is_empty())
        .collect()
}

/// English slots, one word shape: a leading letter, then letters, digits,
/// underscores, hyphens (as in `article-list`). Case is significant.
pub(crate) fn valid_name(name: &str) -> Option<()> {
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

/// First `key:`-prefixed value among canonical hints.
pub(crate) fn hint_value<'a>(hints: &'a [String], key: &str) -> Option<&'a str> {
    hints
        .iter()
        .find_map(|h| h.strip_prefix(key))
        .filter(|v| !v.is_empty())
}

/// Toggle semantics default on for footer (sticky is the designed baseline).
pub(crate) fn toggle_on(hints: &[String], key: &str) -> bool {
    !hints.iter().any(|h| h == &format!("{key}:off"))
}
