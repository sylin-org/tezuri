# Implementation decisions

This file records consequential implementation decisions that are currently in force. The product
contract belongs in [`PRODUCT-BRIEF.md`](PRODUCT-BRIEF.md) and is not repeated here.

The log starts from the implementation reset. Earlier implementation choices are intentionally not
carried forward; repository history remains history, not architecture guidance.

---

## 2026-08-14 — Use one repository-native article convention

An article lives at `content/articles/<slug>/`. Its canonical `article.json` contains the modeled
metadata and a bounded Tiptap-compatible document tree; content-addressed images live in the sibling
`media/` directory. Tezuri regenerates `index.md` from the record on every save and repairs missing
or stale generated Markdown when the article is opened.

Unknown top-level JSON values are retained when Tezuri writes its modeled fields. The fixed layout
keeps projects setup-free and portable; v1 does not offer path templates, arbitrary document nodes,
raw HTML, embeds, or a second content schema.

This settles the record format and layout left open by the implementation reset. It replaces no
product behavior.

## 2026-08-14 — Keep native authority behind product commands

The bundled webview can invoke only named product operations. Rust owns native dialogs, canonical
project roots, path and link checks, atomic writes, media decoding, process execution, Git, and the
project registry. Opaque project-session identifiers bind every repository operation to the project
currently open in the one main window. A native close request is held until the frontend has flushed
its current save; a failed save leaves the window and draft open.

The Tauri application enables only the single-instance and native-dialog plugins. It grants the
main window an explicit command allowlist and no generic frontend filesystem, shell, process,
opener, asset, updater, or network capability. Frontend code reaches Tauri only through `src/ipc.ts`.

This replaces the undecided native command and security boundary at reset time.

## 2026-08-14 — Make proof, publication, and import bounded workflows

Proof recognizes conventional package-manager builds and Hugo, then runs the detected build in a
disposable copy with fixed arguments, bounded time and output, process-tree cleanup, offline hints,
and redacted evidence. It never accepts executable text through the interface. A repository build is
user-owned code, so v1 does not pretend that environment hints are an operating-system network
sandbox.

Publication is review, exact path selection, commit, explicit fetch, then lease-checked push. Each
step revalidates the state it was shown; unrelated staged and working-tree content is preserved.
Substack is the one v1 import convention: preview and apply are separate, local images are copied,
remote references are reported without fetching, and existing article folders are never replaced.

This settles the v1 proof, publication, and import paths left open by the implementation reset.

## 2026-08-14 — Use a domain-driven monolith

Tezuri is one application, one Rust crate, and one frontend bundle. Code is organized around the
product's domains — projects, articles, media, proof, publication, and import — with Tauri kept as a
thin desktop and command boundary.

Domain-driven means the product vocabulary and rules shape the code. It does not mean a crate per
domain, an interface for every operation, a command bus, dependency-injection machinery, or layers
that only forward calls. Shared path safety and atomic-write code exists once and is used directly.

A new crate, abstraction, or subsystem needs a concrete pressure that the monolith cannot express
cleanly. Until then, prefer a small number of cohesive modules and working vertical slices.

This refines the Rust and Tauri decision below. It replaces no product behavior.

## 2026-08-14 — Build Tezuri afresh in Rust and Tauri

Tezuri will be a fresh Rust and Tauri desktop implementation of the existing product brief. This is
not a source port and not a redesign of the product objective.

At the reset point only the foundation was decided. Later entries refine the structure; remaining
choices must still be made from current requirements and validated in working vertical slices.

Deleted source and earlier architecture are not implementation evidence unless the owner explicitly
requests a historical comparison.

This replaces every prior stack-specific implementation decision. It does not replace the product
brief or the Sylin visual and accessibility contract.
