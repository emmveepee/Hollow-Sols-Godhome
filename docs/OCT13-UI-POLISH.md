# October 13 pause/options UI evidence

Dated evidence, not a live worklist. [UI01–02 in the canonical register](ROSTER-COMPLETION.md) own current implementation and player acceptance.

The new pause entry inherited cloned visibility forced to full opacity and was parented to the content group instead of retaining the native row parent. It now shares the donor parent and mirrors only the donor text/fade-chain visibility. Selection cursors stay independent. No guessed delay or write to the original native opacity is involved.

Configuration previously used a bare custom panel and a typed left-side `[X]`. It now owns an inactive-created native Options screen clone, with its native title/font/ornaments/content/controls and real UIManager ShowMenu/HideMenu sequences. Owned donor callbacks/list are removed or disabled before activation; the donor list cannot reactivate old rows. Native anchors/coordinates and back fade ownership are retained. Native submit/cancel sounds, highlights and navigation remain in use. The right-side indicator is an owned white-stroke square/checkmark, rather than a redistributed asset or default Unity Toggle skin; native Options uses menu rows rather than providing this checkbox prefab. Global-contact dependency and next-encounter settings semantics remain unchanged.

Only the owned configuration fade enumerators are scoped. Teardown/replaced visibility ends outstanding fades before touching retired objects. Original Options visibility, settings and focus are restored on return. Combat, saves and input-binding semantics are unchanged.

## Actual checks

- Both final plugin builds passed with zero warnings/errors and matching native-menu deployments.
- After the inspected run, only the older configuration-probe diagnostic was adjusted to await native transition completion before checking focus, and HK was rebuilt/redeployed. That legacy diagnostic was not rerun; the ordinary UI path is the one inspected below.
- Final existing-save fixture **e90ebab6-69a1-4f4b-a64d-d135f4017409** passed. Both games started concurrently; an existing mod-owned save was copied into the disposable namespace, without creating a fresh game or playing its opening cutscene. Completion/drained cleanup ended the run early.
- Native and owned pause text effective alpha matched **0, 0, 0.4857161, 1** at four reveal samples. Actual screenshots were inspected: new text is hidden before native reveal, then appears with the vanilla labels.
- Actual native Options selector and configuration checked/unchecked captures were inspected at the run's 16:9 display. Native typography, title ornament, independent selected cursors, spacing, right indicator, no overlapping donor rows, and visible Back were checked.
- Fixture assertions passed for configuration transition/default focus, return focus, restoration of original settings, native Nine Sols Controls presentation/close, host unpause, paired save and native menu return.
- All **26 vanilla save fingerprints** matched before/after. Only disposable paired data was used; original mod saves remain intact.

## Intermediate failures and limits

The initial e7a878c4 mechanical harness passed, but visual inspection failed: native ShowMenu reactivated donor rows and anchor origins pushed added rows down. After those corrections, e273adc1 mechanical checks passed and the layout/indicator looked correct, but inspection exposed a hidden Back owner. The final e90ebab6 run includes that correction and the owned-fade lifetime scope. These earlier images are retained locally as failed iterations, not live defects or final visual passes.

No physical keyboard/controller navigation session, repeated rapid human actions, alternate resolution/aspect ratio/localization, or unexpected process loss during this particular panel transition was exercised. Those are player/manual scopes; the focused pass does not certify every display/input case or promote boss acceptance. Existing prior suites were not newly rerun for this UI-only change.

Screenshots, logs, copied saves and deployment manifests remain ignored local evidence. No game assets or save data entered Git; no release/tag/package was published.
