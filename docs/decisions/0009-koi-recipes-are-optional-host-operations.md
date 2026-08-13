# ADR 0009: Koi recipes are optional outer-host operations

- Status: Accepted
- Date: 2026-08-13

## Context

Koi can provide LAN discovery, TLS/trust, and UDP/network recipes around local services. Those
capabilities may improve a future operator-managed environment, but Tezuri V1 is a loopback-only
single-user container and must not acquire ambient network or host-trust authority.

## Decision

Koi LAN, TLS, and UDP recipes are optional outer-host operations. They are not Tezuri V1 runtime
dependencies, are not bundled into the container, and are not invoked by Tezuri startup, proof, or
publication. If documented later, they run explicitly in a trusted host/operator workflow with
their authority and rollback shown before execution. Tezuri remains fully useful with loopback port
mapping and no Koi installation.

## Consequences

- V1 needs no multicast/UDP permission, host trust-store mutation, privileged networking, or Koi
  control plane.
- Container tests remain deterministic and offline-capable.
- A future recipe can wrap Tezuri without changing repository content or the application protocol.
- Remote access remains out of scope under ADR 0005.

## Evidence

The current product contract requires loopback-only access, no silent network traffic, and the
smallest real Kintsugi workflow. Koi recipes solve an outer deployment concern rather than an
editor, proof, or Git publication requirement.

## Rejected alternatives

- Make Koi mandatory for local startup: it adds network setup before the first useful result.
- Let Tezuri mutate host TLS or firewall state: the container would exceed its declared authority.
- Bundle UDP discovery by default: it contradicts loopback-only V1 behavior.

## Revalidation triggers

Revisit only with an explicitly supported remote/LAN mode and a threat-model update covering
identity, authorization, discovery exposure, trust installation, revocation, and rollback.

