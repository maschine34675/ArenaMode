# Surviving player body: bleeding registration cleanup

## Evidence and attribution

Read-only snapshot: `D:\SPT41\BepInEx\LogOutput.log`, 26,606,630 bytes,
348,946 lines, last written 2026-09-06 20:21:25 local time. SHA-256:
`99D676480CC4AA0C0D2A9F52AC9356D97A6E4F06D416F976295A821D73792391`.
The log was not modified or copied into the repository.

There are 30,164 error headers. Their complete families are:

| Count | Signature / interval | Assessment |
|---:|---|---|
| 30,156 | `Transform.get_position -> BifacialTransform.get_position -> Player.get_Position -> EffectsCommutator.UpdatePlayersBleedings`; 20:15:29.003–20:19:07.928 | Persistent reference to a destroyed player transform in the native blood-decal list. The Arena living-body replacement path has a concrete missing cleanup that produces this failure. |
| 4 | `BotWeaponSelector.OnWeaponTaken` asynchronous hands callback; 19:37:28.590, 20:03:45.054, 20:06:37.390, 20:11:10.258 | Separate bot/weapon lifecycle family. Native callback dereferences the owner/weapon manager and delayed task manager. One occurrence coincides with raid teardown. The method has PFT prefix/finalizer patches in the log. Arena spawning, deaths or teardown could expose late callbacks indirectly, but neither the failing actor nor the null member is identified. No evidence supports assigning all four to PFT or Arena. |
| 1 | `BotFirstAid.CG_ApplyToSelf.method_1 -> MedsInHandsOperation.Finish -> OnEffectRemoved`; 19:15:10.613 | Late medical completion callback dereferences bot owner/weapon manager/settings. `OnEffectRemoved` has a ContinuousHealing prefix in the recorded patch list. It occurred 202 ms after a corpse trim, which establishes timing only, not actor identity or causality. |
| 1 | `Shot.method_1 -> Shot.Create -> BallisticsCalculator.CreateShot -> ExplosionSharedMethods.CG_SpawnRandomFragments`; 20:06:43.005 | Grenade fragment coroutine, 4.178 seconds after the preceding Arena session ended. Native shot setup still accesses global world/configuration/trajectory state. Consistent with a coroutine outliving raid teardown; the stack provides no exact null member. Arena tracer code is downstream rendering, not this creation method. |
| 2 | Inactive `MenuScreen` could not start a coroutine; 19:10:42.043 and 20:19:04.616 | UI transition errors without a caller stack. No reliable attribution. |

The bleeding family accounts for 99.97% of the errors and repeats for 218.925
seconds (about 138 errors/second). First header is line 17063. In the active
**solo Tactical** Labyrinth match, round 5 was won at 20:15:18.926; the surviving
PMC was replaced for round 6 at 20:15:26.620–20:15:27.050. Five opponents arrived
20:15:27.477–20:15:27.510; the bleeding errors start 1.953 seconds after the
completed local replacement. The last corpse trim was 20:15:23.678, 5.325
seconds before the errors. The errors continue through a subsequent death and
round 7, ending 209 ms after Arena session end.

The historical log does not identify the stale IPlayer instance or record its
heavy-bleed state. It cannot prove that this exact old local body caused every
observed error. The matching lifecycle defect is nevertheless demonstrable from
the actual native algorithm and the two Arena destruction call sites; it does
not require guessing a bot identity or suppressing an exception.

## Native contract and repair

Local decompiled SPT 4.1 sources:

- `../_decompiled/Systems.Effects/EffectsCommutator.cs:157–206`: heavy-bleeding
  membership is a list of `KeyValuePair<IPlayer,float>`. Start deduplicates the
  exact interface reference; Stop removes that reference only. Update reads
  `IPlayer.Position` without a destroyed-object check. A single invalid entry
  also prevents later entries in that frame from receiving blood decals.
- `../_decompiled/EFT/Player.cs:26181–26217`: heavy-bleed started registers;
  heavy-bleed residual and native visual death unregister. The three visual
  subscriptions are independent of other health/medical handlers.
- `Player.OnDestroy` (28429), `Player.Dispose` (30917), and
  `GameWorld.UnregisterPlayer` (2060) do not unregister the blood-decal entry.
- `LiveKitRespawn` replaces a **living** player, then destroys the obsolete
  body without a death event, both after success and after replacement-body
  recovery. Normal death respawn keeps the corpse and follows native death
  cleanup. `CorpseCleanupService` trims renderers/colliders/gear and disables
  corpse ticking; it does not destroy the player skeleton or its Transform.

`ArenaRetiredBodyEffects.TryDestroy` now owns those two terminal destruction
steps. It requires an active, started, exactly bound Arena game/world, a new
current LocalPlayer consistently installed in both game and world, the same
nonempty profile ID but a different object reference, and a living old body
without a corpse. Profile equality only proves that a local replacement
occurred; native removal always receives the old object reference.

The helper removes exactly the three native visual delegates, invokes native
`StopBleedingForPlayer(oldBody)`, and only then requests destruction. Removing
the delegates before deferred Unity destruction prevents the old health
controller from registering that body again. Independent health subscribers,
the new body (even with the same profile), other players and bots, native heavy
bleeding and ordinary raids remain unchanged. There is no global Update hook,
list clear, exception finalizer or health/profile mutation.

The existing callers retain their coroutine epoch/stand-down checks and their
warning/error handling. An incomplete recovery with no consistent MainPlayer
does **not** destroy the old body: the successful native respawn pipeline sets
MainPlayer before returning, while a failed partial rewire can legitimately
have not reached that point. Keeping the body in that case is safer than
invalidating the world's still-current reference.

On a cleanup exception, destruction is not requested. Only successfully removed
visual delegates are restored, and the exception propagates to the existing
caller warning. If rollback itself fails, an AggregateException preserves both
errors. An existing Effects singleton without its native commutator is an
explicit cleanup failure, so it cannot silently destroy an uncleaned body;
an absent Effects singleton remains valid because no current native registry is
available. No blind StartBleeding is used for rollback: the retired body may never
have had a blood-decal entry. A Destroy failure after successful blood cleanup
can therefore leave a retained retired body without decals until a genuine
subsequent heavy-bleed event; this is preferable to inventing a registration.
No attempt is made to patch the eight unrelated errors from the table.

## Verification and remaining playtest

`pwsh -NoProfile -File tools/BleedingCleanupChecks/Verify.ps1` checks installed
native IL and compiles the production helper with the actual locally extracted
native Start/Stop/Update and Player visual event methods. It first reproduces
the old destroyed-Transform NRE, then covers exact-reference cleanup, same-ID
replacement preservation, remaining blood decals, re-registration during
deferred Destroy, independent subscribers, death/residual behavior, missing
effects, all negative scope/lifetime gates and partial-step exception rollback.
Deliberate wrong-identity, omitted visual detach, stale-game, dead-body and
rollback mutants must fail their intended behavioral assertion.

Root completed G1 successfully: installed native contracts, the reproduced old
NRE using the native methods, the current production-linked baseline, all five
mutants rejected at their intended behavioral assertions, and the final baseline
recheck. Independent adversarial review found no open P0–P2 findings. The gate
records execution evidence. This worker did not run tests, build, deploy or
commit.

The GPU playtest remains open; offline checks are not a raid. For the next game
test: win a solo Tactical round while heavily bleeding, continue through several
live round replacements and
a death respawn, and confirm the log stays free of this bleeding-update storm.
Also verify blood decals still appear for a newly injured current player/bots
and that normal-raid behavior is unchanged.
