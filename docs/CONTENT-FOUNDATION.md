# Native content and service ownership

Current ownership reference; validation status belongs in [acceptance](ROSTER-COMPLETION.md). Earlier acquisition experiments and withdrawn drivers are historical, not alternative production paths.

## Runtime ownership

`src/NineSols/NativeCanvas.cs` owns YiGodhomeCanvas for Yi, imported terrain/receivers, rest and presentation. YiGodhomeServices retains the initialized native service graph after the original Horseman scene unloads. Native GameLevel remains the reset/revival service context. Preserve cached reset/start/destruction owner identities; scene unloading does not prove that service assets can be pruned safely. Do not reconstruct native engine services.

NativeContentCatalog defines typed extraction/preparation. NativeContentRepository serializes on-demand requests, owns inactive templates and explicit disposable leases, waits for donor unload and bounds retained templates/leases. NativeDonorManifest pins the supported build and admitted scene/type set; NativeDonorQuarantine suppresses exact lifecycle callbacks before donor activation and through pending cancellation/unload. Reject an unmanifested donor before loading it. Ordinary integration must not scan every scene/assembly or initialize donor gameplay to obtain art.

Independent content owners retain independent state. Immutable prepared attack templates cannot be rebound as live dealers. Verify native reset before pooling a new type; otherwise dispose it. Preserve native initialization of warning pools. Canvas ownership uses the native physics world; moving objects does not authorize a new physics model.

## Adding a content type

1. Identify the installed native emitter/asset, donor and dependency closure. Update the narrow reviewed manifest and build pin before admitting a donor.
2. Define exact extraction, inactive preparation and required native services. Keep gameplay callbacks quarantined.
3. Request asynchronously and gate use on readiness, including geometry changes during a live encounter.
4. Borrow bounded independent leases; handle cancellation, pending unload, disposal and capacity refusal.
5. Validate actual pixels, native timing, movement/reset, independent reuse and teardown in a separately authorized bounded fixture, then verify the consuming integration. Acquisition alone proves none of these.

Current content includes native warning/rage artwork and climb/wall trim. Imported collision remains separately authoritative; changing an image must not silently change wall physics or enable campaign hazards.

## Developer catalogue tools

`tools/effects/` contains offline catalogue/semantic/integrity tooling; native preview tools are development-only. Generated galleries, extracted art/audio, metadata and native captures stay under ignored local storage and never enter the release package. They are not an asset license or an admission manifest.

Use an explicitly provisioned Python interpreter and the prerequisites recorded in [SOURCES](SOURCES.md); inspect each tool's arguments before use. The native-library tooling accepts a caller-supplied game-data path and local output directory. Catalogue phases pin installed fingerprints; do not bypass them. Static previews do not reproduce native shader, timing, random audio, spatialization or full attack behavior.

Broad dependency audits are standalone, explicitly authorized and bounded. The canvas-service audit is restricted to its existing memory-ns/enemy-probe capability and120-second limit, never ordinary play. Public builds cannot activate developer capture or fixture startup.
