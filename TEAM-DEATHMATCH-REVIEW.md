# Independent Team Deathmatch

## Behavior and selection

Select Arena on the native Scav/PMC screen, then **TEAM DM** in the existing
mode selector above the map controls. F12 also exposes `teamdeathmatch`.
`Team Deathmatch / PlayersPerTeam` defaults to 5, accepts 2–12, and is captured
at match start. The human's unchanged PMC faction determines their side.
At the default there are four friendly bots plus the human against five bots.
Death reserves the human's place until their normal instant Arena respawn.

This is continuous USEC-versus-BEAR combat using existing Arena kits and bot
progression, without a score limit, intermission, buy phase or PFT followers.
Both factions receive periodic knowledge of the nearest live opponent, even
while the human is dead; native AI or installed SAIN owns movement and combat.
The ordinary HuntPlayer/PMC-floor controls do not govern this mode.

The HUD shows faction kill totals. The Arena scoreboard separates both teams,
including all earlier lives in match totals. New generated bots start a new
personal run; they do not impersonate a previous bot. Existing team-colored
tracers classify admitted friendly/enemy identities and preserve the color of
posthumous projectiles. Same-team kills do not earn points or announcements.

Same-faction attributed bullets/explosions are blocked before health and armor
mutations. Self-damage, environmental effects and unknown attribution retain
their native behavior. Friendly grenade camera shake/contusion can remain:
the separate native contusion callback has no attacker identity, and no broad
invulnerability/visual suppression was added.

PFT is optional and is not used to form these teams. A selected full PFT roster
continues to force protected Tactical; its ownership and persistence gates are
unchanged. To play TEAM DM with PFT installed, enter the native selection alone.
No SAIN/PFT/BigBrain binary or code was bundled. Installed SAIN 4.5.1 was checked
locally; normal native AI is the fallback when SAIN is absent.

## Lifetime and safety

The server edits only its exact live per-raid location clone. PMC role rules
explicitly make USEC friendly to USEC and BEAR friendly to BEAR; opposite roles
are enemies. Copies of touched hostility records/lists prevent changes to SPT's
shared configuration. The quiet map disables all ordinary waves and bosses.

The client requests one explicit-faction bot at a time per faction, keeps the
human slot reserved, and rejects unowned direct dispatch before native counters
are charged. Each request has an exact raid lease and separate reservation.
An expired profile load cannot start a parallel load for that faction. Detached
native activations retain uncertainty, with at most three outstanding requests
per faction; late/duplicate/wrong-faction bodies cannot be admitted as replacements.
No request is cancelled with StopSpawn after native dispatch.

PreActive bodies immediately occupy capacity, but must become Active within
45 seconds. Failed or expired activations are retired before a replacement is
requested. Three consecutive activation failures stop the mode with a diagnostic.
Administrative retirement verifies the body is no longer alive; ambiguous or
suppressed retirement stops refills instead of silently creating more bots.
When a mode stops for an invariant failure, use the existing Arena Give up action.

Native group and memory enemy admission, goal selection and optional SAIN enemy
admission all apply the same faction rule. Reconciliation removes stale bodies
before new relations are added, because SAIN removes some entries by profile ID.
Weak affiliation records preserve dead-body attribution without retaining
corpses. No global faction/profile, cached difficulty or ammo template is changed.
Both sides retain Arena's configured corpse-looting restriction.

## Verification and remaining playtest

The implementation has production-linked population/dispatch/score/award/tracer
tests, modeled mode lifecycle scenarios, server clone and native picker tests,
mutation controls, native-source review and an independent adversarial review.
The detailed acceptance ledger is `tools/TeamDeathmatchChecks/GATES.md`.
Final offline results: 157 server/profile/scoreboard tests; 14,931 population,
dispatch, scoring and award assertions; 143 actual mode/relationship checks with
modeled engine seams; 76 picker checks; 144 tracer checks; 84 Labs spawn checks.
The PFT (231), team/SAIN (767), Solo Tactical (47), announcer (142), cleanup (44)
and existing side/map/menu checks also pass. Controlled regression runs rejected
five setup, thirteen policy, three runtime, eight tracer and two Labs mutations.

Independent cross-review found and resolved failed-activation slot retention
and unverified administrative retirement. The integration also keeps friendly
TDM bots under the corpse-looting rule and waits for complete local respawn
before adopting a replacement human body. No unresolved P0–P2 review findings.

Client and server Release builds succeeded. Four existing client nullable-context
warnings remain; server build reported none. The explicit client Release build
was last, to leave Redeploy.Core in Release. Built/deployed SHA256 pairs match:

| Artifact | SHA256 |
|---|---|
| Arena client | `9F9B89662D4E1174232A5C487AFFC39375607CFBFA17B4F714E8056F44FA5773` |
| Arena server | `A979C4497B1965634DBB7DDD3573C874C7AD7BD6688912C60E73E25E09E7AE8F` |
| Redeploy.Core | `3973761FDE09BBDBBD924AA893F3D351ECA50EFE14B93183A6E1C8CE947BAA8C` |

Native anchors checked: `EFT/BotSpawner.cs` activation callback/counters,
`EFT/BotOwner.cs` PreActive/ActiveFail transition, `BotsGroup.cs` enemy/ally APIs,
`EFT/BotMemory.cs` admission/removal/goal, and `EFT/Player.cs` shot/armor order.
Installed SAIN 4.5.1 SHA256:
`C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489`.
Its `EnemyListController.tryAddEnemy(IPlayer)`, inherited BotOwner and exact
EnemyPlayer/EnemyInfo/last-seen APIs were verified against tagged source and DLL
metadata. Existing protected-team contract checks also pass against this DLL.

There is no GPU on this host. **Restart both game and server**, then test:

1. TEAM DM with the default 5v5: four friendly PMCs and five opposing PMCs;
   friendly bots must not target the human or their own faction.
2. Kill an opponent and a friendly bot externally if needed: each death fills
   only its own faction slot. Shoot a teammate and check health/armor protection;
   enemy and self/environmental damage must still work.
3. Die and respawn repeatedly; verify team affiliation, opponent activity,
   tracer colors and durable team points. Repeat with the other PMC faction
   using a separate test profile, without editing the main profile's faction.
4. Enter an ordinary raid afterward, and separately test protected PFT Tactical
   with a selected full roster. Neither must inherit TDM spawning or friendship.

Actual navigation, combat and mod interactions remain unverified until that
playtest. Offline passes are not a claim that a live Unity raid was run.
