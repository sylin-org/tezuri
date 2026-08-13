# Tezuri documentation

This tree separates current product truth from decision history, operator instructions, and dated
evidence. When documents disagree, the checked-in code/tests and accepted ADRs outrank old evidence;
update contradictory product or operations documentation in the same change.

## Product truth

- [Product contract](product/PRODUCT-CONTRACT.md)
- [Content model](product/CONTENT-MODEL.md)
- [Sylin visual contract](design/SYLIN-VISUAL-CONTRACT.md)

## Architecture and decisions

- [Architecture map](architecture/README.md)
- [Workspace and publication](architecture/WORKSPACE-AND-PUBLICATION.md)
- [Threat model](architecture/THREAT-MODEL.md)
- [Public contract catalog](contracts/README.md)
- [Article and media profile](contracts/ARTICLE-AND-MEDIA-PROFILE-V1.md)
- [API and compatibility profile](contracts/API-AND-COMPATIBILITY-V1.md)
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
