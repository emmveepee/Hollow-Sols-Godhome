# October 14 — Controls layering, native unbound icon and Radiance tuning

This is dated evidence. Current issue states are solely in [ROSTER-COMPLETION](ROSTER-COMPLETION.md), UI08/UI12, AR03–05 and P14-PV01. No whole-fight acceptance or release claim is made.

Forwarded peer-menu pixels now use a last-sibling RawImage on the native HK canvas, above the first-sibling Yi HUD, instead of relying on mixed IMGUI/canvas draw order. Native NS PlayerUI alpha is masked only during the Controls camera render and restored in its finally block; its active state/callbacks are untouched. Owned mask components retire on teardown. The original gameplay/pause visibility guard is retained.

Installed InputPromptUILabel resolves a missing icon to GameConfig.defaultEmptyIcon. Unbound action capture now borrows that exact runtime asset rather than rendering clipped fallback text. Native FindText is unchanged; no E alias/default binding is added. Text-only bindings retain native font/material and use actual TMP text bounds for the capture camera. The earlier provisional text-only approach was superseded before the final build.

Radiance beam renderers are excluded from silhouette owners; beam collision/counter rules and the body ribbon remain. EB2/EB3 no longer allocate separate action identities, and native warning dispatch uses Radiance's crimson sequence instead of per-strike identities. The triple stationary attack therefore keeps one warning lifetime.

All Radiance outgoing reflections are fixed42, independent of incoming damage/difficulty. Crimson rain counter blades retain their 2x ratio at84, with no internal injury. Deflected swords move at twice their observed native incoming speed. Nonreflected sword resolution retires translated strike admission while preserving native sword/comb flight and recycle, so imperfect parry does not destroy the blade. Native phases/health/final melee authority remain.

Rain ribbons keep their style. Identical immutable native nail geometry shares contour acquisition; matching pose/scale/palette shares animated deformation and a single mesh upload per frame. Each sword keeps its native transform and independent counter. Mesh/cache ownership is bounded and cleared on geometry/teardown; exact cache comparison includes vertices/UV/triangles/colors/texture/shader. This removes duplicated work, but no full rain-phase frame-rate result is claimed.

Pure Vessel T Hit now identifies the native boss as CounterInjuryEntity, routing successful native tentacle counters through existing internal injury and health acknowledgement. No synthetic damage amount or replacement stun was added.

| Test | Actual result |
| --- | --- |
| Both plugin builds | Passed, zero warnings/errors; final plugins deployed. |
| HitChecks / ContactChecks / VisualChecks | Passed. Fixed per-profile reflection damage, crimson rain ratio, counter metadata and visual/pixel ownership checks remain covered. |
| 61461098 Controls | Passed initial menu canvas ordering/mask, Root/reopen/jump/save. Unbound still used provisional text; not evidence for the final empty icon. |
| c80feabf Controls | Passed final native empty-icon identity/opaque pixels, generic glyph variants, retained B/Y/Root/reopen/jump and saved return. HK post-Root Controls image inspected: text renders above forwarded HUD. Binding discovery is injected; hardware/restart grade remains player scope. |
| 566682d3 Radiance | Failed whole-launch timeout before completion/return. Two reflections and one native 3000→2958 impact were recorded. Subsequent peer EOF/visual-stop followed HK timeout/quit. Not a whole-test pass. |
| 3574554a Radiance | Passed bounded first-phase sample, 3 reflections / 2 impacts, native cancelled return and paired save; no victory claimed. Ended at ~115s when complete, before its 170s ceiling. |

The passing native runs preserve 26 vanilla/development fingerprints and use disposable copies of existing paired saves. No fresh save/cutscene or original-save write. The shell command chaining the first passing Controls test stopped on a stale LASTEXITCODE; its authenticated completion/saved-return result remains valid, and Radiance ran separately. Final cache comparison hardening and retained menu game-state guard were rebuilt/deployed after the native sample; those narrow final edits were compilation-checked, not rerun in game.

Not run: full stationary-rain performance/visual comparison, accelerated/imperfect sword paths, full triple-beam cue count/listening, ascent/finale and Pure Vessel tentacle counter damage. These are implemented changes awaiting focused player grading. The sampled first-phase body-only aura cost does not establish rain performance.
