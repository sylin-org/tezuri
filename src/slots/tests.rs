use super::*;

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
        banner: true,
        cta: None,
        site_url: "https://example.com/".into(),
        footer_md: "\u{a9} 2026 Field Notes".into(),
        publishable,
        require_article: false,
    }
}
use crate::articles::State;
use crate::desk::DeskEntry;
#[test]
fn style_blocks_and_comments_never_yield_slots() {
    // A shipped pack once documented its frame inside a CSS comment; the
    // scan saw a second {{ARTICLE}} there, whose "first banner wins"
    // fallback silently retired the hero on every page.
    let tpl = concat!(
        "<html><head><style>",
        "/* frame: {{ARTICLE | title-banner}} */\n",
        ".banner{color:red}</style></head>\n",
        "<body>{{ARTICLE | title-banner}}</body></html>"
    );
    let parts = parse_template(tpl);
    let articles: Vec<&str> = parts
        .iter()
        .filter_map(|p| match p {
            Part::Slot(s) if s.name == "ARTICLE" => Some(s.raw.as_str()),
            _ => None,
        })
        .collect();
    assert_eq!(
        articles,
        vec!["{{ARTICLE | title-banner}}"],
        "the style-comment occurrence must not parse as a slot"
    );
    // Author regions still ride through as literal bytes, untouched.
    let joined: String = parts
        .iter()
        .filter_map(|p| match p {
            Part::Text(t) => Some(t.clone()),
            _ => None,
        })
        .collect();
    assert!(joined.contains("/* frame: {{ARTICLE | title-banner}} */"));
    assert!(joined.contains(".banner{color:red}"));
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
    let next = rewrite_slot_raw(tpl, "{{tags}}", &["style:text".to_string()]).expect("raw found");
    assert_eq!(next, "<body>{{tags | style:text}}</body>");

    // Bare alias input canonicalizes on write-back.
    let next2 = rewrite_slot_raw(&next, "{{tags | style:text}}", &["text".to_string()]).unwrap();
    assert_eq!(next2, "<body>{{tags | style:text}}</body>");

    // A second instance stays where it is; only the matched raw changes.
    let two = "{{date}}, {{date}}";
    let one_changed = rewrite_slot_raw(two, "{{date}}", &["iso".to_string()]).unwrap();
    assert_eq!(one_changed, "{{date | format:iso}}, {{date}}");

    let missing = rewrite_slot_raw(tpl, "{{sparkle}}", &["x".into()]);
    assert!(missing.is_none());
}
