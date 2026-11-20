# The audio occluder exception in Zombie raids (2026-11-20)

Follow-up to "Seen on the way" in [GPU-ZOMBIE-PROFILE-POOL-20261120.md](GPU-ZOMBIE-PROFILE-POOL-20261120.md). In run A
of that check (Factory, Zombies, 32 alive, waves 50 +20, no player kills, `Zombie/StuckWaveTimeoutSeconds` 5) the game
threw `Exception: Can't handle not proposed occlusion test : Combined` from
`Audio.SpatialSystem.MultiSourceSpatialAudioOccluder.GetTargetOcclusionType` once per frame, in five bursts, 5259
times in all. This report first read the decompile (`../_decompiled`, EFT 0.16.9.5) and the saved logs (sections 1, 2
and 4); a native check on SPT-GPU-2 later the same day then watched the game's container pool in three Zombie raids
(section 3). Nothing was built or changed in the code. Paths below are relative to `_decompiled`; line numbers are
those of the decompiled files.

## Result in one table

| Question | Answer |
| --- | --- |
| What throws | The main multi-source occluder still lists a one-shot container under its old id after the container went back to the game's pool and was handed out again as a bot's `Combined` container. **Confirmed** by the code and by the shape of the log (no `OnLateUpdate` stack, end two frames after the holder's death). |
| Where the second owner comes from | A pool that holds one container twice. The only path found: the spine container of an **infected** bot that the game's audio source limiter paused and resumed is returned twice at its death. **Observed natively** (section 3): all 6 containers that were seen in both registries lay in two pool slots after their holder died. |
| One death or many | One death of such an infected arms the pool. The exception then needs a one-shot sound and a bot's first audio registration to draw the two copies in one frame. Many deaths in one frame are not required; they multiply both. Natively, 6 of about 1,140 retired infected were armed: a rare first step. |
| Does it accumulate | **Yes, natively.** A pooled-twice container is usually drawn by two new infected, which then share it; both return it at their deaths, so it is pooled twice again, wave after wave, for the rest of the raid. Up to three such containers at once, one of them in three slots. |
| Zombie mode only? | Only bots with the simplified (infected) skeleton enter the limiter, so PMCs and scavs never arm the pool. It is a game bug: a normal raid with infected has the same path. Arena touches no audio container code. |
| What it does while it throws | From the code, not listened to: one-shot sounds that the occluder lists after the stale entry stay silent (mostly other players' shots, shell casings, bullet impacts from another room); occlusion of bots' footsteps and voices is not re-evaluated. |
| Cost | Frame time: no difference beyond noise. Log: about 890 bytes per exception, 85 to 120 per second; 61 % of run A's client log (4.7 of 7.6 MB). |
| What ends it | The death of the bot that holds the container: the game returns that bot's container in the next frame (later only if the bot was speaking). In a played match: when that infected is killed, at the latest when the raid ends. |
| Reproducible on purpose | The precondition, yes: in each of three raids the pool held a container twice after the first armed death. The exception, no: none in 39 retirements (about 1,140 infected), on the installed build without the profile fix, so the third raid's waves filled more slowly than run A's. The stale occluder key itself was seen six times, each time with a second one-shot as the other owner, which does not throw. |
| With player kills at high waves | Same game path; not observed, not excluded. |
| Recommendation | If the owner wants a guard: a prefix on `AudioSourceContainerData.ReturnContainer` that does not pool a container twice (exact Arena raid). It removes the first double return, from which the shared containers that follow all come. Not the watchdog change, not a finalizer. Low priority: audio only, a game bug, never seen in a played match. |

## Evidence

- Client log of run A: `artifacts/zombie-supply-20261120/native/full-default-fixed/host2-bepinex.log` (git-ignored), with
  `keepalive.jsonl` (frame times in 5 s windows). The other runs of that day and the two Zombie logs of the night before
  (`artifacts/opening-rounds-20261119/native/acceptance/zombie-full-wave10-rerun/`, `.../zombie-mid-baseline-72273354/`)
  for the comparisons.
- The decompile, files named in each step.
- The native check (git-ignored): `artifacts/audio-occluder-20261120/`, with `monitor*.jsonl` (one line per poll),
  the scripts that wrote them (`monitor*.mjs`, `probe*.mjs`, `summarize.py`) and `host2-bepinex.log` (the client log
  of the three raids).
- Arena's own code: no reference to `SpatialAudio*`, `AudioSourceContainer*`, `ObservedPlayerAudioSourceLimiter` or
  `ProcessSourceOcclusion` (searched over every `.cs` of the repository). The only audio patch near infected,
  `ZombieVoiceBudget`, prefixes `Player.OnPhraseTold` of living infected and leaves the containers alone. The profile
  patch of the branch touches no audio code.

## 1. The path

### The pieces

- `AudioSourceContainerData` (`Audio.SpatialSystem.AudioSourceContainers/AudioSourceContainerData.cs:21-34`):
  `GetContainer` takes an object from `PoolWithoutAlloc<AudioSourceContainer>`, calls `Clear()` and
  `Initialize(transform, test, _nextID++)`; `ReturnContainer` calls `Clear()` and `PutObject`. The pool lives per raid
  (`SpatialAudioSystem.cs:274`, `EDataLifeTime.Raid`).
- `PoolWithoutAlloc.PutObject` (`PoolWithoutAlloc.cs:49-66`) writes the item into the lowest empty slot and does
  **not** check whether the same object is already in the pool; `GetObject` (`:25-47`) hands out the lowest occupied
  slot. An object returned twice sits in two slots and is handed out twice.
- `AudioSourceContainer.IsValid` (`Audio.SpatialSystem/AudioSourceContainer.cs:75-89`) is true while it is initialised
  and has a source; `Clear()` (`:358-377`) sets it uninitialised and the test type to `None` but leaves the `ID` and
  every event subscription alone.
- The multi-source occluder keeps `_activeContainersByID` keyed by the id the container had **when it was registered**
  (`MultiSourceSpatialAudioOccluder.cs:52, 79`). Each `OnUpdate` (`:107-142`) drops an entry only when its container is
  invalid (`:113-116`), otherwise it calls `GetTargetOcclusionType`, which returns `None` when source and listener share
  an indoor room and throws for any test but the two one-shot ones (`:219-238`, throw at `:231`).

### Arming: an infected's container enters a second registry

1. A bot's first audio source registers its spine container: `Player.CreateNestedSource` (`EFT/Player.cs:27241-27261`)
   calls `SpatialAudioSystem.ProcessSourceOcclusion(this, NestedStepSoundSource, IsAiUseSimpleSkeleton())` for every
   third-person player. That method (`Audio.SpatialSystem/SpatialAudioSystem.cs:308-346`) draws
   `GetContainer(spine, EOcclusionTest.Combined)` (`:324`), stores it in `_registeredPlayerContainers` and
   `_allRegisteredSoundContainers` (`:337, 339`), **not** in `_activeSoundContainers`, and, when `allowedLimiter` is
   true, registers the player with the `ObservedPlayerAudioSourceLimiter` (`:340-343`).
2. `allowedLimiter` is `IsAiUseSimpleSkeleton()` (`EFT/Player.cs:27994-28001`): `UsedSimplifiedSkeleton` for a
   third-person player, set by `Player.Create` (`:28069`) from `LocalPlayer.Create`'s
   `profile.Info.Settings.UseSimpleAnimator` (`EFT/LocalPlayer.cs:52`, the zombie bundle). So only infected enter the
   limiter.
3. The limiter's `WorkCoroutine` (`EFT.Character/ObservedPlayerAudioSourceLimiter.cs:149-173`) visits each registered
   container about every 30 frames and calls `SwitchSoundOcclusionCulling` (`:236-246`): out of hearing range
   `PauseContainerUpdate(id)`, otherwise `ResumeContainerUpdate(id)`.
4. `PauseContainerUpdate` (`SpatialAudioSystem.cs:444-459`) finds the player container through
   `_allRegisteredSoundContainers`, records it as paused and "removes" it from `_activeSoundContainers`, where it never
   was. `ResumeContainerUpdate` (`:461-476`) then **adds** it to `_activeSoundContainers` (`:465`). From the first
   pause-and-resume on, an infected's container is in two registries: `_registeredPlayerContainers` and
   `_activeSoundContainers`.

### Death: the container is returned twice

5. `Player.OnDead` (`EFT/Player.cs:30096-`) raises `OnIPlayerDeadOrUnspawn` (`:30112`) before it plays the death phrase.
   `SpatialAudioSystem.OnPlayerDeadOrUnspawn` (`:478-484`) starts `ClearPlayerOcclusionWithDelay` (`:486-528`), which
   runs at once up to its wait: the wait is the speaker's `TimeLeft` (`EFT/BaseSpeaker.cs:65`, `ReleaseTime -
   Time.time`, zero or less when nothing is playing) or 1 s without a speaker. The limiter unregisters the player in the
   same event (`ObservedPlayerAudioSourceLimiter.cs:138-141`). The administrative retirement is `Kill(Undefined)`
   (`ZombieSpawnService.RetireAllAliveInfected`), the same death.
6. The coroutine resumes after the next frame's `Update` (later only if the bot was speaking), disposes the processor,
   removes the container from `_registeredPlayerContainers` and calls `_containerData.ReturnContainer` (`:525`): first
   return. The container stays in `_activeSoundContainers` and `_allRegisteredSoundContainers`.
7. In the same frame `SpatialAudioSystem.LateUpdate` (`:753-784`) runs `method_26` (`:853-863`), finds the container
   invalid and calls `UnregisterSourceContainer` (`:865-870`): second return. The pool now holds it twice. (If someone
   draws it between steps 6 and 7, it is valid at step 7, stays in `_activeSoundContainers` under its new owner, and
   the second return comes when that owner lets it go.)

### Use: two owners, one stale key

8. Two later `GetContainer` calls hand out the same object. If the first is a one-shot sound through the main
   occluder, `RunOneShotPropagationOcclusionTest` (`:576-588`) registers it there under its id K
   (`MultiSourceSpatialAudioOccluder.SetAudioSourceContainer`, `:35-81`, which also sets the source's volume factor to 0
   and a 20 Hz low-pass until the occlusion is computed, `:47-50`) and adds it to `_activeSoundContainers`. If the
   second is a bot's first registration (step 1), `GetContainer` clears it (the one-shot's source is dropped) and
   initialises it as that bot's `Combined` container with a new id.
9. From the occluder's next `OnUpdate` on, the entry K holds a valid `Combined` container: `GetTargetOcclusionType`
   throws every frame, unless the bot stands in the listener's indoor room (then it returns `None` first).
10. When that bot dies, step 6 returns its container in the next frame, the container is invalid, `OnUpdate` drops K,
    and the bursts end. Because the one-shot had put the container into `_activeSoundContainers`, step 7 returns it a
    second time: each burst ends by arming the pool again.

Other orders of the two owners do not throw but are not harmless either (a one-shot entry that completes later queues
the **current** id of its container, `:200-206`, not its key; a bot's container that was repurposed as a one-shot can
have a bot's persistent source released by `ApplyOcclusionToSource`, `:208-217`). These were not traced further.

### What the log adds

- **Both draws fall into one frame.** All 5259 stacks end in `OnUpdate`; none in `OnLateUpdate`. `OnLateUpdate` skips an
  entry whose `_updatedContainersByID` value is still false (`:156-159`), and only a successful pass of `OnUpdate` sets
  it (`:140`). So K never had a successful `OnUpdate`: the bot's draw came before the occluder's first look at the
  one-shot, i.e. in the same frame (or the one-shot's first pass took the `None` path without a calculator).
- **The end is the holder's death.** After each ending mass retirement exactly two more exceptions are logged
  (08:37:37.371 retirement, last exceptions .380 and .403; 08:38:00.591 → .598, .619; 08:40:06.169 → .179, .196;
  08:40:52.781 → .790, .807). That is the coroutine of step 6 with nothing playing. The first burst ended at
  08:32:00.593 with the retirement of the last 2 infected of wave 5, 11 ms **before** the player's body swap
  (08:32:00.604): the earlier report's "ended at the player's body swap" is a near miss; the holder was one of those
  two. The local player itself never registers a spine container (`CreateNestedSource` only for third person).
- **The spawn after the retirement is not what singles out the five.** A spawn arrived within 50 ms of the retirement
  after 52 of 55 mass retirements in run A, 11 of 12 in run C, and 26 of 30 and 8 of 11 in the two logs of the night
  before; the exception followed 5 of these.
- **Not determined:** which one-shot drew the first copy, which spawn the second, and why 5 of 55. With the pool's
  lowest-slot rule, whether two copies of one object are drawn in the same frame depends on what lies in the slots
  between them, which no log line shows. The pool lives for one raid; every armed death and every burst end adds a
  duplicate, so duplicates may accumulate during a raid. The bursts began 3.1, 8.6, 8.9, 11.0 and 11.8 minutes into
  the 12.7-minute match of run A; none came in runs B and C (about 4 minutes each) nor in the two logs of the night
  before, whose full run filled its waves at about a quarter of run A's rate from wave 5 on (fewer deaths per minute).
  The log alone is consistent with accumulation and does not show it; the native check (section 3) does.

## 2. While it throws

- **The rest of the occluder's pass.** The exception leaves `OnUpdate` at the stale entry, so the entries after it in
  the dictionary's slot order get no update this frame and keep `_updatedContainersByID` false, and `OnLateUpdate`
  skips them. Their sources stay where registration put them: volume factor 0 and a 20 Hz low-pass
  (`BetterSource.cs:304-310, 508-512`), i.e. silent for their whole length. Entries in earlier slots are processed.
  What goes through this occluder: other players' shots (`EFT/Firearms.cs:708`), third-person shell casings
  (`EFT/Player.cs:11328`), bullet impacts of any shooter (`Systems.Effects/Effects.cs:334-342`), other one-shot
  `PlayAtPoint` sounds, interactive objects and tripwires; a sound whose source is in the listener's indoor room is
  never registered and never muted.
- **The rest of `SpatialAudioSystem.Update`.** `Update` (`SpatialAudioSystem.cs:721-737`) calls the occluder first, so
  `method_35` (out-of-range processors coming back), `method_24` (disposal of invalid processors) and `method_25` (the
  per-frame scheduling of one continuous processor) do not run; `LateUpdate` then has no scheduled processor. The
  occlusion of bots' footsteps, gear and voices and of continuous sources is not re-evaluated for the length of the
  burst, and a source that comes into range is not taken up. `LateUpdate` itself runs: containers still go back to the
  pool and rooms are still tracked.
- Nobody listened during a burst; the ArenaMapPort noise probe does not tell occlusion apart. All of this is the code's
  reading.
- **Frame time.** Keep-alive windows of 5 s that lie entirely inside a burst (6) against those after 08:31 outside any
  (117): median mean frame 9.23 ms against 8.78 ms, median p95 12.06 ms against 10.46 ms, median 108.5 against 113.9
  fps. Four of the six windows are in wave 10 (all shooters, the heaviest), so the 0.45 ms are an upper bound, not a
  cost. The exception is thrown and logged by Unity, not caught by the game.
- **Log.** 5259 exceptions of 10 lines each, 4,675,251 bytes: 61.3 % of the 7,632,364-byte log; 85 to 120 per second
  during a burst.
- **What ends it** (step 10): the death of the holder, then one frame (or the rest of its phrase). In a played match
  the holder is the infected that spawned at the wrong moment; the burst lasts until it is killed, or, if it is stuck,
  until the watchdog retires it (100 s without a kill while the wave is saturated) or the raid ends. A holder that
  walks into the listener's indoor room pauses the throwing without removing the entry.

## 3. Reproducing it: the native check

Run on SPT-GPU-2 on 2026-11-20 between 11:02 and 11:34 (VM time), after the owner asked for it, with no other session
on the host. Installed and unchanged: client and server `0.6.0+72273354` (client DLL `978B32CB...`, server DLL
`69A60D06...`), the build **without** the branch's profile correction. EFT 0.16.9.5, UiBridge 0.10.1. Factory, Zombies,
Arena kits, 32 alive, no player kills; godmode re-applied every 1.5 s.

### Method

A script (`monitor*.mjs`, built on `tools/ZombiePerformanceChecks/bridge.mjs`, read-only apart from godmode) polled
the singleton `Audio.SpatialSystem.SpatialAudioSystem.Instance` about every 0.65 s with `uibridge_inspect`:

- the pool array `_containerData._audioContainersPool.gparam_0` in one `collection` call (one main-thread work item,
  so one frame). The inspector returns the same handle for the same object, so **one handle in two slots is one object
  pooled twice**; its `ID`, `OcclusionTest` and `_initialized` were read at once;
- `_activeSoundContainers` and the player containers (`_registeredPlayerContainers.Values`). A container in both is
  **armed** (step 4). The same handle twice among the player containers means two players own one object;
- the main occluder's `_activeContainersByID` (keys and values, then each value's `ID` and `OcclusionTest`) whenever
  it was not empty: an entry whose key differs from its container's `ID` is the stale key of step 9.

The lists come from separate calls, so from different frames: a container seen both held and pooled during a spawn
can be a return and a draw between two calls; it counts only when it holds over several polls in a quiet stretch.

### Results

| Raid | Settings (persist=false where not a menu row) | Retirements | Armed containers | Pooled twice after the holder's death | Exceptions |
| --- | --- | --- | --- | --- | --- |
| 1 | waves 32 +0, `StuckWaveTimeoutSeconds` 20, `IntermissionSeconds` 30 | 6 of 32 | 1 | 1 of 1 | 0 |
| 2 | waves 64 +0, 20 s, 30 s | 6 of 32 | 2 | 2 of 2 | 0 |
| 3 | run A's: waves 50 +20, 5 s, 2 s | 27 (21 of 32) | 3 | 3 of 3 | 0 |

- **The double return.** Raid 1, wave 1: the first infected's container (id 70) was armed while the wave filled; at
  the first retirement its holder died, and as soon as the dead players' containers had gone back (about 2.5 s,
  the speakers' remaining phrases) the pool held that object in slots 26 and 34, uninitialised, id 70. It stayed so
  for the whole 30 s intermission. Raids 2 and 3 showed the same for every armed container.
- **The pause and resume.** Once caught between two polls (raid 1, wave 2): `_pausedContainers` went from 2 to 1 in
  the poll in which the holder's container became armed. A second paused entry (key 15, from before the first wave)
  stayed for the whole raid; it is a different leak and was not followed.
- **What the copies did next** (the "other orders" of step 8, now seen):
  - two new infected drew both copies and shared one container (the same object under two player ids), in all three
    raids. Their two deaths put it back in two slots, and the next wave shared it again: the duplicate kept itself
    alive for the rest of each raid;
  - an infected held the object while another copy lay in the pool: through all of raid 1's wave 6 (slot 64, two
    infected sharing the object meanwhile). Wave 2 of raid 1 ended the way this allows: the object came back from the
    retirement in three slots carrying id 188, an id handed out to a one-shot during that retirement, so a one-shot had
    drawn the pooled copy and re-initialised the container of a bot that still lived;
  - two one-shots drew both copies (raid 3, six times): the occluder kept the first one's key (378, 1043, 1119, 1194,
    1287, 1548) for a container that carried the next id (379, 1044, 1121, 1195, 1299, 1549) and a one-shot test. This
    is step 9's stale key with a one-shot as the second owner, which does not throw; each was gone within about 2 s.
- **Accumulation.** Up to three containers were pooled twice at the same time (raid 3), and one object lay in three
  slots (raid 1).
- **No exception.** For the throw a bot has to be the second owner of a container that the occluder lists under an
  older key. That order did not come in 39 retirements. Raid 3 does not reproduce run A's load: on this build the
  waves filled slowly from about wave 5 on, as the night before, and the 21 retirements of 32 took 13.8 minutes
  against 55 in 12.7 minutes in run A. The branch build was not installed for this check.

### What the check does not show

- That the bursts of run A came from this path. It is the only path the code offers, and every intermediate state of
  it was seen, but no burst came under observation.
- Why so few infected are armed (6 of about 1,140 retired): the limiter pauses a container only out of hearing range,
  and the polls did not catch most transitions.
- **Player kills at high waves.** Every infected death that was armed arms the pool, killed or retired. One-shots are
  plentiful in a fight (the player's own bullet impacts count), and in Zombies a spawn follows the kills. The same
  coincidence is possible; it was never seen in a played match, and how often it would come is unknown.

Restored after the check: EFT and the SPT server on SPT-GPU-2 stopped as before; `com.maschine.ArenaMode.cfg` on the VM
and the host (the raid recipe had written mode, own gear and wave sizes) and the server's mirror `client-settings.json`
(it had taken the test values) put back from `D:\SPT41-Backups\audio-occluder\20261120-110123\` and checked by
SHA-256 (`F9AF829F...`, `1F29D2C9...`). The host's test profile differs from that backup only in `Health/UpdateTime` and
`Hideout/sptUpdateLastRunTimestamp`.

## 4. Options

Non-negotiable 2 governs all of them: a patch starts with `ArenaSession.IsActive`, fails open, and a normal raid
(which has the same game bug when it has infected) stays as the game makes it.

Built the same day on the owner's word, on the branch `feature/zombie-audio-container-guard`: the return guard of the
last row (`ArenaAudioContainerReturnGuardPatch`, contract [AUDIO-CONTAINER-GUARD-20261120.md](../../reviews/AUDIO-CONTAINER-GUARD-20261120.md)).
Its review found one more case the prefix cannot see (a return nested in another return of the same container at the
raid's end); the contract records it.

| Option | What it changes | Cause addressed | Risk | Verdict |
| --- | --- | --- | --- | --- |
| Leave it | Nothing | No | None to normal raids. Silent one-shots and frozen bot occlusion while one infected lives; log growth of about 90 KB/s. Natively also: infected that share one container for the rest of the raid (silent; consequences not traced) | Acceptable; the default while nobody hears a difference |
| Spread the watchdog's retirements over several frames | Fewer deaths per frame | No: one armed death arms the pool, and a spawn follows almost every retirement anyway | Changes the watchdog's timing (wave completion, alive count, reward withholding) for a correlation; a rate of 5 in 55 cannot show an effect in a few runs | Not recommended |
| Finalizer on `GetTargetOcclusionType` | Swallows the exception, returns a value | No | The exception is still thrown each frame; the stale entry stays; a returned `Obstruction` or `Propagation` makes the occluder calculate and apply occlusion to a bot's spine source, and `ApplyOcclusionToSource` may `Release()` that persistent source (`:208-217`) | Not recommended |
| Prefix on `MultiSourceSpatialAudioOccluder.OnUpdate` | Queues every key whose container's `ID` differs from the key into `_containersToRemove`; the original's `ProcessPendingRemovals` (`:110`) returns the calculators and drops the entry before its loop | The symptom only: the double ownership stays (bot containers shared, silent consequences of the other orders) | Small: a scan over a few entries per frame on the main occluder; the condition is exact because ids are unique per draw | Acceptable as the minimal guard |
| Prefix on `AudioSourceContainerData.ReturnContainer` | Skips the return when the pool array already holds this object | Yes for the path found: the second return of step 7 is the skipped one, and `UnregisterSourceContainer` has already removed the container from both lists. The shared containers of section 3 all start from such a double return, so they do not arise | A linear scan of the pool (50 and more slots) per return, a few returns per frame. The method is tiny (two calls), so check natively that the patch is hit (a Debug counter): an inlined copy in a caller compiled before the patch would bypass it. Never patch `PoolWithoutAlloc<T>.PutObject` itself: a generic over a reference type shares its code with every other such pool | **Recommended**, if a guard is wanted |

The two prefixes can be combined: the return guard removes the cause found, the occluder prefix catches an alias of
any other origin. Each needs an offline harness with mutants for its pure rule (already pooled; stale key) and a
native run that reads the pool after infected deaths; the monitor of section 3 is that reading, and with the return
guard it must show no object in two slots and no two players on one container. A remaining hole of the return guard:
`method_26` returns a bot's container while the bot still owns it only if all of the bot's sources are gone, which the
step and gear sources prevent while the bot lives. The guard does not undo a duplicate that already exists, so it
must be active from the raid's start (it is, when it is a Harmony patch bound to the Arena session).

## Not shown

- A burst under observation, and so the stale key with a bot as its second owner (section 3).
- What a burst sounds like, and what two infected sharing one container do to their sounds.
- How often the coincidence comes in a played match.
- Frame times with the exception and without it under the same load.
