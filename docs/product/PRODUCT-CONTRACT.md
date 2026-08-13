# Product contract

Tezuri is a local, single-user writing room for Bundling Ways. It makes repository-native Markdown
authoring, owned media, target-site proof, and explicit Git publication approachable without
becoming the authority for the content.

## Fixed boundaries

1. The selected repository is the source of truth for articles, metadata, media, configuration,
   editorial state, and publication history.
2. The application and container are disposable; no database or browser state is needed to recover
   saved work or build the site.
3. Markdown plus YAML frontmatter remains canonical and usable in an ordinary editor. Unknown fields
   and unsupported syntax are preserved, not normalized away.
4. Local editing works offline. Network access happens only for an explicit import, remote Git
   operation, dependency restore, or deployed-site verification. Tezuri has no telemetry.
5. Saving never commits, pushes, merges, changes branches, or publishes. Those are separate,
   previewable actions initiated by a person.
6. The mounted site's own build is the publication renderer. Tezuri's preview is immediate feedback,
   not proof of public output.
7. There are no Tezuri accounts, identity plane, roles, organizations, SSO, or hosted service.
8. V1 solves the real sylin.org/Kintsugi workflow before growing a generic CMS/plugin platform.

## First useful journey

A person mounts a configured repository, opens the nonce-bearing loopback URL, sees its folder-native
articles, edits a supported source region, and saves an atomic localized diff. They can inspect
unsupported source without loss, add owned media, run the repository's declared proof in isolation,
review exact publication paths, prepare a Git commit, and optionally push through a narrowly
delegated host credential.

The first release is not complete until the entire current public Kintsugi Architecture corpus is
enumerated and imported with manifests and local media, the sylin.org Eleventy outputs pass their
acceptance matrix, one coherent block is observed live by exact commit, and an anonymous public OCI
digest repeats the mounted-repository journey.

## Deliberate non-goals

- hosted CMS, newsletter delivery, analytics, multi-site control plane, or generic DAM;
- collaborative real-time editing, accounts, access-control roles, or tenancy;
- automatic Git publication or deployment;
- a Tezuri-owned HTML/feed renderer;
- AI writing/generation as a V1 product surface;
- public/LAN exposure or ambient service discovery.

Current maturity and concrete commands live in the root [`README.md`](../../README.md). Decision
rationale lives in [`docs/decisions`](../decisions/README.md).

