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
| KillcamReplay | Its `SnapshotRecorder` already retires a track whose player has gone unsampled for two seconds, explicitly so it does not pin a dead `Player`. Its death replay uses the LOCAL player's corpse as the puppet source — and own bodies are never released. |
| AutoIFF | Keyed by profile id (`identifiedBots`); holds no body. |
| Redeploy.Core / profile protection | Read the local player's own corpse (`EquipmentSurgeon`, `NearSpawnPolicy`, `RespawnService.FixOwnCorpseRenders`). Own bodies are **never released**, recorded at trim time and re-read at release against `MainPlayer.ProfileId` — which is the same across every respawn, so it covers every body that was ever the player's. |
| Loot on the corpse | Already stripped and unreachable before release: the body is hidden, dropped from the AI's body records, and bots do not loot corpses in Arena. The secure container and the held weapon are what the trim leaves; `PlayerBody.Dispose` and the hands controller pool their models. Release is refused while the player has that body's loot window open or is interacting with it — re-read at release, not trusted from the trim. |
| `ArenaAreaRuntime.Actors`, `CorpseLightService`, `CorpseWeaponRegistry` | `Actors` already prunes on Unity's null check; the other two are cleared in step 5. |
| `TriggerZone` MoveInside coroutines | Not found by the code review. `CorpseTriggerZones` retires the body's own coroutine before the destroy; see below. Pre-existing rather than caused by the release. |
| `Player.LastAggressor` on a living, wounded player | Not found by the code review either, and the one place where the release genuinely broke something. A body stays hidden while anybody still names it (`CorpseAggressors`); see below. |
| `GameWorld.GetEverExistedPlayerByID` in `Player.ManageAggressor` | Suspected in the one unresolved finding below. Not pruned on purpose: a destroyed `Player` still answers the managed reads its callers make. |

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
| that body's `release=` | `RELEASED`, 3.7 s to 85.4 s earlier |
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

**It is not only grenades.** `damage=` was `GrenadeFragment` six times and
`Bullet` five, once at 1.7 damage — a bleed tick, which carries the original
shooter's `DamageInfo` long after the shot.

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
  inside that delay.

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
widened from `LastAggressor` to the in-flight and deferred cases. Not written
yet, and not to be guessed at: the next step is to establish what held that one
body, which the diagnostic now makes a matter of reading one line.

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

`ReleaseTrimmedCorpses` (Performance, default on) remains the off-ramp:
turning it off restores the old hide-only behaviour exactly.

## Bounded work per sweep

At most eight bodies per sweep. A release walks every bot group and tears down a
player hierarchy, and a round-start clear can hide fifty bodies at once — so
without a cap the slow accumulation would be replaced by a frame spike. Eight
per five seconds is well clear of the production rate: the measuring raid
trimmed 446 bodies in 26 minutes, one every 3.5 seconds.

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

## Verification and limits

`pwsh -NoProfile -File tools/DynamicMapCorpseChecks/Verify.ps1` runs everything:
the installed-contract check, 61 marker assertions with eight compiled controls,
then `tools/CorpseCleanupChecks/Verify-Regression.ps1` with 110 assertions and
twelve source mutants. The release is covered by its own block plus these controls,
each rejected at its named assertion:

- `pooled-instead-of-destroyed` — drops `IsStub`, so the body is pushed back
  into the shared player pool.
- `released-in-the-hiding-sweep` — drops the sweep gate.
- `retries-a-failed-release` — drops `ReleaseFailed`.
- `releases-own-and-teammate-bodies` — drops both the recorded flags and the
  re-read, which protect those bodies twice on purpose.
- `unbounded-release-burst` — drops the per-sweep cap.
- `clears-the-record-before-the-ai-knowledge` — moves step 3 ahead of step 1,
  which is the ordering trap above.
- `destroy-behind-a-held-skull` — makes `HoldsMarker` always answer no.
- `leaves-the-trigger-coroutine-running` — drops the trigger-zone retirement.
- `releases-a-pending-aggressor` — drops the pending-aggressor guard.

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
