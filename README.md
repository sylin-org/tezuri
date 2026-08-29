<div align="center">

<img src="res/tezuri-mascot.png" alt="Tezuri mascot: a small origami paper crane with a serene face and a red seal tag, on a starry black background" width="100" height="100">

# Tezuri

**A desk for an author's entire publishing life.**

A local press for long-form writing. Your articles live as plain Markdown in a
git repository you already own; Tezuri is the desk you write them at, and the
press that ships exactly the files you approved.

Part of [Sylin](https://sylin.org) — tools that run on your hardware, show you
their work, and keep running long after you've stopped thinking about where
they came from.

</div>

---

A publication is a folder — usually a git repository. Articles are Markdown. A
small `meta.yaml` rides beside each one. Images deduplicate themselves into a
content-addressed store. Everything Tezuri knows can be deleted and rebuilt
from these files, so the tool gets to be a lens instead of a keeper: index
them, write at them, publish from them — and if Tezuri vanished tomorrow,
every word would still be sitting exactly where you put it, as useful as the
day it was written.

The workflow is three verbs. **Write** at a document-first editor over
live-rendered Markdown. **Consult** advisory agents through the assistant
harnesses you already use, one named verb at a time, results arriving as diffs
you accept hunk by hunk. **Ship** through a pipeline with human gates exactly
where damage is irreversible: proof against your site's own build, a review
scaled to the stakes, then a commit you selected line by line. Saving touches
nothing but files, always.

## How it holds together

Three rules, enforced per feature:

1. **Files are truth; the desk is a lens.** Indexes, journals, previews and
   renditions are derived caches Tezuri may delete and rebuild at any time. A
   feature that cannot express itself as plain files does not ship.
2. **One grammar of change.** Every mutation — a human edit, an accepted
   suggestion, an imported image, a publish commit — flows the same path: an
   atomic write plus a journal entry, with review before anything
   irreversible. The journal answers a fair question: *what did this app ever
   do to my files?*
3. **Your build is authoritative.** When the repository has its own site
   build, that build stays the referee. Tezuri proves it; it never competes
   with it.

The pieces those rules produce:

| Piece | What it is | Canonical? |
| --- | --- | --- |
| **Publication** | A folder carrying articles, media and support files (`voice.md`, `tone.md`, recipes) | Yes — yours |
| **Article** | `article.md` + a small `meta.yaml` sidecar | Yes — yours |
| **Media** | Content-addressed images; renditions are declared intent, derived on demand | Originals yes; renditions derived |
| **Desk** | The local index: states, search, last-opened | Derived — rebuilt from files at every open |
| **Journal** | A per-article log of what the app did, when, and why | Derived — a receipt, not a source |

## Run it

Prerequisites: a current stable Rust toolchain and Node.js 20 or newer. On
Windows, Tezuri uses the installed system WebView2 runtime and does not
download one at startup.

On Windows, double-click or run `launch.bat` — it installs the locked frontend
dependencies and builds the interface bundle on first use, then opens the
desktop application. Missing Node.js or Rust are reported with instructions
instead of failing silently.

Manual equivalent, and the path for macOS or Linux:

```powershell
npm --prefix src-tauri/ui ci
npm --prefix src-tauri/ui run build
cargo run --release -p tezuri-desktop
```

The first screen lists your registered publications with a native folder
picker. Tezuri writes its article folders only when you create writing. A
small `tezuri` CLI drives the same domain library from a terminal:

```powershell
cargo run -p tezuri -- desk
```

## Where it stands

**Works today:**

- A registry of publications with last-opened memory, and a desk index that
  rebuilds from files whenever a publication opens.
- A document-first editor with autosave over plain Markdown articles.
- A content-addressed media store with declared renditions; images arrive by
  paste or drop and land as processed media with a correct relative link.
- Advisory consult verbs through the author's own assistant harnesses.
- A human-gated ship pipeline ending in review-and-select commits and
  lease-checked pushes.
- Presentation through a five-rule template language (`src/slots.rs`): the
  space's own `templates/article.html` composes around a live editor at
  `{{ARTICLE}}`, and one pipeline serves the emitted page and Write mode
  alike.

**Next:**

- Platform installer bundling. Until then the release binary is the
  downloadable artifact, checked by `eng/release-check.ps1`.
- Starter packs owned by the space, for presentation beyond the deliberately
  calm default.

**Not settled:**

- Pre-1.0: package IDs and APIs are open until the first tagged release.
- The bench is Windows. The manual path above is written for macOS and Linux
  as well, but Windows is where it is exercised.

## The repository contract

One deliberately fixed article layout inside each publication:

```text
articles/<article-slug>/
  article.md    # the article: H1 title, optional _standfirst_ line, body — source of truth
  meta.yaml     # small sidecar: state, date, tags, cover — unknown fields preserved verbatim
media/          # content-addressed images; renditions are declared intent, derived on demand
```

Consult support is plain files too: `recipes/<name>.md` verb templates and an
author-curated `assistants.md` harness catalog. Supporting files appear only
when you use those features; Tezuri creates no setup file and demands none.

Metadata Tezuri does not model is preserved verbatim and written back
untouched.

Proof uses the destination repository's conventional build — a `package.json`
`build` script through its matching installed package manager, or Hugo when a
standard Hugo configuration is present. It runs on a disposable copy with a
timeout, bounded output, and credential redaction.

## The trust boundary

Tezuri is local, single-user, and offline by default. It has no accounts,
telemetry, analytics, update checks, crash reporting, hosted service, or
hidden network work. The network moves only when you ask it to: a push, a
fetch, a bounded asset download from the picker.

- Writes stay inside the chosen publication, behind path confinement. Never a
  home directory, credential store, or git's internal directory.
- Saves are atomic temp-file swaps recorded in the journal.
- Subprocesses run from argument arrays with bounded time and output; your
  input never becomes executable shell text.
- Credentials are delegated to your existing tooling and never stored.
- Saving and publishing are deliberately separate. Saving never stages,
  commits, pushes, changes branches, or rewrites history. Publishing reviews
  changed paths, commits only the selected ones, and pushes only while the
  reviewed remote state still holds.

## Under the hood

Tezuri is a domain-driven monolith: one `tezuri` library crate whose modules
follow the product's domains — spine, publications, identity, articles,
media, desk, consult, ship, theme, render, and derive — plus two thin drivers
over it, the `tezuri` CLI binary and the `tezuri-desktop` Tauri shell. The
webview reaches native power only through named product commands; there is no
generic frontend filesystem, shell, process, or network authority. The React
bundle lives at `src-tauri/ui`.

Derived artifacts are lazily fixable: opening a space settles it in the
background — missing or stale rendered pages and image renditions derive
quietly while you work; previews compile on demand and never wait.

## Check everything

```powershell
pwsh ./eng/verify.ps1
```

Type-checks and builds the frontend bundle, checks rustfmt, runs clippy with
warnings denied, runs the whole test suite, compiles the desktop executable,
and audits the working patch for whitespace errors.

Before calling a release shippable, exercise the artifact a person actually
downloads:

```powershell
pwsh ./eng/release-check.ps1
```

It builds the release binary with the current bundle embedded, launches it as
a real process, confirms it stays alive, and stops it cleanly.

## Documents

- [Product brief](docs/PRODUCT-BRIEF.md) — the product authority
- [Implementation decisions](docs/DECISIONS.md) — choices in force
- [Project memory](docs/MEMORY.md) — standing preferences
- [Sylin visual contract](docs/design/SYLIN-VISUAL-CONTRACT.md) — visual and interaction grammar
- [Agent guide](AGENTS.md) — onboarding for agents

## Kin

Tezuri keeps Sylin's continuity promise on the written word. Kin in the same
house: [Koan](https://github.com/sylin-org/koan-framework) grows
applications, [Zen Garden](https://github.com/sylin-org/zen-garden) tends old
machines into a garden, [Suzu](https://github.com/sylin-org/suzu) lets a
house feel its servers. Tezuri gives writing a desk that never takes custody
of the manuscript.

## Licence

Apache-2.0. See [LICENSE](LICENSE).
