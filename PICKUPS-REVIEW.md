# Arena pickups — initial prototype

Accepted 2026-09-15: fixed collectible powerups and loaded weapons, automatic
collection on approach, cooldowns and per-map/area placement. Implemented only in
live **Deathmatch** and independent **Team Deathmatch**. Gun Game, Tactical/PFT,
ordinary raids and existing dropped loot are unchanged.

## Gameplay

| Pickup | Effect | Returns after |
|---|---|---|
| Double Damage (pink) | Twice the post-armor health damage from non-launcher firearm bullets for 20 gameplay seconds | 30 seconds |
| Shield (blue) | Separate pool of 220 impact points; expires after at most 30 gameplay seconds | 30 seconds |
| Weapon (gold) | Loaded primary weapon from catalogue kit `spartan`, with attachments; no spare magazines or other equipment | 45 seconds |

Effects refresh duration/refill the shield to its cap; they never stack. Both
sides' living bots may collect powerups by crossing them; this adds no AI routing.
Weapon auto-take is **local only**, into a free compatible **inactive** weapon slot.
No replacement or automatic draw. Full/unsuitable slots or busy hands leave it in
place: free a slot and approach again. Cooldowns start after confirmed collection.

Collection uses a 1m horizontal radius, 0.9m feet-height tolerance and a clear line
at contact height. Short movement segments prevent fast motion skipping a pickup;
stale samples/large teleports use only the current endpoint. The closest eligible
actor wins a scan. Actors already standing at a newly appearing pickup (including
fresh spawns) must leave and re-enter; camping on a cooldown point cannot farm it.

World markers show loading, collection and cooldown. The local timer/shield HUD
sits below the score panel, following its offsets and Arena HUD visibility. All
markers use procedural geometry and the existing font; no external assets needed.
Death clears that body's effects. Deathmatch round boundaries recreate points;
TDM deaths preserve cooldowns. Intermissions, exit and invalid scene ownership
clear effects/temporary objects. Paused gameplay time pauses durations/cooldowns.

F12 **Arena pickups** defaults enabled with **BotsCollectPowerups** enabled.
`PrototypeWeaponKit` selects another existing kit for the built-in weapon. Layout
and kit choices freeze at match start. Disabling during a match clears pickups and
effects; re-enabling applies next match.

## Initial Factory points

Built-in layouts cover full **Factory day/night** only. These are candidates from
identical native Bot-only markers, not manually surveyed pickup positions or live
acceptance evidence. Independent literal positions are stored; spawn lists stay
unchanged. None uses the excluded penned player spawn.

| Pickup ID | X, Y, Z | Native marker (provenance only) |
|---|---|---|
| factory-damage | 35.924, 4.55418539, 35.339 | cca16615-941d-4b07-a88f-4064628c1581 |
| factory-shield | -32.087, -2.581647, 29.9733315 | de479fb9-789f-499b-ac6b-0530fab5235b |
| factory-weapon | -5.89, 0.08, -27.46 | a5a35da1-0545-4774-b5a0-194f4903a9de |

Sources: native `SPT_Runtime/SPT_Data/database/locations/factory4_day/base.json`
and corresponding `factory4_night/base.json`. At the first live phase the runtime
waits up to 15 gameplay seconds for a grounded local NavMesh anchor. Points must
pass nearby navigation, level floor, pedestal clearance, complete native path and
active-area inset/path checks. Invalid points log a reason and are skipped without
aborting the match. Final geometry/rendering/navigation require a native raid.

## Layouts and authoring

Optional `BepInEx/plugins/ArenaMode/Pickups.json` **replaces** built-ins and is read
once per match. Map IDs ignore case; area IDs match exactly. Omitted/null `AreaId`
means full map only, never all sub-areas. Invalid files disable pickups for that
match with a logged failure. No type-name deserialization or arbitrary prefab load.

Example (reuses one Factory candidate):

```json
{
  "Version": 1,
  "Layouts": [{
    "MapId": "factory4_day",
    "AreaId": null,
    "Points": [{
      "Id": "factory-damage", "Kind": "DoubleDamage",
      "X": 35.924, "Y": 4.55418539, "Z": 35.339,
      "Yaw": 0, "RespawnSeconds": 30
    }]
  }]
}
```

Kinds: `DoubleDamage`, `Shield`, `Weapon`. Weapons additionally require `KitId`.
Limits: version1, 512KiB, 32 layouts, 64 points each, unique map/area pairs and point
IDs, finite coordinates, cooldown10–600 seconds. Empty layouts are allowed.

Enable F12 **Development - Pickups / Enabled**. Select `Kind` and `RespawnSeconds`
before adding a point; weapons use `PrototypeWeaponKit`.

| Default shortcut | Action |
|---|---|
| Left Alt + F6 | Capture floor under crosshair |
| Left Alt + F10 | Capture current feet position |
| Left Alt + F7 | Undo last point |
| Left Alt + F8 | Export uniquely named JSON draft |

The preview numbers points and records the current map/area. Export writes only
to `BepInEx/plugins/ArenaMode/PickupDrafts/`, never overwriting or activating a
layout. Review/copy or merge desired layouts into `Pickups.json` for a later match.
Draft state ends with the raid.

For surveying outside an Arena contour use an ordinary **local PMC** raid, also
enabling **Development - Area outline / AllowInNormalPmc**. Both opt-ins default
off. Only coordinates/exports are permitted there: no items, effects, protection
or Arena activation. Scav, Hideout, dead/spectator and non-BattleUI capture excluded.

## Native boundaries

- Exact match/game/world, round, registered physical body, health owner and alive
  state are checked; AI additionally requires active BotOwner. Effects cannot be
  recovered by profile ID on a replacement body. Native death forgets that life.
- Last-priority prefix changes the argument of the one native
  `ActiveHealthController.ApplyDamage` call. Armor/durability already resolved in
  `Player.ApplyShot`; health overflow does not reapply the bonus. No shared item,
  template, armor, configuration or `DamageCoeff` mutation.
- Shield absorbs hostile Bullet/Explosion/GrenadeFragment impact damage after the
  native incoming multiplier, before body-part scaling/clamping/black-limb spread.
  **220 points are not necessarily 220 total HP** across those native rules. Shield
  does not prevent wound relapse/stamina loss or repair worn armor. Bleeding, falls,
  self/admin damage, blocked friendly damage and native invulnerability neither
  spend shield nor receive a bonus. Proven hostile dead grenade owners can still
  meet a victim's shield.
- DD uses native **impact-time live firearm ownership**. Grenades/launchers and
  their fragments are excluded because detonation can resolve a replacement owner
  by profile ID. No fire-time projectile registry: firearm ricochet/fragment source
  attribution remains the native impact attribution.
- Weapons preload kit bundles before creating the item/model. Dedicated source
  `OutProcess` guards run after the async inventory yield and recheck exact
  life/round/slot/hands before moving. Final success **and** destination ownership
  consume the appearance. Native events remove the source model. Timeout/ambiguous
  transfers retire the point for the round instead of rearming/duplicating it.
- Views never parent/destroy pooled weapon models. Pending disposal hides the
  source immediately but waits for native settlement before pool cleanup. Late
  callbacks cannot revive old appearances. Ordinary loot has no such source owner.
- Existing assists still observe actual final HP deltas. No new profile routes,
  persistent rewards, server/Core dependencies or audio.

## Verification / GPU acceptance

Checks link production source with explicit native/world/physics/inventory/HUD
doubles. Installed IL/decompile checks verify damage/inventory seams; compiled
fault controls must fail intended assertions. They do not run Unity/Harmony/a raid.
Run `tools/ArenaPowerupChecks/Verify.ps1`, `tools/ArenaWeaponPickupChecks/Verify.ps1`,
`tools/ArenaPickupChecks/Verify.ps1`, `tools/ArenaPickupChecks/Verify-Regressions.ps1`.
Use isolated build/deploy helpers: ordinary project builds deploy dependencies too.

Independent review covered all new code and wiring; no remaining P0–P2 blocker
after anchor retry, queued guards, reservation ordering and partial marker cleanup.
Integration checks subsequently found an outward-segment retry and a zero-length
interpolated-contact LOS bypass. Both fixes were independently reviewed: leaving
only rearms a later sample, and the current position must also have clear LOS.
HANDOFF and deployment record contain exact counts, evidence and build IDs.

Pending user checks (restart EFT only after delivery):

1. Full Factory day/night, Deathmatch and TDM: log should report 3 configured points
   and placed count/reasons. Find markers, check floor position/readable labels,
   bobbing/spinning weapon and no navigation obstruction.
2. Collect buffs, verify timers/shield balance, expiry/death reset. Both bot sides
   can collect. Check DD/shield, unchanged worn armor and no self/fall/bleed immunity.
3. Full weapon slots: leave weapon. Free a compatible inactive slot, leave/re-enter,
   verify a loaded usable weapon and unchanged hands. Repeat after cooldown; open
   inventory/die/exit during collection: no stuck hands, duplicates or stale model.
4. Camp a cooling point: must leave/re-enter to collect. Cross a round boundary and
   disable/re-enable: no old effects/objects should return unexpectedly.
5. Capture/export points, including two nearby floors/wall obstruction. Install
   reviewed layouts for next match and verify exact-area behavior. Ordinary PMC
   survey, Tactical and Gun Game must never get pickups/effects.

Supply fresh logs and marker/HUD screenshots. Native transfer, rendering, Factory
placement/collision and C: mirror receipt are not certified by offline success.
