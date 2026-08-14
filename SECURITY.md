# Security policy

## Supported versions

Tezuri has no released version. Security fixes currently target the latest `main` revision only;
development snapshots receive no compatibility or patch-support promise. This table will be replaced
with explicit release lines before 1.0.

| Version | Supported |
| --- | --- |
| Public releases | None yet |
| Latest `main` | Best-effort during development |

## Report a vulnerability privately

Prefer a [private GitHub security advisory](https://github.com/sylin-org/tezuri/security/advisories/new).
If that channel is unavailable, email `hello@sylin.org` with “Tezuri security” in the subject. That
address is the current public security contact published by sylin.org.

Include the affected revision/version, environment, impact, minimal reproduction, and any proposed
mitigation. Do not include live credentials or unnecessary private repository/article contents.
Please do not open a public issue until the report has been assessed and coordinated disclosure is
safe. We will acknowledge and investigate in good faith, but this volunteer project does not promise
a response or remediation SLA.

## Scope

Tezuri is local, single-user software. It binds loopback only, requires a per-launch nonce for every
mutation, and writes only inside the repository you chose. The boundary and the decisions behind it
are in [`docs/DECISIONS.md`](docs/DECISIONS.md) and [`README.md`](README.md).

Reports are particularly useful for workspace or symlink escape, DNS rebinding or CSRF bypass,
unsafe imported markup, arbitrary command execution, credential leakage, Git history damage, and
dependency compromise.

Tezuri is intentionally a loopback single-user tool, not an authenticated multi-user service. Do
not expose its loopback port to a LAN or the public Internet. Social engineering, unsupported public
deployment, and vulnerabilities that require a user to deliberately replace trusted `tezuri.yaml`
with an untrusted executable configuration may fall outside the supported boundary, though defense-
in-depth reports are still welcome.

