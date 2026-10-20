# Arena admission with other AI mods

Scope requested on 2026-10-19: permit MoreBotsAPI and AI Limit through scoped
adapters; temporarily reject active Questing Bots and loaded ORBIT for Arena.
Ordinary PMC/Scav raids retain the other mods' original behavior, including their
own prerequisites, limitations and incompatibilities with each other.

## Admission boundary

`ArenaOptionalMods` reads loaded plugin identities. `ArenaMenuSelection` holds
its process-lifetime rejection provider separately from screen-flow delegates.
The character screen, normal eligibility, pending mode eligibility and final
freeze all consult it. The native local-start prefix checks it only after its
ordinary-raid return, then checks again after remote settings refresh, before
arming profile protection. Closing/reopening setup cannot lose the guard.
Failures reject Arena with a visible explanation; they do not disable the Arena
plugin, change another mod's configuration or reject ordinary PMC/Scav starts.
There are no BepInEx incompatibility declarations, only soft load-order entries.

Update 2026-10-20: loaded Adaptive Bot Culling also blocks Arena; see
[published-DLL review and persistent menu explanations](ARENA-AVAILABILITY-20261020.md).
Earlier references to a visible reason described the rejection/toast path;
disabled controls now display that reason without needing a click.

- **Questing Bots 1.1.0**, source `313d0275e07e84566c3676cc6f194612306026cd`,
  GUID `com.danw.questingbots`: block its active systems. Only the reviewed
  whole-mod server configuration `enabled: false` at startup is exempt. Read
  the cached `_currentConfig.Enabled`, never the lazy `CurrentConfig` getter
  that can issue HTTP. An already-installed gameplay Harmony patch invalidates
  the exemption even when the flag is false; the disabled path's MenuShowPatch
  is harmless. Once active/unknown is observed, a later flag change cannot
  prove that generators, layers and patches disappeared. Restart is required.
  Unknown versions/contracts stay blocked, including their unverified off-switch.
  F12 questing, spawning or AI-limiter switches are not sufficient.
- **ORBIT 2.1.1**, source `428b861393fabed7053f9e358464f2654b11fa88`, GUID
  `com.chazut.orbit`: loaded plugin blocks Arena. Its delayed initialization,
  faction exclusions and Ghost switch do not provide a complete per-raid pause
  for extraction, behavior and virtualization. Absence is checked live, not
  cached at Arena startup.

The source pins describe the reviewed releases, not a claim that every future
release retains these implementations. No foreign code or DLL is shipped.

Sources: [Questing Bots](https://sp-mod.com/mod/1109/questing-bots),
[ORBIT](https://sp-mod.com/mod/2706/orbit-20).

## Checks and limits

- `NativeMenuChecks`: 752 assertions, including real production menu/start
  hooks, changes between selection and start, changes during settings refresh,
  failed probes, visible reasons, persistent admission after EndFlow, and
  ordinary PMC/Scav starts that never even invoke the optional admission probe.
  Also covers failed MoreBots binding before kit issue and exact-arm cancellation.
- `OptionalModAdmissionChecks`: 48 production-source assertions with controlled
  discovery/configuration/Harmony boundaries, plus eight compiled fault controls.
  Covers disabled/active/partial/unknown Questing Bots, every Harmony patch kind,
  sticky activation, delayed ORBIT presence and rejection propagation across
  MoreBotsAPI, AI Limit, Looting Bots, Remove The Dead and Smoothie adapters.

These are offline behavioral tests, not native combined-mod acceptance. Before
claiming native compatibility, restart with the actual third-party plugins and
check ordinary PMC -> Arena -> ordinary PMC/Scav in one process. Verify blocked
Arena UI and stale-selection rejection with each blocker separately, and
Questing Bots' whole-mod-disabled exception after a cold restart. Other mods'
own global compatibility restrictions are not bypassed.
