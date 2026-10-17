# Technical references

These pinned references informed the existing design; they are not vendored code or execution instructions. User requirements and installed-build evidence take precedence. Consult the actual pinned implementation when a guide and code disagree. This consolidation does not re-audit upstream repositories.

| Reference | Reviewed guidance and adopted boundary |
| --- | --- |
| [Universal Modder](https://github.com/rehan-remade/universal-modder/tree/76b9c7e77ead5fd2d5f1b7613c6a7ed591b01c70) | Native authority, scoped verification oracles, timestamped presentation and comparable benchmarks. Reviewed mashup, Unity and oracle guidance; no external agent policies or scripts inherited. |
| [AI Game Modding Guides](https://github.com/trevaintdead/ai-game-modding-guides/tree/54a980970606f71996e6f25723d42fb9a95aaae5) | Passthrough/event-state separation, explicit crash behavior and testing. Shared C# contracts already supply one schema; generated cross-language code is not required. |
| [SkyCraft](https://github.com/chasmlol/SkyCraft/tree/bfcaf178524b92c2cdeb88e4ce0f13ef9ded6f32) | Native actor identity, collision back-channel and explicit mirror ownership; reviewed Combat.cpp, Link.cpp, Input.cpp, protocol header, ProxySync and MirrorWorld. No Skyrim damage inference, multiplayer/world loading or launcher behavior imported. |

The existing review records root MIT licenses and no copied implementation from these references; [THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES.md) owns notices and provenance limits. Linked game projects/assets do not inherit a guide's license. SkyCraft's dependency inventory is not Hollow Sols' dependency inventory.

## Deliberate departures

- Preserve bounded ordered pipes, exact identities and acknowledged damage. A full input ring in reference code can drop input; that is not permission to drop/replay uncertain damage here.
- Shared GPU textures or lockstep in another project's draft do not establish suitability for these two Unity builds. Measure current capture/transfer before changing them.
- Failure must not silently hand gameplay back to the hidden Knight. Preserve native authority, safe session termination and checkpoint recovery.
- Generic upstream backup/setup advice does not broaden this project's save boundary. No source reference authorizes native launches or deployment.

## Local analysis tooling

Recorded versions: UnityPy1.25.4, Pillow12.3, vgmstream r2117 and PresentMon1.10.0. These optional tools support local metadata/media/performance investigation, not normal mod execution or all .NET checks. Packages, extracted results, native IL and private captures stay excluded. Their versions/transitive terms need review before any redistribution; no new installation is authorized by this page.

Current code contracts are in [DESIGN](DESIGN.md), [combat](BOSS-COMPATIBILITY.md) and [content ownership](CONTENT-FOUNDATION.md). Historical per-boss source-application narratives remain in development Git history, not a current work queue.
