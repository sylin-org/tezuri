# ADR 0005: Protect the local container with origin checks and a launch nonce

- Status: Accepted
- Date: 2026-08-13

## Context

Tezuri is a single-user local application with authority to edit a mounted Git repository and run
trusted target commands. Loopback exposure reduces reach but does not by itself prevent DNS
rebinding, cross-site requests, path traversal, or malicious article scripts.

## Decision

The process listens on the container interface so port publishing works; documented host mappings
bind `127.0.0.1:8080` to container port `8080`. Each launch creates an unguessable, ephemeral
bootstrap/session nonce. Mutating requests require the nonce and pass strict Host and Origin checks.
This is process protection, not an account system.

The production container runs as non-root, mounts only the selected repository at `/workspace`, and
never requires the Docker socket, home directory, or `.ssh`. All paths are canonicalized and must
remain within configured workspace roots after symlink resolution. Preview HTML is sanitized,
article scripts never execute, CSP is restrictive, commands and media work are bounded, and logs
redact credentials and sensitive content.

## Consequences

- Remote LAN access is outside the default V1 contract.
- Restarting invalidates the nonce without affecting saved content.
- The client holds the nonce in tab-scoped session storage rather than a module variable, so an
  ordinary page refresh does not silently downgrade the editor to read-only. Session storage is
  origin-scoped and discarded with the tab, and any script able to read it could already have issued
  requests with an in-memory value, so this does not widen the boundary. The nonce is still never
  written to durable storage, a cookie, the container layer, or the repository.
- Trusting configured build commands is a distinct, explicit user action.
- Security tests must include Host/Origin rejection, CSRF, traversal, symlink escape, CSP, and
  non-root operation.

## Evidence

The startup brief fixes the local single-user boundary and these controls because filesystem and
command authority make a nominally local web process consequential.

## Rejected alternatives

- Bind the host port to all interfaces by default: this silently creates a LAN service.
- Add Tezuri accounts or SSO: identity is not the product boundary.
- Treat loopback as the only CSRF control: browsers can originate hostile local requests.
- Mount host credentials or the Docker socket: authority would exceed the task.

## Revalidation triggers

Any supported remote-access mode, collaboration feature, plugin execution model, or additional
mount requires a new threat-model decision before implementation.

