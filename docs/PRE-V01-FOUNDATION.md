# Pre-v0.1 foundation — current engineering summary

This is initialization/save/lifecycle architecture, not another issue list or a public-release certificate. [ROSTER-COMPLETION](ROSTER-COMPLETION.md) alone owns per-issue state and player acceptance. Latest detailed evidence is [OCT13-FOCUSED-VALIDATION](OCT13-FOCUSED-VALIDATION.md); the original A–H [October 12 report](DOCUMENTATION.md#historical-evidence) is a dated snapshot.

## A. Architecture

Passive native menu infrastructure permits ordinary vanilla play. One explicit Hollow Sols Bridge press in either ready menu authorizes counterpart adoption; this supersedes the original two-button brief. HK selects slots and controls sessions; NS waits for instructions. A connection has a fresh role/version/nonce identity and may exist without a loaded save. Loading adds a fresh session token, stable pair ID, typed slot and committed generation. Stale/duplicate/foreign operations are refused or idempotent.

The existing combat bridge is retained: NS owns Yi simulation/outgoing combat, HK owns its world and native encounter authority. Current source declares gameplay wire47 and15 native binding records; see DESIGN for the complete schema matrix. Historical counts are dated evidence, not additional supported layouts. Initial loading caption/arm inspection are save-session scoped, rather than room scoped. [Latest regression evidence](OCT13-LATEST-REGRESSIONS.md) records native UI input-state ownership, talisman/receiver retirement corrections and validation limits.

Vanilla restoration consumes local or remote disconnect intent before a new handshake. Authenticated ordered heartbeat tail may drain only during Disconnecting against its exact retained offer; lifecycle connection/session identities remain invalid and restoration clears them. Engine-free repeated reconnect validation is distinct from native NS vanilla-load/title/UI testing (P16-LC01).

## B. Components

Foundation contains typed lifecycle/control transport, isolated store/checkpoint coordinator, native exchange and scoped save context. Shared HollowSolsMenuPlugin/Sessions connect those contracts to native menus and gameplay. Each game's native adapters perform scoped load/capture/return and UI integration; existing combat/render/input subsystems are retained. Current worker publication, native HUD ownership and bounded existing-save fixtures are recorded in the focused report.

## C. Save safety

Exactly four logical pairs use the mod namespace, normally LocalApplicationData/HollowSols/paired/HollowSols. Previously initialized namespaces stay put. Native serializer contexts HK4/NS3 are private memory-provider indices, not mod slot aliases or vanilla destinations. No automatic migration, reset, replacement or deletion of existing user/development saves is authorized.

Immutable generations contain both native payloads plus checked slot/pair/session/generation/transaction identity. Flushed payloads/manifest precede commit publication; hashes and expected-generation checks detect interrupted/mismatched data. Store leases serialize cooperating operations. Recovery selects the latest valid synchronized checkpoint and visibly warns of possible lost newer progress; suspect originals remain preserved. With no valid pair, load stops without creating a replacement or falling back to vanilla.

Native captures and lifecycle decisions stay on the main thread. HK's immutable store publication runs on one worker and is adopted only after exact validation. Busy/publication/session fences prevent a new load overtaking in-flight work. User-confirmed deletion handles the logical pair conservatively and retains quarantine; creation/deletion never occurs as a cleanup shortcut. Automatic saves apply only to already-created slots.

## D. Player flow

Both menus expose Hollow Sols Bridge. HK presents four isolated save cards; NS waits. Existing saved location determines occupied card art; empty cards remain blank. Classic/Steel Soul warnings and Godhome access Yes/No use native-style dialogs. Godhome intro remains native and interactive. Full-access choice permits challenges without fabricated clears/bindings/rewards. Upgraded Yi initialization is creation-only; saved loadouts are retained.

Normal HK/NS saves request paired persistence. Session exit drains/finalizes and returns to the connected modded selection/waiting state. Slot switching retains connection and creates fresh session identity. Entering vanilla selection ends/disconnects and restores native runtime/persistence boundaries. Unexpected disconnects preserve committed data and require fresh activation; no replacement save is invented. Steel Soul defeat remains native, with confirmed paired clearing; campaign compatibility remains experimental.

## E. UI

Native components/assets/fonts/navigation/fades are reused, not redistributed. Current native HUD is forwarded and visually verified in both games; a scoped native display vote is revoked at teardown. Dialog wrap extends downward; title-only content pips belong only on the title. Controls navigation stays in HK with native NS reveal/unlock/Back callbacks and synchronized pause restoration. Only explicit native keyboard binding capture temporarily focuses NS. Initial artwork/inspection waits for completed paired preparation; native snapshots and donor acquisition finish behind the loading caption. Further menu/controller/scaling and in-motion VFX acceptance is governed by the canonical register, not old snapshot claims.

## F. Evidence

Both final builds passed with zero warnings/errors. Eight isolated suites passed, including 4,167 paired-save assertions. The focused report distinguishes actual native mechanic passes, failed attempts, screenshot inspection and unrun cases. Earlier creation/access/switch/recovery/deletion/death/vanilla/disconnect fixtures remain dated evidence in the original report, not newly rerun claims. Copies of existing mod saves were used for routine tests; vanilla fingerprints stayed unchanged.

## G. Limits

Classic/Steel Soul are experimental, complete Pantheons/bindings/persistence are not implied by a late-sequence fixture, and all Radiance phases are not certified. Native semantic recovery for unsupported payload formats and power-loss guarantees remain bounded. Partial root/configuration and VFX work awaits the player's specified tests. No full public-release readiness is claimed.

## H. Before public release

Use canonical issue states to choose remaining work; do not reimplement native-verified fixes from archive failures. Obtain the player's extended/in-motion/hardware grades, resolve newly reproduced remaining defects, complete relevant mode/menu/slot/recovery/disconnect regressions on the final build, and separately verify full challenge/sequence semantics. Do not publish v0.1 from compilation or this summary alone.
