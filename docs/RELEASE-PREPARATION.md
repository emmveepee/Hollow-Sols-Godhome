# v0.1.0 release preparation

This is a preparation checklist, not a release certificate or gameplay worklist. [ROSTER-COMPLETION](ROSTER-COMPLETION.md) owns implementation and player acceptance. No public release, tag or upload is authorized by this document.

## Current staging decisions

- Destination: `https://github.com/emmveepee/Hollow-Sols-Godhome`, verified **private, empty and Issues-enabled** on October17. Cleanup is committed; the user authorized continuing candidate preparation and private source staging. Public visibility, tags and release publication remain separate actions.
- The user selected a **clean reviewed source snapshot with new Git history**. Preserve the original local repository/history, unrelated staged controls-fixture edit and conflict copy. Do not push development history or include that staged hunk automatically.
- Source cleanup removes superseded HISTORY/STATUS snapshots, the old documentation archive and two legacy encounter launchers from the tracked release tree. Hash-verified originals remain local; see [historical evidence](DOCUMENTATION.md#historical-evidence). Current contracts, canonical acceptance, focused evidence, save guards and referenced developer tooling remain.
- Public builds default new report configurations to this repository; development defaults remain empty. Existing values and installed configurations are unchanged. The source issue form must reach the default branch before browser-assisted submission testing.
- License choice: **GPL-3.0-or-later** for original project-owned material. Retain third-party terms; do not invent a linking exception or claim legal clearance.

## Source review

The initial technical scan covered 597 tracked files and4,393 reachable blobs across358 development commits (127,786,790 blob bytes). No prohibited binary/asset/save path was found in the current tree. History contains a conflict-copy entry, personal interpreter paths and author contact metadata; it must not be uploaded wholesale. Credential-like content flagged in source is synthetic redaction test data. Current personal interpreter paths are replaced by a configurable Python command.

This bounded scan is not exhaustive secret detection or proof of ownership. Review the final snapshot itself, its commit identity and file manifest before upload. Use a GitHub no-reply address for the snapshot's new history. Preserve exact protected-file/staged-patch hashes in ignored local notes.

## Current build and compatibility contract

Use the explicit `-PublicRelease -NoRestore` profile in [BUG-REPORTING](BUG-REPORTING.md); select named outputs rather than copying build directories wholesale. Public outputs/receipts are separate from development builds. Public builds exclude F9/debug rendering and detailed activation while retaining ordinary diagnostics. Existing development checks remain available without enabling public fixture startup.

Current source schemas are control6, gameplay52, contact38, hit outcome6/ack3, visual15, geometry7, paired store1, native bundle1 and native exchange1. [DESIGN](DESIGN.md#component-boundaries-and-wire-compatibility) owns these contracts. Historical evidence keeps its original numbers. No save format, gameplay protocol, dependency, deployment or installed environment changes are part of cleanup.

Before the cleanup follow-up, both roles built in both profiles with zero warnings/errors; eight isolated suites and public-artifact checks passed (P17-REL01). Native rendered reporting, real issue submission and recovery were not established by those checks. Rebuild the exact reviewed source after excluding unrelated edits; dirty working-tree binaries are not a matching-source release.

Cleanup follow-up: all four role/profile builds pass, public artifact checks verify the configured repository and debug/capture exclusions, BridgeChecks and PairedSaveChecks pass (4,207 harness assertions plus report checks), all 170 PowerShell scripts parse and remaining Markdown file links resolve. Preservation checks confirm all 71 archived originals, the staged patch and conflict copy unchanged. These builds include the user's preserved staged hunk; they are validation outputs, not release assets. The clean source candidate excludes that hunk and requires its own matching-source build before binary distribution.

## Distribution allowlist

### Candidate construction contract

Build a clean exported source snapshot, excluding the unrelated staged hunk, with version0.1.0 and mapped compiler paths. Use only already provisioned dependencies/SDK cache; record that this is not a fresh-machine reproduction. Build Bootstrap, SaveGuard and both public plugins from that source. Receipts identify the exact source revision/tree, cleanliness and artifact hashes. Packaging refuses dirty source, failed/stale receipts and mismatched assembly identities. Source and binaries must identify the same new clean-history commit used for private GitHub staging.

Create one candidate ZIP with separate `HollowKnight`/`NineSols` game-root payloads: four project DLLs, the exact native-menu marker and clean authored Doorstop/BepInEx configuration per role. Include an explicit SHA-256 manifest, a read-only installation verifier, player instructions, project license/notices and matching source ZIP. BepInEx/Doorstop binaries are external prerequisites, never copied into this package. Existing installation/configuration conflicts require deliberate review/backup; packaging itself never writes to a game directory or save location. Removal instructions restore prior loader configuration and remove only unchanged named project files, leaving saves and external loader ownership alone.

Validate package bounds, paths, file allowlist, versions, checksums, source identity, exclusion of debug/capture paths and compiler machine paths, plus extraction and verifier rejection/success cases on synthetic disposable roots. Do not infer actual installation/gameplay/recovery or browser-report success from these offline checks. Upload only the clean reviewed source history to the verified private repository; the candidate ZIP stays local until separately authorized release staging.

Candidate validation passes: Bootstrap, SaveGuard and both public plugins build from a clean source checkout using75 previously provisioned SDK cache files; no restore/install/download is involved. All eight isolated suites pass (PairedSave4,207 harness assertions plus reporting checks), along with public version/profile/repository inspection. The mod-only ZIP passes exact21-file allowlist, all checksums, matching source/exclusions and synthetic two-role read-only verifier success/tampered-file/native-hash/traversal rejection. Dirty-source, stale-receipt and existing-output refusal preserve existing artifacts. This does not establish native installation, browser submission, gameplay recovery or fresh-machine reproducibility. No live installation/configuration/save was modified; existing private playtest ZIPs remain unchanged.

| Deliverable | Allowed content |
| --- | --- |
| Source snapshot | Reviewed original `src`/`tests` sources and projects, authored tooling, necessary config/build files, current documentation, LICENSE and retained third-party notices; the exact issue form/config. Explicitly exclude conflict copies, removed history and ignored local evidence. |
| Project binary candidate | Matching `HollowSols.Foundation.dll`, `YiGodhome.Bootstrap.dll`, `YiGodhome.SaveGuard.dll` and the appropriate game's plugin DLL, plus reviewed native-menu marker/config, build identity/hashes, licenses/notices and installation/removal instructions. |
| External loader | Default public candidate requires the loader separately. A bundled variant needs an exact component allowlist and completed notice/source/replacement review. The prior private bundle does not establish public redistribution clearance. |

Always exclude `.git`, `.local`, caches, `bin/obj`, personal configs, deployment receipts, absolute machine-local evidence, secrets, tickets, reports/logs, captures, saves/checkpoints/backups, extracted/decompiled game material, game/engine/proprietary SDK assemblies and third-party package caches. Do not use old update ZIPs as standalone installers. Archive source and binary identities must match; include Corresponding Source and required notices as applicable.

## Remaining gates, in order

1. Review the cleanup diff and final source snapshot; resolve ownership/provenance questions. Confirm the staged edit/conflict copy remain untouched and excluded.
2. Finalize consistent v0.1.0 build identity and build the exact candidate. Review source and binary manifests, GPL declarations and third-party integration/distribution questions; see [THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES.md).
3. Reproduce contributor builds and extract/install/uninstall the final package on a clean provisioned machine with legitimate supported games. Existing SDK caches and same-machine builds are not clean-clone proof.
4. Investigate the documented two-attempt native menu-control connection blocker before further scoped launches. Test session entry, save/checkpoint behavior, native disconnect recovery and the geometry return correction using isolated existing paired-save copies. Do not replace pending player acceptance with automated results.
5. After separately authorized private source upload, test the issue form and one complete browser-assisted report with the exact public candidate. Inspect/redact the ZIP and attach/submit deliberately. Testers need private repository access.
6. Review the final installation/removal instructions, known limitations, assets, checksums and matching source; prepare a draft release. Public visibility, tag/release creation and publication remain separate authorized actions.

## Preserved private packages and evidence

The October16 full friend bundle and October17 minimal update ZIPs are private development artifacts, not final public v0.1.0 candidates. Later source/public-profile changes do not update those ZIPs or live installations. Their original manifests, hashes, dependency source/notices and dated packaging evidence remain locally preserved and in development history. Do not rebuild, overwrite or distribute them as a side effect of cleanup.
