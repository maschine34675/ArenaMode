# Corpse cleanup: paired native inventory notifications

The 2026-09-05 long-raid report identified 5,640
`No parent inventory ... RemoveItemEventArgs` diagnostics matching Arena's
649 trimmed corpses. `LONG-RAID-REVIEW-2026-09-16.md`, which supersedes it,
records 14 of them against 657 trimmed corpses — this pairing holds at a
comparable corpse count. These are missing inventory-activity notifications, not
evidence of lost player gear or a failed profile restore. The original log
snapshot has since rotated; the report records the historical evidence.

## Correction

`CorpseCleanupService.StripGear` now sends the complete successful native
removal result through `Begin`, then `Succeed` in a `finally` block. Physical
removal already happened before this notification pair. A native refusal
emits neither event; a Begin observer failure still attempts the completion
needed to settle the activity and update the reactive slot view.

Before removal, an item with any existing activity on its corpse controller
is skipped. Native completion matches only event type and item, not event ID;
without this exclusion it could settle another operation's earlier Begin.
Unrelated idle equipment remains eligible. Busy gear stays on the trimmed
corpse and is not retried, an intentional safety tradeoff.

No inventories are rolled back, active-event lists cleared, events swallowed,
or corpse/player objects destroyed. The complete result retains resize and
search-state notifications. The existing Arena-only, teammate, open-loot,
own-corpse, distance, settled-ragdoll, held-weapon and secure-container
exclusions are unchanged.

## Native evidence

Verified against installed SPT 4.1 source in `../_decompiled/`:

- `EFT.InventoryLogic/ItemManipulator.cs:2123-2185` and `Slot.cs:983-995`:
  unrestricted removal mutates the slot and item address before returning.
- `EFT.InventoryLogic/DestroyResult.cs:98-108` and
  `ApplySortItemsPositionResult.cs:40-56`: native immediate removals issue
  Begin/Succeed after mutation. `ChangeItemsOperation` uses the same ordering.
- `EFT.InventoryLogic/ItemController.cs:1733-1748`: ProcessActivity adds Begin
  or removes the first matching type/item activity, logging if none exists.
  `SelectEvents` at 984-993 and `HasForeignEvents` at 1106-1110 explain why
  the same-owner busy-item exclusion is necessary.
- `EFT.InventoryLogic/RemoveResult.cs:81-89`: forward the full result for both
  statuses, including resize and successful search cleanup.
- `ContainerRemoveResult.cs:12-15` and `ItemAddress.cs:51-57`: use the retained
  original slot address after item detachment.
- `SlotItemAddress.cs:39-46` and `Slot.cs:727-730`: only Succeed updates the
  reactive slot value. `EFT/PlayerBody.cs:201-222,305-327` then cancels pending
  model work, unskins and returns the previous model to its pool. Begin does
  not duplicate that model release.

## Verification and limits

The checks link the entire production service and invoke its public Tick.
Contract fakes deliberately separate slot mutation, activity matching and
reactive model release. The old Succeed-only behavior is a positive control:
it produces the diagnostic while still releasing the model, proving that
the test does not confuse logging noise with a resource leak.

Coverage includes normal success, repeated sweeps, protected gear, teammates,
normal raids, current looting, active ragdolls, native refusal, failures before
and after mutation, and notification exceptions. Four source-copy mutations
omit Begin, omit completion, remove the busy-item guard, or remove the finally;
each must fail its intended assertion. Production files remain unchanged.

This does not recover every possible native observer failure. A raw
`Slot.OnAddOrRemoveItem` callback can throw after detachment but before a result
is returned. A persistent success-notification observer failure can prevent
the later slot-view update. These paths remain logged without falsely saying
the item stayed attached; no speculative retry or rollback is added.

Independent native/code review reported no unresolved P0-P2 findings. Local
tests cannot prove Unity pooling or FPS improvements. After restarting the
client, verify many kills with corpse stripping enabled: the original per-item
activity diagnostics should disappear, bodies/gear should still trim normally,
and raid exit/profile restoration should complete. This server has no GPU;
the live check remains an explicit handoff in `tools/CorpseCleanupChecks/GATES.md`.

---

# Releasing a trimmed corpse (2026-09-17)

## Why hiding was not enough

`../Redeploy/docs/RESPAWN-API.md`, header block "Messraid 17.09.2026" (Redeploy
`8542ef4`, analysis `tools/pipeline-timing/analyze.py`): a 26-minute Arena
Deathmatch FFA on Labyrinth with 37 respawns and zero player kills reproduced the
slowdown from `LONG-RAID-REVIEW-2026-09-16.md`. Arena's `pipeline` grew from
0.66 s to 1.66 s, driven by elapsed raid time rather than respawn count — it
jumped across a three-minute pause — and the growth sat entirely in steps that
search the whole scene.

The cause was this service. `FindObjectsOfType<LocalPlayer>()` found 53 objects
after 3 minutes, 232 after 14 and 415 after 23, and took 95, 178 and 303 ms
doing it, while the log read "trimmed 1 old corpse(s), 10 kept visible, 446
trimmed in total". A trimmed body is invisible and inert but still a complete,
active player GameObject with every MonoBehaviour it was born with, and the
count only ever grows. Everything that walks the scene pays for it, including
the game's own searches — which is why Redeploy 1.0.3/1.0.4 and KillcamReplay
0.9.10 removing their three own sweeps was a fix for those three sweeps only.

## What release does, and where each step comes from

`CorpseCleanupService.ReleaseTrimmed` runs at the end of each 5 s sweep, after
the trim pass. A body hidden in one sweep becomes eligible in the next
(`TrimState.Sweep`), so nothing is hidden and destroyed in the same frame and
the optional map-marker removal has run on it first.

The old attempt — the one the trim was written to replace — called
`GameWorld.DestroyLoot` and nothing else, so the skeleton went back through the
shared player pool with its `Player` component never disposed. The steps the
game itself performs and that attempt skipped, verified in `../_decompiled/`:

1. `BotsController.DestroyInfo(player)` — `EFT/BotsController.cs:650`. Used by
   `EFT/BaseLocalGame.cs:942-948` (`BotDespawn`), the game's own mid-raid path
   for a bot that leaves the map. It reaches `BotSpawner.DeletePlayer`
   (`EFT/BotSpawner.cs:682-694`): every `BotsGroup.DeletePlayerCauseDead` →
   `RemoveEnemy` (`BotsGroup.cs:733-747`), which clears the group's `Enemies`
   and `Neutrals` and each member's `Memory.DeleteInfoAboutEnemy`, plus
   `_allPlayers`, `_bots` and `player.AIData.Dispose()`. A normally dying bot
   never gets this: `BotOwner.OnDied` (`EFT/BotOwner.cs:698-721`) calls
   `BotsController.BotDied` and `Dispose`, not `DestroyInfo`.
2. `Player.Dispose()` — `EFT/Player.cs:30917-30973`. What
   `BaseLocalGame.CleanUp` (`EFT/BaseLocalGame.cs:1151-1163`) runs before it
   returns a player object, and what `Player.OnDestroy` (28429) runs by itself.
   It unregisters from `GameWorld` (2060-2085), leaves the speaker group, ends
   the audio subscriptions, disposes the conditional controllers and the
   movement context, and destroys the hands controller — and that last step is
   what hands the corpse's WEAPON model back to its own pool
   (`FirearmController.Destroy` → `AssetPoolObject.ReturnToPool`,
   `EFT/Player.cs:21343-21351`). It deliberately skips `_playerBody.Dispose()`
   while `Corpse` is set; `Corpse.Kill` owns that one.
3. The dead-body record, **after** 1 and 2. This ordering is the one genuine
   trap in the sequence: clearing the AI's knowledge is itself what re-creates
   the record, because `BotsGroup.RemoveEnemy` calls
   `DeadBodiesController.AddBody` for a person whose health controller says
   dead. A record left behind resolves its `Player` through
   `GameWorld.GetEverExistedPlayerByID`, which nothing ever cleans, and reads
   its position from `PlayerBones` (`DeadBody.cs:38-44`) — so it would point at
   a destroyed body. If the removal cannot be confirmed the release is refused
   and the body stays hidden.
4. `GameWorld.DestroyLoot(corpse)` — `EFT/GameWorld.cs:1322-1348`: the loot
   list, the net-id table, the item-owner registry, every player's interaction
   raycast, then `Corpse.Kill` (`EFT.Interactive/Corpse.cs:238-244`) →
   `PlayerBody.Dispose` (skins unskinned, slot models back to their pools) →
   `LootItem.Kill` (`EFT.Interactive/LootItem.cs:490-502`): shadows restored,
   culling registration dropped, `AssetPoolObject.ReturnToPool`.
5. Arena's own tables (`CorpseWeaponRegistry`, `CorpseLightService`, `Trimmed`)
   last, because until step 4 has run they are what keeps the hidden state
   honest and the optional map marker suppressed. Both `Forget` methods use a
   reference-null check on purpose: a destroyed object compares equal to null
   under Unity's operator, so the ordinary guard would skip exactly the key
   that has to go.

## Destroyed, not pooled

Step 4 sets `AssetPoolObject.IsStub` before the call. That is the pool's own
switch for "this object did not come from a pool"
(`EFT.AssetsManager/AssetPoolObject.cs:108-120`): the nested pool objects still
go back to their pools through `ClearChildren`, and the body itself is
destroyed instead of pushed back. It is reset in a `finally` in the one case
that matters — the destroy not happening — because `IsStub` left set on a live
pooled body would destroy it the next time anything returned it.

Pooling the body was the obvious alternative and is wrong here on two counts.
`PlayerPoolObject` is one of the two pool types the pool keeps ACTIVE while
idle (`AssetsPoolStaticConfig.IsGameObjectActiveInPool`, and
`AbstractAssetsPool.Push` only moves it to `UNSEEN_PLACE`), so pooling would not
shorten the scene at all — which is the entire point. And the only components
`ReturnToPool` removes are those in `RegisteredComponentsToClean`, which holds
the `Player` (`EFT/Player.cs:28055`), a box collider and a beacon setter, and
nothing else: not the `BotOwner` added at `EFT/BotOwner.cs:473`, not the
`Corpse` added at `Corpse.cs:104`, not the `ProxyTransportee` on the pelvis, and
certainly not whatever another mod attached to a bot. `BaseLocalGame.BotDespawn`
does exactly this and inherits exactly those leftovers. Destroying has no
recycling hazard at all.

Nothing puts the hidden renderers back, and nothing has to: both primitives the
trim writes are the pool-restored ones (argued at `TrimCorpse`), so every model
that outlives the body has its `forceRenderingOff` cleared by the pool as it
goes back in. Writing `false` at release time would instead write over
renderers that may already belong to a living bot.

## What holds a reference to a dead body, and what happens to it

| Holder | Handling |
|---|---|
| `GameWorld.RegisteredPlayers`, `AllAlivePlayersList`, `allAlivePlayersByID`, `AllAlivePlayerBridges`, `PlayersColliders`, `ItemOwners` | `Player.Dispose` → `UnregisterPlayer`. Already done at death by `OnDeadCoroutine`, so this is the idempotent repeat the game itself performs. |
| `GameWorld.LootList`, `LootItems`, the corpse's `ItemOwner` | `DestroyLoot`. |
| `GameWorld._allPlayersEverExisted`, `AllPlayerBridgesEverExisted` | Never cleaned by the game. The entries survive as destroyed Unity objects, which is what the DynamicMaps gate below is about. |
| `DeadBodiesController` (bots pathing to bodies to loot) | Step 3. The only reader in Arena is `BotDeadBodyWork.UpdateCheck`, which `NoCorpseLootingPatch` gates off. |
| `BotsGroup` enemy/neutral memory, each member's `BotMemory` | Step 1. A group created later can only add living players (`CheckAndAddEnemy` refuses a dead one). |
| SAIN | Drops its `BotComponent` at death already: it subscribes to `BotSpawner.OnBotRemoved`, which `BotSpawner.BotDied` raises from `BotOwner.OnDied`, and its tables are keyed by profile id. Nothing of SAIN's survives to see the destroy. |
| PitFireTeam | Roster members are **never released**, not even for a round-start clear, which does lift the hiding exemption. Recorded at trim time (`TrimState.Protected`, when PFT's answer is still reliable) and re-read at release. |
| The killfeed and the scoreboard | Keyed by immutable identity, not by body (`ASSIST-SCORING-REVIEW.md`); no `Player` reference is retained. |
| DynamicMaps corpse markers | A body is not released while any tracked provider still holds its marker (`DynamicMapsCorpseMarkers.HoldsMarker`, fails closed). 1.2.1's `OnMapChanged` re-adds every key it still has and reads the body's nickname and world position doing it, which a destroyed body cannot answer. Its other creation paths gate on `HasCorpse()`, which a released body fails; the creation prefix refuses a destroyed body as well. `OtherPlayersMarkerProvider` only ever removes. |
| KillcamReplay | Its `SnapshotRecorder` already retires a track whose player has gone unsampled for two seconds, explicitly so it does not pin a dead `Player`. Its death replay uses the LOCAL player's corpse as the puppet source — which is the NEWEST own body at the moment of that death, and only the newest own body is exempt from release since 2026-09-18 (see "Own corpses release too"). Not run against the new release timing in-game; flagged as an open risk there, not ruled out. |
| AutoIFF | Keyed by profile id (`identifiedBots`); holds no body. |
| Redeploy.Core / profile protection | Read the local player's own corpse (`EquipmentSurgeon`, `NearSpawnPolicy`, `RespawnService.FixOwnCorpseRenders`) — the NEWEST one, which these always mean and which is the only own body **never released** since 2026-09-18 (see "Own corpses release too"). Recorded at trim time and re-read live at release against `MainPlayer.ProfileId` plus a fresh `world.LootList` scan for which own corpse is newest — the profile id is the same across every respawn, so it covers every body that was ever the player's, but only the newest of them is exempt. |
| Loot on the corpse | Already stripped and unreachable before release: the body is hidden, dropped from the AI's body records, and bots do not loot corpses in Arena. The secure container and the held weapon are what the trim leaves; `PlayerBody.Dispose` and the hands controller pool their models. Release is refused while the player has that body's loot window open or is interacting with it — re-read at release, not trusted from the trim. |
| `ArenaAreaRuntime.Actors`, `CorpseLightService`, `CorpseWeaponRegistry` | `Actors` already prunes on Unity's null check; the other two are cleared in step 5. |
| `TriggerZone` MoveInside coroutines | Not found by the code review. `CorpseTriggerZones` retires the body's own coroutine before the destroy; see below. Pre-existing rather than caused by the release. |
| `Player.LastAggressor` on a living, wounded player | Not found by the code review either, and the one place where the release genuinely broke something. A body stays hidden while anybody still names it (`CorpseAggressors`); see below. |
| `GameWorld.GetEverExistedPlayerByID` / `GetEverExistedBridgeByProfileID` | Never pruned, on purpose: a destroyed `Player` still answers the managed reads most callers make. What made them bite is anything that keeps a body's PROFILE ID as a damage source — the next row. |
| A tripwire the body planted, a grenade it threw, a shot it fired — each by profile id | Found by measurement, not by the review: the Labyrinth boss's own tripwires. A body stays hidden while any of them still names it, and for a quiet period after (`CorpseDamageSources`); see "What held that body" below. |

## What the measuring raids found

Two separate things, and only one of them was Arena's doing. The comparison is
clean because the control raid of the same evening (rotated log
`LogOutput-2026-09-17_20-52-22.log`, 405 bodies trimmed, no release) ran the
same mod set with only the previous ArenaMode.

### Trigger-zone coroutines — pre-existing, now fixed

The first release raid (24:25, 36 respawns, 379 trimmed, 344 released) logged
16 of these:

    UnityEngine.Transform.get_position ()
    EFT.BifacialTransform.get_position ()
    EFT.GameTriggers.TriggerZone+CG_MoveInsideRoutine.MoveNext ()

The control raid logged 17 of the same, and an 18:08 session 18 — so this is
NOT a consequence of the release, and the rate barely moved. A trigger zone in
MoveInside mode starts an endless coroutine per player who enters it
(`TriggerZone.cs:172`), hosts it on ITSELF rather than on the player, and reads
`player.Transform.position` every frame forever (`TriggerZone.cs:198-211`). The
only thing that stops it is that player's collider count in the zone returning
to zero, and a dead body never walks out, so the coroutine outlives its
subject and something about a dead body's transform eventually makes it throw.
The coroutine then ends itself on the exception, which is why the count per
raid is a couple of dozen rather than thousands. Labyrinth has these zones
around its boss alarm.

`CorpseTriggerZones.Release` retires exactly that body's coroutine first — the
same pair of steps the zone's own exit path performs, `Remove` then
`StopCoroutine`, for the one player id about to disappear. It reads the zones
from `LocationScene`'s own per-scene `ITriggerEntity` registry, once per raid,
the same source `LabyrinthAlarmPatches` uses; there is no scene search. The
zone's enter/exit bookkeeping (`_playersInside`, `_playerInCollidersCount`) is
left alone: it holds ints rather than bodies, and a dead player never leaving a
zone is a native condition that predates the release.

This is the one Arena native integration that fails OPEN. If the private
coroutine table cannot be bound, the entire consequence is one logged native
exception per affected body and a coroutine that ends itself — a warning, not a
reason to give up the release. The `leaves-the-trigger-coroutine-running`
control covers the ordering. The two raids after the fix logged zero and one,
against 16-18 in every raid before it. The one that got through is worth
remembering: the zone list is bound ONCE per raid, at the first release, so a
zone belonging to a `LocationScene` that was not loaded yet is not in it.

### Pending aggressors — introduced by the release, now guarded

The same first release raid logged five `NullReferenceException`s the control
raid did not have a single one of, and every one of them came in through the
same door:

    (wrapper dynamic-method) EFT.Player.DMD<EFT.Player::OnDead>(...)
    EFT.LocalPlayer.OnDead (EFT.EDamageType damageType)
    EFT.HealthSystem.ActiveHealthController.Kill (EFT.EDamageType damageType)
    ...ActiveHealthController+Bleeding.BleedHealth ()

`Player.ManageAggressor` remembers who hurt you in `LastAggressor`
(`EFT/Player.cs:29526,29558`), and `Player.OnDead` hands that reference to
`OnBeenKilledByAggressor` as its FIRST statement (`30099-30101`), which
dereferences it. Nothing ever clears the field when the aggressor's body goes
away, and the field is an `IPlayer` — so Unity's lifetime operator does not
apply to the native `!= null` check in front of it, and a destroyed body sails
straight through. Every one of the five was a bot that had been wounded, whose
attacker died and was released, and who then bled out.

The consequence is not a log line. The throw is in the first statement of
`OnDead`, so the rest of that death never runs: no corpse, no `OnPlayerDead`,
no `BotsController.BotDied` — which is the spawner bookkeeping whose leak
starves bot refills, precisely the wreckage the first destroy attempt was
abandoned for, arriving by a different route.

`CorpseAggressors.IsPendingAggressor` therefore keeps a body hidden while any
living player still names it. It reads `GameWorld.AllAlivePlayersList`, which
is the world's own short list, compares by reference (a profile id would match
every body that participant ever had) and writes nothing. Bleeding is bounded —
it kills or is healed — so the body becomes releasable shortly afterwards, and
until then it costs exactly what a hidden body has always cost. It fails
CLOSED, unlike the trigger-zone retirement, and for the opposite reason: what
is at stake is a death that never completes.

### Settled (2026-09-17, 23:06-23:19 Labyrinth raid on `90add07`)

The release IS the cause, and the mechanism is not either of the two candidates
below. Measured, not argued: 12:32 Labyrinth Deathmatch FFA, give up every
~35-45 s, 207 player bodies created and 173 released, zero release failures,
eleven diagnostic reports, zero diagnostic stand-downs.

**Every report has the same shape. Eleven of eleven:**

| Field | Every single report |
|---|---|
| `victim` | `live` — a healthy AI bot, `alive=True` |
| `aggressor` / `shooter` | `DESTROYED`, `alive=False` |
| that body's `release=` | `RELEASED`, 3.7 s to 232 s earlier |
| `aliveById` | `null` — the alive table is correctly pruned |
| `loyalty`, `health` | `set` on both sides, in all eleven |

**The throwing method**, recovered from the exception's own trace read inside the
finalizer:

    UnityEngine.Transform.get_position_Injected        <- throws here
    UnityEngine.Transform.get_position
    EFT.BifacialTransform.get_position
    EFT.Player.get_Position                            <- on the DESTROYED body
    BotWeaponManager.OnBulletNear (BotOwner, IPlayer source)
    EFT.BotMemory.SetUnderFire (IPlayer source)
    EFT.BotMemory.GetHitInner (DamageInfo)
    (wrapper dynamic-method) EFT.BotMemory.DMD<EFT.BotMemory::GetHit>

`BotWeaponManager.OnBulletNear` (`../_decompiled/BotWeaponManager.cs:199-205`)
is four lines long:

```csharp
public void OnBulletNear(BotOwner arg1, IPlayer source)
{
    if (source != null) { ChecnBlockAuto(source.Position); }
}
```

`source` is an **`IPlayer`**, so that `!= null` is a plain reference check and
Unity's lifetime operator never reaches it — a destroyed body sails through it
exactly as the review predicted for `LastAggressor`, at a site nobody had
looked at. `source.Position` is then `Player.Position` → `BifacialTransform`
→ a destroyed `Transform`, and that throws. `OnGetHit` two lines below has the
same shape through `arg1.Player.iPlayer.Position`.

Three things this rules out, two of them instrumented on purpose as negative
controls:

- **Not an Arena prefix.** No Arena frame appears anywhere in the trace.
- **Not `ManageAggressor`** and not its null branch, which was the leading
  hypothesis. It is not on the stack at all.
- **Not `_healthController` and not `Loyalty`**, the two members
  `Player.ApplyDamageInfo` dereferences unguarded in its own IL. Both reported
  `set` in all eleven reports.

**`ShotDelegate` is the same defect, not a second one.** Its three reports name
the same released body as shooter, and its trace is
`Player.ApplyShot` ← `PlayerBridge.ApplyShot` ← `BodyPartCollider.ApplyHit` ←
`ShotDelegate`. The 33-66 ms correlation was siblings of one cause. One fix
covers both.

**Every one was a grenade, and eleven reports are eight exceptions.** (Corrected
2026-09-18; the first write-up read the 1.7-damage `Bullet` as a bleed tick.)
`damage=Bullet` is the grenade's FRAGMENTS: shots in the shared calculator, ammo
`63b35f281745dd52341e5da7` (F1 shrapnel) in every `ShotDelegate` report, landing
28-103 ms after the blast's own `GrenadeFragment` report on the same victim.
Bleeding cannot reach this path at all: `Bleeding.BleedHealth` applies damage
through `HealthController.ApplyDamage` (`ActiveHealthController.cs:707-717`),
never through `Player.ApplyDamageInfo`, so it never raises `BeingHitAction`
(`EFT/Player.cs:30055`) and never reaches `BotOwner.OnGetHit`. And each
`ShotDelegate` report is the `ApplyDamageInfo` exception of 1-3 ms earlier,
rethrown up through `Player.ApplyShot` and caught a second time by the outer
finalizer — Unity logged eight exceptions for the eleven reports. Five
explosions, then.

**How a released body is still named as an aggressor.** Two native mechanisms,
both verified in the decompile, and together they make the window wide rather
than one frame:

- `Shot` resolves its shooter through
  `GameWorld.GetEverExistedBridgeByProfileID` (`EFT.Ballistics/Shot.cs:460`) —
  the bridge twin of the one table nothing prunes. Any damage that names a
  released body's profile id therefore gets a live bridge whose `iPlayer` is the
  destroyed `Player`. The pooling is not the problem; `Shot.Reset` does clear
  `Player` (`:414`). The resolution is.
- `BotMemory.GetHit` does not call `GetHitInner` immediately unless the bot
  already has a `GoalEnemy`. Otherwise it defers it behind a
  `HIT_DELAY_WHEN_PEACE` / `HIT_DELAY_WHEN_HAVE_SMT` timer with the `DamageInfo`
  captured in the closure (`EFT/BotMemory.cs:681-694`). The body can be released
  inside that delay. (Not on any of these traces — `GetHitInner` sits directly
  under `GetHit` in all of them — and short: the database sets both delays to -1,
  never, for every bot type; SAIN sets 0.4 s and 0.1 s.)

**One body did all of it.** All eleven exceptions name the same profile
(`6aac5637…`, "Shadow of…", released in sweep 19), spread over 85 seconds, while
172 other releases in the same raid caused none. So the trigger is narrow: not
"a released body" but "a released body something still holds a damage reference
to". That is what a fix has to find, and it is why the existing
`CorpseAggressors` guard is not enough — it checks living players'
`LastAggressor`, which covers neither a `DamageInfo` captured in a deferred
`GetHit` timer nor a projectile still in flight.

**What the fix is not.** Pruning `_allPlayersEverExisted` or its bridge twin,
for the reason given above: a destroyed `Player` is still good for the managed
reads most callers make, and nulling it trades this for a new set of null
dereferences. The shape that fits the evidence is to keep a body while anything
can still name it as a damage SOURCE, which is the pending-aggressor guard
widened from `LastAggressor` to the in-flight and deferred cases — once it is
established what held that one body. The next section does that.

### What held that body: its own tripwires (2026-09-18)

**The one line.** The same raid log has eight lines of this shape, 23:07:58 to
23:11:49, and no other profile in them:

    [Error  :      SAIN] [SAIN.Components.GrenadeController] : [[ActivateGrenade().ThrowGrenade().GrenadeThrown()]:] : [Player Null from ID 6aac56372266080c60611ca3]

SAIN's `GrenadeThrown` handler resolves a new grenade's `ProfileId` through
`GetAlivePlayerByProfileID` and logs this when nobody alive has it. The event it
listens to, `GlobalEventDispatcher.ThrowGrenade`, is raised from exactly two
places: a living player's own grenade hands (`EFT/Player.cs:15708,15712`, under
that player's own id) and `TripwireSynchronizableObject.ActivateGrenade`
(`EFT.SynchronizableObjects/TripwireSynchronizableObject.cs:274-298`), which
creates the grenade under `PlacerPlayerId`. A dead body throws nothing. These
are tripwires the body planted while it was alive, going off one by one as bots
walked into them.

**The body.** `6aac5637…` "Shadow of Tagilla" is the Labyrinth boss
`bossTagillaAgro`. The burst-spawn line lists `bossTagillaAgro x1` among the
others alive up to 23:07:23 and no longer from 23:07:33; the release sweep took
it at 23:07:59.6. `BossTagillaAgro.PrewarmMines` queues tripwires and
`BossLogicUpdate` plants them (`BossTagillaAgro.cs:69-85`). A tripwire keeps its
planter as an ID for its whole life (`:160`), and nothing about the planter's
death or release touches it.

**Every exception follows an activation by the fuse**, 3.51-4.22 s, in all three
release raids. And the evening's logs make a complete two-by-two:

| Log | release | tripwire activations (one dead planter each) | `ApplyDamageInfo` + `ShotDelegate` |
|---|---|---|---|
| 18:08 (`LogOutput-2026-09-17_18-08-46`) | no | 8 | 0 |
| 19:35 (`…_19-35-59`) | no | 9 | 0 |
| 20:52 control (`…_20-52-22`) | no | 2 | 0 |
| 21:47 (`…_21-47-49`) | yes | 0 | 0 |
| 22:13 (`…_22-13-23`) | yes | 10 | 7, each 3.52-3.59 s after one |
| 22:14 (`…_23-01-27`) | yes | 6 | 6, each 3.51-4.22 s after one |
| 23:06 on `90add07` | yes | 8 | 8, each 3.52-3.79 s after one |

Twenty-one of twenty-one. The planter's tripwires go off whether or not Arena
releases bodies; they only throw once the body is gone. That also settles the
older "grenade finding" below — it was never luck, and the correlation with
the trigger-zone commit was a coincidence of which raids were Labyrinth raids
with a boss kill in them.

**Why exactly one body.** The 172 other releases were PMCs and Scavs, and they
plant nothing. There is one planter per Labyrinth raid.

**Why the pending-aggressor guard could not see it.** A tripwire holds an id,
not a reference, and until it goes off nobody's `LastAggressor` names the
planter — by the time somebody's does, the explosion that set it has already
thrown. And `TripwireManager`, the obvious list to read, drops a tripwire the
moment it is TRIGGERED (`EFT/GameWorld.cs:1698-1702`), which is exactly when its
grenade starts burning: at the release sweep of 23:07:59.6 one of this boss's
tripwires had been triggered 1.2 s earlier and was no longer listed there.

**The fix: `CorpseDamageSources`.** `MayRelease` keeps a body while the world
still names its profile id as a damage source:

- a tripwire it planted that has not finished, read from the sync-object
  processor's own two lists, which hold a tripwire from `TakeFromPool` until it
  is returned. `Wait` (armed), `Active` (grenade burning), `Exploding` (the
  30 s `DestroyedSeconds` after the grenade went, fragments included) and any
  state this code does not know all count; `Exploded`, `Inert` (disarmed or
  expired, the grenade dropped as plain loot) and `None` (pooled, planter
  cleared) do not;
- a grenade it threw or a flare it fired that is still in `GameWorld.Grenades`
  and not destroyed — not observed; a tripwire's grenade is never registered
  there, but a grenade thrown or a flare fired just before death is. A flare
  keeps its shooter as a private `IPlayer` and resolves it on impact through the
  same table (`EFT/FlareCartridge.cs:176-190`; found by the adversarial review).
  A grenade whose explosion reaches nobody does not count: the native
  explosion only touches who is inside max(2 x `MaxExplosionDistance`,
  `Blindness.y`, `Contusion.y`) (`EFT/ExplosionSharedMethods.cs:306`), both
  smoke grenades carry zero for all three, and a smoke is the one grenade never
  destroyed after it goes off (`SmokeGrenade.OnExplosion` replaces the base
  `DestroyImmediate`) — counted, it would hold its thrower all raid. Any other
  kind of throwable counts as naming every body;
- a shot it fired still in flight, which includes every fragment, because a
  grenade's calculator IS the world's shared one (`EFT/GameWorld.cs:1061-1064`).
  The client calculator is read too when it exists; offline it normally does not.

Then for a quiet period: `DamageSourcesQuiet` releases only when a reading finds
nothing AND an earlier reading at least 2 s before already found nothing. That
is the deferred `GetHit` above, which no reading can see: a dead body cannot
plant, throw or fire, so once nothing names it nothing new can start to, and
every hit already dealt has run within SAIN's 0.4 s. The first reading is taken
at the trim, so a body that nothing names still goes one sweep after it was
hidden, exactly as before; a sweep that a round-start clear pulls forward
cannot cut the period short.

Profile id rather than reference, because an id is all a tripwire, a grenade or
a shot holds. It can only match more bodies — which, at the time this was
written, meant the one participant whose bodies share an id, the local
player, was never released anyway, so this could only ever over-protect
bots. **That precondition no longer holds since 2026-09-18** — see "Own
corpses release too" below, which is also where the guard gained a way to
tell an old own body's own trailing source apart from the live player's
current one sharing its id. It fails CLOSED for the reason `CorpseAggressors`
gives: anything unreadable keeps the body and is reported once per raid.

The cost is the one a hidden body always had: a planter whose tripwires nobody
walks into stays hidden until they expire (`TRIPWIRE_INERT_TIME`, 1200 s by
default), and there is one planter per Labyrinth raid. SAIN's "Player Null from
ID" lines will keep appearing — they are the native activation of a dead
planter's tripwire, present in the three raids that released nothing at all,
not a defect of either mod. What this does not cover: another mod or preset
raising `HIT_DELAY_*` above 2 s would reopen the deferred case.

**The log could never have said this.** For the identical exception, Unity
logged `(wrapper dynamic-method) EFT.Player.DMD<EFT.Player::ApplyDamageInfo>`
as its innermost frame and nothing above it, while the finalizer read four more
frames under it. So the truncation described in the next section is real and
measured, not inferred. `Exception.TargetSite` was null in every report
(`thrownIn=unknown`), and on some of these NREs Mono leaves `StackTrace` empty
too — the trace is what did the work here, so a report without one is worth
nothing, and the first-report full-trace slot is now only spent on a report that
actually has a trace (fixed after this raid; the evidence above was produced by
`90add07`, whose eight-frame form happened to cover the whole chain).

Also of note from the same raid: **zero `OnDead` exceptions** across 173
releases, against five before the pending-aggressor guard. That guard holds.

### The original finding, as it stood before the raid above

(Kept as the record of what was believed. The cause is in "What held that
body" above: the planter's tripwires, not luck.)

Both raids after the trigger fix logged four of a third kind each, which the
control raid and the first release raid did not have at all:

    (wrapper dynamic-method) EFT.Player.DMD<EFT.Player::ApplyDamageInfo>(...)
    BodyPartCollider+PlayerBridge.ApplyDamageInfo (...)
    BodyPartCollider.ApplyEnvironmentalDamage (...)
    EFT.ExplosionSharedMethods.AffectPlayersShared (...)
    EFT.Grenade.Explosion () / InvokeBlowUpEvent ()

Not explained, and the evidence does not even settle whether the release causes
it. The counts below are every log of the evening recounted by exception BLOCK
(one `Stack trace:` header each, classified by the frames under it) rather than
by substring, which corrected three of the numbers first recorded here and added
a whole kind that had been missed. Same mod set throughout except ArenaMode; one
raid per log, and the trim/release totals are each log's last sweep line.

| Log (raid) | release | `CorpseTriggerZones` | `OnDead` | `ApplyDamageInfo` | `ShotDelegate` | `TriggerZone` |
|---|---|---|---|---|---|---|
| 17:13, 17:46, 19:46 | no | no | 0 | 0 | 0 | 0 |
| 18:08 | no | no | 0 | 0 | 0 | 18 |
| 19:35 | no | no | 0 | 0 | 0 | 30 |
| 20:52 control, 405 trimmed | no | no | 0 | 0 | 0 | 17 |
| 21:47, 379 trimmed / 344 released | yes | no | **5** | 0 | 0 | 16 |
| 22:13, 221 trimmed / 207 released | yes | yes | **2** | **4** | **3** | 3 |
| 22:14, 159 trimmed / 147 released | yes | yes | 0 | **4** | **2** | 1 |

Three corrections and one addition against the numbers first written here:

- The 22:13 raid was recorded as having no `OnDead` and no `TriggerZone`
  exceptions. It has two and three. The pending-aggressor guard therefore did
  NOT take that kind to zero — it went 5 → 2 → 0, and the last raid is the only
  one that is actually clean. The trigger-zone retirement went 16 → 3 → 1, not
  16 → 0 → 1.
- A fourth kind was missed entirely: `ClientGameWorld.ShotDelegate`, three and
  two, in exactly the two raids that also have `ApplyDamageInfo` and in no other
  log of the evening. See below.
- The two release totals recorded as 119 and 147 are mid-raid snapshots; those
  raids finished at 207 and 147.
- `GameWorld.DoWorldTick` appears in several of these traces and is NOT a defect
  of its own: it is the shared tail frame under both `Bleeding.BleedHealth` and
  `BallisticsCalculator.UpdateShots`, so its count simply equals `OnDead` plus
  `ShotDelegate`.

So `ApplyDamageInfo` appears only with the release, but only in the two raids
that ALSO have the trigger-zone retirement, and there is no mechanism by which
stopping a coroutine could cause a grenade to fail. Grenade events near a body
that has just been released are a matter of luck, so four-and-four against zero
may simply be that.

The consequence is bounded but not nothing: the exception leaves
`Grenade.Explosion` through its fuse coroutine, which Unity catches and logs,
so the remaining victims of that one explosion take no damage.

The leading hypothesis is the aggressor side. `AffectPlayersShared` collects
victims from `GetAlivePlayerBridgeByCollider`, so the victim should be alive and
registered, whereas `Player.ManageAggressor` resolves the attacker through
`GameWorld.GetEverExistedPlayerByID` (`EFT/Player.cs:29515`) — and
`_allPlayersEverExisted` is the one table `UnregisterPlayer` never touches, so
it can still hand back a destroyed body. Against that: the destroyed body
compares equal to null under Unity's operator, which sends that code down its
own null branch, and the null branch only reads managed state. The top frame is
a Harmony-patched `ApplyDamageInfo` with Arena prefixes on it, so an Arena
prefix is equally possible; both are unverified.

Pruning `_allPlayersEverExisted` on release was considered and rejected. A
destroyed `Player` is still perfectly good for the MANAGED reads most of its
callers make (`Profile`, `Nickname`, `EftStats`), so replacing it with null
would trade this for a new set of null dereferences in code that is written
expecting an object.

### The missed kind: a shot that throws one frame after the grenade did

    (wrapper dynamic-method) EFT.ClientGameWorld.DMD<EFT.ClientGameWorld::ShotDelegate>(...)
    EFT.Ballistics.BallisticsCalculator.UpdateShots (System.Single simulationTime)
    EFT.Ballistics.BallisticsCalculator:ManualUpdate(Single)
    EFT.GameWorld:DMD<EFT.GameWorld::DoWorldTick>(GameWorld, Single)
    EFT.GameWorldUnityTickListener:Update()

Three in the 22:13 raid and two in the 22:14 one, none anywhere else. What makes
this worth more than a fourth row is the timing, which is not a coincidence any
reasonable reading survives:

| `ApplyDamageInfo` | `ShotDelegate` | gap |
|---|---|---|
| 21:54:11.291 | 21:54:11.343 | 52 ms |
| 21:54:36.885 | 21:54:36.951 | 66 ms |
| 22:01:52.049 | 22:01:52.084 | 35 ms |
| 22:19:27.865 | 22:19:27.898 | 33 ms |
| 22:19:49.338 | 22:19:49.373 | 35 ms |

Five of five follow an `ApplyDamageInfo` exception by one or two frames. Not one
occurs without one in front of it, and never the other way round. Three of the
eight `ApplyDamageInfo` exceptions have no follower, so this is a consequence or
a shared trigger, not an independent defect — and it means the grenade finding is
very likely the only thing that has to be fixed. The `OnDead` exceptions in the
same raids (21:58:53, 22:02:37) are NOT part of the pattern; they are the
pending-aggressor defect above.

One tempting mechanism was checked and does not hold. `ExplosionSharedMethods`
does all of its work in seven PUBLIC STATIC buffers (`_playerBuffer`,
`_colliderBuffer`, `_bodyPartCollidersBuffer`, `_hitsBuffer`, `_damagedParts`,
`_damagedPlayers`, `_armorDamageBuffer`), and an `AffectPlayersShared` that
unwinds mid-loop leaves every one of them populated — but each is cleared at the
entry of the method that next uses it (`Explosion` clears `_playerBuffer` as its
first statement after the dummy check, `AffectPlayersShared` clears the next two,
and the per-victim ones are cleared inside the loop). A dirty buffer carrying
over is therefore not the explanation.

What the two sites do have in common is structural, and it is the reason the
diagnostic instruments both: each dereferences an aggressor BRIDGE in its own IL
with nothing guarding what is behind it. `Player.ApplyDamageInfo` does it on its
second line (`player.iPlayer.ProfileId` behind an interface null check that says
nothing about `iPlayer`), and `ClientGameWorld.ShotDelegate` does it in the
explosive-ammunition branch (`shotResult.Player.iPlayer.ProfileId`, unguarded),
next to two more unguarded reads — `shotResult.Ammo`, and
`shotResult.HittedBallisticCollider.HitType` in the offline-only BTR branch that
every Arena raid takes, which is tested with Unity's operator and then read
again. That last one cannot be a destroyed collider, incidentally: `HitType` is a
plain field, which a destroyed object still answers, so only a genuinely
reference-null collider throws there.

A single actor whose bridge has become unusable would make both paths throw
within a frame or two of each other, which is exactly the observed shape. That is
a hypothesis, and the finalizer on each site records the state that confirms or
kills it.

### Why none of these traces says where it came from

Every one of the three kinds above is logged with a bare
`(wrapper dynamic-method)` frame on top and nothing above it, which is the whole
reason "an Arena prefix or the original body?" is an open question rather than a
lookup. Two different things produce that frame and the log cannot separate them:

- the throw really being in the original method's body, whose IL Harmony copies
  INTO the dynamic method, so the body's own frame IS the wrapper; or
- the trace having been reset by the rethrow at the end of Harmony's finalizer
  block, which discards everything under it.

The evening supplies one control for this, and it fits: of the four kinds those
raids logged, the three with a bare wrapper frame are all on methods Arena puts
a FINALIZER on — `Player.ApplyDamageInfo` (`CombatPatches`,
`ArenaSelfGrenadeTaunt`), `Player.OnDead` (`ArenaAssistCapture`) and
`ClientGameWorld.ShotDelegate` (`ArenaShotAwardHooks`) — while the fourth,
`TriggerZone+CG_MoveInsideRoutine`, is an unpatched native coroutine and is the
only one whose trace shows its innermost frames (`UnityEngine.Transform`
`.get_position` under `EFT.BifacialTransform.get_position`). Three for three,
with a negative control. Either way the consequence is the same: the trace has
to be read INSIDE the finalizer, before anything rethrows.

Read there, the frames also narrow the grenade case by themselves. Nothing sits
between the wrapper frame and `PlayerBridge.ApplyDamageInfo`, and the methods
`Player.ApplyDamageInfo` calls are virtual and would each show a frame of their
own — so if the trace is not reset, the throw is in that method's own top-level
IL, and it has exactly three unguarded dereferences there:

- `_healthController.IsAlive`, its first line, read with no null check at all;
- `player.iPlayer.ProfileId`, where `player` is the aggressor BRIDGE and the
  `player != null` in front of it is an interface reference check that says
  nothing about `iPlayer`;
- `player2?.Loyalty.MarkAsAggressor(this)`, which guards `player2` and NOT
  `Loyalty` — and `Loyalty` is assigned only by `Player.Init`, so a body that is
  registered as alive before its Init finished throws there.

None of the three needs a destroyed object, which is why the diagnostic records
the state of every candidate rather than assuming the release is at fault.

### The diagnostic (2026-09-17)

`ArenaDamageExceptionLog` — a Harmony FINALIZER on `Player.ApplyDamageInfo` and
one on `ClientGameWorld.ShotDelegate`, both of which record and then return the
exception unchanged. Nothing about a damage outcome changes. `LogDamageExceptions`
(Diagnostics, default on) gates it, and it is on by default because it costs one
null check until something throws: the finalizers do not build a string, read a
field or evaluate the Arena gate until they have been handed an exception.

Each report is one line, and it answers the two questions the log could not:

- **Which side is destroyed.** Every candidate is printed as `null`,
  `DESTROYED` or `live`, because those are three states and the native code
  cannot see the difference between the first two wherever it null-checks
  through an interface. `DESTROYED` is a reference that is alive to the managed
  runtime and equal to null under Unity's operator. The candidates are the
  victim, the aggressor bridge, the bridge's `iPlayer`, the results of asking
  BOTH resolution tables for that profile id (`GetEverExistedPlayerByID`, which
  nothing prunes, and `GetAlivePlayerByProfileID`, which death prunes), the
  victim's `LastAggressor`, and the body-part collider the explosion chose. Each
  carries its profile id, nickname, AI flag, alive flag, and whether its
  `Loyalty` and `HealthController` are set — the two unguarded members above.
- **Where the throw came from.** `thrownIn=` is the exception's `TargetSite`,
  which names the throwing method outright, and the first report of each raid
  carries its complete stack trace; later ones carry the innermost eight frames.
  An Arena prefix appears under its own name; the original body does not.

Two cross-references make the causation question answerable rather than
inferred. `release=` comes from `CorpseReleaseLedger`, a bounded per-raid record
of the bodies this service actually destroyed, written on confirmed releases
only and holding no `Player` reference (a profile id is identity enough, and the
long-raid heap is an open question that a diagnostic must not feed). `corpse=`
comes from `CorpseCleanupService.DescribeCorpseState` and separates a body that
is merely HIDDEN — which this service has always produced — from one that is
gone. If a body named in a report is neither, the release is not what broke it.

The reports are capped at 32 per raid and the cap announces itself; a report
that throws stands the whole thing down for the raid with one warning, because a
diagnostic must not become the failure it is looking at.

It stays on after the fix, as the fix's acceptance test and its regression
alarm: a Labyrinth raid with a boss kill on the fixed build should report none.

`ReleaseTrimmedCorpses` (Performance, default on) remains the off-ramp:
turning it off restores the old hide-only behaviour exactly.

### GPU acceptance (2026-09-18, 06:34-06:48 Labyrinth raid on `d265353`)

Client `0.1.0+d265353`, SHA256 `81DBDD36…F157C`, confirmed in the running game
by its plugin table (`2026-09-18 06:28`, session start 06:31:01) and by both
`ArenaDamageExceptionLog` finalizers being patched. Deathmatch FFA, endless,
give up every ~45 s: 14:10 raid time, 15 deaths, 244 player bodies created, 218
trimmed and 200 released — more than the 173 of the raid above.

The same scenario, and this time the guard in it. `bossTagillaAgro`
(`6aacbf3c…`) died in the first minute and a half; its tripwires went off
eleven times from 06:35:21 to 06:43:48, every one after its death. The sweep
logged `keeping 6aacbf3c… hidden while a tripwire it planted (Wait) still names
it as a damage source` at 06:36:09 and `nothing names 6aacbf3c… as a damage
source any more` at 06:44:25 — 36.6 s after the last activation, which is the
fuse, the 30 s `Exploding` state and a sweep. No activation followed.

**Zero `ArenaDamageExceptionLog` reports and zero NullReferenceExceptions for
the whole session**, against eleven reports from eight activations in the raid
above. The only exceptions in the log are two
`TriggerZone+CG_MoveInsideRoutine` ones at 06:48:48, six seconds after "Arena
session ended", while the scene unloads. That is not this finding and not new:
the raid above ended with the same two, and the raids that released nothing
end with 17-30 of them (18:08, 19:35, 20:52). The trigger-zone counts in the
table further up are mostly this teardown burst, not mid-raid throws.

## Bounded work per sweep

At most eight bodies per sweep. A release walks every bot group and tears down a
player hierarchy, and a round-start clear can hide fifty bodies at once — so
without a cap the slow accumulation would be replaced by a frame spike. Eight
per five seconds is well clear of the production rate: the measuring raid
trimmed 446 bodies in 26 minutes, one every 3.5 seconds.

**Zombie mode (2026-10-07).** Eight per five seconds was never tuned against a
mode where up to 56 bodies can be hidden by one watchdog clear-out
(`ZombieSpawnService.RetireAllAliveInfected`) — draining that at the ordinary
rate took 33.5 s (host log, 2026-10-06, 46 bodies), during which every one of those bodies was still a
complete, active player GameObject hierarchy sitting in the scene, in
`GameWorld.LootList` and in every scene search anybody runs — the same
bounded-scene-growth cost "What holds a reference to a dead body" above
already measured, not a per-frame player-tick cost: a dead player leaves
`AllAlivePlayersList` one frame after death (`Player.OnDead` starts
`OnDeadCoroutine`, which calls `GameWorld.UnregisterPlayer`), well before
hiding or trimming ever runs, so `GameWorld.PlayerTick`/`AfterPlayerTick`
never tick it again regardless of how long release waits (corrected
2026-10-07; an earlier version of this paragraph claimed the opposite — see
ZOMBIE-MODE-REVIEW.md, "v3 (2026-10-07)", for the full correction). Zombie
mode now sweeps every second instead of every five
(`CorpseCleanupService.ZombieCadenceSeconds`) and releases at most two bodies
a frame instead of eight every five seconds
(`ZombieMaxReleasesPerFrame`) — the same bounded-work argument above, applied
more often in smaller bites rather than widened into a bigger one — AND drains
that two-per-frame release between sweeps as well
(`CorpseCleanupService.DrainBetweenSweeps`, called from `Tick`'s per-frame
early return whenever the active mode is Zombie), so a backlog does not have
to wait for the next sweep at all. Measured on SPT-GPU-2 on 2026-10-07 (build
`43cc7c3`): a 36-body watchdog clear-out was fully released 5.1 s after the
watchdog and a 47-body one after 4.6 s, with the 2 s damage-source quiet window
— not the two-per-frame cap — as the limiting factor (up to 24 releases inside
one second); see GPU-ZOMBIE-PROTOTYPE-RESULTS-20261006.md, "v3-Messraids". The
reassert/sweep-count invariant below is
unaffected: `DrainBetweenSweeps` never runs `ReassertTrimmed`, never trims, and
never increments the sweep counter, so a body hidden in the CURRENT sweep is
exactly as unreleasable through the drain as through the sweep's own release
pass. See ZOMBIE-MODE-REVIEW.md, "v3 (2026-10-07): faster corpse release in
Zombie mode", for the full decompile evidence and the measured-raid context
this responds to.

**Review pass (2026-10-07, F1-PERF).** The two-per-frame cap bounds how many
bodies a drain frame can RELEASE, but every one of the up to ~59 drain frames
a second still used to pay the full `MayRelease` gauntlet — including
`CorpseAggressors.IsPendingAggressor` and `DamageSourcesQuiet`'s own reads —
for every Trimmed entry that was sweep-eligible but still inside the 2 s
`DamageSourceQuietSeconds` window, which is every entry in exactly the
~46-56-body backlog this feature targets, for as long as that window stays
open. `CorpseCleanupService.QuietWindowKnownOpen` is a cheap pre-check in
`ReleaseTrimmed`'s own loop, ahead of `MayRelease`, that skips the gauntlet
once the quiet timer it would consult is already known and has not elapsed —
sound because that timer can only grow more quiet, never less, while
unchanged, so the skipped call would have answered `false` regardless. It
never skips for an own corpse while `TrimState.LeftRegistry` is still
undecided, so the one real reading that decides it always runs.

## Behaviour that does not change

Normal raids are untouched: `Tick` still returns on `ArenaSession.IsActive`
before anything else, and the release is a phase inside it. The profile is not
read or written. `MaxCorpses = 0` still means "keep all" and skips the sweep
entirely, so it also disables the release. `ReleaseTrimmedCorpses` (Performance,
default on) turns the release off on its own and leaves the old hide-only
behaviour exactly as it was. A failed trim is never released — `Completed` is
the same flag the map-marker suppression requires. A release that throws is
counted, reported once per raid and never retried; the trim record is
deliberately kept so the reassert pass still stops the body reappearing.

**Zombie mode (2026-10-07).** Every mode except Zombie keeps exactly this
behaviour, unchanged: the sweep cadence stays five seconds, the release cap
stays eight per sweep, and nothing ever drains between sweeps — `Tick`'s
per-frame early return gains one ordinal string comparison
(`ArenaSession.ActiveMode` against `ArenaModeCatalog.ZombieId`) and takes the
exact same path as before whenever that comparison is false. Within Zombie
mode, every existing `MayRelease` hold (damage sources, own-corpse, loot
window, pending aggressor, map marker, the ragdoll-settled gate) applies to a
between-sweep drain frame exactly as it does to the sweep's own release pass,
because both call the same `ReleaseTrimmed`/`MayRelease` path — the drain adds
no new way for a body to become releasable, only a way to catch up on a
backlog of bodies that already are. The ragdoll-settled gate in particular is
completely untouched either way: a body still visibly falls before any of
this touches it.

## Verification and limits

`pwsh -NoProfile -File tools/DynamicMapCorpseChecks/Verify.ps1` runs everything:
the installed-contract check, 61 marker assertions with eight compiled controls,
then `tools/CorpseCleanupChecks/Verify-Regression.ps1` with 309 assertions and
35 source mutants (as of 2026-10-07; includes the Zombie cadence/drain and its
own ten mutants, see "Bounded work per sweep" above and
ZOMBIE-MODE-REVIEW.md). `CorpseDamageSources.cs` is linked as production
source too, against fakes of the three registries it reads, so its own rules are
mutated rather than only its call site. The release is covered by its own block
plus these controls, each rejected at its named assertion:

- `pooled-instead-of-destroyed` — drops `IsStub`, so the body is pushed back
  into the shared player pool.
- `released-in-the-hiding-sweep` — drops the sweep gate, and the quiet period's
  threshold with it: since the quiet period takes its first reading at the
  trim, it keeps a body out of the sweep that hid it on its own as well.
- `retries-a-failed-release` — drops `ReleaseFailed`.
- `releases-own-and-teammate-bodies` — drops both the recorded flags and the
  re-read, which protect those bodies twice on purpose.
- `unbounded-release-burst` — drops the per-sweep cap.
- `clears-the-record-before-the-ai-knowledge` — moves step 3 ahead of step 1,
  which is the ordering trap above.
- `destroy-behind-a-held-skull` — makes `HoldsMarker` always answer no.
- `leaves-the-trigger-coroutine-running` — drops the trigger-zone retirement.
- `releases-a-pending-aggressor` — drops the pending-aggressor guard.
- `releases-a-named-damage-source` — drops the damage-source guard's call.
- `forgets-a-triggered-tripwire` — counts only an ARMED tripwire, which is
  what reading `TripwireManager` alone would amount to and exactly the gap the
  measured raids fell into: rejected at the `Active` state.
- `releases-inside-the-hit-delay` — drops the quiet period.
- `trusts-an-unreadable-world` — turns the guard's fail-closed catch into
  "nothing names it".
- `holds-every-flare` — drops the flare branch, so a flare falls to the
  fail-closed default and holds every body while it flies: rejected by another
  shooter's flare.
- `holds-behind-a-smoke` — drops the reach test, so a smoke that stays in the
  registry after going off holds its thrower for good.

The damage-source block walks the measured sequence (a planter's tripwire
through `Wait`, `Active`, `Exploding` and an unknown state, each held and each
released only after the quiet period), the sources that name nobody (`Exploded`,
`Inert`, `None`, another planter's tripwire, another shooter's flare, a smoke —
released on the ordinary schedule, which is also the proof that the first
reading is taken at the trim), a tripwire in the static list, a registered
grenade, a flare, an unknown throwable, a shot in each calculator, the quiet
period against a sweep a round-start clear pulled forward, the hold and
let-go log lines, and five unreadable worlds.

These are contract fakes, not Unity. They model loot removal, the pool switch,
the AI retirement and its re-added dead-body record as separate observable
steps; they do not execute `Player.Dispose`, `PlayerBody.Dispose`,
`DestroyImmediate` or Harmony. The double `Player.Dispose` (ours, then the one
`Player.OnDestroy` runs) is argued from the native sequence rather than tested,
as is the weapon model's pool return. Nothing here proves a frame-time or memory
improvement. That is the measuring raid.

## Measured result (2026-09-17)

Labyrinth Deathmatch FFA, give up every ~30 s, `python
..\Redeploy\tools\pipeline-timing\analyze.py <log>`. The before column is
the control raid of the same evening (Core `1.0.4+9aba783`, KillcamReplay
0.9.10, ArenaMode `630b098`, 35 respawns); the after column is 37 respawns over
24:25 on `2c46c84`. Only ArenaMode differs.

| Raid minute | ArenaMode `pipeline` | `rewire.camera` | rest of frame |
|---|---|---|---|
| 3 | 0.38 → **0.36 s** | 111 → 104 ms | 225 → 213 ms |
| 11 | 0.51 → **0.48 s** | 168 → 141 ms | 286 → 284 ms |
| 20 | 0.73 → **0.58 s** | 239 → 202 ms | 417 → 333 ms |
| slope per raid minute | 20.6 → **13.2 ms** | 8.8 → 5.8 ms | 11.1 → 7.2 ms |

A third of the remaining growth, then, not all of it — and the two posts that
carry it fell by a third each. Against the original 2026-09-17 measuring raid,
before Core 1.0.3/1.0.4 removed their own three sweeps, minute 20 went from
1.58 s to 0.58 s.

The body population is bounded, which is what this change set out to do. The
sweep line's own counters, none of which searches the scene: 407 player bodies
created, 0 pooled, 344 released at 24 min, so about 63 player objects left in
the scene, against 415 at 23 min in the raid that measured the problem. A later
raid read 182 created and 147 released at 14 min, about 35 left, against 232 at
14 min. (The "active LocalPlayer objects N" probe those 415 and 232 came from no
longer exists — Redeploy.Core 1.0.4 (`9aba783`) removed it because the count
itself cost up to 303 ms — so the after figures are
created - pooled - released, the same quantity as arithmetic.)

What is left of the growth is now dominated by the bodies this change
deliberately does not take away. The 24-minute raid trimmed 379 bodies and
released 344; the 35 that stayed are the player's own, one per death, and the
player died 36 times. An ordinary Arena raid does not die once every 40 seconds,
so this is close to the worst case for the residue — but it does mean the curve
is not flat, and the own-corpse exemption is the reason.

The heap is unchanged and untouched by this: 1.1 GB to 11.4 GB over the raid
with `collections` never moving, the same as before. That remains a separate
open question, and one that no longer has a corpse explanation.

---

# Own corpses release too (2026-09-18)

## What changed

Until this change, `MayRelease` refused every own corpse unconditionally —
three separate ways at once: `state.Own` in the fast-path disqualifier
(`:607` in the pre-change source), the own branch of the profile re-read
(`:625-629`), and `player.IsYourPlayer` in the identity check (`:644`), which
blocked every own body regardless of the other two. The trim side already
distinguished the newest own corpse from an older one (`KeepOwnCorpses = 1`,
the `protectedOwn` set in `Tick`) — only the RELEASE side treated "own" as one
undifferentiated, permanently-protected group.

The user's decision (2026-09-18): release every own corpse except the newest,
exactly like any other corpse. The newest keeps the old hide-only behaviour
(Redeploy respawns near it, the profile protection reads it); everything older
is now eligible, subject to every other check `MayRelease` already applies to
a foreign body — settled ragdoll, no open interaction, not a pending
aggressor, quiet of any damage source, no held map marker.

`IsNewestOwnCorpse` (`CorpseCleanupService.cs`) answers "is this still the
newest" with a live query over `world.LootList` rather than the cached
`TrimState.Own` flag, because a trim-time snapshot cannot answer a question
about the CURRENT world — a round-start clear can trim the very corpse that
is about to stay the sole/newest one, and a later own death can make an
already-trimmed corpse stop being newest without retrimming it. `world.LootList`
is append-ordered, so the last matching entry is the newest; this is
re-derived on every call, never cached, which is what makes it correct across
clears and respawns without extra bookkeeping.

`state.Own` did not go away: it is still recorded at trim time (while PFT's
answer and the profile match are both still reliable) and is still read —
just no longer as a release gate. It backs the `own` tag in
`DescribeCorpseState`, the own-release counts in the sweep line, and
`ReleaseTrimmed`'s own-release tally, all of which stay correct because a
corpse's `PlayerProfileID` never changes after `Corpse.CreateCorpse` and the
local player keeps one profile id across every respawn.

## The registry argument (the damage-source refinement)

Releasing an older own corpse reopens the exact hazard `CorpseDamageSources`
exists for: `CorpseDamageSources` matches a tripwire, a grenade or a shot by
PROFILE ID, never by corpse reference (finding 1/2 of the prior critic
review, both re-confirmed here). Before this change the local player's
bodies were never released, so that never mattered — the comment at
`CorpseDamageSources.cs:84-87` said so outright, and it was true. Now that an
older own body can be released while the live player, sharing that same
profile id, is still fighting, `DamageSourcesQuiet`'s ordinary reading cannot
tell "this old corpse's own trailing tripwire/grenade/shot" apart from "the
live player is still shooting" — sustained combat could hold every hidden own
corpse open at once, for as long as it continues, which is precisely the
fast-continuous-combat scenario this service targets. This cannot cause a
wrongful EARLY release (the guard still only ever holds a body longer, never
releases one early), but it could erase the entire point of releasing own
corpses at all during the raids that produce the most of them.

The fix is `CorpseDamageSources.StillNamedInRegistry`, and it rests on one
fact about the game verified directly against `../_decompiled/` for this
change:

- `GameWorld.RegisterPlayer` (`_decompiled/EFT/GameWorld.cs:2012-2083`)
  OVERWRITES `_allPlayersEverExisted[profileId]`,
  `allAlivePlayersByID[profileId]`, `AllAlivePlayerBridges[profileId]` and
  `AllPlayerBridgesEverExisted[profileId]` with whichever body registers
  under that profile id — all four tables, every time, unconditionally.
- `GetEverExistedPlayerByID` and `GetEverExistedBridgeByProfileID`
  (`GameWorld.cs:889-896,967-977`) read only the CURRENT entry in those
  tables — nothing keeps history.
- Every resolution `CorpseDamageSources` guards against goes through one of
  these two lookups: a tripwire's explosion resolves its planter through
  `Shot.Create` → `Shot` → the shooter bridge (the "Settled" section above),
  and a flare resolves its shooter through `GetEverExistedBridgeByProfileID`
  directly (`EFT/FlareCartridge.cs:176-190`) — both LAZILY, at the moment
  they land. **Correction (2026-09-18 review pass, major finding):** a
  directly-fired shot does NOT resolve the same way. `Shot.Player` is set
  EAGERLY, once, at the moment of firing — `Player.InitiateShot` →
  `BallisticsCalculator.Shoot`/`CreateShot` → `Shot.method_1`
  (`EFT/Player.cs:14322-14329`, `EFT.Ballistics/Shot.cs:429-460`) — and never
  reassigned before `PrepareToPool` clears it. So once a NEWER own body has
  registered under the profile id, neither a tripwire's explosion nor a
  flare's impact can return the OLDER corpse any more — only the live
  player — but a bullet the older corpse fired while it was still alive can
  still be travelling, with its frozen `Player` bridge still pointing at
  exactly that corpse, regardless of what the registry says by the time it
  lands. `CorpseDamageSources.ShotStillInFlightFromBody` closes that gap: a
  REFERENCE check (not a profile-id one, which would also catch the live
  player's own current shots) against this exact body's still-in-flight
  shots, consulted before the registry bypass returns unconditionally. See
  `DamageSourcesQuiet`'s own doc for how the two combine. The window this
  actually closes is narrow under the current constants — `MayRelease` only
  reaches an own corpse's `DamageSourcesQuiet` call once a LATER own death
  already produced a newer corpse, which needs an intervening respawn,
  several seconds at minimum against any bullet's flight time — but nothing
  in the architecture guaranteed that margin, so the reference check no
  longer relies on it. Releasing the older corpse once BOTH checks agree
  cannot produce the dangling-reference crash `CorpseDamageSources` was
  built to prevent, because nothing can be resolved TO that corpse's
  `Player` component any more in the first place.
- Verified that every own respawn actually goes through `RegisterPlayer`
  before relying on this: `Redeploy.Core/Engine/RespawnEngine.cs` creates the
  replacement via `game.vmethod_3(...)` (`RunRespawn`, `"create"` timing
  mark), which decompiles to `BaseLocalGame.vmethod_3` →
  `LocalPlayer.Create(...)` → the shared `Player.Init` path, whose last
  quarter calls `GameWorld.RegisterPlayer(this)` at `EFT/Player.cs:28272`.
  Confirmed both for a death respawn and for Arena's own live-kit swap
  (`ArenaMode.Client/Respawn/LiveKitRespawn.cs`, `SwapRoutine`), which drives
  the exact same `RespawnEngine.RunRespawn` — it is not a separate path. The
  one place `LiveKitRespawn` re-registers a body by hand
  (`RecoverOldBody`, `world.RegisterPlayer(oldBody)`) is a failure-recovery
  path that re-arms the OLD body as the still-alive local player — that body
  never becomes a corpse in that branch, so it never reaches `MayRelease` at
  all, and does not weaken the argument above.

`StillNamedInRegistry(body, profileId, world)` compares `body` by reference
against `GetEverExistedPlayerByID(profileId)` and
`GetEverExistedBridgeByProfileID(profileId)?.iPlayer`. If either still names
this exact `Player`, the ordinary `DamageSourcesQuiet` hold applies
unchanged — ordinary quiet-period bookkeeping, ordinary hold/release log
lines. If neither does, the hold is skipped outright for this corpse: it
cannot be what a profile-id source resolves to, so there is nothing left to
wait for. Fails CLOSED like every other guard in this file: no body, no
profile id, no world, or an exception reading the registry, all answer
"still named". The result is cached on `TrimState.LeftRegistry` once it
comes back `true` (not-named), because the answer is monotonic — a corpse
cannot re-register, so it can never become "still named" again — but a
`false` (still named, including "unreadable") is never cached, so a
transient read failure is retried every sweep rather than latched.

Foreign and PitFireTeam bodies never take this branch: `DamageSourcesQuiet`'s
new own-corpse block is gated on `isOwnCorpse`, which `MayRelease` only ever
passes as true for a corpse whose profile id matches the CURRENT
`mainPlayer.ProfileId` — a foreign or PFT body's `DamageSourcesQuiet` call is
bit-identical to before.

## Diagnostics

The sweep's own summary line (`Report`, logged only on a sweep that did
work) now carries an own/foreign split: `released N (M own); T trimmed and R
released in total (O own)`, plus a live snapshot of what is still held —
`, K own held as newest` and, only when nonzero, `+ J own held for another
reason` (`DescribeOwnHeld`, read fresh from `Trimmed` every time, not a
counter). The per-body hold/release log lines `DamageSourcesQuiet` already
had are unchanged for the case the registry still names a body; a new one
("...left the profile-id registry...releasing regardless of any source...")
covers the bypass, so a GPU raid log can show both paths being exercised
rather than assumed.

## Offline verification

`tools/CorpseCleanupChecks/Program.cs` gained: two own corpses (older
released, newest kept, across several sweeps); a round-start clear with a
single own corpse (already covered by the existing `"own"` case in the
"never released" loop — annotated to explain why it still passes: with only
one own corpse in the world it is trivially the newest); an own corpse the
registry still names staying held behind a live damage source, and a sibling
whose registry entry has moved on releasing despite the same kind of source;
foreign-corpse behaviour re-run unmodified (every existing damage-source,
aggressor, interaction and quiet-period case) as the proof nothing there
changed.

`tools/CorpseCleanupChecks/Verify-Regression.ps1` replaced the old
`releases-own-and-teammate-bodies` mutant, which no longer matches the
source it targeted, with mutants that match the new structure precisely:

- `releases-a-teammate-body` — drops `state.Protected` from the fast path and
  defeats the PFT re-read branch: a PFT body is released. (The own half of
  the old combined mutant is superseded by the next one, which targets it
  specifically rather than as a side effect of gutting both gates at once.)
- `releases-the-newest-own-body` — removes the newest-check from the
  own-corpse branch of the profile re-read: even the sole/newest own corpse
  is released.
- `ignores-the-registry-for-own-corpses` — drops the registry condition from
  `DamageSourcesQuiet`'s own-corpse bypass entirely: an own corpse the
  registry STILL names is released anyway, defeating the test that proves
  the ordinary hold survives for it.
- `inverts-the-registry-condition` — flips the bypass condition, so it
  triggers when the registry still names the body and falls through to the
  ordinary hold when it does not: both directions of the new two-case test
  fail.

Each is rejected at the assertion the change above exists to keep true.

## What this does not claim

Not run in this pass, same as the prior design's own risk list: a GPU raid
exercising the own-corpse release path end to end (no GPU in this
environment). `KillcamReplay`'s own replay-puppet path during rapid own
deaths remains an open risk, structurally unaffected (it reads the newest
own corpse, which stays exempt) but not measured against this exact timing.

## Review pass, own-corpse release (2026-09-18)

Adversarial review of "Own corpses release too" (this document, above) found
one major finding that held and fixed it; the rest were doc/tooling defects
or theoretical-but-currently-inert observations, fixed where cheap, rejected
with evidence otherwise.

**Fixed:**

- **Major — the registry bypass was unsound for a directly-fired shot.**
  `StillNamedInRegistry` correctly argues a tripwire's explosion and a
  flare's impact both resolve their source LAZILY, at the moment they land,
  so once the registry has moved on neither can reach an older own body. A
  shot does not resolve that way: `Shot.Player` is frozen EAGERLY, at the
  moment of firing (verified against `../_decompiled/`), so a bullet an
  older own body fired while still alive can still be travelling, still
  pointing at exactly that body, after a newer own body has registered.
  `CorpseDamageSources.ShotStillInFlightFromBody` closes this with a
  REFERENCE check (not profile id, which the live player's own current
  shots share) against the exact body, consulted before the registry bypass
  returns unconditionally. See `DamageSourcesQuiet`'s doc and the
  corrected paragraph above. The window this closes is narrow under the
  current cadence/respawn constants (a bullet's flight time is far shorter
  than the minimum time before an own corpse can even reach this branch),
  but the architecture never guaranteed that margin, so the check no longer
  relies on it alone. Covered by two new `Program.cs` cases (a shot from
  this exact body holds it past LeftRegistry; a same-profile-id shot from a
  *different* body does not) and a new `Verify-Regression.ps1` mutant,
  `ignores-an-own-bodys-own-shot-in-flight`.
- **Minor — a stale comment in `CorpseReleaseLedger.Describe`** still
  asserted "an id can only be in here once", contradicting the class doc a
  few lines above (updated in the same diff this comment was not).
- **Minor — AGENTS.md's GPU-acceptance sentence** sat directly after the
  own-corpse-release paragraph with nothing marking the boundary, reading as
  if the GPU raid (06:34-06:48) covered a path introduced by commits later
  that same evening (08:27+). Reworded and cross-referenced to "What this
  does not claim" above.
- **Minor — `IsNewestOwnCorpse` re-scanned `world.LootList` from scratch on
  every call**, up to twice per own corpse per sweep (`MayRelease` and
  `DescribeOwnHeld`). Nothing in `Tick()` adds to `world.LootList`, so the
  answer cannot change within one sweep; it is now cached per `_sweep`
  number instead, one scan per sweep regardless of how many own corpses are
  waiting.
- **Minor — `Verify-Regression.ps1`'s mutant runs raced on a shared build
  output path.** Back-to-back `dotnet run` calls against the same apphost
  file occasionally hit `MSB3026` (Windows holding a brief lock on a
  just-written exe), and the script's one retry did not always win, letting
  a STALE exe from an earlier mutant run in the current mutant's place —
  reproduced three times, a different mutant failing each time, purely from
  timing. Each mutant now builds to its own `-p:BaseOutputPath`, so no two
  runs can ever contend for the same file.

**Rejected:**

- **Minor — the "left the profile-id registry...releasing regardless" log
  line was claimed dead code**, unreachable because `LeftRegistry` is
  already true by the FIRST time an own corpse's `DamageSourcesQuiet` branch
  runs (true in the code as it stood). Not fixed as a separate item because
  the shot-in-flight fix above changes the reachability directly: `HeldAsSource`
  can now be set by the new in-flight hold and later cleared when it lets
  go, which is exactly the transition that line was written to report. A
  new dedicated `LeftRegistryAnnounced` flag was added alongside it so the
  registry-departure itself is always logged once per corpse, independent of
  whether a shot happens to be in flight at that moment — the diagnostic
  purpose the finding's own suggested fix asked for.
- **Minor — the mutation-harness flakiness finding's root-cause claim** was
  independently reproduced (see Fixed, above) and is not a rejection; noted
  here only so the two related findings are not confused with each other.

## GPU raid, own-corpse release (2026-09-18, 10:22-10:46 Labyrinth on `9218bb7`)

Deathmatch FFA, profile ArenaGPU20260908, giving up every 40 s: 32 respawns in
23 minutes, client `D8311C49`. Compared with raid B of the same morning (same mod
set on `d265353`, 16 minutes, 22 respawns):

| | B (`d265353`) | this raid |
|---|---|---|
| Own corpses released | 0 | 31 of 32 deaths |
| Corpses in the world | 35 at 16 min (max 39) | 12-17 all raid |
| Player bodies created / released | - | 463 / 434 |
| Arena `pipeline` at minute 3 / 11 / 15 | 0.38 / 0.47 / 0.57 s | 0.37 / 0.47 / 0.55 s |
| Growth up to minute 16 | 12.8 ms per minute | 14.7 ms per minute |

Every own release logged "left the profile-id registry"; no damage-source
report, no NullReferenceException, and the warning set is the one raid B had
(stale PerfectCulling sampler per respawn, missing scripts at raid start, SAIN's
"Player Null from ID" for the dead boss's tripwires before any own release).

**What this falsifies.** The corpse population is now flat, and the respawn
pipeline grows exactly as before. The own corpses were not what carried the
remaining growth; the claim above ("dominated by the bodies this change
deliberately keeps") is wrong. Core's timing lines put the growth where it was:
`rewire.camera` +6.5 ms per minute (R² 0.93) and the rest of the frame +8.6 ms
per minute, neither correlated with the heap. What does rise with every respawn
or every created body, in Core's "before" counters: `cullingJobs` +1 per respawn
(2 to 32; the replaced camera's entry in `CullingManager.CullingJob.JobParameters`
is never removed, though a destroyed camera is skipped when jobs are scheduled),
`traderHandlers` +1 and `skillHandlers` +6 per respawn (handlers on the one
shared Profile/SkillManager), `flareSubscribers` +1 per created player body (438
at the end, against 463 bodies created). Which of these, if any, costs the
camera creation its time is not measured yet.
