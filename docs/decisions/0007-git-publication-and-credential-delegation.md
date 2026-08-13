# ADR 0007: Publish through explicit Git operations and delegated credentials

- Status: Accepted
- Date: 2026-08-13

## Context

Git already supplies publication history and the target repository's deployment trigger. Tezuri
needs to prepare and optionally push a coherent change without taking ownership of accounts,
credentials, branches, or repository history.

## Decision

Publishing is an explicit user action. Tezuri never automatically changes branches, resets, cleans,
rebases, force-pushes, merges, or rewrites history. It stages only reviewed article, media,
configuration, manifest, and target-integration paths in an isolated preparation area. Before push,
it fetches and refuses unexpected remote divergence. Repeating the same content/configuration hash
must not create another commit.

A credential-free **prepare commit** path always works and leaves a commit ready for the user's
normal host-side push. Optional push first uses a narrowly delegated SSH agent or credential helper;
an ephemeral secret file or askpass process is the automation fallback. Tezuri never stores Git
credentials in repository files, image layers, logs, or browser storage and never requires a home
directory or `.ssh` mount. GitHub PR creation may be an adapter; ordinary commit and push do not
depend on GitHub APIs.

## Consequences

- Receipts name selected paths, commit SHA, branch/push result, and a safe correction path.
- Dirty trees and conflicts are reported and preserved rather than cleaned away.
- CI proves push behavior against a local bare remote without external credentials.
- Deployment verification follows Git publication; it is not inferred from push success.

## Evidence

The startup contract fixes human-controlled publication, credential delegation, divergence checks,
idempotency, and the website's Git-to-Cloudflare dogfood path.

## Rejected alternatives

- Store a GitHub token in Tezuri: this creates an account and secret-management product.
- Mount the user's full home or `.ssh`: the container receives unnecessary authority.
- Push every autosave: editing would become publication.
- Force remote history into agreement: user work could be lost.

## Revalidation triggers

Any hosted collaboration, automatic merge, server-side credential vault, or non-Git publication
target requires a separate decision and explicit owner authorization.

