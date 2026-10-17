# October14 Hall endurance evidence

This is dated engineering evidence, not the live issue or boss acceptance register. Current state is P14-GC01 in ROSTER-COMPLETION.

## Method

Test-HallEndurance uses all 44 declared production Hall profiles, native regular/dream statue selection and Attuned entry. Both games launch concurrently. Existing mod-owned save data is copied into a new authenticated fixture namespace; no fresh save/introduction. Original vanilla/development files are fingerprinted before/after every run.

Timed combat begins only after paired input/native entry readiness and lasts at least 180 seconds. Yi uses ordinary movement, jump, parry, melee, bow and talisman inputs. A 210-second native invulnerability vote prevents premature defeat and expires on room exit. Outgoing boss-health subtraction is held after 10 seconds or before a lethal/half-initial-budget strike; summon health remains native. Boss HP is never restored, attack states are not forced, and native cancellation/drained Hall return precedes saving. These modifiers limit progression through later phases; this is not ordinary combat/victory or all-phase acceptance.

Native GCManager.ForceCollect calls are observed without changing collector mode/threshold. Two-second frame windows wholly inside the timed interval are compared separately for each process, with boundary windows retained separately. NS timestamps are aligned with HK marker timestamps, never with the other game's frame numbers. Any missing END/receipt is a failed fixture, never a performance pass.

## Initial baseline

| Boss / fixture | Elapsed | HK mean/max | HK>=25ms /fullGC /native forcedGC | NS mean/max | NS>=25ms | Save boundary |
| --- | --- | --- | --- | --- | --- | --- |
| Gruz Mother 0b62ef08 |180.007s |6.953/24.268ms |0/0/0 |6.956/36.565ms |5 |26 protected fingerprints unchanged |
| Xero 8722740c |180.007s |6.949/20.979ms |0/0/0 |7.001/180.731ms |5 |26 protected fingerprints unchanged |

HK worst window p95 was7.287ms; NS8.535ms.89 wholly contained windows per process. HK heap220→977MiB. NS automatic collection counters advanced32 during timed windows; that does not imply32 visible long pauses. Batch fbcf4177 completed Xero, then Steam halted Vengefly launch at its HK SynchronizingCloud syncfailed confirmation. No HK process started; 42 entries contain infrastructure failures, not combat results. The other 41 were rejected by the unconsumed ticket before launch. The runner now stops at the first infrastructure failure. All affected evidence is preserved. At that historical stop no game/test was running. The then-next step is clearing the Steam launch confirmation and issuing fresh authenticated tests for the42 remaining profiles, followed by retesting changed-build failures. Do not combine results from changed builds into a single baseline.

## Shared follow-up — built and deployed; native performance retest pending

Contact serialization already used the bit-exact allocation-free floating writer. The same writer is now extended to control/visual/hit packets, RTT responses and geometry serialization to remove remaining Mono float/double temporary arrays globally. Protocol versions, bytes, pixel fidelity, native gameplay and GC policy remain unchanged. Both builds passed with zero warnings/errors. Bridge/Contact/Hit/Visual/Geometry suites passed, including 10,000 arbitrary float/double bit-pattern byte comparisons, real-pipe packet delivery and 1000 atomic geometry replacements. Both components are deployed with manifest hashes. Native performance testing of this extension awaits the Steam launch confirmation; initial baseline fixtures do not validate it. Read-only GC policy telemetry now reports each process mode/incremental setting/slice on fixture startup. Installed NS Assembly-CSharp inspection found explicit GC calls in GameCore scene/static-data/resource cleanup, but no HK-style GCManager or GCMode assignments in that assembly. At that initial audit runtime incremental mode was unverified;32 collected cycles are not32 proven pauses. Native engine/texture/GPU memory is outside managedMB.

## Resumed sweep

The user authorizes Steam Play Anyway through the computer-use skill and five-second relaunch spacing. Resume batches837c46a0 and2f6240cc preserve prior failures and all original fingerprints. Five-second spacing does not eliminate every cloud dialog: the supervisor observed and pressed fresh Play Anyway for NKG. The bounded launch deadline is90s, still below the ticket expiry; normal launches do not wait this deadline.

| Workload/fixture | HK max | Mid-fight native collections | NS max | Scope |
| --- | --- | --- | --- | --- |
| Vengefly ccedb018 |60.650ms |1 at87.923s |81.692ms |180s; GC target failed |
| Mawlek48f6d6ce |21.878ms |0 |48.099ms |180s; NS hitch remains |
| Pure Vessel25400422 |54.943ms |2 at83.221/178.319s |33.951ms |180s; GC target failed |
| Mantis d02186b7 |21.148ms |0 |40.970ms |180s; NS hitch remains |
| Sisters18c9ea0c |28.477ms |0 |215.781ms |180s; combined smoothness failed |
| Grimm0612781a |59.074ms |1 at160.662s |55.556ms |180s; GC target failed |

Source follow-ups pending validation/deployment: global strict native-circle metadata (previously Grimm-only), full contour/polygon fallback unchanged; actor presenter reuses bounded body membership scratch and uses nonallocating native root-type probes for empty pools. Vengefly admitted30 empty root-renderer owners and retried filtered component arrays every frame. This identified allocation is corrected in source, not yet proven sufficient to remove its collection. PV has0 such empty roots and needs separate attribution. Aura cache was examined: steady PV retained8 shapes with no buffer growth, so a guessed aura-cache expansion was rejected.

Additional user-requested workload: Ascended VengeflyKingAscended, tier1, GG_Vengefly_V, two distinct live native boss bodies required, separate180s markers. It is queued separately and not covered by the Attuned sample.

Native policy readouts in ccedb018: HK Disabled/non-incremental/50ms configured slice (inactive in this build); NS Enabled/incremental/3ms configured slice. Incremental mode is not a guarantee that every collection-related or native frame costs at most3ms. Managed heap counters omit GPU/native texture allocations.

## Continuous-session extension

Latest user instruction replaces restart-per-boss as the default with one copied save, bridge connection and pair of native processes. The finite authenticated plan allows at most45 unique cases and retains the600s cap for ordinary fixtures; only a GUID-matched explicit continuous plan admits a larger budget. Per-index placement publication is atomic; immunity expires on old room exit and native attack/health holds are scoped to the current room. After180s, native cancellation must drain and return to Hall before the next statue is selected. Final session ending/save/quit occurs only after the final case. Per-case durable receipts and separate timing markers preserve completed evidence if a later case fails.

Twelve base cases completed before changing runners, including NKG9d5f1568 (mid-fight collection), Sly8682df82, Watchersd2c756e5 and HornetProtector997e46f2. The current three-case continuous fixture validates Vengefly, separate Ascended Vengefly and PV without process restart. Both plugins and Bootstrap build with0 warnings/errors; Guard/Bridge/Contact/Hit/Visual/Geometry checks passed. The global circle/renderer changes are now deployed, replacing the preceding source-only state; their performance sufficiency remains under test. No saves/acceptance changed.

Continuous2307da0c completed Vengefly/Ascended/PV180s each in one651.44-second native session: identical connection/save token through both handoffs, Ascended peak2 live boss bodies, three drained receipts, final paired save/native return and26 unchanged protected fingerprints. Each case had2 mid-fight forced collections; the performance target failed. The original analyzer ignored retained rotation files; it was corrected before final analysis, and all three cases have88–89 wholly contained timing windows per process. HK maxima were60.751/57.500/60.612ms; NS maxima193.289/178.596/39.815ms. Compact sparse performance retention was added for the longer45-case run, where bounded general rotations would eventually discard old cases; FoundationChecks verifies general log rotation cannot discard retained timing samples. The analyzer now reads known rotation/startup/compact files with duplicate removal and reports timingCoverageComplete separately. Repeated native ActionData initialization errors and pool expansion were also recorded; temporal attribution to combat versus startup remains an investigation, not a verified cause of every collection.

The full45-workload continuous sweep now runs with compact retention and unchanged native GC policy. Collector hooks stay installed through native transitions, classify those calls as noncombat, and reset only per-fight counters. Every handoff validates the complete original SessionToken. Both final plugins build with0 warnings/errors; FoundationChecks passed after timing retention. This ongoing sweep is not a completed roster/performance grade.

Continuous6d04ee8e completed12 workloads/36minutes of timed combat in the same process/save, then failed on Sentinel Native barb launch did not provide flight.26 original fingerprints stayed unchanged; partial receipts and compact timing remain preserved. This is a real adapter failure, not an infrastructure skip. HornetBarbBridge now queues one accepted native event and awaits actual Hit-state rigidbody flight for at most1 gameplay second with matching activation/target lease; it never invents velocity or resends the uncertain event. Host build/HitChecks passed; remaining33-workload continuous retest is running. No full45 completion claimed.

42551b5a completed Sentinel/BrokenVessel/LostKin/CrystalGuardian180s each before a fixture-only File.Replace/read-sharing race at the next Hall placement.26 protected fingerprints unchanged. Sentinel had no queued/parried barb markers in known retained logs, so its180s completion does not validate the changed mechanical path. Immutable per-index placement files now publish by create-only atomic rename; NS reads only the expected next index.86af3417 has passed Enraged/Dung/White handoffs and continues the remaining29-case plan in the same processes/save. Builds pass0 warnings/errors. The prior12+4 and ongoing29 sessions stay separate, with no fabricated uninterrupted45-case claim.

The first continuous12-case timing record includes non-GC outliers: Gruz881.690ms (probe attributes878.8ms inside plugin Update but existing subscopes do not explain it) and Sisters214.423ms/NS260.749ms. These remain unexplained failures; do not call either whole window smooth from its mean or0 collection count. PV/Vengefly/Grimm/NKG/Sly/Watchers/Hornet also have combat collections. Source audits ruled out large serialized geometry payload (PV528bytes/7terrain colliders), guessed aura-buffer growth, unbuffered production visual transport, and repeated unchanged-binding decode as established causes. Do not introduce speculative caches/collection suspension from those rejected guesses.

## Completed global sample — final evidence

All44 production profiles plus the separate Ascended Vengefly workload have completed180s samples:12 in6d04ee8e,4 in42551b5a and29 in86af3417. This is135minutes of timed combat across three sessions/builds, not an uninterrupted45-fight run. The final process pair stayed live for5649.88seconds (94.16minutes), retained the exact save/connection token through28 handoffs, then saved the copied pair, returned to its native menu and exited early on completion. Ascended recorded peak2 distinct native boss bodies. All26 original fingerprints matched after each session. Startup/introduction used an existing copied save; no fresh save or migration.

The global performance target **failed**.20 workloads triggered HK combat collection, with22 exact forced calls (21 collection-counter increments in wholly contained windows; one fell in a boundary window). Exact hooked counts are listed below.11 workloads had no wholly contained25ms frames in either process and no HK collection. This is a bounded smooth sample, not a whole-fight/player acceptance grade. Every process/workload retained88–90 wholly contained two-second timing windows; boundary windows remain separate. NS gc2 counters include incremental cycles and are not all proven stop-the-world pauses. The first three-case continuity validation2307da0c remains separate baseline evidence.

| Workload | HK mean / maximum ms | Exact combat forced GC | NS mean / maximum ms | HK / NS frames >=25ms |
| --- | --- | --- | --- | --- |
| GruzMother | 6.985 / 881.690 | 0 | 6.955 / 30.648 | 2 / 4 |
| Xero | 6.948 / 22.852 | 0 | 6.965 / 17.423 | 0 / 0 |
| VengeflyKing | 6.961 / 58.025 | 1 | 7.080 / 182.314 | 2 / 23 |
| BroodingMawlek | 6.948 / 18.429 | 0 | 6.951 / 18.202 | 0 / 0 |
| PureVessel | 6.953 / 65.183 | 2 | 6.964 / 228.655 | 2 / 2 |
| MantisLords | 6.950 / 22.356 | 0 | 6.954 / 33.247 | 0 / 2 |
| SistersOfBattle | 6.957 / 214.423 | 0 | 6.969 / 260.749 | 1 / 1 |
| Grimm | 6.954 / 67.238 | 1 | 6.958 / 58.756 | 1 / 6 |
| NightmareKingGrimm | 6.959 / 91.431 | 1 | 6.968 / 89.858 | 2 / 9 |
| NailsageSly | 6.950 / 71.127 | 1 | 6.950 / 17.796 | 1 / 0 |
| WatcherKnights | 6.955 / 78.763 | 1 | 6.958 / 37.421 | 3 / 2 |
| HornetProtector | 6.953 / 73.312 | 1 | 6.951 / 39.064 | 1 / 2 |
| HornetSentinel | 6.949 / 22.182 | 0 | 6.947 / 16.797 | 0 / 0 |
| BrokenVessel | 6.948 / 17.482 | 0 | 6.947 / 16.744 | 0 / 0 |
| LostKin | 6.948 / 58.471 | 1 | 6.954 / 36.657 | 1 / 4 |
| CrystalGuardian | 6.948 / 15.456 | 0 | 6.947 / 17.521 | 0 / 0 |
| EnragedGuardian | 6.950 / 25.359 | 0 | 6.948 / 26.776 | 1 / 2 |
| DungDefender | 6.950 / 20.846 | 0 | 6.947 / 14.356 | 0 / 0 |
| WhiteDefender | 6.949 / 18.566 | 0 | 6.953 / 33.320 | 0 / 3 |
| HiveKnight | 6.950 / 30.710 | 0 | 6.947 / 15.065 | 1 / 0 |
| TraitorLord | 6.950 / 21.322 | 0 | 6.966 / 264.957 | 0 / 2 |
| MassiveMossCharger | 6.951 / 27.308 | 0 | 6.950 / 32.636 | 1 / 1 |
| SoulWarrior | 6.949 / 24.546 | 0 | 6.947 / 15.576 | 0 / 0 |
| Nosk | 6.949 / 36.014 | 1 | 6.948 / 22.207 | 1 / 0 |
| WingedNosk | 6.952 / 64.356 | 1 | 6.960 / 57.976 | 1 / 6 |
| OroMato | 6.950 / 28.359 | 0 | 6.948 / 18.471 | 1 / 0 |
| Sheo | 6.952 / 67.992 | 1 | 6.950 / 22.863 | 1 / 0 |
| Oblobbles | 6.948 / 23.320 | 0 | 6.947 / 14.937 | 0 / 0 |
| GodTamer | 6.953 / 77.147 | 1 | 6.952 / 41.892 | 1 / 1 |
| Galien | 6.954 / 70.693 | 1 | 6.953 / 48.295 | 2 / 2 |
| Gorb | 6.950 / 71.790 | 1 | 6.965 / 30.458 | 2 / 1 |
| ElderHu | 6.948 / 18.223 | 0 | 6.948 / 24.112 | 0 / 0 |
| Markoth | 6.949 / 26.792 | 0 | 6.950 / 59.059 | 1 / 1 |
| Marmu | 6.955 / 71.346 | 1 | 6.978 / 65.160 | 2 / 18 |
| NoEyes | 6.948 / 22.442 | 0 | 7.000 / 180.868 | 0 / 4 |
| SoulMaster | 6.949 / 23.257 | 0 | 6.952 / 26.429 | 0 / 1 |
| SoulTyrant | 6.953 / 81.729 | 1 | 6.952 / 60.578 | 1 / 1 |
| Collector | 6.949 / 23.436 | 0 | 6.953 / 24.028 | 0 / 0 |
| GreyPrinceZote | 6.960 / 90.948 | 1 | 6.992 / 58.989 | 1 / 14 |
| Flukemarm | 6.950 / 34.857 | 0 | 6.954 / 35.281 | 1 / 2 |
| FalseKnight | 6.947 / 19.312 | 0 | 6.946 / 14.578 | 0 / 0 |
| FailedChampion | 6.951 / 87.785 | 1 | 6.947 / 15.217 | 2 / 0 |
| Uumuu | 6.952 / 95.409 | 1 | 6.981 / 89.731 | 2 / 12 |
| AbsoluteRadiance | 6.951 / 28.811 | 0 | 6.949 / 26.664 | 2 / 1 |
| VengeflyKingAscended | 6.962 / 109.203 | 2 | 7.108 / 188.134 | 2 / 30 |

Final Bridge/Contact/Hit/Foundation/Guard suites passed. Visual and Geometry initially failed deadline-sensitive cases during seven-suite parallel execution, then both passed on isolated rerun; those failed attempts are retained, not silently converted into passes. Both deployed plugins and Bootstrap compiled with0 warnings/errors; the native assemblies and original saves remained intact. No release/tag/package was published. Original user BaselinePlugin edit and conflict copy remain unstaged/preserved.

Remaining technical limits: the20 combat-collection cases and isolated non-GC hitches are unresolved. The emitted Mono per-thread allocation counter and release-build engine allocation metrics were unavailable; process-wide heap deltas are not exact allocation-stack attribution. Two shared allocation changes did not meet the global target, so speculative cache expansion, collection suppression and unsafe reuse of escaping immutable contact arrays were rejected. Native GC mode/threshold remains unchanged. Follow-up needs focused attribution of unscoped hit/telemetry work (Gruz’s881ms event) and explicit ownership leases if immutable snapshot allocations prove dominant. Hornet’s accepted-event launch source correction still lacks actual barb-flight validation in42551b5a; the180s completion alone does not close P13-HS01. The10s/half-budget outgoing hold limits later-phase coverage, including Ouro/Mato and Radiance; full phase coverage, sustained movement and physical-device grading remain unrun by this sweep. All per-issue/player acceptance remains in ROSTER-COMPLETION.
