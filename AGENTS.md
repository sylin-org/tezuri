# Tezuri agent guide

This file is the durable, no-memory entry point for coding and content agents. Read it completely at
the start of a new session; do not use it for transient task notes.

## Required reading

1. [`README.md`](README.md) for the honest supported boundary and commands.
2. [`docs/product/PRODUCT-CONTRACT.md`](docs/product/PRODUCT-CONTRACT.md) for product invariants.
3. [`docs/architecture/README.md`](docs/architecture/README.md) and
   [`docs/architecture/THREAT-MODEL.md`](docs/architecture/THREAT-MODEL.md).
4. [`docs/decisions/README.md`](docs/decisions/README.md) plus ADRs relevant to the change.
5. [`docs/operations/TESTING.md`](docs/operations/TESTING.md) and the nearest existing code/test.
6. [`docs/MEMORY.md`](docs/MEMORY.md) for standing preferences, durable learnings, and the index of
   which document owns which subject.

Working notes that must not be committed — environment paths, credential *locations*, session
handoffs — live in the gitignored `local/NOTES.md`; see [`local/README.md`](local/README.md).

Repository-local instructions closer to a changed file override this guide. At present there are no
nested `AGENTS.md` files.

## Non-negotiable product invariants

- `/workspace` repository files are authoritative; do not introduce an article database.
- Markdown/frontmatter and owned media must remain useful without Tezuri.
- No-op article saves are byte-identical. Local edits use byte-ranged preconditions and external
  changes conflict; unknown syntax and metadata are preserved.
- The container is disposable. Never put canonical content in browser, database, or image state.
- Publishing is explicit. Saving must never commit, push, switch branches, rewrite history, or
  publish.
- The target repository's declared build is authoritative. Do not build a competing HTML renderer.
- Tezuri is local, single-user software: no accounts, SSO, roles, tenancy, telemetry, or silent
  network work.
- Mutations require loopback Host/Origin validation and the per-process nonce. Do not weaken path,
  symlink, CSP, command, log-redaction, or credential boundaries.
- Browser input never supplies executable shell text. Only reviewed `tezuri.yaml` executable and
  argument arrays may reach a bounded process runner.
- Do not mount or request the Docker socket, a whole home directory, or `.ssh`.

## Repository map and ownership

- `src/Tezuri.Domain`: app-owned wire/domain contracts; no filesystem, HTTP, Koan, or editor types.
- `src/Tezuri.Infrastructure`: filesystem, process, Git, import, and target adapters.
- `src/Tezuri.App`: composition, controllers, security, and bundled `ClientApp`.
- `schemas`: versioned public contracts. Schema changes require compatibility evidence and an ADR.
- `samples`: synthetic, redistributable fixtures only—never copied private or live corpora.
- `tests`: keep layer-focused tests close to the owning boundary.
- `docs/decisions`: history; never rewrite an accepted decision as if it always said something else.
- `docs/evidence`: dated, inspectable dogfood/release evidence—not product truth.

Generated boundaries: never hand-edit `bin/`, `obj/`, `node_modules/`, `dist/`, `wwwroot/`, coverage,
browser reports, or imported scratch data. Commit every `packages.lock.json`, `koan.lock.json`, npm
lockfile, schema, and intentional fixture.

## Change workflow

1. Inspect status, instructions, relevant ADRs, current types/constants, and the closest pattern.
2. State the exact files/layers and risks before production edits.
3. Preserve unrelated or owner-authored working-tree changes.
4. Implement the smallest coherent slice. Do not duplicate protocol strings or editor-native state
   across the permanent API.
5. Run focused tests, then `pwsh ./eng/verify.ps1` (or `./eng/verify.sh`). Run
   `pwsh ./eng/container-smoke.ps1` for host/runtime/container/security changes.
6. UI changes require desktop and true 390px review, keyboard flow, reduced-motion, zoom, and a
   no-console-error check. Record screenshots only after they represent real behavior.
7. Update affected product/operations docs and add or supersede an ADR for consequential choices.

Never use destructive Git cleanup, automatic branch changes, broad staging, history rewrites, or
force pushes. Each commit is one logical conventional-commit block and includes only intentional
paths.

## Dogfood and publication

The complete current public Kintsugi Architecture Substack corpus is the import acceptance set;
synthetic fixtures are the ordinary CI set. Live inventory/import belongs in explicit dogfood work,
with manifests, checksums, reviewed exclusions, transformation warnings, and no hotlinked media.

Website changes occur only in the distinct `sylin-org/website` checkout. Preserve its owner files and
one-logical-block commit discipline. Run its clean Eleventy checks, publish only with explicit owner
authority, observe the exact commit live, and verify desktop/390px/no-JavaScript/feed/discovery
outputs. Never modify the dirty Koan checkout; it is read-only framework evidence.

Releases require a verified tag, green gates, multi-platform image, SBOM/provenance, public GHCR
visibility, anonymous digest pull, and clean-environment smoke. Repository-setting mutations and
the first public release require the owner's explicit go-ahead.

## Current handoff

Tezuri is pre-release. The repository foundation, locked .NET/Node lines, `AddKoan()` host,
folder-native configuration, byte-preserving source envelope/patch API, local request boundary,
client shell, container/CI definitions, and initial tests exist. Rich-edit fidelity, full media,
Proof, import, Git publication, sylin.org migration, live dogfood, and public release remain gates;
consult `CHANGELOG.md`, current tests, and `git status` rather than assuming they shipped.

The v1 contract catalog now separates publication state, editorial currency, and operation state;
defines article/media, editor hints, import, operation, proof, Git, publication-orchestration, and
problem schemas; and validates golden examples plus existing domain serialization in
`Tezuri.Contracts.Tests`. Keep target-owned metadata authoritative and change schema, record,
fixtures, tests, and ADR evidence together.
