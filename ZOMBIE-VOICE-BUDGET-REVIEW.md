# Zombie repetitive voice budget — 2026-10-09

## Scope and interface

`ZombieVoiceBudget.EnablePatch()` installs one prefix/postfix pair on the exact
`Player.OnPhraseTold(EPhraseTrigger, TaggedClip, TagBank, BaseSpeaker)` method.
`Tick()` belongs in the persistent controller update, including outside an active
raid, to sweep sources twice per second and publish 30-second aggregates. `Reset()`
is also available for explicit teardown. `Settings.ZombieVoiceBudgetEnabled`
defaults true; false observes native audio and records `wouldSkip` without
suppressing any clip. Root-owned integration and native build/playtests are
separate from the standalone checks documented here.

Only `OnMutter` and `OnEnemyConversation` from live, exact, native generic infected
bodies in the confirmed active Zombie LocalGame/GameWorld pair compete. The
four roles are infectedAssault, infectedCivil, infectedLaborant and infectedPmc.
Each body must still be the native alive-player registry's exact instance, own
its current BotOwner, belong to the same world, have an active native
`InfectedLayersStrategy` (no subclass), and be neither a native follower nor a
known PFT teammate. Boss infected, human bots, normal raids, stale bodies,
unknown/custom brains and uncertain state retain native behavior. No profile,
PFT persistence, world registration or population state is changed.

Initial tuning, awaiting hearing acceptance: six concurrent repetitive voices;
at most four of those may be at or beyond 20 m. This leaves two slots for closer
zombies without interrupting an existing clip. `OnEnemyConversation` at or below
8 m is always admitted, including behind the player. `OnMutter` at that distance
still competes. `OnFight`, hurt, agony, death and every other phrase always pass.
These exemptions can exceed six total audible voices deliberately. Native
speaker priority/busy checks still decide whether those phrases reach playback.
Distance follows the current living local main body; if it is dead, missing or
ambiguous, including PFT spectator play, audio admission fails open.

## Native boundary

Verified against the local SPT 4.1 decompile:

- `InfectedAttackLayer.cs:105` and `InfectedPatrolLayer.cs:42` repeat their respective
  `OnEnemyConversation`/`OnMutter` calls every random 1–3 seconds and drop the native
  talk cooldown first. This identifies the two selected atmosphere triggers.
- `EFT/Player.cs:28374` emits `GlobalEventDispatcher.SayPhrase(this, phrase)` before
  `Speaker.Play`. `BaseSpeaker.Play` independently checks busy/importance and
  `SpeakerManager.FreeToSpeak`, selects a tagged clip, sets `Speaking` and
  `ReleaseTime`, then invokes its multicast event. None of this is skipped.
- `EFT/Player.cs:28419` contains only `PlayPhraseClip(clip)`. Suppressing this one
  subscriber's body preserves the other subscribers and the subsequent native
  chain/release logic. No prefix on Say, BotTalk, BaseSpeaker or AI events exists.
- `PlayPhraseClip` synchronously calls `PlaySpeechFromTime` at 27065. Native range,
  muffling, source activation and playback then occur. The speech group uses
  `ReverbSimpleSource` (`BetterAudio.cs:989`), which calls `SimpleSource.Play`:
  `CanPlay` can refuse, while successful non-one-shot speech assigns
  `source1.clip` and calls `source1.Play()` synchronously. The primary source is
  the observed lifetime for the voice, including its linked reverb rendering.

The source accessor reads the existing protected `_speechSource` field through a
single cached Harmony field delegate. It never calls the lazy `SpeechSource`
property. Thus a refused candidate does not create/prewarm a speech source.
This does **not** claim to remove all ongoing spatialization or occlusion work:
previously created sources, native event delivery, bank selection and the existing
speaker manager continue. No global audio setting, source mute, mixer, native
priority, source stop or audio-thread setting is changed.

## Actual source lifetime

The prefix captures a small value-type admission, without reserving a slot.
Only after the synchronous original returns does the postfix observe a live
source whose `source1.isPlaying` is true and whose clip is the requested clip.
A native range/CanPlay refusal or a thrown original cannot create a phantom slot.
If the exact requested clip was already playing, it remains one observed voice;
the `played` counter is a confirmed playing-source observation, not an audio-device
start notification or proof that the listener heard it.

On every admission and the half-second sweep, stale/dead/swapped bodies,
replaced sources, changed clips and stopped playback release their observation.
No guessed cooldown or clip-duration expiry can release a still-playing source.
A body's next phrase can reuse its existing slot. Its old observation is retained
until playback is confirmed: if native range checking refuses the replacement,
the old voice continues occupying the slot. Critical playback replaces the clip
and is noticed on the next sweep without stopping or modifying either voice.

World/game boundaries clear all references, while settings toggles retain actual
playing observations and start a new diagnostic window. A delayed postfix cannot
publish into another world or game. Up to 128 observations fit in a preallocated
array, including the unsuppressed comparison baseline; overflow fails open and is
reported. Enabled admission normally needs six entries. Steady admission creates
no managed per-call objects in the controlled adapter fixture.

## Diagnostics and comparison limits

The aggregate line reports `enabled`, `candidates`, `admitted`, `played`, `skipped`,
`wouldSkip`, `exempt`, `nativeUnplayed`, `observedPeak` and `overflow`. It is emitted
every 30 seconds with activity and at settings/world/end boundaries. Candidate
counts include both selected repetitive triggers; near attack exemptions are
included in candidates and exempt, while other critical phrases are only exempt.
Admitted/played count budget-participating requests, excluding explicit exemptions.
`observedPeak` counts those observed repetitive voices, not all audio sources.

With the switch off, `skipped` stays zero. `wouldSkip` evaluates the actual native
occupancy, so it is a useful demand measurement but **not** a simulation of the
exact enabled timeline. Native random clip choice, movement, source ends and AI
events affect both runs. Compare source-bound builds, the same map/config/wave,
similar live counts and the same profiler window; report native audibility and
performance separately. No FPS gain, full occlusion reduction, budget tuning or
native hearing acceptance follows from the offline checks.

## Offline evidence and four passes

`dotnet run --project tools/ZombieVoiceBudgetChecks -c Release` compiles the actual
adapter and policy sources with narrow native doubles, and checks the decompiled
native call contracts. `tools/ZombieVoiceBudgetChecks/Verify.ps1` additionally
compiles faulty source variants and requires every one to fail a runtime assertion;
compilation failure is rejected as invalid negative evidence. It writes source
hashes, counts and per-case logs under the harness's ignored `artifacts` folder.

1. Complete implementation: two phrase triggers, six/four admission, threat and
   critical exemptions, exact identity, native playback observations and counters.
2. Domain review: native phrase signals, speaker busy/chain, lazy source creation,
   SimpleSource and ReverbSimpleSource playback verified against local source.
3. Defect hunt: checked native refusal with an already playing replacement,
   interrupted/dead/stale/PFT/custom bodies, silent failures, long clips and
   toggled baseline. Strengthened missed-end tests to attempt many voices in an
   unconfirmed new game/world, since a single allowed call did not refute a leak.
4. Polish and measurement: preallocated observations, no inactive-raid array
   clearing each frame, exact immediate peak update, bounded diagnostics and
   portable expected-failure capture for Windows PowerShell/PowerShell 7.

The final standalone result and compiled mutation count are reported by the
success tokens, not inferred from this document. Independent parent review,
staged real-reference build, restarted GPU gameplay/hearing and performance
comparison remain root-owned acceptance work.
