# Implementation decisions

This file records consequential implementation decisions that are currently in force. The product
contract belongs in [`PRODUCT-BRIEF.md`](PRODUCT-BRIEF.md) and is not repeated here.

The log starts from the implementation reset. Earlier implementation choices are intentionally not
carried forward; repository history remains history, not architecture guidance. Entries whose model
was wiped with the first tree have been removed rather than left in force alongside their
replacements.

---

## 2026-08-26 — The template language: five rules, live slots, conducted

The presentation layer gets a frozen v1 contract. Content is sacred (`.md` + `meta.yaml`);
presentation (`theme.css`, `templates/*.html`) is the space's own files, optional, with a
deliberately dumb default: the entire built-in template is `{{ARTICLE}}` over a GitHub-calm
stylesheet. Gorgeousness — the gposingway look — is a *starter pack* the author picks and then
owns as plain files. One pipeline serves everything (Gather → evaluate context → substitute →
behaviors); the article page, the index, a feed, an embeddable card, and Write mode itself are
all template + context through the same engine.

**The five rules.** (1) Slots are English, not code. (2) A slot never contains logic, and an
empty slot renders zero bytes — "no next article?" is a CSS question (`nav:empty`), never a
template one. (3) One optional hint after `|`, arguments as English data (`{{date | long}}`,
`{{article-list | count:8}}`), never control flow. (4) The template owns layout and `<head>`;
Tezuri owns behaviors (scroll-spy, lightbox) and theme application — `{{css}}` is gone, templates
never reference the theme. (5) A mistake is a whisper: unknown slot renders empty plus one editor
note; missing `{{ARTICLE}}` falls back to plain flow with a chirp; the page never breaks.

**Grammar.** UPPERCASE = the one required slot; lowercase = optional. `{{ARTICLE}}` is the
page's reason to exist and the live editing surface in Write mode.

**Slot vocabulary v1.**
- Content: `title`, `standfirst`, `date | long/iso`, `reading_time`, `tags | pills/text`,
  `cover_img`, `excerpt | N`
- Navigation: `toc`, `prev_link`/`prev_title`, `next_link`/`next_title`, `home_link`,
  `body_class` (context classes like `is-article is-published`, Ghost's cheap trick)
- Site: `site_name`, `byline`, `site_cta`
- Collections: `article-list | count:N / newest / around / similar`; for index/feed/card
  outputs: `items`, `site_url`, `updated`

**Slot semantics.** Published only, always — lists never leak drafts. Current article excluded.
`around` = a timeline window centered on this article; `similar` = ranked by shared-tag count,
date tiebreak. First has no prev, last has no next — until the published set changes, at which
point affected pages re-derive through the existing lazy settle (a published-index fingerprint
joins the staleness check). Feed = `templates/feed.xml`, card = `templates/card.html`: RSS is a
template, not a feature. Unknown slots render empty and are surfaced once in the editor.

**Styling contracts** (theme surface, documented): `.article-prose`, `.tagpill`,
`ul.article-list > li.article-list-item > a`, `body_class` values.

**The projection rule — Write mode is the render.** Every slot renders its live projection in
the editor: first-class fields get inline editors (`{{tags}}` → pill editor fed by the space's
tag vocabulary, `{{date}}` → date control, `{{cover_img}}` → media picker); flow-derived fields
(title, standfirst) stay click-to-edit in the `{{ARTICLE}}` flow, and additional template
instances of them are live-synced mirrors — one content state, many slots, no dual carets.
Behaviors are Tezuri's natively; scripts are stripped in-app and re-provided bound to the
template's classes. No-blocks boundary holds in v1: loops and conditionals are deliberately
absent; pipeline-composed lists and boundary-aware nav cover the 90%, and a template item
fragment is the named-but-deferred escape hatch. Preview remains the byte-exact proof lens —
the emitted bytes themselves.

**Conduct, don't configure.** Every slot in Write mode is menuable: click its rendered content
and a small anchored popover offers the hints the registry declares for it — Pills/Text for
tags, Long/ISO for dates, Newest/Around/Similar with counts for lists, some with live
mini-previews. A choice re-renders that slot in place and rewrites the template *draft*
(specimen follows); saving to `templates/article.html` follows the usual propose→apply and is
journaled. Multi-instance slots are menued per instance. View-only toggles (such as
mirror-vs-editable for a repeated title) are session-only and never touch the file. No custom
expressions, no modal. The menu is the docs, wearing the product.

**Editor delight commitments.** Ghost hints for the bare default template; `{{` autocomplete
with one-line docs; an insert palette of unused slots; the template editor's live specimen
renders a real article through the real pipeline; starter packs (Vanilla, GPosingway) copied
into the space on pick. An earlier hardcoded in-editor TOC is superseded by this contract —
navigation exists if and only if the template says `{{toc}}`.

Prior art behind the stance: Liquid's learnable restraint vs Go templates' debugging tax
(Jekyll/Hugo comparisons); Eleventy's everything-is-a-template feeds; Ghost's prev/next and
`body_class` patterns (adopted pre-composed, without block syntax); Typora's live-preview
devotion and theme-by-pure-CSS culture ("most have a clunky two-pane window… which is just
terrible"); Publii's desktop-CMS shape with internally-held content — the gap Tezuri's
files-are-truth fills. This supersedes the render-stage entry's template details where they
conflict (embedded default template, `{{cover_section}}`/`{{cta}}`/`{{css}}` placeholders).

## 2026-08-26 — Work in the background; ask in the flow, never over it

Tezuri takes care of itself. Derived work — settling, rendering, rendition derivation — happens
without being asked, and surfaces only small, temporary chirps in the band facts while it runs.
Questions are asked only when an answer is truly needed, and then **in the flow of the object**:
adding a space shows an inline form card in the grid; removing one turns its row into a two-step
inline confirmation; a new article's slug and title are asked in the rail; link and image targets
are edited in anchored popovers beside their toolbar button. There are no modal dialogs. A modal
seizes the whole window, breaks context, and interrupts thought — the exact opposite of a calm
desk. Native file/folder pickers remain the one OS-level interruption, because choosing a folder
is genuinely the user's job.

This replaces the promise-based modal system (`prompts.tsx`, ModalHost/askForm/askConfirm) that
had replaced the webview's silent no-op `window.prompt`/`confirm`; the modal was the wrong
replacement, not the wrong implementation.

## 2026-08-26 — Settle lazily: derived artifacts heal in the background

Loading a current-state repository is a normal state, not an error state: pages not yet emitted,
renditions not yet derived, stale outputs older than their inputs — all noticed by a cheap
stat-only scan (`derive::scan_plan`) and repaired by a single sequential background worker
(`derive::settle`) that starts when a publication opens, and again after an identity or theme
save. The band reports quiet progress ("settling 3/8") and returns to silence. Previews never
wait for the settler — they compile on demand regardless — and everything stays idempotent:
running the scan-and-settle twice changes nothing, so no locking protocol is needed alongside the
existing atomic writes.

With this, `render/` is defined as the publishable set: review and published articles only.
Drafts preview on demand and are never written behind the author's back; the index page lists
the same set. Rendition pre-warming covers thumb and 1024-width; other recipes derive at display
time exactly as before.

This extends "files are truth; the desk is a lens" into an operating rule: every derived artifact
is lazily fixable, by design, without ceremony.

## 2026-08-26 — Add the render stage; renderer authority is conditional

Tezuri compiles articles into complete styled HTML: the Markdown flow, `meta.yaml`, the space's
`theme.css`, and a layout template become self-contained pages written to `render/` inside the
publication (`render/<slug>.html` plus a simple `render/index.html`). The renderer is Rust-side
(pulldown-cmark) for deterministic, byte-stable output shared by the CLI, tests, and the app.
Gallery-by-adjacency compiles to an embedded figure grid with a small lightbox; media and
article references are rewritten to emitted paths; headings feed a scroll-spy TOC. A publication
may override the layout with `templates/article.html` and `templates/index.html` — plain HTML
with a small `{{placeholder}}` contract; embedded defaults ship in the binary and nothing is
fetched.

Authority is conditional: when the destination repository builds its own pages, that build stays
authoritative and Tezuri only proves it (unchanged). Spaces without a build engine are exactly
where Tezuri renders. Emission is an idempotent write of current pages; v1 never deletes
unrecognized files in `render/`. The preview surface renders the same compiled artifact, so what
an author sees while writing is byte-identical to what emits.

This amends the reset-era "the target repository's build is authoritative — do not build a
competing renderer" invariant, which presumed every destination had a build. It replaces the
absolute with the conditional framing now recorded in the product brief.

## 2026-08-26 — Adopt the Sylin workbench dialect from Ghostlight

Tezuri's interface speaks the same visual grammar as Ghostlight's orchestrator window
(`sylin-org/browser-mcp` `crates/orchestrator/ui`): the night-garden token block adopted verbatim —
ground, five-step ink ramp, hairlines, panel washes, one accent carried as `--a`/`--al`/`--argb`,
mono for labels and facts, the 58px lamp band with a state word, and the capability colors
(read dim, action blue, write amber, execute violet). The accent is a signal, not a theme: the
window is neutral at rest and the accent brightens only while Tezuri writes, proves, or consults.

Tezuri's accent is a placeholder — moonlit leaf `#bef264` — until the deck entry and icon land;
Ghostlight wears teal, and the reskin's earlier amber is retired because the family reserves amber
for writes and held states. Landing, space view, assistant rail, and the deck card arrive as later
slices under this decision.

This supersedes the 2025-08-25 reskin's amber accent choice. It replaces no product behavior.

## 2026-08-26 — Keep space identity inside the publication

A publication's own characteristics — display name, byline, persona — will live in a small
`publication.yaml` inside the publication folder, following the `meta.yaml` pattern: Tezuri models
a few keys and preserves unknown ones verbatim. The registry keeps its entry names as a display
cache so the landing renders without opening each space; the yaml inside the publication is
canonical whenever present. The last-opened pointer stays application-side. Space detail editing
writes the file through the one atomic path and journals it. Implementation lands with the space-view slice; this entry fixes the
direction so no intermediate work invests in registry-held identity.

This upholds "no canonical content in application settings" for the coming landing and space views.

## 2026-08-25 — Make the Markdown file the article

An article is `articles/<slug>/article.md`: its first `# ` heading is the title, an optional
standalone `_…_` line after it is the standfirst, and everything after is body prose — one Markdown
flow. A deliberately tiny sibling `meta.yaml` carries the modeled metadata (`slug`, `state`
(draft/review/published), `date`, `tags`, `cover`, and a standfirst fallback). Unknown YAML fields
survive saves untouched: serde ignores them and writes rewrite only known keys.

Images live in the publication's content-addressed `media/` directory under
`{uuidv7}-{plug}.{ext}` identities with declared renditions derived on demand from the original.
The desk index and the per-article journal are derived state, rebuilt from these files whenever a
publication opens; no canonical record exists inside Tezuri's own settings.

This replaces the reset-era convention that made `article.json` canonical with generated Markdown.
That generator model was wiped with the earlier tree; its schema, repair-on-open logic, and import
conventions were dropped rather than ported.

## 2026-08-25 — Ship in gated steps; consult as bounded local jobs

Proof detects the destination repository's conventional build — a `package.json` build script via
an installed package manager, or Hugo given a standard configuration file — and runs it on a
disposable copy of the publication with a fixed timeout, capped captured output, a kill of the
direct child process on expiry (grandchildren are not reaped yet), and redacted evidence.
Publishing stays human-gated: review changed paths, select exact paths, commit only that selection,
then push only while the reviewed remote head still matches. Other changes already staged in the
index are refused rather than swept into the selection's commit. Saving never touches git.

Consult keeps agent help local and bounded. Recipes (`recipes/<name>.md`, plus five built-in verbs:
polish, align-to-voice, fact-check, suggest-tags, summarize-scratch) assemble the prompt; an
author-curated `assistants.md` catalog records how to invoke each assistant harness — how to run it,
never any credentials. Every job shells out through argv arrays only, bounded in time and output,
and produces verdict-first advisory output that enters documents solely through the ordinary accept
path.

This replaces the reset-era proof/publication/import entry, including the Substack preview/apply
flow, which has not been ported.

## 2026-08-14 — Keep native authority behind product commands

The bundled webview can invoke only named product operations. Rust owns native dialogs, canonical
publication roots, path confinement checks, atomic writes, media decoding, subprocess execution,
Git, and the publication registry. The one main window holds one open publication session; every
repository operation resolves through it.

The Tauri application enables only the native-dialog plugin. The webview bundle lives at
`src-tauri/ui`; frontend code invokes named commands directly, and there is deliberately no generic
frontend filesystem, shell, process, or network capability. The last-opened pointer joins the
registry as application state kept outside publications — it holds preferences only, never writing.

This replaces the undecided native command and security boundary at reset time. Amended 2026-08-26
for the rebuilt shell (bundle location, plugin set, session shape).

## 2026-08-14 — Use a domain-driven monolith

Tezuri is one application: a single `tezuri` library crate organized around the product's domains —
`spine`, `publications`, `articles`, `media`, `desk`, `consult`, and `ship` — plus two thin drivers
over it, a `tezuri` CLI binary and the `tezuri-desktop` Tauri shell.

Domain-driven means the product vocabulary and rules shape the code. It does not mean a crate per
domain, an interface for every operation, a command bus, dependency-injection machinery, or layers
that only forward calls. Shared path confinement and atomic-write code exists once, in `spine`,
and is used directly.

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
