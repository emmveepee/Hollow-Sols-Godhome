# Save-load handoff and application quit — October 13, 2026

Dated evidence; UI06/G12 in [the canonical register](ROSTER-COMPLETION.md) own current status.

## Cause and correction

HK StartSelected used the Back/menu-close routine before native load. Close restored title CanvasGroups, logo/decorations and focus, exposing the Bridge button during asynchronous loading. ActivateBridge only special-cased Connected/SaveSelection: the stale callback attempted Activate in Loading, failed lifecycle validation and routed into session failure.

BeginLoading now clears the owned card panel while retaining hidden title ownership. Native title groups remain invisible and noninteractive, with enforcement before render; title and DLC decoration stay hidden. Restore them on native title return/explicit cleanup. Actual game loading/fade code and save paths are unchanged.

The shared activation boundary allows only idle/retry or connected menu re-entry without an active token. Waiting/loading/active/ending/disconnect/quit duplicates are no-ops; stale slot and Back callbacks cannot abort or replace an active session. Title Bridge selection is also disabled during loading. A bounded native fixture injects two activation callbacks and verifies unchanged session, connection and transport, then checks six rendered title frames.

Quit previously checked its deadline after menu/session Update work. Repeated cleanup exceptions could skip that code every frame. Explicit quit now has a ten-second monotonic/unscaled bound checked before fallible Update and by CanQuit itself. Normal coordinated final saving remains attempted. At expiry, permit native application exit and report final save unconfirmed; prior committed checkpoints remain intact. This requires Unity's main thread to keep running; it is not an OS force-kill mechanism or a promise to recover a frozen engine.

## Validation

Final HK/NS builds pass with zero warnings/errors. Existing-save reload **ab6dd9a7-669a-4c73-a086-518408debb53** passed duplicate-callback identity checks, six-frame title hiding/noninteraction, active gameplay and paired saved return. The inspected handoff screenshot contains native background only, with no title menu/buttons/logo. All 26 vanilla save fingerprints unchanged. Original saves were copied into a disposable mod namespace.

NS production-exit fixture **e9d78134-c757-46a4-9a9c-2e6cb95e8701** passed: normal ApplicationCore exit used the production quit guard, coordinated a newer paired generation, restored vanilla/menu state and closed without Task Manager. All 26 vanilla fingerprints remained unchanged. The repeated-cleanup-exception deadline path is source-reviewed, not fault-injected in a native process. Player retest remains for the exact original mouse/controller timing and failure-state exit. No gameplay/combat or save-schema change, migration, release or new save creation occurred.
