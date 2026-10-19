# ArenaMode — working notes for agents

Documentation, reviews, handoffs and historical build/deployment manifests live
in `docs/`; start with [docs/README.md](docs/README.md). Keep this `AGENTS.md` at
the repository root for automatic discovery and the offline harness root checks.
Place new documentation and evidence summaries in `docs/`, not beside this file.

Read [docs/LOGGING.md](docs/LOGGING.md) before adding runtime logging. Info is
reserved for player-relevant events; routine technical details use Debug and
expensive probes require Diagnostics/DeveloperMode. Never gate gameplay cleanup,
protection warnings or the independent LogDamageExceptions safety alarm.

A total-conversion mod for SPT 4.1 that turns Tarkov into an Unreal-Tournament-style
arena: instant respawn, kits instead of loot, and a profile that is never touched.
No Fika dependency. Arena is solo by default; the optional API-validated
PitFireTeam compatibility path supports protected Tactical (PMC + 1-4 companions versus five enemies) and
Zombie squad survival; read docs/PFT-SQUAD-SIZE-REVIEW.md for the frozen roster contract; read docs/ZOMBIE-PITFIRETEAM-REVIEW.md before changing the latter. The
accepted versions in `ArenaMode.Shared/PitFireTeamVersions.cs` are 0.10.0 and
0.10.3 or newer; 0.10.1/0.10.2 remain excluded. Forward admission was explicitly
requested on 2026-10-19. Keep metadata/assembly identity and every reflected
profile-protection contract; the proficiency contract is mandatory from 0.10.3
onward. SAIN 4.5.1+, APBS 2.4.1+ and DynamicMaps 1.2.1+ likewise attempt their
existing APIs and warn once for newer unreviewed versions. This is not a promise
of future compatibility. See docs/OPTIONAL-MOD-VERSIONS-20261019.md.
Five modes: Deathmatch, Gun Game, Tactical, Team Deathmatch, Zombies. Deathmatch merges
legacy Endless/Rounds with frozen individual FFA or all-vs-player rules and
optional finite/unlimited scoring rounds. See docs/DEATHMATCH-MENU-REVIEW.md and
docs/DEATHMATCH-MODE-REVIEW.md before changing score admission or terminal death holds.
Deathmatch FFA group/relationship hooks also operate during a confirmed arm on
its matching loading map, since native preactivation fixes group membership.
They never apply to an ordinary/unarmed map; server rejection after that seam
shares the documented preload exception's limits. Individual native groups must
remain registered and must be retired only after membership ended (not merely
AfterCreation, which is still PreActive). See docs/DEATHMATCH-GROUPS-REVIEW.md.
Team Deathmatch is independent USEC vs BEAR combat with continuous respawns;
it is separate from protected PitFireTeam Tactical. See docs/TEAM-DEATHMATCH-REVIEW.md.
Independent TDM bots also own finite exact-body spawn damage shields, with a
world-space cage and first-attack release. Read docs/TDM-BOT-SPAWN-SHIELD-20261017.md
before changing damage admission, attack hooks or shield lifetime. Never reuse
historical team IDs as shield authority or modify their DamageCoeff.
Team Friendly Fire is a raid-frozen mutator: Team DM and PFT Tactical default on,
PFT Zombies off; solo is unavailable. Read docs/FRIENDLY-FIRE-REVIEW.md before changing
damage admission, physical team identities or attributed grenade effects. Do not
use historical tracer IDs as authority to grant immunity to replacement bodies.
The dedicated Arena Molotov is a private, natively harmless item; ordinary RGD-5s
are never converted. Read docs/MOLOTOV-ITEM-REVIEW.md and
docs/MOLOTOV-PRESENTATION-REVIEW.md before changing its early database registration,
confirmed private-item capability, one live rig/pocket supply, native custom flea exclusion, bottle pool ownership
or local-only attributed fire. Registration must precede SPT's profile snapshot;
never weaken the harmless read-back to accommodate another mod's database changes.
No downloaded reference sound is licensed for this feature; the shipped effects
are synthesized by ArenaFireSound. Native test boundaries are recorded separately
in docs/GPU-MOLOTOV-ITEM-RESULTS-20261012.md. The current integration and single
client supply authority are documented in docs/MOLOTOV-NATIVE-INTEGRATION-20261015.md.
Equator remains unavailable for Tactical and is also excluded from PFT Zombies
because the five-person squad obstructs itself; solo Zombies stays supported.
Air Pit is an optional full map for all current modes, using its existing safe
generic spawn markers and the same exact-raid PFT coverless adapters as Bay5.
Independent AirPit Team DM requires MapPort 0.2.2 navigation and passport gates
for its original north USEC / south BEAR boxes; see docs/AIRPIT-TEAM-BOXES-REVIEW.md.
See docs/AIRPIT-INTEGRATION-20261017.md for migration and native-test boundaries.
Independent Equator, Bay5 and AirPit Team DM use their original team boxes for initial
placement and respawns (4–20 total participants). Equator is north USEC / south
BEAR; Bay5 is east warehouse USEC / southwest depot BEAR. Read
docs/BAY5-TEAM-BOXES-REVIEW.md for the map-keyed catalog/runtime. The exact
raid clone adds twenty private versioned markers; never move or overwrite the
common MapPort markers. Read docs/EQUATOR-TEAM-SPAWNS-REVIEW.md before changing the
MapPort repair ordering, initial physics/live navigation gates, reservations or
body-arrival validation. No map-wide fallback is allowed after box negotiation.
With MapPort's optional SpawnGates API, those booths stay closed through assembly
and countdown. Only the exact owned hold permits a complete intra-booth path
instead of the temporarily cut atrium route; Live still requires full navigation
after opening. See docs/EQUATOR-SPAWN-GATES-20261017.md for gate ownership,
preparation extraction deferral and cleanup. Never re-close during live respawns.
Zombies offers solo or a protected one-to-four-companion PFT squad, with endless waves of EFT's own infected reactivated for exactly
one armed raid, with Arena's own wave director replacing the native crowd
spawner. Solo has one life: death freezes survival and exits protected after any
selected replay; only the PFT squad restores fallen members between cleared waves.
See docs/ZOMBIE-MODE-REVIEW.md before touching the native Halloween-event
hooks, the wave policy or its stuck-wave watchdog.
The opening wave now waits for player/kit/team readiness and then the configured
intermission countdown. PFT wave breaks restore the surviving PMC's entry gear
through protected live-body replacement as well as restoring fallen members.
Read docs/ZOMBIE-WAVE-REFILL-20261015.md before changing these ownership seams.
Map-screen participant totals include one reserved human slot. Negotiated
Deathmatch/Gun Game owns its single-bot roster and optional Labyrinth boss slot;
native waves and unowned creator calls are suppressed only for the exact selected
Arena raid, including its confirmed matching-map loading phase. Never reset
ArenaSoloPopulation during the player's respawn or expire dispatched reservations
into free capacity. New full-map totals require exact quiet-clone validation on
the server, not just the existing bounded-area check. See docs/PARTICIPANTS-REVIEW.md
and docs/SOLO-POPULATION-REVIEW.md before changing this admission contract.
Independent TDM also has a ready/countdown/live/result lifecycle and client-owned
kill/time limits, frozen per match. See docs/TDM-MATCH-FLOW-REVIEW.md before changing
scoring or death handling: terminal death interception must remain armed until
the native player owner is verified and the intentional protected exit starts.
Damage assists add proportional base points without changing the killer reward,
kill limits or Tactical money. Read docs/ASSIST-SCORING-REVIEW.md before changing HP
capture, settlement or scoreboard compaction: actual HP deltas precede native
kill callbacks, and assist identity must use immutable keys across archived rows.
Arena-owned powerups and automatic weapon pickups are limited to live Deathmatch
and independent TDM. Read docs/PICKUPS-REVIEW.md before changing damage, scene or native
inventory ownership: only confirmed transfers consume a pickup, ambiguous/pending
failures never rearm it, and pooled weapon models must never be destroyed or parented
by the marker view. Gun Game, Tactical/PFT and ordinary loot are excluded.
Arena landing recovery, firearm draw/holster and deliberate airborne vault/hip
fire are documented in docs/AIR-MOVEMENT-REVIEW.md. Preserve exact local-body guards,
native geometry/firearm restrictions, temporary-state cleanup and double-jump
precedence; offline checks cannot certify native animator transitions.
Optional timed Bunnyhop is documented in docs/BUNNYHOP-REVIEW.md. Preserve actual
controller contact, one input owner, native landing/Jump entry, collision-reduced
momentum and single-application displacement scaling; never grant hops from the
ground-proximity ray alone or cancel every ordinary native Fall limit.
Bot locomotion has independent Arena-only walk/sprint controls. Read
docs/BOT-MOVEMENT-REVIEW.md before changing the native motion/cap bracket or SAIN door
guard; preserve exact live body identity, native collision and finalizer cleanup.
Normalized movement targets alone do not provide the displacement bonus.
Customs offers three unified surveyed areas: Factory Center, Factory Side and
Customs Yard, available in every Arena mode; whole Customs is rejected in Arena.
Independent TDM retains 2v2–5v5 faction starts; DM/Gun Game negotiate total
participants, Tactical and Zombies own their fixed/wave rosters. Read
docs/CUSTOMS-ALL-MODES-REVIEW.md and docs/PFT-PORTED-MAPS-REVIEW.md for bounded PFT placement,
deferred native follower startup and exact-world area readiness before mode clocks.
PFT changes the local entry category to Coop; only the exact frozen squad's
Tactical/Zombie loading request may use that category for the bounded PMC start.
Initial roster discovery waits until the deferred bounded mode can bind its
leader; any prior leader/profile/body history retains the strict live checks.
Bounded duplicate IDs and raw creation leases belong to the exact native raid
creator; never clear old-creator tombstones while delayed creation can resume.
Protected Tactical keeps its five frozen opponent bodies until roster retirement
because identity validation still reads them after death. Read
docs/TACTICAL-CORPSE-REVIEW.md before changing physical release; normal hiding/stripping
and strict unexpected live-body loss checks remain active.
Read docs/PORTED-COVER-REVIEW.md for Bay5/Equator's empty-cover combat guards: retain
real native NavMesh results, never manufacture cover points or successful paths.
Failed starts queue their native Stop after the startup frame and revoke both
ambient burst work and the separate managed solo population leases.
The delayed native raid-intro fade can otherwise cancel Stop's GameEnd callback;
see docs/FAILED-START-INTRO-REVIEW.md. Preserve exact captured identity and native
teardown checks; do not suppress general fades or replace native GameEnd.
The September 28 contours use new versioned IDs; all earlier area IDs remain
invalid on the wire. Config migration preserves exact recognized preferences
only. Yard adds twenty private per-raid spawn records around two native ground
anchors using NEW IDs (native FixParams does not change existing marker zones).
Never repair extras during final response validation or mutate native donor rows.
All areas retain unlimited height and fail-closed live navigation/occupancy checks.
See docs/CUSTOMS-AREAS-REVIEW.md and docs/AREA-TEAM-SETUP-REVIEW.md; database candidate
capacity and offline checks are not GPU acceptance of the new Yard offsets.

Read this before changing anything. Most of it was learned the expensive way.

Then read [docs/HANDOFF.md](docs/HANDOFF.md) for the dated working state, pending GPU tests,
and coordination between development/test machines. Verify its recorded commits
against the current checkout; it is a handoff snapshot, not an automatic record
of another running session. Update it when handing work over or changing the
agreed responsibilities. Historical milestone/test counts below are not current
acceptance evidence; use the dated handoff and the relevant review document.

Read `docs/DEPENDENCIES.md` for hard/conditional dependencies, exact optional-mod
contracts, absorbed own mods and release package boundaries. An installed mod
is not automatically required or exhaustively certified compatible.
Arena's local default full-auto rule is documented in `docs/DEFAULT-FIRE-MODE-REVIEW.md`.
Preserve manual selection while held, native available modes and exact confirmed
raid/body/hands ownership; the standalone DefaultAutoFireMode must be disabled
when testing Arena's own rule in isolation.
`docs/HUD-MUTATORS-REVIEW.md` documents the scoped ARENA HUD switch: preserve individual
HUD preferences, essential match notices and the separate audio controls.

**UiBridge is allowed in every GPU run (user decision, 2026-09-18).**
`BepInEx\plugins\maschine-UiBridge.dll` (`com.maschine.UiBridge`, repo
`../UiBridge`) is a permanent part of the GPU host installation: a test bridge
that lets agent sessions read the UI, wait for game states and, with
`[Actions] AllowActions`/`AllowTestHooks` switched on (deliberately, keep them),
inject ECommands, run item actions and set the look direction. GPU test
instructions must not treat it as an unexpected or unrelated plugin, must not
remove or disable it, and must not count its single Harmony patch
(`UiBridge.Game.CommandInjector::Prefix` on `InputManager`'s
`(List<ECommand>, float[])` delivery method) as contamination; it adds nothing
unless a session sends a command. Record its ProductVersion/SHA256 with the other
unchanged mods; it is not part of the Arena binding, so its hash is not an Arena
hash change. A run may use it instead of fixed coordinates and sleeps (see
`../UiBridge/README.md`); any action it takes is in the BepInEx log as `action …`.
Before the first game test of a session call `uibridge_help topic=overview` (proxy
0.8.1+): its playbooks (start, raid, inventory, shooting, gpu-run, troubleshooting)
hold the sequences and failure pictures; this file keeps only what is Arena-specific.

---

## Layout

| Path | What |
|---|---|
| `ArenaMode.Client/` | BepInEx plugin (netstandard2.1, Harmony). The bulk of the mod. |
| `ArenaMode.BigBrainCompat/` | Plain optional BigBrain 1.5.0+ library for pursuit, pickups and fire avoidance; no separate plugin GUID. |
| `ArenaMode.Server/` | SPT server mod (net10.0) + the Blazor config page. |
| `ArenaMode.Shared/` | Linked source compiled into BOTH — routes and DTOs only. |
| `tools/` | Offline generators for the kit catalogue and the weapon ladder. |

External, outside the repo:

| Path | What |
|---|---|
| `../_decompiled/` | The complete decompiled game source. **Verify every claim about game internals here.** |
| `../_reference/` | Other people's mods, for technique. Licence rules below. |
| `D:\SPT41\` | The game. Arena client DLLs/assets deploy to `BepInEx\plugins\maschine-ArenaMode\`; shared Core stays flat. |
| `D:\SPT41\SPT_Runtime\` | The server. Server DLL deploys to `user\mods\ArenaMode\`. |

## Build and deploy

BigBrainCompat is owned by Arena's Start/Update/OnDestroy, not an independent
plugin. Preserve lazy named-type loading and the NoInlining typed boundary;
do not add BigBrain types or a hard companion reference to the main client.
Update both Arena client DLLs together. Read docs/BIGBRAIN-LIBRARY-20261018.md
before changing this contract. Arena needs Arena.Server for profile protection;
Redeploy.Core itself has no server dependency.

```bash
dotnet build ArenaMode.sln -c Release   # builds and deploys both halves
dotnet test  ArenaMode.Tests            # ~30 tests, under a second
```

All mod projects copy themselves into place on build. The tests cover the SERVER
half and the generated data; the client half is Harmony patches against Unity and EFT
types that cannot load outside the game, so **for anything client-side, verification is
still a raid.**

Build/deploy and release packaging share `tools/Write-ArenaAssets.ps1`; do not
reintroduce direct copies of source notice JSON after its relocation to `docs/`.
`tools/Deploy-ArenaClient.ps1` migrates the old `plugins/ArenaMode` folder and
flat owned DLL/PDBs into `plugins/maschine-ArenaMode`, preserving user data and
archiving replaced files outside plugin discovery. Keep both existing deployment
target names overridable: `SPTClientDeploy` and `DeployArenaAudioAssets`.
`StageArenaAssets`/`StageArenaPublishAssets` write only build/publish output.
Read [docs/UNIFIED-DEPLOY-20261017.md](docs/UNIFIED-DEPLOY-20261017.md) for migration
limits, no-deploy checks and current evidence. Core's separate sibling target
must remain suppressed in offline builds too.

- **Client changes** need the game restarted, nothing else.
- **`config.json` changes** need nothing: hot-reload via the config page.
- **`kits.json` / `ladder.json` changes** need the SERVER restarted — the catalogue is
  read at load.
- **Config page**: `https://127.0.0.1:6969/arenamode` — **https**, not http. It is a
  self-signed certificate, so `curl` and most browsers fail silently on `http://`.

Logs: client `D:\SPT41\BepInEx\LogOutput.log` (local time), server
`D:\SPT41\SPT_Runtime\user\logs\spt\sptYYYYMMDD.log` (**UTC** — the two disagree by
your offset, which has cost hours before).

## Tests

`ArenaMode.Tests/` — xunit, no game required (checks that need the item database skip
themselves; point `SPT_DATABASE` at it to run them elsewhere).

They exist because these are the things that broke repeatedly and quietly:

- **Kit catalogue invariants** — unique ids, one root, one primary weapon, no orphaned
  parents, no slot filled twice, every template real. The client REFUSES a kit that
  breaks these, so the symptom is a player respawning naked mid-raid rather than an
  error at build time.
- **Ladder draw** — the right number of rungs, still ordered weakest to strongest, and
  every ammunition site on a weapon holding the same cartridge. A revolver whose camoras
  disagreed with its chamber shipped once and threw nothing.
- **Location mutation** — raid time, bot cap, boss handling, and that every generated
  wave names exactly ONE real zone. The engine matches a zone by exact string, so a
  comma-separated list matches nothing and the wave is dropped in silence; that shipped,
  and it emptied the Lab.

When adding a test here, check it fails against the bug it claims to catch. The zone
test above was verified that way — reintroduce the comma and three tests go red.

## Non-negotiable

1. **The profile is sacred.** An arena raid must cost and gain nothing. Two layers
   protect it: an exact-ID `EndLocalRaidAsync` prefix that replaces the complete native
   post-raid pipeline, and a snapshot/restore around the raid. Never weaken either.
2. **Normal raids must be bit-identical.** Every patch starts with an
   `ArenaSession.IsActive` gate and fails open to vanilla. A setting that leaks into a
   non-arena raid is a bug even if it is harmless.
   Loading-time exceptions are documented here and at their call sites:
   Protected PFT Tactical may issue one PMC-only Arena kit after the exact
   native local-start identity binds, before LocalGame creates the first body.
   Read docs/PFT-TACTICAL-KITS-20261018.md: exact arm token/epoch, profile,
   equipment root, matching world and no local body are mandatory on both sides
   of the required pool await. Preserve protected slots transactionally, never
   replace a PFT leader later, never change companions or PFT Zombies.
   `ArenaProfilePreloadPatch` runs inside the loading screen, where `IsActive`
   cannot yet be latched, so it gates on ARMED + the map being loaded instead.
   If the server then refuses the arm token, that raid runs ordinary with an
   emptied bot-profile pool — not broken (a pool miss fetches three more) but
   not vanilla either. The loading-screen
   kit preload (`KitBundlePreloader.BeginRaidStart`, started from the armed
   `LocalRaidStarted` continuation) together with the optic guard it engages
   (`KitOpticPreloadGuard`, whose loading-screen requests stay current from
   Begin until the native call returns, whatever the arming or IsActive do in
   between): both build prefab pools for the coming kit inside the raid's own
   pools, and the guard touches nothing but the role models of those pools. A
   raid that then runs ordinary carries a few unused pools that die with it,
   and the guard covers their building to the end. The Deathmatch group and
   relationship hooks described above likewise require a confirmed matching-map
   arm before activation; they must settle initial membership during loading.
   `ZombieNativeEventBridge`'s four loading-time Harmony hooks (on
   `BotsEventsController.Activate()`/`Dispose()` and the static
   `BotHalloweenWithZombies.GetProfilesOnStart()`/`AddBackupProfiles()`) are the
   same exception for the same reason: all three non-`Dispose` hooks fire inside
   `LocalGame.vmethod_1`, before `IsActive` can have latched, so they gate on
   `ArenaPopulationPolicy.IsArmedZombieLoading` instead. The native
   `ACTIVE_HALLOWEEN_ZOMBIES_EVENT` flag these hooks flip on is always restored
   (end of raid via `Dispose()`/`ArenaSession.OnRaidEnded`, or — if a previous
   raid's load aborted before either ran — by the very next raid's own
   `Activate()` call, which fires unconditionally for every raid); see
   docs/ZOMBIE-MODE-REVIEW.md for the full restore-path argument.
   Post-raid UI runs after IsActive is cleared. ArenaPostRaidScreens captures the
   exact ended LocalRaidSettings/profile/epoch and binds one native result
   operation before changing its warning/XP presentation. This is a UI-only
   exception; native profile refresh and result completion must remain intact.
   See docs/POSTRAID-PAGES-REVIEW.md for both forward and health-back navigation.
   Dedicated-item metadata is registered before the native database/profile
   snapshot: the private Molotov template, locales/cache and private loot/flea
   exclusions exist at server startup. This adds no normal loot, trader stock or
   profile item and never edits the RGD-5 donor. Exact-template model/icon/pool
   restoration and native harmless cleanup likewise do not require an active
   raid; fire and supplies retain their separate strict Arena admission gates.
   See docs/MOLOTOV-ITEM-REVIEW.md for this static item-registration boundary.
3. **Licences.** ABPS, APBS, SAIN, RaidOverhaul, MoreBotsAPI, SVM, PitFireTeam, BigBrain:
   technique only — read them, never copy code, never bundle their DLLs. MIT/Apache
   sources (ORBIT, DragonDen, FieldKit, traumacore, bullettime) may be adapted with
   attribution. LacywayPvETweaks and LetMeRightClick are proprietary: do not touch.
4. **Adversarial review after every implementation.** This has caught a genuine
   critical bug in nearly every milestone — a hands softlock, a permanent-invulnerability
   latch, a deadlocked game mode. Do not skip it because a change looks small. The tests
   are a floor, not a substitute: none of those three would have been caught by one.

## User-authorized survey helper exception (2026-09-07)

The area-outline development tool may read scene positions, draw its preview and
explicitly export draft files in a normal local PMC raid when BOTH `Enabled` and
`AllowInNormalPmc` are enabled. The user needs to walk beyond the existing Arena
contour. This does not authorize ordinary-raid gameplay/profile changes, Arena
activation, invulnerability, Scav/Hideout capture or weakening any Arena barrier.
Both switches default off. See `docs/POLYGON-AREA-REVIEW.md` for the exact boundary.
The accepted pickup prototype extends this read-only exception to its separate
`Development - Pickups / Enabled` capture tool (both Development sections and
Diagnostics are F12 advanced entries, see docs/CONFIG-MENU-20261018.md) plus the same explicit
`AllowInNormalPmc` switch. It may preview coordinates and export new PickupDrafts;
it must never spawn loot, grant effects or automatically install a layout there.
See `docs/PICKUPS-REVIEW.md` for per-map/area layout admission and capture shortcuts.

## Traps this project has already fallen into

Each of these cost a debugging session. They recur.

**`MongoId` is a struct.** `FirstOrDefault()` on an empty sequence returns twenty-four
zeroes, never null. A null check will not fire and you will write a node referencing a
template that does not exist.

**Max-armorClass over a preset counts parts you are about to remove.** Bit us twice:
bare plate carriers scored class 6 (the plate slots' filter, not the contents), and the
Maska helmet scored class 6 for a face shield the generator drops. Always compute over
the nodes that survive. Same for a plate carrier at runtime — its own template is class 0.

**Any item conjured at runtime needs its bundles preloaded.** `ObjectsFactory` returns
null for a template with no prefab pool, the NRE lands *after* the weapon-change
operation opened and before anything closes it, `IsChangingWeapon` stays latched, and the
player has empty hands — no weapon, no meds, no grenades — for the rest of the raid.
`KitBundlePreloader` exists for this. Cache by TEMPLATE, never by kit id: Gun Game names
merged kits `basekit+rung`, so an id-keyed cache never hits.

**`GlobalConfiguration` is shared with bots.** Never mutate it. The two exceptions are
argued in place: the deploy countdown (one reader) and the vaulting limits (bots have no
vaulting component at all — `InitVaultingComponent` returns immediately for AI).

**Server: only prefixes on async methods.** A postfix fires at the first suspension
point, not at completion. "After the raid" logic lives in its own route.

**`OperationResult<T>` is not a bool.** Discarding it means the container mutates but
nothing is notified — items stay visible, models stay in the pool. Call `RaiseEvents`.

**BSG's own flags lie.** `IsBoss()` is true for PMCs, exUsec, arenaFighter and more —
use `IsPureBoss()`. `ShallBossAttack == true` means "do NOT attack". `ActivateBotsByWave`
is a boss spawn path despite the name.

**An enum's `default` is a real value, not "none".** `EBoundItem.default` is
`ItemG` — the grenade slot — so a `FirstOrDefault` over quick slots cannot say
"nothing found", and acting on the result writes over the grenade picker. Same
family as the MongoId trap above: check the count, do not compare to default.

**Loading is bound by frames, not CPU, once a frame is over budget.** Every bundle load
and pool build is a chain of `JobScheduler.Yield` steps. Under budget (frame < 1000 /
framerate limit, measured from the previous frame's end so VSync and limiter waits count)
the scheduler keeps passing over the queue for the slack that is left, up to half the
budget; over budget it makes ONE pass and stops, so every chain moves one step per frame.
A machine held at 60 fps under the 120 limit is over budget on every frame: it loaded a
bot in 13-54 s and the kit catalogue in 5.5 minutes where the other took 0.4-2 s and 19 s,
with only 1.7-1.8x between them on plugin and map load. `BotLoadBoost` re-enables the
scheduler's force mode while bots or kits load. Passes only help CPU-bound steps: an
asynchronous wait costs at least one frame whatever the multiplier, because the engine
integrates such an operation in a per-frame step. A bundle load's whole OPENING is of
that kind — a `Task.Yield` lock spin, a `Task.Run` consistency check, then
`LoadFromFileAsync` and `LoadAllAssetsAsync` — which is why shortening the boost's delay
bought nothing measurable; force mode earns its keep in the asset scan and the pool build.
How much the integration step gets is `Application.backgroundLoadingPriority`, which
`Streamer` drops to Low on maps that carry it — measured as BelowNormal (Unity's default)
on Customs on both machines, so no arena map streams and that lever ships off. And `MovingAverage` exists twice in
Assembly-CSharp: the bare name is the global one, `Diz.Jobs.MovingAverage` must be
spelled out.

**The loading screen has a seam, and it is not where it looks.** The obvious place to
warm a kit early, the player-prefab preload in `TarkovApplication.LocalGameMatching`,
runs BEFORE `LocalRaidStarted` and so before the arena is armed; a hook there fires
for nothing. The first moment that is armed AND has the raid's `GameWorld` with its
raid pools registered (`GameWorld.InitLevel` inside `LoadMapAndData`) is the
continuation of the `LocalRaidStarted` task in `ArenaLocalRaidStartedPatch.ObserveLocalStart`,
which resumes on the main thread (the HTTP layer completes on it, and nothing in the
game uses `ConfigureAwait`). From there until `OnGameStarted` the game's force mode is
on and the bundle lock unlimited. Bookkeeping traps around it: `ResetSession` runs at
the start of the raid the preload was made for, so anything keyed on the preloader's
epoch or warm set is wiped — adopt after it, explicitly; `ObjectsFactory.DefaultCancellationToken`
is a static that raid teardown cancels and REPLACES, so read it at call time and read it
again at the end, because a load cancelled while filling pools completes as if it had
succeeded; the Raid `Pools` object is never unregistered and is no identity for a raid,
the `GameWorld` (fresh per raid, `Singleton<GameWorld>.Instance` already the raid's at
that seam) is; and two concurrent `LoadBundlesAndCreatePools` for the same key both
retain the bundle and only one token is ever released — vanilla does this to itself,
but do not add to it: wait for a load in flight instead of asking again.

**BSG ships placeholders, marked only in the display name.** Eight templates in the
4.1 database are unfinished duplicates whose English name says `!!!DO_NOT_USE!!!` (one
of them `!!!DO NOT USE!!!`): a 23x75 round with a full set of ballistic numbers and no
handbook entry, a 40 mm smoke round, six AXMC parts. Their internal names look ordinary
and their properties rank like anything else — the Cheremukha round was drawn as Gun
Game ammunition and did not fire. `ArenaTemplateEligibility` is the one answer to "may
this template be handed out": the pool generator and the offline script apply its name
rule and the configured list, the attachment draw and the per-raid cartridge draw
refuse the ids they are handed at every raid, and `excludedTemplates` in the config is
where the next one goes.

**`Profile.Clone()` turns "knows every item" into "knows none".** The clone runs through
`ProfileDescriptor`, whose encyclopedia copy answers a NULL dictionary with an EMPTY one —
and those are opposites: `Profile.Examined(templateId)` returns true for null and
`ContainsKey` for empty. PitFireTeam's stored teammates ship a null encyclopedia, so a
cloned follower silently stopped knowing anything. Almost nothing notices, because
`InventoryController.Examined` short-circuits on `Item.ExaminedByDefault`, which nearly
every template sets. The M60E6 does not. The consequence was a follower paralysed with no
error anywhere: `IsAtReachablePlace` requires `Examined`, `Player.TryProceed` answers an
unreachable item by DIVERTING to `SetFirstAvailableItem`, a companion with no sidearm gets
EFT's default bayonet, a knife takes `UpdateHandsController`'s melee branch — which sets
`Selector.IsWeaponReady` but never `BotWeaponManager.IsReady` — and `BotOwner.UpdateManual`
leaves `PreActive` only on the latter. `BotWeaponSelector.OnWeaponTaken` schedules no retry
either, because the melee branch reported success AND its fallback excludes `PreActive`.
Nine native steps, one flipped null, zero log lines. `TryPreserveEncyclopediaUnsafe`
restores the source's null; the whole chain is asserted against `../_decompiled/` in
`PitFireTeamRoundLifecycleChecks`. There is exactly one `Profile.Clone()` in this
repository — keep it that way, or repeat this.

**The client caches server answers per game process.** Bot difficulties are fetched once
and never again. Anything that must vary per raid cannot live there.

**EFT switches the garbage collector off for the raid.** `BaseLocalGame.PrepareSession`
sets `InGameMemoryManagement.GCEnabled = false` on machines with at least
`GigabytesRequiredToDisableGCDuringRaid` GB of RAM (SPT serves 25), so the managed heap
is the raid's total allocation — 7 MB/s in a Labyrinth deathmatch — and a growing heap is
not a leak. The only collections are forced and BLOCKING (raid end, menu): 0.5 to 0.9 s
each on the GPU host. Never call `GC.Collect` or `GC.GetTotalMemory(true)` from Arena
code, not even for diagnostics. `GcThresholdMonitor` switches the incremental collector
back on past `GcHeapThresholdGB`; see `docs/GC-THRESHOLD-REVIEW.md`.

**Every `ModulePatch` subclass is named `Arena…`.** SPT's parameterless `ModulePatch`
constructor uses the bare class name as the Harmony id, so a nested `LocationPatch` or
`TimerPatch` shows up without the mod name and can share an id with another mod's patch.
Scheme (2026-10-18): `Arena` + outer type without its leading `Arena` and trailing
`Patches`/`Patch` + the class name, unless the name already starts with that stem
(`ArenaSessionModeCaptionPatch.LocationPatch` -> `ArenaSessionModeCaptionLocationPatch`); a
top-level class is `Arena` + name, the server bot cap patch `ArenaServerBotCapPatch`. A
primary constructor (`class X(Type t) : ModulePatch`) is a patch class too.

**A Harmony patch that asks for `__originalMethod` on a method declared on a generic
type throws on every call, before the patch body ever runs — even for a closed
instantiation.** First hit patching `ComponentSystem<MuzzleManager, MuzzleSystem>.Update`
(`RespawnLeakProbe.Timing.MethodTimer.IsPatchable`): HarmonyX/the CLR cannot resolve a
`MethodBase` for a method handle whose declaring type still carries an open type
parameter, and the error is "Cannot resolve method ... because the declaring type of the
method handle ... is generic". Hit again patching `AbstractAssetsPool<T>.ExpandPool`
(`Performance.PoolPrewarmMonitor`, one prefix shared across seven closed `T`s via
`AccessTools.DeclaredMethod(typeof(AbstractAssetsPool<>).MakeGenericType(t), ...)`). The
fix both times was the same: never declare `MethodBase __originalMethod` in the patch
signature — use `object __instance` cast to whatever public interface the type exposes,
and by-name pass-through parameters spelled EXACTLY as the concrete implementation spells
them, not as an interface it implements may spell them (`IAssetPool.ExpandPool`
misspells `objectsCount` as `objectsCoun`; Harmony binds by name against the method it
actually patched). See `docs/POOL-PREWARM-REVIEW.md`, "The Harmony recipe".

**SAIN's `RunToEnemyUpdate` prefix returns `false` for every bot SAIN does
not own.** Its trailing, unconditional `return false;` skips Harmony's
call to the ORIGINAL method for any excluded bot — all four generic
infected roles included — so a Zombie-mode infected's native melee swing
never ran (25 minutes, zero player damage, first GPU raid 2026-10-06).
Never patch SAIN or race its patch order; instead patch one call site up
(`OneMeleeAttackNode.UpdateNodeByBrain`) and drive the real native body
through a Harmony reverse patch (`Harmony.CreateReversePatcher`), which
copies the original IL into a patch-chain-free `MethodInfo` SAIN's prefix
never sees. See `ZombieMeleePatch` and docs/ZOMBIE-MODE-REVIEW.md, "Native melee
is suppressed by SAIN's global prefix".

**A cached live-body reference goes stale, silently, for any body swap that
is not the one event you listen for.** `ZombieMode._local` was refreshed only
by the real death-respawn event (`OnLocalRespawn`), but the raid-start kit
swap AND the wave-intermission refill's own live-body swap
(`LiveKitRespawn.AtRaidStart`/`AtRoundBreak`/`AtRoundBreakOwnGear`, all
through the shared `SwapRoutine`) replace the live body too and destroy the
old one WITHOUT raising that event. `EFT.Player : MonoBehaviour`, so Unity's
overridden `==`/`!=` makes `_local != null` read false forever for the
destroyed body on the very next tick — no exception, no log line, just a
feature (the wave-intermission refill) that silently stops firing after its
first live-body swap. A desktop-CLR xunit test cannot reproduce this; it is
Unity-specific object identity, not a logic bug a pure test layer can see.
The fix is to poll the authoritative source (`_world.MainPlayer`) and heal
the cached reference when it changes, guarded on `!ArenaRespawnDriver.
IsRespawnInProgress` so the heal fires only once a replacement body is fully
wired — `TeamDeathmatchMode.Tick` already did this for its own cached
reference; `ZombieMode.Tick` now does too. Any new per-mode code that caches
a live `Player`/`BotOwner` reference across ticks must self-heal the same
way, not just subscribe to the one event that happens to be named for it.

**Do not detour constructed generic inventory queries on Mono.** Patching
`GetReachableItemsOfTypeNonAlloc<Magazine>` also replaced Ammo and ThrowWeap
calls with the Magazine wrapper. The original scanner then threw
`ArrayTypeMismatchException` before the postfix could check the runtime types,
breaking the weapon HUD, internal reload and G. Patch non-generic consumers and
replace only their exact Magazine calls instead. A desktop-CLR test of a postfix
cannot certify Mono generic detours. See `docs/BACKPACK-RELOAD-REVIEW.md`.

## Where the knowledge is

There is no design doc. The reasoning lives in two places, both worth reading before
touching an area:

- **Commit messages.** 77 of them, and they explain *why*, including the mistakes. `git
  log` over a file you are about to change is the fastest way in.
- **Doc comments.** Every non-obvious decision is argued at the code, not summarised
  elsewhere. If a comment says a rule is deliberate, it is — and if you find one that
  disagrees with the code, the comment is a bug too.

## State

M0–M7 are done and playtested. The Blazor config page, Labs as a third map, and the
Tactical mode are in and lightly tested.

Open:

- **M8 polish** — optional per-mode Killcam is implemented; see
  `docs/KILLCAM-MODES-REVIEW.md` for frozen menu selection, camera ownership and
  pending native acceptance. README and release packaging remain separate.
  Never bundle ABPS/APBS/SAIN DLLs.
- **Tactical** needs a longer playtest; only the first rounds have been played.
- **Performance** is now measured, not guessed: see docs/LONG-RAID-REVIEW-2026-09-16.md,
  which supersedes the 2026-09-05 report. Over 27 minutes all mods together grow
  from ~2.1 to ~3.0 ms/frame, SAIN is five times our cost, and one SAIN postfix on
  `GameWorld.DoWorldTick` is 29% of measured mod time — too small to explain a
  severe collapse. The one concrete Arena-side lead is the respawn pipeline stage:
  strictly monotonic, +0.023 s per respawn, tracking respawn count rather than
  elapsed time. Time the stages inside it instead of theorising: Redeploy Core
  1.0.2 logs `Respawn pipeline #N` per stage, and since 2026-09-17 Arena's
  `Arena respawn timing` line splits the review's `pipeline` into `prepare`
  (selector, area preflight, request — the spawn point is resolved here only
  while an area is active, otherwise in Core's `spawn`), `pipeline` (the
  `RunRespawn` call alone) and, only when the terminal result hold waited,
  `hold`; compare older logs against their sum. Read the review's
  methodology before trusting any new ModProfiler numbers: the counters only
  advance while its window is open, the frame count is `WebOverlayPlugin.Update`
  (per-mod call sums are not frame counts), and rows near 0.6 us/call are the
  instrumentation, not work. The Arena-side root cause is fixed since
  2026-09-17: a trimmed corpse is now RELEASED a sweep later — the body leaves
  the scene instead of staying an active player object — because 446 hidden
  bodies were what made every whole-scene search grow (415 active LocalPlayer
  objects, 303 ms to count them). Read docs/CORPSE-CLEANUP-REVIEW.md, section
  "Releasing a trimmed corpse", before touching corpse lifetime: the step order
  is load-bearing, PitFireTeam bodies are never released, and a body is
  never destroyed while DynamicMaps still holds its marker or while anybody is
  still owed a death by it. Measured on Labyrinth against a same-evening
  control raid: the pipeline's growth per raid minute fell from 20.6 to 13.2 ms
  and the live player-object count from 415 at 23 min to about 63 at 24 min.
  What was left there was dominated by the player's OWN corpses, which were
  exempt by design at the time. Since 2026-09-18 ("Own corpses release too",
  docs/CORPSE-CLEANUP-REVIEW.md) only the NEWEST own body keeps that exemption: an
  older one releases like any other corpse, once nothing still needs it —
  including a refinement so the live player's own current combat cannot hold
  an older own body open forever just because it shares that body's profile
  id (`CorpseDamageSources.StillNamedInRegistry`, keyed on whether
  `GameWorld`'s own ever-existed registry still resolves to that exact body).
  The own-corpse-release path itself has NOT had its own GPU raid — see
  docs/CORPSE-CLEANUP-REVIEW.md's "What this does not claim". Do not read the GPU
  acceptance below as covering it; it predates it.
  Zombie mode additionally sweeps at a 1 s cadence (`ZombieCadenceSeconds`)
  instead of 5 s and drains releases between sweeps at up to two per frame
  (`ZombieMaxReleasesPerFrame`/`DrainBetweenSweeps`) rather than waiting for
  the next sweep's eight-per-sweep cap (2026-10-07; see
  docs/ZOMBIE-MODE-REVIEW.md, "v3"); every other mode's cadence, release cap and
  `MayRelease` holds, and the ragdoll-settled gate that keeps a body visibly
  falling, are unchanged by this.
  The `Player.ApplyDamageInfo` / `ClientGameWorld.ShotDelegate`
  exceptions the ORIGINAL (2026-09-17) trimmed-corpse release introduced are
  explained and, on `d265353` (06:27, still before the own-corpse-release
  commits that evening), fixed and GPU-accepted: a Labyrinth raid from
  06:34-06:48 logged zero reports. Every one of the 21 logged before the fix
  was a TRIPWIRE planted by the dead Labyrinth boss `bossTagillaAgro`,
  going off after Arena had released his body. A tripwire keeps its planter's
  profile id for its whole life; the explosion and its fragments resolve that id
  through `GetEverExistedBridgeByProfileID`, and the victim bot's
  `BotWeaponManager.OnBulletNear` reads `Position` on the destroyed body behind
  an `IPlayer` null check Unity's lifetime operator never reaches. The same
  activations appear in raids that release nothing, and throw nothing there.
  `CorpseDamageSources` keeps a body hidden while an unfinished tripwire it
  planted, a grenade it threw or a shot it fired still names it, then for a 2 s
  quiet period (SAIN's deferred `BotMemory.GetHit`). Note that `TripwireManager`
  drops a tripwire the moment it is TRIGGERED, i.e. while its grenade burns —
  read the sync-object processor's lists instead. Do NOT prune
  `_allPlayersEverExisted` or its bridge twin; read docs/CORPSE-CLEANUP-REVIEW.md,
  "Settled (2026-09-17…)" and "What held that body", before touching corpse
  lifetime. `ArenaDamageExceptionLog` (Diagnostics/`LogDamageExceptions`,
  default on) stays as the regression alarm, and `ReleaseTrimmedCorpses` still
  turns the whole release off. The heap still grows to 11 GB without a
  collection and no longer has a corpse explanation. EFT disables Unity's GC
  for the whole raid on its own (`InGameMemoryManagement.GCEnabled = false`
  in `BaseLocalGame.PrepareSession`, effective above `../_decompiled`'s
  `GigabytesRequiredToDisableGCDuringRaid` — SPT serves 25 GB) and only
  collects again through a forced, BLOCKING sweep at raid end or in the menu:
  never call `GC.Collect`/`GC.GetTotalMemory(true)` from Arena code, not even
  for diagnostics. `Config.Settings.GcHeapThresholdGB` (default 4, 0 = never)
  switches Unity's own incremental collector back on past a heap threshold,
  timed to a death/round break/bounded wait so it costs nothing on a raid
  that never crosses it; see `docs/GC-THRESHOLD-REVIEW.md` and
  `ArenaMode.Client/Performance/`.
  A second, separate cost in the same class: `ObjectsFactory.InitAndFillPools` sizes
  every asset pool (AFAKs, survival kits, weapon families, hands prefabs, ...) to the
  TOTAL number of bots that have ever spawned this raid, never the concurrent demand,
  and that counter never drops — harmless at vanilla's ~30 bots, unbounded under Arena's
  continuous respawn (measured: 210 idle AFAKs and 207 idle survival kits after 267
  bodies in 17 minutes, ~380 stale components per body). `Config.Settings.
  PoolPrewarmKeepIdle` (default 4, 0 = never) caps the idle instances EFT's own prewarm
  keeps per resource during an arena raid via `Performance.PoolPrewarmMonitor`, a Harmony
  prefix on `AbstractAssetsPool<T>.ExpandPool` gated on `ArenaSession.IsActive` and the
  exact `"LoadPlayerResources"` reason string; pool creation and the separate
  `MaintainMinPoolSize` top-up are untouched, and a capped pool still creates on demand
  exactly as vanilla does for any pool's first use. Measured on the GPU host (19.09.2026,
  257 bodies, same protocol as the unclamped raid): weapon-pool creations 7.2 → 2.6 per
  body, active MonoBehaviours 91.5 → 64.1 per body, 6,948 top-ups declined, no spawn
  error; a non-arena control raid (Factory as Scav, 12 bots, 19.09.2026) logged no
  ArenaMode line at all. What remains is the number of pools itself (+6 per body: every never-seen
  loadout variant creates a pool with its `"initial"` fill), deliberately untouched.
  See `docs/POOL-PREWARM-REVIEW.md`.
- **Bounding the play area** on large maps (filter `SpawnPointParams` on the per-raid
  clone).
- **PitFireTeam 5v5** — the optional 0.10.0/0.10.3+ compatibility path now has a guarded
  best-of-N round lifecycle and still needs a GPU playtest. It preserves the PMC entry
  loadout after a death, rebuilds fresh follower/opponent bodies between rounds, offers
  post-death third-person spectating, refreshes both teams' pursuit knowledge, and uses
  a bundled optional companion for BigBrain 1.5.0+ (newer versions warn) to own bounded movement toward live opponents while
  the PMC is down. It excludes teammate bots/corpses from Arena cleanup/counts and blocks
  both SPT-profile effects and PitFireTeam's separate teammate persistence. Every
  teardown/rebind step fails closed as no-contest when the exact reflected contract or
  body identities drift.

## How to work here

Create a commit after every completed change, including its tests and documentation;
do not leave finished work uncommitted or combine unrelated changes. Use small,
self-contained commits with a message that explains the reasoning. Verify game
internals against `../_decompiled/` rather than assuming — the decompiled source is
complete and it has contradicted confident guesses many times. When a playtest disagrees
with your explanation, the explanation is wrong; measure before you theorise again.

Core admission: both clients fail closed before runtime initialization when the loaded
Core assembly is missing, unknown or older than RedeployCoreBuiltAgainst (currently
1.0.8). Keep Plugin.Awake free of Core types and InitializeRuntime NoInlining. Arena
Start/Update/OnDestroy must stay guarded, including cleanup of rejected plugins.
Build/deploy/release share tools/Arena-NoticeLayout.ps1: notices and embedded map
provenance go under docs/licenses; preserve unknown user files during migration.
Active announcer OGGs live in announcements/, the four effect WAVs in sounds/.
Audio records are consolidated under docs/licenses/audio; see
docs/AUDIO-LAYOUT-20261018.md. Keep source-only reserve audio out of payloads.
