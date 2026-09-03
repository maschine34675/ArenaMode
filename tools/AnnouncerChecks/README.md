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
  diagnostics when mixer or listener state changes. The service never overrides
  mixer volume, listener mute or pause.
- A dedicated highest-priority 2D source on EFT's master-controlled bypass,
  next-frame real-voice confirmation, and one delayed `GUISounds` fallback when
  Unity reports the private source disabled, muted, inactive, stopped or virtual.
- Reflection-safe observation of EFT's single UI source, a temporary priority
  lease that restores the original value, and confirmation of the fallback on
  the following audio frame.

The request pump and clock are controlled; these checks perform no real waits
for playback. Fixture audio files contain placeholder bytes, not real audio.
Actual Ogg decoding, Unity's async context and native lifetime implementation,
Harmony/game integration, mixer routing and audible output need a real client.
A successful `Play` or `PlaySound` request is only considered locally confirmed
after its source remains playing and non-virtual on the next audio frame; the
test harness still cannot prove physical device output.

`Verify-Regression.ps1` compiles two controlled copies of the current service.
The first keeps the loading flag set after completion and must fail the
failed-first-load retry assertion. The second disables only the late-readiness
guard and must fail the native-clip preservation check. Neither test replaces
production files; a hash check verifies the source stayed unchanged, and the
unchanged service is tested again afterward. Temporary negative-test evidence
is retained in the printed directory.
