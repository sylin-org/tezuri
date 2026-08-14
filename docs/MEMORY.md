# Project memory

Durable, model-agnostic memory for anyone working on Tezuri — human or agent. Standing preferences
and learnings that the code, git history, and decision records do not already carry, plus an index
of which document owns which subject.

It **points at** decision and state documents; it never duplicates them.

Sensitive and session-scoped working notes live in [`local/NOTES.md`](../local/NOTES.md), which is
gitignored. See [`local/README.md`](../local/README.md).

## Standing preferences

- **Exercise the real artifact.** Run the published executable, not a convenience wrapper. This was
  once "use Docker, never `dotnet run`"; the container is gone and the rule survives it, because the
  point was always to test what ships. `--server` exists for hosts without a desktop.
- **Least meaningful moving parts.** One deployable, organised by domain concept rather than
  technical layer. Prefer convention over configuration; prefer deleting a mechanism over
  documenting it.
- **Ceremony is not quality.** This project front-loaded a full open-source establishment contract —
  15 ADRs, 13 schemas, 27 golden samples, 8 root policies — around a prototype that could not create
  an article. Governance follows a working product, not the other way around.

## Durable learnings

- **The expensive complexity came from one product decision, not from the stack.** Treating the
  Markdown file as authoritative forced a byte-patch protocol, a frontmatter byte-range reader, a
  round-trip fidelity gate, and an external-edit conflict experience. Reversing that decision
  deleted roughly 2,000 lines in an afternoon. Interrogate the premise before optimising the
  machinery built on it.
- **A generator owns its output.** Once Markdown became a one-way render artifact, external-edit
  reconciliation stopped being a problem to solve and became a case that cannot arise.
- `@milkdown/kit` already bundles everything for a document-first editor — `plugin/block`,
  `plugin/slash`, `plugin/tooltip`, `plugin/upload`, `component/image-block`, `component/code-block`.
  Vue, CodeMirror and DOMPurify are already transitive dependencies. Reach for the kit before adding
  anything.
- Milkdown emits `markdownUpdated` when it first parses a document. That is the serializer speaking,
  not a person typing; treating it as an edit makes every open look dirty.
- Hard-wrapped Markdown does not survive a Milkdown parse/serialize round trip — paragraphs come back
  as single long lines. This is why the old fidelity gate fired constantly on ordinary files.
- Koan's JSON connector has two layouts. `Aggregate` stores an entire entity set in one array file,
  which makes per-article commits impossible; `IndividualFiles` with `IndividualFilePath` gives one
  file per entity. Use `IndividualFiles`.
- Koan does not enforce optimistic concurrency: the JSON connector does not implement
  `IConditionalWriteRepository`, and `EntityController` does not enforce `If-Match`. Any write path
  needing conflict safety must compare a revision itself.
- Configuration read from `builder.Configuration` at host-build time cannot be overridden by
  `WebApplicationFactory`, whose `ConfigureAppConfiguration` runs later. Resolve runtime-selected
  values lazily.
- Koan's static entity facade (`Article.All`, `Article.Upsert`) binds to the host that built it, so
  two `WebApplicationFactory` instances alive at once will not stay separate. Tests that touch
  articles share one host through the `tezuri-host` xUnit collection and isolate themselves with
  unique slugs instead.
- **Idempotence can replace a protocol.** The Substack importer used to carry plan digests, an
  `If-Match` preview/apply handshake, a staging tree, atomic directory moves, and a committed
  manifest — roughly 1,100 lines — so that a re-run could not damage anything. Making the apply step
  skip any article that already exists gives the same guarantee in one `Directory.Exists` check.
  When a mechanism exists to make an operation safe to repeat, ask whether the operation can simply
  be repeatable.
- `node --test <dir>` is resolved as a module path and fails; `node --test` with no positional
  argument discovers `**/*.test.ts` correctly and skips `node_modules`.
- **A compile-error list is not a plan.** A handoff once described the remaining work as "port three
  services", which was a reading of the 28 build errors rather than of the agreed design. The work
  was then done faithfully and was still wrong: the flattening and the wire-protocol removal were
  simply absent. Check finished work against the proposal, not against the compiler.
- `WebApplication.CreateBuilder(args)` takes the content root from the *working directory*. A
  double-clicked application has no meaningful working directory, so pass
  `ContentRootPath = AppContext.BaseDirectory`. Without it the published executable serves the SPA
  only when launched from its own folder.
- `SelfContained` as a project property makes the project unreferenceable by a non-self-contained
  test project (NETSDK1151). Keep it on the `dotnet publish` command line instead.
- A single-file publish still leaves `wwwroot/` beside the executable. Embedding it
  (`GenerateEmbeddedFilesManifest` plus `ManifestEmbeddedFileProvider`) is what makes the artifact
  genuinely one file; guard the publish so a missing client build fails loudly rather than shipping
  an empty shell.
- Photino needs a single-threaded apartment on Windows and blocks until the window closes, so the
  window runs on its own STA thread while the host runs normally.

## Index

| Topic | Owner document |
| --- | --- |
| Every decision in force | [`docs/DECISIONS.md`](DECISIONS.md) |
| Visual tokens and component grammar | [`docs/design/SYLIN-VISUAL-CONTRACT.md`](design/SYLIN-VISUAL-CONTRACT.md) |
| What Tezuri is, how to run and build it, invariants | [`README.md`](../README.md) |
| Agent onboarding | [`AGENTS.md`](../AGENTS.md) |
| **In-flight work and resume point** | [`local/NOTES.md`](../local/NOTES.md) |
