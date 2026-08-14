# ADR 0015: The article entity is canonical and Markdown is generated

- Status: Accepted
- Date: 2026-08-14
- Supersedes: [ADR 0001](0001-repository-files-are-authoritative.md),
  [ADR 0002](0002-lossless-markdown-frontmatter-protocol.md),
  [ADR 0003](0003-rich-editor-boundary-and-milkdown-spike.md)

## Context

ADR 0001 made the Markdown file the authority and forbade an entity store. Everything expensive in
Tezuri followed from that one decision:

- a byte-range patch protocol with expected-bytes preconditions and SHA-256 bases;
- a frontmatter reader that locates YAML scalars by exact byte offset, deliberately unable to see
  shapes it does not fully understand;
- a per-article round-trip gate, because a rich editor that re-serialises a hard-wrapped document
  would turn a one-word change into a whole-file diff;
- a conflict experience for external edits, because any text editor was a peer writer.

That machinery exists to defend a property — *the file a person edits by hand is the truth* — and it
worked. But it bought that property at the cost of the product actually being pleasant to use. In
practice the round-trip gate fired on ordinary hard-wrapped Markdown and dropped the writer into a
textarea, which is the opposite of the intended experience.

The reframe that resolves it: **Tezuri is a craftsman's article maintainer and generator, not a
generic Markdown editor.** A generator owns its output. Nobody hand-edits `dist/`.

## Decision

The article is a Koan entity persisted as JSON. Markdown is a generated artifact.

```text
src/writing/<slug>/
  article.json     canonical entity, written by Koan's JSON connector
  index.md         generated on save; what the site build consumes
  media/           images owned by this article, moving with the folder
```

Storage uses the published `Sylin.Koan.Data.Connector.Json` `IndividualFiles` layout with
`IndividualFilePath` of `{id}/article.json`, so each article is one file. Aggregate layout is
rejected: with every article in one array file, committing one article necessarily commits every
other article's persisted changes, and Tezuri's publish flow depends on selecting exact paths.

**The flow is unidirectional: Tezuri writes the article, the article renders Markdown.** Markdown is
never read back as an input. An external edit to `index.md` is not a supported way to change an
article and will be replaced on the next save. This is the whole simplification: the file is an
output, so it cannot disagree with the entity.

Imported metadata Tezuri has no typed property for is preserved through a Json.NET extension-data
dictionary on the entity, so arbitrary keys from a Substack corpus survive a read/write cycle and are
written back as ordinary top-level JSON.

Koan does not enforce optimistic concurrency: the JSON connector does not implement
`IConditionalWriteRepository`, and `EntityController` does not enforce `If-Match`. Because the flow
is unidirectional, the only remaining writer contention is two Tezuri sessions on one article, which
a Tezuri-owned revision comparison immediately before the write handles. Reads, list, and delete use
`EntityController` directly; only the write path is Tezuri's.

## Consequences

- The byte-patch protocol, the frontmatter byte reader, the round-trip gate, and the conflict
  experience are all deleted, along with their tests. That is a large reduction in the code that
  carried the most risk.
- The rich editor is simply the editor. There is no fidelity gate, because the serializer's output
  *is* the artifact rather than something that must match a pre-existing file byte for byte.
- Hand-editing `index.md` while Tezuri is in use is lossy. The escape hatch becomes read-only for as
  long as Tezuri owns the article.
- The "content outlives Tezuri" promise survives in the form that matters: delete `article.json` and
  the Markdown stands alone as a complete, ordinary file that the site still builds.
- `src/writing/` gains a generated file beside its source. The target repository's convention treats
  `src/` as source and `dist/` as generated; committing `index.md` there is a deliberate exception,
  taken because the deployed build reads Markdown from Git and must keep working.
- Round-tripping is no longer a correctness property, so the preservation test suite is replaced by
  tests over generation and imported-metadata retention.

## Evidence

Verified on 2026-08-14 against the published packages, not a local checkout:

- `Sylin.Koan.App` 1.0.0 is on NuGet; Tezuri restores and builds against it with zero warnings.
- `Koan.Data.Connector.Json` 1.0.0 contains `JsonStorageLayout`, `IndividualFiles`,
  `IndividualFilePath`, and `JsonIndividualFilesRepository`.
- `Koan.Data.Connector.Json` 0.20.x stored an entire set as one array
  (`JArray.Parse(File.ReadAllText(path))`, records compact, whole-file rewrite on persist), which is
  what made the aggregate layout unusable here.
- The Koan handoff states plainly that generic `EntityController` writes can overwrite a change made
  after the client read the entity.

## Rejected alternatives

- **Keep files canonical and add a derived index.** Preserves hand-editability and readable prose
  diffs, but keeps every piece of byte-level machinery — the expensive half — while solving none of
  the experience problems.
- **Bidirectional sync, importing external Markdown edits back into the entity.** Technically
  possible via a path-based metadata dictionary and an import pass, but it reintroduces exactly the
  reconciliation and conflict surface this decision removes, to support an input the product does not
  need.
- **Aggregate JSON layout.** Rejected on commit granularity, as above.
- **A custom out-of-tree `KeyValueStore` for file-per-article.** Was the plan until Koan 1.0.0's
  `IndividualFiles` layout made it unnecessary. Koan confirms the seam is supported if a future
  layout need outgrows the stock connector.

## Revalidation triggers

Revisit if hand-editing Markdown outside Tezuri becomes a required workflow, if a second writer
(another person or tool) must edit articles concurrently, or if Koan gains enforced optimistic
concurrency that would let the write path return to `EntityController`.
