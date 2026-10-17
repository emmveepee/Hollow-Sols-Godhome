# October 14 — Radiance terminal routing and capture performance

This is dated engineering evidence. [ROSTER-COMPLETION](ROSTER-COMPLETION.md) owns current issue states; no whole-fight acceptance or public-release readiness is implied.

## Preserved player evidence and causes

Paired session 30040f4d-5027-4165-b818-f27d3eb1ddbf, capture aebc536a-a2fa-4e65-9e38-a08a4725371e, was copied and hash verified under ignored local evidence. Original logs and saves remain intact.

Radiance entered native Death Ready at HP693; reflected orbs continued ordinary health subtraction, eventually 4 to 0 without the required native FINAL HIT. The admitted boss receiver is distinct from the nondamageable encounter anchor. Retiring that receiver explains the later loss of translated projectile admission; zero HP did not establish native victory.

The actor pixel pool permanently retained its first eight sizes. Later 15/25MiB tracking-arrow crops repeatedly allocated uncached arrays. Player NS windows reached 30 full collections per two seconds, with capture mean16.540ms/readback mean7.131ms. A separate HK526.668ms post-zombie frame had no recorded collection and remains unattributed. These findings do not explain every reported spike.

## Changes

Body warnings follow authored eye-beam, ascent and rain states rather than residual pooled renderer visibility. Sweeping beam volumes remain crimson but emit no body warning, VFX or SFX. Terminal native states retire translated attack admission and body presentation.

Actual melee/reflected collisions in native Death Ready send the audited FINAL HIT event. Ordinary raw health damage cannot substitute for this finale; native encounter events still establish completion. The secondary boss receiver now uses the same melee rule as the primary route.

Exact-length pixel buffers use bounded LRU reuse: sixteen buckets,128MiB retained free bytes, at most three free arrays per bucket. Outstanding leases remain owned. Two actor GPU readbacks reuse the existing native HUD implementation, pin source render targets, preserve capture pose/tick/geometry and reject out-of-order completions. Native effects, resolution, input and combat cadence are retained. Entry/gesture/recovery/detailed captures remain synchronous. HK actor textures/sprites are reused by exact dimensions, with six entries and64MiB nominal RGBA storage. These are resource accounting bounds, not a total driver-memory guarantee.

## Tests that passed

- VisualChecks reproduced the ninth/tenth-size allocation failure before the fix, then passed reuse, byte-budget and outstanding-lease checks plus existing ownership/freshness tests. ContactChecks and HitChecks passed. Both final plugins built with zero warnings/errors and were deployed.
- Synchronous capture stress3d92f6cf passed warmed100 large frames with no new buffers/full collections; capture mean6.736ms,max10.689ms. Async79f19468 and strengthened b90f898b also passed.
- Final capture904978a0 passed movement/jump, exact native sync/async byte comparisons including15/25MiB crops, and paired saved return. Warmed100 requests averaged0.564ms,max4.131ms, with no new buffers and one full collection. Later GPU copy is separate work; steady readback age was approximately13ms in sampled windows. This is not a whole-frame FPS comparison or zero-latency claim.
- Finale b7eb60d5 passed an actual reflected orb collision in native Death Ready: FINAL HIT, native victory, Hall return, drained outcomes and paired save. The helper explicitly enters the terminal window without writing HP; unchanged HP3000 before/after the event is expected. This validates terminal handoff, not full phase progression.
- Existing disposable paired-save copies were used. All26 vanilla/development fingerprints were unchanged.

## Failed or unavailable attempts

Finale1947f127 lost Yi to unrelated attacks before the witness; c47fe3f3 looked up the boss too early; e5371731 confused the anchor with the admitted boss receiver. Scoped helper corrections use existing receiver/state evidence. f0b02a0d and636d9f7c never launched HK because Steam displayed a Cloud-sync prompt; these were not gameplay failures. The expired launch was cancelled; Cloud later resolved normally. No Cloud conflict or save override was selected.

## Remaining validation limits

Final narrow secondary-melee routing and transient terminal-state guards were compiled/unit checked after the native finale sample, not separately rerun in-game. Full sword/rain/ascent timing, body warning cleanup across all phases, tracking-arrow combat under load and player motion grading remain manual scope. Detailed logging/reference comparisons use a more expensive synchronous path and must be separated from ordinary-play benchmarks.

Retained vanilla NKG144Hz steady means are approximately6.946ms, p95 approximately7.28–7.31ms. No equivalent whole-fight Radiance comparison was run; matching that performance baseline is not certified. Cold readback/reference work can still spike, and the post-zombie HK spike remains unattributed. No full Radiance/P5 clear is claimed.
