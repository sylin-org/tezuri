# Tezuri contracts

Tezuri edits repository-native content without becoming its authority. These contracts define the
small interoperability surface needed to open that content safely, present it helpfully, migrate it
inspectably, prove the target build, and publish an exact reviewed change.

## Authority

The mounted repository remains authoritative for article Markdown, frontmatter, media, target build
configuration, and Git history. Tezuri-owned contracts are authoritative only for Tezuri
configuration, manifests, operation receipts, and the local HTTP wire format.

The public JSON Schemas in [`/schemas`](../../schemas/) are the normative serialized shapes. Domain
records implement those shapes. Golden examples and contract tests prevent drift between the two.
OpenAPI describes the HTTP binding; it does not create a second semantic definition.

## Contract catalog

| Contract | Discriminator or `$id` | Job |
| --- | --- | --- |
| Workspace | `tezuri.workspace/v1` | Grants repository-local paths, proof commands, media policy, and Git scope. |
| Article source | `tezuri.article-source` and related protocols | Transfers canonical bytes and guarded source patches. |
| Default article metadata | `urn:sylin:tezuri:article-metadata:v1` | Offers a deliberately thin default vocabulary; a target may replace or extend it. |
| Editor hints | `tezuri.editor-hints/v1` | Maps target-owned frontmatter fields to stable editor roles and controls. |
| Media receipt | `tezuri.media-asset-receipt` | Confirms a bounded owned-media ingest. |
| Media manifest | `tezuri.media-manifest/v1` | Records intrinsic asset facts and provenance without duplicating placement copy. |
| Import manifest | `tezuri.import-manifest/v1` | Proves source inventory, outcomes, transformations, assets, warnings, and fidelity. |
| Operation envelope | `tezuri.operation/v1` | Gives long-running work one state and progress vocabulary. |
| Site proof | `tezuri.site-proof-run` with version `1` | Records target commands, findings, artifacts, and the revision they proved. |
| Git publication | `tezuri.git-*` | Inspects, plans, commits, and pushes exact reviewed repository state. |
| Publication orchestration | `tezuri.publication-plan/v1` / `tezuri.publication-receipt/v1` | Binds proof and Git evidence into one reviewed workflow. |
| Problem details | `urn:sylin:tezuri:problem:v1` | Extends RFC 9457 with stable local error codes and correction context. |

[`ARTICLE-AND-MEDIA-PROFILE-V1.md`](ARTICLE-AND-MEDIA-PROFILE-V1.md) defines source-format and
editorial semantics. [`API-AND-COMPATIBILITY-V1.md`](API-AND-COMPATIBILITY-V1.md) defines wire
envelopes, errors, versioning, and evolution.

## Vocabulary boundaries

Three state families are intentionally distinct:

- `publicationState` describes the article lifecycle: `draft`, `scheduled`, `published`, or
  `archived`;
- `editorialCurrency` describes how a published article stands today: `timeless`, `current`,
  `of-its-time`, or `revised`;
- `operationState` describes work performed by Tezuri: `queued`, `running`, `awaiting-approval`,
  `succeeded`, `failed`, or `cancelled`.

A target may keep a different on-disk name. For example, a target `status` field can map to
`editorial-currency` through editor hints. Tezuri must not infer publication state from it.

## Conformance

Every checked-in example must validate against its named schema. Every domain record that implements
a public wire contract must serialize with web JSON naming and validate against the same schema.
Invalid examples cover containment, digest, required-field, enum, and unknown-property boundaries.

Schemas and examples contain synthetic redistributable data. Live Substack material belongs in the
explicit dogfood workflow and must not enter the ordinary test fixtures.
