# Architecture

Tezuri is one local process with explicit layered boundaries:

```text
Browser client
    │ versioned HTTP source/media/proof/publication contracts
Tezuri.App (Koan host, controllers, nonce/origin boundary, composition)
    │ app-owned interfaces and records
Tezuri.Infrastructure (guarded filesystem, process, import, Git, target adapters)
    │ canonical relative paths and byte operations
Mounted repository at /workspace (sole content authority)
```

`Tezuri.Domain` has no dependency on ASP.NET, Koan, the filesystem, Git, a target site, or the chosen
editor. `Tezuri.Infrastructure` may depend on Domain and trusted configuration. `Tezuri.App` composes
the layers and hosts the bundled client. The client speaks Tezuri protocols; Milkdown is an
replaceable view adapter rather than a storage model.

The host uses `builder.Services.AddKoan()` and module discovery. Tezuri registers an
`IKoanWebPipelineContributor` at `BeforeRouting` for its strict local Host/Origin/nonce boundary and
security headers. This is Koan's supported ordering seam; Program-level middleware would otherwise
sit after its mapped controller endpoints.

Read [workspace and publication](WORKSPACE-AND-PUBLICATION.md), the [threat model](THREAT-MODEL.md),
and the [ADR index](../decisions/README.md) before changing authority or boundaries.
