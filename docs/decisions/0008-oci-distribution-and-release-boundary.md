# ADR 0008: Release Tezuri as a verified public OCI image

- Status: Accepted
- Date: 2026-08-13

## Context

The supported user path is an anonymously pullable, disposable container that operates on one
mounted repository. Development-only execution or an unversioned image does not prove that path.

## Decision

Publish `ghcr.io/sylin-org/tezuri` as a reproducible multi-stage OCI image for `linux/amd64` and
`linux/arm64`. The runtime is non-root, includes only the runtimes required by supported target
proofs, and declares health and OCI metadata. Compose is a convenience over the same image and
workspace contract, not a separate deployment architecture.

Release publication starts only from an explicit semantic-version tag or release after all gates
pass. Publish immutable version and commit-SHA tags; move `latest` only for a verified stable
release. Generate an SBOM and build provenance and use GitHub-supported keyless signing or
attestation. Verify package visibility and an anonymous pull by digest, then repeat the mounted
workspace smoke journey from that digest.

## Consequences

- Pull-by-digest is the release verification identity.
- Forked pull requests receive no package or repository write permission.
- Public dependencies and pinned toolchain lines must restore without sibling source repositories.
- A successful workflow is insufficient until the published digest passes the clean smoke journey.

## Evidence

The startup contract fixes the GHCR name, public multi-architecture distribution, provenance,
versioning, and anonymous-pull completion gate.

## Rejected alternatives

- Publish on every branch build: unreviewed code would gain release authority.
- Ship only `latest`: users could not reproduce or retain a known version.
- Depend on unpublished local Koan artifacts: a clean consumer build would fail.
- Require a hosted Tezuri service: the container would cease to be independently useful.

## Revalidation triggers

Revisit supported platforms, bundled target runtimes, or signing mechanism when upstream support or
verified user demand changes. Do not advertise a platform before its image and smoke test pass.

