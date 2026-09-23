# Health pickups and bot pickup objectives

2026-09-23. User-approved next step after the Arena-flow roadmap in
`FUTURE-WORK.md`. Scope remains live **Deathmatch and independent Team Deathmatch**.
Ordinary PMC/Scav, Gun Game, Tactical/PFT, intermissions and completed matches are
excluded by the existing exact game/world/round/body admission. No server/profile
route, inventory item, shared bot setting or third-party configuration is changed.

## Health

`Health` is appended to the layout enum; all existing numeric kinds and version-1
custom layouts remain valid. Factory day/night now place six pickups from the
same eight-position pool: the added health point returns after **20 gameplay
seconds**. Custom `Pickups.json` still replaces built-ins and is never rewritten;
add an explicitly authored `Health` point to use healing in a custom layout.

The white cross/beacon restores up to **75 HP total**, prioritizing head, thorax,
stomach, legs, then arms. Each surviving damaged part is capped at its current
native maximum, including reductions from prior surgery. Destroyed or zero-HP
parts stay unchanged. Bleeding, fractures, pain, toxins, energy and hydration are
not removed/restored. Full health or exclusively unhealable damage leaves the
pickup available. The local notice reports confirmed HP gained. Bots collect it
under the existing `BotsCollectPowerups` switch; no additional setting is added.

The native seam is `ActiveHealthController.ChangeHealth(part, positiveValue,
DamageHelper.Existence)`, also used by native passive regeneration. No `Heal`
stub, `RestoreBodyPart`, full-health restoration, medical-item animation or
inventory transaction is used. Positive HP changes still trigger native temporary
stats/XP; Arena's existing protected return restores persistent profile state.
Assist damage history is unchanged (its capture ignores positive health deltas).

`ChangeHealth` can invoke synchronous listeners after mutating HP. The adapter
captures physical health ownership and generation, rechecks before every part,
and debits requested rather than net HP from the 75-point budget. Confirmed
partial healing consumes once; an attempted but unconfirmed mutation retires
the appearance instead of granting it again. The outer collection scan returns
immediately if a callback changes generation, preserving a replacement round.

Native sources: `../_decompiled/EFT.HealthSystem/ActiveHealthController.cs`
(`ChangeHealth`, passive regeneration), `HealthValue.cs` (current-max clamp), and
`HealthStatisticsManager.cs` (positive-healing event). This is not a promise to
cure wounds or to make bleeding harmless.

## Bot objectives

The optional existing **BigBrain 1.5.0 companion** registers a separate priority-35
`Arena Combat Pickup` layer. PFT's priority-74 pursuit/lease/goal handoff stays
unchanged. Without a supported companion, pickups and passive bot collection
still work; the round log states that deliberate routing is inactive.

Targets come only from actually available placed appearances, never raw layout
coordinates. They carry generation, appearance, physical health owner and point
identity, and honor fresh-spawn/leave-before-reentry requirements. Bots seek:

- Health when at least 15 healable HP are missing.
- Double Damage when at most three seconds remain (or none is active).
- Shield when fewer than 110 impact points or at most three seconds remain.

Weapons, Jump Boots and Infinite Ammo remain local-only. Useful targets are
weighted by benefit and distance. At most two bots may contest a point, with
only one from the same native `BotsGroup`; this is **not a separate guarantee of
one bot per TDM faction**. Normal collection still chooses the physically nearest
eligible entrant, not the bot holding a routing claim. A player can take it first.

No trip is chosen while an enemy is visible/recently seen or fired upon, while
under fire, healing, reloading, throwing a grenade, dealing with native danger,
flashed, or interacting with a door. SAIN's exact owner/door contract is checked
read-only. SAIN search and combat share a layer, so numerical priority alone is
not sufficient; the action rechecks these conditions during travel. It suspends
SAIN movement only after it owns the BigBrain action slot. Unsupported SAIN state
fails closed for routing, not for the whole match.

Each bot checks at most two candidate paths per selection, no more often than
every two seconds. Maximum straight-line distance is 40m, complete route length
60m, trip duration 20 gameplay seconds, no-progress tolerance four seconds, and
retry delay after interruption eight seconds. Paths are checked against current
area contours both before and after native acquisition. Arrival distance is
0.45m, inside the pickup's 1m contact radius. Native geometry, door/mine handling,
pose, weapon choice and steering remain in charge; no teleport or forced vault.

Door distance is not pickup progress. While actual `NearDoor` owns the mover,
only the captured default pickup path and its matching return target are detached;
the temporary door path stays intact. Native delayed return cannot revive the
abandoned pickup route. After the door, a still-current action must acquire a new
validated route. Cleanup is bound to the captured body, mover and path object so
it does not stop a replacement life or a newer foreign action. Pruned claims are
permanently invalidated. Periodic cleanup releases static references after raids.

Native sources: `BotMover`, `BotMoverStateMachine`, `BotPathController`,
`BotDoorOpener`, `RunToEnemy`, `MoveToEnemy` and `MoveNode` under `../_decompiled`.
The new action deliberately does not call stock `RunToEnemy.UpdateNodeByBrain`,
which would dereference/repath to `GoalEnemy`. It does not pause native patrol data.

## Verification and remaining native acceptance

Run with PowerShell 7:

```
pwsh -NoProfile -File tools/PickupObjectiveChecks/Verify.ps1
pwsh -NoProfile -File tools/ArenaPickupChecks/Verify.ps1
pwsh -NoProfile -File tools/PickupObjectiveChecks/Build-Staged.ps1
```

These link production health/runtime/target/layer/action code against explicit
native boundaries, include deliberate compiled failures, and compile both client
and companion against installed EFT/BigBrain APIs without deployment. They do
not prove actual Unity navigation, animation, rendered markers, runtime cost or
profile restoration. Exact delivery/test status belongs in the dated handoff.

Native acceptance: restart EFT with both matching DLLs; Factory day/night DM and
TDM should log six placed points or explain a refused position. Walk through the
white cross while damaged, confirm at most 75 HP, remaining bleeding/fracture,
unchanged blacked parts, the 20s cooldown and no consumption at full health.
Observe bots taking available useful health/DD/shield, returning to combat on
contact, and yielding after a failed path. Test closed doors, a stolen target,
death/respawn, round transition and pickup disabling. Route selection/release
logs prove decisions only, not successful physical travel. Verify ordinary PMC,
Tactical/PFT and Gun Game remain unchanged. Compare protected profiles using a
baseline taken after login synchronization, not a potentially stale disk mirror.
