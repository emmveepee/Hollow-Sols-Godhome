# Documentation map and maintenance

## Short reading paths

- Players: [README](../README.md), then [INSTALLATION](INSTALLATION.md).
- Agents: [AGENTS](../AGENTS.md), [STATUS](../STATUS.md), [current acceptance](ROSTER-COMPLETION.md), then the relevant contract below.
- Contributors: [CONTRIBUTING](../CONTRIBUTING.md) and [DESIGN](DESIGN.md).

## Canonical homes

| Topic | Owner |
| --- | --- |
| Operating/safety rules | [AGENTS](../AGENTS.md) |
| Player approval, known limitations, unresolved validation | [ROSTER-COMPLETION](ROSTER-COMPLETION.md) |
| Implemented components, protocols, sessions and persistence | [DESIGN](DESIGN.md) |
| Shared combat/presentation invariants and boss exceptions, including Radiance | [BOSS-COMPATIBILITY](BOSS-COMPATIBILITY.md) |
| Native service/content acquisition and diagnostic catalogue tooling | [CONTENT-FOUNDATION](CONTENT-FOUNDATION.md) |
| Test evidence standards and performance methodology | [COVERAGE](COVERAGE.md) |
| Build/check commands | [CONTRIBUTING](../CONTRIBUTING.md) |
| Installation, verification, removal | [INSTALLATION](INSTALLATION.md) |
| Reporting behavior, configuration and privacy | [BUG-REPORTING](BUG-REPORTING.md) |
| Packaging/release procedure and distribution exclusions | [RELEASE-PREPARATION](RELEASE-PREPARATION.md) |
| Future v0.2.0 architecture | [ROADMAP](ROADMAP.md) |
| Pinned technical references | [SOURCES](SOURCES.md) |
| License/dependency inventory and unresolved provenance | [THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES.md) and [LICENSE](../LICENSE) |

STATUS is only an orientation page; MODLOG is a short milestone index. Neither owns a duplicate issue table. Source defines implemented mechanics; a plan is not implementation and implementation is not player acceptance. Latest direct user decisions govern requirements and acceptance.

## Update rules

Update the owning document when a fact changes and link to it elsewhere. Keep only independently unresolved, evidence-backed items in the current register. Record the affected scope, implementation/evidence level and next check; do not paste whole investigation transcripts or stack traces into it.

Use **player accepted**, **native sample/verified**, **implemented; validation pending**, **unresolved**, or **planned** precisely. Whole-fight approval supersedes earlier pending fight grades; it does not imply full campaign, Pantheon, recovery or hardware coverage. Reopen an accepted fight only from new evidence.

## Historical evidence

Superseded dated reports, old status narratives and redundant boss pages live in the original development Git history. The clean public snapshot intentionally does not include that private history. It contains the current contracts and scoped evidence summaries needed to work on the project.

For local historical investigation, use `git log --all -- <path>` and `git show <revision>:<path>` without restoring old files into the current tree. The pre-consolidation documents are available at development commit `1dfea9f`; subsequent uncommitted reporting acceptance was preserved locally and incorporated into the current register. Older local preservation copies and raw evidence remain ignored. Historical source revisions and incident records are evidence pointers, not current deployment instructions or permission to access saves.

Do not revive old task lists, diagnostic launch commands, save-replacement permissions or failed approaches simply because they remain in history. Conflict copies are not authoritative documents. No new documentation archive is maintained.
