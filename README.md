# Tezuri

Tezuri is a local press: a desktop application for people who publish long-form writing from their
own git repositories.

Open a publication, write, add images, prove the site's own build, then review and publish exactly
the files you intend. Nothing leaves the machine unless the user explicitly asks for it.

## Status

Tezuri now has an early working core in Rust and Tauri. It covers the spine of the workflow: a
registry of publications with last-opened memory, a document-first editor with autosave over plain
Markdown articles, a content-addressed media store with declared renditions, advisory consult verbs
through the author's own assistant harnesses, and a human-gated ship pipeline ending in
review-and-select commits and lease-checked pushes. The desk index rebuilds from files whenever a
publication opens; nothing canonical lives inside the application.

The presentation contract has its engine: spaces render through a five-rule template language
(`src/slots.rs`) — one pipeline serves the emitted page and Write mode alike, where the space's
own `templates/article.html` composes around a live editor at `{{ARTICLE}}`. Default
presentation is deliberately calm; gorgeousness arrives as starter packs owned by the space.

[`docs/PRODUCT-BRIEF.md`](docs/PRODUCT-BRIEF.md) is the authority for what the product must do and
must never do. Deleted source and historical implementation choices are not design inputs for the
new application.

## Run it

Prerequisites: a current stable Rust toolchain and Node.js 20 or newer. On Windows, Tezuri uses the
installed system WebView2 runtime and does not download one at startup.

On Windows, double-click or run `launch.bat` — it installs the locked frontend dependencies and
builds the interface bundle on first use, then opens the desktop application. Missing Node.js or
Rust are reported with instructions instead of failing silently.

Manual equivalent, and the path for macOS or Linux:

```powershell
npm --prefix src-tauri/ui ci
npm --prefix src-tauri/ui run build
cargo run --release -p tezuri-desktop
```

The first screen lists your registered publications with a native folder picker. Tezuri writes its
article folders only when you create writing. A small `tezuri` CLI drives the same domain library
from a terminal (`cargo run -p tezuri -- desk`).

The canonical repository check is:

```powershell
pwsh ./eng/verify.ps1
```

It type-checks and builds the frontend bundle, checks rustfmt, runs clippy with warnings denied,
runs the whole test suite, compiles the desktop executable, and audits the working patch for
whitespace errors.
Platform installer bundling is not wired up yet; once installers exist, release checks will cover
the artifact a person actually downloads.

## Product model

Files are truth; the desk is a lens. An article's Markdown file and its small `meta.yaml` sidecar
are canonical inside the chosen publication; indexes, journals, previews, and renditions are derived
caches Tezuri may delete and rebuild at any time. Metadata Tezuri does not model is preserved
verbatim.

This gives the writer a calm, structured editing experience without taking ownership of their work:
content and media remain ordinary repository files that are useful without Tezuri.

## Repository contract

Tezuri uses one deliberately fixed article layout inside each publication:

```text
articles/<article-slug>/
  article.md    # the article: H1 title, optional _standfirst_ line, body — source of truth
  meta.yaml     # small sidecar: state, date, tags, cover — unknown fields preserved verbatim
media/          # content-addressed images; renditions are declared intent, derived on demand
```

Consult support is plain files too: `recipes/<name>.md` verb templates and an author-curated
`assistants.md` harness catalog. Supporting files appear only when you use those features; Tezuri
creates no setup file and demands none.

Proof uses the destination repository's conventional build — a `package.json` `build` script through
its matching installed package manager, or Hugo when a standard Hugo configuration is present. It
runs on a disposable copy with a timeout, bounded output, and credential redaction.

## Trust boundary

Tezuri is local, single-user, and offline by default. It has no accounts, telemetry, analytics,
update checks, crash reporting, hosted service, or hidden network work. Writes stay inside the
chosen publication behind path confinement, saves are atomic temp-file swaps recorded in a journal,
subprocesses run from argv arrays with bounded time and output, and credentials are never stored.

Saving and publishing are deliberately separate. Saving never stages, commits, pushes, changes
branches, or rewrites history. Publishing reviews changed paths, commits only the selected ones, and
pushes only while the reviewed remote state still holds.

## Implementation direction

Tezuri is a domain-driven monolith: one `tezuri` library crate whose modules follow the product's
domains — spine, publications, identity, articles, media, desk, consult, ship, theme, render, and
derive — plus two thin drivers over it, a `tezuri` CLI binary and the `tezuri-desktop` Tauri shell.
The webview reaches native power only through named product commands; there is no generic frontend
filesystem, shell, process, or network authority. The React bundle lives at `src-tauri/ui`.

Derived artifacts are lazily fixable: opening a space settles it in the background — missing or
stale rendered pages and image renditions derive quietly while you work; previews compile on
demand and never wait.

Consequential choices belong in [`docs/DECISIONS.md`](docs/DECISIONS.md). The visual and interaction
contract lives in [`docs/design/SYLIN-VISUAL-CONTRACT.md`](docs/design/SYLIN-VISUAL-CONTRACT.md).

## Documents

- [Product brief](docs/PRODUCT-BRIEF.md)
- [Implementation decisions](docs/DECISIONS.md)
- [Project memory](docs/MEMORY.md)
- [Sylin visual contract](docs/design/SYLIN-VISUAL-CONTRACT.md)
- [Agent guide](AGENTS.md)

## Licence

Apache-2.0. See [LICENSE](LICENSE).
