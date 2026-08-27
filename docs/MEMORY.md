# Project memory

Durable, model-agnostic memory for anyone working on Tezuri. It contains standing preferences and
learnings that are not already owned by the product brief, decision log, or current code.

Sensitive and session-scoped working notes live in [`local/NOTES.md`](../local/NOTES.md), which is
gitignored. See [`local/README.md`](../local/README.md).

## Standing preferences

- **The brief is the product authority.** Trace behavior, refusals, and v1 scope to
  [`PRODUCT-BRIEF.md`](PRODUCT-BRIEF.md). Do not infer requirements from deleted source or history.
- **Fresh implementation means fresh reasoning.** Rust and Tauri are chosen; subordinate libraries,
  boundaries, formats, and layouts are not. Select them for the current product rather than for
  resemblance to an earlier application.
- **Exercise the real artifact.** A development runner can support iteration, but release confidence
  comes from the packaged application a person downloads.
- **Least meaningful moving parts.** Prefer convention over configuration and delete a mechanism
  before documenting it when a simpler product rule provides the same safety.
- **Ceremony follows a working product.** Add policy, extension points, schemas, and compatibility
  surfaces only when real use gives them a job.

## Durable learnings

- **Chirps, not ceremony.** The app takes care of the background and stays silent about it: small
  activity chirps when something is worth knowing, a question only when an answer is truly needed —
  and questions are asked inline where the object lives, never as blocking dialogs. Modals are an
  antipattern in this product, full stop.
- **Interrogate the premise before optimising its machinery.** Disproportionate complexity is a
  reason to re-check the requirement and model, not merely to add another abstraction.
- **Every view is downstream of the file.** Editor surfaces render projections of the source
  Markdown and write back only to source files; the desk index and action journal stay derived
  caches Tezuri may drop and rebuild at any time. A feature that cannot express itself as plain
  files does not ship.
- **Idempotence can replace a protocol.** Prefer operations that are naturally safe to repeat, such
  as rebuilding the desk from files on every open, or re-storing identical media to no effect.
- **A refusal is a feature.** Unsafe media, escaping paths, ambiguous publication state, hidden
  network work, and executable shell text must be declined clearly and actionably.
- **A compile-error list is not a delivery plan.** Check completed work against the product outcome
  and its failure cases, not only against what happens to build.
- **Packaging behavior is product behavior.** Startup, bundled assets, platform dependencies, and
  first-run flow must be tested from the distributable artifact.

## Index

| Topic | Owner document |
| --- | --- |
| Product objective, scope, and invariants | [`docs/PRODUCT-BRIEF.md`](PRODUCT-BRIEF.md) |
| Implementation decisions in force | [`docs/DECISIONS.md`](DECISIONS.md) |
| Visual tokens and interaction grammar | [`docs/design/SYLIN-VISUAL-CONTRACT.md`](design/SYLIN-VISUAL-CONTRACT.md) |
| Current repository state | [`README.md`](../README.md) |
| Agent onboarding | [`AGENTS.md`](../AGENTS.md) |
| In-flight local work | [`local/NOTES.md`](../local/NOTES.md) |
