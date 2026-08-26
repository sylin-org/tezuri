//! Tezuri desktop shell: a thin typed command boundary over the tezuri
//! library. The webview gets named product operations only — no generic
//! filesystem, shell, process, or network authority.

#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use serde::Serialize;
use std::path::PathBuf;
use tauri::State;
use tezuri::{
    articles::{Article, ArticleMeta},
    consult,
    desk::Desk,
    media, publications, ship,
};

/// The one publication open in this session. v1 keeps a single window bound
/// to a single publication root; switching means reopening.
struct Session(std::sync::Mutex<Option<PathBuf>>);

#[derive(Serialize)]
pub struct CommandError {
    pub message: String,
}

fn err<E: std::fmt::Display>(e: E) -> CommandError {
    CommandError {
        message: e.to_string(),
    }
}

fn root(session: &Session) -> Result<PathBuf, CommandError> {
    session
        .0
        .lock()
        .unwrap()
        .clone()
        .ok_or_else(|| CommandError {
            message: "no publication is open. Open one first.".into(),
        })
}

// -- registry ----------------------------------------------------------------

use tezuri::publications::Registry;

#[tauri::command]
fn registry_load() -> Result<Registry, CommandError> {
    Registry::load().map_err(err)
}

#[derive(serde::Deserialize)]
pub struct NewPublication {
    pub name: String,
    pub persona: String,
    pub path: String,
}

#[tauri::command]
fn registry_add(pub_data: NewPublication) -> Result<Registry, CommandError> {
    let mut reg = Registry::load().map_err(err)?;
    reg.add(publications::Publication {
        name: pub_data.name,
        persona: pub_data.persona,
        root: PathBuf::from(&pub_data.path),
    })
    .map_err(err)?;
    Ok(reg)
}

#[tauri::command]
fn registry_remove(path: String) -> Result<Registry, CommandError> {
    let mut reg = Registry::load().map_err(err)?;
    reg.publications
        .retain(|p| p.root != std::path::Path::new(&path));
    reg.save().map_err(err)?;
    Ok(reg)
}

/// Last-opened publication, remembered outside the registry file.
#[tauri::command]
fn set_last_opened(path: String) -> Result<(), CommandError> {
    let base = dirs_home().ok_or_else(|| err("no home directory"))?;
    let p = base.join(".tezuri").join("last-opened.txt");
    if let Some(parent) = p.parent() {
        std::fs::create_dir_all(parent).map_err(err)?;
    }
    tezuri::spine::atomic_write(&p, path.as_bytes()).map_err(err)
}

#[tauri::command]
fn get_last_opened() -> Result<Option<String>, CommandError> {
    let base = dirs_home().ok_or_else(|| err("no home directory"))?;
    let p = base.join(".tezuri").join("last-opened.txt");
    if p.exists() {
        Ok(Some(
            std::fs::read_to_string(p).map_err(err)?.trim().to_string(),
        ))
    } else {
        Ok(None)
    }
}

fn dirs_home() -> Option<PathBuf> {
    std::env::var_os("HOME")
        .or_else(|| std::env::var_os("USERPROFILE"))
        .map(PathBuf::from)
}

// -- session ---------------------------------------------------------------

#[tauri::command]
fn open_publication(
    path: String,
    session: State<Session>,
) -> Result<PublicationsInfo, CommandError> {
    let p = PathBuf::from(&path);
    if !p.is_dir() {
        return Err(err("that folder does not exist"));
    }
    *session.0.lock().unwrap() = Some(p.clone());
    let d = Desk::rebuild(&p).map_err(err)?;
    Ok(PublicationsInfo {
        path,
        articles: d.entries.len(),
        words: d.momentum().total_words,
    })
}

#[derive(Serialize)]
pub struct PublicationsInfo {
    pub path: String,
    pub articles: usize,
    pub words: usize,
}

// -- desk ------------------------------------------------------------------

static APP_HANDLE: std::sync::OnceLock<tauri::AppHandle> = std::sync::OnceLock::new();

#[tauri::command]
fn pick_folder() -> Result<Option<String>, CommandError> {
    use tauri_plugin_dialog::DialogExt;
    let handle = APP_HANDLE.get().ok_or_else(|| err("app not ready"))?;
    Ok(handle
        .dialog()
        .file()
        .blocking_pick_folder()
        .map(|f| f.to_string()))
}

#[tauri::command]
fn read_theme(path: String) -> Result<String, CommandError> {
    // Only ever the publication's own theme.css; confinement via spine.
    let root = PathBuf::from(&path);
    let theme = tezuri::spine::confine(&root, std::path::Path::new("theme.css")).map_err(err)?;
    std::fs::read_to_string(theme).map_err(err)
}

#[tauri::command]
fn desk(session: State<Session>) -> Result<Desk, CommandError> {
    Desk::rebuild(&root(&session)?).map_err(err)
}

// -- articles ----------------------------------------------------------------

#[derive(serde::Serialize)]
pub struct ArticleFull {
    pub article: Article,
    pub raw: String,
}

#[tauri::command]
fn read_article(slug: String, session: State<Session>) -> Result<ArticleFull, CommandError> {
    let root_path = root(&session)?;
    let a = Article::load(&root_path, &slug).map_err(err)?;
    let doc_path = tezuri::articles::Article::doc_path(&root_path, &slug).map_err(err)?;
    let raw = std::fs::read_to_string(doc_path).map_err(err)?;
    Ok(ArticleFull { article: a, raw })
}

#[derive(serde::Deserialize)]
pub struct ArticleInput {
    pub meta: ArticleMeta,
    pub document: String,
}

#[tauri::command]
fn save_article(article: ArticleInput, session: State<Session>) -> Result<String, CommandError> {
    let root_path = root(&session)?;
    let a = Article {
        meta: article.meta,
        document: article.document,
    };
    a.save(&root_path).map_err(err)
}

#[tauri::command]
fn save_article_raw(
    slug: String,
    document: String,
    session: State<Session>,
) -> Result<String, CommandError> {
    let root_path = root(&session)?;
    let mut a = Article::load(&root_path, &slug).map_err(err)?;
    a.document = document;
    a.save(&root_path).map_err(err)
}

#[tauri::command]
fn create_article(
    slug: String,
    title: String,
    session: State<Session>,
) -> Result<Article, CommandError> {
    Article::create(&root(&session)?, &slug, &title).map_err(err)
}

#[derive(Serialize)]
pub struct SetStateResult {
    pub slug: String,
    pub state: String,
}

#[tauri::command]
fn set_article_state(
    slug: String,
    state: String,
    session: State<Session>,
) -> Result<SetStateResult, CommandError> {
    let st = match state.as_str() {
        "draft" => tezuri::articles::State::Draft,
        "review" => tezuri::articles::State::Review,
        "published" => tezuri::articles::State::Published,
        _ => return Err(err("unknown state")),
    };
    let root_path = root(&session)?;
    let mut a = Article::load(&root_path, &slug).map_err(err)?;
    a.meta.state = st;
    a.save(&root_path).map_err(err)?;
    Ok(SetStateResult {
        slug,
        state: a.meta.state.as_str().into(),
    })
}

// -- media -------------------------------------------------------------------

#[tauri::command]
fn add_media(
    bytes: Vec<u8>,
    original_name: String,
    session: State<Session>,
) -> Result<String, CommandError> {
    let stored = media::store_identified(&root(&session)?, &bytes, &original_name).map_err(err)?;
    Ok(media::base_ref(&stored))
}

/// Fetch a remote image at explicit user request (import flow), store it
/// under a local identity, and return the base reference.
#[tauri::command]
fn fetch_media(url: String, session: State<Session>) -> Result<String, CommandError> {
    let root_path = root(&session)?;
    let resp = reqwest::blocking::get(&url).map_err(err)?;
    if !resp.status().is_success() {
        return Err(err(format!("fetch failed: {}", resp.status())));
    }
    let bytes = resp.bytes().map_err(err)?;
    let name = url.rsplit('/').next().unwrap_or("image");
    let stored = media::store_identified(&root_path, &bytes, name).map_err(err)?;
    Ok(media::base_ref(&stored))
}

// -- consult -----------------------------------------------------------------

#[derive(Serialize)]
pub struct AdviceResult {
    pub recipe: String,
    pub assistant: String,
    pub output: String,
}

#[tauri::command]
fn consult_recipe(
    recipe: String,
    slug: String,
    assistant: Option<String>,
    session: State<Session>,
) -> Result<AdviceResult, CommandError> {
    let a = consult::advise(&root(&session)?, &recipe, &slug, assistant.as_deref()).map_err(err)?;
    Ok(AdviceResult {
        recipe: a.recipe,
        assistant: a.assistant,
        output: a.verdict_first_output,
    })
}

#[tauri::command]
fn list_assistants(session: State<Session>) -> Result<Vec<String>, CommandError> {
    Ok(consult::Catalog::load(&root(&session)?)
        .map_err(err)?
        .assistants
        .into_iter()
        .map(|a| a.id)
        .collect())
}

// -- ship --------------------------------------------------------------------

#[derive(Serialize)]
pub struct ProofResult {
    pub verdict: String,
    pub evidence: String,
}

#[tauri::command]
fn prove(session: State<Session>) -> Result<ProofResult, CommandError> {
    let p = ship::prove(&root(&session)?).map_err(err)?;
    Ok(ProofResult {
        verdict: p.verdict,
        evidence: p.evidence,
    })
}

#[derive(Serialize)]
pub struct ChangeView {
    pub status: char,
    pub path: String,
}

#[tauri::command]
fn review_changes(session: State<Session>) -> Result<Vec<ChangeView>, CommandError> {
    Ok(ship::review(&root(&session)?)
        .map_err(err)?
        .into_iter()
        .map(|c| ChangeView {
            status: c.status,
            path: c.path,
        })
        .collect())
}

#[derive(Serialize)]
pub struct CommitResult {
    pub hash: String,
}

#[tauri::command]
fn commit_selected(
    paths: Vec<String>,
    message: String,
    session: State<Session>,
) -> Result<CommitResult, CommandError> {
    let h = ship::commit_selection(&root(&session)?, &paths, &message).map_err(err)?;
    Ok(CommitResult { hash: h })
}

#[tauri::command]
fn push_published(expected: Option<String>, session: State<Session>) -> Result<(), CommandError> {
    ship::push(&root(&session)?, expected.as_deref()).map_err(err)
}

#[tauri::command]
fn remote_head(session: State<Session>) -> Result<Option<String>, CommandError> {
    ship::remote_head(&root(&session)?).map_err(err)
}

pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .setup(|app| {
            let _ = APP_HANDLE.set(app.handle().clone());
            Ok(())
        })
        .manage(Session(std::sync::Mutex::new(None)))
        .invoke_handler(tauri::generate_handler![
            registry_load,
            registry_add,
            registry_remove,
            set_last_opened,
            get_last_opened,
            pick_folder,
            open_publication,
            read_theme,
            desk,
            read_article,
            save_article,
            save_article_raw,
            create_article,
            set_article_state,
            add_media,
            fetch_media,
            consult_recipe,
            list_assistants,
            prove,
            review_changes,
            commit_selected,
            push_published,
            remote_head,
        ])
        .run(tauri::generate_context!())
        .expect("error while running Tezuri");
}

// keep imports used even as the surface evolves
#[allow(unused_imports)]
use publications as _publications;
