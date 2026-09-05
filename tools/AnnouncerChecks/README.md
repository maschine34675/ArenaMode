# Announcer lifecycle and recovery checks

Run from the ArenaMode repository:

```powershell
dotnet run --project tools/AnnouncerChecks -c Release
pwsh -NoProfile -File tools/AnnouncerChecks/Verify-Regression.ps1
```

The harness compiles the actual `AnnouncerService.cs`. Only its external game,
Unity audio, transport, configuration and logging dependencies are stand-ins.
It drives `EnsureLoaded`, `Attach`, `Detach` and match callout events; assertions
observe native playback requests, transport requests, clips and logs rather than
private cache/loading flags.

Coverage includes:

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
- Direct requests through EFT's native
  `GUISounds.PlaySound(clip, single: true, commonUiSound: false, volume)` route.
  Callouts and immediately eligible cues request playback synchronously. The
  clip is reconstructed from the signal-bearing managed Arena PCM cache, and
  every event produces at most one native request.
- Requested WARLORD award keys, priority/coalescing of rapid awards, bounded
  four-second pending age, and match results superseding pending situation cues.
- A Tactical result arriving while a successfully requested kill callout owns
  the single UI channel waits for that clip's actual duration plus a short gap.
  The controlled per-frame tick drains a result or one pending award; round start
  remains immediate, and detach, shutdown or a new round cancels stale work with
  a diagnostic rather than leaking it into another round or match.
- No Arena-owned `AudioSource`, BetterAudio prerequisite, output watchdog,
  reflection, fallback, or shared-source priority lease. Detach and shutdown do
  not stop or reprioritize the shared GUISounds source.
- Fail-soft handling when GUISounds is unavailable or rejects a request, without
  a second output path or an exception escaping the match event.

The request pump and clock are controlled; these checks perform no real waits
for playback. Fixture audio files contain placeholder bytes, not real audio.
Actual Ogg decoding, Unity's async context, `AudioSettings.Reset`, Harmony/game
integration, mixer routing and audible output need a real client. The harness
proves the reset-recovery, serialized request and ownership contracts, not that
Unity's audio device made the request audible.

`Verify-Regression.ps1` compiles controlled copies of the current service. It
keeps the loading flag set, accepts a zeroed runtime wrapper as valid, replaces
the managed samples with silence during reconstruction, disables the shutdown
load-epoch guard, mutates both native GUISounds routing flags, removes the real
callout duration, bypasses result admission, prevents queue draining, and removes
both lifecycle cancellation boundaries. Each mutation must fail at its claimed
boundary. A
source-contract check requires the managed PCM/rebuild guards and rejects a
decoder-owned durable cache along with the old private-source, BetterAudio,
reflection, priority-lease and output-watchdog machinery. No test replaces a
production file; a hash check verifies the source stayed unchanged, and the
unchanged service is tested again afterward. Temporary negative-test evidence
is retained in the printed directory.
