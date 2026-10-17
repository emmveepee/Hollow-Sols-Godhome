# October 14 — Radiance rain and homing workload performance

This is dated engineering evidence, not whole-fight acceptance. Current issue states remain in [ROSTER-COMPLETION](ROSTER-COMPLETION.md), especially P13-AR05, P14-AR07 and UI14.

## Scope and safe setup

Both games launch concurrently. Tests reuse disposable copies of the existing Hollow Sols pair, skip creation/introduction, and end as soon as native Hall return, outcome drain and paired saving complete. All 26 vanilla/development file fingerprints were unchanged. Original paired saves were not modified. No release/tag was created.

Authenticated bounded fixtures select the statue normally, then request exact audited native Rage1 Tele (rain) or Stun1 Start (platform transition) states. They do not write boss HP or phase variables, infer victory, or certify natural whole-fight progression. Platform placement uses the highest actual authored climb platform. Actual native arrows must hit; native ammunition/damage remain unchanged.

The user explicitly authorized debug invulnerability for center-idle rain. That fixture places Yi at the authored center and native ground, holds every control neutral for 120 seconds, and verifies unchanged boss HP/no reflected or bow damage. A native Health invulnerability vote expires after 135 seconds or on room/fixture exit and is removed before cleanup/saving. It is unavailable to ordinary play. Screenshots occur after timed markers, not inside the measured window.

## Reproduced causes and changes

Homing native arrows repeatedly changed expanded capture dimensions, defeating exact-size reuse and recreating GPU targets. Baseline dcdcc199 produced 23 NS frames above 25 ms and 38 full collections in contained firing windows. The retained path holds expanded dimensions across a three-second arrow burst, reserves the existing maximum tier for homing, and grows camera bounds to preserve the original pixel density and all selected native effects. Entry/paused native UI prepares one maximum target and three distinct reusable byte buffers. Preparation never publishes images or changes session identity.

A queued actor image now prevents redundant new GPU renders while that channel is busy. Native input/physics and the existing completed-image/metadata checks remain independent. This retains existing latest-frame delivery behavior; it does not imply 144 image updates per second. Two pinned readbacks, bounded pools, unchanged 2x supersampling, native materials, effects and density remain.

Rain uses exact renderer membership and cached native component identities for aura admission. Native source/owner equality was checked against the previous selection algorithm. Static nail classification is retained per renderer; attack components refresh on native pool activation or direct reparenting. Motion and FSM state still read live. Geometry scratch lists/box points are reused; final contact arrays remain immutable across transport ownership. No attacks, colliders, outlines, warnings or physics updates are removed to improve frame time.

## Measurements

All means are weighted across wholly contained two-second windows. Worst-window p95 is not an invented aggregate percentile. First partial boundary windows are separate; native scene entry/return costs outside markers are excluded, not hidden. `tools/Measure-RadiancePerformance.py` derives ignored JSON from session-validated local evidence. Baseline vanilla NKG steady mean was 6.946 ms (about 144 Hz).

| Workload / capture | HK mean / worst frame | NS mean / worst frame | Frames >=25 ms, HK / NS |
| --- | --- | --- | --- |
| Initial platform homing dcdcc199 | 7.069 / 32.596 ms | 7.641 / 64.832 ms | 1 / 23 |
| Retained prewarm/backpressure homing 26fa2a39 | 6.950 / 13.790 ms | 6.957 / 25.025 ms | 0 / 1 |
| Final active platform homing 92bc9435 | 6.950 / 14.002 ms | 6.946 / 10.902 ms | 0 / 0 |
| Final active crimson rain 501a4275 | 6.954 / 13.251 ms | 6.958 / 26.658 ms | 0 / 1 |
| Short actual crimson rain b2183c07 | 6.946 / 12.498 ms | 6.946 / 10.770 ms | 0 / 0 |
| Initial center-idle rain, 120.007 s, bd3ea8af | 6.954 / 64.565 ms | 6.946 / 11.934 ms | 2 / 0 |
| Cached component/scratch repeat, 120 s, beea9572 | 6.951 / 64.952 ms | 6.946 / 11.421 ms | 1 / 0 |

Homing 26fa2a39 recorded 13 native arrow hits and stable nine pixel allocations/four targets through firing. First boundary window maxima were 22.390 ms HK / 15.566 ms NS. Maximum-tier preparation took 29.303 ms while entry was held, and 3.034 ms on held return.

The long-idle repeat contained 59 windows and 16,981 HK frames. Worst-window p95 was 7.322 ms HK / 7.596 ms NS. HK heap growth fell from roughly 16 MiB/s to 9.4 MiB/s; sampled decoration heap deltas fell to zero in steady rain. One full collection remains, producing a 65 ms hitch. NS remained below 12 ms throughout contained windows. The first partial window was below 15 ms in both games. Both extended samples maintained actual crimson blades, unchanged boss HP and neutral controls.

## Honest limits and failed attempts

The remaining HK collection is real and is not declared fixed. Its long frame is in native Update outside the measured plugin scope; a process-wide collector can collect allocations from both native and mod work, so that alone does not prove vanilla is responsible. Escaping contact snapshots still allocate. Do not recycle them before transport ownership ends or force collections during combat to conceal this result.

Maximum-tier raw images are 25 MiB and sampled delivery round trips remain around 50–57 ms (roughly 18 images/s). Engine frame time matching NKG does not certify visually smooth bow motion. This throughput limitation and full in-motion grading remain UI14/P14-AR07 scope. No full-fight comparison, natural phase progression, every arrow/difficulty, or hours-long endurance session was run.

Two initial platform fixtures failed setup: a5ea133c rejected the unaudited room-placement guard; 4f691e22 selected the lowest climb platform and never entered platform combat. Both were corrected against observed native contracts, never passed off as performance results. A sparse-zero checksum experiment passed byte-equivalence tests but showed no meaningful native transfer improvement; it was reverted. Its b78b5e97 timings are not the shipped result.

Visual and Contact suites passed, including native rain traits/independent retirement, fixed counter damage/no injury, malformed wire rejection, exact pixel/lease ownership, crop containment/density and warmed reuse. Both plugin builds passed with zero warnings/errors. Final92bc9435 platform combat passed14 native bow hits, native Hall return/drained saving and unchanged fingerprints. Its first partial firing boundary had one26.277ms NS frame (HK16.319ms); the contained-window table does not erase that cost. Wholly contained no-arrow means were6.946ms in both games, maxima9.648ms HK/10.043ms NS. End screenshots were inspected for actual native scene/actor/HUD; stills do not grade animation cadence.

Final501a4275 active rain passed14.004 seconds/2,014 actual blade frames, native Hall return, drained saving and unchanged fingerprints. It uses native movement/parry inputs rather than the idle immunity lease. Contained window p95 maxima7.400ms HK/7.977ms NS; one26.658ms NS frame remains. Including cold attack setup gave HK26.231ms and an initial partial NS38.787ms window. These are retained as costs, not declared smooth by excluding their context. Native rain counter reflection timing was not certified by this performance fixture. Contact/Hit/Visual suites passed; both final builds were deployed and owned deployment hashes were checked.


## Follow-up — visible crimson rain and floor retirement

The player correctly noted that the earlier performance screenshots lacked crimson blade outlines. Those requests were immediately followed by native return/cancellation, before Unity's end-of-frame screenshot render. They establish scene/actor presence but cannot validate active aura appearance. The fixture now retains attack owners for two render frames after its timed END marker, then begins native return. This changes diagnostic capture timing, not attack/outline timing or normal gameplay fades.

Initial visual retry cee6ffef failed because Yi died in the moving rain fixture before completion; it is not a passing visual result. The explicitly authorized native invulnerability lease was reused in a short eight-second `combat-RadianceRainVisual` fixture, avoiding another120-second benchmark or fresh save. 16bb9a97 passed neutral center placement, unchanged boss HP, actual rain, vote removal, native Hall return/drained saving and unchanged26 vanilla fingerprints. Its inherited marker/image name says `rain-idle-extended`; its actual elapsed window is8seconds and is not presented as a new endurance pass. The ignored `HollowKnight/radiance-idle-extended.png` was visually inspected: native falling blades and body have the accepted black-inner/red-outer crimson effect. Ordinary blade emissions still receive no rain-only outline.

Radiance nails now use the existing pooled outside-envelope omission path. The floor comes from the current session/room/geometry's validated contiguous flat standable terrain; missing/foreign geometry disables culling. Only whole native hitbox and artwork bounds below floor minus0.01 are omitted. Polygon generation/export and crimson deformation/draw stop; native pool lifecycle and identity/acknowledgement checks remain. Cached retirement invalidates on native activation/reparenting and above-floor samples rearm, preserving native reflection/reuse authority. No object deletion, forced recycling, HP change or camera-dependent cutoff is used. Grimm culling remains unchanged.

The native witness recorded16 blade hitbox tops at19.893 below floor20, with continuing visible crimson rain above ground. Visible aura counts sampled33/17 rather than the earlier49/65; these are partial native counts, not a claimed clean CPU A/B. Eight-second contained-window means were HK6.954ms/NS6.946ms; maxima12.656/10.556ms and no25ms spikes. This short pass does not prove the prior65ms endurance collection has disappeared. Above-ground counter timing, returning reflections from below the floor, other phase geometry and full-fight acceptance remain unverified. Contact/Geometry suites and both builds passed. No original saves were touched.


Final artwork-inclusive cutoff e311aaf3 passed8.002seconds/1,151 actual blade frames, unchanged boss HP, native immunity removal, drained/saved return and26 unchanged fingerprints. It waits for both artwork and hitbox to clear the floor: sampled effective top19.94225, collider top19.00525, floor20. Native crimson blade/body screenshot was inspected again before teardown. Contained means HK6.954ms/NS6.946ms, maxima12.644/10.834ms, zero25ms spikes. Earlier16bb9a97 was the hitbox-only version; the final cutoff is more conservative about still-visible artwork. No new120-second endurance claim is made.


## Post-culling 120-second endurance — 3a05ca42

The requested repeat ran on deployed f63bc27 with the artwork-inclusive floor culling. It reused the existing disposable paired save, centered Yi with the authorized finite native immunity vote, kept controls neutral/no attacks and verified unchanged boss HP. Completed120.007seconds/17,267 actual blade frames, captured visible crimson rain before teardown, removed the native vote and finished native Hall return/drained saving. All26 protected vanilla/development fingerprints stayed unchanged; original mod-owned saves were untouched. Both games closed on completion.

| Wholly contained endurance windows | HK | NS |
| --- | --- | --- |
| Windows / frames | 59 / 16,982 | 59 / 16,992 |
| Weighted mean | 6.950ms | 6.946ms |
| Worst-window p95 | 7.329ms | 7.566ms |
| Worst frame | 61.511ms | 12.778ms |
| Frames>=25ms | 1 | 0 |
| Full collections | 1 | 21 |
| Per-window mean range | 6.945–7.119ms | 6.945–6.948ms |
| Windows averaging>=8ms | 0 | 0 |

There were no sustained or repeated dips in this sample. The one remaining HK hitch occurred around108seconds and coincided with a full collection. Native frame scopes measured Update56.609ms, plugin1.546ms, contacts0.698ms and aura1.099ms on that event; the CPU probe's59.140ms is distinct from FRAME_PERF's61.511ms whole-frame maximum. This remains a process-wide collection, not proof that only native game code allocated. HK heap growth is now approximately7MiB/s (earlier roughly16, then9.4), rising to1015.91MiB before collecting to185.06MiB. No accumulating retained-heap leak was demonstrated. First partial boundary windows max22.596ms HK/17.224ms NS; no25ms spikes there. The final screenshot again visibly shows black/red rain blades and body.

No additional gameplay code was changed: the conditional sustained/repeated dip case did not reproduce. The isolated collection is explicitly retained as a remaining limitation; it is not called fixed or hidden by transition timing. This verifies this120-second rain workload, not every phase, counter timing, moving combat or the separate maximum-image homing cadence limit. Current AR05/P14-AR07–08 states remain bounded samples/partial scope. Existing builds/checks are retained evidence rather than newly rerun with unchanged code.


## Allocation mitigation and installed vanilla GC policy — 4264e4b0

The user asked whether to make less garbage or defer collection until fades. Read-only offline inspection of the installed game and engine assemblies found the native GCManager policy, with private IL evidence under ignored.local/gc-audit. No game assembly was modified. This build's non-incremental path disables Unity's automatic GC and implements its own memory-threshold-driven blocking collection, plus explicit collection calls from native scene loading, menu return and freeze-moment routines. The manager can collect during combat; it has no general combat-exclusion gate. Its initial threshold is selected from machine RAM and clamped; runtime4264e4b0 explicitly reported mode=Disabled, incremental=False, threshold1024MiB. This explains the prior roughly1GiB threshold hitch; it does not imply every allocated byte came from vanilla. Other versions/builds can differ.

Preserved that native policy, threshold, transition calls and memory safety behavior. No fight-wide suppression, no threshold increase, no forced extra collections, and no attempt to enable unsupported incremental collection. Instead, volume/summon validation now retains separate thread-owned scratch sets containing at most128/32 primitive IDs. Every invocation clears scratch, including after errors; no frame, receiver or vertex arrays are retained. Repeated native object-name marshaling in pooled sword classification now follows the existing activation/damage/hazard classification stamp. Destroyed-source cleanup reuses an owned scratch list. Escaping contact snapshots remain immutable; no transport ownership was relaxed.

New tests passed warmed maximum-size validation with zero allocated bytes, duplicate rejection after reuse/failure, four concurrent callers and unchanged input data. Existing complete Contact, Hit and Visual suites passed; both plugin builds passed with zero warnings/errors. Final owned deployment and original assembly hashes were checked. Vanilla comparison here is the inspected installed policy, not a newly run vanilla fight.

Final existing-copy4264e4b0 passed120.007seconds/17,273 actual stationary-rain frames with neutral controls, unchanged boss HP, authorized finite native immunity removed before completion, visible crimson blades/body, native Hall/drained paired saving and26 unchanged protected fingerprints. The actual screenshot was inspected. Native collector policy remained unchanged.

| Contained two-minute windows | HK | NS |
| --- | --- | --- |
| Windows / frames | 59 / 16,988 | 59 / 16,993 |
| Weighted mean | 6.948ms | 6.946ms |
| Worst-window p95 | 7.320ms | 7.567ms |
| Worst frame | 18.329ms | 13.964ms |
| Frames>=25ms | 0 | 0 |
| Full collections | 0 | 19 |

HK heap growth fell from approximately7 to5.3MiB/s, about25% lower. Contained samples grew289.75→900.33MiB and stayed below the unchanged1024MiB threshold, avoiding the previous61.511ms event in this repeat. First partial boundary windows max9.295ms HK/11.241ms NS, no25ms spikes. No sustained/repeated dips appeared.

This supersedes the earlier remaining-hitch claim for the measured120-second rain workload. It does not promise collection-free arbitrarily long fights: allocation continues, native memory reclamation can still occur when necessary, and native transitions continue to collect. It also does not certify moving counter timing, every cached source family or homing image cadence. Existing user saves and the unrelated user source edit/conflict copy remain untouched. No public release/tag.
