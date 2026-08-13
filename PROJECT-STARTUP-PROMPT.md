# Tezuri project startup prompt

Use this prompt to start a fresh coding-agent session. It is an execution brief, not a request for
another proposal. Maintain a working plan, make reasonable reversible decisions, and continue until
the completion contract is met or a genuinely external permission prevents further progress.

## Mission

Build **Tezuri - the local press**: a production-quality, repository-native writing and publishing
application built with Koan and distributed as a public OCI container.

The complete outcome has three connected parts:

1. A delightful local browser application for writing, editing, organizing, previewing, validating,
   and publishing articles and their media.
2. A tested, versioned, multi-architecture container that anyone can pull anonymously and run by
   mounting a Git repository.
3. A real dogfood integration with sylin.org: import the complete public Kintsugi Architecture
   archive from Substack, preserve the source richness, write the canonical article folders into the
   local website repository, render every public artifact through the website's Eleventy build, and
   publish through the existing Git-to-Cloudflare path.

Do not call the project complete when the application merely runs in development. It is complete
when an anonymous user can pull the published image, mount a compatible repository, edit and preview
an article, and carry a tested publication change through Git; and when the real Kintsugi corpus has
successfully exercised the same path against sylin.org.

## Fixed identity and product language

- Product: **Tezuri**
- Tagline: **The local press.**
- Descriptor: **A repository-native writing and publishing room.**
- Core promise: **Mount your Git repository, write through a rich local editor, preview the real
  site, and publish with Git.**
- Container name: `ghcr.io/sylin-org/tezuri`
- Default container workspace: `/workspace`
- Default container port: `8080`, published on the host as a loopback-only port
- Default license: Apache-2.0, consistent with Koan, unless the owner explicitly changes it before
  the first release

Use `Tezuri`, not `Tezuri CMS`, in product surfaces. CMS is a useful category, but the differentiator
is that Tezuri does not become the sovereign home of the content.

## Local environment and repositories

The three local repositories have different authority. Keep their changes and commits separate.

### Tezuri - implementation repository

`F:\Replica\NAS\Files\repo\github\sylin-org\tezuri`

- This repository was created empty on `main` with remote
  `https://github.com/sylin-org/tezuri.git`.
- Build the application, tests, container, documentation, release workflow, and generic fixtures
  here.
- Create a concise `AGENTS.md` early and keep it synchronized with durable architecture and
  verification requirements.

### sylin.org - dogfood target and publication renderer

`F:\Files\repo\github\sylin-org\website`

- Read its `AGENTS.md` completely before changing anything, then read every document it requires,
  especially `README.md`, `RESPONSIBLE-DELIGHT.md`, `VOICE.md`,
  `PROJECT-PAGE-GUIDE.md`, and `WRITING-PUBLICATION-BRIEF.md`.
- The website is an Eleventy 3 static site. `src/` is source; `dist/` is generated and gitignored.
- `npm test` is the required clean build and generated-site gate.
- Cloudflare Pages builds from GitHub `main` and publishes `dist` at `https://sylin.org`.
- Preserve every existing carousel, project-page, Workbench, metadata, cache-revision, and
  progressive-enhancement requirement in its `AGENTS.md`.
- `WRITING-PUBLICATION-BRIEF.md` is currently an untracked owner artifact. Preserve it. It is the
  authoritative UX/editorial brief for the writing experience, but its flat-file authoring and
  implementation sections need to be reconciled with the approved Tezuri folder-native workflow.
  Do not silently delete or overwrite the original reasoning.
- Inspect Git status before every block. Preserve unrelated owner work and stage explicit paths.

### Koan - framework evidence, not a workspace to modify

`F:\Files\repo\github\sylin-org\koan-framework`

- Read its root agent guidance, current product-surface ledger, package documentation, closest
  maintained samples, and relevant tests before selecting Koan capabilities.
- Start with `samples/applications/DevPortal` for editorial workflow ideas and
  `samples/applications/SnapVault` for local media and progress behavior.
- The local Koan `dev` checkout contains substantial active owner work. Treat it as read-only
  evidence. Do not modify it, clean it, build Tezuri with project references into it, or depend on
  unpublished local artifacts.
- Consume exact, publicly available `Sylin.Koan.*` NuGet packages and commit the lockfile. As of
  2026-08-13, NuGet lists `Sylin.Koan.App` 0.20.7; reverify the compatible supported package set at
  implementation time and pin versions that restore in a clean networked build.
- Tezuri must build from its own clean clone without the Koan source repository being present.

## Empty-repository establishment contract

The Tezuri repository is empty. Establish it as a complete, legible public open-source project before
feature work obscures the foundation. This is part of the product deliverable, not cleanup for the
end. Every file must have an actual owner and job; do not cargo-cult badges, policies, or automation
that the project cannot honor.

### Root project baseline

Create and keep current:

- `README.md` - product promise, current maturity, exact supported boundary, screenshots only after
  they are real, five-minute PowerShell and POSIX first runs, Compose path, workspace contract,
  architecture overview, development/test commands, published-image verification, upgrade/removal,
  contribution/security links, and license. Lead with the useful local result, not the technology
  stack.
- `LICENSE` - Apache License 2.0 unless the owner changes the decision before first release. Add a
  `NOTICE` only when dependencies or project notices genuinely require it.
- `AGENTS.md` - canonical no-memory onboarding for coding/content agents: required reading, product
  invariants, repository map, build/test gates, container and dogfood verification, generated-file
  boundaries, security constraints, publication workflow, and current handoff. Keep transient task
  notes out of it.
- `CONTRIBUTING.md` - reproducible setup, supported toolchain, branch/commit expectations, tests by
  change type, ADR expectations, UI/accessibility review, issue/PR flow, and how to update fixtures
  without importing secrets or copyrighted third-party corpora.
- `SECURITY.md` - supported versions, private vulnerability-reporting route, expected response
  boundary, threat-model link, and explicit warning against publishing credentials or sensitive
  repository contents in issues. Resolve the actual Sylin security contact from the owner's current
  public contact source; do not invent one.
- `CODE_OF_CONDUCT.md` if the repository accepts community participation. Use a recognized current
  text and real enforcement contact; do not add a conduct policy with an unreachable address.
- `CHANGELOG.md` using Keep a Changelog shape and semantic versions. Do not fill it with imagined
  releases. Decide and document whether release notes are manually curated or automation-assisted.
- `SUPPORT.md` or an equivalent concise support section describing what belongs in discussions,
  issues, and private security reports. Avoid promising response times.
- `.editorconfig`, `.gitattributes`, and `.gitignore` covering .NET, the chosen frontend toolchain,
  IDEs, test/browser artifacts, local caches, imported scratch data, secrets, container output, and
  OS noise. Preserve fixture and lock files intentionally.
- `global.json`, `Directory.Build.props`, `Directory.Packages.props`, package lock files, frontend
  runtime declaration, and the selected frontend lockfile so clean builds use explicit compatible
  toolchain and dependency lines.
- `Tezuri.slnx` or the current supported .NET solution format, with clear application, domain,
  infrastructure/adapters, and test boundaries. Prefer the smallest structure that keeps the domain
  visible; do not create one project per noun.
- `.dockerignore`, production `Dockerfile`, `compose.yaml`, and `.env.example`. The example contains
  no real credentials and explains every variable. A Compose file is a convenience, not a second
  deployment architecture.

### Durable documentation map

Create a small navigable `docs/` tree with an index rather than loose design notes:

```text
docs/
  README.md
  product/
    PRODUCT-CONTRACT.md
    CONTENT-MODEL.md
  design/
    SYLIN-VISUAL-CONTRACT.md
  architecture/
    README.md
    WORKSPACE-AND-PUBLICATION.md
    THREAT-MODEL.md
  decisions/
    README.md
    ADR-TEMPLATE.md
    0001-....md
  operations/
    DEVELOPMENT.md
    TESTING.md
    RELEASE.md
    DOGFOOD-SYLIN-ORG.md
```

Adapt filenames if a simpler grouping is clearer, but preserve the jobs. Link the map from README
and AGENTS. Mark product truth, decision history, operator instructions, and transient evidence
distinctly so they cannot silently contradict each other.

Create accepted ADRs for decisions already fixed by this brief, and exploratory ADRs only after the
relevant spike. The initial decision set must cover at least:

1. repository files as authority and no article database;
2. Markdown/frontmatter canonical document and non-destructive round trips;
3. folder-per-article and owned-media contract;
4. local single-user/container/nonce security boundary;
5. editor library and permanent document-schema boundary;
6. target-site Proof and Eleventy-as-authoritative-renderer boundary;
7. Git commit/push and credential delegation;
8. Substack import completeness, fidelity, and fixture policy;
9. Sylin visual/semantic inheritance without a cross-repository runtime dependency;
10. OCI distribution, provenance, versioning, and supported-platform policy.

Each ADR records context, decision, consequences, evidence, rejected alternatives, and revalidation
triggers. Do not write retrospective fiction: if implementation evidence changes a proposed
decision, update its status and record the change.

### GitHub project surface

Create and validate:

- `.github/workflows/ci.yml`, release/container publication workflow, and any narrowly scoped
  reusable workflow justified by actual duplication;
- `.github/dependabot.yml` for NuGet, the chosen frontend package manager, GitHub Actions, and Docker,
  with a humane grouped cadence rather than update spam;
- pull-request template with change intent, evidence, tests, visual/accessibility review, security
  impact, and documentation/ADR prompts;
- issue forms for a reproducible defect and a bounded feature/problem proposal, plus config directing
  vulnerabilities to `SECURITY.md`; do not create ten empty labels or forms;
- `CODEOWNERS` after verifying the owner's actual GitHub handle and desired ownership paths;
- repository description, homepage, and a small accurate topic set after the first README exists;
- branch/ruleset protection appropriate to the current one-owner project: required green CI,
  protected release tags, no force pushes or branch deletion, without requiring unavailable
  reviewers;
- public GHCR package metadata and README linkage after the first image publishes.

Pin third-party GitHub Actions to immutable commit SHAs, with a readable version comment or update
mechanism. Grant each workflow the minimum permissions it needs. Forked pull requests must not gain
package, release, or repository-write credentials. Keep release publication triggered by an explicit
version tag/release, not by every unreviewed branch build.

Configure repository settings through an inspectable script or documented `gh` commands where
possible, then read them back to verify the result. External repository-setting mutations and the
first public release must remain within the owner's explicit authorization and current credentials;
if a one-time owner action is required, name that exact action and resume afterward.

### Repository-quality gates

- One command from the root runs formatting/linting, unit/integration tests, frontend tests/build,
  and relevant contract checks. A separate explicit command runs slower browser/container tests.
- `git diff --check`, Markdown/link checks, JSON/YAML validation, license/dependency notices, and
  secret scanning are part of CI at an appropriate cost.
- Use useful status badges only after the linked workflow/package is public and stable. Never add
  aspirational coverage, security, download, or compliance badges.
- Add a small architecture/dependency rule test if needed to keep filesystem authority, target
  adapters, and the UI from collapsing into each other. Do not adopt a heavy architecture-testing
  framework for appearances.
- Prove the contributor path from a clean clone on a supported host, not only from the creator's
  machine.

The first conventional commit should establish this honest foundation and pass its own available
checks. Follow with cohesive product slices; do not place the entire project in one initial commit
called `initial commit`.

## Governing product contract

These are decisions, not questions to reopen casually.

1. **The mounted repository is the source of truth.** Articles, metadata, media, editorial
   configuration, and publication state are ordinary files. Git supplies durable history,
   attribution, branches, review, and recovery.
2. **The container is disposable.** Removing the container, image, cache, or browser storage loses
   no saved article and does not make the site unbuildable. Any index or cache must be fully
   reconstructible from `/workspace`.
3. **No database is required.** Do not add SQLite, a JSON entity store, or another authoritative
   persistence system for articles. Incidental runtime state must be optional, disposable, and
   stored outside the mounted content paths.
4. **No Tezuri accounts, SSO, organizations, roles, tenancy, or hosted control plane.** Tezuri is a
   single-user local tool. Git hosting authentication is publication-target authentication, not a
   Tezuri identity system.
5. **Local editing remains useful offline.** Network access is used only for explicit operations
   such as import, remote Git operations, dependency restoration, and deployed-site verification.
   There is no telemetry and no silent network traffic.
6. **Publishing is explicit and human-controlled.** Autosave may save source files; it must never
   commit, push, merge, or publish.
7. **The site's own build is authoritative.** Tezuri provides an immediate editor preview, then runs
   the mounted repository's declared build for the proof. For sylin.org, Eleventy owns HTML, RSS,
   `/writing.md`, sitemap entries, JSON-LD, social metadata, and all public layout. Do not create a
   second Tezuri HTML renderer and do not commit `dist/`.
8. **The content format outlives Tezuri.** A person using a text editor and the repository's normal
   build commands can edit and publish after Tezuri disappears.
9. **Unknown content is preserved.** Tezuri may provide rich controls for configured metadata, but
   it must never drop or rewrite unknown frontmatter, unfamiliar Markdown, or unsupported rich
   blocks silently.
10. **The first release serves the real Kintsugi workflow.** Avoid a generic plugin marketplace,
    multi-site control plane, newsletter service, analytics product, AI writing suite, or general
    headless CMS architecture. Add a general abstraction only where the sylin.org vertical slice
    proves it and the abstraction does not make the first result worse.

## Recommended architecture

Use Koan as the local application and orchestration substrate, not as an excuse to turn content into
database entities.

- ASP.NET Core / Koan host targeting the framework's supported .NET 10 line.
- A client-side rich editor bundled into and served by the same application/container.
- App-owned services with narrow boundaries, such as `ArticleWorkspace`, `ArticleDocumentCodec`,
  `MediaWorkspace`, `SiteProofRunner`, `GitPublicationService`, and `SubstackImporter`.
- An in-memory, filesystem-watched article index rebuilt from the mount. If a disposable on-disk
  search cache is later justified, key it by workspace and schema hashes and prove deletion is safe.
- Background operations and progress streaming for imports, media work, site builds, and publishing.
  Do not require a durable job database. Git commits and explicit operation receipts are the durable
  publication record.
- Versioned workspace configuration committed in the mounted repository, preferably `tezuri.yaml`,
  with a JSON Schema for validation and editor support.
- Do not execute arbitrary browser-supplied shell text. Build/test commands come only from the
  trusted, mounted repository configuration and are displayed before first execution.

Record consequential decisions as short ADRs. At minimum, document the editor/document model,
Markdown round-trip strategy, workspace/configuration contract, media policy, Git credential
boundary, and container security model.

## Repository and article contract

Tezuri must support folder-per-article content with colocated media. Configure sylin.org initially as:

```text
src/writing/
  <slug>/
    index.md
    media/
      <owned original or source asset>
      <deterministic web derivative>
```

Use repository configuration rather than hard-coding those paths into the generic application.

The Markdown file carries YAML frontmatter and a semantic Markdown body. The configured schema must
be rich enough for the current Kintsugi brief and the complete imported Substack corpus. Support at
least:

- stable ID and slug;
- title and subtitle/description;
- author/byline;
- original publication date and substantive update date;
- draft/published state;
- tags, preserving all source tags, with existing-tag autocomplete and near-duplicate guidance;
- optional curated topics when a target site distinguishes navigation topics from source tags;
- sections/categories and series name, slug, and order where present;
- canonical URL;
- cover/social image with alt text, caption, credit, and rights/source metadata;
- inline media with alt text, caption, credit, link target, and source provenance;
- origin provider, publication, source URL, source post ID, original slug, original dates, source
  tags/section, import timestamp, fidelity state, and source-content checksum;
- Kintsugi-specific `status`, `standing`, `reviewed`, `project`, `abstract`, `bluesky`, and
  Standard.site document fields from `WRITING-PUBLICATION-BRIEF.md`;
- arbitrary additional frontmatter, preserved losslessly even when Tezuri has no dedicated control.

Prefer standard JSON Schema vocabulary for configured metadata fields, with a small Tezuri UI-hints
layer only where JSON Schema cannot describe a delightful editor. Implement the field shapes needed
by the dogfood site before attempting a universal schema-form system.

### Non-destructive editing invariants

- Opening and saving an article without edits produces a byte-identical file.
- Editing one paragraph produces a localized, reviewable diff rather than reserializing the entire
  document or reordering unrelated frontmatter.
- External filesystem edits are detected. If the editor is clean, reload safely; if it has unsaved
  work, show a three-way conflict experience and preserve both versions.
- Writes are atomic: write, flush, validate, and replace without a partially written canonical file.
- Unknown frontmatter keys retain their values and ordering.
- Unsupported Markdown/HTML blocks remain visible as protected source blocks and survive the
  round trip. They produce an actionable compatibility notice, never silent deletion.

Select a maintained, permissively licensed client editor after comparing its Markdown fidelity,
accessibility, extension boundary, bundle size, and ability to implement these invariants. The
permanent source contract must be Sylin-owned Markdown and metadata, never the private JSON format of
Tiptap, ProseMirror, Lexical, or another editor library.

The rich body vocabulary must cover the corpus rather than an imagined word processor: paragraphs,
H2/H3 headings, emphasis, strong text, links, ordered/unordered lists, blockquotes, code and fenced
code, horizontal rules, tables when present, footnotes when present, images/figures/captions, and a
safe representation for embeds or raw HTML that cannot yet be converted. Disallow scripts, inline
event handlers, and arbitrary presentational styling.

## Media contract

- Dragging, pasting, or selecting an image copies it into the article's own `media/` directory. A
  remote hotlink is never the default result.
- Preserve the best available source asset and generate deterministic, content-hashed web
  derivatives appropriate to the target configuration.
- Require meaningful alt text before publication unless the image is explicitly marked decorative.
  Offer caption, credit, source URL, and rights notes without forcing them when they are irrelevant.
- Detect duplicate media by content hash and avoid duplicate binaries.
- Enforce configurable byte, pixel, and derivative budgets with human-readable corrections.
- Never put credentials, private drafts, or unrelated files into generated media.
- Keep video, audio transcoding, cloud object storage, and a general DAM outside V1 unless the real
  Substack corpus proves one of them is necessary. Preserve external embeds/links and report the
  boundary rather than fabricating support.

## Sylin visual and semantic language

Tezuri must look, read, and behave like a working room in the same house as sylin.org. Do not make a
generic light-themed admin dashboard and apply amber afterward. Also do not copy the portfolio's
trading cards, mascot effects, or public-site navigation into a workstation where they have no job.
Carry the underlying grammar into an application-shaped experience.

The current visual sources of truth are:

- `F:\Files\repo\github\sylin-org\website\src\assets\site.css` for the night-garden tokens,
  typography, focus, buttons, hierarchy, and responsive foundation;
- `F:\Files\repo\github\sylin-org\website\src\assets\image-tools.css` and the Image Tools workspace
  for Sylin's existing full-width local-tool language;
- the website's live templates and components for semantic HTML and progressive enhancement;
- `RESPONSIBLE-DELIGHT.md` and `VOICE.md` for the meaning the visual system must carry;
- `WRITING-PUBLICATION-BRIEF.md` for the reading surface and article semantics.

Inspect the live site and current tree before designing; the tree and rendered experience outrank a
token list copied into this prompt. Create a concise, versioned Tezuri visual contract documenting
which Sylin primitives are reused, how they are adapted for a dense authoring workspace, and when the
source was last reconciled. Snapshot the small stable token set locally rather than introducing a
runtime or build dependency on the website repository. Token changes must be deliberate and tested,
not accidental drift.

### Visual foundation

- Use Sylin's single dusk/night-garden surface: near-black violet backgrounds, quiet layered panels,
  warm off-white text, restrained translucent borders, and amber as the primary light. The current
  foundational values include `#0f0e12` background, `#f4f4f5` principal ink, `#c9c9d1` secondary
  ink, `#a1a1aa` quiet ink, `#fbbf24` amber, and `#fcd34d` light amber; revalidate them from
  `site.css` before implementation.
- Use the existing system sans and monospace stacks. Do not add a webfont, font account, tracking
  request, or decorative type dependency. The authoritative rendered article preview uses the
  publication's scoped reading typography; the application chrome stays in Sylin's sans, and paths,
  branches, commits, commands, checksums, and machine facts use the mono stack.
- Preserve the existing type hierarchy: light, spacious page titles; small tracked uppercase
  eyebrows for context; compact, readable labels; quiet explanatory copy; and monospace only where
  it reveals structure. Avoid oversized dashboard numerals and gratuitous all-caps copy.
- Use 1px low-contrast dividers, 6-12px radii for ordinary controls/panels, and restrained shadows.
  Depth should explain containment or the active working surface. Avoid generic glassmorphism,
  neon gradients, card grids for everything, or ornamental shadows on every control.
- Use the shared content rhythm and responsive spacing rather than introducing a separate 4/8px
  design system that merely resembles the site. Dense editor controls may compress spacing, but the
  hierarchy must remain calm and legible.

### Color semantics

- Amber means primary action, active context, and a small amount of earned attention. It is not an
  all-purpose warning color and should not flood the editor.
- The ink scale carries information hierarchy before color does.
- Use the established blue focus treatment where application controls need a focus color distinct
  from selected/active amber; keep focus highly visible and consistent.
- Reserve green for a proved ready/success/local-running state and rose for a destructive or failed
  state. Never communicate state through color alone.
- If Tezuri later receives a project accent, express it through the site's `--a`, `--al`, and
  `--argb` convention. Do not introduce multiple arbitrary accent colors or let article tags become
  a rainbow.

### Component semantics

- Primary actions use the familiar filled amber button; secondary actions use a quiet bordered
  treatment; tertiary actions are honest text/quiet controls. Destructive actions are explicitly
  named, visually distinct, and never adjacent twins of the primary action.
- Tabs use a quiet label and a narrow active underline, following the Workbench rather than a row of
  filled pills. Pills are reserved for genuinely compact states or filters, not ordinary navigation.
- Panels have names and jobs. Prefer one strong editor canvas with supporting rails over a mosaic of
  equal cards. Empty states collapse unused machinery and lead directly to the first useful action.
- Use left-accent callouts for a boundary or explanation, monospace fact rows for evidence, and
  native disclosure/details behavior for optional proof depth. Do not hide required corrections in
  a tooltip or transient toast.
- A status must state a property and its evidence: `Saved to src/writing/...`, `Proof passed at
  14:32`, or `Remote moved by 2 commits`. Avoid vague badges such as `Healthy`, `Synced`, or `Error`
  without the fact and the next action.
- Success is quiet and durable. Keep the receipt in the interface; do not rely on a disappearing
  green toast for a commit or deployment result.
- Touch targets must reach at least 44px where controls are used on narrow/touch layouts, even when
  their visual treatment remains compact.

### Workspace shape

- Treat Image Tools as the closest existing application precedent: persistent identity and local
  state at the top, focused controls beneath, a bounded workspace, and a quiet closing status. Tezuri
  needs its own information architecture rather than copying the pixel-work layout literally.
- On a capable desktop, give article navigation, the editor, and metadata/proof appropriate stable
  regions so the author can understand the whole operation without modal hopping. The article text
  remains the visual center of gravity.
- On narrow or height-constrained screens, switch to an ordinary scrolling document with explicit
  region navigation. Do not squeeze three desktop rails into 390px or create horizontal page
  overflow.
- Source, editor, and Proof are views of the same file, not three competing documents. Their labels
  and transitions must reinforce that semantic identity.
- Rendered Proof must inherit the actual target site CSS and markup. Do not restyle it to resemble
  Tezuri, and do not restyle Tezuri's editing chrome to impersonate the public article.

### Motion and decoration

- The working room is still by default. Use short transitions only to clarify hover, selection,
  saving, panel changes, and progress. Honor `prefers-reduced-motion` completely.
- Do not bring the landing page's twinkling stars, card tilt, foil, mascot halo, or ambient animation
  into a writing session. Those belong to discovery and identity, not concentration.
- A small Tezuri mark inspired by hand printing or a baren may establish identity if it earns its
  space, but avoid ornamental Japanese pastiche. The name's meaning should inform the craft and
  deliberate publish gesture, not become a theme pack.

### Semantic and editorial consistency

- Apply `VOICE.md` to every label, empty state, validation message, onboarding sentence, and release
  document. Write plainly as one maker. State what the system does, what happened, and what the user
  can do next. Avoid SaaS, growth, enterprise, engagement, and conversion language.
- Responsibility and delight remain one craft: validation should feel like useful guidance;
  provenance should feel like confidence; and local ownership should be visible in paths, diffs,
  receipts, and recovery—not repeated as marketing copy.
- Lead each surface with the user's job and felt outcome. Put deeper architecture and evidence where
  it answers a real question. Never let governance vocabulary dominate the act of writing.
- Name unfinished support candidly as a boundary. Do not use warning theater, apology banners, or
  maturity language that makes a working pre-1.0 path sound defective.

### Visual verification gate

- Maintain representative screenshots for first run, populated article list, focused writing,
  metadata/tags, media import, Proof success, Proof failure, Git diff, and publication receipt.
- Review at 1440x900, 1280x720, a height-constrained desktop, and true 390x844. Require no document
  overflow at 390px, visible focus, usable 200% zoom, and equivalent reduced-motion behavior.
- Compare the implemented shell beside the live sylin.org Workbench and writing surface. The test is
  not pixel identity; it is whether typography, hierarchy, color meaning, interaction restraint,
  evidence, and voice unmistakably belong to the same system.
- Add visual regression coverage for the stable shell and core states, while keeping dynamic article
  content and platform font rasterization from creating a noisy gate.

## Delight and interaction requirements

Responsible Delight applies to the authoring room as much as to the public site. Favor relief,
clarity, and recoverability over dashboard density.

### First run

- One documented container command mounts the current repository and publishes the UI only on
  `127.0.0.1`.
- Tezuri detects the workspace configuration, Git branch/status, article count, available site
  commands, and any repairable problem. There is no signup or setup wizard when the repository is
  already ready.
- If configuration is absent, offer to create a small reviewed `tezuri.yaml`; never scatter hidden
  project state.
- Show the exact mounted path and a visible **Open as files** escape hatch or path/copy affordance.

### Writing room

- A calm article list supports title search, tags, state, series, and recent work without turning a
  small corpus into an analytics dashboard.
- The editor provides excellent keyboard behavior, Markdown shortcuts, undo/redo, paste cleanup,
  find, reliable autosave, and a visible last-saved state.
- Tags autocomplete from the corpus, show counts, preserve imported tags, and flag likely casing or
  spelling duplicates without silently merging them.
- Metadata controls explain their public consequence in plain language: for example, where a cover
  appears, what `standing` tells a reader, and which date reaches RSS.
- Pasting or dropping media immediately shows the owned local result and guides alt text at the
  moment context is freshest.
- Never make source access feel like an advanced or dangerous mode. The Markdown and frontmatter are
  the product's continuity promise.

### Proof

- Immediate preview is fast, but clearly named as an editor preview.
- **Proof** runs the repository's actual clean build in an isolated temporary copy so dependencies,
  `dist/`, and generated files do not pollute or lock the mounted checkout.
- For sylin.org, Proof presents the actual Eleventy page at desktop and true 390px widths, plus the
  full-text RSS item, social metadata/cover, and agent-readable entry when available.
- The proof report names broken links, remote media, unsupported import blocks, missing alt text,
  invalid tags/metadata, build failures, and unrelated dirty files with exact correction paths.

### Publish

- Before publication, show the exact source diff, the paths Tezuri will touch, the target branch and
  remote, the checks that passed, and the resulting public routes.
- Never include unrelated dirty files automatically.
- The primary action is deliberate and unambiguous. A decorative fake printing gesture must not
  obscure what will happen.
- After publication, show a receipt: commit SHA, branch/PR or push result, deployment URL/status,
  live verification result, and a safe correction/revert path.
- A failed build, conflict, rejected push, or failed deployment must preserve the article and make
  the next corrective action obvious.

### Accessibility and resilience

- Meet WCAG 2.2 AA for the implemented surface: semantic landmarks, labels, error association,
  focus order/visibility, keyboard completeness, contrast, reduced motion, and screen-reader status
  announcements.
- No writing, navigation, save, proof, or publication control depends on drag alone.
- Test Chromium and Firefox at minimum, desktop and 390px, with zoom and reduced motion.
- The container may be stopped at any time without corrupting a saved article.

## Git and publication boundary

Tezuri publishes locally through ordinary Git. It does not invent a hosted editorial workflow.

- Never change branches, reset, clean, rebase, force-push, or rewrite history in the user's mounted
  checkout automatically.
- Use an isolated temporary clone/worktree for authoritative build and publication preparation.
  Do not require a sibling host path to be mounted merely to create that isolation.
- Refuse to publish when the target remote moved unexpectedly. Fetch, explain the divergence, and
  preserve all work.
- Commit only explicit article, media, configuration, and target integration paths.
- Make publication idempotent: publishing the same content/configuration hash twice creates no
  second commit.
- Support a credential-free **prepare commit** path that always works and leaves the repository
  ready for the user's normal host-side `git push`.
- Also support an explicit **push** path without a Tezuri account. First prefer a narrowly delegated
  SSH agent/credential helper when the platform makes that safe. Provide an ephemeral secret-file or
  askpass fallback for automation. Never require mounting the whole home directory or `.ssh`, never
  store a Git credential in the repository/container layer/browser storage, and never print it.
- Prove push behavior in CI against a local bare Git remote. Dogfood the real GitHub remote with the
  owner's existing approved credential path.
- GitHub-specific PR creation may be an optional adapter. Ordinary commit and push must not require
  GitHub APIs.

## Local container security

The process inside the container must listen on the container interface so port mapping works; the
documented host mapping must bind it to `127.0.0.1`, not every interface.

- Run as a non-root user and ensure created files have usable host ownership. Document Linux UID/GID,
  macOS, and Windows Docker Desktop behavior.
- Mount only the selected repository at `/workspace`; never require the Docker socket or home
  directory.
- Canonicalize every path and reject traversal or symlink escapes outside the allowed workspace.
- Protect mutating requests against CSRF and DNS rebinding with strict Host/Origin policy and a
  random per-launch bootstrap/session nonce. This is local process protection, not an account
  system.
- Use a restrictive content security policy. Sanitize preview HTML. Never execute scripts imported
  from an article.
- Apply time, output, and resource bounds to repository commands and media operations.
- Redact secrets and potentially sensitive content from logs. Provide a diagnostics export that is
  safe to inspect before sharing.
- Do not run untrusted repositories' declared commands without displaying and explicitly trusting
  the configuration first.

## Mandatory Substack baseline and migration

The real Kintsugi Architecture publication is the acceptance corpus, not a hand-made toy fixture.

Primary public sources:

- Publication: `https://kintsugiarchitecture.substack.com`
- RSS: `https://kintsugiarchitecture.substack.com/feed`
- Archive: `https://kintsugiarchitecture.substack.com/archive`

Substack documents the `/feed` convention, but a feed may omit older, private, paid, or platform-only
material. Inventory both feed and archive. If the owner can provide a current Substack export, use it
as the highest-fidelity source and compare it with the public inventory. Do not depend permanently
on an undocumented Substack API.

### Completeness rule

At migration time, enumerate every public article in the publication. Import count must equal the
discovered public-article count minus a reviewed manifest of explicit exclusions. Do not hard-code
the current count.

The existing website brief establishes a minimum baseline of these three posts:

1. `Why Your Best Ideas Come in the Shower` - 2026-03-17
2. `Craft, or How You Could Care` - 2026-03-21
3. `MCP 2026-07-28: The Kid Looks Adorable in a Tuxedo` - 2026-08-03

If any is absent, truncated, or unenumerated, the import acceptance test fails. If additional public
articles exist, they are required too. Exclude Substack Notes, comments, chats, administrative pages,
and subscription chrome unless the inventory proves they are authored publication articles. Never
invent missing paid/private text; request the owner's export when public sources expose only a
teaser.

### Fidelity rule

Preserve all meaningful richness available today:

- exact title, subtitle, slug, author/byline, original publish date, and substantive update date;
- every tag, category/section, series relationship, and discoverable source identifier;
- cover/social image and all inline images/media;
- image alt text, captions, links, credits, and source information when present;
- headings, emphasis, lists, quotations, code, tables, footnotes, dividers, links, and embeds;
- canonical/source URL and a visible imported-origin record;
- any additional source metadata in a namespaced origin record so fidelity does not depend on the
  current Tezuri UI knowing every Substack feature.

Download every asset the articles display into its article folder. Remove Substack tracking
parameters, subscription widgets, recommendation chrome, analytics pixels, and platform navigation,
but record each transformation in the import report. Preserve meaningful embed targets. No published
article may depend on `substackcdn`, Substack image proxies, Imgur, DEV image proxies, or another
temporary hotlink.

For every imported article, produce an inspectable import manifest containing the source URL/ID,
source and normalized metadata, source body checksum, downloaded asset mapping/checksums,
transformations, warnings, and final local path. Re-running the import must be idempotent and must not
overwrite local editorial changes without a three-way review.

### Test-fixture policy

- Check compact synthetic Substack fixtures into Tezuri for deterministic parser/error/security
  coverage.
- Exercise the complete live/imported Kintsugi corpus as the dogfood acceptance suite in the website
  repository and retain its source articles there, where they belong.
- Do not make ordinary Tezuri CI depend on live Substack responses. Record metadata/checksums needed
  to prove the dogfood run, while keeping current network discovery as an explicit refresh test.
- Test a feed/archive disagreement, duplicate assets, unknown metadata, malformed markup, remote
  failure, interrupted import, paid teaser, and rerun after local edits.
- Prove that editor open/save preserves every imported article semantically and that no-op save is
  byte-identical.

The website brief also describes two older DEV articles. After the complete Substack baseline is
green, migrate those through the same import/provenance/media contract if doing so does not delay the
first public Tezuri vertical slice.

## sylin.org integration and output pipeline

Tezuri writes canonical source artifacts into the mounted website repository. Eleventy renders all
public deployables.

1. Add reviewed `tezuri.yaml` and any versioned metadata schema/UI hints to the website repository.
2. Reconcile `WRITING-PUBLICATION-BRIEF.md` with folder-per-article source and Tezuri authoring while
   preserving its information architecture, editorial states, typography, accessibility, RSS,
   Standard.site, Bluesky, agent discovery, migration, and verification decisions.
3. Implement the site's writing collection and templates against
   `src/writing/<slug>/index.md` plus colocated media. Absence must remain a no-op until the first
   published article exists.
4. Tezuri writes Markdown, frontmatter, owned media, deterministic derivatives when required, and an
   import manifest. It does not write site chrome or `dist/`.
5. Eleventy generates canonical article HTML, `/writing/`, conditional series/topic routes,
   `/writing/feed.xml` with full text and tags/categories, `/writing.md`, sitemap entries, JSON-LD,
   canonical/Open Graph data, and any Standard.site discovery files described by the brief.
6. Proof runs `npm test` in a clean isolated copy and serves the resulting `dist/` for inspection.
7. Publish commits source paths only, pushes through Git, and lets the existing Cloudflare Pages Git
   integration deploy.
8. Poll the public origin until its asset revision contains the exact commit SHA, then verify the
   changed routes, feed content type/full text, agent document content type, metadata, links, true
   390px width, no-JavaScript reading/navigation, and any relevant Standard.site behavior.

The website's existing acceptance matrix remains binding. Extend `scripts/check-site.js`; never
weaken it to make writing pass. At minimum, enforce every writing criterion already enumerated in
`WRITING-PUBLICATION-BRIEF.md`, plus:

- every imported source tag is either represented in canonical metadata or deliberately mapped in
  the import manifest;
- unknown metadata survives Tezuri round trips;
- no imported media hotlinks remain;
- article-folder media references resolve;
- opening and saving the full corpus without changes leaves Git clean;
- a one-paragraph edit produces a localized diff;
- generated RSS categories/tags agree with canonical article metadata;
- imported article count agrees with the reviewed discovery manifest.

Follow the website's established one-logical-block-per-conventional-commit workflow. Test each block,
push only after it is coherent, wait for the exact commit to deploy, and verify it live before the
next website block. Preserve the public site when Tezuri is stopped or removed.

## Test strategy and release gates

Create a layered suite that is fast locally and decisive in CI.

### Unit and contract tests

- YAML/frontmatter parsing and lossless preservation, including unknown fields and ordering.
- Markdown parse/serialize golden corpus and byte-identical no-op saves.
- Atomic writes, external-change detection, and three-way conflict behavior.
- Workspace discovery/config validation and path/symlink containment.
- Tag normalization/autocomplete without destructive merging.
- Media hashing, duplicate detection, deterministic naming/derivatives, and validation.
- Substack inventory, conversion, provenance, sanitization, manifest, and rerun behavior.
- Git status selection, idempotent commit planning, remote divergence, and credential redaction.
- Build-command allowlisting and operation state/progress/cancellation.

### Integration tests

- Mount fixture repositories with clean, dirty, conflicting, malformed, and missing configurations.
- Edit an article through the API and prove exact filesystem effects.
- Import fixtures including all supported rich structures and unsupported-block preservation.
- Run a declared static-site build in an isolated copy and serve its output.
- Commit and push to a local bare remote, retry, and prove no duplicate commit.
- Kill the process during save/import/build and prove canonical files remain valid.

### Browser tests

Use a real browser suite such as Playwright for first run, article discovery, rich editing, keyboard
flows, tags, paste/drop media, autosave, external conflict, proof, source diff, failed publication,
successful publication receipt, 390px layout, zoom, reduced motion, and accessibility smoke checks.
Include a no-network editing scenario.

### Container tests

- Build from a clean clone using only public dependencies.
- Start with a read/write fixture mount and verify health/readiness.
- Prove non-root operation and usable host file ownership.
- Prove the documented loopback mapping and bootstrap nonce.
- Prove `/workspace` containment, read-only-root compatibility where practical, graceful shutdown,
  and restart without content loss.
- Run the browser vertical slice against the built image, not only a development server.
- Test `linux/amd64` and `linux/arm64`; manually dogfood Windows Docker Desktop bind mounts.

### Dogfood tests

- Discover and import the complete live Kintsugi Substack archive.
- Review every import warning and every article visually and structurally.
- Exercise no-op and localized-diff invariants across the entire corpus.
- Run the actual sylin.org clean build and generated-site checks.
- Inspect desktop, true 390px, keyboard, no-JavaScript, reduced-motion, feed, social metadata, and
  agent document outputs.
- Publish at least one coherent website block through Git and verify the exact commit on the public
  origin before declaring the pipeline proven.

## Container, CI, and public distribution

- Provide a reproducible multi-stage `Dockerfile`, `.dockerignore`, health check, OCI labels, pinned
  base-image line, non-root runtime, and a small documented Compose example.
- Include only the runtimes required by the supported V1 path. If sylin.org Proof requires Node,
  include a deliberate pinned Node LTS/build-tool strategy or an equally simple isolated build
  runner; do not mutate host `node_modules` through the bind mount.
- Publish multi-platform images for `linux/amd64` and `linux/arm64`.
- GitHub Actions must restore, lint/format, build, test, run browser tests, build and smoke-test the
  image, and publish only after all gates pass.
- Version releases semantically. Publish immutable version and commit-SHA tags; move `latest` only
  for a verified stable release.
- Generate an SBOM and build provenance. Use keyless signing/attestation where GitHub Actions
  supports it without owner-held secrets.
- Add dependency and container vulnerability review with a documented severity policy; do not make
  an unactionable scanner dump the product gate.
- Publish to `ghcr.io/sylin-org/tezuri`. Ensure package visibility is public and verify an anonymous
  pull by digest. If GitHub requires a one-time owner visibility action, stop only at that precise
  external step, provide the exact instruction, then resume verification afterward.
- Document a copy/paste first run for PowerShell and POSIX shells, a Compose example, workspace
  configuration, file ownership, Git publication credentials, offline behavior, backup/recovery,
  upgrades, troubleshooting, and complete removal.

The release is not proven by a successful workflow alone. Pull the published digest into a clean
environment and repeat the core mounted-repository smoke journey.

## Suggested implementation sequence

Keep every block coherent, tested, conventionally committed, and independently useful.

1. **Foundation:** repository guidance, product README, ADRs, solution/app/test skeleton, pinned
   public Koan packages, minimal client shell, container development loop, and CI foundation.
2. **File-native vertical slice:** workspace config, article discovery, lossless metadata/Markdown,
   atomic save, article list, constrained rich editor, source view, and deterministic tests.
3. **Media and metadata richness:** paste/drop/import, owned media, alt/caption/credit, covers, tags,
   series/sections, arbitrary metadata preservation, and validation.
4. **Proof:** isolated repository copy, configured build runner, real rendered preview, desktop/390px
   modes, feed/social/agent previews, and actionable validation report.
5. **Substack import:** complete inventory, high-fidelity converter, assets, import manifest,
   idempotent refresh, synthetic CI fixtures, and full Kintsugi dogfood import.
6. **Git publication:** exact diff/path selection, local commit, safe optional push, divergence and
   retry handling, deployment receipt, and local bare-remote proof.
7. **sylin.org publication pipeline:** website config/schema, folder-native Eleventy collection,
   all deployables and gates, real corpus, then block-by-block live verification.
8. **Public release:** hardened multi-arch image, documentation, SBOM/provenance/signature, version
   tag, GHCR publication, anonymous pull, and clean-environment smoke test.

Do not defer all testing, accessibility, containerization, or dogfeeding to the end. Each slice must
exercise the built container and the mounted-file contract proportionately.

## Working discipline

- Inspect current files, history, remotes, and working-tree state before acting. The checked-out
  tree and live product surface outrank stale prose.
- Use the closest existing pattern and supported Koan surface; do not invent framework APIs.
- Keep Tezuri, website, and Koan worktrees distinct. Never stage or commit across repository
  boundaries accidentally.
- Do not edit generated `dist/`, dependency folders, or the dirty Koan checkout.
- Preserve user changes. Never use destructive Git cleanup or force publication.
- Prefer explicit paths and deterministic commands. Avoid secrets in command lines, logs, images,
  layers, committed configuration, or test artifacts.
- Keep an evidence log for dogfood inventory count, import warnings, test commands/results, image
  digest, release URL, website commit SHA, deployed verification, and remaining limitations.
- Update `AGENTS.md`, README, ADRs, and the website handoff when durable architecture or workflow
  changes.
- Report real maturity. Do not describe unsupported platforms, editors, media types, authentication
  mechanisms, or publication targets as shipped.

## Completion contract

The mission is complete only when all of the following are true:

1. Tezuri builds and tests from a clean clone using public, pinned Koan dependencies.
2. Articles and required metadata/media live only as ordinary mounted-repository files; deleting all
   Tezuri runtime state loses no saved or published content.
3. The rich editor is non-destructive, accessible, and proven against the complete imported corpus.
4. Tags and every other meaningful Substack metadata/media feature are preserved or explicitly
   reported and reviewed; nothing disappears silently.
5. The complete current public Kintsugi Architecture Substack archive is enumerated, imported,
   localized, manifested, reviewed, and represented in the sylin.org source tree.
6. The actual sylin.org Eleventy build generates canonical HTML, full-text RSS, discovery/agent
   documents, metadata, sitemap entries, and other approved deployables from those files.
7. Proof and publication are safe under dirty trees, conflicts, retries, failures, restarts, and
   remote divergence.
8. No CMS account, SSO, database, hosted control plane, telemetry, or runtime dependency is required.
9. The hardened multi-arch container is published publicly at GHCR with version, SHA, digest, SBOM,
   provenance/signing evidence, and clear run documentation.
10. An anonymous clean-environment pull of the published digest completes the mounted-repository
    smoke journey.
11. At least one real dogfood publication block reaches `https://sylin.org`, the exact Git commit is
    observed live, and the required desktop/mobile/no-JavaScript/feed checks pass on the origin.
12. Both repositories end with intentional commits, no unexplained generated or secret files, an
    accurate handoff, and candidly documented V1 boundaries.

Lead the final handoff with the runnable command, public image digest, Tezuri release/commit, website
commit and live routes, Substack discovered/imported/excluded counts, test evidence, and any manual
credential or platform constraint that remains.
