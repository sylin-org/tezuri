# ADR 0004: Use folder-per-article content with owned media

- Status: Accepted
- Date: 2026-08-13

## Context

An article and the media needed to render it should move, review, and survive together. External
image proxies and publication CDNs are poor archival dependencies, while a global media bucket makes
ownership and safe deletion ambiguous.

## Decision

Targets configure a folder-per-article convention. The initial sylin.org mapping is:

```text
src/writing/<slug>/
  index.md
  media/
    <owned source asset>
    <deterministic web derivative>
```

Tezuri will discover these locations from committed workspace configuration rather than hard-code
the sylin.org path. Every displayed imported asset is downloaded into the article's media folder.
Canonical metadata records alt text, caption, credit, rights/source details, and import provenance
when available. Derivative names and contents are deterministic, and references resolve within the
allowed workspace.

## Consequences

- An article change can stage its source, assets, and manifest as one explicit unit.
- Duplicate detection may share bytes operationally, but canonical ownership remains legible.
- Remote hotlinks, tracking parameters, and analytics pixels do not enter published articles.
- Path and symlink containment checks apply to every media operation.

## Evidence

The startup contract fixes folder-per-article storage and requires the complete Kintsugi import to
own every displayed asset. Eleventy 3 derives `page.fileSlug` for an `index.md` from its parent
folder, so the convention maps naturally to `/writing/<slug>/`.

## Rejected alternatives

- Flat `src/writing/<slug>.md` plus a global image directory: article ownership is split.
- Preserve Substack or proxy URLs: the archive remains dependent on a third party.
- Content-addressed assets as the only canonical layout: review and manual maintenance become opaque.

## Revalidation triggers

Revisit if a target site cannot serve colocated media, while retaining configured paths, owned
assets, deterministic output, and a text-editor-only publication path.

