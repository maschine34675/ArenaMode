# Labyrinth center alarm: Arena-only suppression

`Audio / MuteLabyrinthAlarm` defaults to true. Enabling it during an exact
active Labyrinth Arena raid stops existing/scheduled siren playback once and
suppresses subsequent output. Disabling it allows the native loop to play at
its next cycle (about four seconds). Ordinary raids, other maps, announcements
and combat audio do not enter this suppression path.

## Installed scene evidence (SPT 4.1, 2026-09-05)

Read-only inspection used [UnityPy](https://github.com/K0lb3/UnityPy) 1.25.3.
The game assets and audio were not changed or copied into the mod.
`globalgamemanagers` BuildSettings maps scene index 552 to
`Assets/Content/Locations/Labyrinth/Labyrinth_DesignStuff.unity`.
Installed `level552` SHA256:
`d39b3c4606d51fe9699319ef1da4f671f129843c1bcd7bc8f23efd68436bbfeb`.

- `SyncLoopSoundPlayer` object 8958 lives on `alarm_sound_root`.
- Its clip is `alarm_oneshot`, four seconds, from `sharedassets552.assets`
  object 93. `_playOnAwake=false`, `_useSoundDelayCalculation=true`.
- Seven sources belong to its transform hierarchy: 8211, 8214, 8209, 8212,
  8210, 8215, 8213. Their serialized `playOnAwake` flags are false.
- Four `SyncableLoopSoundPlayer` handlers on
  `INTERACTIVE_Labyrinth_Boss_Alarm` all reference that same player: entry IDs
  `alarm_enter_3508252692`, `alarm_enter_4274982985`,
  `alarm_enter_336915922`, `alarm_enter_3861355289`; stop IDs are empty.
- Associated trigger zones emit these entry IDs. Other handlers/switches can
  disable the entry zones and emit the global bot event `alarm_stop`.

`tools/LabyrinthAlarmChecks/Inspect-InstalledScene.py` rechecks the scene index,
serialized contract, shared player, clip and all seven source ancestries against
the locally installed game, without exporting or modifying assets. UnityPy is
an optional inspection dependency; it is not needed or bundled at runtime.

## Native boundary and safety

Source of truth is `../_decompiled/`:

- `Audio.AudioCulling/SyncLoopSoundPlayer.cs`: `method_0()` assigns the shared
  clip to each source when delay calculation is enabled. `PlayCoroutine()`
  invokes `InitializeSource(AudioSource)` per active source every clip length
  plus 0.005 seconds. That method only calls `PlayDelayed` or `PlayOneShot`.
- `CommonAssets.Scripts.Game/SyncableLoopSoundPlayer.cs`: `PlaySound()` calls
  the player and raises `StateChanged`. The patch does not skip that method.
- `LocationScene.cs`: `Awake()` registers `ITriggerEntity` instances, not
  `SyncLoopSoundPlayer`. The startup scan uses that existing registry.
- `AIPlaceLogicAgroTagilla.cs`: global `alarm`/`alarm_stop` events affect bot
  behavior. Those events and all trigger state remain untouched.

The prefix requires `ArenaSession.IsActive`, the F12 setting, exact active
`GameWorld`/`AbstractGame` ownership, Labyrinth, the loaded exact scene path,
root/clip names, native playback flags, source membership, source hierarchy,
matching scene instance and exact assigned clip. Any mismatch/exception lets
the native call proceed. The one-time startup/off-to-on scan also validates
the four known handlers and deduplicates their shared player.

Only these sources receive `Stop()` to cancel existing/scheduled playback.
No global audio hook, mixer, volume, mute, listener, asset, loop flag, bot event,
map trigger or profile state is changed. Native coroutine and state events
continue running, so no restoration lease is necessary. Shutdown disables the
feature and removes its own patch; source registry enumeration is never per
frame. A missing scene contract is logged and fails open.

## Verification and live handoff

The production-linked harness exercises the actual registered prefix and scan:
normal/stale raid exclusion, other maps, mismatched scene/source/clip ownership,
four-handler deduplication, repeated output, scheduled-audio cancellation,
hot toggling, failure behavior and shutdown. Independent adversarial review
found no unresolved P0/P1/P2 issue.

Local verification: 91 alarm assertions, three deliberately broken source-copy
variants rejected at their intended checks, 142 existing announcer assertions,
135 unit tests, and successful Release client build. Built/deployed client
and Redeploy.Core Release hashes match. Initial compilation has four existing
CS8632 warnings; the final incremental build has no warnings or errors.

The server cannot launch the game (no GPU). After restarting the client:

1. Start Arena on Labyrinth and enter the center. The log should report
   `Arena Labyrinth alarm suppression active: 1 verified player(s), 7 source(s)`.
2. Verify the siren stays silent through respawns/rounds, while announcer and
   combat sounds remain audible.
3. While the alarm is triggered, toggle F12 Audio / MuteLabyrinthAlarm off:
   siren output should resume within one native cycle. Toggle on to silence it.
4. A normal Labyrinth raid must retain the unmodified alarm.

See `tools/LabyrinthAlarmChecks/GATES.md` for measured local evidence and the
explicit outstanding GPU handoff. Offline checks are not proof of audibility.
