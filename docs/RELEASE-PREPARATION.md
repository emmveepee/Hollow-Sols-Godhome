# v0.1.0 release preparation

This is the packaging/review procedure. [Acceptance](ROSTER-COMPLETION.md) owns passed samples and outstanding validation; [THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES.md) owns licensing/provenance questions. Neither this document nor clean documentation declares the release ready.

## Staging model

The selected destination is [emmveepee/Hollow-Sols-Godhome](https://github.com/emmveepee/Hollow-Sols-Godhome). Source and the issue form have been staged privately using a clean reviewed snapshot with new history. Preserve the original development history and unrelated staged/conflict material locally; do not upload them wholesale. Private staging is distinct from public visibility, tags and releases.

Use a reviewed source tree and no-reply commit identity. Review the final snapshot and manifests for ownership, secrets, local evidence and prohibited material. Prior scans are bounded evidence, not exhaustive clearance. The documentation cleanup must be separately reviewed/synchronized before a new source-matched package; it does not update an existing installation or ZIP.

## Build and package

Follow [CONTRIBUTING](../CONTRIBUTING.md) for prerequisites and commands. Build Bootstrap, SaveGuard and both public plugins from the same clean source. Public outputs/receipts are separate from development outputs. Receipts record source revision/tree, cleanliness and artifact hashes; packaging rejects dirty/stale inputs and mismatched identities. Reusing provisioned SDK caches is not clean-machine reproduction.

`tools/New-PublicPackage.ps1` creates a local mod-only ZIP with two game-root payload folders. Each contains four project DLLs, the native-menu marker and authored Doorstop/BepInEx configuration. The package also contains BUILD-INFO.json, SHA256SUMS.txt, a read-only verifier, installation guide, license/notices and matching SOURCE.zip. BepInEx/Doorstop binaries remain separately installed prerequisites. Current protocol/schema values live only in [DESIGN](DESIGN.md#component-boundaries-and-wire-compatibility).

`tools/Test-PublicPackage.ps1` checks the exact file allowlist, bounds, traversal, checksums, matching source and synthetic verifier success/rejection cases. Public-artifact inspection checks profile/version/reporting destination and debug exclusions. These checks never establish native recovery or clean-machine reproduction. Packaging does not deploy, upload or publish.

| Deliverable | Allowed content |
| --- | --- |
| Source | Reviewed original source/tests/projects/tooling, necessary config/build files, current documentation, issue form, LICENSE and retained notices. |
| Binary payload | Matching Foundation, Bootstrap, SaveGuard and role plugin DLLs; reviewed marker/configs, build/hash metadata, installation/verifier, license/notices and matching source. |
| External loader | Installed separately. A bundled variant requires its own exact dependency/notice/source/replacement review. |

Exclude `.git`, `.local`, caches, `bin/obj`, conflict copies, personal configs, deployment receipts, tickets, reports/logs/captures, saves/checkpoints/backups, extracted/decompiled material, game/engine/proprietary SDK assemblies and third-party package caches. Never ZIP a working directory wholesale. Preserve original GPL-3.0-or-later declarations and applicable third-party notices; do not assume exclusion resolves proprietary integration questions.

## Release gates

1. Review the final source diff and provenance questions; preserve/exclude unrelated staged work. No blanket legal clearance is claimed.
2. Build the exact reviewed candidate and validate identities, checksums, public exclusions and matching source. Existing private playtest/update archives are not standalone public installers.
3. Reproduce build/install/verification/removal on a clean provisioned machine with supported legitimate games. Inspect installation conflicts and preserve prior loader configuration.
4. Complete the remaining scoped native session/recovery/reporting checks in the acceptance register using authorized isolated existing saves. Preserve committed checkpoints and original data; distinguish reporting success from recovery.
5. Review concise player instructions, known limitations and final artifacts. Public visibility, upload of release assets, tag/release creation and publication require their own explicit authorization.

The future Option C work is [v0.2.0](ROADMAP.md), not a release gate for v0.1.0.
