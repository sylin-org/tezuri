# Content model

## Workspace

A workspace is one Git-capable repository Tezuri has been pointed at. Its layout is convention, not
configuration: articles live in `src/writing/<slug>/`, each holding `article.json`, a generated
`index.md`, and a `media/` directory. See `Workspace/WorkspaceLayout.cs`.

The only settings left are the media policy, the Proof executable/argument arrays, and the paths Git
publication may touch. Each ships with a working default, and all repository paths are relative,
portable, canonicalized, and containment-checked.

## Article

Each article is a directory under the configured article root:

```text
<article-root>/<slug>/<article-file>
<article-root>/<slug>/<media-directory>/...
```

The article file is exact UTF-8 Markdown with optional UTF-8 BOM and YAML frontmatter. Stable identity
comes from an explicit metadata ID when supported; the folder slug is the safe initial locator.
Configured metadata must eventually cover title/description/byline, original and updated dates,
draft state, source tags and curated topics, section/series, canonical URL, media metadata, origin
provenance, Kintsugi editorial fields, and arbitrary additional keys.

## Exact source protocol

`tezuri.article-source` V1 exposes:

- full original bytes as base64 with encoding/BOM/line-ending metadata and SHA-256;
- immutable article identity and repository-relative path;
- byte slices for frontmatter and body;
- stable byte-ranged `rich` or `protected-raw` segments;
- capabilities and actionable diagnostics.

`tezuri.source-patch-set` V1 supplies a base SHA and ordered, non-overlapping byte replacements. Each
replacement includes the exact expected bytes. The server reopens the file, rejects stale bases or
unexpected ranges, applies only the requested bytes, validates UTF-8, flushes an adjacent temporary
file, and atomically replaces the canonical file. Empty patches are byte-identical no-ops.

Editor-native ProseMirror/Milkdown state is ephemeral and never crosses the permanent boundary.
Frontmatter UI changes will use source/CST-aware localized operations; whole-document serialization
is not an acceptable shortcut.

## Media

Owned media lives inside its article directory. An ingest preserves a safe source asset, calculates
SHA-256, uses a deterministic hash-derived filename, deduplicates equal bytes, and records source,
rights, alt/caption/credit metadata separately in canonical article/frontmatter structures. Remote
hotlinks, scripts/SVG, secrets, and files outside the configured media policy fail closed.

## Publication and import records

Proof results, import manifests, and publication receipts are versioned inspectable records. They may
be written into configured repository paths only when the content contract calls for them; transient
progress is in memory. An import manifest maps source IDs/URLs and metadata, source/body checksums,
asset checksums, transformations, warnings, and final paths. Git commits remain the durable
publication record.

