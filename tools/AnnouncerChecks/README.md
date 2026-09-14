# Announcer lifecycle and recovery checks

Run from the ArenaMode repository:

```powershell
dotnet run --project tools/AnnouncerChecks -c Release
pwsh -NoProfile -File tools/AnnouncerChecks/Verify-Regression.ps1
pwsh -NoProfile -File tools/AnnouncerChecks/Verify-ShotAwards.ps1
```

The harness compiles the actual `AnnouncerService.cs`, `AnnouncerCueCatalog.cs`
and `ArenaAnnouncerOutput.cs`. Only external game,
Unity audio, transport, configuration and logging dependencies are stand-ins.
It drives `EnsureLoaded`, `Attach`, `Detach` and match callout events; assertions
observe native playback requests, transport requests, clips and logs rather than
private cache/loading flags.

The director itself is a controlled boundary here, with assertions that the
exact attached match is forwarded and director invalidation runs before queue
drain. Its production progress policy and mode adapters have separate checks.

Coverage includes:

- Logical Trickshot / No Scope awards retain queue priorities 70 / 65 while
  Godlike is only their provisional recording. A healthy future root
  `trickshot.ogg` or `no_scope.ogg` wins; missing, undecodable or unmaterializable
  optional audio falls back without rewriting its key or changing output routes.
  Output rejection/mute cannot trigger another playback attempt. Diagnostics
  remain bounded per key/match. The combined shot verifier retains the historical
  audio/combat regressions and adds six compiled priority/clip-fallback faults.

- First-match asynchronous loading, URI escaping, nonduplicated subscriptions,
  configured volume, missing phrases, and detached/old matches.
- Full-signal managed PCM capture from decoded Ogg clips and unconditional
  release of the decoder-owned clip and web request.
- Healthy cache reuse, retry after an initially failed request or decoder
  exception, discovery of a previously missing folder at the next match, and
  no delayed replay of skipped kills.
- Shutdown while a decoder request is in flight: its late continuation is
  disposed, cannot repopulate the cleared managed cache, and cannot leave the
  next loader stuck.
- The observed raid-start audio reset failure shape: a runtime clip remains
  `Loaded` but its frames, channels and duration become zero. Managed PCM
  survives a reset both before the first cue and between cues; the next cue
  synchronously builds a new signal-bearing Unity clip without disk/network IO.
- Unity's destroyed-object null behavior, `AudioDataLoadState`, explicit
  decoder `LoadAudioData`, readiness timeout, runtime reconstruction failure,
  and retry from the same managed PCM on a later cue.
- Bounded diagnostics per key/reason and strict runtime metadata validation.
- Attach/Detach counters and initial playback diagnostics, with additional
  diagnostics when mixer, listener, voice or DSP-buffer state changes. The
  service observes `UIVolume` and never probes the nonexistent
  `InterfaceVolume` parameter or overrides mixer volume, listener mute or pause.
- Explicit legacy requests through EFT's native
  `GUISounds.PlaySound(clip, single: true, commonUiSound: false, volume)` route.
  Callouts and immediately eligible cues request playback synchronously. The
  clip is reconstructed from the signal-bearing managed Arena PCM cache, and
  every event produces at most one native request.
- Requested WARLORD award keys, priority/coalescing of rapid awards, bounded
  four-second pending age, and match results superseding pending situation cues.
- Explicit loading of 19 optional reserve basenames; 4 alternate result and 5
  CTF recordings stay unused. Nested files cannot enter the cache. Existing root
  customization wins a same-name optional file once. Original pack intake
  `usage` and `eventKey` remain unchanged; selected rows add `runtimeEventKey`.
- Multi-kills outrank sprees in either arrival order. One pending phrase is
  shared between combat awards and lower-priority status, with only two seconds
  allowed for status. Current results cancel pending status and preserve the
  existing result/kill serialization and highest-priority awards.
- Countdown digits never queue or replay late; representative recorded digit
  lengths fit one-second updates without the combat-callout gap. Readiness loss,
  new rounds, results, mute, zero volume, pause and detach cancel stale status.
  Cancellation stops only the owned Master output. The optional legacy shared
  GUI source is never forcibly stopped, so a started legacy phrase may finish.
- Clutch consumes only the immediately preceding, same-frame hint for an actual
  RoundWon/MatchWon event. Other subscribers retain that original lifecycle
  event. Missing or unmaterializable Clutch falls back to the established win
  recording; unpaired, delayed and losing outcomes cannot inherit the hint.
- A Tactical result arriving while a successfully requested kill callout plays
  waits for that clip's actual duration plus a short gap on either selected route.
  The controlled per-frame tick drains a result or one pending award; round start
  remains immediate, and detach, shutdown or a new round cancels stale work with
  a diagnostic rather than leaking it into another round or match.
- Default independent output: one owned 2D source on GUISounds.gameObject,
  exact Master group and matching mixer, independent of UIVolume. Master/listener
  volume and pause remain effective; shared GUI sources are never touched.
- Mid-phrase volume changes, native audio reset, destroyed sources, replaced
  hosts/mixers, missing/substring/foreign/ambiguous Master group rejection,
  GUI-button sounds not interrupting Arena, and owned cleanup on disable/detach.
- Native Stop/Destroy failures cannot retain a subscription to an ended match.
- No BetterAudio prerequisite, output watchdog, reflection, automatic fallback
  or shared-source priority lease. Only the explicit legacy switch uses UI.
- Fail-soft handling when GUISounds is unavailable or rejects a request, without
  a second output path or an exception escaping the match event.

The request pump and clock are controlled; these checks perform no real waits
for playback. Fixture audio files contain placeholder bytes, not real audio.
Actual Ogg decoding, Unity's async context, `AudioSettings.Reset`, Harmony/game
integration, mixer routing and audible output need a real client. The harness
proves the reset-recovery, serialized request and ownership contracts, not that
Unity's audio device made the request audible.

`Verify-Regression.ps1` compiles controlled copies of the current service/output. It
keeps the loading flag set, accepts a zeroed runtime wrapper as valid, replaces
the managed samples with silence during reconstruction, disables the shutdown
load-epoch guard, mutates both native GUISounds routing flags, removes the real
callout duration, bypasses result admission, prevents queue draining, and removes
both lifecycle cancellation boundaries. Independent-output mutants change the
Master route, listener-pause policy, mixer/uniqueness validation and hot volume.
Each mutation must fail at its claimed boundary. A
source-contract check requires the managed PCM/rebuild guards and rejects a
decoder-owned durable cache along with BetterAudio, reflection, priority-lease
and output-watchdog machinery. The owned Master source is deliberately confined
to the small output adapter. No test replaces a
production file; hash checks verify all three sources stayed unchanged, and the
unchanged service is tested again afterward. Temporary negative-test evidence
is retained in the printed directory.

Expansion faults deliberately reintroduce recursive/colliding loads, reverse
multi/spree priority, queue countdown digits, extend status lifetime, remove
status/director cancellation, weaken Clutch pairing/fallback, restore the extra
digit gap and let volume-zero revive a canceled owned countdown. Every fault
must reach its named failed assertion. The same regression entry point verifies
all 46 encoded hashes and decoded signals, exact optional metadata, unchanged
reviewed cuts/intake, and seven rejecting metadata faults. Python and FFmpeg /
ffprobe are required for that real-asset check. The splitter reproduces the
new integration metadata without changing its cut or encoding procedure.
