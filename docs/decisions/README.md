# Architecture decisions

These records capture durable Tezuri boundaries. `Accepted` decisions are fixed by the product
contract. `Proposed` decisions still require the evidence named in the record before implementation
may depend on them.

| ADR | Status | Decision |
| --- | --- | --- |
| [0001](0001-repository-files-are-authoritative.md) | Superseded by 0015 | Repository files and Git are authoritative; article databases are not. |
| [0002](0002-lossless-markdown-frontmatter-protocol.md) | Superseded by 0015 | Markdown and YAML frontmatter use a non-destructive editing protocol. |
| [0003](0003-rich-editor-boundary-and-milkdown-spike.md) | Superseded by 0015 | Keep the editor behind an app-owned document boundary; evaluate Milkdown by spike. |
| [0004](0004-folder-per-article-and-owned-media.md) | Accepted | Each article owns a folder and its published media. |
| [0005](0005-local-container-security-boundary.md) | Accepted | Protect the loopback application with origin checks and a launch nonce. |
| [0006](0006-target-site-proof-is-authoritative.md) | Accepted | The target site's build, not Tezuri's editor preview, proves publication. |
| [0007](0007-git-publication-and-credential-delegation.md) | Accepted | Publishing is explicit Git work with narrowly delegated credentials. |
| [0008](0008-oci-distribution-and-release-boundary.md) | Accepted | Ship a verified public multi-architecture OCI image from explicit releases. |
| [0009](0009-koi-recipes-are-optional-host-operations.md) | Accepted | Koi LAN, TLS, and UDP recipes remain optional outer-host operations. |
| [0010](0010-substack-import-is-manifested-and-complete.md) | Accepted | Substack import is inventory-complete, fidelity-preserving, local, and manifested. |
| [0011](0011-sylin-semantics-without-runtime-coupling.md) | Superseded by 0014 | Tezuri inherits Sylin semantics without a website runtime dependency. |
| [0012](0012-versioned-restricted-workspace-configuration.md) | Accepted | A versioned restricted YAML contract grants repository-local capabilities. |
| [0013](0013-contract-authority-and-compatible-evolution.md) | Accepted | Public schemas own serialized contracts and evolve explicitly. |
| [0014](0014-sylin-workstation-design-language.md) | Accepted | Tezuri implements the Sylin workstation dialect from pinned tokens. |
| [0015](0015-article-entity-is-canonical-markdown-is-generated.md) | Accepted | The article entity is canonical JSON; Markdown is generated one way. |
