# Tezuri

Tezuri is a local press: a desktop application for people who publish long-form writing from their
own git repositories.

Open a project, write, add images, prove the site's own build, then review and publish exactly the
files you intend. Nothing leaves the machine unless the user explicitly asks for a git network
operation.

## Status

Tezuri now has a runnable v1 implementation in Rust and Tauri. The application covers the complete
local workflow: project registry, structured writing and autosave, safe image handling, site proof,
reviewed Git publication, and previewed Substack archive import.

[`docs/PRODUCT-BRIEF.md`](docs/PRODUCT-BRIEF.md) is the authority for what the product must do and
must never do. Deleted source and historical implementation choices are not design inputs for the
new application.

## Run it

Install a current Rust toolchain (Rust 1.85 or newer), Node.js 24, and the native prerequisites for
Tauri 2 on your operating system. On Windows, Tezuri uses the installed system WebView2 runtime and
does not download one at startup.

```powershell
./start.bat
```

`start.bat` is the Windows source launcher. It installs the locked frontend dependencies when they
are absent, then opens the real Tauri application. On macOS or Linux, run `npm ci` followed by
`npm run tauri -- dev`.

The first screen opens the operating system's folder picker. Choose the root of a Git repository;
Tezuri creates its article folders only when you create or import writing.

The canonical repository check is:

```powershell
pwsh ./eng/verify.ps1
```

It type-checks and tests the frontend, formats, lints, and tests the Rust crate, builds the embedded
desktop executable, checks the Tauri authority boundary, and checks the patch for whitespace errors.
To create platform installers locally, run `npm run tauri -- build`.

## Product model

Tezuri owns an article's canonical record inside the user's repository and generates the site's
Markdown from it. Generated content is output, not an editing source. Metadata Tezuri does not
understand is preserved unchanged.

This gives the writer a calm, structured editing experience without taking ownership of their work:
content and media remain ordinary repository files that are useful without Tezuri.

## Core experience

- **Projects:** add, open, reorder, remove, and relocate repositories through the native interface.
- **Write:** a document-first, autosaving editor with restrained metadata controls and a searchable
  body of work.
- **Media:** paste or drop safe images; Tezuri stores them with the article and deduplicates identical
  content.
- **Prove:** run the site's own configured build against an isolated copy with a timeout, bounded
  output, and credential redaction.
- **Publish:** review and select changed paths, commit only that selection, and push separately only
  if the reviewed remote state still holds.
- **Import:** preview an offline archive import, preserve source metadata, bring local images, and
  safely skip articles that already exist.

## Repository contract

Tezuri uses one deliberately fixed article layout:

```text
content/articles/<article-slug>/
  article.json    # canonical structured article
  index.md        # generated on every save; never read as editing input
  media/          # content-addressed PNG, JPEG, WebP, or GIF images
```

`article.json` contains `title`, `standfirst`, `body`, `state`, `publicationDate`, and `tags`.
`body` is a bounded, versioned rich-document tree. Unknown top-level metadata is retained when the
modeled fields change. Tezuri creates this layout without a project setup file.

Proof uses the repository's conventional build: a `package.json` `build` script through its matching
installed package manager, or Hugo when a standard Hugo configuration file is present. It runs in a
disposable project copy with a timeout, bounded output, offline package-manager settings, and
credential redaction. The synthetic [`samples/tezuri-site`](samples/tezuri-site) project demonstrates
the expected layout and build contract.

## Trust boundary

Tezuri is local, single-user, and offline by default. It has no accounts, telemetry, analytics,
update checks, crash reporting, hosted service, or implicit fetching. Writes stay inside the chosen
project, saves are atomic, application settings contain no canonical content, and text entered by a
user never becomes shell syntax.

Saving and publishing are deliberately separate. Saving never stages, commits, pushes, changes
branches, or rewrites history.

## Implementation direction

Tezuri is a domain-driven monolith: one Rust crate, one embedded React/Tiptap frontend, and one Tauri
desktop process. Product operations cross a narrow typed command boundary; the frontend receives no
generic filesystem, shell, process, or network authority. The cohesive domain modules are projects,
articles, media, proof, publication, and import.

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
