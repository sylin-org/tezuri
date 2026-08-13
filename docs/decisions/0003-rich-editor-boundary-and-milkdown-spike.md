# ADR 0003: Keep the rich editor behind a document boundary

- Status: Proposed
- Date: 2026-08-13

## Context

Tezuri needs a constrained rich editor without allowing a browser editor's document model to become
the permanent content format. Milkdown is the provisional permissively licensed candidate and is
bundled directly through `@milkdown/kit`, but no full corpus spike has yet proved it against Tezuri's
preservation and accessibility contracts.

## Decision

Define an app-owned, versioned editor projection and explicit edit operations over the lossless
document codec from ADR 0002. The client editor may render supported structures and return bounded
operations; it may not replace the canonical document wholesale or become the serializer.

Run a Milkdown spike before selection. It passes only if it demonstrates:

- byte-identical open/save and a localized one-paragraph diff;
- protected round trips for unknown Markdown, HTML, and frontmatter;
- keyboard-complete editing, visible focus, labels, and usable screen-reader announcements;
- paste/drop hooks that hand media to Tezuri rather than embedding remote dependencies;
- bundled offline operation with acceptable license, dependency, and security posture; and
- clean recovery when the source file changes outside the editor.

If Milkdown fails, retain the boundary and evaluate another maintained permissive editor or a
smaller hybrid rich/source surface.

## Consequences

- Milkdown is a provisional bundled dependency, not yet an accepted rich-save path or fixed
  architecture choice.
- Editor replacement does not change repository or publication formats.
- Unsupported content has an explicit source-edit path instead of a destructive approximation.

## Evidence

The startup brief explicitly requires a permanent document-schema boundary and a spike before an
editor choice becomes accepted. The current client proves direct headless integration, bundled
offline rendering, responsive keyboard-aware shell behavior, and source/rich projection, while the
server proves byte-identical no-op and localized byte patches. Protected-node/full-corpus fidelity,
Firefox/screen-reader behavior, and rich-save round trips remain unproved.

## Rejected alternatives

- Choose Milkdown from reputation alone: it would turn an untested candidate into product truth.
- Let the editor emit the full Markdown document: this bypasses the lossless protocol.
- Build a universal block schema for V1: the Kintsugi vertical slice does not justify it.

## Revalidation triggers

Accept, revise, or reject this ADR after the spike runs against synthetic edge cases and the complete
dogfood corpus, with accessibility and dependency evidence recorded.
