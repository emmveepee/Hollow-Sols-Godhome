# Validation and performance evidence

[ROSTER-COMPLETION](ROSTER-COMPLETION.md) owns current acceptance and remaining gaps. This page defines how to establish evidence; [CONTRIBUTING](../CONTRIBUTING.md) owns commands.

## Evidence levels

| Evidence | Establishes | Does not establish |
| --- | --- | --- |
| Source/static check | Inspected wiring, paths, schema/constants and exclusions | Actual native execution |
| Isolated fixture | Specified codec, lifetime, store, rendering math or fake-endpoint behavior | Unity/Harmony timing, physical input or gameplay feel |
| Native sample | Recorded action/outcome on the named build and scenario | Unobserved phases, variants, hardware or repeated-use behavior |
| Player acceptance | The behavior/fight explicitly accepted by the player | Separate release, persistence or mode requirements |

Existing suites include Bridge, Geometry, Contact, Hit, Foundation, PairedSave, Visual, Guard, FsmLease and InventoryProgression checks. Guard may read installed assemblies and patch them in memory; native test scripts are a different class of tool. PairedSave uses disposable synthetic payloads. Public-artifact checks inspect compiled exclusions; package checks validate exact contents and synthetic verifier behavior. The prior release candidate passed eight isolated suites and package checks; this documentation-only cleanup does not rerun or newly certify runtime tests.

## Native test discipline

Native tests need explicit scope and the save boundary in [AGENTS](../AGENTS.md). Reuse isolated existing paired copies, run concurrent startup when authorized and stop after the intended observation plus drained cleanup. Fresh creation/introduction is a separate test. Preserve original fingerprints and evidence; do not restore/reset saves to make a test pass.

Check actual health changes, target/encounter identity, acknowledgements, native completion and visible return readiness. Timer expiry, artificial invulnerability, an automated win or a late-sequence start is not ordinary combat/full-clear evidence. Image byte counts alone do not prove correct pixels; still frames do not establish motion/timing. Keep failed attempts distinct from later passes.

## Performance contracts

Normal statue entry/exit stutter contained by native fades is accepted. Correlate a spike with fade state before classifying it as visible-gameplay failure. Never extend fades, lower fidelity or alter native timing to disguise work.

Measure both processes with comparable build/settings/warm-up/workload. Record frame-time tails, native clocks/hit-stop, GC, capture/readback/worker/transport costs and pose/image age. Average FPS alone misses pauses and stale presentation. A collection location does not identify its allocation source; old rotated logs cannot establish a complete endurance session.

Preserve implemented bounds: immutable save-store I/O runs on its worker; native capture/adoption remains on the game thread. Pixel pools, readback requests, textures and owner leases remain bounded; outstanding leases cannot be evicted or aliased. Backpressure skips unnecessary capture work without weakening control/contact safety. Do not suppress GC, increase thresholds, reduce native image density or change time scale without a separately evidenced proposal.

Earlier measurements established a main-thread checkpoint-publication block and motivated worker publication; later capture/allocation fixes have mixed isolated/native evidence. Detailed measurements remain historical. They neither certify all future workloads nor create an active fight-fix backlog after player approval. Use new reproducible reports to choose further optimization.
