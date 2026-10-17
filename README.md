# Hollow Sols — Yi in Godhome

Play Yi from **Nine Sols** in **Hollow Knight's Godhome**, with both real games running locally. Nine Sols simulates Yi; Hollow Knight runs its world and bosses. This is an offline, single-player mod for Windows.

**v0.1.0 is being prepared for release.** See the [accepted roster and known limitations](docs/ROSTER-COMPLETION.md). The current boss roster is playable and satisfactory; future tweaks will follow playtest and balancing feedback.

## Install and play

You need legitimate Steam installations of both games, the supported native builds checked by the mod, and **BepInEx 5.4.23.5 Windows x64** installed separately for each game. Game files and assets are not included. Follow the [installation, verification and removal guide](docs/INSTALLATION.md); source-build prerequisites are in [CONTRIBUTING](CONTRIBUTING.md).

1. Launch both games normally through Steam and let both reach their main menus.
2. Choose **Hollow Sols Bridge** in either game. The other ready menu joins the handshake.
3. Select or create a Hollow Sols paired slot in Hollow Knight; Nine Sols follows its session commands.
4. Use native Godhome selection to choose a fight. Yi's controls can be configured through **Nine Sols Controls** in Hollow Knight's pause menu.

Hollow Sols uses four separate mod-owned save pairs. Do not copy vanilla or old development saves into them. Godhome is the primary scope; Classic and Steel Soul remain experimental. Full Pantheon sequences, bindings and other validation limits are listed in the acceptance register.

## Report a problem

On an unexpected bridge failure, choose **Prepare Bug Report**. Inspect the diagnostic ZIP, attach it to the prefilled GitHub issue, describe what happened, then submit. Wait for GitHub to finish uploading and show the attachment link before submitting. Preparing the report never uploads it automatically.

Public builds retain ordinary bounded logs but have no F9 debug menu or detailed-capture activation. For configuration, privacy and local fallbacks, see [bug reporting](docs/BUG-REPORTING.md). For other feedback, use [GitHub Issues](https://github.com/emmveepee/Hollow-Sols-Godhome/issues). During private staging, repository access is required.

## Development and license

Start with [CONTRIBUTING](CONTRIBUTING.md) and the [documentation map](docs/DOCUMENTATION.md). The [v0.2.0 roadmap](docs/ROADMAP.md) describes the planned reusable bridge architecture.

Original project-owned material is **GPL-3.0-or-later**: GNU GPL version 3 or, at your option, any later version. See [LICENSE](LICENSE) and [third-party notices](THIRD-PARTY-NOTICES.md). Game content and third-party dependencies retain their own rights and terms. The software is provided without warranty.
