# Tezuri agent guide

The durable entry point for agents. Read it fully at the start of a session. It holds standing rules,
not task notes.

## Read first

1. [`README.md`](README.md) — what Tezuri is and how to run it
2. [`docs/DECISIONS.md`](docs/DECISIONS.md) — every decision in force, and what each one replaced
3. [`docs/MEMORY.md`](docs/MEMORY.md) — standing preferences and durable learnings
4. The nearest existing code and its test

Working notes that must not be committed — environment paths, credential *locations*, session
handoffs — live in the gitignored `local/NOTES.md`; see [`local/README.md`](local/README.md).

## Invariants

- **The entity is canonical.** `article.json` is the article. `index.md` is generated on save and
  never read back. Do not build anything that parses it.
- **Metadata Tezuri does not model is preserved verbatim** and written back untouched.
- **Publishing is explicit.** Saving must never commit, push, switch branches, or rewrite history.
- **The target repository's build is authoritative.** Do not write a competing HTML renderer.
- **Local, single-user software.** No accounts, roles, tenancy, telemetry, or silent network work.
  Tezuri never fetches from the network on a user's behalf.
- **Mutations require the loopback origin check and the launch nonce.** Do not weaken path,
  symlink, CSP, command, log-redaction, or credential boundaries.
- **Browser input never supplies executable shell text.** Executable and argument arrays stay
  separate, nothing is passed through a shell, and a proof executable may not be a shell interpreter.
- **Writes stay inside the chosen repository.** Never request a home directory or `.ssh`.
- **One session, one repository.** A second repository means a second process.

## Layout and ownership

One project, one file per concept — see the layout in [`README.md`](README.md). Prefer adding to the
file that owns the concept over adding a file.

Never hand-edit `bin/`, `obj/`, `node_modules/`, `dist/`, `wwwroot/`, or coverage output. Do commit
`packages.lock.json`, `koan.lock.json`, and the npm lockfile — a stale one has already hidden a stub
package that only a clean environment caught.

`samples/` holds synthetic fixtures only; never a private or live corpus.

## Working

1. Read the tree before trusting any description of it, including this one.
2. Say which files you will touch, and why, before editing.
3. Preserve unrelated working-tree changes.
4. Run focused tests, then `pwsh ./eng/verify.ps1`. If it is green, the branch is green.
5. UI changes need a real browser: desktop and 390px, keyboard flow, reduced motion, no console
   errors. Screenshots only after they show real behaviour.
6. Record a consequential decision in `docs/DECISIONS.md`, saying what it replaced.

Never use destructive Git cleanup, automatic branch changes, broad staging, history rewrites, or
force pushes. One commit is one logical change and includes only intentional paths.

## External boundaries

The Koan checkout is **read-only framework evidence**. Never modify it, and never take a project
reference into it — consume the published `Sylin.Koan.*` packages only.

Website changes happen only in the separate `sylin-org/website` checkout. Never cross-commit.
Publish only with the owner's explicit go-ahead.
