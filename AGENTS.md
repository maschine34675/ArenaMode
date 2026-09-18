# ArenaMode — working notes for agents

A total-conversion mod for SPT 4.1 that turns Tarkov into an Unreal-Tournament-style
arena: instant respawn, kits instead of loot, and a profile that is never touched.
No Fika dependency. Arena is solo by default; the optional exact-version
PitFireTeam compatibility path supports protected Tactical 5v5 matches; the
accepted builds are the exact set in `ArenaMode.Shared/PitFireTeamVersions.cs`
(0.10.0 and 0.10.3), never a range — 0.10.1/0.10.2 were never diffed.
Four modes: Deathmatch, Gun Game, Tactical, Team Deathmatch. Deathmatch merges
legacy Endless/Rounds with frozen individual FFA or all-vs-player rules and
optional finite/unlimited scoring rounds. See DEATHMATCH-MENU-REVIEW.md and
DEATHMATCH-MODE-REVIEW.md before changing score admission or terminal death holds.
Deathmatch FFA group/relationship hooks also operate during a confirmed arm on
its matching loading map, since native preactivation fixes group membership.
They never apply to an ordinary/unarmed map; server rejection after that seam
shares the documented preload exception's limits. Individual native groups must
remain registered and must be retired only after membership ended (not merely
AfterCreation, which is still PreActive). See DEATHMATCH-GROUPS-REVIEW.md.
Team Deathmatch is independent USEC vs BEAR combat with continuous respawns;
it is separate from protected PitFireTeam Tactical. See TEAM-DEATHMATCH-REVIEW.md.
Independent TDM also has a ready/countdown/live/result lifecycle and client-owned
kill/time limits, frozen per match. See TDM-MATCH-FLOW-REVIEW.md before changing
scoring or death handling: terminal death interception must remain armed until
the native player owner is verified and the intentional protected exit starts.
Damage assists add proportional base points without changing the killer reward,
kill limits or Tactical money. Read ASSIST-SCORING-REVIEW.md before changing HP
capture, settlement or scoreboard compaction: actual HP deltas precede native
kill callbacks, and assist identity must use immutable keys across archived rows.
Arena-owned powerups and automatic weapon pickups are limited to live Deathmatch
and independent TDM. Read PICKUPS-REVIEW.md before changing damage, scene or native
inventory ownership: only confirmed transfers consume a pickup, ambiguous/pending
failures never rearm it, and pooled weapon models must never be destroyed or parented
by the marker view. Gun Game, Tactical/PFT and ordinary loot are excluded.
Arena landing recovery, firearm draw/holster and deliberate airborne vault/hip
fire are documented in AIR-MOVEMENT-REVIEW.md. Preserve exact local-body guards,
native geometry/firearm restrictions, temporary-state cleanup and double-jump
precedence; offline checks cannot certify native animator transitions.
Bot locomotion has independent Arena-only walk/sprint controls. Read
BOT-MOVEMENT-REVIEW.md before changing the native motion/cap bracket or SAIN door
guard; preserve exact live body identity, native collision and finalizer cleanup.
Normalized movement targets alone do not provide the displacement bonus.
The Customs Center and Factory Side team-start presets add independent TDM 2v2–5v5,
with exact per-raid team-size negotiation and reserved faction starts/respawns.
The surveyed Center contour remains fixed 2v2; the old Center rectangle and
Factory South prototype IDs are retired and rejected at arm admission.
Factory Side retains the earlier `customs-factory-east-polygon-v1` ID, but now
requires the explicit area team size on both ends. See AREA-TEAM-SETUP-REVIEW.md and the
dated HANDOFF.md for implementation, delivery and pending user GPU acceptance.

Read this before changing anything. Most of it was learned the expensive way.

Then read [HANDOFF.md](HANDOFF.md) for the dated working state, pending GPU tests,
and coordination between development/test machines. Verify its recorded commits
against the current checkout; it is a handoff snapshot, not an automatic record
of another running session. Update it when handing work over or changing the
agreed responsibilities. Historical milestone/test counts below are not current
acceptance evidence; use the dated handoff and the relevant review document.

---

## Layout

| Path | What |
|---|---|
| `ArenaMode.Client/` | BepInEx plugin (netstandard2.1, Harmony). The bulk of the mod. |
| `ArenaMode.BigBrainCompat/` | Optional BigBrain 1.5.0 action owner for post-death Tactical 5v5 pursuit. |
| `ArenaMode.Server/` | SPT server mod (net10.0) + the Blazor config page. |
| `ArenaMode.Shared/` | Linked source compiled into BOTH — routes and DTOs only. |
| `tools/` | Offline generators for the kit catalogue and the weapon ladder. |

External, outside the repo:

| Path | What |
|---|---|
| `../_decompiled/` | The complete decompiled game source. **Verify every claim about game internals here.** |
| `../_reference/` | Other people's mods, for technique. Licence rules below. |
| `D:\SPT41\` | The game. Client DLLs deploy to `BepInEx\plugins\`. |
| `D:\SPT41\SPT_Runtime\` | The server. Server DLL deploys to `user\mods\ArenaMode\`. |

## Build and deploy

```bash
dotnet build ArenaMode.sln -c Release   # builds and deploys both halves
dotnet test  ArenaMode.Tests            # ~30 tests, under a second
```

All mod projects copy themselves into place on build. The tests cover the SERVER
half and the generated data; the client half is Harmony patches against Unity and EFT
types that cannot load outside the game, so **for anything client-side, verification is
still a raid.**

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
   Post-raid UI runs after IsActive is cleared. ArenaPostRaidScreens captures the
   exact ended LocalRaidSettings/profile/epoch and binds one native result
   operation before changing its warning/XP presentation. This is a UI-only
   exception; native profile refresh and result completion must remain intact.
   See POSTRAID-PAGES-REVIEW.md for both forward and health-back navigation.
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
Both switches default off. See `POLYGON-AREA-REVIEW.md` for the exact boundary.
The accepted pickup prototype extends this read-only exception to its separate
`Development - Pickups / Enabled` capture tool plus the same explicit
`AllowInNormalPmc` switch. It may preview coordinates and export new PickupDrafts;
it must never spawn loot, grant effects or automatically install a layout there.
See `PICKUPS-REVIEW.md` for per-map/area layout admission and capture shortcuts.

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
continuation of the `LocalRaidStarted` task in `LocalRaidStartedPatch.ObserveLocalStart`,
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

- **M8 polish** — KillcamReplay short arena replay (the compat exists but only skips),
  README, release packaging. Never bundle ABPS/APBS/SAIN DLLs.
- **Tactical** needs a longer playtest; only the first rounds have been played.
- **Performance** is now measured, not guessed: see LONG-RAID-REVIEW-2026-09-16.md,
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
  objects, 303 ms to count them). Read CORPSE-CLEANUP-REVIEW.md, section
  "Releasing a trimmed corpse", before touching corpse lifetime: the step order
  is load-bearing, own and PitFireTeam bodies are never released, and a body is
  never destroyed while DynamicMaps still holds its marker or while anybody is
  still owed a death by it. Measured on Labyrinth against a same-evening
  control raid: the pipeline's growth per raid minute fell from 20.6 to 13.2 ms
  and the live player-object count from 415 at 23 min to about 63 at 24 min.
  What is left is dominated by the player's OWN corpses, which are exempt by
  design. The `Player.ApplyDamageInfo` / `ClientGameWorld.ShotDelegate`
  exceptions the release introduced are explained and, since 2026-09-18, fixed
  offline (GPU acceptance: see HANDOFF.md). Every one of the 21 logged that
  evening was a TRIPWIRE planted by the dead Labyrinth boss `bossTagillaAgro`,
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
  `_allPlayersEverExisted` or its bridge twin; read CORPSE-CLEANUP-REVIEW.md,
  "Settled (2026-09-17…)" and "What held that body", before touching corpse
  lifetime. `ArenaDamageExceptionLog` (Diagnostics/`LogDamageExceptions`,
  default on) stays as the regression alarm, and `ReleaseTrimmedCorpses` still
  turns the whole release off. The heap still grows to 11 GB without a
  collection and no longer has a corpse explanation.
- **Bounding the play area** on large maps (filter `SpawnPointParams` on the per-raid
  clone).
- **PitFireTeam 5v5** — the optional 0.10.0/0.10.3 compatibility path now has a guarded
  best-of-N round lifecycle and still needs a GPU playtest. It preserves the PMC entry
  loadout after a death, rebuilds fresh follower/opponent bodies between rounds, offers
  post-death third-person spectating, refreshes both teams' pursuit knowledge, and uses
  an optional companion for exact BigBrain 1.5.0 to own bounded movement toward live opponents while
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
