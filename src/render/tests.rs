//! Unit tests, moved with their code.
use super::*;
use crate::articles::{Article, State};
use crate::identity::Identity;
use std::path::Path;
use tempfile::tempdir;
const DOC: &str = "# On Rust\n\n_A meditation on ownership._\n\n## First part\n\nHello \
                   world.\n\n## Second part\n\nMore words.\n\n### Deep\n\nDeeper.\n";

fn setup(dir: &Path, slug: &str, doc: &str) {
    Article::create(dir, slug, slug).unwrap();
    let mut a = Article::load(dir, slug).unwrap();
    a.document = doc.into();
    a.save(dir).unwrap();
}

#[test]
fn flow_keeps_title_and_standfirst_and_ids_sections() {
    let (flow, headings) = compile_flow(DOC);
    assert!(flow.contains("<h1>On Rust</h1>"), "H1 lives in the flow");
    assert!(flow.contains("<em>A meditation on ownership.</em>"));
    assert!(flow.contains("<h2 id=\"sec-1-first-part\">First part</h2>"));
    assert!(flow.contains("<h3 id=\"sec-3-deep\">Deep</h3>"));
    assert_eq!(headings.len(), 3);
}

#[test]
fn galleries_wrap_adjacent_images() {
    let md = "# T\n\n![a](media/a.png)\n![b](media/b.png)\n\nSolo:\n\n![c](media/c.png)\n";
    let (flow, _) = compile_flow(md);
    assert!(flow.contains("<div class=\"gallery\">"));
    assert!(flow.contains("src=\"../media/a.png\""));
    let gallery_end = flow.find("</div>").unwrap();
    assert!(flow[gallery_end..].contains("<p><img"), "{flow}");
}

#[test]
fn article_links_become_sibling_pages() {
    let (flow, _) = compile_flow("T\n\nsee [that](articles/other.md)\n");
    assert!(flow.contains("href=\"other.html\""));
}

#[test]
fn dumb_default_renders_calm_full_document_page() {
    let dir = tempdir().unwrap();
    setup(dir.path(), "on-rust", DOC);

    let (page, notes) = render_article_warned(dir.path(), "on-rust").unwrap();
    assert!(notes.is_empty());
    assert!(
        page.contains("<div class=\"article-prose\"><h1>On Rust</h1>"),
        "{page}"
    );
    // Navigation exists iff the template says {{toc}}; sections are still
    // anchorable by id.
    assert!(page.contains("<h2 id=\"sec-1-first-part\">"));
    assert!(page.contains("<style id=\"tezuri-baseline\">"), "{page}");
    assert!(page.contains(".lightbox.on"), "behaviors ship");
    assert!(page.contains("is-article is-draft"), "{page}");
    let tail = page.trim_end().replace('\n', "");
    assert!(tail.ends_with("</body></html>"), "{page}");
}

#[test]
fn emit_writes_pages_and_index_and_journals() {
    let dir = tempdir().unwrap();
    let mut alpha = Article::create(dir.path(), "alpha", "Alpha").unwrap();
    alpha.meta.state = State::Published;
    alpha.save(dir.path()).unwrap();
    let mut beta = Article::create(dir.path(), "beta", "Beta").unwrap();
    beta.meta.state = State::Published;
    beta.save(dir.path()).unwrap();
    Article::create(dir.path(), "secret-draft", "Secret").unwrap();

    let written = emit_render(dir.path()).unwrap();
    assert_eq!(
        written.len(),
        6,
        "two pages + two cards + index + feed; drafts never emit"
    );
    assert!(dir.path().join("render/alpha.html").exists());
    assert!(dir.path().join("render/beta.html").exists());
    assert!(!dir.path().join("render/secret-draft.html").exists());
    let card = std::fs::read_to_string(dir.path().join("render/alpha.card.html")).unwrap();
    assert!(card.contains("tezuri-card"), "{card}");
    assert!(card.contains("Alpha"), "{card}");
    assert!(
        !card.contains("tezuri-baseline"),
        "cards carry no chrome: {card}"
    );
    let index = std::fs::read_to_string(dir.path().join("render/index.html")).unwrap();
    assert!(index.contains("href=\"alpha.html\""));
    assert!(index.contains("href=\"beta.html\""));
    assert!(!index.contains("secret-draft"));
    let feed = std::fs::read_to_string(dir.path().join("render/feed.xml")).unwrap();
    assert!(feed.contains("<rss version=\"2.0\">"), "{feed}");
    assert!(feed.contains("<link>alpha.html</link>"), "{feed}");
    assert!(feed.contains("<link>beta.html</link>"), "{feed}");
    assert!(!feed.contains("secret-draft"), "{feed}");

    let events = crate::spine::Journal::open(dir.path())
        .unwrap()
        .events()
        .unwrap();
    assert!(events.iter().any(|(_, e)| e.kind() == "rendered"));
}

#[test]
fn publication_template_overrides_the_embedded_default() {
    let dir = tempdir().unwrap();
    Article::create(dir.path(), "gamma", "Gamma").unwrap();
    let tpl_dir = dir.path().join("templates");
    std::fs::create_dir_all(&tpl_dir).unwrap();
    std::fs::write(
        tpl_dir.join("article.html"),
        "<html><head>{{title}}</head><body class=\"mine\">{{ARTICLE}}</body></html>",
    )
    .unwrap();

    let page = render_article(dir.path(), "gamma").unwrap();
    // Baseline lands immediately inside head, so any later style block
    // in a template's own head always wins over it.
    assert!(
        page.starts_with("<html><head><style id=\"tezuri-baseline\">"),
        "{page}"
    );
    assert!(
        page.contains("</style><style id=\"tezuri-theme\"></style>Gamma</head>"),
        "{page}"
    );
    assert!(
        page.contains("<div class=\"article-prose\"><h1>Gamma</h1>"),
        "{page}"
    );
    let tail = page.trim_end().replace('\n', "");
    assert!(tail.ends_with("</body></html>"), "{page}");
    assert_eq!(page.matches("(function () {").count(), 1, "behaviors once");
}

#[test]
fn unknown_slot_whispers_into_notes_not_breakage() {
    let dir = tempdir().unwrap();
    Article::create(dir.path(), "g2", "G2").unwrap();
    let tpl_dir = dir.path().join("templates");
    std::fs::create_dir_all(&tpl_dir).unwrap();
    std::fs::write(
        tpl_dir.join("article.html"),
        "{{sparkle}}<body>{{ARTICLE}}</body>",
    )
    .unwrap();

    let (page, notes) = render_article_warned(dir.path(), "g2").unwrap();
    assert!(page.contains("G2"), "{page}");
    assert_eq!(notes.len(), 1);
    assert!(notes[0].starts_with("unknown slot {{sparkle}}"));
}

#[test]
fn theme_css_is_injected_for_the_render_plane() {
    let dir = tempdir().unwrap();
    Article::create(dir.path(), "themed", "Themed").unwrap();
    crate::render::write_theme(dir.path(), ".article-prose { letter-spacing: .5px; }").unwrap();

    let page = render_article(dir.path(), "themed").unwrap();
    assert!(
        page.contains("<style id=\"tezuri-theme\">.article-prose"),
        "{page}"
    );
}

#[test]
fn cover_prefers_derived_1024_when_present() {
    let dir = tempdir().unwrap();
    let media = dir.path().join("media");
    std::fs::create_dir_all(&media).unwrap();
    std::fs::write(media.join("ab-plug.png"), b"x").unwrap();
    std::fs::write(media.join("ab-plug_1024.png"), b"x").unwrap();
    let _ = std::fs::write(media.join("cd-other.jpg"), b"x");

    assert_eq!(
        cover_src(dir.path(), &Some("media/ab-plug.png".into())).unwrap(),
        "../media/ab-plug_1024.png"
    );
    assert_eq!(
        cover_src(dir.path(), &Some("media/cd-other.jpg".into())).unwrap(),
        "../media/cd-other.jpg"
    );
    assert!(cover_src(dir.path(), &Some("media/missing.png".into())).is_none());
    assert!(cover_src(dir.path(), &Some("bare-name.png".into())).is_none());
}

#[test]
fn compose_carries_the_artifacts_dress() {
    let dir = tempdir().unwrap();
    setup(dir.path(), "dressed", DOC);
    let tpl_dir = dir.path().join("templates");
    std::fs::create_dir_all(&tpl_dir).unwrap();
    std::fs::write(
        tpl_dir.join("article.html"),
        concat!(
            "<html><head>",
            "<link href=\"https://fonts.googleapis.com/css2?family=X\" rel=\"stylesheet\">",
            "<style>.title-banner--title { font-style: italic; }</style>",
            "</head><body>{{ARTICLE | title-banner}}</body></html>"
        ),
    )
    .unwrap();

    let c = compose_write_view(dir.path(), "dressed").unwrap();
    assert!(
        c.css
            .contains("@import url('https://fonts.googleapis.com/css2?family=X');"),
        "{}",
        c.css
    );
    assert!(c
        .css
        .contains(".title-banner--title { font-style: italic; }"));
    // The artifact's cascade, mirrored: the calm baseline lands early so
    // the template's own styles override it, as on the emitted page.
    let baseline_at = c.css.find("Calm baseline").expect("baseline present");
    let authored_at = c
        .css
        .find(".title-banner--title { font-style: italic; }")
        .expect("template styles present");
    assert!(baseline_at < authored_at, "baseline early, authored wins");
}

#[test]
fn draft_compose_and_specimen_render_through_supplied_bytes() {
    let dir = tempdir().unwrap();
    setup(dir.path(), "drafty", DOC);
    std::fs::create_dir_all(dir.path().join("media")).unwrap();
    std::fs::write(dir.path().join("media").join("drafty.png"), b"x").unwrap();
    let mut a = Article::load(dir.path(), "drafty").unwrap();
    a.meta.cover = Some("media/drafty.png".into());
    a.save(dir.path()).unwrap();
    // Banner is the space's decision; the draft only carries the hint.
    std::fs::write(
        dir.path().join("publication.yaml"),
        b"header_style: banner\n",
    )
    .unwrap();

    let draft = "<html><body>{{ARTICLE | title-banner, cover:fill}}</body></html>";

    // The specimen renders the mode through the whole pipeline.
    let (page, notes) = render_article_with(dir.path(), "drafty", draft).unwrap();
    assert!(notes.is_empty());
    assert!(page.contains("<section class=\"title-banner\">"), "{page}");
    assert!(page.contains("cover-fill"), "{page}");
    // The banner owns title + standfirst: the flow sheds them.
    assert!(page.contains("title-banner--title"), "{page}");
    assert!(
        !page.contains("<div class=\"article-prose\"><h1>"),
        "{page}"
    );
}

#[test]
fn compose_write_view_keeps_order_and_marks_the_flow() {
    let dir = tempdir().unwrap();
    setup(dir.path(), "wv", DOC);

    let c = compose_write_view(dir.path(), "wv").unwrap();
    assert!(!c.space_template, "default template: nothing in the space");
    assert!(c.notes.is_empty());
    let kinds: Vec<&str> = c
        .segments
        .iter()
        .map(|s| match s {
            Seg::Text { .. } => "text",
            Seg::ArticleFlow { .. } => "flow",
            Seg::Slot(_) => "slot",
        })
        .collect();
    assert_eq!(
        kinds,
        vec![
            "text", // body opener + page + article wrappers
            "flow", "text", // closing wrappers before </body>
        ]
    );
    // The head never leaks: no doctype, no injected baseline styles.
    let joined = c
        .segments
        .iter()
        .map(|s| match s {
            Seg::Text { html } => html.clone(),
            _ => String::new(),
        })
        .collect::<Vec<_>>()
        .join("");
    assert!(joined.contains("<div class=\"page\">"), "{joined}");
    assert!(!joined.contains("<!DOCTYPE") && !joined.contains("tezuri-baseline"));
    assert!(c.segments[0..].iter().all(|s| !matches!(s, Seg::Slot(_))));
}

#[test]
fn compose_write_view_projects_slots_in_order() {
    let dir = tempdir().unwrap();
    Article::create(dir.path(), "proj", "Proj").unwrap();
    let mut a = Article::load(dir.path(), "proj").unwrap();
    a.document = "# Proj\n\n_Standfast._\n\nBody.\n".into();
    a.meta.tags = vec!["rust".into()];
    a.save(dir.path()).unwrap();

    let tpl_dir = dir.path().join("templates");
    std::fs::create_dir_all(&tpl_dir).unwrap();
    std::fs::write(
        tpl_dir.join("article.html"),
        concat!(
            "<html><head><style>s{}</style><title>{{title}}</title></head>\n",
            "<body class=\"{{body_class}}\">\n",
            "<header>{{site_name}}</header>{{standfirst}}\n",
            "{{ARTICLE}}\n",
            "<footer>{{tags | pills}}{{toc}}{{sparkle}}</footer>",
            "<script>void</script></body></html>"
        ),
    )
    .unwrap();

    let c = compose_write_view(dir.path(), "proj").unwrap();
    assert!(c.space_template);
    let one_unknown = c.notes.iter().any(|n| n.contains("{{sparkle}}"));
    assert!(one_unknown);

    let mut order: Vec<String> = Vec::new();
    for s in &c.segments {
        match s {
            Seg::Text { html } => order.push(format!("text:{html:?}")),
            Seg::ArticleFlow { mirror, .. } => order.push(format!("flow:{mirror}")),
            Seg::Slot(sl) => order.push(format!("slot:{}:{}", sl.name, sl.mirror)),
        }
    }
    // Head is cut (title lives there), attributes before <body>'s `>` too.
    assert_eq!(order.len(), 10, "{order:?}");
    assert!(order[0].starts_with("text:")); // "\n<header>"
    assert_eq!(order[1], "slot:site_name:false");
    assert!(order[2].contains("</header>"));
    assert_eq!(order[3], "slot:standfirst:false");
    assert_eq!(order[4], "text:\"\\n\"");
    assert_eq!(order[5], "flow:false");
    assert_eq!(order[6], "text:\"\\n<footer>\"");
    assert_eq!(order[7], "slot:tags:false");
    assert_eq!(order[8], "slot:toc:false");
    assert_eq!(order[9], "text:\"</footer>\"");

    let standfirst_html = match &c.segments[3] {
        Seg::Slot(sl) => sl.html.clone(),
        _ => unreachable!(),
    };
    assert_eq!(
        standfirst_html,
        "<p class=\"standfirst\"><em>Standfast.</em></p>"
    );
    let tags_html = match &c.segments[7] {
        Seg::Slot(sl) => sl.html.clone(),
        _ => unreachable!("{order:?}"),
    };
    assert_eq!(tags_html, "<span class=\"tagpill\">#rust</span>");

    // Scripts after </body> are stripped from the plane.
    let joined = c
        .segments
        .iter()
        .filter_map(|s| match s {
            Seg::Text { html } => Some(html.as_str()),
            _ => None,
        })
        .collect::<String>();
    assert!(!joined.contains("<script"), "{joined}");
}

#[test]
fn missing_article_marker_still_hands_the_editor_over() {
    let dir = tempdir().unwrap();
    setup(dir.path(), "noart", "# No art\n\nplain.\n");
    // A deliberately flow-less template: the editor must still mount.
    let tpl_dir = dir.path().join("templates");
    std::fs::create_dir_all(&tpl_dir).unwrap();
    std::fs::write(
        tpl_dir.join("article.html"),
        "<html><body><p>layout without a flow</p></body></html>",
    )
    .unwrap();

    let c = compose_write_view(dir.path(), "noart").unwrap();
    let flows: Vec<&Seg> = c
        .segments
        .iter()
        .filter(|s| matches!(s, Seg::ArticleFlow { .. }))
        .collect();
    assert_eq!(flows.len(), 1, "the editor still mounts exactly once");
    assert!(c.notes.iter().any(|n| n.contains("{{ARTICLE}}")));
}

#[test]
fn duplicate_slots_mirror_the_first() {
    let dir = tempdir().unwrap();
    Article::create(dir.path(), "dup", "Dup").unwrap();
    let tpl_dir = dir.path().join("templates");
    std::fs::create_dir_all(&tpl_dir).unwrap();
    std::fs::write(
        tpl_dir.join("article.html"),
        "<html><body><h1>{{title}}</h1>{{ARTICLE}}<small>{{title}}</small></body></html>",
    )
    .unwrap();

    let c = compose_write_view(dir.path(), "dup").unwrap();
    let titles: Vec<bool> = c
        .segments
        .iter()
        .filter_map(|s| match s {
            Seg::Slot(sl) if sl.name == "title" => Some(sl.mirror),
            _ => None,
        })
        .collect();
    assert_eq!(titles, vec![false, true], "second instance mirrors first");
}

#[test]
fn site_cta_supports_modeled_key_and_legacy_discord() {
    let mut id = Identity {
        name: "K".into(),
        ..Default::default()
    };
    id.extra
        .insert("discord".into(), "https://discord.gg/x".into());

    let cta = site_cta_of(&id).unwrap();
    assert_eq!(cta.0, "Discuss on Discord");
    assert_eq!(cta.1, "https://discord.gg/x");

    id.extra.insert(
        "site_cta_url".into(),
        serde_yaml::Value::String("https://ko-fi.com/k".into()),
    );
    let cta = site_cta_of(&id).unwrap();
    assert_eq!(cta.0, "Read more");
    assert_eq!(cta.1, "https://ko-fi.com/k");
}

#[test]
fn banner_header_style_consumes_the_frame() {
    let dir = tempdir().unwrap();
    setup(dir.path(), "hero", DOC);
    std::fs::write(
        dir.path().join("publication.yaml"),
        b"header_style: banner\n",
    )
    .unwrap();

    // The default template is dumb — the banner only appears when a
    // template asks for it. Through a banner template, title and
    // standfirst feed the hero and leave the flow.
    let tpl = "<html><body>{{ARTICLE | title-banner, cover:none}}</body></html>";
    let (page, _) = render_article_with(dir.path(), "hero", tpl).unwrap();
    assert!(page.contains("title-banner--title"), "{page}");
    assert!(page.contains("title-banner--standfirst"), "{page}");
    assert!(
        !page.contains("<div class=\"article-prose\"><h1>"),
        "flow sheds its frame: {page}"
    );
    assert!(page.contains("Deeper.</p>"), "body survives: {page}");
}

#[test]
fn normal_header_style_keeps_the_flow_whole() {
    let dir = tempdir().unwrap();
    setup(dir.path(), "plain", DOC);
    // No header_style: Normal. Even a banner-carrying template renders
    // the raw flow — the document is king, dressing is a space decision.
    let tpl = "<html><body>{{ARTICLE | title-banner, cover:none}}</body></html>";
    let (page, _) = render_article_with(dir.path(), "plain", tpl).unwrap();
    assert!(!page.contains("<section class=\"title-banner\">"), "{page}");
    assert!(page.contains("<h1>On Rust</h1>"), "{page}");
    assert!(page.contains("A meditation on ownership."), "{page}");
}
