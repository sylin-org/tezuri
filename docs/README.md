# Tezuri documentation

This tree separates current product truth from decision history, operator instructions, and dated
evidence. When documents disagree, the checked-in code/tests and accepted ADRs outrank old evidence;
update contradictory product or operations documentation in the same change.

[`MEMORY.md`](MEMORY.md) carries standing preferences and durable learnings, and indexes which
document owns which subject. Start there when you are not sure where something lives.

## Product truth

- [Product contract](product/PRODUCT-CONTRACT.md)
- [Content model](product/CONTENT-MODEL.md)
- [Sylin visual contract](design/SYLIN-VISUAL-CONTRACT.md)

## Architecture and decisions

- [Architecture map](architecture/README.md)
- [Workspace and publication](architecture/WORKSPACE-AND-PUBLICATION.md)
- [Threat model](architecture/THREAT-MODEL.md)
- [Architecture decision records](decisions/README.md)
- [ADR template](decisions/ADR-TEMPLATE.md)

## Operations

- [Development](operations/DEVELOPMENT.md)
- [Testing](operations/TESTING.md)
- [Release](operations/RELEASE.md)
- [sylin.org dogfood](operations/DOGFOOD-SYLIN-ORG.md)
- [Repository settings](operations/REPOSITORY-SETTINGS.md)

## Evidence

[`evidence/`](evidence/README.md) holds dated manifests and verification summaries. Evidence records
what happened; it does not silently extend the supported product boundary.
