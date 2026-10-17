# Current acceptance and limitations

This is the sole current acceptance register. Source, isolated tests, native samples and player approval have different scopes. Older issue IDs remain useful historical lookup keys, not automatic pending tasks.

## Whole-fight player acceptance

**All 44 encounters below are Complete:** playable, working and satisfactory, approved by the player on October17, 2026 and reaffirmed for this cleanup. This supersedes earlier incomplete grades and pending whole-fight retests. Future fight work is expected to be minor fixes or balancing driven by new playtest feedback. There is no active boss-fix backlog inferred from old reports.

| Encounter | Status |
| --- | --- |
| Gruz Mother | Complete |
| Vengefly King | Complete |
| Brooding Mawlek | Complete |
| False Knight | Complete |
| Failed Champion | Complete |
| Hornet Protector | Complete |
| Hornet Sentinel | Complete |
| Moss Charger / Massive Moss Charger | Complete |
| Flukemarm | Complete |
| Mantis Lords | Complete |
| Sisters of Battle | Complete |
| Oblobble / Oblobbles | Complete |
| Hive Knight | Complete |
| Broken Vessel | Complete |
| Lost Kin | Complete |
| Nosk | Complete |
| Winged Nosk | Complete |
| The Collector | Complete |
| God Tamer | Complete |
| Crystal Guardian | Complete |
| Enraged Guardian | Complete |
| Uumuu | Complete |
| Traitor Lord | Complete |
| Grey Prince Zote | Complete |
| Soul Warrior | Complete |
| Soul Master | Complete |
| Soul Tyrant | Complete |
| Dung Defender | Complete |
| White Defender | Complete |
| Watcher Knights | Complete |
| No Eyes | Complete |
| Marmu | Complete |
| Xero | Complete |
| Markoth | Complete |
| Galien | Complete |
| Gorb | Complete |
| Elder Hu | Complete |
| Brothers Oro & Mato | Complete |
| Paintmaster Sheo | Complete |
| Nailsage Sly | Complete |
| Pure Vessel | Complete |
| Grimm | Complete |
| Nightmare King Grimm | Complete |
| Absolute Radiance | Complete |

## Confirmed release-facing samples

- Public candidate builds and compiled-artifact checks exclude F9/debug rendering and detailed-capture activation; ordinary logs remain.
- Candidate packaging/checksum/source and synthetic install-verifier checks passed. Both roles were installed locally; this is not clean-machine installation coverage.
- Browser-assisted reporting produced a local ZIP containing both roles' ordinary logs. The submitted test issue has a matching incident/build and ZIP attachment link. Remote attachment bytes were not independently compared. The player confirmed the shortened installed report notice fits (P17-REL01/02).
- Native Controls hover/click and Esc handling are player accepted. Keyboard rebinding without switching away from HK is player accepted (P17-UI02/03).

## Independently unresolved scope

These are validation gaps or scoped follow-ups, not evidence that accepted fights are broken. New reports should identify a build, reproduction and observed result before reopening mechanics.

| Area / historical lookup | Present evidence and next action |
| --- | --- |
| Jades | Yi starts fully upgraded, but not every jade has been tested. Some may behave incorrectly or fail entirely. Player approval of the boss roster does not establish compatibility of every jade/loadout. Use new playtest reports to identify specific failures. |
| Save safety and failure recovery | Paired store/recovery has synthetic coverage and earlier scoped native samples. Verify the final candidate's committed checkpoint, visible warning, cleanup/menu return and reload after an unexpected failure using isolated existing paired copies. Successful report export does not establish successful recovery. |
| Remaining reporting variants (P17-REL01) | Synthetic coverage includes missing logs, redaction, bounds, locks, interrupted/repeated export, restart and launcher failures. The native submitted-report sample does not establish every role/failure/restart/offline variant. Scope remaining tests to those gaps. |
| Reconnect/cancel/late load (P16-LC01–03) | Reconnect fixes and bounded repeated-load/native cancellation samples exist; physical timing, vanilla-selection return and further same-process reloads remain separately scoped. Preserve drain/service readiness; do not label this missing implementation. |
| Transient geometry publication (P17-IO01) | Atomic replacement/deferred contention handling is implemented and isolated-tested after prior room-return failures. Final-build contention/room-transition validation remains pending. Two older automated menu-connection attempts stopped before this path; later manual reporting proves a connection can be made, not that those automated failures were diagnosed. Do not repeat speculative harness variations. |
| Input/UI hardware (P17-UI01/03) | Device-hint retention and mouse capture are implemented with isolated checks. Keyboard acceptance does not certify mouse no-focus-switch rebinding, physical controllers, other resolutions or display scaling. |
| Entry/menu variants (P13-UI03/04/07/09, P16-G01/G04/UI08) | Loading/inspection has scoped native evidence. Fresh-save arrival suppression, native Godseeker menu unlock and roof round-trip/airborne entry have source changes and limited coverage. Verify only the relevant variant; ordinary tests should not create new saves. |
| Progression/lifecycle persistence (P16-PROG01, P13-G08) | Non-creating native title lookup and scoped expired-deposit handling are implemented. Preserve saved loadouts/upgrades through repeated menu/return cycles; extended-session persistence coverage is not implied by the fight roster. Old fight symptoms do not justify speculative rewrites. |
| Experimental modes and sequences | Classic/Steel Soul are experimental; native permanent death and paired retirement need final-candidate coverage. Full Pantheon clears, all difficulties/bindings and challenge persistence are not certified by individual fights or late-sequence fixtures. |
| Developer-only contours (P17-DBG01) | Grounded-parry display correction has source/isolated checks; final visual retest is pending. It is excluded from public builds and is not a public release blocker. |
| Distribution and reproduction | Clean provisioned-machine build/install/removal and final provenance/integration review remain open. Follow RELEASE-PREPARATION and THIRD-PARTY-NOTICES. |

Historical performance reports do not reopen the accepted roster. Existing bounded telemetry supports future visible-hitch reports; [COVERAGE](COVERAGE.md) defines measurement standards. No new endurance or balancing project is implied by consolidation.

## Updating this register

Keep new entries short: affected behavior, current build/source state, exact evidence scope and next action. Remove resolved work from the current table while retaining the decision/contract and Git history. Player acceptance is authoritative within its stated scope; do not convert a synthetic pass into a broader native claim.
