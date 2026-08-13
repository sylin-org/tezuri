# Release

No Tezuri release has been published. The following is the required release procedure, not evidence
that it has happened.

## Preconditions

1. Curate `CHANGELOG.md`; choose a SemVer `vMAJOR.MINOR.PATCH` tag.
2. Run the normal, browser, container, dogfood, license, secret, and dependency/vulnerability gates.
3. Prove the supported `linux/amd64` and `linux/arm64` images, non-root/read-only behavior, workspace
   ownership, restart, and a mounted source edit.
4. Resolve each Docker base tag to a current multi-platform digest and review the update.
5. Ensure all action references remain immutable and minimal-permission.
6. Confirm the website dogfood and imported corpus evidence is current and has no unexplained
   warnings/hotlinks.

## Publication

The tag-triggered release workflow builds `ghcr.io/sylin-org/tezuri` for amd64/arm64, emits immutable
version and `sha-<commit>` tags, moves `latest` only for a stable version, generates SBOM/provenance,
and publishes a GitHub attestation. Publication requires the owner's explicit authorization; do not
push a release tag as an incidental development step.

After the first package exists, an owner may need to open the package settings in GitHub and change
visibility to **Public**. Log out of GHCR, pull by digest anonymously in a clean environment, inspect
both manifest platforms, and rerun the mounted-repository smoke. A successful workflow without this
anonymous digest journey is not a verified release.

Record tag, source commit, image digest, manifest platforms, SBOM/provenance/attestation URLs,
anonymous-pull result, test summaries, supported host notes, and known limitations under a dated
`docs/evidence/releases/` record. Link only real public artifacts from the README.

## Rollback and removal

OCI version/digest tags are immutable. If a release is unsafe, publish a corrected version and mark
the affected release clearly; never replace its digest. Stopping/removing Tezuri must not affect the
mounted repository. Recovery is ordinary Git/filesystem recovery, never a Tezuri database restore.

