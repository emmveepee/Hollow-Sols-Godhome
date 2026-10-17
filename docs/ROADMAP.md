# Planned v0.2.0 — Option C

**Planned, not implemented.** v0.1.0 retains the current architecture with limited Option B preparation. The player intends v0.2.0 to establish a more explicitly reusable bridge architecture. This document records direction, not authorization to begin refactoring or a promise of another supported game.

## Intended boundaries

| Layer | Intended responsibility |
| --- | --- |
| Nine Sols integration | Native Yi simulation/abilities, lifecycle and content/UI adapters; expose the needed capabilities without unnecessarily understanding a destination's scenes/bosses. |
| Shared bridge infrastructure | Transport/framing, compatibility/identity, bounded queues/leases, synchronization mechanisms and reusable failure handling. |
| Destination integration | Translate native world/combat/geometry/UI and supply destination session/persistence policy. Hollow Knight remains the first adapter. |

These are conceptual layers. Separate repositories, executables or assemblies are not predetermined. Extract proven mechanisms incrementally; a universal combat model or generic plugin framework requires demonstrated need. Current Foundation is engine-free but still carries Hollow Sols policy; Bridge/Geometry/Shared also mix mechanisms with HK assumptions.

## Contracts to preserve

- Native Yi and native destination authority, accepted fights and current visual/input timing.
- Hollow Sols' HK-owned four-pair policy, NS follower behavior, save isolation, checkpoint identities, conservative recovery and visible warnings.
- Public/development diagnostic separation, bounded ordinary reporting and no automatic uploads.
- Existing protocol/save compatibility where practical. Any unavoidable break needs explicit versioning, old-data preservation and a reviewed migration/rollback strategy.
- GPL-3.0-or-later for project-owned material and independent distribution without proprietary game material; existing provenance questions still apply.

## Decisions for the future audit

Inventory actual HK dependencies on the NS side; identify a small common capability/event set and the policies that must remain adapter-owned. Separate session mechanisms from authority policy without generalizing native save serialization. Decide whether internal interfaces suffice or a separately consumable library is justified by a concrete second integration. Measure any additional copying, dispatch, latency and allocation.

Before implementation, verify repository state, map source dependencies, specify compatibility tests and propose small reviewable extraction phases. A second destination, plugin discovery, arbitrary 3D coordinates and cross-game universal combat are not committed v0.2.0 features. Do not schedule this work as a v0.1.0 release prerequisite.
