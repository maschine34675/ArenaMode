# Announcer silence: diagnosis and recovery

Current asset note (2026-09-12): this is the historical 2026-08-31 diagnosis.
MULTI KILL (spoken ULTRA KILL), MONSTER KILL and UNSTOPPABLE now have recordings;
five user-selected ElevenLabs effects are tracked with the current audio assets.
See LOCAL-ANNOUNCER-TRIAL.md and packaged AUDIO-LICENSES.md for their scope.

## Observed behavior

The user heard the supplied voice pack earlier, then reported silence while the
callout text continued to appear. This confirms that callouts were emitted; it
does not identify which audio guard, mixer or native playback state caused the
silence.

The supplied `BepInEx/LogOutput.log` records six loaded clips at 21:22:42 on
2026-08-31, during a Factory Gun Game match. Another Factory Tactical match starts
at 21:31:46. No announcer warning, audio exception or callout-event error appears
during these two raids. The old service did not log individual playback requests
or silent skips, so the exact cause cannot be established from that log.

MULTI KILL, MONSTER KILL and UNSTOPPABLE still have no recording in this pack.
They remain text-only; no substitute audio or additional licensed asset is added.

## Verified weakness and changes

The old `_loadStarted` flag permanently prevented another load attempt, including
after a missing folder, failed decoding or a cached Unity clip becoming unusable.
That failure mode is distinct from proving that it occurred in the user's raid.

- Revalidate the cache at every Arena match start; reuse healthy clips and retry
  failed, unloaded or destroyed clips. In-flight load passes are shared.
- Check `AudioClip.loadState`, not only completed network status. A clip still
  loading is given bounded time to finish. Newly downloaded unloaded clips first
  request their audio data. Only ready clips enter the cache.
- Publish a ready replacement before destroying an old unusable clip. Cleanup
  touches only recordings downloaded by this service, never game-owned audio.
- Release the loading guard after all outcomes. One corrupt file does not prevent
  the remaining files from loading. Requests and clip-data waits have timeouts.
- During a match, known failed or invalid clips can retry with a ten-second
  backoff. Missing pack phrases do not trigger repeated directory scans. No
  missed callout is queued or replayed after loading.
- Log attachment and a detached-match summary, including callout, playback-request
  and skip counts. Log each skip reason once per key and match. Record the first
  three requests and later output-state changes with UI/master mixer levels and
  listener volume/pause state.

Native `GUISounds.PlaySound(single: true, commonUiSound: false)` routing remains
unchanged. Mixer values and listener settings are read only; mute is respected.
The service still subscribes only to Arena matches, and late load completion
cannot attach an ended match. Scoring, HUD, normal raids and profile handling are
unchanged.

## Verification and remaining live check

The installed Unity wrappers show that `DownloadHandlerAudioClip.GetContent`
does not validate `AudioClip.loadState`; `LoadAudioData` alone is not proof of
completion. EFT's sound settings bind the UI level to `UIVolume` and the overall
level to `MasterVolume`. These exact local APIs underpin the checks and logs.

Validation completed:

- Independent adversarial review passed. It found a race in the first draft:
  an old clip could become ready during its replacement download. The final code
  preserves that now-usable original and destroys only the redundant download.
- The production-service harness passes 78 assertions, including recovery,
  loading timeouts, the above race, detached matches, bounded logs and unchanged
  mixer/listener settings. The test implementation was reviewed independently.
- A copy of the committed old service fails the first-load recovery check. A
  separate copy without the late-readiness guard fails the clip-preservation
  check. Unmodified production passes again afterward; source hashes are unchanged.
- All 65 server tests pass with no skips. Menu negotiation (31), side view (29),
  map view (40) and Labs bot spawning (65) checks also pass.
- The Release client builds with four existing nullable-context warnings and no
  errors. Installed and build DLL hashes match. Decompilation of the installed
  client confirms the new recovery, readiness and diagnostic code. All six audio
  assets and both accompanying license/documentation files match their sources.

Installed client SHA-256 at verification:
`C6B6CCCADCED53ECE8E80A73D2B77E2C44D5622836C6CC1FA22FF91F4337D8B9`.

On the gaming PC, install the updated client DLL and restart the game. Exercise
the first match and a subsequent Arena match with known supplied phrases such as
DOUBLE KILL and KILLING SPREE. If sound is still absent, the new `Arena announcer`
lines distinguish skips from playback requests and include the output state.
A playback request is not proof that a device produced audible sound. This
server has no GPU; no game or listening test was performed here.
