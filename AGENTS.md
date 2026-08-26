# Tezuri agent guide

The durable entry point for agents. Read it fully at the start of a session. It holds standing rules,
not task notes.

## Read first

1. [`docs/PRODUCT-BRIEF.md`](docs/PRODUCT-BRIEF.md) — the product authority
2. [`README.md`](README.md) — the current repository and implementation state
3. [`docs/DECISIONS.md`](docs/DECISIONS.md) — implementation decisions now in force
4. [`docs/MEMORY.md`](docs/MEMORY.md) — standing preferences and durable learnings
5. The nearest existing code and its test

Working notes that must not be committed — environment paths, credential *locations*, session
handoffs — live in the gitignored `local/NOTES.md`; see [`local/README.md`](local/README.md).

## Reset boundary

Tezuri is being implemented afresh in Rust and Tauri. This is a stack reset, not a product reset.
Derive behavior from the product brief and current decisions, not from deleted source or repository
history. Do not inspect or revive the earlier implementation unless the owner explicitly asks.

The current working-tree deletions are intentional. Preserve them and all other unrelated changes.

## Product invariants

- **Files are truth; the desk is a lens.** The article's Markdown file and its small `meta.yaml`
  sidecar are canonical inside the chosen publication. Indexes, journals, previews, and renditions
  are derived caches Tezuri may delete and rebuild at any time.
- **Metadata Tezuri does not model is preserved verbatim** and written back untouched.
- **Publishing is explicit.** Saving must never commit, push, switch branches, or rewrite history.
- **The target repository's build is authoritative.** Do not build a competing renderer.
- **Local, single-user software.** No accounts, roles, tenancy, telemetry, update pings, crash
  reporting, or silent network work. Only an explicit git fetch or push may use the network.
- **Only the application session the user opened may mutate data.** Keep the native-command,
  capability, path, symlink, command, log-redaction, and credential boundaries intact.
- **User input never becomes executable shell text.** Programs and argument arrays stay separate,
  nothing is passed through a shell, and a proof program may not be a shell interpreter.
- **Writes stay inside the chosen publication.** Never request a home directory, credential store,
  or git's internal directory.
- **No canonical content in application settings.** Losing Tezuri's own state may lose preferences
  and the publication registry, but never writing.
- **Publications are isolated.** Supporting several publications must not permit state or writes
  from one to contaminate another.

## Layout and ownership

Prefer one file per concept and add to the file that owns a concern before creating another layer.
Do not invent extension points, schemas, or configuration ahead of a demonstrated need.

Never hand-edit generated build output such as `target/`, `node_modules/`, `dist/`, bundled assets,
or coverage output. Commit dependency lockfiles once the toolchain creates them.

`samples/` holds synthetic fixtures only; never a private or live corpus.

## Working

1. Read the current tree before trusting any description of it, including this one.
2. Say which files you will touch, and why, before editing.
3. Preserve unrelated working-tree changes.
4. Run focused tests, then `pwsh ./eng/verify.ps1`. Do not cite deleted commands as a green build.
5. Exercise the packaged application, not only a development runner. A release check must cover the
   artifact a person actually downloads.
6. UI changes need the real Tauri window at desktop and narrow widths, keyboard flow, 200% zoom,
   reduced motion, and a no-console-error check.
7. Record a consequential implementation decision in `docs/DECISIONS.md`, including what it
   replaces. Do not restate the product brief there.

Never use destructive Git cleanup, automatic branch changes, broad staging, history rewrites, or
force pushes. One commit is one logical change and includes only intentional paths.

## External boundaries

The separate `sylin-org/website` checkout is read-only visual reference unless the owner explicitly
authorizes website work. Never cross-commit, and never publish it without explicit go-ahead.
