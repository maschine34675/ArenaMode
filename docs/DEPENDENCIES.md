# Dependencies, integrations and distribution

As of 2026-10-20. This overview describes the reviewed ArenaMode source revision,
not the latest release of each third-party mod. Newer versions are attempted
with a warning and the same mandatory API checks. This is not a blanket
compatibility guarantee. Loading together does not establish compatibility
for every feature.

## Hard requirements

| Component | Requirement |
|---|---|
| SPT 4.1.x | Including the BepInEx/client components supplied with SPT. Current GPU checks used SPT 4.1.6. |
| ArenaMode client and server | Both components and their required data/assets belong in the package. |
| `maschine-Redeploy.Core.dll` | Direct runtime dependency. The current source requires at least 1.0.8 for optional respawn timing diagnostics; older test/build reports may still mention 1.0.6/1.0.7. Ship the version appropriate to the specific Arena build. Do not confuse this library with the complete Redeploy mod. |

The main client declares SPT Core, PFT, SAIN, BigBrain, DynamicMaps, MoreBotsAPI,
AI Limit, Questing Bots, ORBIT, Adaptive Bot Culling, Looting Bots, Remove The Dead,
Smoothie, Continuous Healing, Late To The Party, Classic Movement, StatTrack,
Continuous Load Ammo, Deminvincibility, ISB Special Forces and ISB Aishi as soft load-order dependencies.
The hard Core dependency comes from the assembly reference and `RedeployCore.Init`.
The server metadata declares no additional mandatory third-party mods.
Platform DLLs supplied with SPT do not require separate mod downloads.

Core does not require `Redeploy.Server`. The standalone Redeploy client queries
its `/redeploy/ping` route; Core makes no such server requests. Arena's server
handles its own protected raid ending and profile restoration. Neither
Redeploy.Client nor Redeploy.Server therefore belongs in the Arena package.
See [source review and library conversion](BIGBRAIN-LIBRARY-20261018.md).

Sources: `ArenaMode.Client/Plugin.cs`, `ArenaMode.Client/ArenaMode.Client.csproj`,
`ArenaMode.Server/ModMetadata.cs`, `../Redeploy/Redeploy.Core/CoreVersion.props`.

## Optional features and targeted adapters

| Mod/component | Reviewed contract | Purpose / behavior without it |
|---|---|---|
| SAIN | 4.5.1 or newer; reviewed at that version, newer versions warn | Personalities, search speed, movement and lifecycle safeguards. Native AI is used without SAIN. |
| BigBrain + ArenaMode.BigBrainCompat | 1.5.0 or newer; tested at that version, newer versions warn and undergo runtime checks | Directed pickup pursuit, pursuit after the player's death and escape from fire. Without the Arena companion library, directed pickup control and Molotov fire are unavailable; the private Molotov item stays harmless and normal grenades remain unchanged. Pickups themselves remain usable. BigBrain is also a separate prerequisite of SAIN/PFT. |
| PitFireTeam, client and server | 0.10.0 or 0.10.3 and newer; newer versions warn, 0.10.1/0.10.2 remain excluded | 1–4 invited companions for Tactical (still against five enemies) and PFT Zombies. Solo and independent Team DM do not require PFT. Equator remains excluded from both PFT variants. |
| APBS | Metadata version 2.4.1 or newer; reviewed at that version, newer versions warn | Bot equipment/progression. Without APBS, native SPT generation is used; a missing/old version or invalid API contract leaves APBS's own level selection in place with a warning. |
| ArenaMapPort, client/server/map assets | The optional map must be present and enabled on both client and server; Air Pit Team DM requires the team starts from 0.2.2 or newer | Bay5, Equator and Air Pit. Not required for ordinary SPT maps. |
| Killcam | Current integration target: 1.0.0; API checks rather than a fixed version allowlist | Actual killcam replays; DeathReplay must be enabled. The former KillcamReplay plugin is not accepted. Arena's subsequent team-spectating mode is separate Arena code. |
| DynamicMaps | 1.2.1 or newer; reviewed at that version, newer versions warn | Minimaps and corpse-marker cleanup. Arena's static map previews work without DynamicMaps. |
| Waypoints | No direct Arena dependency; coexistence observed with 1.9.0 | Independent navigation/spawn-display features; do not list it as mandatory for Arena merely because it is part of a common AI-mod setup. |

Optional mods retain their own prerequisites. Third-party SAIN/APBS/PFT/BigBrain
DLLs are not bundled with Arena, as required by `AGENTS.md`.
The [version policy and checks](OPTIONAL-MOD-VERSIONS-20261019.md) explain
minimum versions, warnings and the protection contracts that remain mandatory.
`ArenaMode.BigBrainCompat` is Arena's own code and is always included.
Since its conversion to a library, this DLL registers neither a BepInEx plugin
nor its own plugin GUID. Arena explicitly loads it beside the client DLL and
owns initialization, updates and shutdown. Update both DLLs together, remove
old plugin copies and restart the game.
Without loaded BigBrain, the library remains quietly inactive. Versions older
than 1.5.0 or invalid API contracts safely disable the integration. Newer versions
are attempted with a one-time warning; this is not confirmation of full compatibility.

## Other explicitly considered combinations

The [Factory checks from 2026-10-20](GPU-OPTIONAL-COMPAT-20261020.md) supplement
the offline evidence: Classic Movement/StatTrack were exercised in ordinary raids
before and after protected Arena endings; SVM's SaveLoot/Ignore behavior and basic
CLA loading/respawn paths were checked in-game. Deminvincibility's 1-HP and
Second Chance controls, protected solo-Zombie endings and PFT waves with one
companion also passed. Some timing/UI and replay edge cases remain untested.
This does not certify every map, version or mod combination; the descriptions
below state each adapter's contract.

- **Continuous Load Ammo 1.2.0+:** loading controllers are rebound when Arena
  replaces the player body. Cleanup and delayed completion from the old body
  must not unlock or finish work on the replacement body or a newer loading
  operation. Normal loading, cancellation and saved settings remain intact.
  See [loading controllers and verification limits](CONTINUOUS-LOAD-AMMO-COMPAT-20261019.md).
- **SVM 2.2.3+:** SaveLoot/Ignore shortcuts cannot bypass Arena's protected
  raid ending. Ordinary raids retain SVM's rules; other balance settings are
  not reset. The adapter also checks the actual patch order of the outer
  raid-end callback.
  See [raid-end contract and verification limits](SVM-COMPAT-20261019.md).
- **Deminvincibility 2.2.0+:** immortality, Second Chance and the minimum-one-HP
  clamp yield to native death/respawn rules only for Arena's own local body.
  Damage multipliers, medical and fracture settings remain effective; no
  saved settings are changed.
  See [damage and death contract](DEMINVINCIBILITY-COMPAT-20261019.md).
- **Classic Movement 1.1.3+:** Arena retains native movement states and its own
  movement controls for its bodies, including creation during loading. Ordinary
  raids retain Classic Movement. Legacy `NostalgiaMode=true`, no longer supported
  by that mod's current server component, prevents Arena entry only.
  See [movement states and verification limits](CLASSIC-MOVEMENT-COMPAT-20261019.md).
- **StatTrack 2.1.1+:** Arena shots, hits and kills do not contribute to permanent
  weapon statistics; replacing a body does not finalize the statistics for a raid.
  Existing data and pending ordinary save requests are neither deleted nor
  indiscriminately blocked. Normal insurance/ID changes remain unchanged.
  See [statistics ownership and verification limits](STATTRACK-COMPAT-20261019.md).
- **Continuous Healing 1.6.3+:** player-specific healing continuation and
  cancellation are isolated from Arena bots and retired player bodies. The
  current living player retains Continuous Healing; ordinary raids are unchanged.
  See [healing operations and verification limits](CONTINUOUS-HEALING-COMPAT-20261019.md).
- **Late To The Party 3.1.1+:** only original weapon-pickup sources still owned
  by Arena, and their parts, are excluded from third-party loot discovery/cleanup.
  Collected weapons and ordinary loot retain the other mod's rules.
  See [pickup ownership and verification limits](LATE-TO-THE-PARTY-COMPAT-20261019.md).
- **Looting Bots 1.8.0+:** autonomous search/loot actions are suppressed only for
  exactly bound Arena bots, including both Team DM sides and replacement bots.
  Genuine PFT companions retain their commanded actions. The additional inventory
  cleanup in the reviewed 1.8.1-RC path is also covered. Saved looting settings
  are not changed.
  See [contracts, checks and limits](LOOTING-BOTS-COMPAT-20261019.md).
- **Remove The Dead 4.1.3+:** the shared automatic/manual deletion function is
  paused only in the confirmed Arena game. Arena thus retains control over its
  protected corpses and weapon pickups. Timers, queues and ordinary raids
  remain unchanged.
  See [cleanup protection](REMOVE-THE-DEAD-COMPAT-20261019.md).
- **Smoothie 1.0.8+:** Arena pickup markers use a layer excluded from Smoothie's
  static culling, provided both the main and optic cameras can render it. Only
  the optional GC pause is suppressed during an active Arena raid; physics,
  animations, ragdolls and other optimizations are unchanged. An incompatible
  optional contract produces a warning but does not block Arena. In that case,
  leave Smoothie's memory-cleanup pause disabled.
  See [rendering and GC boundaries](SMOOTHIE-COMPAT-20261019.md).
- **AI Limit 1.9.2+:** creation of its limiter component is suppressed only in the
  exact server-confirmed Arena game, even before Arena's Started event. No global
  settings are toggled and no third-party patches are removed. Ordinary PMC/Scav
  raids retain their original behavior; a subsequent raid on the same map does
  not inherit the restriction. Missing API/hook contracts block Arena only.
  See [AI Limit adapter and native-test limits](AI-LIMIT-COMPAT-20261019.md).
- **MoreBotsAPI 2.1.1+:** Arena uses its own spawn identifiers and a narrow guard
  for PFT companions without an identifier; external revenge persistence and its
  hostility override are excluded for the exact bound Arena raid. The server
  route is not indiscriminately blocked and foreign queues are not cleared.
  Older/incompatible API contracts block Arena only; newer versions warn while
  still requiring a valid contract. This does not certify every dependent
  content mod. See [MoreBots adapter and limits](MOREBOTS-COMPATIBILITY-20261019.md).
- **UIFixes:** the alternative R-key reload path is covered; installed version 6.0.3.
- **ABPS/BotPlacementSystem:** spawn-pump/bot-limit behavior is covered; Arena's
  population ownership prevents additional third-party ambient spawns.
- **AutoCorpseSearch:** Arena's search yields when the standalone mod is loaded.
- **NotificationFilter:** Arena also suppresses replacement quest popups without
  changing the mod's global configuration.
- **BrightLasers:** Arena's brightness/team-color code accounts for the reviewed
  3.0.0 contract and avoids double amplification. This does not establish complete
  native acceptance of the combined installation.
- **EnableLabyrinth:** Arena access and countdown are independent; the other
  mod's additional Scav/key/exit changes are not overwritten.
- **Terkoiz Freecam:** camera ownership is detected to avoid competing controls;
  this is not blanket approval of every camera feature.
- **Fika:** explicitly unsupported. Arena refuses activation; this is more than
  an absence of test coverage.
- **Questing Bots / ORBIT:** Arena entry is provisionally blocked when Questing
  Bots is active or ORBIT is loaded. Ordinary PMC/Scav raids remain unchanged.
  The reviewed Questing Bots version 1.1.0 may remain installed if fully disabled
  in its server configuration; partial F12 switches are insufficient.
  Details: [Arena admission](OPTIONAL-MOD-ADMISSION-20261019.md).
- **Adaptive Bot Culling 1.2.0:** Arena entry is provisionally blocked because the
  mod pauses culled bots and their combat and changes death animations. F12
  switches do not remove every intervention. Ordinary PMC/Scav raids remain
  unchanged. [Source review and visible menu notice](ARENA-AVAILABILITY-20261020.md).
- **ISB Special Forces / ISB Aishi:** loaded client or server components
  provisionally block Arena. Their separate progression stores are not covered
  by the ordinary SPT profile snapshot; Aishi also has item-dependent death
  prevention. MoreBotsAPI support does not isolate these systems. Ordinary
  PMC/Scav raids remain available. Remove the conflicting mod and restart both
  server and game to use Arena. Reviewed SF 2.0.1 and Aishi 2.0.4 source;
  [admission, checks and limits](ISB-ADMISSION-20261020.md).

## Own mods: absorbed or still standalone?

| Own mod | Status | Consequence |
|---|---|---|
| RaidInfoPanels | Fully absorbed; standalone local project/old DLL archived. | No separate package for Arena. |
| CombatSlide | Absorbed and restricted to Arena; standalone archived. | No separate package for Arena. |
| AutoCorpseSearch | Arena-relevant searching when opening a corpse is integrated; the broader standalone mod remains available. | Not needed for Arena; still useful independently for ordinary raids/additional search features. |
| NotificationFilter | Behavioral reference only; Arena has its own narrower implementation, with no external code/DLL absorbed. | Keep the standalone mod; not an Arena prerequisite. |
| DefaultAutoFireMode | Arena now has the corresponding local weapon-draw rule: actual full auto when supported. The standalone burst/double-action fallbacks were not adopted. | No additional Arena download needed. The standalone mod remains separate for ordinary raids and continues applying its own rules when both are installed. |
| Redeploy.Core | Shared library with its own version. | Bundle the appropriate tested Core DLL directly with Arena. |
| Redeploy.Client/Server | Standalone respawn mod for ordinary raids; not absorbed. | Separate product, not a mandatory Arena installation. |
| Killcam | Standalone optional plugin; connected through reflection. | Separate optional package for replays. |
| ArenaMapPort | Standalone optional map project. | Separate map package. |
| UiBridge / diagnostic tools | Development and testing, not an Arena runtime prerequisite. | Exclude from the normal player package. |

UseItemsAnywhere, BrightLasers, ColoredTracers, EnableLabyrinth, SpeedLoader and
EnemyMarkers are not additional prerequisites for the corresponding Arena
features. Do not describe behavioral references or independent implementations
as complete absorption of those third-party mods. Adapted MIT portions from
DragonDen retain their existing attribution; its mod DLL is not required either.

## Release boundaries

The release contains Arena's client, server, own data/assets and exactly one
appropriate Redeploy.Core DLL directly under the plugins directory, byte-identical
to the library in a corresponding Redeploy package. No separate mandatory Core
download is needed. `tools/Package-TestRelease.ps1 -Release` builds from isolated,
committed sources without deploying and verifies tests, archive contents and
checksums. Without that switch it creates a package explicitly labelled as a
local test. Both variants use the same runtime assets and license notices.

Player documentation is in the [README](../README.md) and the
[Forge description with six tabs](FORGE-DESCRIPTION.md). The central
[license overview](../THIRD-PARTY-NOTICES.md) distinguishes Arena's MIT code from
separate image/audio rights. It and the [LICENSE](../LICENSE) are shipped with
the asset notices. Readable documents are under
`BepInEx/plugins/maschine-ArenaMode/docs/`, with UI provenance under `docs/licenses/ui/`.
Building from source requires the adjacent public
[Redeploy repository](https://github.com/maschine34675/Redeploy); its exact source
revision is recorded in the package manifest. Core remains at version 1.0.8.

The 18 previously extracted EFT Arena UI graphics have been replaced in the
current source by eight generated realistic weapon-category images, six original
icons or banners (MIT), and four game-icons.net icons (CC BY 3.0).
The latter are Skull crack, Grenade and Barbed wire by Lorc, and Bowie knife by Skoll.
Sources, generator and provenance records are in the repository. NOTICE, SOURCES
and the MIT license text are embedded in the client and shipped as readable files
under `maschine-ArenaMode/docs/licenses/ui`; the shared CC-BY-3.0 license text is
under `maschine-ArenaMode/docs/licenses/CC-BY-3.0.txt`.
See also `UI-GAME-ICONS-20261016.md` and `UI-ARTWORK-REPLACEMENT-20261016.md`.
Old graphics remain in Git history and earlier packages: replacing them does not
clean that history or grant permission to publish it or other assets.

Further reading: `RAID-INFO-PANELS-REVIEW.md`, `APBS-COMPATIBILITY-REVIEW.md`,
`FRIENDLY-FIRE-REVIEW.md`, `KILLCAM-MODES-REVIEW.md`,
`tools/ArenaNotificationChecks/README.md`, `../Redeploy/docs/RESPAWN-API.md`.
