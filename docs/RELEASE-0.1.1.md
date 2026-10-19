# ArenaMode 0.1.1

Changes since 0.1.0, for SPT 4.1.x. Native checks used SPT 4.1.6.

## Changes

- MoreBotsAPI 2.1.1+: protect Arena spawn identities, team relationships and
  persistent revenge state without changing ordinary raids.
- AI Limit 1.9.2+: leave its bot-limiter component inactive for the exact Arena
  match so it cannot suspend participants.
- Looting Bots 1.8.0+: pause autonomous looting for Arena combatants while
  preserving authenticated PitFireTeam companion commands.
- Remove The Dead 4.1.3+: pause manual and automatic corpse removal while
  Arena owns the match's bodies and weapon pickups.
- Smoothie 1.0.8+: retain other optimizations, protect Arena pickup markers
  from static culling where a camera-visible excluded layer is available, and
  suppress its optional memory-cleanup pause during the match.
- Active Questing Bots and loaded ORBIT now block Arena selection/start;
  ordinary PMC/Scav raids remain available. Questing Bots 1.1.0 may remain
  installed when fully disabled in its server configuration before startup.
  Individual F12 switches are not sufficient.
- Fix pickup labels failing to initialize when the marker root was hidden
  before TextMeshPro initialization.
- Allow slow walking while splinting a leg, under the same Fast Movement
  conditions and 1 m/s cap as surgery.
- Keep failed skill restoration isolated and retryable instead of releasing
  Arena's restoration protection early.
- Match the four-kill HUD text to the existing Ultra Kill announcement.
- Bind the BigBrain companion's exact client-version check to the compiled
  client version, so a release bump cannot leave a stale required version.

## Compatibility and verification boundaries

The new adapters apply only to the exact protected Arena world and do not edit
the other mods' saved settings. Newer optional-mod versions are attempted with
a warning and runtime API checks; this is not a future-version guarantee.
Safety-critical adapter failures prevent Arena entry. Smoothie is optional:
if its adapter is unavailable, leave its optional memory-cleanup pause disabled.

Native Factory Deathmatch and ordinary-PMC checks covered Looting Bots 1.8.0,
Remove The Dead 4.1.3 and Smoothie 1.0.8. The pickup correction was checked in
the game at commit `2624bef`, before the release-only version/documentation
change. The detailed scope is in
[the native compatibility report](GPU-OPTIONAL-COMPAT-20261019.md).

MoreBotsAPI, AI Limit and Black Division have not received native acceptance
in this test series. Black Division is not blocked, but is not certified
compatible. PitFireTeam-command preservation and Looting Bots' RC-specific
cleanup guard were checked offline, not in these native tests. Remove The Dead's
ordinary-PMC timer ran without a dead-body candidate; actual normal-raid corpse
removal was therefore not observed. The leg-splint walking extension has offline
checks but no separate native acceptance yet.

One pre-existing bot medical-operation null-reference error occurred during
raid exit. Its origin remains unresolved; the test report does not claim an
error-free exit or attribute it to a particular mod.

## Updating

- Replace both Arena client DLLs and the Arena server from the same ZIP.
- Keep one copy of the bundled Redeploy.Core 1.0.8 library. Its release bytes
  remain identical to the published Redeploy 1.0.0 package.
- Preserve customized kit/ladder/config files. The ZIP contains only the
  installation folders, runtime assets and required documentation/notices;
  temporary compatibility-test mods are not included.
- Close the game and server before replacing files. See the
  [installation instructions](../README.md#installation) for the folder layout.

The GitHub release records the final ZIP hash, build/test results and a
VirusTotal link for every packaged DLL. Packaging itself is not another
in-game test. Version 0.1.0 remains available as the previous release.
