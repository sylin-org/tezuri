# ADR 0011: Inherit Sylin semantics without runtime coupling

- Status: Accepted
- Date: 2026-08-13

## Context

Tezuri belongs in the Sylin product language and should feel coherent with sylin.org. The public site
and local editor have different jobs, release cycles, accessibility states, and failure boundaries.
Importing the website build, CSS, or assets at runtime would make offline authoring and independent
versioning fragile.

## Decision

Tezuri owns a small local visual implementation that translates stable Sylin semantics: warm
night-garden surfaces, tactile restrained decoration, plain human language, clear state, and
long-form reading priority. The durable contract is documented in
`docs/design/SYLIN-VISUAL-CONTRACT.md`; there is no cross-repository runtime dependency.

The website remains authoritative for public pages. Tezuri never writes site chrome or commits a
rendered `dist/`. Browser review compares current public semantics during dogfood and updates this
contract deliberately when needed.

## Consequences

- Tezuri can run offline and release independently.
- Some style translation is duplicated intentionally and must be revalidated, not blindly synced.
- Component names/status language stay semantically aligned while implementation details differ.
- Public article layout/accessibility remains an Eleventy/website responsibility.

## Evidence

The existing website uses a night-garden/pixel character and plain local-first language. The initial
client shell implements those semantics with Tezuri-owned CSS and passed Chromium desktop/390px,
keyboard tab, nonce-scrub, overflow, and console checks. Broader browser/accessibility evidence
remains a release gate.

## Rejected alternatives

- Consume sylin.org CSS/assets at runtime: breaks offline operation and couples deploys.
- Copy the full site stylesheet: imports irrelevant public-site assumptions and drifts silently.
- Ignore Sylin semantics: makes the real dogfood workflow feel like a generic admin console.

## Revalidation triggers

Revisit when the Sylin design language materially changes, when shared versioned tokens become a
real maintained artifact, or when accessibility review shows the translation is harmful.

