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

pub(crate) const FLOW_ONLY: &[Host] = &[Host::Flow];

pub(crate) const RAIL_ONLY: &[Host] = &[Host::Rail];

pub(crate) const ANYWHERE: &[Host] = &[Host::Flow, Host::Rail];

/// Value aliases kept legal so every example the v1 ADR published still
/// parses exactly as signed.
pub(crate) fn canonicalize(key: &str, value: &str) -> String {
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

pub fn options_of(entry_name: &str) -> &'static [OptionSpec] {
    registry()
        .into_iter()
        .find(|d| d.name == entry_name)
        .map(|d| d.options)
        .unwrap_or(&[])
}

pub(crate) const DATE_OPTS: &[OptionSpec] = &[OptionSpec {
    key: "format",
    label: "Format",
    control: Control::Choice(&["long", "iso"]),
    default: "long",
}];

pub(crate) const TAGS_OPTS: &[OptionSpec] = &[OptionSpec {
    key: "style",
    label: "Style",
    control: Control::Choice(&["pills", "text"]),
    default: "pills",
}];

pub(crate) const COVER_OPTS: &[OptionSpec] = &[OptionSpec {
    key: "fit",
    label: "Image fit",
    control: Control::Choice(&["natural", "fill", "contain"]),
    default: "natural",
}];

pub(crate) const EXCERPT_OPTS: &[OptionSpec] = &[OptionSpec {
    key: "count",
    label: "Word count",
    control: Control::Count { min: 1, max: 200 },
    default: "40",
}];

pub(crate) const LIST_OPTS: &[OptionSpec] = &[
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

pub(crate) const FOOTER_OPTS: &[OptionSpec] = &[OptionSpec {
    key: "sticky",
    label: "Stick to viewport bottom",
    control: Control::Toggle,
    default: "on",
}];

/// {{ARTICLE}} carries frame modes and, when a mode declares them, the
/// fields that presentation shows. Conduct reads this table.
pub(crate) const ARTICLE_OPTS: &[OptionSpec] = &[
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
pub(crate) const ARTICLE_MODES: &[&str] = &["plain", "title-banner"];

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
