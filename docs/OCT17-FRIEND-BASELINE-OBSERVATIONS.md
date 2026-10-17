# October17 friend baseline observations

This is a read-only review of the supplied 01:20 log archive from the original October16 private playtest build, explicitly before P17-IO01/UI01/DBG01. It is not updated-build validation or new fight acceptance. Raw logs and local analysis remain ignored under `.local/friend-latest-2026-10-17-0120` and `.local/friend-latest-analysis-0120`.

## Final disconnect

Session d7eb5fdc runs from05:19:09UTC to06:20:26UTC, about61minutes17seconds. HK's module identity matches the earlier failing original package. At06:20:22.934UTC, NS records Defeated in the Pure Vessel encounter; HK enters GG_Workshop at06:20:25.867UTC. At06:20:26.2771674UTC (01:20:26CDT), HK terminates with HK_HUB_FAILED / IOException / Unable to remove the file to be replaced, through File.Replace -> GeometryExport.Save -> HubPresence.CaptureGeometry. NS records PEER_DISCONNECTED at06:20:26.2801703UTC, about3ms later.

This is the same transient geometry-publication failure signature addressed by P17-IO01. It is not a new terminal cause or evidence that the update failed. The OS reason for replacement refusal remains unproven. Earlier same-process BepInEx records also contain a short failed load,4d5c7b33, with that same File.Replace stack, followed by recovery/reconnect noise. The final return followed player defeat, not boss victory.

## Corroborating input-prompt evidence

The retained NS tail contains seven DeviceBindingSource/XboxOne_B -> KeyBindingSource/F_Key_Dark pairs, each lasting approximately0.10seconds. Twenty-three queued input expirations are recorded in the retained samples; several prompt switches coincide with return/collection/input-expiry windows. For example, the controller glyph appears06:14:25.6238156UTC, expired input is drained06:14:25.6278127UTC, and keyboard presentation resumes06:14:25.7255116UTC. This matches the stale-neutral/absent-keyboard-flag source path addressed by P17-UI01. These are pre-fix observations, not a post-fix failure or proof that a physical/virtual controller was present.

## Performance observations

Retained FRAME_PERF windows total956 in HK and631 in NS. Their frame-weighted means are6.989ms/7.038ms respectively, roughly143/142FPS. These are different retained time ranges, not a complete-hour or matched-window benchmark. Rotated files preserve approximately the last31minutes for HK and21minutes for NS, plus short startup captures.

There are isolated mid-fight spikes, including Sisters-room HK94.585ms/NS85.308ms near06:06:00UTC and HK92.367ms/NS84.642ms near06:05:36UTC. Those summary windows record no garbage collections in either process and lie well away from recorded room/terminal transitions. Sly has a later HK67.931ms/NS65.207ms pair, with a collection in NS. These are frame-spike observations, not established causes; native hit effects, rendering/transport, scheduling and logging cannot be separated by these summaries alone. Do not attribute every spike to GC or reopen accepted combat mechanics from this evidence.

The largest retained HK summary maximum is121.502ms in a Workshop return window; NS reaches201.673ms at startup and165.857ms during a return. Fade-contained loading/return work must remain distinct from visible combat stalls. Managed memory fluctuates and drops after collection; these logs do not establish a monotonic leak or physical-memory exhaustion. Process/private-memory counters are unavailable.

## Persistence and diagnostics

BepInEx logs record49 successful paired checkpoint commits, generations8 through56, after loading generation7. Final generation56 commits before the disconnect. Background publication wall time rises from roughly91ms early to392ms late; measured HK game-thread preparation never exceeds0.230ms. NS native capture reaches114.882ms separately. These measures do not prove that background publication blocked visible gameplay. No save-store exception, save-guard failure, missing dependency or protocol/version-mismatch signature was found. No save bytes were supplied, so independent integrity verification is unavailable.

CONTACT_STOP/Pipe is broken and HIT_STOP/EOF messages recur during encounter disposal and subsequent returns/rechallenges. They are not additional whole-session disconnects: the paired session continues afterward, and only the final geometry exception terminates this retained session. Native duplicate-owner/expired-talisman cleanup and rejected-contact records are guard/verdict telemetry, not by themselves new defects.

The optional SendRandomEvent/eventMax reflection warning is guarded by the existing nullable-field branch; it is not the terminal failure. Two UIManager CanvasGroup/FadeInCanvasGroup null-reference exceptions appear during earlier recovery/reselection, also seen in the first archive; later loading succeeds. Preserve them as nonterminal menu-cleanup evidence rather than attributing the final failure to them.

HK logs contain embedded NUL/control bytes inside PlayVibration highFidelityVibration diagnostic field values. The inspected instances are authored vibration-data strings, not demonstrated file corruption or evidence of an attached controller. Text-forced searches are required because default binary detection can hide later matches. Existing rotation gaps limit the observations; no new runtime code, game launch, save access, deployment or package change was made for this review.

Current implementation/acceptance states remain in ROSTER-COMPLETION. P17-IO01/UI01 need updated-build friend evidence; this old-build archive cannot close or reopen those validation gates.
