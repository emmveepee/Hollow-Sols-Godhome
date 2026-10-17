# Boss compatibility — current interpretation

October17, 2026 player approval: **all 44 listed encounters are Complete**, fully functional with no major issues. This supersedes the October13/partial-retest grades and conflicting pending whole-fight acceptance. It does not certify unreported difficulties/bindings, complete Pantheon sequences, experimental modes, physical menu rebinding, failure reporting or save recovery. Earlier per-issue source/native evidence remains historical; do not infer a new source fix or reopen accepted fights from old pending text. See [canonical roster](ROSTER-COMPLETION.md#whole-fight-player-acceptance).

P17-REL01 adds a public profile without developer controls and optional failure-report export. It changes no boss mechanics, gameplay schemas or fight acceptance; see [BUG-REPORTING](BUG-REPORTING.md) for offline/native validation boundaries.

P17-UI02 mouse hover/click and Esc are player verified; P17-UI03 keyboard no-switch rebinding is also player verified. Mouse rebinding now uses requested host button/wheel snapshots in the native listener scope; corrected mouse no-switch retest pending. No encounter acceptance change; current gameplay52/visual15.

P17-DBG01 aerial display is player confirmed; grounded omission is confirmed in session d076177a. Grounded follow-up includes native health-bound incoming-hit receivers while in Parry, alongside enabled outward dealer geometry. Collision toggle and idle suppression retained. No defense/encounter changes; ground visual retest pending, both current components gameplay52.

P17-UI01 corrects shared keyboard/controller prompt identity across input expiry; it changes no encounter behavior or acceptance. Real-pipe checks pass; native Root/interaction presentation awaits player retest.

P17-IO01 addresses shared room-geometry publication failures reported at Soul Master entry and Gruz Mother return. This is a transport correction across rooms, not new boss acceptance; native/friend validation scope is recorded in ROSTER-COMPLETION.

Full-access Godhome physical roof route uses the native lever access branch; player reached the roof (P16-G02). Roof-return disconnect correction P16-G03 is player verified; native airborne-entry loop correction G04 awaits retest; scoped upper-wall depth W01 is player verified; full Pantheon5 traversal remains unverified. No challenge clears are granted.

Pure Vessel title/bar, first-NKG entry delivery and first-dodge follow-ups are tracked only in the canonical register (P16-UI05/UI06/PERF04). Submenu highlight retention P16-UI07 is player verified. Fresh Godhome Knight/particles/arrival audio suppression is P16-UI08; native Godseeker mode-selection unlock is P16-G01. Those source changes are deployed; player validation remains pending.

Uumuu shielded talisman/Full Control correction is player verified (P16-UU01). Global minion/summon HP sharpness follow-up uses fixed native-widget tiles instead of arena-sized rasters (P16-UI04); native visual/performance retest pending. See docs/ROSTER-COMPLETION.md for current states.

Broken Vessel counter cleanup/overhead identity fixes remain. Projectile outlines are removed at user request due to severe performance loss; native projectile visuals and crimson gameplay remain. No sustained post-rollback head-shake slowdown is demonstrated in the supplied capture; user authorizes moving on, retaining lightweight timing counters for future evidence (P16-BV03). See canonical P16-BV01/02/03.

Vengefly alternate-arena counter-stun parity is player verified; see canonical P16-VK01 in ROSTER-COMPLETION.md.

GPZ independent native-title ownership correction awaits player validation; prior AreaTitle-ancestry implementation failed retest. Traitor Lord emitted-wave counter-only behavior is player verified; see canonical P16-ZT01/TL01. Other accepted encounter behavior remains unchanged.

P16-RF01 supersedes older per-boss reflected-speed exceptions with fixed40 world units/s. HI01 corrects Hive needle parenting, authored facing and pooled transform restoration. Damage/eligibility remain unchanged; automated checks/builds pass, native player grading pending.

Sisters P16-SB01/02 reopens the specific terminal-action disconnect and phase-name presentation cases. The receipt correction applies to shared roster handling, not a Sisters-only counter patch. Automated checks/builds pass; native/player validation remains pending in the canonical register.

Boss-bar entrance follow-up (P16-UI03/OM06): the previous host-only title gate let native entrance animations finish invisibly and persisted across Oro/Mato phases. Contact37 now authorizes native widget creation/Enter at title/HUD reveal; phase two waits for its combined title and both live bars, then starts both entrances together. Both builds and Visual/Contact checks pass; deployed, native animation/player retest pending.

P16-AR07/08/UI03: Radiance ascent platform contact correction preserves four-sided solidity and native floor heights; native phase notches now follow the corrected display HP scale. Other encounters wait for their native title fade-up before drawing boss bars and still respect native HUD hiding. Both builds/Geometry/Contact/Visual checks pass; player runtime grading pending.

P16-AR05 follow-up: player confirmed Yi finale hiding but found native Knight mask artwork and an invisible Hall return softlock. Arena-scoped hide requests now reject stale Hall callbacks; exact Knight Split/Ball native renderer references suppress separate cutscene artwork while preserving native return logic. HK build and Contact/Visual checks pass; native visual/return retest pending.

Inventory upgrade loss P16-PROG01: root cause identified in both new captures. VanillaSelection read StartMenuLogic.Instance after title unload, creating a new title whose Awake clears all runtime stats and unloads audio banks. Use non-creating InstanceRaw identity checks. Native arrow Update clamps charges when capacity collapses, explaining why later capacity repair did not refill arrows. NS build and production-helper regression pass; deployed, post-fix in-game retest pending. Saved upgrade flags remain intact.

Latest HUD/Radiance follow-up: P16-UI02 separates native hurt/fire effects from HK HUD visibility and makes the boss bar follow native HUD timing. AR04 projects Radiance's scripted reserve/final-hit health for display only; AR05 hides Yi on native finale; AR06 adds a0.25-unit unbound-only proximity margin to first-phase stationary crimson rain without changing hurtboxes. Contact36, both builds and Contact/Visual/Hit checks pass; native visuals/timing await player. See the canonical register for scope.

P16-PERF01/02/03 distinguish NKG revival NS stalls from a separate Pure Vessel HK mid-fight collection. Latest40162495 has stable HUD buffers but345MiB of world-effect buffer allocation around the residual74.607ms NS window. Eight-lease world-matte retention within128MiB is implemented, offline checked and deployed; complete native hitch resolution awaits player retest. Fire bats keep independent borrow actions, with explicit no-volley grouping and production codec/retirement checks. The PV HK collector source remains unresolved. See PERFORMANCE for measured scope. Sly SL02 is player accepted. Radiance P16-AR01/02 fast orb reflection and native HK ascent checkpoint recovery are player accepted. AR03 attached ledge-parry facing and UI01 native HUD timing/PV authored title position are source changed/player retest, with no new whole-fight acceptance.

Sly P16-SL01 merges body into authored sword strikes and shares charged dash travel/finisher retirement, increases ordinary combo push, and triggers native final-phase stun after3 consecutive unbound counters. SL02 replaces the incomplete direct Rage Dash return with exact native dynamic/zero-gravity restoration and Fire D upward relaunch, followed by native facing and Rage Dash, without HP initialization; Sly notches are removed. Both builds/combat/native contract checks pass; repeated-stun flight remains player retest. Builds/combat/geometry/native contract checks pass; actual hit grouping, positioning and final-phase stun remain player retest. OM05 brother knockdown presentation is now player accepted.

Oro/Mato P16-OM03 supersedes OM02's native-ending recovery: dispatch only completion events and cancel the whirlwind follow-up entirely. Native fallen/get-up artwork replaces idle during the paired unbound stun, without defeat/HP-reset state entry. Double swings get15% more non-tumbling recoil. OM04 corrects OM03's invalid standalone push refinement that disconnected both reported attempts; valid paired flags now pass production codec/reaction regression. Builds/offline timing/contracts pass; native presentation/behavior remain player retest. OM05 adds native outward knockdown flight and requires floor settlement before the fallen hold, addressing hover from midair pinning; HK build/offline checks pass, ground alignment and motion await player retest. Prior paired-counter mechanics acceptance does not establish full fight acceptance.

GPZ P13-ZO01/03 and P15-ZO04 now address late-counter large landing waves, detached-head melee after Victory closes combat channels, and low-resolution minion HUD. Smaller requested counter-stagger waves are preserved. Both builds/offline Contact/Visual checks pass; native behavior and HUD performance remain player retest. P15-ZO05 additionally gates all jumping melee/body hurtboxes until native apex/descent, including global contact; HK build passes, timing retest pending.

Traitor Lord P13-TL01 preserves the native attack contour plus body contact and a shared action through aerial/landing phases, active only from native apex DSlash Antic onward; optional global contact cannot add an early or second hit. Player accepts the apex/shared-strike change after82591e6. Revival Jade glow P15-VIS03 is now player verified.

## Uumuu follow-up

P15-UU01 now snapshots native JumpKick before callbacks because its dealer vector remains horizontal. P15-UU03 preserves Uumuu native shield at attachment and delayed detonation: a shielded explosion is consumed without damage or replay. P15-VIS02 changes only minion HUD/wall artwork layering. Player accepts Uumuu after6d84bc1. Player confirms wall anchoring restored but rejects global foreground promotion. P15-VIS02 now isolates each matched native Uumuu/Flukemarm wall and trim in a local sorting group; original external bands elsewhere return. Player accepts Uumuu/Markoth/Hall wall visuals. Player accepts latest hub/Flukemarm corrections after1deae62 as well. Wall visuals are player verified in the canonical register.

Intermittent first-arena Yi flicker is a shared entry investigation (P15-ENTRY01), not a False Knight-specific compatibility claim. First-image readiness now rejects transparent artwork per room; builds/offline checks pass, native symptom/root-cause confirmation remains pending.

Option B [release preparation](RELEASE-PREPARATION.md) changes documentation/licensing and isolated compatibility tests only. It adds no destination adapter, boss support or native acceptance; preserve the canonical register's current grades and evidence.

Marked Hall/hub/arena wall strips now align their imported NS face and enable trigger together to native contact precision, independent of room-entry anchor (P14-WALL01). Source coordinates/art and unmarked terrain remain intact. Native first-entry wall retest remains pending.

P14-AR10 gives Radiance's ascent beams and three stationary bursts independent strikes, with one warning per whole barrage. P14-VIS01 globally separates native boss warnings from Yi's actor-motion correction. These are source/build/offline verified changes awaiting player native grading.

Radiance P14-AR09 fixes terminal boss-bar metadata rejection; native finishing-hit HP gate and the later statue/Pantheon branch remain authoritative. The Knight-break animation before that branch is shared native content. Both builds/offline checks pass; final statue completion/return awaits player retest.

NKG P14-NKG01 changes shared base/column damage retirement through native collider gaps; independent pillars remain separate. Native damage/low-health HUD correction is P14-HUD01; NKG post-death checkpoint-read stall/disconnect is P14-NKG02. Source/build checks do not establish player visual or whole-fight acceptance.

The [canonical register](ROSTER-COMPLETION.md#whole-fight-player-acceptance) is the only whole-fight acceptance table and per-issue status source. This page does not duplicate labels that could drift. Preserve its eleven player-accepted fights, Gruz's later demotion, uncertain family labels and encounters without an explicit overall grade. Native Hall selection remains unrestricted; do not add tested-roster locks.

Source changes, isolated checks and a scoped native pass establish their named mechanics, not a whole fight or Pantheon clear. NATIVE VERIFIED issues are retained fixes awaiting player approval, not automatic engineering work. SOURCE CHANGED issues await retest; PARTIAL cases retain their stated unresolved scope. Reopen only from new evidence.

Recent exact mechanic evidence is in [October13 focused validation](OCT13-FOCUSED-VALIDATION.md). Nosk's left-wall jump and Collector's same-swing damage passed; sampled Soul/Radiance orb deflections passed. Radiance's ascent/finale, sword/rain-counter visuals and P5 completion are not certified. [Radiance contracts](ABSOLUTE-RADIANCE.md) identify its current paths and validation boundaries.

Shared corrections retain native authority, independent bodies/strikes and lethal phase/death handling. Attached explosions cover hidden/offscreen/underground targets within their exact live leases, with authored armor/head and Radiance finale exceptions. Normal corpse rejection must retain Zote's authored detached-head interaction; retired projection colliders remain quarantined after admission-hook teardown. Receiver-owned deposit expiry/cast and latest Gruz damage evidence are scoped in the [latest regression report](OCT13-LATEST-REGRESSIONS.md), not whole-fight acceptance. All preserved hub/Hall/arena walls remain within current scope.

Classic/Steel Soul remain experimental. Paired-save/lifecycle evidence validates initialization/persistence, not boss acceptance. The [pre-consolidation compatibility page](DOCUMENTATION.md#historical-evidence) and older HISTORY files are dated evidence only; old completion claims or slot-4 commands cannot override the register or current save boundaries.

The current bow-density correction extends the existing native capture budget to match retained arrow reach. It changes neither arrow damage nor encounter acceptance; UI14 and the dated rebind follow-up record its bounded rendering sample.

October14 Radiance tuning supersedes prior beam-outline and outgoing damage contracts; see AR03–05 and ABSOLUTE-RADIANCE. Pure Vessel tentacle internal-injury ownership is P14-PV01. Neither changes whole-fight acceptance.

Radiance reflected terminal handoff has a forced-window native victory/return sample. Authored warning cleanup and bounded expanded capture are implemented; full-phase performance and whole-fight acceptance remain pending. See [October14 evidence](OCT14-RADIANCE-TERMINAL-PERFORMANCE.md) and P14-AR06–07.

October14 actual rain/platform workload follow-up: see [measured performance and residual GC/image cadence](OCT14-RADIANCE-WORKLOAD-PERFORMANCE.md), with current states in AR05/P14-AR07/UI14. Neither bounded workload success nor debug invulnerability changes whole-fight acceptance.

October15 Uumuu follow-up reopens only melee launch direction and explosion-associated performance (P15-UU01–02). Preserve native Ooma shield-breaking/damage/core ownership, accepted walls and water. Forwarding authored melee direction is implemented; the >1s paired stall is not yet conclusively attributed or verified resolved. See the canonical register.
