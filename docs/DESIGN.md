# Implemented architecture

This describes v0.1.0 source. [Acceptance](ROSTER-COMPLETION.md) owns validation status; [ROADMAP](ROADMAP.md) owns the planned v0.2.0 Option C work. Current Option B boundaries do not constitute a destination-neutral SDK.

## Components and data flow

Two separate Unity game processes run BepInEx plugins. Nine Sols owns Yi's native simulation; Hollow Knight owns its native world and encounters. Shared source is compiled into both plugins rather than running as a third bridge process.

| Component | Implemented responsibility and boundary |
| --- | --- |
| `src/Bootstrap/Entrypoint.cs`, `SessionTicket.cs` | Startup admission, protected diagnostic tickets and native-menu startup route. No game binary replacement. |
| `src/SaveGuard/Patcher.cs` | Supported installed-assembly fingerprints and early in-memory native save routing. Unsupported builds fail closed. |
| `src/Foundation` | Engine-free control/lifecycle, typed identities, paired store/coordinator, native file exchange and reporting store. It still embeds Hollow Sols roles, four-slot policy and HK authority. |
| `src/Shared/HollowSolsMenuPlugin.cs`, `HollowSolsSessions.cs` | Role-conditional composition of native menus, session operations, persistence and teardown. Shared source is not an adapter API. |
| `src/Shared/BaselinePlugin.cs` and other shared gameplay helpers | Gameplay composition, native integration, input/presentation and bounded diagnostics. |
| `src/HollowKnight` | Native geometry, boss/health/phase/encounter events, hit application, host UI, save selection and coordinator policy. |
| `src/NineSols` | Real Yi, native defenses/resources, imported terrain/receivers, native UI, retained services/content and image capture. It still understands HK scenes and boss families. |
| `src/Bridge`, `src/Geometry` | Source-linked codecs/endpoints, acknowledgements, identity/lease helpers and coordinate math, mixed with encounter/room/balance policy. Engine-independent helpers are reuse candidates, not a universal gameplay model. |

Control and gameplay/contact/hit/visual channels use local named pipes. Control queues are bounded and asynchronous; channel server ownership varies and does not define gameplay authority. Geometry and native-save bundles use bounded binary file exchange. HK exports geometry/enemy state; NS imports terrain/receivers, resolves Yi's native interactions and sends outcomes; HK applies its native hits and acknowledges them. NS pose/images/UI/audio are presented through the existing host integration.

## Component boundaries and wire compatibility

The following versions are read from current source. Both roles must match; there is no destination/capability negotiation.

| Contract | Version | Source |
| --- | --- | --- |
| Control | 6 | `Foundation/BridgeLifecycle.cs`, `ControlProtocol.cs` |
| Gameplay | 52 | `Bridge/Protocol.cs` |
| Contact | 38 | `Bridge/ContactFrame.cs` |
| Hit outcome / acknowledgement | 6 / 3 | `Bridge/HitOutcome.cs` |
| Visual | 15 | `Bridge/VisualFrame.cs` |
| Geometry | 7 | `Geometry/Snapshot.cs` |
| Paired store / native bundle / exchange | 1 / 1 / 1 | `Foundation/PairedSaveStore.cs`, `NativeSaveBundle.cs`, `NativeFileExchange.cs` |

Source paths above are relative to `src/`. Independent fixtures cover selected bytes; full compatibility requires both matching implementations. Do not copy protocol numbers into status histories as alternate supported layouts. Source cleanup does not authorize wire or persistence migration.

Native capture, simulation, lifecycle and result adoption stay on game threads. Workers handle immutable I/O/pixel work with exact identities and buffer leases. The gameplay worker's short wait is not deterministic lockstep. Pose/contact/image freshness, hit acknowledgements, peer silence and cleanup use separate deadlines. Unity scaled time, native movement time and monotonic/unscaled deadlines are distinct; host pause also stops independently stepped NS movement.

`CoordinateMap` translates and uniformly scales two-dimensional coordinates with positive scale. It does not support arbitrary orientation or 3D. Geometry scene authorization and runtime path checks remain game-specific safeguards. Temporary geometry publication contention uses bounded deferred retries; it is separate from durable save publication.

## Menus and sessions

Both native menus expose Hollow Sols Bridge. Current `MenuBridgeController.Receive` permits a ready counterpart to adopt an explicit Hello initiated by either game. One user activation starts the bidirectional handshake; mere process presence does not load a save. This is the implemented one-button flow, replacing the early two-button brief as recorded by the later foundation design.

HK exclusively selects/creates/deletes pairs and coordinates sessions; NS waits and follows. A connection can exist without gameplay. Role/version/activation identities fence the connection; each load adds a fresh session token, pair identity and committed generation. Encounter, target and strike generations fence later gameplay. Stale/foreign operations fail or are idempotently refused.

Session exit drains outcomes and finalizes paired persistence before returning to connected selection/waiting menus. Slot switching retains the connection with fresh session identity. Manual NS exit safely ends the active session. Vanilla selection ends/disconnects and restores native UI/runtime/persistence state. Unexpected peer loss stops dependent simulation and preserves committed data; fresh activation is required. A quit/unsafe native-return deadline cannot be held indefinitely by reporting or cleanup.

Native title controllers must be queried without creating missing singleton instances: an accidental native title Awake can clear runtime upgrades/audio. Loading cancellation must drain native scene work before reusing services; EOF is not proof of successful native return. UI retirement must not reactivate title controls during load. Readiness, native cutscene completion and input release are separate gates.

Initial save loading uses the native-style Bridging Yi caption and once-per-load inspection. Native scene transitions and statue sit/meditate/stand remain native-timed. Prepare images before reveal; never extend a fade to conceal late work. Controls borrows the real NS UI and capture/validation/settings logic while both games pause. Keyboard/mouse rebinding uses scoped host input leases; it must not leak into gameplay. Menus use each game's native styling and restore borrowed state at teardown.

HK slot enumeration, checkpoint preview reads and selected-save validation run on a bounded file-read worker (`DeferredRead`); the game thread continues pumping control traffic while the native menu displays a waiting caption. Native parsing, card construction, recovery warnings and session adoption remain on the game thread. At most one read runs and one replacement waits; cancellation, connection identity and request generation retire stale results without interrupting store leases. Preview bytes are scoped to the displayed slot objects; selected saves are validated again before loading. This does not change the peer-silence deadline, protocol or save formats.

## Saves and checkpoint ownership

Exactly four logical pairs live in the mod namespace, normally `%LOCALAPPDATA%/HollowSols/paired/HollowSols`. Previously initialized namespaces stay put. HK profile4 and NS index3 are private serializer contexts, not aliases for the four logical slots or permission to modify vanilla saves. Creation initializes upgraded Yi once; loading restores saved choices. Failed native loading must not fabricate a fresh baseline.

HK owns checkpoint coordination (`PairedSaveCoordinator`, shared session adapter). Native save requests join that coordination; neither side independently publishes half a pair. Both native payloads must be captured before an immutable synchronized generation is committed. Slot/pair/session/generation/transaction identities, hashes, durable flushes, manifest/commit ordering, expected-generation checks and store exclusion protect publication. Only immutable store I/O runs on the worker; busy/result fences prevent cancellation or a later session from adopting the wrong result.

Interrupted, missing or corrupt latest data recovers from the latest valid synchronized checkpoint, preserving original/unrecovered data and showing a visible warning. No valid checkpoint means safe refusal. Creation/deletion never serves as a recovery shortcut; confirmed logical-pair deletion retains conservative quarantine. This is not a promise of cross-process atomicity or universal power-loss survival.

Godhome unlock-all grants access without fabricated clears, bindings or rewards. Classic and experimental Steel Soul retain their warning; Steel Soul death follows native permanent-death handling and paired retirement. Campaign conversion and full challenge persistence are not inferred from mode launch support. Validation scope remains in the register.

## Gameplay, content and diagnostics

[BOSS-COMPATIBILITY](BOSS-COMPATIBILITY.md) owns collision, native damage/completion, counter timing, presentation and boss exceptions. [CONTENT-FOUNDATION](CONTENT-FOUNDATION.md) owns YiGodhomeCanvas/YiGodhomeServices, donor quarantine and content leases.

[BUG-REPORTING](BUG-REPORTING.md) owns the public/development profile and optional failure exports. `Shared/FailureReporting.cs` integrates native notices with `Foundation/FailureReports.cs`; report storage is separate from paired saves. It contains no automated upload or GitHub credentials. [RELEASE-PREPARATION](RELEASE-PREPARATION.md) owns source-matched packaging and exclusions.
