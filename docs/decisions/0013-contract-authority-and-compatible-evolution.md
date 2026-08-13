# ADR 0013: Public schemas own serialized contracts and evolve explicitly

- Status: Accepted
- Date: 2026-08-13

## Context

Tezuri already has a versioned workspace JSON Schema and app-owned C# wire records. Import, proof,
publication, media provenance, and editor semantics will be consumed by the browser, local API,
fixtures, dogfood scripts, and mounted repositories. Duplicated informal shapes would drift, while a
universal article model would compete with the target repository's content contract.

The first dogfood target also uses `status` for editorial currency, while Tezuri needs distinct
publication and operation states. The distinction must exist before those meanings spread through
the API and import corpus.

## Decision

JSON Schema Draft 2020-12 files in `/schemas` are normative for Tezuri-owned persisted and wire
formats. C# domain records implement them, OpenAPI 3.1.1 binds them to HTTP, and golden examples plus
contract tests detect drift. Stable discriminator strings and `$id` values are immutable within one
major version.

Tezuri owns only interoperability and operation contracts. Target `articles.metadataSchema` remains
authoritative for raw frontmatter. A thin optional default article schema and a separate editor-hints
schema provide a pleasant generic experience without renaming or owning target fields.

Publication state, editorial currency, and operation state are separate vocabularies. Editor hints
may map a target field such as `status` to editorial currency; Tezuri does not infer another state
from it.

Closed persisted contracts reject unknown properties. Additive response and problem extensions are
allowed only where their schemas are open and readers must ignore unknown properties. A breaking
shape or meaning change receives a new major discriminator and schema ID.

## Consequences

- Import and publication work cannot ship from ad hoc controller payloads.
- Schema, implementation record, fixtures, and tests change together.
- Old major schemas remain checked in while migrations are supported.
- Target sites can retain their own names, taxonomies, currentness semantics, and build behavior.
- Editors need a semantic-hints adapter rather than hard-coded frontmatter property names.
- Strict documents require an explicit version change for new capabilities after v1 stabilizes.

## Evidence

- `tezuri-workspace-v1.schema.json` already establishes Draft 2020-12 and a version discriminator.
- `ArticleSourceProtocolV1.cs` already establishes app-owned protocol names and versions.
- ADRs 0001, 0002, 0006, 0007, and 0010 require repository authority, lossless source, target proof,
  explicit Git work, and inspectable import evidence respectively.
- JSON Schema Draft 2020-12, YAML 1.2.2, CommonMark 0.31.2, OpenAPI 3.1.1, and RFC 9457 provide the
  external format and transport baselines.

## Rejected alternatives

- C# records alone: mounted repositories and non-.NET clients could not validate durable artifacts.
- OpenAPI as the only authority: persisted manifests and configuration are not merely HTTP payloads.
- One universal article schema: it would either reject real target metadata or become meaningless.
- Reuse `status` everywhere: it conflates editorial judgment, publication lifecycle, and operation
  progress.
- Accept every unknown configuration field: misspelled or unsupported capabilities could appear to
  work while being ignored.

## Revalidation triggers

Revisit if a standards-based content vocabulary covers the real imported corpus without weakening
target authority, if schema generation can prove semantically equivalent output without manual
schemas, or if multiple stable external clients require a different compatibility cadence.
