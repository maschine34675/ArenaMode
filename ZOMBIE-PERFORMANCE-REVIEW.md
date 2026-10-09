# Bounded Zombie AI work reduction

2026-10-09. This change removes selected useless work; it does **not** establish
a higher supported zombie population or an FPS improvement. Client/server alive
limits, the wave director and simulation cadence are unchanged. See
`ZOMBIE-KIT-WARMUP-REVIEW.md` for the separate catalogue-preload change.

## Scope and native basis

`ZombiePerformancePatches.EnablePatch()` registers precisely two prefixes:

1. `BotHearingSensor.OnSoundPlayed(IPlayer, Vector3, float, AISoundType)`.
   The native outer method (`../_decompiled/BotHearingSensor.cs:49-76`) first
   checks mute, takes an immediate path when `Memory.GoalEnemy` exists, or
   schedules `AITaskManager.RegisterDelayedTask` when the selected hearing delay
   is positive. The inner method (79-119) only then discards non-enemy steps.
   The installed IL contains the closure construction, one delayed-task call
   and two direct inner calls. A prefix can avoid creating the irrelevant task
   and executing its future callback.
2. `BotFriendChecker.ManualUpdate()`.
   Its first check uses the strict `_nextTimeCheck < Time.time`, then advances
   the retry by one second and calls `BotsConnections.GetClosest`. Successful
   candidates can lead to a linecast and a peaceful-action choice
   (`../_decompiled/BotFriendChecker.cs:50-114`). Infected combat does not need
   these partner searches. This patch leaves all `BotsConnections.AddPerson`,
   pair objects, periodic pair updates, removal and follower events untouched.

The native bot's ordinary `UpdateManual` calls the social checker along with
other components (`../_decompiled/EFT/BotOwner.cs:1043`). We suppress only this
checker, not the update loop or any other component. The existing gesture
suppression did not avoid this preceding search.

## Admission and exclusions

Both paths require an active confirmed Zombie Arena and the exact recorded
`LocalGame`/`GameWorld`, with game status `Started`. A listener must be one of
the four generic infected roles, Active, alive, a real AI rather than local/main
player, and the exact current body returned by the world's alive-ID dictionary.
Its body-to-owner and owner-to-sensor/checker references must agree. No per-bot
cache can outlive a replacement, and a matching profile ID alone is insufficient.
`infectedTagilla`, ordinary PMCs/scavs, PFT companions and other modes are excluded.

Hearing is narrower still:

- Only the native `AISoundType.step` category. That category also covers native
  door sounds; friendly versions already have the same discarded inner result.
- The listener has **no current combat target**, and the exact peace/search
  delay chosen by native code is positive and finite. The already-cheap inline
  combat path remains vanilla, as do muted sources and zero/unknown delays.
- The source is the actual `Player` emitted by native `MovementContext`, with
  its own exact current, live, Active generic-infected owner. Foreign `IPlayer`
  adapters are not converted merely because their ID matches.
- Both current groups belong to this exact game, have a generic-infected initial
  role, contain their respective owners and have initialized relationship data.
- Friendship is positively established **in both directions** by native group
  membership, an explicit ally, or `IsSuitable(role)`. Merely not being in the
  enemy dictionary is insufficient. Native suitability groups the three ordinary
  civilian/laborant/assault infected; `infectedPmc` is not automatically assumed
  suitable to all other roles. Its explicit memberships/allies can still qualify.
- Current enemy keys, `IsEnemy`'s numeric-ID lookup, neutral entries or a current
  opposing target defeat old membership/suitability/ally data. Relationship
  changes are re-read on every sound; no friendship result is cached.

Shots, silenced shots, null/foreign/human/PFT sources, unknown relationships,
enemy infected and any missing/stale native object continue through the original
method. Exceptions fail open and issue at most one warning per process.

Social admission runs only when the original one-second search is actually due.
On a qualifying skip it writes only the same next retry (`Time.time + 1f`),
avoiding expensive identity checks on every otherwise idle bot frame. Existing
peaceful actions are not cancelled, settings are not changed, and the separate
pair system is not removed. An ineligible checker retains its timer verbatim.

## Diagnostics and performance limits

One activation message per exact world, then at most one aggregate per 60 real
seconds while a qualifying hook runs:

- `delayedStepCandidates`: current infected listener step events on the native
  delayed path, including candidates later retained for an unproven source/pair.
- `friendlyStepsSkipped`: avoided outer hearing calls/tasks for proven pairs.
- `socialSearchesSkipped`: due partner searches avoided, **not** all bot frames.

These are interval counts, not CPU times or FPS gains. A horde already fighting
at the player can legitimately report zero delayed hearing candidates: its
`GoalEnemy` path was deliberately not made more expensive. Compare an approach
phase as well as close combat. Counters reset when the exact world changes and
hold only a weak world reference; there is no new raid-end subscription. A final
short interval may have no log because no future valid hook triggers its flush.

The normal hook path allocates no lists/delegates/strings per sound. The native
alive-player lookup is a dictionary read (`GameWorld.cs:1002`), not a scene scan.
The desktop boundary fixture confirms zero managed allocation over 10,000
warmed repeated hearing checks. That is **not** a Unity/Mono benchmark. Mutual
group/identity checks still cost time; the native comparison must determine
whether avoided scheduling and social searches outweigh that cost in the actual
installation. No AI layer, navigation, vision, movement, animation event, attack,
healing, follower lifecycle or cap is disabled by this patch.

## Offline verification

`tools/ZombiePerformanceChecks` compiles the actual patch with focused boundary
doubles, including Unity-style destroyed-object null behavior, exact world/body
identities, native-ID enemy aliases, current relationship changes, role/mode
exclusions, delay selection, social cadence and aggregate reset. Real prefix
entry points and target signatures are also exercised. It does not copy the
production decision into a second pure policy.

- `dotnet run --project tools/ZombiePerformanceChecks -c Release`: 192 assertions,
  including zero live-registry lookups on combat/no-delay/muted fast paths and a
  two-lookup positive control on qualifying delayed hearing.
- `tools/ZombiePerformanceChecks/Verify.ps1`: 17 compiled faulty production variants
  must compile successfully and fail a runtime assertion, covering no-op hooks,
  widened sound/raid/role guards, stale body/world/hook identity, one-way or
  unknown friendship, both native enemy representations, combat/zero-delay
  bypass, per-frame social retries and cross-raid counter contamination.
- `tools/ZombiePerformanceChecks/Verify-NativeContract.ps1`: checks the installed
  native method/member types, exact Harmony argument names, scheduling/inner
  call sites, social search and live-player dictionary lookup. Verified native
  Assembly-CSharp SHA256:
  `EE25CEE1259777B38ED8B3E7841FDC2DB3C98540B1469FA539B1FF183476E436`.

Native acceptance belongs to the root delivery report: restarted final DLL,
comparable map/time/horde/settings, approach and combat behavior, melee and gun
threats, profile/PFT persistence, ordinary-raid exclusion, exact loaded identities
and an honest distinction between repeatable savings and measurement noise.

## Four implementation passes

1. Implemented exact-body two-hook ownership and non-cached relationship checks.
2. Re-read native scheduling, social cadence, role suitability and installed IL.
   Restricted hearing to actual positive-delay work; retained inline combat.
3. Tested mode/identity/relationship/lifecycle failures, compiled regressions and
   aggregation reset. Kept native social retry to avoid a new per-frame guard cost.
4. Added bounded diagnostic wording and native contract checks; documented real
   source-category scope and measurement limitations rather than promising FPS.
