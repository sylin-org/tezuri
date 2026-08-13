# ADR 0006: The target site's build is authoritative

- Status: Accepted
- Date: 2026-08-13

## Context

Tezuri can offer an immediate editing preview, but the mounted site owns its templates, routes,
metadata, feeds, and deployment behavior. A second Tezuri renderer would inevitably disagree with
the published site.

## Decision

Tezuri's editor preview is fast feedback only. Proof runs commands declared in trusted, committed
workspace configuration inside an isolated temporary clone or worktree. It displays the exact
commands before first trust and never executes browser-supplied shell text.

The target build is the publication authority. For sylin.org, Eleventy generates article HTML,
`/writing/`, RSS, `/writing.md`, sitemap entries, JSON-LD, social metadata, and discovery files;
`npm test` is the clean proof gate and `dist/` is never committed. Tezuri serves the proof output for
inspection and reports command, validation, and artifact results without rewriting site chrome.

## Consequences

- Target adapters define configuration and result interpretation, not alternative HTML rendering.
- Proof is reproducible outside Tezuri with the repository's normal commands.
- A preview may be approximate and must be labeled separately from proof.
- Failed proof preserves source and presents the smallest corrective action.

## Evidence

The startup contract and the website's agent guide make Eleventy and `npm test` authoritative. The
existing website already validates routes, links, fragments, metadata, sitemap targets, and agent
documents from a clean generated tree.

## Rejected alternatives

- Generate production HTML in Tezuri: the target would have two renderers.
- Build in the user's dirty checkout: generated or incidental changes could contaminate publication.
- Accept arbitrary commands from a request: repository execution authority would be unreviewable.

## Revalidation triggers

Add a target only after defining its trusted configuration, isolated proof command, output boundary,
and verification adapter. A target's own renderer remains authoritative.

