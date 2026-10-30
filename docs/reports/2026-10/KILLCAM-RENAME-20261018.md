# Killcam rename and Arena integration — 2026-10-18

Arena `5d53458` follows the rename of KillcamReplay 0.9.14 to **Killcam 0.9.15**.
The optional integration now resolves `com.maschine.Killcam`, `Killcam.Plugin`
and `Killcam.Replay.ReplayController`. Reflected members, camera ownership and
respawn timing are unchanged. The adapter is named `KillcamCompat`; menu/F12
copy, current feature/dependency/Forge text and linked tests use the new name.

A loaded old Replay GUID is explicitly incompatible, even when a new Killcam
also appears. Its camera cannot be mistaken for absent, and Arena does not
authorize an early replacement. Remove old Replay DLLs and replace the obsolete
experimental Killcam 1.0.0 when upgrading. The Killcam build's deploy script
archives both old DLL names outside plugin discovery and preserves settings.

## Checks

- Arena replay/hold harness: 176 assertions, eight rejected negative controls,
  restored production source passes.
- Handoff: 72; settings migration: 12 assertions in the Killcam repository.
- Spectator health 49, menu 2,527, team expansion 895, solo Zombies 85 and team
  Zombies 176 passed. Unit suite: 1,167 passed, two optional skips, no failures.
- Paired Release build succeeds with four existing nullable-context warnings.
- Independent review found no camera-contract or migration blocker.

## Native acceptance on SPT-GPU-2

Process `194a160f`, 2026-10-18, loaded matching VM/host/process binaries and only
the new Killcam identity. Automatic migration preserved 19 saved config entries
(18 active plus one old retained entry), including the non-default live tail.

Factory DM, eight participants, 3-second replay: real bot-caused death at
16:21:57.483; playback started at 16:21:58.285, with 56 snapshots / 2.95 seconds.
Native frame 12937 shows the actual replay and was visually checked. Playback
ended normally at 16:22:05.417, then the new player appeared at 16:22:05.634 with
440 HP. Two real AKM shots after respawn consumed two rounds, 31 to 29. Replay
ownership was idle/released; normal Disconnect returned to MainMenu.

All Arena settings were restored, protected profile sections match (Health
except the ordinary UpdateTime), no test hooks remain. Other client DLLs and all
16 server DLLs are unchanged. Native material, Factory marker/Transit,
negative-scale and missing-script warnings were also recorded; their cause was
not assigned by an old-build A/B test. No Killcam/Arena/Core
failure was found in this bounded run. This is not an all-modes or standalone
PMC regression test.

Tested SHA-256:

| Binary | SHA-256 |
| --- | --- |
| Killcam | `9F9E45BD940AAD2360067A09CB9F027DF9A70C8402E9CF7BFF6A3920BDAE7631` |
| Arena Client | `EEA431D6406747915313D183767323F6C53433CB6C52420956F62DEF1376C050` |
| Arena BigBrain library | `557EFC1932AB74730DB654438323A59D298B30AC4A2A3141EA9A9F0B88DA1A9E` |

Local raw evidence and the screenshot are in the Killcam repository's ignored
`artifacts/native-rename-20261018/`. Do not publish configuration/profile snapshots.
Historical reports intentionally retain the names used by their tested builds.
