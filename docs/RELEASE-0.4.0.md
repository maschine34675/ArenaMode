# ArenaMode 0.4.0

29 October 2026. SPT 4.1.x; native checks used SPT 4.1.6. Single-player only.

## Highlights

- **Domination:** capture/hold objectives, team respawns, bot assignments and
  separate score/time limits. Factory varies three points each round; the four
  ArenaMapPort maps use their original objective anchors. Requires BigBrain.
- **Direct start:** launch Session.exe, choose a profile or use its desktop
  shortcut. Normal EFT need not be opened first.
- **Reliable returns:** fix the observed profile-restoration hang, recover
  stopped sessions with missing disposable data, and discard uncertain old
  rewards automatically while preserving the normal profile.
- **Clearer menus:** short copy, tooltip details, one return action, integrated
  objective HUD and Team DM as the default for new installations.
- **Compatibility:** shared Core 1.0.17, updated body/camera integrations and
  reviewed whitelist entries for ArenaMapPort 0.4.0 and Redeploy 1.1.0.
  Recognized version differences remain advisory, including UIFixes. ArenaMapPort
  0.4.0 was reviewed as a local candidate; its separate release is postponed.
- **Quieter logs:** routine session link cleanup is opt-in debug output;
  reward opt-out no longer produces a missing-snapshot warning.

See the [versioned changelog](https://github.com/maschine34675/ArenaMode/blob/v0.4.0/CHANGELOG.md#040---2026-10-29) for the full list.

## Installation

Extract **ArenaMode-0.4.0.zip** into the SPT root, merging `BepInEx/` and
`SPT_Runtime/`. Close game/server first and preserve customized kits/ladder.
Keep one copy of each Arena DLL and the supplied **Redeploy.Core 1.0.17**.
The same Core is bundled in Redeploy 1.1.0; the standalone Redeploy client/server
are optional. BigBrain, its prerequisites and ArenaMapPort are separate downloads.

Existing F12 settings and personal whitelist choices are preserved. Only new
installations default to Team DM. The old flat plugin/asset layout needs the
migration described in the [README](https://github.com/maschine34675/ArenaMode/blob/v0.4.0/README.md#install-or-update).

Direct start is the installed
`BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Session.exe`.
It manages a local SPT installation. A short normal-server start/stop before the
private Arena server is expected preparation, even with rewards off.

## Validation and remaining limits

The [native sweep](https://github.com/maschine34675/ArenaMode/blob/v0.4.0/docs/RELEASE-SWEEP-20261029.md) exercised representative scenarios
across all six modes and all four ported maps on two hosts, plus standalone PMC
respawn and own-Scav transfer. Tested round ends returned without a hang.
That sweep used the gameplay source before the final version/documentation bump;
it does not claim the rebuilt release hashes were exercised in those raids.
Final committed-source builds, version/stamp checks and ZIP manifests accompany
publication. The package manifest records the precise Arena and Core revisions.

Domination, Tactical, Zombies, Akimbo and Molotov retain their WIP bounds.
Domination currently has no profile rewards. Fika and remote-helper management
are unsupported. One native BotFirstAid teardown exception remains unattributed
and did not recur in the final sweep. ArenaMapPort's missing Books impact sounds
and sporadic audio report remain documented there; these are not suppressed.

## Package checks

Built from `578f6fa3b1f9786db483f63a4a6a72fddfbaddbc`. ZIP SHA-256: `1450fbe64ab53deb3e75395cf502d6fbf77681bb855900b27af0b880fe7d2d78`. The uploaded ZIP includes the verified Client/Server versions and shared Core 1.0.17. Package contents, binary metadata, source stamps and extracted bytes passed validation.

Fresh checks: 3,398 passed, with three explicit environment-dependent skips. Credential scanning found no confirmed credentials in the release source or payload.

VirusTotal results below apply to these exact binaries (29 October 2026). The five DLLs had no malicious or suspicious labels. The unsigned [Session.exe report](https://www.virustotal.com/gui/file/bba7cb9008472c79fdff9675cb2cec47c9ac6802d03a2f8304e8df7817ef55e0) has **3 malicious labels**, 66 undetected, 2 timeouts and 4 unsupported engines. Labels: VirIT: `Trojan.Win32.MSIL_Heur.A`; Elastic: `malicious (moderate confidence)`; McAfeeD: `ti!BBA7CB900847`. A separate Microsoft Defender scan found no threats. These findings are unresolved; they are not established false positives. No antivirus protection needs to be disabled.

| File | SHA-256 / scan |
|---|---|
| `maschine-ArenaMode.Gate.dll` | [d4c4a4432922…](https://www.virustotal.com/gui/file/d4c4a4432922c6eefed164d4efc405c26d408757f6fa8942aa8da5b2f95fe543) |
| `maschine-Redeploy.Core.dll` | [256632c7d9d6…](https://www.virustotal.com/gui/file/256632c7d9d66751db54660f05d8bb6de55a4ec87feb3e3607f6e1c9b5c70cfe) |
| `maschine-ArenaMode.BigBrainCompat.dll` | [6cdfc239a8b0…](https://www.virustotal.com/gui/file/6cdfc239a8b0a73a4589a2dd8a685585e298bcd76455c7cfa21af10f469461e7) |
| `maschine-ArenaMode.Client.dll` | [b8fd6342d9d9…](https://www.virustotal.com/gui/file/b8fd6342d9d91e162ea1505378cd460702005929f40c815b49f11b793ed527cc) |
| `maschine-ArenaMode.Session.exe` | [bba7cb900847…](https://www.virustotal.com/gui/file/bba7cb9008472c79fdff9675cb2cec47c9ac6802d03a2f8304e8df7817ef55e0) |
| `maschine-ArenaMode.Server.dll` | [5af2e1ec096a…](https://www.virustotal.com/gui/file/5af2e1ec096a15860ea98356effcbb7eec7bf87442a793e403742f3c2599cafd) |
