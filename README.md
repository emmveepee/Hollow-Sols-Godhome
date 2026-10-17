# Hollow Sols — Yi in Godhome

Bring Yi from **Nine Sols** into **Hollow Knight's Godhome**, using both real games running locally. Nine Sols owns Yi's movement, combat, health and abilities; Hollow Knight owns its world, bosses, hazards and encounters.

This is an experimental, offline, single-player mod. **v0.1.0 is being prepared; no public release is available yet.** Compiling the source does not establish native gameplay or release readiness.

## Requirements

- Legitimate Windows installations of **both games**, with normal Steam authentication.
- The supported game assemblies listed by `src/SaveGuard/Patcher.cs`; other builds have not been verified.
- BepInEx **5.4.23.5**, separately provisioned for each game. Game files and assets are not included in this repository.
- For source builds: .NET SDK **9.0.316** and the prerequisites in [CONTRIBUTING](CONTRIBUTING.md).

Candidate installation/removal instructions are in [INSTALLATION](docs/INSTALLATION.md); native installation and release acceptance remain pending. Do not treat older private-playtest update ZIPs as standalone public installers. Contributor build and deployment steps are separate operations.

## How it works

Launch both games normally through Steam. In a ready main menu, choose **Hollow Sols Bridge**; the counterpart must reach its own native menu before save selection can continue. The optional developer `Start Yi in Godhome.cmd` launcher requires local deployment receipts; downloaded packages use normal Steam launching.

Hollow Knight manages **four isolated mod-owned paired save slots**. Nine Sols follows its session commands. Vanilla and previous development saves remain separate and are never migrated into those pairs. Paired checkpoints synchronize when Hollow Knight saves; recovery must preserve original data and show a warning.

Godhome, Hall of Gods and practical Pantheon encounters are the primary scope. Classic and Steel Soul launching remain experimental behind their warning. Full encounter, Pantheon, mode and recovery support must not be inferred from access to a menu or a successful build.

Yi's bindings come from the real **Nine Sols Controls** screen, exposed through Hollow Knight's pause menu. Both games pause while it is open. For current validation and known limitations, read [STATUS](STATUS.md), [boss compatibility](docs/BOSS-COMPATIBILITY.md) and the [canonical issue/player-acceptance register](docs/ROSTER-COMPLETION.md). An incomplete fight is not automatically blocked from selection.

## Report a problem

Public builds contain **no F9 debug menu or detailed-capture activation**. Ordinary bounded failure/performance logs remain available.

On an unexpected bridge failure, choose **Prepare Bug Report**. It prepares a sanitized diagnostic ZIP, reveals it locally and opens a prefilled [GitHub issue](https://github.com/emmveepee/Hollow-Sols-Godhome/issues/new/choose) when reporting is configured. **Attach the ZIP, describe what happened, then submit.** Preparing a report does not upload anything. Inspect attachments before sharing.

New public configurations default to this repository. Existing settings are preserved; development configurations remain unconfigured by default. While the repository is private, testers need repository access. Export/browser failures have local fallbacks, and reporting never replaces safe session cleanup. See [report configuration, privacy and recovery](docs/BUG-REPORTING.md).

## Development

Start with [CONTRIBUTING](CONTRIBUTING.md) and the [documentation map](docs/DOCUMENTATION.md). Native tests and deployment require their own save-safe scope; ordinary builds and isolated checks do not launch games. Development builds retain bounded F9 tools that are excluded from public builds.

- [Architecture and wire contracts](docs/DESIGN.md)
- [Paired-save foundation](docs/PRE-V01-FOUNDATION.md)
- [Validation scope](docs/COVERAGE.md) and [performance evidence](docs/PERFORMANCE.md)
- [Release preparation and remaining gates](docs/RELEASE-PREPARATION.md)
- [Technical sources](docs/SOURCES.md) and [third-party notices](THIRD-PARTY-NOTICES.md)

## License

Original project-owned source, tests, scripts and documentation use **GNU GPL version 3 or, at your option, any later version** (`GPL-3.0-or-later`), except material identified under its own terms. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md).

This grants no rights to proprietary games, installed dependencies or extracted material. Hollow Sols is provided without warranty. Provenance, third-party integration and distribution questions remain release gates.
