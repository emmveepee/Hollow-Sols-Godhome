# October 13 — Controls return and initial presentation

Dated engineering evidence, not a second issue list. Current states belong to UI07–09/G09 in [the canonical register](ROSTER-COMPLETION.md). Whole-fight acceptance is unchanged.

## Player evidence and causes

The paired e236edd4 session and detailed captures d74a3f68/db59743a remain preserved. The controls capture shows HK unpaused while NS Controls remained open and Yi's animation speed stayed zero. The old opening path explicitly focused NS. Initial loading recorded a 448.678ms NS frame; HK expired both image/pose leases and hid Yi and Root together. The depth-only Root correction was therefore insufficient.

Installed native UI inspection and stronger fixtures identified the navigation lock: ShowUIEasy displayed Controls before a later PushShow locked its group. Native ShowUI then returned early for an already-visible panel, skipping the unlock callback. Borrowed UI tweens needed scoped unscaled time while gameplay clocks were paused. NS-focused physical UI input bypassed forwarded HK navigation. Pause restoration separately captured Yi's animation speed after an earlier hook had set it to zero; HK's image presenter could capture native pause's zero clock and restore it after resume.

Initialization telemetry reproduced a 789.016ms frame with two full collections during arm inspection (645fd77a). Cached gesture assets alone did not eliminate it. Inspection previously admitted content readiness before paired preparation/snapshots finished. It now waits for the paired Active state. A905ca9c recorded a 775.536ms preparation frame before inspection, an 80.654ms initial native capture including one full collection, then the complete inspection from 01:03:06.7325188Z to 01:03:09.4831595Z with no >=100ms inspection-frame event. Capture duration includes native asynchronous work and snapshot preparation; it is not an isolated game-thread cost attribution. This corrects ordering rather than claiming every preparation cost is eliminated.

## Implemented contracts

- First native PushShow owns Controls reveal, selection and unlock. Borrowed UI transitions use native unscaled tweens; gameplay remains paused.
- Controls navigation always uses forwarded HK input. HK retains focus. Explicit keyboard/mouse binding submission alone temporarily focuses NS for its native listener; completion/cancel returns focus. Only that submitted action listens to raw native bindings. Binding data and gameplay mirroring remain native-owned.
- Pause while Controls owns input requests bounded-rate native Back pulses. HK stays paused until Controls closes, then returns to its native pause menu. Subsequent resume restores both clocks and Yi's pre-pause animation speed together.
- The image presenter owns a synthetic freeze only while HK is playing; it never borrows native pause's zero clock.
- Native gesture sprite/scale frames are acquired in the quarantined donor phase. Live leases do not clone or sample the donor transform hierarchy. No source timeline/story callbacks run and no native assets are distributed.
- Arm inspection waits for paired initialization/initial snapshots. HK prepares images while Loading but keeps Yi/Root hidden behind its native loading presentation. The once-per-save token and existing statue sit/meditate/stand contract remain unchanged.
- An already-displayed inspection image may survive a missing frame for at most 0.75s, only for the same save token/current room with entry controls held. Non-null mismatched images, normal gameplay, different rooms/slots and disconnected cleanup cannot borrow it. Gameplay freshness stays at 250ms.
- Native entry's neutral reveal acknowledgement renews at most every 100ms until entry completes. Expired acknowledgements remain rejected; only a fresh retry is consumed. Movement/combat commands cannot use renewal.
- Native fixture preflight rejects a valid-but-outdated deployment before copying saves or launching games. Startup processes are dispatched together, existing mod-save copies are reused, and fixtures end on completion/failure/drained cleanup.

## Tests and limits

Both plugin builds pass with zero warnings/errors. BridgeChecks passes, including the new actual-pipe expired-entry/retry test, neutral-only validation and rate budget. VisualChecks passes its codec/freshness/pixel/lease checks. These suites do not certify native UI or visual motion.

C6bb8742 controller-panel and 645fd77a keyboard-panel fixtures passed actual selected-object changes, native paused gameplay, zero talisman input, native Back, retained HK focus, both clocks/animation restored, post-return movement and paired saved return. A905ca9c keyboard-panel repeated the path after the inspection ordering correction, with 1.34375 units of native Yi movement after resume. The forwarded native keyboard panel screenshot was inspected for layout/coherence. Device-panel tests select the native controller layout but do not operate a physical controller or rebind a physical key.

Earlier failures remain in ignored local evidence: 141976fe (diagnostic naming of a destroyed Unity object), 4c828549 (native group remained locked), b6d1f5d3/f95ecbdd (physical focused input overrode forwarded navigation), 1fad6bce (zero animation restoration). C3b4272b mistakenly tested the earlier installed DLL; it is not evidence about its newly built replacement. B130678a repeated the keyboard pass while checking a native-cleanup hypothesis; the reproduced stall did not invoke that cleanup method, and the temporary observer was removed.

Bf46b1a6 exposed the visibility-gate follow-up: its one entry acknowledgement expired during preparation, leaving the native entrance waiting. Fresh neutral renewal corrects this without relaxing freshness or replaying combat input. Final 77895d23, NS-origin activation/controller-panel on the deployed code, passed startup, actual Auto Detect→Binding Button selection, retained HK focus/pause, native Back, clocks/animation restored to 1, 1.34375 units of native movement and paired saved return. Inspection ran 01:10:54.2946786Z–01:10:57.0426560Z with no recorded >=100ms inspection frame and no Yi/Root stale-hide event. Its 741.313ms preparation-frame maximum preceded inspection; a later two-second inspection window had max10.660ms with zero >25ms frames. A bounded missing-image hold activated during preparation. Per-frame gesture PNG capture was disabled in Controls fixtures so image encoding would not contaminate timing.

Every native case used a copied existing mod-owned namespace and preserved all 26 vanilla/development fingerprints. No fresh save/introduction, extended talisman/loadout session, physical rebinding, hardware matrix or whole boss fight was tested here. The earlier input-value-only bd6ff315 result remains dated evidence but cannot certify selected-row changes or clock restoration. Expected counterpart EOF during owned harness shutdown after host completion is distinguished from the tested controls path; this is not an independent crash/quit certification.

Player in-motion grading of Yi/Root, perceived startup timing and physical rebinding remains pending. Preparation still has native collection/serialization costs under the loading caption. There is no release/tag/package or public-readiness claim.
