# Arena pickups — initial prototype

## Collection sound (2026-10-07)

Confirmed local collections play one 600 ms CC0 cue, IENBA's **Game Pick Up**.
See `ArenaMode.Client/Assets/ArenaMode/pickups/SOURCES.json` and the shipped CC0
text for provenance.
The sound is admitted only after the current appearance reservation completes
successfully; async weapon failure, duplicate/old callbacks and bot collections
are silent. `ArenaPickupAudio` rechecks exact live actor ownership before output.
The service owns a separate `ArenaMasterAudioOutput`; it cannot stop announcer
or hitmarker sources and bypasses interface volume while honoring Master/listener
mute. `Arena pickups / SoundVolume` defaults to 0.7; 0 mutes it. No replay queue.
Pause stops the short tail; round/context reset destroys only owned audio and PCM.
Missing/invalid files warn and attempt loading once per round, never each frame.
The production runtime/audio checks exercise collection and callback lifecycle;
real device audibility still requires a native listening test.

## Existing pickup contract

Accepted 2026-09-15: fixed collectible powerups and loaded weapons, automatic
collection on approach, cooldowns and per-map/area placement. Implemented only in
live **Deathmatch** and independent **Team Deathmatch**. Gun Game, Tactical/PFT,
ordinary raids and existing dropped loot are unchanged.

September 21 extension: per-round Factory position rotation and short Infinite
Ammo. Offline checks: 226 runtime/layout/geometry assertions with 14 compiled
fault controls; 366 effect assertions with 42 native contracts and 10 faults;
2,839 shot-adapter assertions with 32 native contracts and four faults. Independent
reviews found no remaining concrete P0–P2 after preserving legacy fixed spacing.
These are source-linked/installed-IL checks, not native shooting or placement proof.
The dated HANDOFF and deployment receipt bind final delivery and pending GPU work.

## Gameplay

2026-09-23 extension: `Health` and optional deliberate bot collection are specified
in `PICKUP-OBJECTIVES-REVIEW.md`. Factory now has six pickups from the same eight
candidate positions. Healing increased from 75 to 150 HP on 2026-09-26.

| Pickup | Effect | Returns after |
|---|---|---|
| Double Damage (pink) | Twice the post-armor health damage from non-launcher firearm bullets for 20 gameplay seconds | 30 seconds |
| Shield (blue) | Separate pool of 220 impact points; expires after at most 30 gameplay seconds | 30 seconds |
| Health (white cross) | Up to 150 HP on surviving damaged parts; wounds and native maxima remain | 20 seconds |
| Jump Boots (green) | 100% more jump height for 30 gameplay seconds, including the existing double jump; no extra jump credit | 30 seconds |
| Infinite Ammo (orange) | Loaded magazine firearms consume no cartridges for 10 gameplay seconds; no magazine reload needed | 45 seconds |
| Weapon (gold) | Pickup-only M60E6 FDE copied from `retzrestdfg`, with attachments and 100 M993 rounds; no spare magazines or other equipment | 45 seconds |

Effects refresh duration/refill the shield to its cap; they never stack. Both
sides' living bots may collect damage/shield/health pickups by crossing them.
The optional exact BigBrain companion also routes eligible bots to useful available
pickups, with combat priority and bounded travel; see the objective review above.
Jump Boots are local-only and require Arena movement enabled; bots leave them available.
Infinite Ammo is local-only and does not fill an empty weapon or magazine.
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
`PrototypeWeaponKit` defaults to `pickup-m60-retzrestdfg`; it can also select an
existing catalogue kit. The September 19 follow-up migrates the old `spartan`
prototype value once on the machine loading the plugin, using the persistent
`M60DefaultMigrated` marker. A new default alone had left the saved M4 selection
in place, including after mirroring a stale config back from another host.
Other kit choices and later deliberate changes (including back to `spartan`)
are preserved. Custom `Pickups.json` weapon entries are not rewritten. The layout
log now includes built-in/custom origin and the actual weapon point/kit IDs.
Layout and kit choices freeze at match start. Disabling during a match clears pickups and
effects; re-enabling applies next match.

## Factory position pool (2026-09-21)

Built-in layouts cover full **Factory day/night** only. Each round selects six
positions from eight candidates: one pickup of each kind, not eight pickups.
The original four positions below remain in the pool; the user accepted their
placement. Four additional positions come from identical native Bot-only markers
on both map variants and still need a GPU placement check. Independent literal
positions are stored; native spawn lists stay unchanged. None uses the excluded
penned player spawn near Main Gate.

| Pickup ID | X, Y, Z | Native marker (provenance only) |
|---|---|---|
| factory-damage | 35.924, 4.55418539, 35.339 | cca16615-941d-4b07-a88f-4064628c1581 |
| factory-shield | -32.087, -2.581647, 29.9733315 | de479fb9-789f-499b-ac6b-0530fab5235b |
| factory-weapon | -5.89, 0.08, -27.46 | a5a35da1-0545-4774-b5a0-194f4903a9de |
| factory-boots | -6.77, 0.17, 10.01 | 86ff84ac-edf7-4efd-9643-4bacc59073e7 |
| additional / factory-ammo origin | 13.78, -2.291, -21.65 | 03a779de-ef2d-43e3-a4f3-fdcfed474f05 |
| additional | -20.756, 0.166, 22.769 | 2355be95-341b-46db-90fd-6a383e286e5c |
| additional | 4.73, 0.01, -35.10 | 68ac9245-633b-4287-a999-2c3028fa8d45 |
| additional | 23.83, -2.604, -30.66 | 90da8fb3-422a-46cd-b540-648cde8ee9f3 |

All six types share this pool. Each point shuffles its candidates using a private
RNG, keeps its identity/type/kit/cooldown, and chooses the first safe floor at least
3m from already placed pickups. Final floors determine separation. Exhausted
candidates leave that pickup pending during the bounded placement window below.
A round's selected position stays
fixed through cooldowns and deaths; Deathmatch rounds resample, continuous TDM
resamples next match. Layout input stays frozen and unmodified. Random selection
can repeat a previous position; it does not guarantee a different spot every time.

Sources: native `SPT_Runtime/SPT_Data/database/locations/factory4_day/base.json`
and corresponding `factory4_night/base.json`. At the first live phase the runtime
allows up to 15 gameplay seconds for anchor acquisition and missing-point placement.
It checks readiness every 0.25 seconds; a full placement pass is limited to once
per second and always gets a fresh grounded local NavMesh anchor. Respawn, weapon
swap and a paused game postpone attempts without extending the gameplay deadline.
The deadline is checked before starting another pass, including when an anchor
first becomes available after expiry. Only missing point IDs are retried; created
entries, cooldowns, weapon preparation and positions never reset on a retry.
Phase/round/raid/settings changes retain the existing cleanup and disable latch.
Points must
pass nearby navigation, level floor, pedestal clearance, complete native path and
active-area inset/path checks. Still-invalid points are skipped at the deadline
without aborting the match. Built-in prototypes alone try up to 32 nearby candidates on
0.5/1/1.5/2m rings if the original point fails. Every candidate passes the same
checks; the final sampled floor must stay within 2m horizontally and 0.5m in
height of the original. The first safe floor and original refusal are logged.
Custom `Pickups.json` points never use this fallback. Final geometry/rendering/navigation require a native raid.

Fixed custom points are visited before rotating points on each pass. Either side
being rotating requires 3m separation: a fixed point recovering later can remain
unplaced if a previously successful rotating point now occupies its space. We do
not move that successful point; fixed/fixed layouts keep their authored spacing.
Late-created points retain the normal leave/reenter requirement for actors already
inside their collection radius.

Each full pass logs actor/anchor coordinates, the number of top-level alternatives
actually tried, raw geometry probe count (including nearby fallbacks), and counts
of every rejection reason. Distance/level fallback limits and pickup spacing are
also counted. Intermediate failures are Info summaries, not final skip warnings.
One final summary distinguishes success from deadline exhaustion; missing points
report their last complete pass diagnostics rather than only its last candidate.
The 2026-09-25 18:54:41 Factory log showed 0/6 from the previous one-shot pass;
the exact disconnected origin was not recorded, so that native cause remains an
inference. Offline tests cover total/partial recovery, moved anchor, timeout,
throttling, pause, respawn readiness, preserved weapon/cooldown state, spacing,
late-spawn contact, cancellation and diagnostic counts. Native recovery in that
specific Factory situation still requires a GPU test.

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

Kinds: `DoubleDamage`, `Shield`, `Weapon`, `JumpBoots`, `InfiniteAmmo`, `Health`. Weapons additionally require `KitId`.
Limits: version1, 512KiB, 32 layouts, 64 points each, unique map/area pairs and point
IDs, finite coordinates, cooldown10–600 seconds. Empty layouts are allowed.
Each point optionally accepts `"Alternatives": [{"X": 1, "Y": 0, "Z": 2, "Yaw": 0}]`
(up to 15 entries). The original position and alternatives form its per-round
pool; no alternatives preserves the old fixed point. Each explicit custom candidate
still requires normal geometry validation and never uses the built-in nearby
fallback. Fixed custom points are placed first and retain their authored spacing;
rotating points must stay at least 3m away from those and other selected points.

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

### Confirmed examination refusal and instance-scoped correction (2026-09-23)

The subsequent GPU log confirms `InstallNotExaminedError` for the M60E6 itself
at 20:09:00.334 and 20:09:05.717, including the free SecondPrimaryWeapon slot.
This is not a capacity failure. `Slot.Examined(Item)` dispatches through the
native virtual ItemController slot to `InventoryController.Examined(Item)`.

Before an otherwise eligible automatic collection attempt, Arena now remembers
only the exact original item instances created for that pickup, including its
attachments/ammunition. A postfix on the concrete, non-generic inventory method
can turn its false answer into true only for the same current local inventory,
physical player, world and pickup generation in live Deathmatch/independent TDM.
`IsLiveActor` retains the exact active raid, phase, roster and settings guards.
Known native answers remain unchanged. The snapshot is captured when the weapon
handle is created, so later attached ordinary loot does not gain this exception.
Identity is by object reference, not template or item ID. A weak-key table avoids
owning item lifetime; retired bodies/rounds/raids are rejected even if old objects
remain alive. The grant survives the successful pickup's source-model cleanup,
so the carried gun can still be drawn, reloaded and modified during that life.

There is no Profile/Encyclopedia write, examination operation/XP, shared template
change, inventory restriction bypass or automatic draw. Free inactive slots,
CanExecute, discard limits and delayed source ownership remain native/unchanged.
Ordinary weapons (including another M60), other controllers, bots and all modes
outside pickup admission keep vanilla examination. The previous diagnosis below
records the historical state before this narrowly scoped correction.

Offline adapter tests model the previously failing unexamined weapon, exercise
collection and post-transfer knowledge, reject identity/actor/world/generation
changes and later added parts, and preserve native known answers. Fault controls
remove registration, replace the frozen item tree or weaken the lifetime guards.
These tests and the installed IL contract checks do not prove Harmony execution
or native animation/ownership behavior. Next GPU test after restart: collect the
M60 with a free second slot, draw/fire/reload it, and confirm no permanent new
examination entries after protected return. Native acceptance remains pending.

### Weapon refusal diagnosis (2026-09-23)

The MASCHINE11 run 17:02–17:19 recorded nine `Arena pickup weapon not taken`
messages (first 17:05:56.541), all with native Move simulation failures for every
free inactive slot. At 17:06:17.207 and 17:06:18.748 even three slots were free.
No transaction was submitted in these attempts. The old `incompatible` counter
included **every** failed Move, so neither physical incompatibility nor insufficient
space nor a specific other mod is established by this log. There is no recorded
native error object to reconstruct the actual cause after the raid.

Refusals now log the native error type/detail separately for each attempted slot;
`planRejected` replaces the misleading `incompatible` label, and the HUD no longer
claims slot incompatibility for an unspecified planning error. Details are bounded
to 512 characters per slot, single-line and protected against throwing formatters.
This is a diagnostic change, **not a proven fix for weapon collection**. The Move,
CanExecute, destruction, inactive-slot and delayed ownership checks are unchanged.
Native `ItemManipulator.Move` / `CanModifyItem` / `Add` and `Slot.CheckConditions`
can reject for distinct search, lock, examination, filter or other restrictions.
No examination state or profile is changed to bypass them.

Next native test: restart EFT, leave a secondary weapon slot empty, then approach
the M60 and retain the `reasons=[SecondPrimaryWeapon=...]` log line if refused.
Leaving and reentering the pickup remains required for a retry. UiBridge was
unreachable during diagnosis, so no fresh runtime reproduction is claimed.
The adapter checks cover error identity/detail, multiple target slots, bounded
formatting, a throwing formatter and unchanged refusal/consumption behavior; a
compiled negative control removes the error detail and must fail those checks.

- Infinite Ammo intercepts only `FireOperationBase.ProcessInventoryOperationsForSingleShot`
  with no malfunction, on the exact current local player's current hands/operation.
  It gives the native shot a detached one-round clone of the actual chamber/top
  cartridge using the native factory ID source. The real chamber, magazine order,
  stacks, item ownership and inventory events remain untouched. Shot counters and
  chamber-model data describe retained cartridges; no item is inserted into inventory.
  Mixed ammunition repeats the currently loaded/top cartridge until expiry. Native
  recoil, heat, wear, malfunctions, animations and bolt/pump cycling remain intact.
  Empty guns, revolvers, launchers, flare/one-off weapons and multi-barrel/break-action
  firearms use vanilla consumption. A magazine is required, even with a chambered
  round. An already prepared native shot may finish after expiry or death (vanilla
  `FastForward`); no new bonus decision is allowed. Do not cancel native weapon
  operations to prevent that completion. Body/health/round changes clear the effect;
  ordinary raids, bots, Tactical and Gun Game never receive it.
- Jump Boots multiply only a newly requested native jump impulse by sqrt(2),
  after Arena's base height. Gravity, skills/injuries, stamina and collisions stay
  native. Expiration changes the next impulse, never snaps an in-flight trajectory.
  A weak entry record remembers the factor actually applied to each native jump
  state; the double-jump cache removes that recorded factor and applies the current
  buff once. Expiring between jumps, collecting midair and reusing jump states
  cannot retain/compound the old bonus. Ownership/effect state is rechecked after
  synchronous native stamina debit callbacks. Air-vault limits remain unchanged.
- The M60 uses a synthetic equipment parent solely for the existing preload plan;
  `KitBuilder` creates fresh owned native item IDs. The 11-node weapon/attachment
  tree matches the provided preset and stash: MRS, NcSTAR blue laser, RK-1/B25U,
  M60E3 barrel and Fortis RED; its Capco box contains 40+40+20 M993. Runtime uses
  a static copied definition, never reads/writes a profile or inserts a normal kit.
  Generated pickup instances have the bounded examination exception above;
  template/profile knowledge and native hands/inventory restrictions stay intact.
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
  persistent rewards or server/Core dependencies. Collection audio is documented
  separately above.

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

1. Full Factory day/night, Deathmatch and TDM: log should report 6 configured points
   and placed count/reasons. Find markers, check floor position/readable labels,
   bobbing/spinning weapon and no navigation obstruction.
2. Collect buffs, verify timers/shield balance, expiry/death reset. Both bot sides
   can collect DD/shield only. Check unchanged worn armor and no self/fall/bleed immunity.
   Check the orange 10s ammo timer with semi-auto, automatic, bolt-action and M60;
   loaded ammo must not decrease during it and must decrease again after expiry.
   Test an empty gun, a weapon switch, last chambered round, malfunction and death.
   No extra ammo items may remain in inventory. Compare a normal PMC raid.
3. Full weapon slots: leave weapon. Free a compatible inactive slot, leave/re-enter,
   verify a loaded usable weapon and unchanged hands. Repeat after cooldown; open
   inventory/die/exit during collection: no stuck hands, duplicates or stale model.
4. Camp a cooling point: must leave/re-enter to collect. Cross a round boundary and
   disable/re-enable: no old effects/objects should return unexpectedly. Verify
   position choices can vary across rounds but never relocate during cooldown.
5. Capture/export points, including two nearby floors/wall obstruction. Install
   reviewed layouts for next match and verify exact-area behavior. Ordinary PMC
   survey, Tactical and Gun Game must never get pickups/effects.

Supply fresh logs and marker/HUD screenshots. Native transfer, rendering, Factory
placement/collision and C: mirror receipt are not certified by offline success.

## Pickup playtest corrections (2026-09-15)

The user confirmed shield collection. It now grants/refills 220 impact points for
30 gameplay seconds, while Double Damage remains 20 seconds. The original damage
ordering, physical-life ownership and no-stacking rules are unchanged.

The rejected weapon pickup was a local adapter bug: native
`ChangeDiscardLimitsResult.ItemsToDestroy` enumerates bookkeeping entries with
`NumberToDestroy=0`. Testing only `Any()` rejected even a lossless transfer into an
empty compatible inactive weapon slot. The adapter now rejects positive losses
only; the source gate, queued checks, native transaction, ownership confirmation
and pending/uncertain quarantine are unchanged. Refusal text/logs distinguish
full/incompatible slots, native veto and actual loss. Installed IL contracts and
a faithful nonempty zero-loss boundary model reproduce this case; restoring the
old predicate must fail the first valid take. Actual native collection, empty
hands and cooldown repetition still require the next user raid test.

The 16:28:11 playtest log reports `factory-damage` skipped for obstructed pedestal
clearance (2/3 points placed), not an active powerup inside the obstacle. The
built-in bounded fallback above attempts a safe nearby floor without changing
spawn lists or weakening clearance/path checks; exhaustion still skips just that
point. This is a prototype recovery, not a claim to have surveyed Factory geometry.

Available pickups also have a same-color upward beacon (blue shield, pink damage,
gold weapon): a narrow core with a wider translucent glow, fading upward over at
most 4m. An upward scene ray clips it below the nearest ceiling. It uses ordinary
scene depth, casts no shadow and adds no collider/light; it is hidden while loading,
pending, cooling or retired. The view owns its beam children/material and disposes
them with the marker; native pooled weapon ownership stays untouched. Missing
beam shader logs a warning and retains the ordinary marker. GPU acceptance must
check visibility, walls/ceilings, cooldown disappearance and clean round/raid exit.

Independent adversarial reviews accepted the shield, weapon and geometry/view
changes with no remaining concrete blocker. Offline checks pass 329 powerup
assertions plus 42 native contracts and 8 compiled faults; 108 weapon assertions,
46 native contracts and 5 faults; 189 runtime/geometry assertions, 25 wiring
contracts and 13 faults. Native client compilation passes (4 existing CS8632
warnings). These checks do not certify the actual Unity renderer or raid transfer.
