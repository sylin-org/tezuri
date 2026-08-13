# Workspace and publication architecture

## Authority and containment

The operator selects exactly one host repository and mounts it at `/workspace`. All app paths start
as configured repository-relative paths, are normalized to a canonical absolute path, checked to
remain under the root, and rejected if an existing symlink/reparse-point traversal escapes it.
Article identifiers are portable single segments. Writes use an adjacent temporary file, flush, and
atomic replace; callers use hashes/expected bytes to avoid overwriting external edits.

Runtime indexes, operation progress, and caches are disposable. If a future cache lands on disk, it
must be outside canonical content paths, keyed by workspace/schema hashes, and safe to delete.

## Proof

The workspace configuration declares reviewed commands as an executable plus an argument vector,
working directory, timeout, and optional output directory. Tezuri displays them before trust, copies
the repository into a bounded isolated temporary workspace, excludes Git/dependency/build output,
runs the executable without a shell, captures bounded/redacted output, and cleans up. The mounted
repository is never a build scratch directory. The target build decides whether public artifacts
are correct.

## Git publication

Publication starts from an exact allowed-path diff and stable repository/remote state. “Prepare
commit” stages only those paths and creates no duplicate commit for the same content/config hash.
Tezuri never switches branches, cleans, resets, rebases, force-pushes, or rewrites history.

Optional push first uses a narrowly delegated SSH agent or credential helper. An ephemeral askpass or
secret file may be used for automation, but never committed, logged, stored in browser state, or
baked into an image. A fetch must prove the expected remote tip immediately before push; divergence
stops with an explanation and preserves local work. A temporary clone/worktree, created inside
container-owned temporary storage, isolates authoritative builds/publication preparation.

For sylin.org, Tezuri commits source/config/media/import records only. Existing Cloudflare Pages Git
integration deploys Eleventy output. Verification polls the public origin until the expected commit
revision appears, then checks the changed pages, full-text feed, discovery files, metadata, links,
390px layout, and no-JavaScript reading.

