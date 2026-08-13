# API and compatibility profile v1

This profile defines how Tezuri's public contracts evolve and how local operations report progress,
failure, proof, and publication.

## Serialized contract authority

JSON Schema Draft 2020-12 documents under `/schemas` are normative for Tezuri-owned persisted and
wire shapes. Each immutable schema `$id` names exactly one major contract. C# records implement those
shapes and serialize with `System.Text.Json` web defaults. Contract tests validate representative
serialized records and golden files against the public schemas.

OpenAPI 3.1.1 describes HTTP paths, methods, status codes, content types, and schema bindings. It is a
generated or checked artifact, not a second place to redefine field meaning.

## Names and versions

Persisted documents use a `schema` discriminator such as `tezuri.import-manifest/v1`. HTTP wire
envelopes use `protocol` plus integer `version` where an existing protocol already follows
that convention. Discriminator strings and `$id` values are constants beside their owning types.

A v1 schema is immutable after the first stable Tezuri release. Before that release, corrections are
allowed only with an ADR, updated examples, and compatibility tests. After release:

- adding or removing a required property is breaking;
- changing meaning, type, format, constraints, or a closed enum is breaking;
- renaming a property or discriminator is breaking;
- adding a property to a closed persisted document is breaking for old readers;
- adding an optional response property is compatible only where readers are required to ignore
  unknown properties;
- correcting prose without changing observable meaning is compatible.

Breaking changes receive a new major discriminator and `$id`; the old schema and fixtures remain
available for migration tests. A consumer must never guess a newer major version.

## Open and closed documents

Configuration, plans, manifests, and receipts are closed unless a schema explicitly says otherwise.
This catches misspellings and prevents a repository file from silently requesting a capability that
an older Tezuri does not understand.

Response envelopes and RFC 9457 problem documents are open to additive extension fields. Clients
must ignore unknown response properties while preserving the understood operation identity and
state. Enum additions are still breaking because old clients cannot safely infer their meaning.

Target-owned article metadata is open in the optional default schema. The workspace-selected target
schema decides whether its own document is open or closed.

## Operations

Import, proof, and publication use one operation vocabulary:

`queued -> running -> awaiting-approval -> running -> succeeded`

An operation may instead reach `failed` or `cancelled`. Terminal states do not change. Retrying a
mutation with the same idempotency key and identical input returns the existing operation or receipt;
reusing the key with different input is a conflict.

Progress is advisory and monotonic within one operation attempt. A missing total means the work is
indeterminate, not zero. Logs and human messages are evidence, not machine state; clients switch on
stable state and code values.

## Problems and conflicts

Errors use `application/problem+json` and RFC 9457 fields. Tezuri extensions add:

- `code`: a stable kebab-case programmatic reason;
- `operationId`: the related long-running operation when present;
- `currentHash`: the repository's current source digest for optimistic concurrency conflicts;
- `fieldErrors`: JSON Pointer, stable code, and corrective message for invalid input;
- `retryable`: whether identical input may reasonably succeed without human correction.

Problem `detail` is safe local guidance. It must not contain credentials, launch nonces, complete
environment values, remote URLs with secrets, or unrestricted command output.

## Import evidence

An import manifest inventories every discovered source article. Each inventory member has one
disposition: imported, skipped, failed, or review-required. A skipped item requires a reviewed
exclusion or a stable reason. Each imported asset records source and destination mappings, digest,
transformations, warnings, and fidelity.

Reruns identify source items by source kind plus stable source ID, falling back to canonical source
URL only when necessary. They compare prior source/result digests and never overwrite locally edited
output without a reviewable conflict.

## Proof evidence

A proof receipt records the exact workspace revision and source-set digest, declared commands,
bounded logs, findings, and artifacts. Passing means all required target commands completed and no
error finding remains. It does not mean the public origin has deployed the revision.

Findings have stable codes and optional repository path, route, artifact, evidence, and correction.
Artifacts have a kind, repository or proof-output path, media type where applicable, size, and digest.

## Publication evidence

Publication is two documents:

1. A plan freezes the base commit, optional observed remote tip, branch, exact allowed paths, source
   and proof digests, commit message, push intent, and idempotency key for review.
2. A receipt records the executed plan, commit SHA, push outcome, optional deployed-origin
   verification, and any correction needed.

A stale base commit, changed selected path, changed proof digest, changed remote tip, or failed proof
invalidates execution. Saving an article cannot create either document implicitly.
