# Dependencies, integrations and distribution

Updated 2026-11-13, development source after ArenaMode 0.5.0. This overview describes the reviewed ArenaMode source revision,
not the latest release of each third-party mod. Newer versions are attempted
with a warning and the same mandatory API checks. This is not a blanket
compatibility guarantee. Loading together does not establish compatibility
for every feature.

## Hard requirements

| Component | Requirement |
|---|---|
| SPT 4.1.x | Including the BepInEx/client components supplied with SPT. Current GPU checks used SPT 4.1.6. |
| ArenaMode client and server | Both components and their required data/assets belong in the package. |
| `maschine-Redeploy.Core.dll` | Direct runtime dependency. ArenaMode 0.5.0 bundles/requires **1.0.20**, shared byte-for-byte with Redeploy 1.1.2 (see below). Older releases used their own earlier Core versions. Shared optional-mod body replacement protection arrived in 1.0.9. Ship the exact Core built and verified for the Arena package, keeping one copy. This is a library, not the complete Redeploy mod. |

The main client declares SPT Core, PFT, SAIN, BigBrain, DynamicMaps, Continuous
Healing, Continuous Load Ammo and Recoil Rework Legacy as soft load-order dependencies.
The hard Core dependency comes from the assembly reference and `RedeployCore.Init`.
FOV Fix and Manimal support was introduced in **Core 1.0.11**;
their body-replacement fixes are implemented there for both Arena and standalone
Redeploy. Published Arena 0.2.1's older bundle does not include these additions.
Magazine Check Interrupt's floating-panel correction additionally requires
**Core 1.0.12**; Radar and Simple Crosshair handovers require **Core 1.0.13**.
The 0.5.0 package requires **Core 1.0.20**. Core 1.0.14 added ImpactCue and
Enemy Markers handovers; 1.0.15 added Quick Cancel, PiP Disabler and the bounded
Amand camera correction. Core 1.0.16/1.0.17 also support Redeploy's own-Scav
continuation, and 1.0.18 hands Recoil Rework Legacy and FOV Fix over to that
Scav body; Arena respawns behave as with 1.0.17. Core 1.0.19 made the optional-mod
adapters of absent mods stop unpatching on every admission check
([map-screen admission](reviews/MAP-MENU-ADMISSION-20261031.md)), and 1.0.20 adds the
Raid Review 1.10.0, HollywoodFX 3.0.0 and Hands Are Not Busy 1.7.1 handovers (validated
offline only); a client built from this source refuses Core 1.0.19 or older, so ArenaMode
0.5.0 and Redeploy 1.1.2 ship together. Core keeps its independent version line; the
public Redeploy client/server release is 1.1.2. Keep one copy of the shared library.
Development Core **1.0.23** adds the DERP and Player Encumbrance Bar body handovers.
The development client built with these fixes requires that Core; the published
0.5.0 package retains its original dependency.
The server metadata declares no additional mandatory third-party mods.
Platform DLLs supplied with SPT do not require separate mod downloads.

Core does not require `Redeploy.Server`. The standalone Redeploy client queries
its `/redeploy/ping` route; Core makes no such server requests. Arena's server
handles its own protected raid ending and profile restoration. Neither
Redeploy.Client nor Redeploy.Server therefore belongs in the Arena package.
See [source review and library conversion](reviews/BIGBRAIN-LIBRARY-20261018.md).

Sources: `ArenaMode.Client/Plugin.cs`, `ArenaMode.Client/ArenaMode.Client.csproj`,
`ArenaMode.Server/ModMetadata.cs`, `../Redeploy/Redeploy.Core/CoreVersion.props`.

## Arena session and the mod whitelist

Arena runs only with whitelisted mods: the default list allows SAIN, APBS, ABPS, BigBrain and
Waypoints, ArenaMapPort, Killcam, Redeploy and the other mods of this author (AutoCorpseSearch,
DefaultAutoFireMode, DualSideDoorBreach, LinkedSearchInStash, LowAmmoCue, RaidMenuCleanupFix, StashFilter,
SurroundAudio, UnloadAllMagazines), PitFireTeam, DynamicMaps, UIFixes, WTT-CommonLib, WTT-ContentBackport
(and its patcher), SVM, give-ui, ContinuousHealing, Continuous Load Ammo, Recoil Rework Legacy,
Freecam, DoorDash, Anvil-WebOverlay, ArmorClassIcon,
Caliber Under Ammo Name, Compatibility Highlighter, Trader Modding and Improved Weapon Building,
BetterAmmoLoadingList, BrighterInteriors, DevBalaclavaFix, HealingAutoCancel and SmajlecLights, plus
Fontaine FOV Fix, Manimal Ammo Loading Animations, Increase Look Direction,
Auto Shoulder Swap, Dynamic External Resolution Patch, Player Encumbrance Bar,
Bot Callsigns Reloaded,
Magazine Check Interrupt, Tarkova Cola, Accurate Circular Radar, Simple Crosshair
and Better BepInEx, plus Use Items Anywhere,
ImpactCue (client and Web Config server), Enemy Markers, Fast Grenade Throw 2.0.0,
Manimal's Quick Cancel 2.0.0, HUD Breakout 1.2.0 and PiP Disabler 2.1.0, plus the diagnostic tools
UiBridge, ModProfiler, LogTools and ConsoleToMonitor (each with the version it was reviewed with; a
differing recognized version remains advisory in direct play and new private
sessions). Versions are still attempted, including UIFixes; actual identity,
dependency and protection/API requirements remain mandatory. The list records
ArenaMapPort 0.5.0 (published 2026-11-07; 0.4.0 still works without Iceberg and the crowd
reactions, 0.3.0 also without score monitors, each with a version advisory), PitFireTeam
1.1.1 and Redeploy 1.1.2 on both sides. User policy is preserved.
Its deny list holds the five mods ArenaMode refused by code before the
whitelist existed: ORBIT, Questing Bots, Adaptive Bot Culling, ISB Special Forces and Aishi; that code
was removed on 2026-10-22, so the deny list alone refuses them. They are never loaded in an Arena
session, and a user `allow` entry is what lifts that. The platform plugins
and ArenaMode are always allowed, and Fika is never filtered. Everything else is not loaded in an
Arena session (a separate game process with a preloader gate, against a second SPT server with a
private tree of the allowed server mods). The shipped list is `arena-whitelist.json`; a user's own
additions and exclusions are
`BepInEx/config/com.maschine.ArenaMode.whitelist.json` and are never shipped. That file is a combined
allow/deny list: with `"unlisted": "allow"` it turns the whitelist into a blacklist (every mod neither
list denies loads). A denied mod is never pulled in as a hard dependency: an allowed mod that needs one,
directly or through other mods, is excluded with it (reason `requires-denied:<guid>`, listed under "Blocked for
Arena" in the refusal), so whoever wants such a mod has to allow the library. A library nobody denied is still
pulled in for the mods that need it, with a warning. For server mods the hard dependencies are their
`ModDependencies`, which the server's ping reports, because the server refuses to load any mod when one is missing.
The optional adapters below therefore apply to mods that are allowed to load: in the default Arena
session the adapters of installed mods on the default list (for example PitFireTeam, DynamicMaps,
ContinuousHealing, Continuous Load Ammo, Recoil Rework Legacy, SVM, Killcam) are active.
The two newly listed clients were approved after the shared-Core GPU checks on 2026-10-23;
their existing Core version/API checks still apply. Recoil's server package shares its GUID
and is therefore also admitted if installed, but was not part of those checks. See
[the session specification](ARENA-SESSION-20261021.md) and the [README](../README.md).

## Optional features and targeted adapters

| Mod/component | Reviewed contract | Purpose / behavior without it |
|---|---|---|
| SAIN | 4.5.1 or newer; reviewed at that version, newer versions warn | Personalities, search speed, movement and lifecycle safeguards; no SAIN extraction in an Arena raid. Native AI is used without SAIN. |
| BigBrain + ArenaMode.BigBrainCompat | 1.5.0 or newer; tested at that version, newer versions warn and undergo runtime checks | Domination (required: without BigBrain and the companion only Domination is locked), directed pickup pursuit, pursuit after the player's death and escape from fire. Without the Arena companion library, directed pickup control and Molotov fire are unavailable; the private Molotov item stays harmless and normal grenades remain unchanged. Pickups themselves remain usable. BigBrain is also a separate prerequisite of SAIN/PFT. |
| PitFireTeam, client and server | 0.10.0 or 0.10.3 and newer; reviewed adapter baselines: 0.10.0, 0.10.3, 1.0.0, 1.1.1 (1.1.x binds three more members, gated on the installed version: the served spawn-settings preflight, the six-argument kill report, two stop resets; see [the 1.1.1 review](reviews/PITFIRETEAM-111-20261107.md)). Current whitelist review: 1.1.1. Other admitted builds warn; 0.10.1/0.10.2 remain excluded | 1–4 invited companions for Tactical (still against five enemies) and PFT Zombies. Solo and independent Team DM do not require PFT. Equator and Chop Shop remain excluded from both PFT variants; Iceberg from PFT Tactical (PFT Zombies is admitted). |
| APBS | Metadata version 2.4.1 or newer; reviewed at that version, newer versions warn | Bot equipment/progression. Without APBS, native SPT generation is used; a missing/old version or invalid API contract leaves APBS's own level selection in place with a warning. |
| ArenaMapPort, client/server/map assets | The optional map must be present and enabled on both client and server; Air Pit Team DM requires the team starts from 0.2.2 or newer; Iceberg requires ArenaMapPort 0.5.0 (published 2026-11-07; 0.4.0 has none, and Arena detects Iceberg by the live location, never by version) | Bay5, Equator, Air Pit, Bowl, Iceberg and Chop Shop (`arena_autoservice`, which needs an ArenaMapPort build that ships it: today only the unreleased `feature/autoservice` build 0.5.0+b7876d3, since the published 0.5.0 has none and the same version string; Arena detects it by the live location, never by version). Independent Team DM and Domination use their team starts; Bowl supports 2v2–10v10 from its east/west boxes, Iceberg 2v2–10v10 from its USEC west / BEAR east pockets. Chop Shop supports 2v2-10v10 from its USEC east / BEAR west pockets. Iceberg and Chop Shop exclude Tactical, Chop Shop also PFT Zombies. Other Bowl, Iceberg and Chop Shop modes use generic spawns. Without a build that ships Iceberg or Chop Shop, Arena simply does not offer that map. Not required for ordinary SPT maps. |
| ArenaMapPort SpectatorCrowd contract (client) | Contract version 1, optional, bound by exact signatures in two independent parts: the murmur intensity (`SetExcitement`, published in 0.4.0) and the crowd reactions (`ReactionKinds`, `ClaimReactions`, `React`, `ReleaseReactions`; phase A of ArenaMapPort's plan, published in ArenaMapPort 0.5.0 on 2026-11-07 together with the roar layer; kinds 1 to 5, mask 62, Lull 6 and PurifiedTeam 7 reserved; owners up to 64 characters, ArenaMode's 40 pass unchanged) | F12 Audio `CrowdReactions`. With 0.4.0 only the murmur follows the match; 0.5.0 plays the reactions and the roar. Absent members are an older ArenaMapPort (silent), changed members switch their part off with one warning. A refused `React` (also MapPort's withdrawal of the kinds mid-match through its developer key `Spectators/Reactions`) ends the reactions for that match; the next match claims anew. Nothing in a match depends on it. ArenaMapPort's roar layer (`e3e4290`, 2026-11-07: each played reaction roars one of Arena's Cheer beds for Approve, Start and Finish or an Angry bed for Disapprove from every stand, the other kind cut in 0.5 s; contract unchanged) is why ArenaMode sends one mood per flush and holds a 1 s mood window. Offline check against the installed client: the real-surface stage of `tools/CrowdReactionChecks`, which fails without a phase A client and reports whether the roar layer is present. See [the contract](reviews/CROWD-REACTIONS-20261102.md). |
| Killcam | Current integration target: 1.0.0; API checks rather than a fixed version allowlist | Actual killcam replays; DeathReplay must be enabled. The former KillcamReplay plugin is not accepted. Arena's subsequent team-spectating mode is separate Arena code. |
| DynamicMaps | 1.2.1 or newer; reviewed at that version, newer versions warn | Minimaps and corpse-marker cleanup. Arena's static map previews work without DynamicMaps. |
| Waypoints | No direct Arena dependency; coexistence observed with 1.9.0 | Independent navigation/spawn-display features; do not list it as mandatory for Arena merely because it is part of a common AI-mod setup. |

Optional mods retain their own prerequisites. Third-party SAIN/APBS/PFT/BigBrain
DLLs are not bundled with Arena, as required by `AGENTS.md`.
The [version policy and checks](reviews/OPTIONAL-MOD-VERSIONS-20261019.md) explain
minimum versions, warnings and the protection contracts that remain mandatory.
`ArenaMode.BigBrainCompat` is Arena's own code and is always included.

Domination on Factory and the six ported maps requires this library's verified
objective layer. Missing BigBrain/support locks only Domination in the mode picker;
other modes retain their existing optional dependency rules. See
[Domination options and scope](reviews/DOMINATION-OPTIONS-ROUTES-20261027.md).
Since its conversion to a library, this DLL registers neither a BepInEx plugin
nor its own plugin GUID. Arena explicitly loads it beside the client DLL and
owns initialization, updates and shutdown. Update both DLLs together, remove
old plugin copies and restart the game.
Without loaded BigBrain, the library remains quietly inactive. Versions older
than 1.5.0 or invalid API contracts safely disable the integration. Newer versions
are attempted with a one-time warning; this is not confirmation of full compatibility.

## Other explicitly considered combinations

- **Caliber Under Ammo Name 1.2.0** adds caliber labels and can sort ammunition
  by caliber. The release tag is `1.2.0b`; the client DLL reports `1.2.0`.
  Its Amand's Sense integration is optional. [Release](https://github.com/slpf/CaliberUnderName/releases/tag/1.2.0b)
- **Compatibility Highlighter 1.3.2** highlights compatible items and slots
  during inventory hover and drag. [Release](https://github.com/awnova/CompatibilityHighlighter/releases/tag/v1.3.2)
- **Trader Modding and Improved Weapon Building 2.1.2** needs its client and
  server components for trader and flea prices in the preset editor. Quick Buy
  and item transfers use the normal out-of-raid inventory transactions.
  Arena's Tactical buy menu is separate. [Release](https://github.com/Soulztorm/ChooChoo-TraderModding/releases/tag/2.1.2)

The current whitelist includes Fast Grenade Throw 2.0.0, Quick Cancel 2.0.0,
HUD Breakout 1.2.0 and PiP Disabler 2.1.0 after bounded Arena and standalone
tests. The same final Core passed two additional Factory respawns with Arena
absent, including a scoped death, old PiP-cache retirement, current-body HUD and
graphics bindings, native medical cancellation and an RGD throw after respawn.
See the [remaining-mod report](reviews/REMAINING-MOD-COMPAT-20261026.md) for exact
exercises, source contracts and limitations. Amand's Graphics was admitted
separately on 2026-11-08; Amand's Sense remains unlisted.

The [Factory checks from 2026-10-20](reports/2026-10/GPU-OPTIONAL-COMPAT-20261020.md) supplement
the offline evidence: SVM's SaveLoot/Ignore behavior and basic CLA
loading/respawn paths were checked in-game. Some timing/UI and replay edge cases
remain untested. This does not certify every map, version or mod combination; the
descriptions below state each adapter's contract. That document also records tests
of adapters that were removed on 2026-10-22 (see the last bullets of this list).

The following Recoil Rework Legacy, Continuous Load Ammo and Continuous Healing
contracts are implemented by Redeploy.Core since 1.0.9 (0.2.0 ships 1.0.10), also for standalone Redeploy.
Arena supplies only its explicit additional medical scope for Arena bots. The
native observations below predate that migration; see
[shared implementation and new verification boundaries](reviews/SHARED-BODY-COMPAT-20261023.md).

- **Recoil Rework Legacy 2.0.0+:** the two foreign recoil/camera singleton
  components are transferred across Arena player replacements without destroying
  either player object. Weapon-controller caches follow the physically equipped
  weapon; retired bodies cannot update recoil. Failed replacements retain the
  old components for Arena's recovery. Ordinary raids and saved recoil settings
  are unchanged. Native tests covered supplied kits, two death respawns, living
  and dead PFT wave refills, actual firing afterward and ordinary raids before
  and after Arena. Recovery failures, Akimbo and the full camera/perk matrix
  remain untested. See [adapter and remaining checks](reviews/RECOIL-REWORK-COMPAT-20261020.md).
- **Continuous Load Ammo 1.2.0+:** loading controllers are rebound when Arena
  replaces the player body. Cleanup and delayed completion from the old body
  must not unlock or finish work on the replacement body or a newer loading
  operation. Normal loading, cancellation and saved settings remain intact.
  See [loading controllers and verification limits](reviews/CONTINUOUS-LOAD-AMMO-COMPAT-20261019.md).
- **SVM 2.2.3+:** SaveLoot/Ignore shortcuts cannot bypass Arena's protected
  raid ending. Ordinary raids retain SVM's rules; other balance settings are
  not reset. The adapter also checks the actual patch order of the outer
  raid-end callback.
  See [raid-end contract and verification limits](reviews/SVM-COMPAT-20261019.md).
- **Continuous Healing 1.6.3+:** player-specific healing continuation and
  cancellation are isolated from Arena bots and retired player bodies. The
  current living player retains Continuous Healing; ordinary raids are unchanged.
  See [healing operations and verification limits](reviews/CONTINUOUS-HEALING-COMPAT-20261019.md).
- **UIFixes:** the alternative R-key reload path is covered; installed version 6.0.3.
- **Fontaine FOV Fix 4.1.0+:** Core 1.0.11 transfers the persistent camera controller
  to the replacement body and fences retired-body aim/weapon callbacks. Reviewed
  whitelist version: 4.1.0. Newer versions still require the reflected APIs.
- **Manimal Ammo Loading Animations 2.0.0+:** Core 1.0.11 removes retired owned
  player sessions and fences delayed animation/controller/loading work. Reviewed
  client/server whitelist version: 2.0.0. WTT CommonLib and the mod's own dependency
  requirements still apply; the native checks include Continuous Load Ammo 1.2.0.
  See [four-candidate review and verification boundaries](reports/2026-10/FOUR-MOD-COMPAT-20261025.md)
  for both additions and the initial exclusions. The subsequent Fast Grenade
  Throw and Tarkova Cola integrations are described below.
- **Tarkova Cola 2.2.1+:** both client and server are admitted with checked
  lifecycle, storage and achievement-catalog contracts. Arena protects ordinary
  progression and restores foreign state after play. Completed Zombie matches
  can transfer research and earned achievements through the existing protected
  reward path; bottle/grenade refunds are excluded. Other Arena modes retain
  temporary perks without exporting Cola progression. Install Tarkova Cola
  separately; its optional VisitAPI storyline is outside this integration.
  See [scope, payout and recovery](TARKOVA-COLA-COMPAT.md).
- **ABPS/BotPlacementSystem:** spawn-pump/bot-limit behavior is covered; Arena's
  population ownership prevents additional third-party ambient spawns.
- **Increase Look Direction 1.3.0:** no body-replacement adapter is needed. When
  Fontaine FOV Fix also handles armed free-look, its **Free Look Angle** takes
  precedence over this mod's horizontal limits. The experimental camera lift
  remains the mod's own option, disabled by default.
- **Magazine Check Interrupt 1.1.1+:** Core 1.0.12 retires the old body's floating
  magazine panel and fences delayed hide callbacks after replacement. The
  custom UIFixes Reload in Place animation does not receive Arena's reload-speed
  multiplier. Reviewed whitelist version: 1.1.1.
- **Auto Shoulder Swap 1.1.1:** Arena guards its unsafe knife/non-firearm and
  incomplete-scope callbacks while preserving native lean. The upstream bug
  remains outside Arena. Reviewed GUID: `hazelify.StanceSync`.
- **Dynamic External Resolution Patch 1.1.2:** Core 1.0.23 hands its private
  player cache to the replacement and fences retired optic callbacks.
- **Player Encumbrance Bar 1.3.0:** Core 1.0.23 binds inventory and skill events
  to the replacement body. Arena also supplies its effective carry limits to
  the displayed thresholds. Reviewed GUID: `com.mpstark.PlayerEncumbranceBar`.
- **Bot Callsigns Reloaded 2.0.6:** server-side USEC/BEAR naming lists; no
  player lifecycle adapter. Reviewed GUID: `com.harmonyzt.botcallsigns`.
  See [the four-mod contracts](reviews/FOUR-MOD-COMPAT-20261113.md) and
  [scoped native checks](reports/2026-11/GPU-FOUR-MOD-COMPAT-20261113.md).
- **Accurate Circular Radar 1.3.4 / Simple Crosshair 1.4.0:** Core 1.0.13
  preserves their HUDs and rebinds physical player/camera references. Late old
  bodies cannot remove the replacement crosshair. Radar colors remain faction
  and role colors; they do not establish team hostility.
- **Use Items Anywhere 2.1.6:** its configured slots own reach, binding and
  magazine selection whenever installed. Arena does not widen those slots,
  including when UIA's features are disabled. Arena's magazine HUD reads UIA's
  current query. Without UIA, Arena's own extension remains available.
- **ImpactCue 2.0.3 / Enemy Markers 1.4.0:** Core 1.0.14 transfers physical-body
  telemetry and billboard cameras after replacement. Existing combat history
  and bot markers are retained; local replacement bodies receive no enemy marker.
  These mods retain their own HUD and marker settings, so overlapping HUDs may
  need configuring in the respective mods. See
  [native tests and limits](reviews/ITEMS-HUD-COMPAT-20261025.md).
- **Fast Grenade Throw 2.0.0:** the Arena adapter respects the
  current player's grenade priority within the effective candidate list and
  blocks direct quick-throw input during Arena input locks/replacement.
  Native priority Molotov, underhand RGD and inventory-blocked inputs passed.
  No body adapter is needed; ordinary raids retain FGT's behavior.
- **Manimal's Quick Cancel 2.0.0:** Core (since 1.0.15) retires only the
  old body's pending medical controllers and rejects their late callbacks.
  Actual cancellation and firearm recovery after replacement passed; the rare
  pending-drop/death race is covered offline, not established by that native run.
- **HUD Breakout 1.2.0:** no new body adapter. Native health, ammunition and
  grenade displays followed two Arena respawns. Its grenade count uses rig/pockets
  rather than Arena's expanded backpack reach, and its spectator HUD describes
  the local player. Shield, assist, damage-number and killfeed semantics remain
  those of the foreign mod; both HUDs can overlap.
- **Picture in Picture Disabler 2.1.0:** Core (since 1.0.15) bounds optic
  callbacks and generated mesh caches to their physical player/weapon owners.
  Arena additionally isolates its cold-kit preloads. Two actual scoped Arena
  deaths and a newly held scope's rendering passed with Fontaine FOV Fix 4.1.0.
  Coverage does not extend to every optic, NVG/thermal mode or callback race.
- **Amand's Graphics 1.8.0:** reviewed in the development whitelist. Arena uses
  Labyrinth's actual lighting settings when Graphics cannot find its usual
  scene object. The initial kit and two deaths on Labyrinth passed, including
  one with graphics switched off. [Native test and scope](reports/2026-11/GPU-AMANDS-20261108.md)
- **Amand's Sense Updated 3.1.0:** development Core 1.0.22 preserves Sense
  markers and the current inventory across a body replacement. The mod remains
  outside the default Arena whitelist. [Test and scope](reports/2026-11/GPU-AMANDS-20261108.md)
- **Better BepInEx 1.0.0:** normal client mod, not a BepInEx core replacement.
  Its existing ConfigurationManager dependency must remain installed. Other
  mods' stock F12-window patches may be removed by Better BepInEx itself.
  See [reviewed artifacts and native test scope](reviews/HUD-MOD-COMPAT-20261025.md).
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
- **Questing Bots, ORBIT, Adaptive Bot Culling, ISB Special Forces / ISB Aishi:**
  refused only by the shipped default deny list of the Arena session (never
  loaded there); an `allow` entry in the user's own list lifts that, at the
  user's risk. Arena has no code specific to these mods any more: their former
  per-mod admission checks were removed on 2026-10-22. ISB's progression stores
  are not covered by the ordinary SPT profile snapshot, Aishi has item-dependent
  death prevention, and Adaptive Bot Culling pauses culled bots and changes death
  animations. Ordinary PMC/Scav raids remain unchanged. The historical reviews:
  [Arena admission](reports/2026-10/OPTIONAL-MOD-ADMISSION-20261019.md),
  [Adaptive Bot Culling](reviews/ARENA-AVAILABILITY-20261020.md),
  [ISB admission](reports/2026-10/ISB-ADMISSION-20261020.md).
- **Removed adapters (2026-10-22):** MoreBotsAPI, AI Limit, Looting Bots, Remove The
  Dead, Smoothie, Late To The Party, Classic Movement, StatTrack, Deminvincibility,
  Skills Extended, Pause Continued and EFT Effects Manager / Broken Limbs Don't Heal.
  The whitelist is the one mechanism that keeps foreign mods away from Arena, and
  an allowed mod runs unadapted. Their reviews, contracts and test results in
  this directory are dated records of what was once true. One caveat of that
  period stays relevant: Skills Extended's server enum extension persists two
  custom skill entries, so removing that mod can leave profiles unloadable without
  appropriate cleanup.

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
to the library in a corresponding Redeploy package. The Arena session adds three
own files, built from the same revision: the preloader gate
`BepInEx/patchers/maschine-ArenaMode/maschine-ArenaMode.Gate.dll`, the helper
`BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Session.exe` and the default
list `BepInEx/plugins/maschine-ArenaMode/arena-whitelist.json`. No separate mandatory Core
download is needed. `tools/Package-TestRelease.ps1 -Release` builds from isolated,
committed sources without deploying and verifies tests, archive contents and
checksums. Without that switch it creates a package explicitly labelled as a
local test. Both variants use the same runtime assets and license notices.

Player documentation is in the [README](../README.md) and the
[Forge description with tabs](FORGE-DESCRIPTION.md). The central
[license overview](../THIRD-PARTY-NOTICES.md) distinguishes Arena's MIT code from
separate image/audio rights. It and the [LICENSE](../LICENSE) are shipped with
the asset notices. Readable documents are under
`BepInEx/plugins/maschine-ArenaMode/docs/`, with UI provenance under `docs/licenses/ui/`.
Building from source requires the adjacent public
[Redeploy repository](https://github.com/maschine34675/Redeploy); its exact source
revision is recorded in the package manifest. ArenaMode 0.5.0 bundles Core
1.0.20, identical to the library in Redeploy 1.1.2. See
[release notes](RELEASE-0.5.0.md) and [native coverage](reports/2026-10/RELEASE-SWEEP-20261029.md).

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
