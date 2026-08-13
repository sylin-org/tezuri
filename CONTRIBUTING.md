# Contributing to Tezuri

Thank you for helping build a writing tool that leaves its users' repositories understandable and
portable.

## Setup

Use Git, .NET SDK 10.0.302, Node.js 24, npm, and (for container checks) Docker Desktop or a compatible
Linux Docker Engine. Clone the repository, then run the root gate:

```sh
./eng/verify.sh
```

On PowerShell use `pwsh ./eng/verify.ps1`. Restores are locked; change a dependency intentionally,
review its license/security/release notes, regenerate the relevant lock file, and explain why.

## Working agreement

- Start from an issue or a bounded problem statement. For material architecture, discuss the shape
  before a large patch.
- Create a focused branch. Use conventional commit subjects such as `feat(media): ...` or
  `fix(source): ...`; do not mix formatting or unrelated cleanup into a product change.
- Preserve mounted-file authority, lossless editing, explicit publication, and local security.
- Add tests at the lowest useful layer and update public contracts/docs in the same change.
- A decision that changes authority, compatibility, security, distribution, editor boundaries, or
  target behavior needs a new ADR (or a superseding ADR), not a silent rewrite.

## Evidence by change type

- Domain/config/source: focused unit/contract tests, no-op bytes, localized diff, and malformed input.
- Filesystem/media/Git: containment, symlink/race/conflict, atomicity, idempotence, and failure paths.
- HTTP/security: in-memory host tests for Host, Origin, nonce, headers, validation, and conflict shape.
- Client/UI: type/tests/build plus Chromium desktop and 390px, keyboard, reduced motion, zoom, and
  accessibility smoke; include images only when they show real behavior.
- Container/runtime: build and `eng/container-smoke` with a writable fixture, non-root/read-only-root
  assertions, restart, and clean content diff.
- Website/dogfood: the website's own full check, imported-corpus invariants, reviewed visual output,
  and exact deployed commit evidence when publication is authorized.

## Fixtures and rights

Keep ordinary CI deterministic and offline. New fixtures must be compact, synthetic, redistributable,
and free of secrets, private drafts, personal data, access tokens, and copyrighted third-party
corpora. Record why any nontrivial fixture is safe to redistribute. Live Kintsugi evidence and
canonical imported articles belong in the website repository under its reviewed import manifest,
not as an unexplained Tezuri test dump.

## Pull requests

Complete the pull-request template honestly. A maintainer may ask for smaller commits, an ADR,
security review, or visual/accessibility evidence. All available CI checks must pass. The current
one-owner project does not promise review or merge timelines.

By submitting a contribution, you agree that it is licensed under the repository's Apache-2.0
license. Community participation follows [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md). Report
vulnerabilities privately as described in [`SECURITY.md`](SECURITY.md).

