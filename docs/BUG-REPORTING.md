# Public build and browser-assisted reports

Public profiles remove the F9 testing menu, collision renderer and detailed-capture activation. Development profiles retain them. Ordinary bounded gameplay logs, startup logs, failures and existing performance summaries remain enabled; reports cannot reconstruct the fine-grained collision snapshots that detailed capture would have recorded.

## Build and repository configuration

With existing dependencies provisioned, build both roles sequentially:

```powershell
./tools/Build.ps1 -Project src/HollowKnight/Plugin.csproj -PublicRelease -NoRestore
./tools/Build.ps1 -Project src/NineSols/Plugin.csproj -PublicRelease -NoRestore
```

Outputs go to `.local/public-build/<Game>/`; separate `.local/build-<Game>-public.json` receipts identify them. The default Build invocation remains a development build in its existing output location. Public builds refuse automated native-menu fixture startup. No build command installs, deploys, packages or publishes the output. Do not copy an output directory wholesale; the reviewed distribution allowlist still governs binaries/dependencies. Rebuild from the exact reviewed release source after resolving unrelated working-tree edits before distributing a matching source/binary release.

New public configurations default to `https://github.com/emmveepee/Hollow-Sols-Godhome`. Development configurations default to empty. Existing configured values, including an explicitly empty value, are preserved. To enable reporting on an existing installation, configure the `Support.RepositoryUrl` entry in each game's `BepInEx/config/local.hollowsols.menu.hk.cfg` or `local.hollowsols.menu.ns.cfg`, then restart:

```ini
[Support]
RepositoryUrl = https://github.com/emmveepee/Hollow-Sols-Godhome
```

Only an HTTPS github.com repository URL with exactly owner/repository is accepted; credentials, query strings, fragments and nonstandard ports are rejected. Empty/invalid configuration leaves local export available and clearly marks browser submission unavailable. No token, OAuth flow, reporting service or GitHub API client is present. The issue form lives in `.github/ISSUE_TEMPLATE/bug-report.yml` and must be present on the destination repository's default branch. A private repository requires the reporting player to have access.

## Player flow

The copied-clipboard success notice ends after the short browser/configuration status. Full ZIP paths and fallback instructions remain on the clipboard, avoiding native dialog clipping. Clipboard-unavailable fallback retains its existing visible instructions.

After an unexpected link/session failure, the surviving game's existing native notice offers **Prepare Bug Report** and **Continue**. An unsafe native-return timeout retains **Close Game** instead of offering a gameplay continuation. Report export is optional and independent of committed-save preservation, native teardown and quit deadlines.

Press Prepare Bug Report, wait for the bounded local snapshot/export, then **attach this ZIP, describe what happened, then submit** in the opened GitHub form. Explorer selects the ZIP and full instructions/path/issue link are copied to the clipboard when available. Browser/Explorer failures retain manual paths/links in those instructions; if the clipboard is unavailable, the dialog shows them directly. Export failure offers retry; an export exceeding ten seconds offers a safe continuation while preserving available evidence. No files are uploaded or issues submitted by the game. GitHub sign-in, attachment and submission remain deliberate browser actions; the form cannot enforce that a ZIP was attached.

Reports are under `%LOCALAPPDATA%/HollowSols/logs/reports`, separate from paired-save storage. They retain exact incident/session/connection IDs, role/build module identity, game/Unity versions, failure scene/reason and allowlisted ordinary log tails. Both processes use the same session incident ID; missing roles/files are explicitly identified. Repeated export reuses the published ZIP. If a late peer record arrives after export, that already-exported ZIP stays immutable and its availability description reflects the original snapshot.

The collector reads only known `plugin.log` startup/rotation/current and `bootstrap.log` names within that exact role/session runtime directory, plus the failure dialog's bounded in-memory menu tail. It does not search other sessions, save slots or the whole installation. Native log reads are capped at 1 MiB per file; exports cap selected text at 16 MiB, with omission/truncation notes. Detailed captures, native payloads, deployment files, assets and unrelated files are excluded. Windows/known Unix personal paths, known local user/machine values and common credential-like values are redacted. Inspect the ZIP before sharing publicly; automated redaction cannot identify every possible personal datum in arbitrary text.

Active-session markers record process ID and start identity. On restart, an abandoned marker creates a report offer from available evidence; a live or uncertain owner is not declared dead. Normal exit/cancellation disarms markers, and Continue acknowledges a pending offer. Disk shutdown failure can leave an unconfirmed marker; recovery describes absence of a recorded orderly end, not proof of a crash. Reports/snapshots are not silently deleted; acknowledged reports remain local. No report journal mutates the save store.

## Validation and remaining gates

PairedSaveChecks includes synthetic two-role, missing-peer, redaction, source-preservation, large-tail, duplicate export, restart/live-owner/normal-disarm, lock/contention, interrupted-export and offline launcher tests. Existing Bridge/Foundation checks exercise EOF, stale/timeout/mismatched identities and lifecycle cancellation separately. GuardChecks has a `public-build` mode to inspect the compiled public plugins for absence of the F9 menu/renderer and detailed activation/I/O, and verify report scheduling precedes native teardown:

```powershell
dotnet run --project tests/GuardChecks/GuardChecks.csproj -c Release --no-restore -- public-build .local/public-build/HollowKnight/YiGodhome.HollowKnight.dll .local/public-build/NineSols/YiGodhome.NineSols.dll
```

Current native/player evidence and untested variants are recorded only in [ROSTER-COMPLETION](ROSTER-COMPLETION.md). For a browser test, inspect the ZIP, attach it, wait for GitHub's attachment link, and submit deliberately. A form field naming a ZIP is not an uploaded attachment.

Native recovery testing needs separately authorized isolated existing paired-save copies and exact candidate identity. Confirm native return/quit behavior, committed checkpoint preservation, visible warning and reload separately from report preparation. Use a public candidate for public UI tests; automated development fixtures are not available in public builds. Do not repeat a blocked native harness without new evidence or treat a successful report as full recovery acceptance.
