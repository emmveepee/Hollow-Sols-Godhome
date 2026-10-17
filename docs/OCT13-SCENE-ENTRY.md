# Scene entry and Godhome EXP — October 13, 2026

This is dated evidence. UI03/G11 in [the canonical register](ROSTER-COMPLETION.md) control current status; whole-fight acceptance is unchanged.

## Behavior

Before native paired new/load, acquire HK HUD/presentation ownership. Clone the runtime pause dimmer and native text/font under its canvas. Center the caption; cycle . / .. / ... every 0.4 seconds with transparent trailing dots preserving alignment. Hide Knight renderers and scoped native LoadingCanvas graphics without stopping animation, entry or loading callbacks. The normal introduction and interact prompts remain native and unobscured.

Each gameplay scene has a readiness fence: current-scene Yi pixels, matching geometry, completed native entry and the existing controls verdict start a 0.25-second unscaled fade. Input and the existing encounter gate stay held through it. Native fades retain their timing. Pause during loading is suppressed to avoid an invisible paused initialization. Native return restores original presentation state before menus resume. Session identity and retired-instance guards prevent stale operations affecting another slot.

Only mod-owned Godhome clamps EXP writes/gains and displayed percentage to zero. Normalize loaded EXP after paired native data is ready. No level, skills, equipment or baseline initialization is reset. Vanilla/Classic/Steel Soul retain native EXP behavior.

## Passed tests

- Final existing-save controls fixture **674691c3-c7d9-4075-82ed-be0dcaefd630**: loading handoff, controls round trip, EXP/progress both zero, paired checkpoint and saved return. All 26 vanilla fingerprints unchanged. Screenshots show centered native caption, dimmed scene and real Yi without HK HUD/Knight, loading emblem or inherited selection cursors. Some dot captures fall during the native black fade; stills do not prove visibility through every engine transition frame.
- **134a4506-4dc5-46fe-ba88-354b01d8d023**: existing-save hub/Hall/Nosk arena handoffs, readiness gate, native left-wall jump and drained saved return passed. This is not whole-fight certification. Final emblem/cursor cleanup followed this run; readiness logic was unchanged.
- Existing-save controls **40074d8f** and **52386b77** passed zero EXP/progress, loading readiness and saved return. **2e09fe61** passed mechanically, but visual inspection still found emblem/cursor artifacts; final 674691c3 supersedes those visuals.

## Intermediate failures and partial evidence

- **73a632f6** failed on unavailable initial pause content; native main-menu typography fallback corrected it.
- **67a8d467** exposed mask deactivation during native entry and stale Unity-object return references. Reassertion, Unity null checks and pre-return presentation release corrected these paths. **7f286cc8** passed controls but did not show the mask; it is not a loading visual pass.
- Fresh disposable Godhome-Yes **f085d5ed-1be9-4df5-a757-7241eaa4f1b9** preserved interactive introduction and showed waiting, real Yi reveal and readiness without visible Knight/HK HUD. Host saved return completed. Whole harness failed afterward on NS EOF and then-build stale return references; the full fresh-save round trip is not passed.
- **8a9e7758** on a copy of that save showed loading/reveal and a committed checkpoint but timed out in Ending, followed by NS EOF. Bounded fixture-only acknowledgement tracing was added; subsequent current-save runs completed. This does not establish every earlier timeout's root cause or justify changing persistence.

Affected data/evidence remain in ignored local storage. Subsequent routine tests reused existing isolated mod-save copies. Only the specific introduction test created a disposable save. No vanilla/development migration or writes occurred.

## Remaining validation

Final HK/NS builds pass with zero warnings/errors. Final stale-instance guards and unused-state cleanup were rebuilt after 674691c3; no full runtime rerun followed those small changes. Broad suites listed in STATUS are prior evidence, not newly run for this UI change. Player validation remains for alternate resolutions/controllers, final fresh-save full round trip and Classic/Steel Soul presentation/unchanged EXP. No public release readiness is claimed.
