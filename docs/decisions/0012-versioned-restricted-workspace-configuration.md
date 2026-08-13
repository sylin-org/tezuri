# ADR 0012: Use versioned restricted workspace configuration

- Status: Accepted
- Date: 2026-08-13

## Context

Article paths, metadata, owned-media limits, target URL, Proof commands, and Git publication paths
belong to the mounted repository. Configuration must remain human-editable while also granting
sensitive filesystem/process/publication capabilities. A general YAML object graph or raw shell
command would unnecessarily expand the attack and compatibility surface.

## Decision

V1 uses committed root `tezuri.yaml` with discriminator `tezuri.workspace/v1` and a public JSON Schema.
The application parses a deliberately restricted dependency-free YAML subset: mappings, sequences,
and bounded scalars needed by the schema. It rejects unknown keys, duplicate keys, aliases/tags,
flow values, block scalars, document markers, tabs, excessive size/depth, and unsupported constructs.

Paths are portable repository-relative values validated again through canonical containment at use.
Proof declares executable plus argument array, timeout, working/output directories; shell interpreters
and browser-supplied commands are forbidden. V1 requires owned media and explicit allowed Git paths.

## Consequences

- Configuration is deterministic, reviewable, and portable without a YAML runtime dependency.
- Some valid general YAML syntax is intentionally invalid Tezuri V1 syntax with actionable errors.
- Schema, typed contract, parser, validator, sample, and tests must evolve together under a new
  version for incompatible changes.
- Reading configuration grants no automatic command trust or publication action; first execution
  still requires a visible human decision.

## Evidence

The implementation includes the V1 schema/sample and seven parser/validator/loader tests covering
valid configuration, unknown/advanced YAML, unsafe paths, shell interpreters, and size boundaries.

## Rejected alternatives

- Raw shell command strings: unsafe transport and quoting boundary.
- Generic YAML deserialization: accepts features and object shapes V1 does not need.
- Hard-coded sylin.org paths: prevents a legible reusable file-native contract.
- Browser/local-storage configuration: not versioned with or recoverable from the repository.

## Revalidation triggers

Revisit when a real target needs unsupported YAML ergonomics or fields, when schema vocabulary alone
cannot express required constraints, or when a maintained parser demonstrably reduces risk without
expanding accepted syntax accidentally.

