# ADR 0010: Substack import is manifested and complete

- Status: Accepted
- Date: 2026-08-13

## Context

The Kintsugi Architecture publication is the first-release acceptance corpus. A feed alone may omit
older, paid, private, or platform-only material, while public HTML contains tracking and subscription
chrome that is not authored article content. A casual HTML-to-Markdown copy cannot prove completeness,
asset ownership, or fidelity.

## Decision

Each live migration inventories both public feed and archive at run time and prefers a current owner
export when available. Public articles minus a reviewed exclusion manifest must equal imported
articles. Paid/private text that is not available is never invented.

Every article gets canonical folder-native Markdown and local displayed media plus an inspectable
manifest containing source URL/ID, source and normalized metadata, body/source checksums, asset
mapping/checksums, transformations, warnings, final path, and fidelity state. Import removes tracking,
subscription, analytics, recommendation, and navigation chrome but records the transformation.
Reruns are idempotent and never overwrite local editorial work without a three-way review.

Ordinary CI uses compact synthetic fixtures. The complete live/imported corpus and visual review live
in the website dogfood workflow.

## Consequences

- Current discovered count is evidence, not a hard-coded product constant.
- Feed/archive disagreement, teasers, failures, duplicates, unknown metadata, and reruns need tests.
- Every meaningful source tag/media/structure is preserved or appears as a reviewed warning.
- No published migrated article may rely on a Substack/CDN/proxy hotlink.

## Evidence

The founding product contract names three known baseline posts but explicitly requires any additional
current public articles and a reviewed import manifest. Live discovery and owner-export comparison
remain dogfood gates; this ADR does not claim they have run.

## Rejected alternatives

- RSS-only inventory: it cannot prove completeness.
- Undocumented Substack API as permanent input: it is unstable and unauthoritative.
- Keep remote image URLs: it violates owned, durable article content.
- Store only normalized output: transformations and fidelity could not be audited.

## Revalidation triggers

Revisit if Substack offers a stable documented export/API with greater fidelity, if the corpus proves
a required unsupported media type, or if source licensing/authorization changes.

