# PMC voice checks

Run `pwsh -NoProfile -File tools/PmcVoiceChecks/Verify.ps1` from the repository.
The console project links the complete production runtime and policy, with small
external/native doubles. It never builds or deploys the mod projects. The wrapper
runs the production baseline, adversarial source mutants, then production again.

Coverage includes ordinary/stale/dead/unpublished players, exact registered-body
identity, killcam/spectator/respawn holds, successful grenade/dry-fire hooks,
observed reload transitions, shared cooldown, delayed kill expiration, friendly
and old-body kills, faction rotation including English BEAR, original voice/bank
restoration, live body/speaker replacements, pending load cancellation and faults,
announcer arbitration, native queued/pain speech, pooled source ownership and
reentrant speech callbacks. Result holds keep voice assets until the actual raid
ends because a native death clip can still use the target bank.

Native contract inspected against this installation's `../_decompiled/`:

- `EFT/Player.cs`: `FirearmController.IsInReloadOperation` includes base reload,
  insert/remove magazine, chamber and revolver operations. Polling this query
  observes the actual operation rather than a rejected `ReloadMag` attempt.
- `EFT/ClientFirearmController.cs`: `DryShot` calls the patched base implementation.
- `EFT/Player.cs`: `BaseGrenadeHandsController.ThrowGrenade` calls
  `CreateAndRegisterGrenade` only after the native operation succeeds; normal
  and quick-throw hands share the base creation path. Tripwire placement does not.
- `EFT/BaseSpeaker.cs`: `Play` returns the selected bank after synchronously
  emitting `OnPhraseTold`; `ReplaceVoice` changes only banks, not `PlayerVoice`
  or `SideTag`. Existing `Busy` speech and native queued phrases are respected.
- `PlayerVoiceLoader.cs`: retain the voice bundle via `IEasyAssets.Retain`, then
  await `EasyAssetsExtensions.LoadBundles`. Our runtime has its own cancellation
  race and 15-second ceiling; it never calls global voice-unload helpers.
- `EFT/Player.cs`: `SpeechSource` allocates when `_speechSource` is null. Playback
  tracking therefore reads the existing field, never that getter. Cancellation
  validates body, speaker, native source, Unity source, clip and release time.

These checks prove code-path behavior and regression controls, not Mono detour
installation, actual bundle availability, audible volume or native animation
timing. The root task's GPU report supplies those observations.
