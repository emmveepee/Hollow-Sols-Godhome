# Performance — current acceptance and measured correction

## October17 — original friend-build baseline

[Sessiond7eb5fdc review](OCT17-FRIEND-BASELINE-OBSERVATIONS.md) records retained HK/NS frame-weighted means6.989/7.038ms with isolated Sisters-room roughly85–95ms spikes in both processes without GC, and separate larger startup/return maxima. Coverage is unequal and rotated, not a complete-hour benchmark. Background checkpoint publication rises91→392ms, HK preparation stays<=0.230ms, and separate NS native capture reaches114.882ms. These old-build observations establish neither a new cause nor updated-build success/failure, and do not reopen accepted mechanics or prove a leak. Preserve for correlation if the updated friend build reproduces visible hitches.

## October16 — Broken Vessel post-rollback pacing investigation

Canonical P16-BV03 is UNCONFIRMED, not a performance-fix claim. The user explicitly authorizes moving on when the logs do not demonstrate a cause. No further test is required by this investigation. Pairedaf22d75e matches the rollback module94b6d82b; compare outlinedf1a469de, both preserved in ignored `.local/oct16-vessel-performance`. Earlier outlined HK drops to205frames per two seconds/p9513.562ms during shake. Post-rollback recorded shakes at01:31:10.7–22Z retain roughly144FPS in both games, HK max11.404ms and NS max19.763ms with no25ms frames in those windows. This does not certify good frame pacing: the player reports slow-motion feel despite FPS holding. Actor image delivery averages84–90Hz during shake versus82Hz before it; pose delivery remains227Hz. Native Hurt receipts at14.770/15.317/15.811/20.617/22.117/22.916Z correlate with NS Unity/gameplay clock0, while rendering continues. HK captures new body-outline poses through the first shake; its12.303ms aura maximum spans the lifetime and cannot be assigned to one pose from the current logs. NS56.564ms collection at29.4Z is later than the two recorded shake windows.

Existing collision-stage costs previously logged only Grimm/NKG. BV/LK now also emit two-second summaries for placement/query/sync and fixture creation, plus once-per-render-frame wall/Unity/native/unscaled clock totals, frames over9/12ms, frozen frames and bridge-held frames. This separates real computation/creation spikes from native hit-stop and clocks falling behind wall time. No native time-scale/hit-stop/collision changes or projectile outlines are introduced. NS build and Contact suite pass; no new native run. If a future report supplies new evidence, the retained counters can distinguish clock pauses from collision/creation cost. No current follow-up playtest is required.

## October16 follow-up — revival world-matte allocation burst

Latest paired40162495 and both rotated logs are preserved under ignored `.local/evidence/revival-followup-20261016`. Three procs occur19:25:49.094,19:28:08.374 and19:29:50.181Z. First two windows have no frames over25ms. Third NS window reaches74.607ms, three frames over25ms and five collections; HK remains smooth at the event. Prior HUD correction improves full-screen worker resolve to roughly31–37ms and total send around48ms; its pool counters remain flat.

The separate world-effects stream carries the revival orbs. Between19:29:46.210 and19:29:51.753 its counters increase169→197 buffers and525598732→887095308 allocated bytes (361496576bytes,344.75MiB), with no new capture targets and61.089ms maximum capture cost. Actor counters stay flat. This correlates a remaining mod allocation burst with the residual stall; it does not prove all five collections or the entire74.607ms originate there. Installed native revival contract spawns the health drops rather than saving or restoring a scene. Acquired weapon enumeration is a native list field, not an allocating getter; no speculative arrow-provenance rewrite was made.

World capture permits two asynchronous dual-matte requests: four GPU destination arrays, two pending and two worker-owned. Its former three-free cap discards recurring buffers. Only the arrow/world channel now keeps up to eight arrays per exact size, under the original128MiB free-byte budget. HUD keeps six; other channels keep three. No resolution/opacity/native time-scale changes or GC deferral. Very large mixed working sets still exceed the budget; do not promise allocation-free capture at every size. Proc-scoped `NS_REVIVAL_WORLD_PRESSURE` records start/settled allocation/eviction deltas even when the stream becomes empty before its90-image cost window fills.

VisualChecks reproduces eight-lease churn with three and six retained arrays, and exercises the actual endpoint at1536px over120 bursts with no new warmed buffers, aliasing or outstanding leases.2560px returns obey128MiB. Existing exact colour/coverage/pipe checks and ContactChecks pass; both builds have zero warnings/errors. Deployed through audited updater with games closed. No post-change native playtest or save changes. Canonical PERF01 remains PARTIAL; separate PV PERF02 remains unresolved.

## October16 — revival HUD pressure and preceding Pure Vessel

Paired session18f8a481 and both rotated logs are preserved under ignored local evidence. Revival health drops spawn at19:02:40.045Z during NKG balloon. NS19:02:42 window has184 frames,10.912ms mean,27.339ms p95,66.669ms max,11 frames over25ms and7 collections; the following window has58.918ms max/two more collections. HK at the same event mostly remains6.946ms mean (one28.965ms spike); its separate63.338ms/full-collection window is19:02:48. Native revival hitstop briefly sets NS time scale0; this is separate from measured long frames and is preserved. Actor/arrow buffer allocation counters are stable through the proc; there is no evidence of another homing-image size explosion. Full-screen HUD sends are8,294,400bytes and about79ms processing/send round trip against a20Hz capture request schedule.

Current correction: admission backpressure avoids rendering/replacing queued HUD images; six-free HUD-only exact-length pooling covers producer/readback/pending/worker matte leases within the existing128MiB retained budget. Other streams retain three-free policy. Full-screen HUD leases and bounded colour tables warm during initialization. Reusable hierarchy/layer collections replace per-capture LINQ arrays. Dual-matte/straight-alpha tables preserve scalar integer rounding, native opacity, resolution and shaders; native20Hz target remains. New NS_HUD_PRESSURE and sent resolveMs separate pool churn, queue pressure and actual CPU resolve cost on the next player run. The synthetic six-lease workload demonstrates old-cap churn, not proof of exact allocation provenance in the uninstrumented player run.

VisualChecks pass655360 randomized matte pixels/five backgrounds,65536 alpha/value pairs, zero warmed conversion allocation,120 six-lease bursts with no new buffers/aliasing/budget growth, and existing real-pipe/colour tests. Offline .NET1080p random full-screen conversion measures59.39ms scalar versus36.64ms optimized; **this is not a native Mono/GPU/gameplay measurement**. Both builds and ContactChecks pass. No post-change game launch; revival performance remains PARTIAL/player retest. No collection suppression, heap-threshold change, resolution reduction or revival gameplay alteration.

Pure Vessel: HK80.277/85.004ms collection windows coincide with encounter entry; native fade coverage was not captured. A55.337ms collection at18:58:58 is genuinely mid-fight, before native victory/return (boss death18:59:24). NS also has31.465ms at18:56:09 and32.800ms at18:57:29 during active attempts; the larger108–157ms NS events align with native return/loading. Shared HUD corrections apply there too, but available logs do not attribute the independent HK mid-fight collector's allocation source. **That HK collection is not declared fixed.** Main-thread allocation counters were unavailable in this run; retain existing data and use new narrow pressure/resolve telemetry on subsequent player captures. See P16-PERF01/02 in the canonical register.

Latest GC mitigation:4264e4b0 passed120seconds of actual rain with no25ms frames/HK full collections, HK6.948ms/NS6.946ms means and maxima18.329/13.964ms. Heap growth about7→5.3MiB/s. Earlier61–65ms events below are historical baselines; arbitrarily long fights remain capable of native collection. [Latest policy/cause/test scope](OCT14-RADIANCE-WORKLOAD-PERFORMANCE.md).

## October15 GPZ homing-arrow allocation correction

Latest paired session846f6228-daa8-42b1-bf30-3ea101b96f92 is preserved locally. At02:11:02.440Z NS arrow allocatedPixelBytes=1,140,064,276; at02:11:05.422Z it reaches2,047,082,516:907,018,240 additional bytes in2.982seconds. The02:11:04.733Z frame window records nine collections at each generation and81.170ms worst frame. Actor captures largely remain sub-millisecond; the separate arrow stream reaches68.653ms capture time. HK simultaneously creates successive768/1024/1536/2048/2560 textures. This supports image-allocation/collection pressure as a measured cause, not a claim that every Zote hitch has one cause.

The pool preserved cold singleton sizes and discarded returning hot-size spare leases under budget pressure, repeatedly allocating26MiB arrays. A synthetic cold-tier/120-frame concurrent-burst test fails before the correction and passes afterward. Returns now trim other-size spares/cold buckets to retain hot-size concurrent buffers, preserving original128MiB bound and three-free-per-size cap. The older nine-tier budget regression still passes at108 allocations versus historical198. Detached arrows now receive existing homing maximum-tier hold and safe-entry/UI preparation, avoiding repeated intermediate expanded capture shapes; native pixel density/coverage remain unchanged. HK minion HUD now caches its four aspect tiers within32MiB instead of destroying textures on each shape change. No GC suppression, threshold change, FPS cap, image resolution reduction or native arrow behavior change. Both builds and Visual/Contact checks pass. Post-fix native timings were not measured; P15-ZO06 awaits the player's retest.

## Acceptance criterion

The player accepts normal statue entry/exit stutter contained within native fade-in/fade-out. The reported save hitch happened outside that transition and interrupted visible gameplay. Do not extend fades to hide delayed work. The player will assess transitions in the next session; a 274 ms/full-GC sample without fade correlation is not automatically an unacceptable visible hitch.

## Fixed: game-thread store-publication block

`cf9f195` transfers only immutable isolated-store publication to a worker. Native capture, lifecycle validation, generation adoption, acknowledgements and save callbacks remain on HK's game thread. Single-publication ownership, slot/pair/session/transaction fencing, durable flushes, hashes and recovery checks remain active; no cross-process atomicity or power-loss guarantee is claimed.

| Measurement | Actual result |
| --- | --- |
| Before, existing-save copy e79ea2ff | HK publication blocked 1580.331/1525.921 ms. |
| After, existing-save copy 7454408a | Game-thread preparation 6.444/5.604 ms; worker publication 1681.692/1625.598 ms. |
| Observed publication frame windows | Max 8.919/7.785/8.676 ms; zero frames over 25 ms. |

This removes the demonstrated storage blocking, not every possible native serialization/GC cost. The worker still takes time; gameplay need not wait for that I/O. Both native payloads and exact identity checks remain required before publication/adoption. A late/cancelled result cannot update a newer session. Old data remains recoverable.

## Remaining validation

Initial arm-inspection stutter is tracked separately as UI09. The player's e236edd4 capture correlated a 448.678ms NS frame with simultaneous expired Yi/Root images. Stronger fixtures reproduced a 789.016ms frame with two full collections during inspection; cached gesture samples alone did not remove it. Inspection now waits for completed paired preparation/initial native snapshots. a905ca9c recorded a 2.75s inspection with no >=100ms inspection-frame event; a 775.536ms preparation frame still occurs before inspection, under the loading caption. This is an ordering correction, not a claim that all native collection/serialization costs are removed. See [current evidence](OCT13-CONTROLS-AND-INITIALIZATION.md) and the canonical UI07/UI09 rows.

The player will assess extended-session behavior and any visible hitch outside the fade. The October14 instruction now authorizes the separate all-Hall 180-second performance sweep; it does not request hours of talisman-lockout reproduction. Do not label a normal fade-contained loading spike a new live regression. The canonical G04 row separates its fixed saving component from remaining observation.

Use frame IDs/timestamps, native fade state, capture/publication timing and GC counters to attribute a new incident. Compare equivalent workloads/settings/warm-up; moving combat and room transitions are not idle benchmarks. Preserve native visual fidelity and paired-save correctness. All native comparisons here used existing disposable mod-save copies and unchanged vanilla fingerprints.

The full [pre-consolidation performance record](DOCUMENTATION.md#historical-evidence) preserves earlier idle/soak/aura/geometry measurements and budgets as dated evidence. Its earlier “saving is only a hypothesis” statement does not undo the measured publication fix, and its old task claims do not override [the canonical register](ROSTER-COMPLETION.md). [Focused report](OCT13-FOCUSED-VALIDATION.md) records exact current test scopes.


## October 14 — bow capture work

The original adaptive bow-density path is retained. Straight-alpha conversion now runs on the existing visual channel worker before checksum/send, removing that pure pixel loop from native gameplay. Final d6cc66bf firing/density/save test passed: expanded capture mean 4.304ms, readback mean 2.529ms, max total 33.404ms, including fixture-only PNGs. Idle windows averaged 1.180–1.452ms. Earlier player windows of 7.274/5.603ms used different workloads/logging and do not constitute a clean A/B. Native rendering/readback remain game-thread costs; sustained combat slowdown is not certified resolved. See UI14 and the [dated evidence](OCT13-REBINDING-ROOT-FOLLOWUP.md).


## October 14 — Radiance effects

Player reports massive lag from beam outlines and per-sword phase-ending rain effects. Beam meshes no longer enter silhouette capture. Identical rain nail geometry shares contour acquisition; matching pose/scale/palette shares deformation and mesh uploads. Native art, collision, sword counter leases and body ribbon remain. The bounded orb sample exercises body-only presentation, not phase-ending rain; Later focused rain measurements are recorded below; whole-fight acceptance remains separate. See [dated validation scope](OCT14-RADIANCE-AND-CONTROLS.md) and AR03–05.

## October 14 — expanded actor capture

The preserved Radiance capture reproduced an eight-size pool retaining its first buckets forever while later15/25MiB crops allocated repeatedly. Byte-budgeted LRU reuse removes that churn. Two pinned actor readbacks reuse native HUD code; HK dimension-keyed textures avoid repeated resize recreation. Exact expanded native bytes and ownership/budget regressions passed. Final904978a0 warmed100 requests averaged0.564ms,max4.131ms, buffers0/full collections1; synchronous3d92f6cf capture averaged6.736ms. Request work excludes later copy and approximately13ms sampled readback age, so these are not whole-frame FPS figures. Detailed captures stay synchronous.

Vanilla NKG reference steady mean6.946ms/p95 approximately7.28–7.31ms remains the target. A full Radiance comparison was not run; cold work and the separate526ms post-zombie HK spike remain unassigned. See [dated causes, passes, failed attempts and limits](OCT14-RADIANCE-TERMINAL-PERFORMANCE.md) and P14-AR07.


## October 14 — actual rain and homing workloads

[Workload report](OCT14-RADIANCE-WORKLOAD-PERFORMANCE.md) records causes, fixes, failed setups, reverted checksum experiment and safety. Homing dimension churn was reproduced in real platform combat (NS23 frames>=25ms/38 collections). Size retention, held/paused prewarm and queued-capture backpressure reduced the retained sample to HK6.950ms/NS6.957ms means with 0/1 frames>=25ms. Native arrow/effect resolution and selected content remain unchanged.

Two120-second neutral center-idle rain samples used explicitly authorized finite native invulnerability. HK allocation reductions lowered heap growth about16→9.4MiB/s and full-GC hitches2→1. Final contained means were HK6.951ms/NS6.946ms, worst-window p95 7.322/7.596ms; one64.952ms HK collection remains. Native Update contains the collection but process-wide allocation ownership is not thereby proven. NS max11.421ms, no25ms spikes. Do not hide this remaining visible hitch behind the ordinary fade acceptance rule.

Maximum-tier raw image transfers still take around50–57ms; matching144Hz engine cadence does not certify144Hz visuals or all bow motion. Detailed captures intentionally retain their synchronous path. Full-fight/natural phase/difficulty/extended-session comparisons remain unrun. Canonical P14-AR07 is PARTIAL, AR05/UI14 are bounded samples pending player grading.


Post-culling120-second3a05ca42 endurance: HK6.950ms/NS6.946ms weighted means; worst-window p95 7.329/7.566ms, max61.511/12.778ms,25ms frames1/0. No sustained/repeated dips:59 contained windows per side, all window means below8ms. One isolated HK full collection remains at about108seconds, with Native Update56.609ms and plugin1.546ms; process-wide allocation origin is not proven by the collection location. HK heap growth approximately7MiB/s, collecting1015.91→185.06MiB. Native crimson/safe-floor culling continued through17,267 blade frames, then saved/drained return and26 unchanged fingerprints. No code changes or broad test reruns were needed; this does not certify homing image cadence or whole-fight moving performance. [Full scope](OCT14-RADIANCE-WORKLOAD-PERFORMANCE.md).


The installed HK native GCManager already calls collection during transitions and monitors a RAM-derived heap threshold during ordinary Update. Runtime confirmed non-incremental modeDisabled/1024MiB; this manager temporarily enables GC when forcing collection. There is no general combat exclusion. Preserve it instead of indefinitely suppressing collection. Reused thread-owned bounded primitive-ID validation scratch and activation-stamped native name classification reduced allocation pressure without recycling escaping contact arrays or changing wire validation. Native policy inspection/raw IL remain ignored.local evidence. New zero-allocation/concurrent/error-reuse tests plus Contact/Hit/Visual checks and both builds passed. This is a measured mitigation, not a promise of no future full collections.

## Global 180-second target

The latest player request is now P14-GC01 in the canonical register: every one of 44 Hall profiles, sustained normal combat inputs and an authenticated outgoing-damage hold. Measure both processes, native HK collection calls, combat-only frame windows and heap growth. Preserve vanilla collection during native fades. [Method and current evidence](OCT14-HALL-ENDURANCE.md). A144FPS host does not prove fresh Yi simulation/images from NS.

### Continuous Hall endurance evidence

The global sweep defaults to one process pair/bridge/save session across45 unique workloads (44 native profiles plus Ascended Vengefly). Each case runs180s after entry readiness, cancels through native Hall return and drains before next native statue selection. A copied existing mod-owned save is used once; no fresh introductions/restarts between cases. Fixture-only immunity/outgoing hold limits later-phase/ordinary-victory scope. Native collection policy stays unchanged. Preserve complete timing via sparse bounded .perf logs and report coverage separately from completed native receipts. Three-case2307da0c validates continuity/save safety but fails performance (2 forced collections per fight); see P14-GC01 and dated Hall evidence for current status.

## Completed-sweep allocation correction

[Attribution and implemented reductions](OCT14-HALL-GC-ATTRIBUTION.md) separates starting heap/growth, measured contact/presentation/presence contributions and unassigned non-GC hitches. Exact immutable contour retention and synchronous presentation/geometry reductions are implemented and deployed; both builds and Contact/Geometry/Visual/Hit suites pass. The user deferred extended playtesting, so there is no measured post-change FPS/GC pass. Native collection policy remains unchanged.

Expanded completed-data audit additionally identifies actor image churn:52 new HK buffers in Ascended Vengefly;16/17>=20MiB heap jumps align with new buffers. Spare-first eviction keeps128MiB/immutable leases and reduces the offline nine-tier workload198→108 allocations. Capture-target frequency retention, native FSM contour reuse, geometry streaming scratch/validation, bounded recovery telemetry and IPC scratch are implemented/deployed. Required immutable frame allocations, native engine costs and unassigned hitches remain; no in-game improvement claim. [Detailed audit and failed/passed test scope](OCT14-HALL-GC-ATTRIBUTION.md).

Player43ec152e report exposes image-limited movement with HK near144Hz and images119→73–77Hz. New native-pose/anchor correction and4ms compact transport are deployed; Visual/Bridge suites/builds pass, no runtime pass. Large-arrow image cadence and manual synchronous capture cost remain. [Evidence](OCT14-MOVEMENT-CADENCE.md).

October15 Uumuu follow-up (P15-UU02): paired973871a2 records HK1176.178ms and NS1024.329ms overlapping stalls in the second Uumuu attempt. User associates the event with Ooma exploding into the boss. Wounded begins23:46:22.663Z/229HP,129HP is visible by23.224Z, while the host log gap runs23.441→24.612Z. Both GC counters remain zero and no paired-save event occurs in that window. Two adjacent host trace calls bracket the gap, motivating removal of synchronous routine log writes/flush/rotation from the game thread. Bounded worker queue with drop/slow-I/O markers passes blocked-sink/order/budget/retention checks; this is not a native performance pass or conclusive explosion/GPU/disk attribution. No native replay was run. See the canonical register for remaining player validation.
