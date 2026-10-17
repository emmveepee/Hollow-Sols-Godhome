# October 14 — Hall garbage attribution and implemented reductions

This is dated engineering evidence, not boss acceptance. Current state: P14-GC01/P14-GC02 in [the register](ROSTER-COMPLETION.md). The user explicitly deferred extended gameplay validation to their next playtest; no new game launches or timed playtests were performed for these changes.

## What the completed sweep establishes

45 unique180-second workloads produced22 exact native combat collection calls across20 workloads. Exact native receipts remain authoritative: Nosk's call lies in a boundary window and would be missed by using only contained FRAME_PERF counters. The original data is preserved. Three sessions/builds are separate comparison groups, not a single controlled A/B.

The installed native collector checks a1024MiB managed-heap threshold. Failed samples have median initial contained heap343.75MiB and median positive two-second heap growth3.70MiB/s; clean samples321.03MiB and3.19MiB/s. These measurements are process-wide growth, not precise per-thread allocation bytes. Negative collection windows are excluded from growth medians, so burst/collection intervals cannot be reconstructed as total allocated bytes.

| Workload | Initial heap MiB | Median positive growth MiB/s | Native combat collections |
| --- | ---: | ---: | ---: |
| Pure Vessel |334.8|9.72|2|
| Vengefly, Attuned |292.5|5.61|1|
| Vengefly, Ascended |398.5|6.20|2|
| Nightmare King Grimm |345.4|5.59|1|
| Uumuu |337.8|5.13|1|
| Grey Prince Zote |346.8|4.86|1|
| False Knight |341.7|3.22|0|
| Failed Champion |335.6|4.05|1|
| Broken Vessel |258.5|3.58|0|
| Lost Kin |348.2|3.79|1|
| Soul Master |339.9|3.01|0|
| Soul Tyrant |372.0|3.32|1|

Correlations indicate both continuing allocation pressure and starting headroom, not one universal boss-family defect. Multi-body status alone is insufficient: Oblobbles has two bodies but no collection; Ascended Vengefly has two and collects twice. Collector summons alone do not explain failure either. Several nominally clean samples end near the threshold (Broken Vessel1008MiB, White Defender1018MiB, Soul Master1004MiB); a three-minute pass is not an indefinite-memory guarantee.

Retained scoped brackets point to different expensive paths. Pure Vessel contact capture averages approximately46,808 bytes of process-wide growth per sampled frame, compared with3,008 for Mawlek and4,142 for Mantis. NKG contact capture averages21,276; Uumuu11,514. Ascended Vengefly presentation averages9,557 versus1,047 for PV and910 for sampled Radiance. Presence commonly contributes5–12KiB per sampled frame. These nested brackets overlap and include concurrent worker growth: do not sum them or mistake them for exact allocation stacks. Sparse/rotated logs do not preserve complete fine-grained stage coverage for every workload.

## Implemented solutions

1. Shared pooled collision export retains immutable box/circle contours under exact matrix, offset, radius and box-size equality. Unchanged native pools no longer recreate their world-point arrays every frame. Changed shapes replace arrays; previous queued snapshots remain untouched. Dynamic polygons remain freshly sampled. Attack eligibility, native generation, decoration, warning windows, retirement and complete64-vertex contours are preserved. Bounded diagnostics verify reused coordinates against current native geometry and report reuse/build counts.
2. Pure Vessel decoration reads each native source name once per invocation instead of repeatedly across its tests. No name lifetime/rename assumption is introduced.
3. Host/roster/summon presentation ownership uses bounded synchronous lists instead of per-frame LINQ/array chains. BindBodies immediately copies into its own scratch; these lists never enter transport. Actual combat frames, target records and volume arrays remain independently owned and immutable. Summon validation reuses its one newly published snapshot instead of allocating a duplicate.
4. Actor draw-order strings are built only when numeric depth/layer/order/body-count changes. Uumuu/Flukemarm water ordering uses a direct live loop instead of sorting/allocating LINQ collections each draw. Native sorting/depth behavior is retained.
5. Live geometry preserves30Hz discovery and change detection. Refreshes borrow the fixed validated room-anchor floor instead of repeating an unused floor raycast, retain RoomColliderSource's already established native instance ordering, and sort dream-catcher scratch in place before publishing its independently owned result. Additions, removals, changing shapes and hazard identity continue to be observed.

These remove concrete recurring allocations in measured hot areas. They do not establish that all measured growth belonged to those individual arrays or that the target is now met. No native collector suppression, increased heap threshold, reduced image density, omitted live projectile, save change, or combat alteration was used.

## Checks and remaining evidence

Both native plugin builds passed with zero warnings/errors. Contact, Geometry, Visual and Hit suites passed. New warmed contour lookup test measures zero allocations across8192 lookups, checks exact key invalidation, preserved prior-frame arrays and isolated owners. Existing contact tests cover full64-vertex contours, independent activations, immutable receiver identity and actual pipes. Geometry tests cover live removal,1000 concurrent atomic replacements and Nosk normalization. These are automated/source checks, not an in-game collision or performance pass.

Both owned plugins were deployed with games closed and deployment hashes updated. No save was loaded, created, written or deleted. Existing user BaselinePlugin edits and conflict copy are preserved separately.

Unresolved: post-change collection frequency/FPS requires the user's extended playtest; Vengefly/Marmu burst allocations and intermittent NS hitches are not fully attributed. The Gruz881ms spike has gc=0 and approximately878ms in an incompletely bracketed plugin Update; it must not be relabelled a collection or claimed fixed by contour retention. Fine-grained object stack attribution is unavailable in the inspected release allocation counters. The early outgoing hold in the original sweep limits later-phase coverage, including Radiance rain/homing. Earlier dedicated Radiance workload evidence remains separate.

Reproduce analysis with tools/Analyze-HallGarbage.py against the preserved ignored completed-global-summary.json and authenticated fixture logs. Derived complete per-workload metrics and stage sample counts live in ignored.local/hall-endurance/gc-attribution.json. No game logs/assets/decompiled code are tracked.

## Broader follow-up — additional causes and implemented solutions

The first contour/presentation change was not the end of the audit. The entire45-row timing dataset was reprocessed, and the retained per-stage/pixel-pool evidence was compared with burst windows. Logs absent because of bounded rotation are explicitly missing coverage, not zero activity. A startup counter many fights earlier is not accepted as a fight-entry baseline.

### Actor-image burst correlation

| Workload | New HK actor buffers in sampled fight | >=20MiB heap-burst windows | Burst windows with new actor buffers |
| --- | ---: | ---: | ---: |
| Ascended Vengefly |52|17|16|
| Pure Vessel |14|8|6|
| NKG |15|6|5|
| Grimm |8|6|3|
| Sly |10|2|2|
| Hornet Protector |9|7|4|
| Failed Champion |0|1|0|

These align two-second counters, not exact allocation stacks. They establish an additional substantial burst contributor; Failed Champion proves that image churn is not the universal cause. The Ascended trace includes nine distinct actor byte lengths, from768-square to2560-square. NS capture summaries increase pixelAllocations831→969 and targetAllocations3→53 across its approximate combat window. Counts include producer leases; target recreation also costs native/GPU work, not just managed bytes.

The previous128MiB pixel cache evicted an entire least-recent size bucket to retain up to three copies of another. Repeated idle/large-arrow/recovery crop changes could evict recently useful sizes and allocate them again. The shared pool now trims duplicate free copies before sacrificing a size's last reusable free buffer, and declines an extra return rather than evict another size merely to keep duplicates. Exact lengths, maximum16 buckets/three free copies per size, retained128MiB budget and live-lease protection remain unchanged. New allocated/evicted-byte counters expose actual byte churn rather than counts alone.

The297-image offline full-size ownership test uses the observed nine tiers with a displayed image retained during reception. It allocates108 buffers versus the legacy policy's198 (approximately45% fewer), within the same retained-memory budget. The test initially failed an inappropriate zero-after-warmup expectation: two free buffers per tier exceed the128MiB budget. It was corrected to require a substantive reduction, unchanged budget and preserved displayed bytes. This is an actual C# pool test with a calculated legacy-policy reference; it is not a measured in-game FPS or GC comparison. Oversized working sets can still allocate.

NS GPU target eviction now favors recurring resolutions and uses recency to break ties. Actual capture uses update these frequencies; pending readback sources remain protected. Six-target capacity,2x render resolution, exact output dimensions, native selection and96px/world-unit bow-density rules remain unchanged. This source/build change has no claimed native performance pass.

### Continuous allocation and diagnostic paths

| Audited path | Finding and implementation |
| --- | --- |
| Legacy Xero/hazard contour export | Per-vertex float arrays and LINQ chains were created every capture. Reuse only local polygon/box/list scratch; publish one freshly owned final float array per changing contour. Native matrix/offset and point order remain exact. |
| Native FSM flames | The initial pooled-collider fix did not include this separate path. Extend exact immutable matrix/offset/radius/dimension contour retention to the native flame exporter; native FSM variables, action generations and counter retirement remain live. |
| Legacy body-state telemetry/PV hurtbox union | FSM diagnostic strings were formed every frame even when unchanged; PV built filtered hurtbox arrays. Compare existing managed state strings first, format only on transitions, and union live hurtbox bounds directly. No native state or damage eligibility changes. |
|30Hz geometry publication | Encoding repeatedly grew MemoryStreams, copied ToArray payloads, and allocated whole-file reads to compare unchanged data. Reuse a main-thread encoding stream and4096-byte comparison scratch; compare every existing byte under the original exchange lock. Publish written length rather than capacity. Oversized/truncated/trailing/changed data stays rejected or replaced according to the original bounded exchange contract. Live collider discovery and geometry-change cadence stay30Hz. |
| Geometry duplicate validation | Recreated rest/hazard HashSets on validation, including redundant pre-Write validation. Rest placements are bounded32 and checked directly; hazard IDs use bounded thread-local scratch cleared per invocation. Codec.Write still validates before publishing. Failure reuse and concurrent threads are tested; no shared escaping collections. |
| Recovery diagnostic logging | Ascended NS retained4,976 recovery-frame and4,976 animator-detail messages; HK retained2,055 recovery presentation messages. Rate-limit ordinary recovery telemetry to10Hz plus immediate state edges. Animator clip arrays/names are queried only for emitted samples. Native fade, recovery positioning, image publication and structured detailed capture remain unchanged. Synchronous recovery readback is preserved because safe fade/image timing was not reverified. |
| IPC packet construction | Reuse a worker-only input list; emit Array.Empty for no commands and a fresh owned array for commands. Main-thread queues and published packet ownership remain separate. Real-pipe ordered input/ack/late-frame tests pass. |
| Aura meshes, immutable contact/pose payloads, native pool discovery | Reviewed. Earlier aura evidence showed no warmed buffer growth; do not expand cache blindly or recycle asynchronous frame/receiver arrays. Native activation and hierarchy contracts remain protected. Required final immutable frame/target/point allocations remain. |
| Log flush/rotation and image/hit waiting | Possible non-GC hitch contributors, not proven causes of the Gruz881ms event. No speculative asynchronous logger rewrite. Add a bounded first-eight report for image-lease waits>=25ms and measure actual native hit dispatch in the existing hit_work bracket. No runtime scene inventories or new diagnostic authority. |
| Native engine allocations and retained cache headroom | Source scan finds no added production GC.Collect/ForceCollect override. HK's installed collector still runs its policy. Growing initial heap can include bounded pools and native retained content; these logs do not prove an unbounded leak. A clean3-minute sample near1024MiB remains borderline, not indefinite acceptance. |

This covers constant churn, burst churn, retained headroom and non-GC hitches separately. It does not reduce image fidelity, blindly cull inactive state contracts, increase memory allowances, defer collection indefinitely or change boss/player mechanics.

### Follow-up checks and delivery

Both final native plugin builds passed with zero warnings/errors. Visual, Geometry, Bridge and Contact suites passed. The new full-size pool test, allocation-free short-read streaming comparison, duplicate validation after failure/concurrency, and existing exact-pixel/native-input/contour contracts were exercised. The initial pool test failure described above is preserved here. Geometry comparison rejects changes across chunk boundaries and at the last byte, truncation and trailing data; warmed comparison/validation allocates zero managed bytes in the automated harness. Native Unity transform/cache/visual behavior still requires the user's playtest.

Both plugins were deployed with both games closed. No game launch, gameplay/save operation, new save, introduction or endurance test occurred. No user-source conflict was staged. Canonical performance state stays PARTIAL/post-change unverified. Remaining unassigned scope includes Marmu/Zote bursts where verbose logs rotated out, native engine allocation ownership, synchronous recovery GPU/readback cost, and the no-GC Gruz hitch. These remain explicitly distinguishable from the implemented reductions.
