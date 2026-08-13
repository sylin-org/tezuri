# Article and media profile v1

This profile describes what Tezuri may understand about an article while preserving the repository's
own source format and editorial model.

## Source document

An article is one UTF-8 Markdown file, optionally beginning with one YAML frontmatter document, in an
article-owned repository directory. Its adjacent configured media directory contains the assets that
ship with it. The mounted repository chooses the folder names through `tezuri.yaml`.

CommonMark 0.31.2 is the portable Markdown baseline. A target may use declared extensions, shortcodes,
raw HTML, or template syntax. Unsupported source remains protected source, not malformed content to
normalize. The exact byte-preserving patch protocol in ADR 0002 outranks any formatter preference.

Frontmatter is interpreted as YAML 1.2.2. Tezuri-generated scalar values use the YAML JSON schema's
portable null, Boolean, number, string, sequence, and mapping forms. Tezuri does not generate custom
tags, anchors, aliases, merge keys, or directives in v1. Existing unfamiliar constructs remain
inspectable and byte-preserved unless a person replaces the containing source range explicitly.

## Metadata authority

The workspace's `articles.metadataSchema` is authoritative for the target's raw frontmatter. The
default Tezuri article schema is an optional starting point, deliberately open to additional target
fields. It is not a universal CMS model.

An optional editor-hints document maps raw JSON Pointer paths to semantic roles and presentation
controls. Hints affect labels, grouping, and widgets; they never change validation, rename source
keys, create values, or make Tezuri the metadata authority.

Stable roles include title, summary, slug, editorial dates, publication state, editorial currency,
standing, tags, topics, language, canonical URL, and cover media. A target-specific field uses the
`custom` role plus a namespaced custom role such as `org.sylin.kintsugi.series`.

## Identity, dates, and paths

- An article ID is an opaque stable identifier, 1-128 ASCII characters from letters, numbers, dot,
  underscore, colon, and hyphen. It must not change when the title, slug, or path changes.
- Editorial dates are RFC 3339 full dates (`YYYY-MM-DD`). They do not silently acquire a timezone.
- Operation and provenance timestamps are RFC 3339 UTC instants ending in `Z` or `+00:00`.
- Contract paths are repository-relative, slash-separated paths. Absolute paths, backslashes,
  drive or UNC prefixes, `.` or `..` segments, NUL, and glob metacharacters are forbidden.
- Containment and symlink checks remain runtime requirements; a schema pattern is not a filesystem
  security boundary.
- Generic `digest` fields use `sha256:` followed by exactly 64 lowercase hexadecimal characters.
  Existing fields explicitly named `sha256` use 64 lowercase hexadecimal characters without the
  algorithm prefix. A field never accepts both forms.

## Publication and currency

Publication lifecycle and editorial currency are different axes. A draft can already have an
editorial-currency assessment, and a published article can later be archived without changing the
historical judgment of its content.

Tezuri uses `publicationState` only when a target maps or stores that concept. It uses
`editorialCurrency` for the Kintsugi-style `timeless`, `current`, `of-its-time`, and `revised`
assessment. Neither is inferred from Git state, filesystem dates, URLs, or the presence of an output
file.

## Tags and topics

Tags are author-controlled labels. Tezuri preserves their text, Unicode, case, and order. Exact,
case-folded, or visually similar duplicates may produce a non-blocking suggestion; they are never
rewritten silently.

Topics are an optional target-owned curated taxonomy. Tezuri must not promote tags into topics or
invent a universal taxonomy. An editor may offer target-declared topic choices through hints.

## Media

The article Markdown reference owns placement meaning:

- alt text and decorative intent;
- caption or nearby explanatory copy;
- crop, focal point, or presentation role when the target supports them.

An optional media manifest owns intrinsic and provenance facts:

- repository path, media type, bytes, dimensions, duration, and SHA-256 digest;
- source URL/ID, import timestamp, credit, rights statement, SPDX expression or license URL;
- generated derivatives and the transformation that created each one.

Captions do not belong in the intrinsic manifest because the same asset may appear more than once
with different editorial meaning. Tezuri may offer an asset-level alt suggestion, but saving it into
a placement remains an explicit source edit.

Owned media is the publication default. A successful import cannot leave displayed content pointing
to a Substack CDN, tracking proxy, or another transient source. Missing or legally unavailable media
must appear as a manifest warning and fidelity result rather than being invented or hidden.

## Rendering

Tezuri preview is immediate feedback. The target site's declared build is the only publication
renderer and the only authority for routes, HTML, feeds, sitemaps, metadata, responsive behavior, and
no-JavaScript behavior. Tezuri must not encode target rendering semantics into this profile.
