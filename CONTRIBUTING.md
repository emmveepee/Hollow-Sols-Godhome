# Contributing to Hollow Sols

Hollow Sols is preparing v0.1.0. Read [AGENTS.md](AGENTS.md), [STATUS](STATUS.md), the [documentation map](docs/DOCUMENTATION.md), and the [canonical issue register](docs/ROSTER-COMPLETION.md) before changing behavior. A build or synthetic check does not establish native gameplay or player acceptance.

## License and provenance

Original project-owned source, tests, scripts and documentation are licensed under **GNU GPL version 3 or, at your option, any later version** (`GPL-3.0-or-later`), except material identified under its own terms. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md). This declaration grants no rights to proprietary games, installed libraries or extracted material.

Contribute only material you have the right to provide under these terms. Preserve existing attribution and identify any third-party or adapted material, its source, revision, license and modifications for review. Do not submit decompiled game code, extracted assets, saves or captures. Contribution review does not assume copyright assignment or resolve third-party permissions. The reviewed public snapshot still requires the release provenance review described in [release preparation](docs/RELEASE-PREPARATION.md).

## Build prerequisites

The production projects target .NET Standard 2.1; check projects target .NET 9. `global.json` selects SDK **9.0.316** with latest-patch roll-forward. Use Windows and PowerShell for the existing tooling. No tracked solution file is required: build the individual projects.

Native builds require legitimate local installations of both games and the audited managed assemblies. `SaveGuard.Patcher.KnownHashes` lists the supported Assembly-CSharp hashes; different builds require a new native save-boundary audit, not removal of the check. BepInEx **5.4.23.5** and its existing loader dependencies are external prerequisites, not vendored source. Exact observed components and unresolved distribution questions are in THIRD-PARTY-NOTICES. Acquisition/install automation and a clean-machine reproduction are not yet provided.

`Directory.Build.props` currently defaults to one developer's Steam paths. Override the existing MSBuild properties; do not commit your own machine paths or copy game assemblies into the repository. For example, after deliberately provisioning dependencies:

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.local/dotnet-home'
$env:NUGET_PACKAGES = Join-Path $PWD '.local/nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$bepCorePath = 'D:/your-loader/BepInEx/core'
$hkManagedPath = 'D:/Steam/steamapps/common/Hollow Knight/hollow_knight_Data/Managed'
$nsManagedPath = 'D:/Steam/steamapps/common/Nine Sols/NineSols_Data/Managed'
dotnet build src/Foundation/Foundation.csproj -c Release
dotnet build src/Bootstrap/Bootstrap.csproj -c Release
dotnet build src/SaveGuard/SaveGuard.csproj -c Release "-p:BepCore=$bepCorePath"
dotnet build src/HollowKnight/Plugin.csproj -c Release "-p:BepCore=$bepCorePath" "-p:HollowKnightManaged=$hkManagedPath"
dotnet build src/NineSols/Plugin.csproj -c Release "-p:BepCore=$bepCorePath" "-p:NineSolsManaged=$nsManagedPath"
```

A normal build may restore SDK/reference prerequisites. For an offline check with existing assets, append `--no-restore`; stop if prerequisites are absent rather than silently installing or updating them. Installed game references must remain non-copying (`Private=false`). Builds produce ignored `bin/obj` outputs. Direct `dotnet build` does not update the local success/hash receipt used by deployment scripts; it is not a deployment instruction.

## Optional Python analysis tools

Offline metadata/evidence analysis uses a deliberately provisioned Python interpreter and the dependencies named by its tool. `Test-HallEndurance.ps1` accepts `-Python <interpreter-path>` (default `python`); it is a native-game test harness, not an installation or ordinary build check. Never copy a developer-specific interpreter path into tracked files. Do not run native harnesses without their separate save-safe authorization.

## Isolated checks

For a public candidate, `tools/Build.ps1` accepts `-BepCore`, `-HollowKnightManaged` and `-NineSolsManaged` overrides, in addition to `-PublicRelease -NoRestore` for the two plugins. Successful role receipts record source revision/tree, cleanliness and artifact hash. Build Bootstrap and SaveGuard from the same source first. All project assembly/file versions are0.1.0.0; plugin version is0.1.0, informational versions include the source revision and compiler paths are mapped to `/_/HollowSols/`.

Release packaging requires **PowerShell7**, a clean Git checkout and already successful matching public builds. Run `tools/New-PublicPackage.ps1 -BepCore <audited-loader-core>` to create a local mod-only candidate, then `tools/Test-PublicPackage.ps1 -Archive <candidate-zip>` for extraction/checksum/exclusion and synthetic read-only verifier checks. Packaging refuses dirty/stale inputs and existing candidate outputs. It never installs/deploys/launches/uploads. `docs/INSTALLATION.md` owns player installation/removal; the shipped verifier supports Windows PowerShell5.1 and only reads named installation files. Do not interpret these checks or same-machine cached builds as fresh-machine/native acceptance.

Run relevant suites sequentially so competing workloads do not invalidate their short deadlines. With prerequisites already present:

```powershell
dotnet run --project tests/BridgeChecks/BridgeChecks.csproj -c Release --no-restore
dotnet run --project tests/PairedSaveChecks/PairedSaveChecks.csproj -c Release --no-restore -- .local/paired-checks-contribution
```

The other existing suites are GeometryChecks, ContactChecks, HitChecks, FoundationChecks, VisualChecks, FsmLeaseChecks, InventoryProgressionChecks and GuardChecks, under `tests/<Name>/<Name>.csproj`. GuardChecks also reads the supported installed assemblies and exercises patches in memory; inspect its prerequisites first. FsmLeaseChecks compiles the production scoped hold against a fake FSM and patch dispatcher; it checks callback deferral and cleanup, not actual Harmony or in-game animation. InventoryProgressionChecks compiles the production native-effect repair against fake endpoints; it checks non-creating title identity across unload/re-entry, cache restoration, item quantity scaling, native eligibility, exclusions and session ownership, not an in-game Unity/Harmony pass. None of these commands launches either game. PairedSaveChecks creates synthetic native payloads and interruption subprocesses inside its disposable `.local` root; these are not copies of user saves. The check projects can build referenced protection libraries but do not deploy them.

BridgeChecks locks selected gameplay/input/audio and hit-ack bytes against independent fixtures. PairedSaveChecks locks a control creation frame and already exercises recovery, publication cancellation and identity fencing. Tests involving native games are separate and require explicit scope. Preserve four isolated mod-owned pairs, vanilla/development saves, native authority and completed evidence. Never execute launch, deployment, backup, restore or save-preparation scripts as a generic test step.

## Review and release boundaries

Keep changes small, explain the concrete behavior and validation scope, and preserve unrelated working-tree edits. Option C extraction is planned for v0.2.0 in [ROADMAP](docs/ROADMAP.md), not part of v0.1.0 preparation. Native boss selection remains unrestricted. Review staged paths before any commit; publishing, tagging and pushing require separate authorization. Use the proposed allowlist in RELEASE-PREPARATION rather than archiving a working directory.
