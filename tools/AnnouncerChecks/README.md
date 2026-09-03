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
  service never overrides mixer volume, listener mute or pause.
- A dedicated highest-priority 2D source on EFT's master-controlled bypass,
  buffer-aware real-voice confirmation, and one delayed `GUISounds` fallback
  when the private source remains disabled, muted, inactive, stopped or virtual
  after at least 250 ms and two complete DSP-ring durations. A bounded probe
  window prevents a stalled main thread from replaying an already finished cue.
- Reflection-safe access to EFT's single UI source and a temporary priority
  lease that restores the original value. Its aggregate state is logged as
  unverified evidence; the shared `PlayOneShot` fallback is never stopped,
  rejected or replayed from that snapshot.

The request pump and clock are controlled; these checks perform no real waits
for playback. Fixture audio files contain placeholder bytes, not real audio.
Actual Ogg decoding, Unity's async context and native lifetime implementation,
Harmony/game integration, mixer routing and audible output need a real client.
A successful dedicated `Play` request is only considered locally confirmed
after its source remains playing and non-virtual beyond the safe DSP window.
The shared UI one-shot deliberately receives no destructive confirmation; the
test harness still cannot prove physical device output. Its delayed-start model
does verify that the original 50 ms watchdog would reject audio which becomes
ready later inside the real device's measured DSP window.

`Verify-Regression.ps1` compiles controlled copies of the current service. It
keeps the loading flag set, disables the late-readiness guard, restores the
unsafe 50 ms output check, removes the bounded probe expiry, and makes the
shared UI observation destructive. Each mutation must fail at its claimed boundary. No test replaces a
production file; a hash check verifies the source stayed unchanged, and the
unchanged service is tested again afterward. Temporary negative-test evidence
is retained in the printed directory.
