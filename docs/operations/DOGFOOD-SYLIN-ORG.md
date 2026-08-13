# Dogfood: sylin.org and Kintsugi Architecture

This runbook governs explicit live work. It is not part of ordinary CI and does not authorize a
website commit, push, release, or deployment by itself.

## Inventory and import

1. Record timestamp/tool revision. Inventory both the public Kintsugi RSS feed and archive; use a
   current owner export as highest-fidelity input when available.
2. Classify every discovered item. Public articles minus reviewed exclusions must equal the import
   set. Notes/comments/chat/admin/subscription chrome are not articles. Never invent private/paid
   text exposed only as a teaser.
3. The known minimum baseline is three posts dated 2026-03-17, 2026-03-21, and 2026-08-03, but the
   discovered count—not a hard-coded three—is authoritative.
4. Import into `src/writing/<slug>/index.md` plus `media/`. Preserve all meaningful metadata/body
   structure and localize every displayed asset. Strip tracking/subscription/analytics chrome and
   record each transformation.
5. Produce one inventory manifest and per-article manifests with URLs/IDs, source and normalized
   metadata, checksums, asset mapping, transformations, warnings, final paths, and fidelity state.
6. Review every warning. Rerun and prove idempotence; then open/no-op-save the whole corpus with a
   clean Git result and prove one paragraph changes only a localized range.

## Website integration

Work only in the distinct `sylin-org/website` repository and preserve existing owner changes. Add
reviewed `tezuri.yaml`, article schema/UI hints, folder-native Eleventy collection/templates, writing
index/routes, full-text RSS with tags/categories, `/writing.md`, sitemap/JSON-LD/canonical/social and
Standard.site outputs required by its publication brief. Absence of published articles remains a
no-op. Never commit `dist/` or weaken `scripts/check-site.js`.

For each coherent conventional-commit block:

1. run the website's clean `npm` test/check and generated-site acceptance matrix;
2. inspect desktop, true 390px, keyboard, reduced-motion, no-JavaScript, feed content type/full text,
   discovery docs, metadata, internal links, and owned media;
3. push only with explicit owner authority;
4. wait until `https://sylin.org` reports the exact commit/revision;
5. repeat route/feed/discovery/visual checks on the public origin before the next block.

Record discovered/imported/excluded counts, exclusions, manifests, warnings, Tezuri and website
commits, commands/results, deployed revision/routes, and remaining limitations under dated evidence.

