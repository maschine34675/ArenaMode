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
- Full-signal PCM materialization from decoded Ogg clips, release of the
  decoder-owned clip, and a fail-soft path when `GetData` is unavailable.
- Healthy cache reuse, retry after an initially failed request or decoder
  exception, discovery of a previously missing folder at the next match, and
  restoration of destroyed or unloaded Unity audio clips.
- Unity's destroyed-object null behavior, `AudioDataLoadState`, explicit
  `LoadAudioData`, readiness timeout and reuse of an existing Loading clip.
- A cached clip becoming ready while a replacement request is in flight:
  keep the now-playing original, dispose the unnecessary new clip.
- Shared in-flight repairs, at least ten seconds between repair attempts,
  bounded diagnostics per key/reason, and no delayed replay of skipped kills.
- Attach/Detach counters and initial playback diagnostics, with additional
  diagnostics when mixer, listener, voice or DSP-buffer state changes. The
  service observes `InterfaceVolume` as GUISounds' relevant mixer setting and
  never overrides mixer volume, listener mute or pause.
- Direct, synchronous requests through EFT's native
  `GUISounds.PlaySound(clip, single: true, commonUiSound: false, volume)` route.
  The requested clip is the signal-bearing Arena PCM copy and every event
  produces at most one native request.
- No Arena-owned `AudioSource`, BetterAudio prerequisite, delayed `Tick`, output
  watchdog, reflection, fallback, or shared-source priority lease. Detach and
  shutdown only unsubscribe Arena events; they do not stop or reprioritize the
  shared GUISounds source.
- Fail-soft handling when GUISounds is unavailable or rejects a request, without
  a second output path or an exception escaping the match event.

The request pump and clock are controlled; these checks perform no real waits
for playback. Fixture audio files contain placeholder bytes, not real audio.
Actual Ogg decoding, Unity's async context and native lifetime implementation,
Harmony/game integration, mixer routing and audible output need a real client.
The harness proves the synchronous native request and ownership contract, not
that Unity's audio device made the request audible.

`Verify-Regression.ps1` compiles controlled copies of the current service. It
keeps the loading flag set, disables the late-readiness guard, bypasses PCM
materialization, and mutates both native GUISounds routing flags. Each mutation
must fail at its claimed boundary. A source-contract check also rejects the old
private-source, BetterAudio, reflection, priority-lease and Tick/watchdog
machinery. No test replaces a production file; a hash check verifies the source
stayed unchanged, and the unchanged service is tested again afterward.
Temporary negative-test evidence is retained in the printed directory.
