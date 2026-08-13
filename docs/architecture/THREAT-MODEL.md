# Threat model

## Assets and trust

Protected assets are the mounted repository and its Git history, unpublished writing/media,
publication credentials, configured command authority, and the host outside `/workspace`. The person
running Tezuri and the explicitly selected repository configuration are trusted. Article content,
imported HTML/media, browser requests, repository paths/symlinks, remote Git state, network responses,
and dependencies are untrusted or changeable.

V1 is a loopback single-user tool. It is not safe to expose to a LAN or Internet and has no remote
identity/authorization layer.

## Main threats and controls

| Threat | Required control |
| --- | --- |
| DNS rebinding or cross-site mutation | Host must be localhost/loopback; supplied Origin must match scheme/host/port; unsafe methods require a random per-process nonce kept only in client memory. |
| Workspace/path escape | Canonical root containment, portable IDs, reject traversal/rooted paths and existing symlink/reparse escapes; never mount home or Docker socket. |
| Lost or corrupted articles | Exact base SHA and expected bytes, non-overlapping byte patches, strict UTF-8, adjacent flushed atomic replacement, external-change conflict. |
| Script/markup execution | Restrictive CSP, sanitized preview, protected raw blocks, no imported scripts/event handlers/presentational HTML. |
| Arbitrary command execution | Only reviewed committed structured commands; no browser shell strings/interpreter commands; contained working directory, timeout/cancel, resource/output bounds. |
| Credential disclosure | Prefer delegated agents/helpers; ephemeral fallback; redact arguments/output; no repository, browser, layer, diagnostic, or log persistence. |
| Git history damage | No automatic branch/reset/clean/rebase/force operations; explicit allowed paths; fetch and expected-tip check; idempotent preparation. |
| Malicious/oversized media | Content sniffing, type/byte/pixel budgets, deterministic hashes, reject SVG/scripts and hotlinks, article-local containment. |
| Import supply-chain/privacy loss | Explicit live operation, public inventory/export authority, bounded downloads, checksums/manifests, local assets, reviewed warnings; no private/paid content invention. |
| Dependency/image compromise | Exact locks, immutable CI actions, pinned base line, non-root/read-only runtime, dropped capabilities, SBOM/provenance and digest verification before release. |
| Sensitive diagnostics | Structured minimum logs, secret/content redaction, bounded outputs, inspectable export before sharing, no telemetry. |

## Residual risks and non-controls

The launch nonce is process protection, not user authentication; another process acting as the same
local user may observe process output. A deliberately trusted repository command can execute with the
container user's authority inside its isolated copy. Docker Desktop/host filesystem semantics differ,
so containment, ownership, and atomicity need platform tests. A malicious dependency or compromised
host remains outside what the application boundary alone can solve.

Any remote/LAN mode, multi-user feature, browser extension, Docker socket integration, cloud
credential store, or broader filesystem mount requires a new threat model and accepted ADR first.

