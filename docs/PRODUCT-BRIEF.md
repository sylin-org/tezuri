# Tezuri — concept brief

Grounded in prior-art research and user-satisfaction signals from existing tools in this space —
desktop CMSes, git-backed CMSes, local Markdown editors, hosted platforms, and writer communities
(HN, Reddit).

---

## 1. What Tezuri is

**Tezuri is a desk for an author's entire publishing life.** A small, elegant, fully featured local
application where a person's publications live as plain files, and the tool is the craft around
them — never the keeper of them.

An author may run several **publications**: different personas, voices, audiences, destinations.
Each publication is a folder (usually a git repository) carrying articles, media, and support files
(`voice.md`, `tone.md`, recipes). Tezuri indexes them, helps write them, and carries them through a
pipeline to published — with human gates where damage is irreversible.

One sentence per direction:

- To a writer: *your publications, personas, and process — in plain files, with a colleague who
  knows your voice and a press that never touches what you didn't approve.*
- Against incumbents: Typora that publishes; Decap without the server; Publii without the walled
  garden; Ghost without hostage-taking; Obsidian for people who finish things.

## 2. Why this product (research grounding)

Writers on static sites today choose between bad options, and community research shows the same
pains repeatedly:

- **Setup ceremony fatigue** — SSGs are "ridiculously over-engineered"; hours lost to simple things.
- **Images are mechanical misery** — copy files, guess paths, no transforms, no dedup.
- **Frontmatter hand-typing drifts**; no metadata UI without a CMS server.
- **No body of work** — directory listings don't show drafts, staleness, or momentum.
- **Publishing pipelines are fragile** — CORS proxies, OAuth servers, deploy hooks.
- **Hosted platforms won on feel**, then took ownership: writers describe escaping "algorithmic
  feeds where I had to perform," and prize "knowing you own a space nobody can shut down."
- **Dreamweaver trauma** — rich editors that emit garbage HTML destroyed trust for a generation;
  any WYSIWYG must be trustworthy by construction.

Pleasures to amplify: speed-to-update of local tools, ownership reassurance, plain-text
portability, calm single-purpose apps.

The strategic moat is not features — it is **removing friction between having a thought and it being
safely, permanently published**.

## 3. Object model — everything is a file

```text
author
└── publication (persona + destination + conventions)
    ├── articles/          # frontmatter + Markdown body — the source of truth
    ├── media/             # content-addressed images; renditions declared, derived
    ├── voice.md           # style card — read by humans AND agents
    ├── tone.md            # further support files as the author maintains them
    ├── recipes/           # per-publication agent verbs
    └── scratch/           # parking lot, research notes, agent transcripts

desk      # local index — NOT canonical; rebuildable from files at any time
journal   # per-article action log: what the app did, when, why
```

Two rules make it cohere:

1. **Files are truth; the desk is a lens.** Indexes, journals, transcripts are derived caches.
   Delete the application, lose nothing but preferences. Every clever feature must express itself
   as files, or it doesn't ship.
2. **One grammar of change.** Every mutation — human edit, accepted suggestion, rendition, import,
   publish commit — flows through one path: atomic write + journal entry. Receipts, review-before-
   apply, and rehearsal mode are all the same mechanism (**propose → show → accept**) pointed at
   different objects.

## 4. Three verbs

The whole interface is the author performing three verbs on the desk:

- **WRITE** — a document-first editor over live-rendered Markdown; directives render natively;
  `[[` links into your own corpus; parking lot one keystroke away; landing pad always open.
- **CONSULT** — agent collaboration as named verbs (polish, align-to-voice, fact-check, resurface),
  a persistent collaborator per article, costs visible, everything advisory until accepted.
- **SHIP** — proof against the site's own build, review scaled to stakes, persona checkpoint,
  an earned completion moment, opt-in post-publish hooks.

## 5. Editor space — one document, many projections

Source view, rendered preview, and reader-eye rendering are **projections of one canonical buffer**
(the Markdown file), linked by a source map that maps every rendered element to its source range.
This single mechanism powers click-to-edit, scroll sync, reader-eye mode, assist-diff anchoring,
and journal receipts.

Layout: progressive columns — document alone (default), document + preview, plus assistant as a
peer pane when consult is active. Focus states let any pane become sovereign:

- **Maximizer views:** "just show me the preview" — everything else dematerializes into thin
  slide-out affordances and one unmoving *restore work mode* control (`Esc`).
- **Reading maximizer** (preview full-bleed; doubles as proof-reading surface), **writing
  maximizer** (document alone), **flow maximizer** (document + collaborator transcript).
- Modes remember themselves per publication and per mode — no layout configuration surface.
- Signature interaction: click an element in the preview → land editing it in source → watch the
  change flow back. Unmodeled content renders locked in both directions; clicking opens an
  inspector card, never in-place rich editing.

Editor grammar: Typora-model live rendering over CommonMark + GFM tables, with a **closed,
finite syntax budget** — images with declarative transforms, gallery-by-adjacency, and four to six
block directives (note/warning, compare, embed placeholder, columns). Everything else renders as a
visible, copyable, locked source block. Trustworthiness by construction answers Dreamweaver trauma.

### Media and Markdown addons

Markdown-native, HTML-free, degrading gracefully in any foreign renderer:

```markdown
![alt](media/hash.webp?w=1200&format=avif&q=85)   <!-- declared rendition intent -->

![one](a.jpg)
![two](b.jpg)
![three](c.jpg)        <!-- adjacent images collapse into a gallery grid -->

:::note
Callout content
:::

:::compare before="a.jpg" after="b.jpg"
Interactive before/after slider
:::
```

Transforms declare *what*, not *how*: the tool derives renditions at build/save time, deduped by
content hash, EXIF stripped, correct relative links, alt text prompted non-blockingly.

## 6. Consult — agentic help

Agent harnesses (Claude Code, Codex CLI, Gemini CLI, OpenCode) have converged on headless print
modes, making "agent as bounded local subprocess" cheap to integrate. The repository itself is the
agent's context directory — no RAG machinery; hand the harness a cwd and let it read.

- **Recipes as per-publication artifacts:** prompt templates assembled automatically from selection,
  frontmatter, voice/tone cards, and the most similar published posts. Users invoke verbs; they
  never write prompts.
- **Persistent collaborator per article:** a live session kept across iterations — tighten the
  intro, warmer, re-check against voice.md after each revision.
- **Research flows legally:** sources land as cited scratch notes; quotes move into the draft with
  attribution intact.
- **Pipeline checks at review time:** voice alignment with diffs, broken links, unsupported claims,
  alt-text coverage, stale statistics. Verdict first, evidence on demand, advisory only.
- **Mechanics:** shell-out via argv arrays only; ride existing harness auth; never store keys
  (BYO-key direct API as fallback); bounded time and output; cancellable; cost shown before long
  jobs and after every run; results arrive as diffs against source ranges and enter the document
  only through propose → show → accept.

Positioning discipline: the assistant is a quiet capability of an otherwise local tool — "your
files, your words, optionally your models." It must never be perceived as the reason the app exists.

## 7. Ship — the pipeline

```
write → save (files)
      → [assist pass: voice / links / facts — advisory]
      → proof (the site's own build on a disposable copy; bounded, redacted)
      → review diff (scaled to stakes: minor-edit fast path vs full gates)
      → persona checkpoint → commit → push
      → post-publish hooks (announcements, cross-posts — each individually opt-in)
```

Automagic ≠ autonomous: the conveyor belt has human gates exactly where damage is irreversible.
Saving touches nothing but files. Publishing feels like something: a quiet completion moment — the
diff collapses, state flips with one restrained animation, link to the live page. Ceremony earned,
never repeated naggingly.

## 8. Delight system

Three levers score every feature: **friction down, opacity down, ritual up.** A proposal scoring
zero on all three is bloat.

Landed delights:

- **Landing pad** — drop anything on the window: image → processed media + link; PDF/URL → cited
  source note; docx/html → draft article import.
- **Parking lot** — zero-pressure capture per publication; half-thoughts beside real drafts,
  promotable in one keystroke. Attacks activation energy, the actual enemy.
- **Corpus intelligence** — link graph across articles: orphans, hubs, series candidates,
  internal-link suggestions while writing, anniversary/resurfacing agent ("three years old, cites
  a 2022 benchmark").
- **Momentum without gamification cringe** — honest signals: days written, draft vs typical length.
- **Receipts** — a per-article journal answering "what did this app ever do to my files?"
- **Show the money** — token/cost visibility inline on every consult job.
- **Rehearsal mode** — full pipeline dry-run against throwaway copies; first real publish isn't the
  first time the machine ran.
- **Session resume as ritual** — cursor, transcripts, verdicts restored before they're asked for.
- **Minor-edit path** — typo fixes skip ceremony; content changes keep gates. Process scaled to stakes.
- **Reader-eye preview** — faithful typography at phone/desktop widths, reading time; closes the
  writing-blind gap without competing with the site's build.
- **Exit interview** — "leave Tezuri": exports registry, recipes, voice cards flattened back into
  their repos plus a plain-text map of conventions. Making departure easy is the ultimate ownership
  statement.
- **Persona firewall as visible checkpoint** — "publishing under handle Y: voice loaded, real name
  absent" turned into a green moment rather than background safety.

## 9. Constitution

Testable rules, enforced per feature:

1. Expressed-as-files or it doesn't exist.
2. Propose → show → accept is the only way anything changes.
3. One write path: atomic writes + journal entries, always.
4. Bounded, cancellable, redacted — every subprocess.
5. Refusals designed like features: what happened, why, what to do — in the user's terms.
6. User input never becomes executable shell text; argv arrays forever.
7. Closed syntax budget: the directive vocabulary is finite and auditable; growth is deliberate.
8. Writes stay inside the publication; credentials delegated to existing tooling, never stored.
9. Keyboard-first, focus always visible, motion respects the system preference.
10. Three levers only: friction down, opacity down, ritual up.

## 10. Non-goals

- Collaboration, comments, presence, multi-user anything.
- Hosting, deploying, analytics dashboards.
- A site generator, theme system, or competing renderer — the destination repo's build is authority.
- Plugin ecosystems and extension points ahead of demonstrated pressure.
- Mobile/web versions; accounts; telemetry of any kind.
- An open-ended rich-content model. The grammar stays closed on purpose.

## 11. Build order

1. **Spine** — file model, atomic writes + journal, session resume, render pipeline + source map.
2. **Editor + media** — live rendering, directives, transforms, gallery-by-adjacency.
3. **Ship** — proof, staged review, persona checkpoint, completion moment.
4. **Desk** — index, search, states, link graph, momentum.
5. **Consult tier 1** — five recipes via harness shells, advisory diffs, cost meter.
6. Small delights — landing pad, parking lot, minor-edit path.
7. Depth — persistent collaborator, anniversary agent, rehearsal mode, exit interview.
8. Multi-publication polish — persona switching and the firewall.

Steps 1–3 are a shippable v0 that beats every incumbent at its own game; 4–6 make it beloved;
7–8 make it singular.

## 12. Done looks like

A person downloads one file, points it at their writing folder, and within a minute is drafting
with images, their voice card loaded, their other posts one `[[` away. When finished, they see the
article as readers will, accept the voice check's suggestions hunk by hunk, watch their own build
pass, approve the diff, commit, push — and feel that quiet, earned click of having shipped. If they
ever stop using Tezuri, every word, image, recipe, and voice card is still sitting in plain files,
exactly as useful as the day it was written.
