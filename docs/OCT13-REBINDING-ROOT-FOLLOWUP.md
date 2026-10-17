# October 13 — rebinding, Root menu and initial image follow-up

Dated evidence, not a second worklist. Current states are [UI07–10/G09/G13](ROSTER-COMPLETION.md). Earlier [Controls samples](OCT13-CONTROLS-AND-INITIALIZATION.md) remain historical evidence; they did not test an actual physical binding assignment.

## Player evidence and causes

Paired capture `c8e1370e-27e6-49b2-9153-0b9994d77e7f`, session `02a9900c-fea5-441d-800e-59d02470149f`, records native Hack capture at 01:18:40.918Z and forced cancellation in the same input tick. Ordinary Back/B was incorrectly a forced cancel. Disabled UI actions also retained held navigation values, allowing selection changes to cancel the native row. Keyboard focus was requested after listener start, where focus-induced deselection could cancel it.

At 01:18:52.251Z, Root Status called a stale UIManager without menuUI and threw. The retained gameplay UI owner was valid. The scoped repair routes this exact Root call to that owner while preserving SavePanel's native ToMenu/HideUI callbacks. A fixture exposed another dependency: forbidden teleport-tab conditions consulted absent campaign services before the former postfix filter ran. Those tabs now reject before evaluation; permitted native loadout tabs retain their native conditions.

The early black oval was separate GG_Atrium bench-seat artwork, not the bench controller or already-masked Root image. Offline installed scene metadata identified standalone `GG_bench_0001_3`. Only its SpriteRenderer is suppressed from world entry and restored on teardown. Root pixels remain masked throughout the caption/fade. No native assets or metadata extracts are tracked.

The latest Yi-flicker report remains authoritative. Startup telemetry recorded an initial approximately 502ms image gap and hold, but no stale-hide during the arm segment. It lacked per-packet opacity evidence and cannot conclusively identify every flash. Code review found two remaining blanking paths: expiration of the 0.75-second hold and uploading a transparent replacement. Current retention closes both. Bounded opacity-transition diagnostics distinguish transparent production from host stale hiding without extra readbacks or whole-scene inventories.

## Resulting contracts

- Ordinary Back/B can be a physical binding. Explicit host pause-close (`NativePauseMenu+Cancel`) cancels capture before native Back; other gameplay actions stay gated.
- Clear held UI values at capture start and stop forwarding navigation while listening. Native binding callbacks, duplicate policy, persistence and action mirroring remain authoritative.
- Keyboard capture settles NS focus for 0.25 seconds before native Submit; pending deadlines use realtime. Controller capture retains HK focus. Keyboard completion/cancel returns focus to HK.
- Retain valid initial images only within the current save token, wire identity, geometry, room, Active lifecycle and initial entry hold. Held images explicitly fail reveal/control readiness. Root cannot bypass the caption mask. Teardown, new rooms/slots and ordinary gameplay cannot borrow this lease.
- The producer logs bounded inspection opacity transitions. Its fixture rejects transparent inspection samples. Combat blinking and native recovery transparency remain unchanged.

## Tests actually performed

Fixtures launched both games concurrently, used disposable existing-save copies, stopped on completion/failure with cleanup, and preserved 26 vanilla/development file fingerprints. Originals were not loaded or modified.

| Run | Result and scope |
| --- | --- |
| `2cba6c84-24f1-4f97-bc25-9539125be1b8` | Failed controller capture: focus change after listener start cancelled it. Superseded by focus/held UI corrections. |
| `b106ce16-ea89-4d01-b9ac-06e888c96b4e` | Passed native controller selection, live listener across ordinary Back, explicit cancel, resume/movement and saved return. Not physical hardware assignment. |
| `56ef9935-d489-4c05-a372-6c2300988052`, `a5a68456-ca76-4ecc-9999-0bbf9e7d2012` | Failed keyboard deferral: native callback/poll clock mismatch expired a pending request despite a Show/interactive panel. Realtime replaced that timer. |
| `61e0ca48-366e-4c7c-881f-580ec49e150a` | Passed settled keyboard focus, live MoveLeft listener, ordinary Back preservation, explicit cancel, movement and saved return. Reveal screenshot inspected: early oval absent. |
| `c6d0cd16-1374-4c00-9117-52d8daa14d2b` | Failed Root Status after owner repair: forbidden teleport prerequisite evaluation threw. Superseded by prefix rejection. |
| `f2957eae-13cc-47ed-b324-d65ecf328b2d` | Passed Root Status with ToMenu/nativeRoot/bridgeRoot true, native menu round trip and saved return. |
| `21ebc8a2-78d6-4565-8209-2f20e5fa4a7a` | Passed final reload with transparent initial replacement, 0.9-second missing-image injection, renderer continuously enabled, cached readiness rejected and foreign identity rejected. Native inspection stayed opaque, completed and saved return passed; no stale-hide recorded. Active screenshot inspected: Yi and Root visible, early oval absent. |
| `583f3371-4598-4c32-a77f-24b2b3b5d12c` | Final stricter keyboard Controls regression passed actual live-listener (not merely pending-focus) preservation, explicit cancel, resume/movement and saved return; native inspection remained opaque. |

Both plugin builds and VisualChecks passed; builds had zero warnings/errors and were deployed locally. VisualChecks exercised unchanged transport freshness expiry, session rejection, pixel lease ownership and peer-exit invalidation. Image-loss injection is restricted to the existing bounded disposable-save fixture, absent from normal play. Earlier broad suite passes remain earlier evidence, not fresh UI certification.

## Remaining player scope

Actual new keyboard/mouse/controller assignment, persisted settings and resulting gameplay action still require physical testing. Capture/cancel fixtures do not certify that scope. Original initialization flicker was not fully reconstructed; retention fault injection is verified, while in-motion acceptance remains pending. Extended talisman/loadout lockout is G08, separate from the repaired G13 Root exception. Fresh introduction, Classic/Steel Soul and other display/hardware profiles were not rerun. No release, package or tag was created.

## Later Submit-close/reopen report — edefd225

The next player session `edefd225-ef44-4051-a12e-07f8bf09c08b` supersedes the earlier direct-row capture sample for host Submit ownership. At 02:03:43.8529489Z HK queued Jump+Confirm. Native NS Hack capture began at .8717950Z; HK queued forced Cancel+NativePauseMenu at .8744088Z, consumed at .8846360Z. Native Controls closed at 02:03:44.1398917Z. This was an explicit host close, not a native physical binding failure or initial listener timeout. Original captures were preserved in ignored local evidence.

Installed-build call references distinguish HK's physical InputHandler pause route from the menu Resume route through PauseGameToggleByMenu. The hidden pause UI still receives Submit/Cancel despite being visually hidden. Its menu Resume callback and the owned row's Cancel callback now remain inert during peer Controls ownership. The independent physical pause route retains the authenticated close protocol. Vanilla and ordinary HK pause handling are unchanged outside that scope.

Controls cleanup also unwinds only borrowed native UI groups toward the recorded previous owner before restoring/hiding parent panels. PopHide/PopUIPanel perform native stack restoration; hiding the panels alone could leave a hidden current group and a self-push on the next opening. No unrelated groups are cleared or synthesized.

The fixture now forwards Submit through the actual native NS EventSystem instead of directly calling a row's Submit. It also invokes the hidden HK native Resume callback, preserves the live listener across ordinary Back, closes explicitly and reopens for a second complete capture cycle. The final keyboard fixture invokes the owned hidden HK Cancel handler as well. Both games remain paused until native closure, then clocks/movement and saved return are checked.

| Run | Result |
| --- | --- |
| `63782ad2-6f5c-4fd4-a51d-a3eee9ee3579` | Passed controller-panel forwarded Submit, hidden Resume rejection, two live capture/cancel/reopen cycles, resumed movement and paired saved return. |
| `7837986b-e3db-445c-924b-e962c5fcb534` | Passed final keyboard-panel forwarded Submit and settled focus, hidden Resume and owned Cancel rejection, two live capture/cancel/reopen cycles, resumed movement and paired saved return. |

Both builds passed with zero warnings/errors; final plugins deployed. Each fixture reused an existing disposable pair and preserved all 26 vanilla/development fingerprints. No failures occurred in these two runs. They do not assign a new physical binding or certify physical hardware/persistence/gameplay delivery; that remaining scope is unchanged. Earlier direct-row tests remain valid only within their narrower scope and must not be cited as coverage of this host input bug.

## Later successful rebind, menu sounds, arbitrary glyphs and Root exit — 07ec8185

The player confirmed that physical assignment worked in session `07ec8185-5b6c-4297-aa3b-36634a0b5085`, then reported hidden HK menu sounds, a stale B interact glyph after swapping Y/B and Root exit failure. Native capture records successful Hack and WeaponAttack rebinding, followed by repeated UI Cancel **and SubmitSpecial** edges while the Root remained active. This narrows the live work; it does not erase earlier capture/navigation passes.

Hidden HK MenuAudioController Select/Submit/Cancel/Slider methods now suppress only during peer Controls ownership. No stored volume, global audio mix or native NS feedback changes are used. Four actual native methods were invoked under the fixture and rejected in both Controls cycles.

The former prompt code cached B/E. The replacement calls the same native action-level FindIcon/FindText resolvers used by InputPromptUILabel, including native device-style overrides, scheme/binding choice, mouse/key/chord text and unbound `[?]`. Iconless text uses an initialized native InputPromptUILabel fallback font/material. Native output pixels are transported; HK crops transparent margins and retains text aspect and native font proportions. No native assets are distributed. Resolution is cached by native display text/input type/device style/mouse scheme, avoiding repeated missing-icon lookup logging. Only render-ready typography is admitted.

Host menu-special input formerly reused the rebindable gameplay bow action. Swapping bow onto B therefore emitted SubmitSpecial with the fixed native Cancel. Menu-special now independently follows native UI C/Y, preserving native menu bindings while gameplay keeps the player's choices. Controls cleanup additionally rejects a hidden prior group as a restoration target; borrowed Root/inventory GUI always forwards host navigation while only the submitted binding action listens locally. A settled native menu callback restores host focus.

The Root stand-up remains native. No direct gameplay unlock, forced animation or stretched fade repairs it. Diagnostics established that button ownership and the current group matched; early Cancel was rejected because the **native reveal was still locked**, not because the group stayed permanently disabled. The revised fixture authorizes Cancel only after the selected native button's group becomes interactive, then verifies native stand-up, normal control and saved return. The original player's repeated swapped-button case still merits a timing/hardware retest.

| Run | Result / evidence scope |
| --- | --- |
| `22781be5-8cc5-4d01-86cc-1ca8f4b7d1c5` | Failed Root exit; first trace showed an inactive Pause Menu restoration target. Sound suppression passed. |
| `d0a8a8a0-553e-4808-a46b-76b600b65145` | Fixture failed from concurrent rewriting/reading its glyph receipt. Fixed write-once fixture synchronization; not a game persistence failure. |
| `fe928bfa-37d3-42bf-b496-8fd00ec20a17` | Y/X/F12 passed; mouse fallback exposed an uninitialized native font/material donor. Fixed donor qualification. Temporary bindings restored on failure/quit. |
| `d294327f-8cfc-4626-9a31-601a02045f58` | All six native glyph variants and host Y metadata passed; fixed-time Root Cancel still failed. |
| `2ce756d8-39cc-4e2b-8126-222128bd76a1` | Diagnostic failure: navigation and Cancel binding correct, no submit feedback/custom cancel blocker, but button's native group still locked at the attempted input. |
| `0e61ddb5-253b-447d-9217-b391e2c15c2a` | Passed final controller flow: six native glyph variants, Y metadata mirrored into HK, two Controls cycles, all four muted host sound methods, same-group native unlock, forwarded Root Cancel, native stand-up/control restoration and paired saved return. |
| `6457d3f4-95c4-42ce-920c-9d6545102a9b` | Final cached-resolver build passed keyboard Controls twice, native unlocked Root Cancel/stand-up/control restoration and paired saved return, plus host menu sound suppression. |

The generic matrix rendered Y, X, F12, mouse LeftButton, Shift+F12 and unbound `[?]`, requiring actual opaque artwork. Test-only binding changes restore original native action-set data before Root saving and on failure/quit; no persistent test rebind is intended. Existing disposable pair copies and all 26 vanilla/development fingerprints were preserved. Builds had zero warnings/errors. These cases verify the general native-resolver route and sampled source types, not every physical key/controller/style/display permutation. The player will grade physical input and visual details; no full public-release claim is made.

Final native-run EOF warnings occur after the host completion receipt and saved return during harness process cleanup; they are not silently counted as a new player disconnect or independent peer-cleanup pass. Earlier broad automated results remain earlier evidence.

VisualChecks passed again after this follow-up (codec, alpha, freshness expiry, pixel leases, session rejection and peer exit). This is transport validation, separate from native GUI appearance.


## October 13 — Root Back, native Controls sounds and Root/Controls overlap

Player capture ccbdb0cd confirmed Hack changed from B to Y but the native Root Back hint remained B. It also reported second-open failure and missing NS UI feedback. The prior world-glyph fix did not cover the native Root Back widget. Root Back now resolves the native Hack action through the existing PlayerInputActionData/native InputPromptUILabel resolver; HK forwards the same rebindable action while menus are open. Root Back has priority over overlapping Submit/Special inputs and retains the release-after-entry guard. It cannot re-enter the Root while its GUI is open. Controls keeps its native fixed UI actions; raw binding capture remains limited to the submitted native action.

The audio mirror previously set global/output gain to zero for every host pause. Paused Controls now enables native UI feedback at the fresh host master/effects mix, retains BGM=0 and blocks non-UI sound starts. Native Wwise navigation/submit events returned nonzero playing IDs; native RTPC readback verified the enabled paused mix. Preferences are untouched. This is a verified audio path, not an acoustic listening grade.

HK previously used any peer menu as proof that Controls remained open. Controls closing to an underlying Root therefore could leave HK's Controls lease active and reject reopening. Visual wire 9 carries an explicit Controls identity (gameplay handshake wire 38); Root pixels and Controls ownership are separate. Missing menu pixels alone do not close the lease; room mismatch or session teardown clears it, while explicit Controls closure releases it even if Root stays visible. Selection recovery is restricted to a revealed, interactive, currently owned native group; it never force-unlocks native transitions.

Actual tests:

- Both plugin builds passed with zero warnings/errors. VisualChecks passed, including Controls identity roundtrip, invalid/actor/closed flag rejection. BridgeChecks passed with wire 38.
- 63a010c5 controller and a9b713ed keyboard passed two Controls capture/cancel/reopen cycles, selection/navigation, native Root Back Y glyph assertions, rebound action delivery, native Root stand-up/control and paired saved return. These precede the explicit Controls identity refinement.
- Final 1c56eeab controller passed the expanded flow: two captures/reopens; missing-image/no-pose lease injections; selection-loss injection; UI sound playing IDs and enabled paused RTPCs; actual native Root Back Y label; a further Controls open over an already-visible Root; Controls closure with Root still visible; HK Controls lease release while native pause stayed active; explicit resume, Root Back, native stand-up/control and paired saved return. Root glyph fixture restores original action-set bytes before native saving. All 26 vanilla/development fingerprints were unchanged; only disposable existing paired-save copies were used.
- f0ffc87d failed its second-cycle HK focus/pause assertion; both NS navigation cycles had passed before the failure. The exact focus event was not diagnosed; it is not counted as a pass.
- cb16ad23 established Controls-over-Root closure/lease release, then failed a fixture assertion that incorrectly kept requiring the Root menu to remain visible during the subsequently requested native Root exit. The corrected final fixture validates preservation once, then permits native exit/stand-up.

Final harness EOF/audio-restore warnings occur after HK's saved-return/completion receipt during process cleanup, as in prior runs; no independent NS cleanup or audio-restoration pass is claimed. Final keyboard-panel Controls-over-Root, real physical binding success/reopen sequences, controller styles, listening quality and restart persistence remain player validation. Native UI widgets/fonts/transitions are reused; no proprietary art is distributed. No full acceptance or public-release claim is made.


## October 13 — completed binding input release and bow density

The player e6951d67 run is preserved under ignored `.local/oct13-binding-b-regression/`, with both roles copied before testing. WeaponAttack binding completed at 03:48:09.974 UTC; Controls closed at 03:48:10.214. At 03:51:20.323 inventory opened during Controls reveal, and Controls closed eight milliseconds later. Earlier capture/cancellation fixtures did not exercise completed assignment followed by held Back.

NativeMenu now retains capture ownership beyond IsListeningForBinding until the captured native BindingSource and forwarded menu controls are released for 150ms. Persistent entry/pause/device metadata is excluded from physical-neutral detection. The original native validation/replacement/completion callbacks remain authoritative; repeated row submissions cannot restart capture. Controls blocks inventory shortcuts and UIManager.ShowMenu. Visual wire 10 carries capture ownership to HK, whose physical pause close is ignored during capture/release; ordinary Back can be the binding itself. Gameplay wire 39 rejects mismatched peers. Native settings/loadouts are not reset.

The bounded fixture substitutes only BindingSourceListener discovery, then runs the installed InControl DetectBindings replacement and ControllerMappingButton callbacks. It restores the original binding data before closing/saving. It verifies accepted B, held Back suppression, inventory rejection, neutral release, a fresh native Back to close, and repeat opening. This is native runtime coverage with injected discovery, not physical-controller hardware certification.

- b2f46754: passed two native B assignments/held Back/inventory rejection, explicit pause closure, Controls-over-Root and paired saved return (pre-final pause fence).
- 8866d21b: failed the stronger fresh-Back path because the release fence counted persistent EntryRevealed metadata as a held button. This was corrected; failure preserved.
- ec8ceb35: passed the final controller assignment/release/fresh-Back/reopen sequence twice, subsequent Controls-over-Root closure, native Root exit and paired saved return.
- 1e9c9223: keyboard fixture rejected Escape through native binding validation; no override of the game's reserved bindings was added.
- 6ae7ac66: final keyboard fixture passed two native F12 assignments, held Back suppression, release, fresh Back closure/reopen, Controls-over-Root and paired saved return. Physical key discovery was injected; native replacement/callbacks and forwarded navigation ran unchanged.

Bow history: f3f2670 introduced adaptive 96px/world-unit capture capped at 1024 square. 50954b1 subsequently retained arrow flights/secondary effects and increased arrow selection reach from 192 to 480 native units without increasing that ceiling. The player's bow capture grew to 22.194 world units, reducing density to about 46px/unit. The renderer, native effect selection, 2x supersampling and pixel transport are retained. Above the old ceiling, rectangular resolution tiers up to 2560 per axis keep the same density strategy without rendering a large empty square. Cached capture shapes remain bounded at six; pixel allocation/codec bounds are updated consistently. No projectile is hidden or shortened to reduce the crop.

- dd4ef1e9 passed reload but did not fire: the harness returned after its jump before its added bow stage. This is not bow evidence. The harness now requires an expanded-capture receipt before passing.
- 6ec06dc9 passed native bow firing, expanded density assertions, image capture and paired saved return. Logged expanded samples were 115.26, 111.78, 108.61 and 105.71px/unit at 1536×1024. The captured native bow/Yi artwork was inspected. The sampled expanded capture cost window averaged 7.2ms, max45.5ms, including fixture-only PNG writing; it is not a clean gameplay performance benchmark. Ordinary sessions do not write these PNGs. All arrow types, sustained combat load and in-motion grading remain player scope.

Both builds passed with zero warnings/errors. VisualChecks and BridgeChecks passed. A VisualChecks failure from its obsolete assumption that a 1664-wide image must exceed actor bounds was corrected for the expanded rectangular bound; malformed/oversized payload checks remain. All native tests used disposable copies of the existing mod-owned save and preserved the 26 protected vanilla/development fingerprints. Original saves were not changed. Harness cleanup EOF messages are not independent NS shutdown certificates. Existing user changes to BaselinePlugin.cs and its edit-conflict copy were preserved and excluded from this milestone.


## October 14 — visible reopening after B/Y, Root callbacks and bow cost

The player clarified that the first Controls opening is visible and subsequent openings are not. Run 2997d063 reproduced an active/input-responsive panel with empty HK and NS Controls screenshots. Prior logical/input receipts must not be cited as proof of visible reopening. Native Options navigation hides the preceding parent group; the borrowed Controls panel inherited that hidden parent. Controls now temporarily moves to its existing native UI-Canvas, preserving/restoring its original parent, sibling and RectTransform. Fonts, artwork and transitions remain native.

Rest introduced a second independent failure: 05e48f66 recorded the pending open command expiring after 250ms while native Rest/save work delayed consumption. Safe neutral menu-open requests now renew at most every 100ms with fresh identities; movement/combat renewal is prohibited. The request remains pending until native pause/readiness permits opening. Navigation waits for the actual current group's authored reveal and release of the opening input.

Root selected/current defaults no longer reset just because their parent is temporarily hidden. Native OnSelect now calls the existing AutoDisableAnimator.SetDirty API, matching native Submit/Deselect behavior. The previously selected row's frozen highlight caused the apparent row offset. No row labels/callbacks were remapped. Fixture probes record actual native callbacks and wait for selection animation before screenshots.

| Run | Actual result |
| --- | --- |
| 2997d063 | Logical post-Root reopening/jump occurred, but screenshots were invisible and a final-return harness stage collision failed cleanup. Not a pass. |
| fcd7839b | Canvas lease passed visible subsequent opening, retained-swap Root/reopen/jump and saved return, before all-row probing. |
| 74c90a62 | Status callback passed; fixture inventory-transition assertion failed. |
| a6b4202f / 05e48f66 | All Root callbacks passed; post-Rest opening timed out. The latter proved request expiry; failures retained. |
| e938dfe8 | Final full Controls flow passed: temporary Hack=Y/WeaponAttack=B retained through Root; Status→Status, Jades→Jade, Skills→SkillTree, Rest→Restore native callbacks; row highlight images inspected; actual rendered post-Root Controls in HK with Y/B bindings; fresh Back closure, native jump and paired saved return. Original runtime bindings restored before final saving. |
| d6cc66bf | Final reload/bow firing, retained density and paired saved return passed. |

All four Root callback probes submit the actual native button directly; binding-source discovery is injected. These are bounded native-runtime tests, not physical-controller or restart-persistence certification. The visibility oracle now requires rendered central pixels and native alpha; sound/state alone cannot pass Controls readiness. Visual proof is preserved in ignored e938dfe8/HollowKnight/post-root-controls.png and NineSols/root-choice-*.png. Both games start concurrently from disposable existing-save copies and finish on completion/failure. Each final run preserved all 26 vanilla/development fingerprints.

Bow resolution/renderers/effect ownership and simulation cadence are unchanged from the density fix. Pure straight-alpha conversion moved from the native gameplay thread to the existing visual channel worker, before checksum/send. Final expanded capture averaged 4.304ms (readback mean 2.529ms, max total 33.404ms), including fixture-only PNG writing. Settled idle windows averaged 1.180–1.452ms. Earlier player windows averaged 7.274/5.603ms under different workload/logging; these are not a clean A/B or whole-fight FPS proof. GPU rendering/readback and remaining capture overhead are still present; sustained bow performance remains player grading.

Both plugin builds passed with zero warnings/errors and final builds were deployed. BridgeChecks passed including fresh neutral menu renewal after a busy consumer and rejection of combat/movement renewal. VisualChecks passed including worker premultiplied conversion. No new whole-fight acceptance, foundation-completion or public-release claim is made. Existing unrelated BaselinePlugin edit and conflict copy remain excluded from the commit.
