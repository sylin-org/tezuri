//! Tezuri CLI — drives the full flow end to end. The desktop shell will sit
//! on top of the same library; the flow is identical either way.

use anyhow::{Context, Result};
use tezuri::{articles::Article, consult, desk::Desk, media, publications, ship, spine};

fn main() -> Result<()> {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let root = std::env::current_dir()?;
    match args.first().map(|s| s.as_str()) {
        Some("write") => cmd_write(&root, &args[1..]),
        Some("media") => cmd_media(&root, &args[1..]),
        Some("desk") => cmd_desk(&root),
        Some("consult") => cmd_consult(&root, &args[1..]),
        Some("prove") => cmd_prove(&root),
        Some("ship") => cmd_ship(&root, &args[1..]),
        _ => {
            eprintln!(
                "tezuri <command>\n\n\
                 write <slug> [title]   create/open an article\n\
                 media <file> [alt]     store an image, print its markdown link\n\
                 desk                   rebuild and show the desk\n\
                 consult <recipe> <slug> [assistant]\n\
                 prove                  run the site's own build on a copy\n\
                 ship review            show changed paths\n\
                 ship commit <msg> <path...>  commit only these paths\n\
                 ship push              push if remote state still holds\n"
            );
            Ok(())
        }
    }
}

fn cmd_write(root: &std::path::Path, rest: &[String]) -> Result<()> {
    let slug = rest.first().context("usage: write <slug> [title]")?;
    let existing = Article::load(root, slug);
    let article = match existing {
        Ok(a) => a,
        Err(_) => Article::create(root, slug, rest.get(1).map(|s| s.as_str()).unwrap_or(slug))?,
    };
    println!(
        "{} [{}] ({} words)",
        article.meta.title,
        article.meta.state.as_str(),
        article.body.split_whitespace().count()
    );
    println!("edit articles/{}/index.md, then: tezuri prove", slug);
    Ok(())
}

fn cmd_media(root: &std::path::Path, rest: &[String]) -> Result<()> {
    let file = rest.first().context("usage: media <file> [alt]")?;
    let bytes = std::fs::read(file)?;
    let alt = rest.get(1).cloned().unwrap_or_default();
    let stored = media::store(root, &bytes, &alt)?;
    println!("{}", media::link_snippet(&stored, &alt));
    Ok(())
}

fn cmd_desk(root: &std::path::Path) -> Result<()> {
    let d = Desk::rebuild(root)?;
    let m = d.momentum();
    for e in &d.entries {
        println!("[{:9}] {:<30} {:>5}w", e.state.as_str(), e.title, e.words);
        for l in &e.dangling_links {
            println!("            dangling link -> {l}");
        }
    }
    println!(
        "\n{} drafts, {} in review, {} published, {} words total",
        m.drafts, m.review, m.published, m.total_words
    );
    Ok(())
}

fn cmd_consult(root: &std::path::Path, rest: &[String]) -> Result<()> {
    let recipe = rest
        .first()
        .context("usage: consult <recipe> <slug> [assistant]")?;
    let slug = rest.get(1).context("usage: consult <recipe> <slug>")?;
    let advice = consult::advise(root, recipe, slug, rest.get(2).map(|s| s.as_str()))?;
    println!(
        "--- {} via {} ({}) ---",
        advice.recipe, advice.assistant, advice.slug
    );
    println!("{}", advice.verdict_first_output);
    Ok(())
}

fn cmd_prove(root: &std::path::Path) -> Result<()> {
    let p = ship::prove(root)?;
    println!("verdict: {}", p.verdict);
    if p.verdict != "passed" || std::env::var("TEZURI_VERBOSE").is_ok() {
        println!("{}", p.evidence);
    }
    Ok(())
}

fn cmd_ship(root: &std::path::Path, rest: &[String]) -> Result<()> {
    match rest.first().map(|s| s.as_str()) {
        Some("review") => {
            let head = ship::remote_head(root)?;
            let changes = ship::review(root)?;
            println!(
                "upstream at review time: {}",
                head.as_deref().unwrap_or("(none yet)")
            );
            for c in changes {
                println!("{} {}", c.status, c.path);
            }
        }
        Some("commit") => {
            let msg = rest
                .get(1)
                .context("usage: ship commit <message> <path...>")?;
            let hash = ship::commit_selection(root, &rest[2..], msg)?;
            println!("committed {hash}");
        }
        Some("push") => {
            ship::push(root, None)?;
            println!("pushed");
        }
        _ => anyhow::bail!("ship needs review | commit | push"),
    }
    Ok(())
}

// Silence unused-import warning until the desktop shell lands.
#[allow(unused_imports)]
use spine as _spine;
