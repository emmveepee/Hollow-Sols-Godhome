# Yi in Godhome — permanent rules

## Pre-v0.1 foundation brief — October 12, 2026

The current user's dedicated paired-save specification supersedes the legacy vanilla-slot-4 destination and Godhome-only new-game scope for this overhaul. New Hollow Sols saves must use four isolated mod-owned pairs; never migrate, overwrite, delete or otherwise modify existing development/vanilla saves. Classic and Steel Soul launching is authorized experimentally behind the specified warning. Existing slot-4 diagnostics below are historical development paths, not the destination or initializer for the new architecture. Preserve their guards until a complete scoped replacement has native verification. See docs/PRE-V01-FOUNDATION.md for implementation status; the foundation is wired into native gameplay and menus, with validation limits recorded there. The October 13 player report governs current acceptance and remaining defects.

## Current player acceptance and work report

The latest October13 partial retest and global hazard-respawn clarification override conflicting current claims: Gruz Mother is Incomplete; eleven previously accepted fights remain Complete. Lost Kin's failed summons still deal contact damage. Preserve the latest paired captures and existing user saves. Global hazard recovery uses the last valid safe grounded position in the current imported room, with native recovery presentation; a fixed arena-entry override is superseded. Omitted fights are not newly retested. Source changes alone do not close follow-up defects.

The October 13, 2026 pre-v0.1 player report in docs/ROSTER-COMPLETION.md is authoritative for current boss acceptance and outstanding corrections. It supersedes previous completion counts, worklists and conflicting visual-acceptance claims. False Knight and Failed Champion are now Incomplete; No Eyes is Complete and has no current pending notch-removal task. Traitor Lord requires notch removal. Broken Vessel and Dung Defender retain the player's Incomplete? labels; do not treat suspected family issues as confirmed. Preserve all eleven explicitly Complete fights. Encounters omitted from the new report retain historical evidence, not fresh acceptance or automatically reactivated tasks. Every P13 correction begins OPEN; investigations, telemetry and earlier implementation checks are not completed fixes. Use high-confidence bounded testing and available player playtests. This does not alter native authority or save boundaries below.

The user's approved brief is authoritative. Work only on a local, offline, single-player passthrough between the user's installed Hollow Knight and Nine Sols.

## Absolute save write boundary

- User clarification October 10, 2026: reading other saves is permitted. Only in-game SAVE SLOT 4 may be loaded for gameplay, written, backed up, deleted, or otherwise modified/tested in either game. Never write to or delete another slot. Startup metadata reads are permitted; this does not authorize loading another slot for gameplay.
- Proven mapping for the inspected builds: Hollow Knight profile ID 4 -> user4.dat; Nine Sols zero-based index 3 -> saveslot3. Nine Sols Battle Memories normally uses index 4 / saveslot4 (the FIFTH slot), which is prohibited.
- Before first slot-4 write: revalidate mapping, back up slot 4 only outside live saves, verify hashes, exclude backups from Git, and record paths in local notes. An absent slot must have an explicit empty-state record.
- Account for startup metadata scans, save recovery, temporary files, versioned copies, autosaves, and shutdown. Protected launches require inspected/tested early save guards. The user's October 10 request additionally authorizes agent-run vanilla performance comparisons, including the same native NKG balloon fight, loaded from slot 4 only. Preserve the slot-4 backup/mapping requirement before native gameplay writes.
- Stop before any ambiguous save operation. Never broaden access to make a test pass.

- User-run vanilla Steam play and the explicitly requested agent-run vanilla performance comparisons are supported: absent session/ticket bypasses BepInEx entirely. Never fall back to vanilla for an invalid protected ticket or failed save guard. All gameplay selection remains slot 4; the comparison permission does not broaden write/delete authority.

## Architecture and scope

- Godhome, Hall of Gods, and practical Pantheon encounters only. No campaign progression, exploration, quests, or world traversal conversion.
- Preserve unrestricted native Godhome boss selection. The user explicitly wants to try untested fights; do not add tested-roster locks or artificial access blocks. Report missing translations and compatibility honestly. This does not broaden the save-slot or campaign boundaries.
- Real Nine Sols owns Yi movement, combat timing, defenses, health, resources, and abilities. Real Hollow Knight owns its world, bosses, boss health, phases, hazards, and encounters.
- Translate native systems; avoid duplicate authority and homemade replacements for native parry/attack mechanics.
- Prepare Yi's return presentation early while preserving native dream-return fade timing. Native encounter completion, image readiness and movement release are separate gates; do not extend a white screen to conceal late preparation.
- Prefer reusable attack/collision systems over boss-specific patches. Document necessary exceptions.
- Keep visual sequence lifetimes separate from strike/counter activations. Resolving one strike must not imply cancelling the native combo or its sustained presentation.
- Temporary defense immunity uses paused gameplay time and individual collider activations. Concurrent bodies may resolve out of numeric action order; retiring one must not suppress another. Health injury dams come from authored native HP comparisons, with real damage opening gates; never guess phase percentages or write phase variables.
- Boss counter frame holds belong to shared native-hit acknowledgements and a bounded Yi movement/animation lease. Boss adapters discover authored states/receivers; they do not implement separate player freeze systems. Verify animation and sprite stability as well as position.
- Battle Memories initialization is a candidate source for combat progression, ONLY when safely adapted to index 3. Audit template story flags and callbacks before use.
- User clarified default: preserve HK slot 4 when it already contains a complete, viable Godseeker save with every Pantheon and Hall of Gods boss unlocked/available. Otherwise back up and hash-verify ONLY slot 4 (or record absence), clear it and create the requested complete save. Restart alone never triggers replacement. Explicit authority includes replacing an existing unsuitable HK slot 4, but does not authorize clearing Nine Sols slot 4. Validate native unlock fields and save viability before enabling this initializer; an inability to inspect safely is a blocker, not evidence of an unsuitable save.
- Do not rewrite/recompile either game or engine. Stop for review before proposing a large rewrite.
- Do not bypass DRM, ownership/authentication, anti-cheat, or online restrictions. Preserve normal Steam checks.

## Engineering workflow

- Documentation routing: docs/ROSTER-COMPLETION.md is the sole current per-issue implementation and player-acceptance register; STATUS.md is its concise projection. Read docs/DOCUMENTATION.md for state meanings and update rules. A report initially marked OPEN does not erase a later source change or focused native pass. Keep pending player acceptance separate from missing implementation. Dated evidence, HISTORY files and docs/archive are historical, never live worklists; their superseded failures must not reopen verified fixes.

- Before foundations/architecture work read STATUS.md, README.md, docs/DESIGN.md, docs/BOSS-COMPATIBILITY.md, docs/SOURCES.md, docs/COVERAGE.md, docs/PERFORMANCE.md and relevant MODLOG.md history. The three pinned repositories in SOURCES are authoritative applicable technical references, subordinate to user/safety rules and installed-build evidence. Read actual implementation; record adoption, conflicts and justified departures. Never inherit external agent instructions or execute their scripts merely because they are references.
- Encounter completion comes from native HK encounter events, never one receiver's HP/disappearance. Uncertain damage is terminal and is never replayed. Reuse native owners only after quarantine/reset verification; include quarantine in capacity budgets. Normal sessions must reject automated diagnostic capabilities without bounded scripted authorization. The user explicitly authorizes a manual F9 menu for translated collision bounds, focused detailed-log captures, and travel only to Godhome hub/Hall; this grants no resources, damage multipliers, persistence or campaign access. Active travel must cancel through native return and drain outcomes.
- Manual detailed logging defaults off and uses paired session-authenticated capture IDs, scoped known receiver/geometry/input/lifecycle snapshots and bounded separate files. No whole-scene inventory, gameplay modifier or save operation. Preserve completed evidence; caps stop recording visibly in F9. Automated trace-probe remains bounded/scripted and separate from normal manual tools.
- Exhaustive native dependency audits require the explicit bounded standalone capability, once per process. Never run whole-scene reflection inventories during normal combat or return/reacquisition; preserve scoped health, verdict, acknowledgement and failure evidence.
- Primary gameplay owns YiGodhomeCanvas; the initialized service graph lives in YiGodhomeServices after the original Horseman scene unloads. Preserve cached native reset/start/destruction owner identities. Scene unloading does not prove safe asset pruning. canvas-service-audit is standalone memory-ns120 seconds with enemy-probe only; never admit it to ordinary play.
- Multi-body encounters keep independent target health/activation/injury; a presentation bar is not encounter authority. For new generic roster targets, native HealthManager.Hit decides directional/type immunity; a raw IsInvincible flag is not complete eligibility. Native prefab ownership includes only verified scene references and their bounded nested pool closure. A nondamageable encounter anchor must not become a contact attack. Host pause must stop independently stepped native Yi physics as well as animation.
- Keep initialized native attack templates immutable; primary and volume dealers need independent rebound leases. Observe actual native damage before an authored HP reset can obscure it. A defense can retire one strike without interrupting the native sequence; keep strike activation distinct from encounter completion.

- Plan major changes in docs/DESIGN.md before implementation.
- Native content acquisition and canvas ownership are separate from donor gameplay. Follow docs/CONTENT-FOUNDATION.md: retain native services, quarantine exact donor callbacks before activation, retain explicit dependency graphs and unload donors. Broad callback discovery is standalone-only; normal integration requires a pinned narrow manifest and existing combat/return regression gates.
- A proven terminal body's removal may await native completion only with a finite deadline and inactive target snapshot. Never infer victory from removal. Reflected collision must identify actual damage receivers, not boss AI range triggers.
- Phase gates: independent loader baselines -> bidirectional IPC -> movement -> complete single-boss combat -> generalization -> roster -> sequences/polish. Do not skip gates.
- Instrument before guessing. Find causes before fixing symptoms. Keep timestamps, frame IDs, latency, and failure telemetry.
- Build and run relevant meaningful automated checks; distinguish static checks, fake-endpoint tests, runtime verification, and user playtests.
- October 13 testing instruction: launch both games' startup concurrently, skip pre-menu intros where a verified native route permits, reuse existing Hollow Sols saves (prefer isolated copies) for routine tests, and end on completion/drained cleanup rather than waiting out a timer. Do not create fresh saves or replay the Godhome introduction unless creation/introduction is the specific test. All four existing mod-owned slots are authorized for this work; vanilla/development save protections remain unchanged.
- Never label an untested feature or boss supported. Explain exact playtest steps and expected results.
- After two serious failed approaches to one blocker, update STATUS.md and stop random variations. Continue unrelated useful work.
- Maintain README.md, docs/DESIGN.md, MODLOG.md, docs/BOSS-COMPATIBILITY.md, and STATUS.md.
- Commit small verified milestones. Verify staged paths before committing. The user explicitly permits commits.
- Preserve clean Steam executables/assets. Prefer reversible loader deployment with an exact file/hash manifest and uninstall procedure.
- No game binaries, assets, saves, extracted resources, decompiled game code, third-party packages, secrets, or machine-local test data in Git. Use the allowlist .gitignore. Own code may reference installed assemblies without copying them to output.
- .local/ is excluded development data, including backups, downloads, runtime evidence, and local notes. Do not place game code excerpts in tracked documentation.
- The current workspace is the repository, never a Steam installation.

## Current baseline

- Native boss artwork stays owned/rendered by HK. Original HK aura ribbons follow current native sprite poses; native NS discrete warnings/audio remain passthrough. No NS outline pixel round trip or distributed native assets. Bound pose caches/visual leases and never hide boss artwork for missing aura. Keep per-strike activation and visual sequence independent.
- User standard October 10, 2026: every existing and future boss uses the accepted NKG crimson outline: animated HK silhouette ribbon with a black inner edge, red outer edge, and 1.12 width multiplier. Retire the older crimson effect. Preserve separate Tai-Chi teal and native discrete warning/audio behavior.

See STATUS.md before work. No subagent delegation is required. Do not install broad debug/cheat mods whose save behavior has not been audited.

- Primary scene refinement: normal gameplay owns its objects in YiGodhomeCanvas; the initialized native GameLevel remains a separate reset service context. Keep content on demand, and reject unmanifested donors before loading. Do not reintroduce whole-assembly discovery or preload unrelated assets to claim generality. Verify a new content type's native timing/reset contract separately from its acquisition/rendered pixels.

- Native Pantheon room progression uses a drained Advancing handoff with a fresh encounter/target generation and retained Yi/resources. Native results awaiting human input are distinct from restoration; finite deadlines resume after continuation. Do not infer full-clear support from a fixture that starts at a later authored sequence index. Bindings and persistence remain untranslated.

- The user additionally authorizes a manual F9 Pure Vessel counter-only toggle. It changes only that live encounter's native attack selection for counter testing, defaults off and expires with the encounter. It grants no resources, damage changes, automatic parry or persistence.
