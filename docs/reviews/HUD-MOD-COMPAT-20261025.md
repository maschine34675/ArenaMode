# HUD mod compatibility — 2026-10-25

This review covers the released SPT 4.1 packages below. The original mods remain
separate downloads; Arena does not ship their DLLs, assets or configuration.

| Mod | Reviewed release | Client GUID | Source revision |
|---|---|---|---|
| [Accurate Circular Radar](https://sp-mod.com/mod/1100/accurate-circular-radar) | [1.3.4](https://github.com/Leonana69/Tyrian-Radar-Standalone/releases/tag/1.3.4) | `com.leonana69.radar` | `8b58dde7b681b37c7dad70153f7b212c64d7da0a` |
| [Simple Crosshair](https://sp-mod.com/mod/1387/simple-crosshair) | [1.4.0](https://github.com/DrakiaXYZ/SPT-SimpleCrosshair/releases/tag/1.4.0) | `com.mpstark.SimpleCrosshair` | `4c5ee788d6b31cd3ac45dc44b589a576b7472382` |
| [Better BepInEx](https://sp-mod.com/mod/3207/better-bepinex) | [1.0.0](https://github.com/JoelHauser/BetterBepInEx/releases/tag/v1.0.0) | `com.mybutthasarash.betterbepinex` | `a0fe0d5e5caf5327559ab60d0b7907f7509c50bc` |

All three release archives matched their published GitHub SHA-256 digest.
The shipped DLLs were decompiled and compared with the corresponding tagged
source. Source compatibility and native acceptance are recorded separately.

## Body and camera lifetimes

Radar parents its HUD to the FPS camera, which Core replaces during respawn.
It also caches the local player in the radar, loot and extraction trackers and
keeps the original camera and compass presentation. Recreating the whole mod
is unsafe: its loot tracker has anonymous container subscriptions and its
static price initialization is not designed to restart. Its missing-HUD
fallback can even destroy the GameWorld object when the radar is toggled.

Simple Crosshair unregisters the current HUD for every local player's
unregister callback. A retired body can be disposed again after the new HUD
exists, removing the successor's crosshair. Its event cleanup also adds an
interaction handler instead of removing it and reads the current player rather
than the subscribed body. These are body-replacement issues shared with
standalone Redeploy, so their fixes belong in Redeploy.Core.

Radar contact colors denote factions and bot roles, not Arena hostility.
In particular, a same-faction FFA opponent is still an enemy. This review does
not add a team-color integration or certify long-session radar performance.
Simple Crosshair is independent of Arena's hitmarker/HUD mutator; both may be
visible at the same time.

Radar 1.3.4 also installs its loot-add, loot-remove and shot hooks anew for every
raid: the second native raid had two copies. Its source confirms repeat work,
not duplicated game items: add replaces the same loot blip, remove is idempotent,
and shot updates write the same frame time. Work grows across raids, especially
with loot display enabled; no FPS impact was measured. Core does not replay that
initialization during respawn. This upstream issue is documented, without a
global hook rewrite that would also change ordinary raids.

## Better BepInEx

The package contains one normal client plugin. It neither replaces BepInEx core
nor includes a preloader. It requires the existing ConfigurationManager plugin;
keep that plugin installed. It is an ordinary reviewed mod in Arena policy,
not a protected platform component.

The mod retains no EFT player or camera. It edits the same BepInEx ConfigEntry
objects as the stock menu. A respawn adapter is not justified solely by its
source. Its global input-lock restoration was checked across respawn and an
overlapping inventory menu, as recorded below.

At startup, the new menu removes other mods' Harmony hooks on
ConfigurationManager `OnGUI` and `SettingsWindow`. No Arena hook targets these
methods. Other F12 resize/restyle mods may lose their behavior; switching the
new menu off does not restore their hooks until a restart. This is an upstream
interaction, not something Arena can promise to preserve.

## Artifact identities

| DLL | SHA-256 |
|---|---|
| Tyrian-Radar.dll | `F295449E2BC5DED84A3E89E1BA759790D395BFD83506DDDE287CCEEFDCDA5171` |
| SimpleCrosshair.dll | `21880A9FCEE185AAAC10023564454549F4310CF66F76700BB21542E1D4372EF2` |
| BetterBepInEx.dll | `D8FE5576B7FD7FA21F32B8D3E006545E3AE8A7BDCC85D15BFC37ED21E1AB640D` |

The archived local ignored directory
`artifacts/archived-worktrees/compat-hud-20261025/artifacts/compat-hud`
holds package identities,
tagged source, release decompilations and detailed source reviews. It must not
be included in release ZIPs.

## Native acceptance on SPT-GPU-2

On 2026-10-25 the baseline Core 1.0.12 reproduced both HUD losses at Arena
Deathmatch's initial living kit replacement: zero Radar and zero Crosshair
components remained. Core **1.0.13+d49426a** corrected that same path. Its loaded
MVID `8293a5b4-edc9-43e1-baeb-df2ef805de6f` and DLL SHA-256
`E14797ECF44B72A93290921989CBD85AE9CFFB08F7AC2C40016791F21F9A2566`
matched the candidate file, and all three foreign DLL identities matched the
table above. Arena used client `0.2.1+0ab7c44` during this isolated Core test.

- Initial kit swap and two death respawns retained exactly one Radar and one
  dynamic Crosshair. Each referenced the actual current player and FPS camera,
  not merely another object with the same profile ID.
- Radar could remain disabled across the second death and be enabled afterward.
  No missing-HUD destruction or inactive-coroutine error occurred. Contacts
  followed the new raid; retained physical corpses still use upstream behavior.
- Exiting the first raid removed both components. A second Factory Deathmatch
  created fresh components with correct body/camera references and two contacts
  for two initial bots. Crosshair event inspection found one own handler per
  current hands, interaction, sense and aiming event, with vanilla handlers intact.
- Better BepInEx displayed/search-filtered Arena settings. Leaving it open across
  death, then closing its X, restored movement and firing. Opening it over the
  inventory and closing X preserved the inventory's input lock; Tab then released
  that lock normally.

The short automated F12 **close** keypress was not reliably observed by the
game, so acceptance uses the visible X close path. No standalone Redeploy raid,
PFT squad, compass layout or long-duration performance test was performed here.
Core is shared by both consumers, but this is bounded Arena native evidence.

The warning/error review found no Radar, Crosshair or Core exception. It still
contained Factory spawn-marker corrections, missing effect/weapon-event messages
and `reward-snapshot-not-observed` with rewards disabled for these tests; those
messages are not evidence of a new HUD failure. Reward settlement was outside
this test matrix.

Offline verification: **3,197 Arena tests passed**, zero failed, one existing
APBS environment skip. Core passed **274 tests** and its production-linked
coordinator harness passed **19 scenarios / 458 assertions**, rejecting all nine
compiled mutants. Pure scope/coordinator tests do not replace the native checks.

## Test cleanup

All three temporary plugins/assets and their newly generated configuration were
removed. The pre-existing Radar, Arena, Redeploy and ConfigurationManager
settings, StanceSync DLL/config, synchronization exclusions and original Core
were restored to their recorded hashes before normal production deployment.
The original reward choice was restored through the menu and independently
confirmed by server consent `enabled: true`. Game and server were shut down.

All nine canonical profile copies across the development host and both GPU
hosts were compared. Only the test profile's health-update and hideout-run
timestamps changed; inventory, skills, quests and XP were preserved. No profile
backup was written over a live profile. The GPU-1 installation received no
temporary test plugins.

## Integrated deployment

After cleanup, the normal build deployed Arena client and BigBrain companion
`0.2.1+c24cbc9`, plus Core `1.0.13+9d1672b`, to the development installation and
GPU-2. Their file hashes and the default policy matched on both machines. The
client's embedded `RedeployCoreBuiltAgainst` value is **1.0.13**. The server DLL
remains `0.2.1+0ab7c44`; no server code changed in this task.

Core implementation is commit `d49426a`; `9d1672b` changes only its native-test
documentation. Arena policy/tests/documentation are `a6b23dd`, integrated by
`c24cbc9` alongside an independently added review document. The final build had
zero errors and four existing nullable-context warnings. The game stayed off
after cleanup, so the new production hashes are disk-deployment evidence; the
native acceptance above identifies the exact tested candidate separately. No
GitHub push or release was performed.
