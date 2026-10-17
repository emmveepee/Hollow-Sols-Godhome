**DATED EVIDENCE REPORT — not a live worklist.** Current implementation/player states are only in [ROSTER-COMPLETION](ROSTER-COMPLETION.md); documentation precedence is in [DOCUMENTATION](DOCUMENTATION.md). Unrun cases and next steps below describe this report's evidence scope, not automatically missing implementations.

# October 13 focused engineering report

This records the requested Nosk, save-stall, orange-orb and Collector follow-up. The larger pre-v0.1 milestone and player acceptance worklist remain in progress. No release, public tag or official package was created.

## A. Architecture

The existing native combat bridge remains intact: Nine Sols owns Yi's simulation and outgoing damage; Hollow Knight owns terrain, boss health, phases, deaths and encounter completion. HK controls four isolated save pairs. Activation from either ready main menu uses the previously implemented single-button connection flow.

Checkpoint capture and lifecycle decisions remain on each game's main thread. HK now hands immutable paired snapshots to a worker for isolated store publication, then validates the exact session, slot, pair, transaction and generation before adopting the result on its game thread. The coordinator stays busy until settlement; a new session cannot start while the prior publication is running.

## B. Changes

- Nosk Attuned's exact two platform terrain signatures become one rectangular edge outline. Only the mapped left face aligns upward to Nine Sols' native eighth-unit collision grid. The top, right face and unrelated terrain are retained. No player jump or collision-query override was added.
- Soul Warrior's host reflection admission now agrees with its existing reflectable-orb classification.
- Native Radiance testing corrected the rain emission binding to `Rage Comb`. Fixed body beams and active pooled beams have separate bounded ownership; inactive pooled beams retire instead of exhausting the cache. Native phase/finale authority remains intact.
- A newly admitted Collector receiver overlapping a still-active native melee collider receives the native dealer's collision check. Native validity, facing, duplicate-hit eligibility, effects and damage remain authoritative. Expired attacks are not enabled or replayed.
- Paired level startup requests the native binder's Action state once. Native UI/cutscene votes remain authoritative. Capture uses the initialized player UI owner, and a lifecycle-owned native HUD display vote restores its presentation; teardown revokes that vote.
- Existing-save combat fixtures use native Hall travel, statue variant selection, ordinary input and actual native damage receipts. They end on success/failure and drained return rather than running out a timer.

## C. Save safety

Tests used disposable copies of existing Hollow Sols saves. No new game or opening cutscene was needed. Vanilla save fingerprints matched before and after every run; the inspected 26 vanilla paths were preserved.

The existing mod namespace, immutable generations, payload hashes, durable flushes, transaction markers, recovery scan and expected-generation checks remain active. Worker publication removes storage blocking from gameplay, without weakening those checks or claiming cross-process atomicity. Cancelled results cannot update a later session; already published data remains available to the normal recovery scan. Original affected checkpoints and failed test evidence remain preserved.

## D. User experience

The normal bridge/save-menu flows are preserved. Resting and native saves still request paired checkpoints; gameplay can advance while store publication finishes. Successful tested sessions saved and returned to the connected modded menu. Yi's authentic health, talisman and bow HUD are visible again in the inspected HK and NS screenshots.

## E. UI fidelity

The HUD correction uses Nine Sols' native display voting, fade and widgets, then the existing image forwarding and HK menu ordering. No replacement HUD artwork or generic controls were introduced. Screenshots from `5ff4a837` were visually inspected in both games. This does not certify every new foundation menu, aspect ratio, physical controller interaction or Hall summary ordering in this batch. The native root/status-menu discrepancy remains open.

## F. Actual validation

Both final plugin builds passed with zero warnings and errors. Geometry, Contact, Hit, Bridge, Foundation, Visual, installed-assembly Guard and PairedSave checks passed; the latter ran 4,167 assertions, including publication ownership, duplicate rejection and cancelled-result non-adoption.

| Native evidence | Result |
| --- | --- |
| Root `e79ea2ff`, then `7454408a` | Store publication initially blocked HK for 1,580/1,526 ms. Worker publication took 1,682/1,626 ms while game-thread preparation took 6.4/5.6 ms. Observed publication windows had frame maxima below 9 ms and no frames over 25 ms. |
| Root/HUD `5ff4a837` | Save/return harness passed; native HUD visibly restored in both games. Root/status UI eligibility discrepancy remained. |
| Nosk `a138f0a7` | Native jump rose 1.59375 room units while pressing into the left face; ceiling probes were clear. Save/return harness passed. |
| Soul Warrior `9a1fd354` | Three actual orb deflections and three 21-damage boss hits; host save/return receipt completed. Original harness reported the subsequent NS EOF as failure, so the whole original run is not labelled a harness pass. |
| Soul Master `17a4868c` | Harness passed: three close-timing deflections and a native 42-damage hit, saved return. |
| Soul Tyrant `c0dfd47c` | Harness passed: three close-timing deflections and two native 42-damage hits, saved return. |
| Radiance `e28e6214` | Harness passed: three orb deflections and three native 21-damage hits, saved return. |
| Collector `18a6bbb8` | Harness passed: two jar breaks, two native enemy spawns and two melee reductions from 15 HP to 0 with native death callbacks. The second hit matched the jar-breaking swing 2; saved return completed. |

Failed runs are retained. These include native startup UI/input ownership failures, the wrong copied statue variant, an early Nosk pulse with no Jump edge, two ineffective topology-only Nosk candidates, two Collector tests with no same-swing damage before Yi died, Radiance's wrong rain action binding and its beam-cache exhaustion. Their later corrections are tied to the evidence above, not inferred from compilation.

Not run in this batch: every possible late-parry timing, higher difficulties, all Radiance phases/blade-rain counters, complete boss victories, long-session endurance/GC reproduction, fresh game modes/introduction, all save-menu/vanilla-transition/crash combinations, and physical hardware rebinding. Ordinary sampled deflections retained fixed 21 damage; Soul Master/Tyrant retained fixed 42, independently of native attack damage.

## G. Remaining limitations

The player's subsequent clarification assigns long-session talisman/loadout validation to their next extended play session; no hours-long agent endurance run is requested. The partial corrections remain awaiting that evidence. The player will also audit Thunder Buster and talisman VFX in motion. Those visuals remain awaiting player acceptance, rather than a judgement from still images.

For statue performance, stutter inside the native fade-in/fade-out transition is acceptable. The defect criterion is a save hitch outside that transition, during visible gameplay. The corrected large store-publication hitch is the relevant demonstrated fix; correlate any remaining spike with the fade before treating it as a regression. Never extend the fade to hide a stall.

The demonstrated store-publication stall is corrected. The earlier long-session 274 ms/full-GC return spike is a separate unresolved attribution, and native serialization can still cost time. Root/status configuration and the reported long-session talisman/loadout lockout remain open. The broader authoritative roster keeps its existing acceptance labels; these mechanical tests do not complete whole fights. Classic and Steel Soul remain experimental, and public-release readiness is not claimed.

## H. Before v0.1

Finish the outstanding player report, resolve root/configuration lifecycle ownership, verify complete Radiance phases and counter behavior, compare every added menu to its native counterpart, validate hardware input/scaling, and perform bounded save recovery, slot switching, vanilla restoration and disconnect regressions on the final build. Reuse existing isolated save copies for routine tests. Creation/introduction tests should be explicit separate cases.
