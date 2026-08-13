# ADR 0002: Use a lossless Markdown and frontmatter protocol

- Status: Accepted
- Date: 2026-08-13

## Context

Articles may contain metadata and Markdown constructs Tezuri does not understand. A conventional
parse-and-reserialize cycle would reorder YAML, normalize unrelated prose, or silently discard rich
blocks, making the editor unsafe for existing repositories.

## Decision

The canonical document is the original Markdown file, including YAML frontmatter. The document
codec will retain the original bytes and a source-aware representation. A no-op save writes nothing
and remains byte-identical. An edit applies the smallest source-span change practical, preserving
unknown keys, ordering, comments, formatting, and unsupported Markdown or HTML. Unsupported blocks
remain visible as protected source and produce an actionable compatibility notice.

Writes use an atomic write, flush, validate, and replace sequence. Saves compare the loaded source
identity with the current file so external edits become a reload or explicit conflict, never an
unnoticed overwrite.

## Consequences

- The codec needs golden byte-preservation and localized-diff tests.
- UI controls may edit known fields without owning the complete serialization format.
- Semantic validation occurs before replacement, while Git remains the durable recovery path.
- A fully normalized serializer is unsuitable for canonical writes.

## Evidence

The startup contract requires byte-identical no-op saves, localized paragraph edits, unknown-field
preservation, protected unsupported blocks, atomic writes, and external-change handling. The full
Kintsugi import corpus is the acceptance corpus in addition to compact synthetic fixtures.

## Rejected alternatives

- Re-emit all YAML and Markdown after each edit: unrelated text would churn.
- Preserve unknown content only in a sidecar: the Markdown would no longer stand alone.
- Silently flatten unsupported rich blocks: fidelity failures would be invisible.

## Revalidation triggers

Revisit the internal representation when corpus evidence finds a construct that cannot survive a
localized edit. The byte-identical no-op and no-silent-loss requirements remain fixed.

