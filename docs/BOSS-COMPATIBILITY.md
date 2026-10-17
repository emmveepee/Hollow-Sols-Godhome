# Combat and presentation contracts

The [acceptance register](ROSTER-COMPLETION.md#whole-fight-player-acceptance) is the only roster/status list. This page describes invariants to preserve when changing accepted mechanics. It is not a backlog. Historical boss investigations remain in Git history.

## Shared authority and lifetimes

- HK native encounter/sequence events determine completion, never a single receiver's HP/disappearance or a presentation bar. A terminal body may await native completion only with a finite deadline and inactive target snapshot.
- Each native target retains independent health, activation and injury state. `HealthManager.Hit` decides directional/type immunity; an invincible flag alone is insufficient. Nondamageable anchors are not contact attacks.
- NS native attacks, parries, resources and timing remain authoritative for Yi. Keep attack templates immutable and primary/volume dealer leases independent. Observe actual native damage before authored resets obscure it.
- Acknowledged outcomes are fenced by exact session/encounter/target/attack identity. Uncertain damage is terminal and never replayed. Retired receipts may refuse late hits but cannot revive targets. Verify reset/quarantine before native-owner reuse and count quarantine against capacity.
- Visual sequences and individual strikes have separate lifetimes. Retiring one strike must not cancel siblings or the native combo. Immunity uses paused gameplay time and actual collider activations; simultaneous bodies need not resolve in numeric order.
- Native HP comparisons determine injury/phase gates. No guessed thresholds or phase writes. Shared native-hit acknowledgements own bounded Yi movement/animation holds; adapters discover authored states rather than implementing separate freeze systems.
- Host pause stops NS physics and animation. Native completion, return-image readiness and input release remain separate gates. Preserve native fades and timing.
- Ordinary hazard recovery uses the latest valid safe grounded point in the current imported room. Never reuse prior-room/donor anchors. Radiance's native ascent checkpoint is the specific exception below.

## Shared presentation and geometry

HK renders its native boss art. The accepted crimson style is an animated native silhouette ribbon with black inner edge, red outer edge and 1.12 width multiplier; retain separate Tai-Chi teal and native warning/audio behavior. Bound caches/leases and never hide boss artwork because its aura is missing. Do not distribute native art or round-trip outline pixels through NS.

`ProjectileReflection.Speed` is **40 world units/second** globally. Historical multipliers and Radiance sword-speed exceptions are superseded. Outgoing damage/eligibility remain their authored policies, independent of incoming damage or Radiant lethality.

Native HUD visibility/title readiness gates widgets; independent hurt/fire effects must not disappear with resource widgets. Multi-target health and titles remain distinct. Minion HP uses fixed native-widget tiles rather than stretching one arena raster. Captures preserve native density, opacity, shader behavior and capture-time transforms with bounded pooling/backpressure.

For native boss sensors, distinguish terrain-relative floor queries from actual-player proximity/aim queries. Change only an inspected consumer that needs a terrain offset; never globally move the native target to fix one boss. Nosk and Radiance imported platform refinements preserve native floor heights and solid faces. Host geometry/readiness, wall art and native Yi collision are separate responsibilities.

## Absolute Radiance

`src/HollowKnight/AbsoluteRadianceCombat.cs`, shared `RadiancePresentation`, `BossHealthProjection`, geometry helpers and native hit receivers hold the implementation. Preserve these contracts:

- Native HP, phase gates, final-hit event and statue/Pantheon branch own progression. Death Ready melee/reflected hits dispatch native FINAL HIT; terminal states stop translated combat. Display reserve/notch corrections must never write combat HP or force a branch.
- Body warnings follow authored eye/ascent/rain windows. Beams retain native collisions/crimson semantics but no expensive silhouette outlines. Each stationary beam burst and ascent-beam activation has its own strike; warning lifetime belongs to the whole authored sequence.
- Phase-ending stationary rain is identified from exact Control/Rage Comb emission. Each blade has an independent crimson counter. Ordinary reflection damage is42; countered rain is84 with zero internal injury. An imperfect parry retires translated admission while native flight/cleanup continue. Reflection speed follows the global contract above.
- Native parent-tween movement matters even when a blade's Rigidbody reports zero velocity. Exclude anticipation/cleanup. Share identical nail contour/deformation/mesh work without sharing strike lifetimes; cull blades only once their whole bounds clear the validated floor.
- Ascent recovery follows HK's native checkpoint chain and downward terrain projection, translated to Yi's pivot and validated against current support. Preserve the room checkpoint through native fade/get-up.
- Finale hiding is arena-scoped and expires on room change. Suppress the exact native Knight artwork without breaking native ending/return callbacks. Keep the receiver identity when hiding a terminal boss bar; presentation metadata cannot make a hidden body damaging.
- Ledge-parry facing restoration applies only while the admitted attachment survives native recoil; do not override detached/free movement.

## Other intentional adapters

| Family | Contract to preserve |
| --- | --- |
| False Knight / Failed Champion | Separate armor, exposed head and authored injury/platform windows; HP reset is not completion. |
| Collector / summons | A newly admitted receiver may overlap a still-live native melee dealer; never reactivate expired attacks. Each summon has its own native lifetime/health. |
| Uumuu | Native shield gates talisman attachment and delayed detonation. Consume a now-shielded explosion without replay; preserve native kick/repulsion timing. |
| Sly / Oro and Mato | Authored body/weapon parts share strikes where intended. Shared acknowledged counters retain native knockdown/stun/relaunch state without defeat or HP-reset shortcuts. |
| Grimm / NKG | Independent projectile/pillar activations and native collider gaps; no accidental volley-wide retirement. |
| Broken Vessel / Lost Kin | Projectile outlines remain removed by player decision; retain native projectile visuals and crimson gameplay. |
| Grey Prince Zote | Jump contacts begin at native apex/descent; counter-stagger landing waves remain distinct. Victory-only detached-head interaction cannot reopen combat authority. |
| Pantheons | Drained Advancing handoff creates fresh encounter/target generations while retaining Yi/resources. Human continuation is distinct from failed restoration. Late-index fixtures do not establish full clears or bindings/persistence. |

These exceptions are reasons to retain narrow adapters, not a universal combat model or instructions to retune working fights.
