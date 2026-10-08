# Arena follow-ups — 2026-10-08 evening

Implemented and deployed locally; **native GPU acceptance remains open**.

## Changes and commits

| Commit | Outcome |
| --- | --- |
| `ea8f5fb` | Verified existing Factory day/night exclusions against both installed databases: the same six unsafe markers are already removed. Tests/docs only; no production change. |
| `b714fd4` | Pickup WAV +12 dB, sample gain 3.981x, peak -1.94 dBFS without clipping. Runtime volume settings and licence unchanged. |
| `2cd95cb` | Akimbo models follow actual solved hands after IK, preserving independent pre-IK recoil targets. Explicit same-frame refresh also handles Manual arms/fire-event ordering. |
| `2280dee` | Confirmation and deployment use native own-equipment preview for Zombie Own Gear and protected Tactical 5v5. Other variants retain representative Arena equipment; actual later random kit is not preselected. |
| `90fbf3a` | Pickups choose another safe authored position after collection/cooldown, and after 60 gameplay seconds while available. IDs/types, buffs and pending weapon transfers remain protected. |

## Verification

Parent reran the relevant checks after independent review:

- Full Akimbo lifetime/ammo/cadence suite; expanded View checks: 52 assertions,
  six deliberate faults rejected. Review found and fixed Manual-arms same-frame
  feedback; camera movement and solved-hand motion are separate test inputs.
- Factory: 533 assertions, eight deliberate faults rejected, including a
  night-only missing exclusion against the actual installed night database.
- Preview: 676 lifecycle assertions / 16 deliberate faults, 52 core assertions /
  five lifetime faults; hooks compile against actual installed EFT references.
- Pickup runtime: 347 assertions, 30 wiring contracts, 27 deliberate faults.
  Objectives/health: 478 combined assertions, 12 deliberate faults.
- Audio: independently compared all 26,460 samples to the previous asset;
  exact rounded +12 dB gain, unchanged non-data RIFF content, no clipping.
  CLI path with spaces verified. Source/licence hashes retained.

Final staged client build: zero errors, four existing CS8632 warnings in
`ClientSettingsModels.cs`. Staging did not write installed files. Subsequent
explicit local deployment verified all copied hashes and unchanged Server,
Redeploy.Core and BigBrainCompat DLLs. No profile or saved configuration changed;
unrelated untracked Molotov intake remains untouched. No push performed.

## Artifact identity

- Source: `90fbf3aae2fa32fd7c789283db33bb9e7b36f02a`
- Client ProductVersion: `0.1.0+90fbf3aae2fa32fd7c789283db33bb9e7b36f02a`
- Client SHA256: `4C9F7C5961A91A8C8ED540DFC721BE05E6E1AE0F8F333DEDA88BF9BA72034B02`
- Pickup WAV SHA256: `E0585D79CD137AC9240A01C79048490EC87F0C2D07F9B84B9BA24F5B5D5CB171`
- Installed locally: `D:\SPT41\BepInEx\plugins\maschine-ArenaMode.Client.dll`
  and `D:\SPT41\BepInEx\plugins\ArenaMode\pickups\` (WAV/provenance/readme).
- Retained staged manifest:
  `artifacts/double-jump-build/71d89dcb33a34f6c9620a41a237e0030/manifest.json`.
  Deployment/backups: `artifacts/arena-followups-oct8/`.

## Remaining native checks

GPU-2 UiBridge at the configured address timed out. GPU-1 desktop/control
connection also timed out on start; its configured share was inaccessible.
No raid was started. There is no loaded-process hash, new screenshot, listening
test, remote deployment receipt or profile roundtrip result for this build.
Do not treat the offline suites as visual, audio or native-navigation acceptance.

On the next available GPU host, verify the client hash after restart, preserve
and restore temporary configuration, and perform these bounded checks:

1. Akimbo APB/APS and another pistol: strafe left/right, turn, stand still and
   fire alternately; inspect a short clip for hand/model separation. Repeat
   with Auto/Manual hands animator if available. Check reload, sprint, OFF/ON,
   free/paid ammo semantics and native renderer restoration.
2. Confirmation/deployment: Zombie Own Gear versus provided gear, then Tactical
   solo versus eligible protected team. Compare to the PMC's visible equipment;
   Back/reopen and local/teammate clicks must retain correct source. Ordinary
   PMC and reconnect remain native.
3. Factory DM/TDM: leave pickups available for 60 gameplay seconds, confirm
   logged relocation and six identities. Collect one and verify full cooldown
   then new location; an occupied destination requires leaving/re-entering.
   Pending M60 transfer must never move or duplicate. Listen to the cue in combat.
4. Inspect logs for new errors, then leave and verify profile preservation.

Factory's exclusion parity itself is source/database-verified. A different
stuck point needs a new marker/coordinate, not a second implementation of the
existing night exclusions. See `FACTORY-SPAWNS-REVIEW.md`.
