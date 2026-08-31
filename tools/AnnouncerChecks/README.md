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
  native UI routing and volume, missing phrases, and detached/old matches.
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

The request pump and clock are controlled; these checks perform no real waits
for playback. Fixture audio files contain placeholder bytes, not real audio.
Actual Ogg decoding, Unity's async context and native lifetime implementation,
Harmony/game integration, mixer routing and audible output need a real client.
A successful native `PlaySound` request does not prove the device was audible.

`Verify-Regression.ps1` first compiles a copy of the committed pre-change service
at `6938751c8c798f793b2ccea48045cf2ab7dcb513`. That baseline must fail at the
failed-first-load retry assertion, before the new diagnostic assertions run.
The optional `BaselineRevision` parameter can select another equivalent
pre-change revision. The second copy disables only the late-readiness guard and
must fail the native-clip preservation check. Neither test replaces production
files; a hash check verifies the source stayed unchanged, and the unchanged
service is tested again afterward. Temporary negative-test evidence is retained
in the printed directory.
