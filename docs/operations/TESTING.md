# Testing

## Normal gate

Run from the repository root:

```sh
./eng/verify.sh
```

or `pwsh ./eng/verify.ps1`. It uses locked restores, runs client checks/tests/build, validates
repository contracts, verifies .NET formatting, builds Release with warnings as errors, runs all
solution tests, and checks Git whitespace.

Focused commands are useful during development:

```sh
dotnet test tests/Tezuri.Infrastructure.Tests/Tezuri.Infrastructure.Tests.csproj
dotnet test tests/Tezuri.Configuration.Tests/Tezuri.Configuration.Tests.csproj
dotnet test tests/Tezuri.Contracts.Tests/Tezuri.Contracts.Tests.csproj
dotnet test tests/Tezuri.Git.Tests/Tezuri.Git.Tests.csproj
dotnet test tests/Tezuri.App.Tests/Tezuri.App.Tests.csproj
cd src/Tezuri.App/ClientApp && npm run check
```

## Slow gate

`eng/container-smoke` builds the clean Docker context and runs as a non-root user with a read-only
root, dropped capabilities, loopback mapping, and a writable sample repository. It checks liveness,
readiness, the SPA/security headers, and a nonce-protected file round trip without leaving content
dirty. Run it for container, runtime, filesystem, security, Proof, or packaging changes.

CI additionally scans the built image with a commit-pinned Anchore action and fails on every high or
critical finding, including findings without a published fix. A local Docker Scout scan is useful
supporting evidence but does not replace that merge gate.

Browser automation will be added before the first release. Until then, every UI change needs manual
Chromium evidence at desktop and exactly 390px, keyboard-only flow, visible focus, 200% zoom,
reduced-motion, and console/network review. Firefox and screen-reader evidence is required before the
rich editor is accepted.

## Required invariants by slice

- Source: BOM/EOL/Unicode/malformed input, byte-identical no-op, localized edit, unknown/protected
  syntax, external conflict, interrupted write.
- Configuration/path: schema/version, unknown/unsafe YAML, traversal, existing symlink escapes,
  structured command restrictions.
- Contracts: every public schema builds, every persisted document has passing and failing golden
  evidence, schema IDs remain unique, and domain wire records serialize against their schemas.
- Media: sniffed type, size/pixel limits, hash/name determinism, dedupe/race, containment, metadata.
- Proof: isolated clean copy, mounted source unchanged, command trust, timeout/cancel, output bounds,
  cleanup, generated-site inspection.
- Import: feed/archive disagreement, rich structures, malicious markup, asset ownership, paid teaser,
  retry/rerun/local edit, manifest completeness.
- Git: exact path plan, dirty/conflicting states, idempotent commit, bare remote push, divergence,
  credential redaction.

Ordinary CI must be deterministic and must not call live Substack, GitHub, or sylin.org. Live
inventory, deployed-site checks, and public digest pulls are explicit dogfood/release evidence.
