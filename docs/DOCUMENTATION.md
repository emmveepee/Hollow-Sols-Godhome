# Documentation authority and maintenance

## Read order / ownership

1. Latest direct user instructions govern scope, acceptance criteria and safety. AGENTS.md preserves permanent project rules and relevant user clarifications.
2. [ROSTER-COMPLETION.md](ROSTER-COMPLETION.md) is the **only current per-issue implementation/acceptance register** and whole-fight player table.
3. [STATUS.md](../STATUS.md) is a concise projection and navigation entry point. It must not invent a second issue state.
4. [DESIGN](DESIGN.md), [PRE-V01-FOUNDATION](PRE-V01-FOUNDATION.md), [PERFORMANCE](PERFORMANCE.md), [COVERAGE](COVERAGE.md) and [ABSOLUTE-RADIANCE](ABSOLUTE-RADIANCE.md) explain current contracts, criteria and validation scope. They link to issue states rather than duplicate worklists.
5. [OCT13-FOCUSED-VALIDATION](OCT13-FOCUSED-VALIDATION.md) is dated evidence. [MODLOG](../MODLOG.md) is commit provenance. Neither is a live worklist.
6. Files named HISTORY, dated STATUS snapshots, and anything in archive are **historical only**, even if their original body says “current”, “OPEN”, “complete” or suggests a next step. Reference contracts/SOURCES remain technical references subordinate to current user/rules/register.

The untracked root MODLOG edit-conflict copy is not authoritative; it is not a second live log. Do not import its contents automatically. The historical October 11 status file has legacy encoding and is intentionally untouched by consolidation.

## Release preparation documents

[INSTALLATION](INSTALLATION.md) owns the candidate's player-facing loader prerequisites, explicit game-root payload, read-only verification and removal instructions. `New-PublicPackage` constructs an allowlisted local candidate from clean matching builds; `Test-PublicPackage` checks its archive and synthetic verifier behavior. Neither grants native deployment, save-testing or public publication authority.

[BUG-REPORTING](BUG-REPORTING.md) owns the public/development profile, local diagnostic export, repository configuration and reporting test procedure. P17-REL01 in the canonical register owns its implementation/native acceptance state. It grants no publication or live-save testing authority.

[RELEASE-PREPARATION](RELEASE-PREPARATION.md) records the bounded Option B baseline, compatibility-check scope, proposed release allowlist and unresolved distribution gates. [CONTRIBUTING](../CONTRIBUTING.md) owns contributor build/check instructions; [THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES.md) owns dependency/provenance and retained upstream notices. These do not change the canonical gameplay register or authorize publication. Current codec versions live in DESIGN; dated evidence retains its original versions.

## State vocabulary

NATIVE VERIFIED means keep the tested mechanic unless new evidence contradicts it. NATIVE SAMPLE means a bounded portion was observed, with remaining scope explicit. SOURCE CHANGED means implemented but awaiting validation; it is not missing code. PARTIAL means unresolved scope remains. APPLICABILITY UNCONFIRMED means a family hypothesis, not a reproduced defect. Only explicit player acceptance establishes a complete fight. Do not collapse all these states into “OPEN.”

Player retest is pending for the new report; this does not reopen Nosk wall-jump, Collector same-swing, native HUD or the measured saving block as engineering defects. G04 separates the fixed saving component from remaining transition observation. Normal fade-contained loading stutter is acceptable; do not lengthen fades to conceal a visible-gameplay hitch.

## Update procedure

- Update the affected canonical ID with implementation state, exact evidence scope and next action first. A new report reopens only its affected case; preserve accepted fights and uncertain applicability labels.
- Update STATUS's projection only if needed. Put commit provenance in MODLOG and detailed reproducible evidence in a dated report/local capture. Never prepend repeated multi-file progress paragraphs.
- Do not turn diagnostics, compilation, elapsed time, still images or a partial sample into full acceptance. Conversely, do not carry an archived failed approach forward as live work after the later verified fix.
- Preserve prior evidence. Clearly archive superseded wording and link it as dated history; do not silently rewrite past test results.
- Routine tests reuse existing disposable paired saves, end on evidence/drain/failure, and preserve vanilla/development data. Historical diagnostic permissions/commands never broaden current save authority.

## Historical evidence

Superseded HISTORY/STATUS documents, the old archive and legacy encounter launchers are removed from the tracked release tree. Their exact originals and SHA-256 manifest remain under ignored `.local/release-staging/legacy-before-cleanup/`; the original local Git history is also retained. This private preservation folder is not a public distribution input. Current contracts, canonical acceptance and still-referenced focused evidence remain tracked. The current README is a player-facing entry point rather than a duplicate implementation log.

Before restoring an old file for investigation, verify it against the preservation manifest and keep its historical status explicit. Restore only the named file; do not restore old deployments, save behavior or worklists. Public source starts from a clean reviewed snapshot, not from a rewritten or pushed development history.

Cleanup does not change gameplay, save authority or acceptance. Source-only reporting defaults and portability edits are validated separately; native reporting/recovery remain pending.
