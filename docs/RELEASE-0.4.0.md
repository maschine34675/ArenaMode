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

See the [versioned changelog](../CHANGELOG.md#040---2026-10-29) for the full list.

## Installation

Extract **ArenaMode-0.4.0.zip** into the SPT root, merging `BepInEx/` and
`SPT_Runtime/`. Close game/server first and preserve customized kits/ladder.
Keep one copy of each Arena DLL and the supplied **Redeploy.Core 1.0.17**.
The same Core is bundled in Redeploy 1.1.0; the standalone Redeploy client/server
are optional. BigBrain, its prerequisites and ArenaMapPort are separate downloads.

Existing F12 settings and personal whitelist choices are preserved. Only new
installations default to Team DM. The old flat plugin/asset layout needs the
migration described in the [README](../README.md#install-or-update).

Direct start is the installed
`BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Session.exe`.
It manages a local SPT installation. A short normal-server start/stop before the
private Arena server is expected preparation, even with rewards off.

## Validation and remaining limits

The [native sweep](RELEASE-SWEEP-20261029.md) exercised representative scenarios
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

Release-asset hashes and any hash-specific scanner results belong to the actual
published bytes. The old 0.3.0 helper's scanner results are historical and must
not be represented as a scan of this build. The helper is unsigned; no antivirus
setting needs to be disabled for installation.

