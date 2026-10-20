# ArenaMode 0.1.2

Changes since 0.1.1, for SPT 4.1.x. Native checks used SPT 4.1.6.

## Changes

- Show why Arena is unavailable directly on the character-selection screen,
  including incompatible mods, missing/outdated Redeploy.Core and failed
  admission checks. The missing-Core notice works without starting the Arena
  runtime; normal PMC/Scav selection remains available.
- Block Arena when Adaptive Bot Culling is loaded. Its AI suspension conflicts
  with Arena participants, and disabling its F12 switches does not remove all
  lifecycle hooks. This does not disable the mod in ordinary raids.
- Continuous Healing 1.6.3+: retain healing for the current local player while
  preventing stale-body callbacks from affecting replacement bodies.
- Late to the Party 3.1.1+: protect Arena-owned original weapon-pickup sources
  from simulated looting; ordinary loot and collected weapons keep normal rules.
- Classic Movement 1.1.3+: retain native movement-state ownership for Arena
  bodies without changing ordinary raids. Nostalgia Mode must be disabled.
- StatTrack 2.1.1+: exclude protected Arena activity from persistent statistics.
- Continuous Load Ammo 1.2.0+: rebind inventory controllers across body changes
  and cancel old work without unlocking or completing a newer operation.
- SVM 2.2.3+: preserve protected Arena endings alongside its loot-retention and
  raid-ending options. Older SVM versions are not supported by this adapter.
- Deminvincibility 2.2.0+: preserve native Arena death and respawn despite
  immortality, Second Chance or its minimum-health rule. Other damage and
  medical multipliers remain active.
- Preserve a protected Arena exit during the delayed initial raid introduction,
  including an early exit before the first Zombie wave.

## Compatibility and verification boundaries

These adapters apply to the protected Arena lifecycle and do not change the
other mods' saved settings. Newer versions are attempted with warnings and API
checks, not guaranteed compatible. Safety-critical adapter failures prevent
Arena entry; nonessential integrations can instead fall back or stay inactive.

Existing support for SAIN, BigBrain, PitFireTeam, APBS, DynamicMaps, ArenaMapPort,
Killcam, MoreBotsAPI, AI Limit, Looting Bots, Remove The Dead and Smoothie remains.
See [the compatibility matrix](DEPENDENCIES.md) for versions and individual limits.
Active Questing Bots, loaded ORBIT and Fika remain Arena blockers.

Native checks covered Continuous Healing and Late to the Party, Classic Movement
and StatTrack, and the combined ammo/health/SVM lifecycle scenarios. These included
ordinary raids, Arena deaths, interrupted magazine work, one-companion PFT Zombies
and protected exits. The final menu checks covered Adaptive Bot Culling, missing
Core, normal PMC/Scav selection and a restored-installation Factory Gun Game raid.
The version-only release build is not another native gameplay test.

Detailed evidence and remaining gaps:

- [Healing and loot](HEALTH-LOOT-COMPAT-INTEGRATION-20261019.md)
- [Movement and statistics](MOVEMENT-STATS-COMPAT-INTEGRATION-20261019.md)
- [Ammo, health and SVM native checks](GPU-OPTIONAL-COMPAT-20261020.md)
- [Arena availability and Adaptive Bot Culling](ARENA-AVAILABILITY-20261020.md)

This is not certification of every option or mod combination. Pending magazine
work during a living body replacement, some narrow asynchronous timing windows,
all Deminvincibility lethal-damage paths and larger PFT squads were not exhaustively
tested. A brief native PerfectCulling exception burst during an early scene exit
and an inactive-menu coroutine error in an ordinary Second Chance control remain
documented observations; no cause in these adapters was established.

## Updating

- Close the game and server, back up the profile and customized configuration.
- Merge the ZIP's **BepInEx** and **SPT_Runtime** folders into the SPT root.
  Replace both Arena client DLLs and the server together; remove duplicate old
  Arena DLLs. Preserve customized kit, ladder and configuration files.
- Keep one copy of the bundled **Redeploy.Core 1.0.8**. Its bytes are identical
  to the published Redeploy 1.0.0 library; standalone Redeploy is not required.
- The bundled BigBrain integration is a library, inactive without BigBrain.
  Optional third-party mods and development/test tools are not included.

The GitHub release records the exact source, ZIP hash, build/test results and
VirusTotal links for every packaged DLL. Older releases remain available.
