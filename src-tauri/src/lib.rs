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
    // add() is pure domain logic; persistence is this caller's explicit act.
    reg.save().map_err(err)?;
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

// -- media protocol ----------------------------------------------------------
//
// The bundled webview cannot read the filesystem, so article images inside
// the app are served through this scheme. It streams only from the *open
// session's* media directory, path-confined like every other access. The
// artifact on disk keeps its relative `../media/` paths — this is purely the
// in-app view seam.

fn percent_decode(s: &str) -> String {
    let b = s.as_bytes();
    let mut out = Vec::with_capacity(b.len());
    let mut i = 0;
    while i < b.len() {
        if b[i] == b'%' && i + 2 < b.len() + 1 && i + 2 < b.len() + 1 {
            let hex = b.get(i + 1..i + 3).and_then(|h| {
                std::str::from_utf8(h)
                    .ok()
                    .and_then(|h| u8::from_str_radix(h, 16).ok())
            });
            if let Some(byte) = hex {
                out.push(byte);
                i += 3;
                continue;
            }
        }
        out.push(b[i]);
        i += 1;
    }
    String::from_utf8_lossy(&out).into_owned()
}

fn mime_of(ext: &str) -> &'static str {
    match ext {
        "png" => "image/png",
        "jpg" | "jpeg" => "image/jpeg",
        "gif" => "image/gif",
        "webp" => "image/webp",
        _ => "application/octet-stream",
    }
}

fn serve_media(
    handle: tauri::AppHandle,
    request: tauri::http::Request<Vec<u8>>,
) -> tauri::http::Response<Vec<u8>> {
    use tauri::Manager;
    let not_found = |msg: &str| {
        tauri::http::Response::builder()
            .status(404)
            .header("Content-Type", "text/plain")
            .body(msg.as_bytes().to_vec())
            .unwrap()
    };
    let root = handle
        .try_state::<Session>()
        .and_then(|s| s.0.lock().unwrap().clone());
    let Some(root) = root else {
        return not_found("no publication is open");
    };
    let raw = request.uri().path().trim_start_matches('/');
    let rel = percent_decode(raw);
    // Only the media tree is reachable through this scheme.
    if !rel.starts_with("media/") {
        return not_found("only media/ is served here");
    }
    let Ok(path) = tezuri::spine::confine(&root, std::path::Path::new(&rel)) else {
        return not_found("path refused");
    };
    match std::fs::read(&path) {
        Ok(bytes) => {
            let ext = path
                .extension()
                .and_then(|e| e.to_str())
                .unwrap_or_default()
                .to_ascii_lowercase();
            tauri::http::Response::builder()
                .header("Content-Type", mime_of(&ext))
                .header("Cache-Control", "max-age=3600")
                .body(bytes)
                .unwrap()
        }
        Err(_) => not_found("not found"),
    }
}

#[tauri::command]
fn media_base() -> String {
    // Windows serves custom schemes at http://<name>.localhost; other
    // platforms at <name>://.
    if cfg!(windows) {
        "http://media.localhost/".into()
    } else {
        "media://".into()
    }
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
    // A loaded space heals in the background: missing pages and renditions
    // derive quietly while the author works.
    enqueue_settle(p);
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

/// Read-only stats for any folder — used by the About page, which must never
/// bind or disturb the open session.
#[derive(Serialize)]
pub struct SpaceStats {
    pub articles: usize,
    pub words: usize,
}

#[tauri::command]
fn space_stats(path: String) -> Result<SpaceStats, CommandError> {
    let p = PathBuf::from(&path);
    if !p.is_dir() {
        return Err(err("that folder does not exist"));
    }
    let d = Desk::rebuild(&p).map_err(err)?;
    Ok(SpaceStats {
        articles: d.entries.len(),
        words: d.momentum().total_words,
    })
}

// -- settling ----------------------------------------------------------------

#[derive(Clone, serde::Serialize)]
struct SettleEv {
    kind: String,
    done: usize,
    total: usize,
}

fn settle_progress(kind: &str, done: usize, total: usize) {
    if let Some(handle) = APP_HANDLE.get() {
        use tauri::Emitter;
        let _ = handle.emit(
            "tezuri:settle",
            SettleEv {
                kind: kind.into(),
                done,
                total,
            },
        );
    }
}

/// Queue a background reconciliation for this space. One worker, sequential
/// jobs: derivation is deliberately calm, never a thundering herd.
fn enqueue_settle(root: PathBuf) {
    static TX: std::sync::OnceLock<std::sync::mpsc::Sender<PathBuf>> = std::sync::OnceLock::new();
    let tx = TX.get_or_init(|| {
        let (tx, rx) = std::sync::mpsc::channel::<PathBuf>();
        let spawned = std::thread::Builder::new()
            .name("settler".into())
            .spawn(move || {
                for root in rx {
                    if let Ok(plan) = tezuri::derive::scan_plan(&root) {
                        if plan.is_empty() {
                            continue;
                        }
                        let _ = tezuri::derive::settle(&root, &plan, &mut settle_progress);
                    }
                }
            });
        spawned.expect("settler thread");
        tx
    });
    let _ = tx.send(root);
}

// -- identity ----------------------------------------------------------------

#[tauri::command]
fn app_version() -> String {
    env!("CARGO_PKG_VERSION").to_string()
}

/// Open one of the About page's known destinations in the system browser.
/// The URL list is hardcoded — the webview never hands us arbitrary URLs,
/// and the launch is argv-only, never a shell.
#[tauri::command]
fn open_about_link(slug: String) -> Result<(), CommandError> {
    let url = match slug.as_str() {
        "source" => "https://github.com/sylin-org/tezuri",
        "brief" => "https://github.com/sylin-org/tezuri/blob/main/docs/PRODUCT-BRIEF.md",
        "decisions" => "https://github.com/sylin-org/tezuri/blob/main/docs/DECISIONS.md",
        "ghostlight" => "https://ghostlight.sylin.org",
        other => return Err(err(format!("unknown link: {other}"))),
    };
    #[cfg(target_os = "windows")]
    {
        std::process::Command::new("explorer")
            .arg(url)
            .spawn()
            .map_err(err)?;
    }
    #[cfg(target_os = "macos")]
    {
        std::process::Command::new("open")
            .arg(url)
            .spawn()
            .map_err(err)?;
    }
    #[cfg(all(unix, not(target_os = "macos")))]
    {
        std::process::Command::new("xdg-open")
            .arg(url)
            .spawn()
            .map_err(err)?;
    }
    Ok(())
}

#[tauri::command]
fn read_identity(path: String) -> Result<tezuri::identity::Identity, CommandError> {
    let p = PathBuf::from(&path);
    if !p.is_dir() {
        return Err(err("that folder does not exist"));
    }
    tezuri::identity::Identity::load(&p).map_err(err)
}

#[tauri::command]
fn save_identity(
    path: String,
    identity: tezuri::identity::Identity,
    session: State<Session>,
) -> Result<(), CommandError> {
    // Only the open session's own publication may be rewritten.
    let current = root(&session)?;
    if current != std::path::Path::new(&path) {
        return Err(err("that publication is not the open session"));
    }
    identity.save(&current).map_err(err)?;
    enqueue_settle(current);
    Ok(())
}

// -- desk ------------------------------------------------------------------

static APP_HANDLE: std::sync::OnceLock<tauri::AppHandle> = std::sync::OnceLock::new();

/// Runs off the main thread on purpose: a synchronous command would block the
/// very event loop the native dialog needs, hanging the window forever.
#[tauri::command(async)]
fn pick_folder() -> Result<Option<String>, CommandError> {
    use tauri_plugin_dialog::DialogExt;
    let handle = APP_HANDLE.get().ok_or_else(|| err("app not ready"))?;
    Ok(handle
        .dialog()
        .file()
        .blocking_pick_folder()
        .map(|f| f.to_string()))
}

/// Pick one existing image file (space covers). Returns its path, or None
/// when cancelled.
#[tauri::command(async)]
fn pick_image_file() -> Result<Option<String>, CommandError> {
    use tauri_plugin_dialog::DialogExt;
    let handle = APP_HANDLE.get().ok_or_else(|| err("app not ready"))?;
    Ok(handle
        .dialog()
        .file()
        .add_filter("Images", &["png", "jpg", "jpeg", "webp"])
        .blocking_pick_file()
        .map(|f| f.to_string()))
}

/// Read a user-picked file's bytes so it can flow into the media store.
/// The path comes from the native picker, never from free-form webview
/// text; the media store enforces its own format and size rules.
#[tauri::command]
fn read_file_bytes(path: String) -> Result<Vec<u8>, CommandError> {
    std::fs::read(&path).map_err(err)
}

// -- theme -------------------------------------------------------------------

#[derive(serde::Serialize)]
pub struct PresetView {
    pub id: String,
    pub name: String,
    pub description: String,
    pub css: String,
}

/// The open space's theme CSS; empty means the built-in look.
#[tauri::command]
fn read_theme(session: State<Session>) -> Result<String, CommandError> {
    tezuri::render::read_theme(&root(&session)?).map_err(err)
}

/// Persist the theme; an empty string clears it back to the built-in look.
#[tauri::command]
fn write_theme(css: String, session: State<Session>) -> Result<(), CommandError> {
    let current = root(&session)?;
    tezuri::render::write_theme(&current, &css).map_err(err)?;
    enqueue_settle(current);
    Ok(())
}

/// Compile one article into its final page — the preview is this exact
/// string, so what the author sees is what emits.
#[tauri::command]
fn render_article(slug: String, session: State<Session>) -> Result<String, CommandError> {
    tezuri::render::render_article(&root(&session)?, &slug).map_err(err)
}

/// Compose the Write-mode page: template segments in order with every slot's
/// live projection; the editor mounts where {{ARTICLE}} sits.
#[tauri::command]
fn write_compose(
    slug: String,
    session: State<Session>,
) -> Result<tezuri::render::WriteCompose, CommandError> {
    tezuri::render::compose_write_view(&root(&session)?, &slug).map_err(err)
}

/// The space's own article template text; None means the embedded default
/// is the presentation. Conduct edits drafts of this.
#[tauri::command]
fn read_template(session: State<Session>) -> Result<Option<String>, CommandError> {
    tezuri::render::read_template(&root(&session)?).map_err(err)
}

/// The Write-plane page: the artifact with the editor runtime injected.
#[tauri::command]
fn write_page(slug: String, session: State<Session>) -> Result<String, CommandError> {
    let root = root(&session)?;
    let tpl = tezuri::render::read_template(&root)
        .map_err(err)?
        .unwrap_or_else(|| tezuri::render::embedded_article_template().to_string());
    let (page, _) =
        tezuri::render::write_page_html(&root, &slug, &tpl, &media_base()).map_err(err)?;
    Ok(page)
}

/// The Write-plane page against a draft template (conduct preview).
#[tauri::command]
fn write_page_draft(
    slug: String,
    template: String,
    session: State<Session>,
) -> Result<String, CommandError> {
    let root = root(&session)?;
    let (page, _) =
        tezuri::render::write_page_html(&root, &slug, &template, &media_base()).map_err(err)?;
    Ok(page)
}

/// The embedded default template's bytes, so conducting can seed a draft
/// even before the space owns a file.
#[tauri::command]
fn default_template() -> String {
    tezuri::render::embedded_article_template().to_string()
}

/// Persist an accepted layout draft. Empty string removes the file so the
/// embedded default speaks again. Journaled; settling follows.
#[tauri::command]
fn write_template(text: String, session: State<Session>) -> Result<(), CommandError> {
    let current = root(&session)?;
    tezuri::render::write_template(&current, &text).map_err(err)?;
    enqueue_settle(current);
    Ok(())
}

/// Compose against a *draft* (conduct in session): same payload as
/// write_compose but from supplied bytes, never touching files.
#[tauri::command]
fn write_compose_draft(
    slug: String,
    template: String,
    session: State<Session>,
) -> Result<tezuri::render::WriteCompose, CommandError> {
    tezuri::render::compose_write_view_with(&root(&session)?, &slug, &template).map_err(err)
}

/// The template editor's live specimen: one real article through the real
/// pipeline under draft bytes — the byte-exact lens before anything saves.
#[tauri::command]
fn render_specimen(
    slug: String,
    template: String,
    session: State<Session>,
) -> Result<(String, Vec<String>), CommandError> {
    tezuri::render::render_article_with(&root(&session)?, &slug, &template).map_err(err)
}

// -- catalog -----------------------------------------------------------------

#[derive(serde::Serialize)]
pub struct ControlView {
    kind: String,
    values: Vec<String>,
    default: String,
}

#[derive(serde::Serialize)]
pub struct OptionView {
    key: String,
    label: String,
    control: ControlView,
}

#[derive(serde::Serialize)]
pub struct SlotCatalogEntry {
    name: String,
    doc: String,
    hosts: Vec<String>,
    options: Vec<OptionView>,
}

/// The characterized vocabulary: menus, palettes, and autocomplete are all
/// views over what this returns.
#[tauri::command]
fn slot_catalog() -> Vec<SlotCatalogEntry> {
    use tezuri::slots::{registry, Control, Host};
    registry()
        .into_iter()
        .map(|d| SlotCatalogEntry {
            name: d.name.to_string(),
            doc: d.doc.to_string(),
            hosts: d
                .hosts
                .iter()
                .map(|h| match h {
                    Host::Flow => "flow".to_string(),
                    Host::Rail => "rail".to_string(),
                })
                .collect(),
            options: d
                .options
                .iter()
                .map(|o| OptionView {
                    key: o.key.to_string(),
                    label: o.label.to_string(),
                    control: match &o.control {
                        Control::Toggle => ControlView {
                            kind: "toggle".into(),
                            values: vec!["on".into(), "off".into()],
                            default: o.default.to_string(),
                        },
                        Control::Choice(vs) => ControlView {
                            kind: "choice".into(),
                            values: vs.iter().map(|s| s.to_string()).collect(),
                            default: o.default.to_string(),
                        },
                        Control::Count { min, max } => ControlView {
                            kind: "count".into(),
                            values: vec![min.to_string(), max.to_string()],
                            default: o.default.to_string(),
                        },
                    },
                })
                .collect(),
        })
        .collect()
}

/// Compile every article into render/ inside the publication.
#[tauri::command]
fn emit_render(session: State<Session>) -> Result<Vec<String>, CommandError> {
    tezuri::render::emit_render(&root(&session)?).map_err(err)
}

#[tauri::command]
fn desk(session: State<Session>) -> Result<Desk, CommandError> {
    Desk::rebuild(&root(&session)?).map_err(err)
}

// -- articles ----------------------------------------------------------------

#[derive(serde::Serialize)]
pub struct ArticleFull {
    pub article: Article,
    /// The editing text: unsaved dirty copy when one exists, else canonical.
    pub raw: String,
    /// The canonical article.md, always — the diff baseline.
    pub canonical_raw: String,
    /// True while unsaved edits sit in the dirty copy.
    pub dirty: bool,
}

#[tauri::command]
fn read_article(slug: String, session: State<Session>) -> Result<ArticleFull, CommandError> {
    let root_path = root(&session)?;
    let a = Article::load(&root_path, &slug).map_err(err)?;
    let doc_path = tezuri::articles::Article::doc_path(&root_path, &slug).map_err(err)?;
    let dirty = tezuri::articles::Article::read_dirty(&root_path, &slug).map_err(err)?;
    let canonical_raw = std::fs::read_to_string(&doc_path).map_err(err)?;
    let raw = match &dirty {
        Some(d) => d.clone(),
        None => canonical_raw.clone(),
    };
    Ok(ArticleFull {
        article: a,
        raw,
        canonical_raw,
        dirty: dirty.is_some(),
    })
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

/// Autosave: the editing copy lands in the space's dirty drafts. The
/// canonical article.md is untouched until an explicit Save.
#[tauri::command]
fn save_dirty(slug: String, document: String, session: State<Session>) -> Result<(), CommandError> {
    let root_path = root(&session)?;
    tezuri::articles::Article::write_dirty(&root_path, &slug, &document).map_err(err)
}

/// Explicit Save: the editing text becomes the canonical article.md. The
/// dirty copy is absorbed — it held nothing the canonical file now lacks.
#[tauri::command]
fn save_document(
    slug: String,
    document: String,
    session: State<Session>,
) -> Result<String, CommandError> {
    let root_path = root(&session)?;
    let mut a = Article::load(&root_path, &slug).map_err(err)?;
    a.document = document;
    let hash = a.save(&root_path).map_err(err)?;
    tezuri::articles::Article::clear_dirty(&root_path, &slug).map_err(err)?;
    Ok(hash)
}

/// Drop unsaved edits: the dirty copy is deleted, the canonical file speaks.
#[tauri::command]
fn discard_dirty(slug: String, session: State<Session>) -> Result<(), CommandError> {
    let root_path = root(&session)?;
    tezuri::articles::Article::clear_dirty(&root_path, &slug).map_err(err)
}

/// Save the fact fields (meta) without touching the document flow.
#[tauri::command]
fn save_meta(meta: ArticleMeta, slug: String, session: State<Session>) -> Result<(), CommandError> {
    let root_path = root(&session)?;
    let mut a = Article::load(&root_path, &slug).map_err(err)?;
    a.meta = meta;
    a.meta.slug = slug;
    a.save_meta_only(&root_path).map_err(err)
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

/// The full assistant catalog for the Configuration editor. `list_assistants`
/// serves the recipe dropdown; this serves editing.
#[tauri::command]
fn read_assistant_catalog(
    session: State<Session>,
) -> Result<Vec<consult::Assistant>, CommandError> {
    Ok(consult::Catalog::load(&root(&session)?)
        .map_err(err)?
        .assistants)
}

/// Persist the edited catalog back to assistants.md. Notes and any YAML the
/// author keeps beside the entries are rebuilt by consult's save; entries are
/// replaced wholesale because this form owns the list.
#[tauri::command]
fn save_assistant_catalog(
    entries: Vec<consult::Assistant>,
    session: State<Session>,
) -> Result<(), CommandError> {
    let catalog = consult::Catalog {
        assistants: entries,
    };
    catalog.save(&root(&session)?).map_err(err)
}

// -- asset library: official + downloaded, with selection history -----------

#[derive(Serialize)]
struct PickerList {
    themes: Vec<tezuri::render::PickerEntry>,
    templates: Vec<tezuri::render::PickerEntry>,
}

/// Everything the picker offers: official assets first, downloads after.
#[tauri::command]
fn picker_list() -> Result<PickerList, CommandError> {
    let (themes, templates) = tezuri::render::picker_list().map_err(err)?;
    Ok(PickerList { themes, templates })
}

/// Apply a theme or template asset: its bytes become the space's own file,
/// journaled, and the selection joins the space's history ring.
#[tauri::command]
fn picker_apply(kind: String, id: String, session: State<Session>) -> Result<String, CommandError> {
    let current = root(&session)?;
    let kind = tezuri::render::picker_apply(&current, &kind, &id).map_err(err)?;
    enqueue_settle(current);
    Ok(kind)
}

/// History depth for both kinds (theme, template).
#[tauri::command]
fn picker_history(session: State<Session>) -> Result<(usize, usize), CommandError> {
    tezuri::render::picker_history(&root(&session)?).map_err(err)
}

/// Step the history ring (-1 back, +1 forward) and re-apply. Returns the
/// new position.
#[tauri::command]
fn picker_history_step(
    kind: String,
    delta: i32,
    session: State<Session>,
) -> Result<i64, CommandError> {
    let current = root(&session)?;
    let pos = tezuri::render::picker_history_step(&current, &kind, delta).map_err(err)?;
    enqueue_settle(current);
    Ok(pos)
}

/// Fetch one asset file over the network, on the user's explicit command.
/// Bounded; stored under the app-state home, never inside a publication.
#[tauri::command]
fn download_asset(url: String) -> Result<String, CommandError> {
    tezuri::render::download_asset(&url).map_err(err)
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
        .register_uri_scheme_protocol("media", |ctx, request| {
            serve_media(ctx.app_handle().clone(), request)
        })
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
            pick_image_file,
            read_file_bytes,
            open_publication,
            space_stats,
            media_base,
            read_theme,
            read_identity,
            save_identity,
            app_version,
            open_about_link,
            read_assistant_catalog,
            save_assistant_catalog,
            read_theme,
            write_theme,
            render_article,
            write_compose,
            read_template,
            write_page,
            write_page_draft,
            default_template,
            write_template,
            write_compose_draft,
            render_specimen,
            slot_catalog,
            picker_list,
            picker_apply,
            picker_history,
            picker_history_step,
            download_asset,
            emit_render,
            desk,
            read_article,
            save_article,
            save_dirty,
            save_document,
            save_meta,
            discard_dirty,
            save_article_raw,
            create_article,
            set_article_state,
            add_media,
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
