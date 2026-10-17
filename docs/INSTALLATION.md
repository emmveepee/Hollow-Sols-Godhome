# Hollow Sols v0.1.0 candidate — Windows x64

This is a testing candidate, not a declaration of public-release readiness. Both legitimate Steam games are required. No game files or BepInEx binaries are included. Only the exact game/loader hashes in BUILD-INFO.json are covered; a verifier pass is not a native gameplay or save-recovery pass.

## Before installing

Close both games. This procedure is for an otherwise unmodded installation. If either game already has Hollow Sols, another mod loader or other mods, stop and review that installation's ownership/backups first; this ZIP is not an automatic update or merge. Do not replace files whose origin you cannot establish. Keep all saves, paired data and previous development evidence untouched.

Extract this candidate to a separate folder. Check its ZIP hash against the supplied checksum. It contains HollowKnight and NineSols game-root payload folders, BUILD-INFO.json, SHA256SUMS.txt, a read-only verifier, license/notices and matching SOURCE.zip. Folder names are labels: copy their contents into the respective game roots, not inside similarly named extra folders.

## Install both roles

1. Open each game's root through Steam: Properties > Installed Files > Browse. Hollow Knight's root contains hollow_knight.exe; Nine Sols contains NineSols.exe.
2. Obtain **BepInEx 5.4.23.5, Windows x64**, from the official pinned release: https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5 . Extract it into each game's root. Do not substitute BepInEx6 or an x86/IL2CPP build. Preserve its licenses. The candidate's verifier checks the expected loader file hashes.
3. Before copying the candidate, preserve each original loader doorstop_config.ini outside the game folder. Preserve BepInEx/config/BepInEx.cfg too if it already exists, and record explicitly if it does not. These are configuration backups, not save operations. Review conflicts rather than merging this candidate over an existing modded setup.
4. Copy the contents of the candidate's HollowKnight folder into Hollow Knight's root and NineSols into Nine Sols' root. The intended configuration replacements are doorstop_config.ini (routes through the save-protecting bootstrap) and the clean BepInEx.cfg. Do not point Doorstop straight at BepInEx.Preloader.dll while Hollow Sols' patcher/plugins remain installed. No loader DLL should be overwritten by this mod-only payload.
5. From the extracted candidate folder, verify both installations. Substitute your actual directories:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Verify-Install.ps1 -Game HollowKnight -GameRoot 'D:\Steam\steamapps\common\Hollow Knight'
powershell -NoProfile -ExecutionPolicy Bypass -File .\Verify-Install.ps1 -Game NineSols -GameRoot 'D:\Steam\steamapps\common\Nine Sols'
```

The verifier only reads listed installation files and hashes. It never loads gameplay, changes an installation or accesses saves. A missing/mismatched file is a stop condition; do not bypass game-build checks or remove the save guard to make a launch pass. Keep the extracted candidate, manifests and original configuration backups for removal/review.

## Start and report problems

Start both games normally through Steam. Choose Hollow Sols Bridge in a ready main menu; the other game must reach its native menu too. Hollow Knight manages four isolated mod-owned paired slots; Nine Sols follows. Never import or replace vanilla/development saves. Godhome is the primary scope; Classic/Steel Soul remain experimental. The source acceptance register owns the approved roster and remaining validation limits.

Public builds have no F9 debug menu or detailed-capture activation. Unexpected bridge-failure notices offer Prepare Bug Report. Inspect the prepared ZIP, attach it to the prefilled GitHub issue, describe what happened and submit. No logs are automatically uploaded. The private staging repository requires tester access. Existing explicitly empty Support.RepositoryUrl settings remain empty; set that key in each game's local.hollowsols.menu.hk.cfg / local.hollowsols.menu.ns.cfg to https://github.com/emmveepee/Hollow-Sols-Godhome if needed, then restart. Source docs/BUG-REPORTING.md describes local fallbacks.

## Remove the candidate

Close both games. Check every project file against BUILD-INFO.json before removal; if any has changed, stop for review. Remove only these five named project files from each game root (substitute the role's plugin name):

- BepInEx/core/YiGodhome.Bootstrap.dll
- BepInEx/core/HollowSols.Foundation.dll
- BepInEx/core/HollowSols.menu
- BepInEx/patchers/YiGodhome.SaveGuard.dll
- BepInEx/plugins/YiGodhome.HollowKnight.dll **or** YiGodhome.NineSols.dll

Restore the original doorstop_config.ini and BepInEx.cfg backed up before installation. If BepInEx.cfg was originally absent, the candidate's unchanged clean config may be removed; retain any subsequently modified configuration for review. Do not remove the entire BepInEx directory or external loader files using this project's manifest. Those belong to the separately installed loader and need their own reviewed removal procedure if desired. Do not launch halfway through removal.

Paired saves and logs remain under their existing application locations. **Do not delete them to uninstall the mod.** This procedure never calls for changing vanilla saves, game assets or Steam authentication.

## Source and known limits

SOURCE.zip contains the exact project-owned source identified by BUILD-INFO.json. Original material is GPL-3.0-or-later; third-party terms remain separate. See LICENSE and THIRD-PARTY-NOTICES.md. See the matching source docs/ROSTER-COMPLETION.md for current acceptance and remaining validation, and docs/RELEASE-PREPARATION.md for release gates. A local installation/reporting sample does not establish clean-machine reproduction or complete save recovery.
