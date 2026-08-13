# ADR 0001: Repository files are authoritative

- Status: Accepted
- Date: 2026-08-13

## Context

Tezuri must improve a repository-native writing workflow without becoming the sovereign home of
its content. The mounted repository already has a durable history, review path, and recovery model
through Git.

## Decision

Article Markdown, metadata, media, editorial configuration, and publication state are ordinary
files in the mounted repository. Git supplies durable history and attribution. Tezuri may build an
in-memory, filesystem-watched index and disposable caches, but it will not use SQLite, a JSON entity
store, or another article database. Deleting all Tezuri runtime state must leave every saved article
and the target site's normal build intact.

## Consequences

- Every mutation has a reviewable filesystem diff.
- Runtime indexes must be reconstructible from `/workspace`.
- Tezuri services operate on file-oriented domain objects rather than persistence entities.
- Repository locking, external edits, and Git state are normal operating conditions.

## Evidence

`PROJECT-STARTUP-PROMPT.md` fixes repository authority, disposable runtime state, and the no-database
boundary. The sylin.org dogfood target already builds solely from checked-in source.

## Rejected alternatives

- A Tezuri-owned article database with export to Markdown: it creates competing authority.
- A database used as a mandatory index: losing runtime state would impair the workspace.

## Revalidation triggers

Revisit only if a required workflow cannot be represented as repository files plus reconstructible
runtime state. Any exception must preserve a complete file-only edit and publication path.

