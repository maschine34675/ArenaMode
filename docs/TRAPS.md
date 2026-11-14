# ArenaMode traps

Each of these cost a debugging session, and they recur. The first part is the trap list of `AGENTS.md` as it stood on
2026-10-29 (preceded by the xunit list from its Tests section; its `ModulePatch` naming rule now lives in `AGENTS.md`);
the second part collects the pitfalls that the working handoff of 2026-10-29 (`handoff-archive/HANDOFF-2026-10-29.md`)
recorded. Three of them apply to every change: never mutate `GlobalConfiguration` (two argued exceptions, below), keep
exactly one `Profile.Clone()` in the repository, and never call `GC.Collect`/`GC.GetTotalMemory(true)` from Arena code.

When you fall into a new one, add it here with symptom, cause and remedy, and name the review that argues the fix.

## Traps from AGENTS.md

The xunit tests exist because these are the things that broke repeatedly and quietly:

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

- **Doors and other world interactive objects in a local raid (2026-11-08)** —
  `GameWorld.WorldInteractiveObjects()` throws a NullReferenceException in a local game: it
  reads the `World` component, which the local game world does not carry. And
  `LocationScene.GetAllObjects<Door>()` answers an empty list without an error: the scenes
  register their doors under `WorldInteractiveObject[]`, and `GetObjects<T>` answers from a
  dictionary keyed by the exact registered type. Enumerate
  `LocationScene.GetAllObjects<WorldInteractiveObject>()` and keep the `Door`s, as
  `Modes/ArenaRoundDoorReset.cs` does; log what the walk saw, because a reset that finds
  nothing looks exactly like one that had nothing to do (docs/reviews/TACTICAL-OBJECTIVE-20261108.md).
- **Every ArenaMode build deploys the Redeploy checkout's Core (2026-11-08)** — the
  `ProjectReference` to `Redeploy.Core` builds and deploys that project, and
  `tools/AirPitChecks/NoDeploy.targets` does not cover it: a no-deploy client build still
  wrote `maschine-Redeploy.Core.dll` into `D:\SPT41`, from whatever commit the Redeploy
  checkout is on (1.0.21 that day, while the hosts ran 1.0.20), and Syncthing mirrored it.
  ArenaMode is then built against that Core and its `CoreVersionGate` refuses an older one,
  so the release bytes cannot simply be copied back. Check `git -C ..\Redeploy log -1` and the
  installed Core's ProductVersion before any build, and record both in the run's report.
- **UiBridge godmode does not survive a Tactical body swap (2026-11-08)** — the hook protects
  the Player object it was set on; a round start that hands the player a new body (a kit,
  the respawn after a death) leaves the new body mortal. Set it again after every round start
  in a test, or expect the death.

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

**Server: `TypePriority` is a sort key, not a slot.** SPT 4.1.6 runs the pre-load
`IOnLoad` callbacks (priority 0 up to, not including, 200000) in the order of its DI list,
a stable sort by priority over the registration order (SPT.Server, Reflection, Web, the
mods by GUID, Core last); negative priorities never run, and `Watermark + 1` is any mod's
natural "run first" value. Refusing a priority range instead of reading the position in
that list switched rewards off for every BetterFlea user for as long as 0.3.0 to 0.4.1
were installed (maschine34675/ArenaMode#1). Verify such claims in the installed IL
(`ilspycmd -t <Type> <dll>`), not in the reference source.

**The session helper parses the lease with a fixed member count.** `ArenaMode.Session`
(`HelperOfflineSession.ParseLease`, `Parse(bytes, 17)`) refuses a lease with an added
member, and the dialog it then shows says "Arena is waiting for SPT to finish saving",
which points nowhere near the cause. New data for the private session goes into a file
of its own in the session directory (`authority-unavailable.json` is the first), never
into `lease.json`; found natively on 2026-11-05 after an adversarial review of 49 agents
had not read the helper.

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
on Customs on both machines, so no arena map streams and that lever ships off.
And `MovingAverage` exists twice in Assembly-CSharp: the bare name is the global one, `Diz.Jobs.MovingAverage` must be
spelled out.

**The loading screen has a seam, and it is not where it looks.** The obvious place to
warm a kit early, the player-prefab preload in `TarkovApplication.LocalGameMatching`,
runs BEFORE `LocalRaidStarted` and so before the arena is armed; a hook there fires
for nothing. The first moment that is armed AND has the raid's `GameWorld` with its
raid pools registered (`GameWorld.InitLevel` inside `LoadMapAndData`) is the
continuation of the `LocalRaidStarted` task in `ArenaLocalRaidStartedPatch.ObserveLocalStart`,
which resumes on the main thread (the HTTP layer completes on it, and nothing in the
game uses `ConfigureAwait`). From there until `OnGameStarted` the game's force mode is
on and the bundle lock unlimited. Bookkeeping traps around it:
`ResetSession` runs at the start of the raid the preload was made for, so anything keyed on the preloader's
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
back on past `GcHeapThresholdGB`; see `docs/reviews/GC-THRESHOLD-REVIEW.md`.

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
actually patched). See `docs/reviews/POOL-PREWARM-REVIEW.md`, "The Harmony recipe".

**SAIN's `RunToEnemyUpdate` prefix returns `false` for every bot SAIN does
not own.** Its trailing, unconditional `return false;` skips Harmony's
call to the ORIGINAL method for any excluded bot — all four generic
infected roles included — so a Zombie-mode infected's native melee swing
never ran (25 minutes, zero player damage, first GPU raid 2026-10-06).
Never patch SAIN or race its patch order; instead patch one call site up
(`OneMeleeAttackNode.UpdateNodeByBrain`) and drive the real native body
through a Harmony reverse patch (`Harmony.CreateReversePatcher`), which
copies the original IL into a patch-chain-free `MethodInfo` SAIN's prefix
never sees. See `ZombieMeleePatch` and docs/reviews/ZOMBIE-MODE-REVIEW.md, "Native melee
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

**The player's own corpse is the source of the next body, and a corpse sweep
can get to it first.** `CorpseCleanupService` strips first-level gear from
trimmed corpses, and a round-start clear (`ClearAllSoon`, Tactical's
`StartRound`) trims the player's own newest corpse too, before the held respawn
reads it: Redeploy's EquipmentSurgeon carries the secure container, scabbard and
armband from the dead tree to the replacement, and the own-gear restore
(`TeamArenaLoadoutSnapshot`) refuses when one of those or a special pocket slot
is gone ("1 originally protected items left or disappeared from protected
slots" over the bayonet, GPU-2 2026-11-09, in the own-gear Tactical attempt that
was withdrawn the same day; it ended the match through the verified-owner
abort). The strip leaves those slots and the pockets on an own
corpse now (`StripGear(corpse, own)`); any new path that takes something from
the dead body at respawn time has to check what the sweep may have done to it
first, and a respawn that is held (Tactical, a killcam) widens that window from
a frame to seconds. See docs/reviews/OWN-GEAR-ALL-MODES-20261109.md.

**Do not detour constructed generic inventory queries on Mono.** Patching
`GetReachableItemsOfTypeNonAlloc<Magazine>` also replaced Ammo and ThrowWeap
calls with the Magazine wrapper. The original scanner then threw
`ArrayTypeMismatchException` before the postfix could check the runtime types,
breaking the weapon HUD, internal reload and G. Patch non-generic consumers and
replace only their exact Magazine calls instead. A desktop-CLR test of a postfix
cannot certify Mono generic detours. See `docs/reviews/BACKPACK-RELOAD-REVIEW.md`.

## From the handoff (snapshot of 2026-10-29)

Working notes of 2026-10-29 that carry a still-valid constraint: verbatim excerpts, or restatements where marked Quoted:/Rule:/Durable form:. Line numbers refer to `handoff-archive/HANDOFF-2026-10-29.md`.

Limits that remain: the patcher test is a search for
  the name `TargetDLLs` in the DLL (BepInEx's own rule; a file that cannot be read counts as a patcher); an unregistered
  server without a marker is not killed (`enter` refuses and `helper.log` names its pid, end it by hand); a patcher the gate cannot place
  is named by type name, not by file; the helper looks at the request file about twice a second.

*(handoff lines 440-443)*

Removing the temporary Skills package exposed its persisted foreign enum
entries in three profiles. Independently reviewed cleanup removed only the
two zero-progress foreign rows from each profile, preserving every other
byte and archiving all originals. This is not a whole-profile rollback or a
general uninstaller. See the GPU report for the recovery and final startup
verification, and AGENTS for the expanded pre-install backup requirement.

*(handoff lines 608-613)*

Recoil Rework Legacy 2.0.0+ also receives a physical-body component/cache
handoff: the published foreign Awake destroys the new player's whole object
when an old singleton still exists. Both singleton guards must be transferred
before the original initialization, and old components are retained until
authoritative rewire/recovery. See [adapter](reviews/RECOIL-REWORK-COMPAT-20261020.md)
and [published-package audit](reviews/RECOIL-REWORK-REVIEW-20261020.md); the latter also
records an independent server JSON-shape concern, not fixed by this adapter.

*(handoff lines 614-620)*

'The real ISB server was not installed because its published profile-load and quest-list hooks can mutate existing profiles outside raids.' Do not install the ISB Special Forces server mod on the synchronized test installations, even temporarily. ISB/Aishi are now refused only by the default deny list (com.samc137.isbspecialforces, com.samc137.aishi).

*(handoff lines 630-634)*

For native model/preview faults, the accepted repair built replay bodies from visual components only, with 'no native script cloning or global Unity logger muting'. Do not silence Unity's global logger to hide native NREs. No such rule exists in docs/LOGGING.md or AGENTS.md, and no Arena code touches unityLogger/filterLogType today.

*(handoff lines 843-845)*

A harness whose Verify.ps1 redirects BaseIntermediateOutputPath per probe still globs generated obj/bin sources left by an earlier direct build, unless its csproj adds `obj/**;bin/**` to DefaultItemExcludes. `47f5e51` fixed this for TacticalPolishChecks; only 2 harness csproj files carry the exclusion, while 29 tools scripts redirect the intermediate path. Pinned announcer text-manifest checks accept CRLF or LF (`029034c`), but audio hashes stay byte-exact.

*(handoff lines 1023-1025)*

Dead-code audits produce false positives. `ArenaEquipmentChoiceState.Label` looks unused in production but is asserted by two xunit tests (ArenaEquipmentChoiceTests.cs:182,184). Test-only fixtures and framework entry points must be kept. Every removal needs a fresh consumer/overload check that includes tests and harnesses (docs/reports/2026-10/DEAD-CODE-REVIEW-20261016.md).

*(handoff lines 1303-1305)*

A profile comparison can show explicitly added StackObjectsCount=1 fields (18 in the 2026-10-07 Akimbo run) and energy/water reset to 100, with no isolated cause. Do not count such a comparison as byte-identical. Repeat it (the following DM comparison was identical in all seven protected areas) and record the difference. Details: docs/reports/2026-10/GPU-AKIMBO-ALTERNATING-RESULTS-20261007.md.

*(handoff lines 2033-2036)*

A whole-profile before/after comparison counts only after the final synchronized write. An early post-raid snapshot (the Bay5 'arena-after-gg.profile.json') still differed under /characters/pmc/Skills/Common, while the flushed snapshot matched except the hideout `sptUpdateLastRunTimestamp`. Wait for the flush before declaring profile preservation (BAY5-MAP-REVIEW.md 140-150).

*(handoff lines 3046-3050)*

MapPort's EQUATOR DEBUG button starts an ordinary PMC raid and cannot certify Arena integration. Test optional maps through the native Arena mode buttons (EQUATOR-MAP-REVIEW.md 90, 121).

*(handoff lines 3077-3079)*

Expect SAIN's "Player Null from ID" lines to remain (native, a dead planter's
tripwire), and two `TriggerZone` NREs at raid teardown (pre-existing).

*(handoff lines 3186-3188)*

Factory Side is still spelled 'East' in code: ArenaAreaCatalog.CustomsFactoryEastPolygonId holds "customs-factory-side-polygon-v2", and its harness is tools/CustomsEastAreaChecks/ (the user's original draft 20260913-090513 plus its byte-hash fixture in Fixtures/). The old "customs-factory-east-polygon-v1" exists only as a saved-config migration in Settings.cs AreaSelectionValues.Clamp. It is never a valid wire ID.

*(handoff lines 3640-3651)*

The exact pre-declared GPU log signatures that are counted but do not stop a run are defined in docs/reports/2026-09/GPU-RESPAWN-STAGE0-TEST-INSTRUCTIONS-r4.md (lines ~33-60): (1) the raid-end meds-callback NRE family, whose first frame is BotSurgicalKit+CG_ApplyToCurrentPart, BotFirstAid+CG_ApplyToSelf or BotStimulators+CG_TryApply (method_0/method_1), with MedsInHandsOperation.Finish in the stack and within 15 s of native Stop/Confirm Leave; (2) the SAIN pair 'Updated player scav bot to use: bossKilla brain' + 'Init SAIN ERROR, Disposing'; (3) the profile delta /characters/pmc/Hideout/sptUpdateLastRunTimestamp. Reuse these definitions instead of widening stop exceptions.

*(handoff lines 3724-3727)*

A single Unity error 'Coroutine couldn't be started because the the game object 'MenuScreen' is inactive!' with no exception stack and no Arena/Redeploy/PitFireTeam frame appeared in menu flows (r2, 2026-09-13). It is counted but does not stop a run while the menu stays usable. Its cause is still open.

*(handoff lines 3761-3763)*

- **Bot Surv12 drop diagnosis (2026-09-12):** the user sees PMC bots start a
  Surv12, stop, and leave it on the ground (Arena). Two verified mechanisms,
  neither in Arena/Redeploy code: (1) ContinuousHealing 1.6.3
  (`com.lacyway.ch`, installed 5./6.9.) keeps a static `CancelRequested` set
  by the LOCAL player's heal cancel and applies it in its
  `MedsInHandsOperation.OnEffectRemoved` prefix to every player before its
  `IsYourPlayer` check, so bot surgeries end at their next effect removal
  until the player starts a new heal; (2) SPT `pmc.json
  forceHealingItemsIntoSecure = true` (honoured by APBS) puts PMC healing
  items into the secure container, and EFT's `BotRandomPlanItemDropper`
  (bear/usec `DO_RANDOM_DROP_ITEM`) throws a random secure-container item on
  every peaceful-move start — i.e.

*(handoff lines 3768-3779)*

To attribute a recurrence of bots dropping or abandoning meds, use the log-only plugin maschine-BotDropDiag (repo ../BotDropDiag, HEAD 696d100). It is not installed in D:\SPT41\BepInEx\plugins today. It logs THROW (with container path), DROPPER, SURGERY-START and MEDS-FINISH (with CH.CancelRequested), each with its stack, and changes nothing in the game. Install it only for a diagnosis and remove it afterwards.

*(handoff lines 3780-3782)*

Do NOT set `Patrol.DO_RANDOM_DROP_ITEM` on an arena bot's
`Settings.FileSettings`: `BotSettingsComponents.Copy()` is shallow and
`Patrol` is a process-wide cached instance per (difficulty, role), so the
change would leak into normal raids.

*(handoff lines 3797-3801)*

**Restart the game**, since healthy announcer PCM remains cached between
matches; no server restart is needed. Native listening remains for the user.

*(handoff lines 3915-3917)*

Native GPU evidence is valid only for the exact client/server hashes it recorded. A documentation-only commit leaves an already built DLL's identity unchanged, but any rebuild after it changes the hash, because ProductVersion embeds the commit. Example: Core FA452E5A was rebuilt at docs-only Redeploy 287c2d9 (lines 3880-3881). Rebind instructions to the new hashes before comparing evidence across builds.

*(handoff lines 3966-3971)*

"`0be7f7a` (friendly diamonds projected through applied native SSAA state into final DLSS output)", review pointer FRIENDLY-MARKERS-REVIEW.md (line 4097). Durable form: under SSAA/DLSS upscaling the game camera renders into a smaller native viewport, so Camera.WorldToScreenPoint does not return final display pixels for the ScreenSpaceOverlay HUD. Screen-space markers must remap through the applied SSAA state; see docs/reviews/FRIENDLY-MARKERS-REVIEW.md.

*(handoff lines 4062-4063)*

The installed server
  matches the current Debug artifact, not the separate Release artifact; do not
  substitute a Release hash for the actual installed file. The later server
  commit changes the weapon ladder draw, with builds predating that commit.
  Independent read-only review matched the installed server's CodeView identity
  to its portable PDB and its LadderPicker document hash to current `939d564`
  source: the last production change is included despite the older embedded id.

*(handoff lines 4107-4113)*

Implementation `3010346e23f802ae40d58669d12b4327fa86b1fe` detaches from known
active screens before native Close; fallback hide/destroy callbacks defer
structural work to the independent guard. New attachments drain old cleanup
before snapshotting native flags and preserve their already-admitted widget
request. Destroyed Unity hosts fail open. Camera/posing/gameplay/audio unchanged.

*(handoff lines 4125-4129)*

"A bounded legacy-id audit also found and fixed the missed canonical Deathmatch admission for point/beam laser brightness (`2066950`). Exact raid/body ownership, unknown-mode refusal and absence of solo team tint remain." Durable form: any per-mode whitelist must admit the canonical DeathmatchSelection.ModeId, not just the legacy endless/rounds ids, which still parse but are saved as deathmatch. A legacy-only list silently disables the feature. Keep unknown-mode refusal (see DEATHMATCH-MENU-REVIEW.md, 'Canonical Deathmatch laser brightness follow-up').

*(handoff lines 4165-4167)*

- **Quick-slot ledger survives the protected 5v5 rebuild (2026-09-12):**
  pre-existing defect from an adversarial review. `AutoQuickSlots` remembered
  its own bindings as `Item` references and counted a key as player-cleared
  only while that object was still on the body. `TeamArenaLoadoutSnapshot`
  reissues rig/pocket/backpack items under fresh ids between rounds and
  carries the FastAccess bindings over to the new instances, so a key the
  player cleared after a round rebuild read as "item gone" and refilled
  within a second; a key the player had filled themselves from a protected
  special pocket slot was never the service's and stayed cleared, hence the
  visible asymmetry. New pure `Kits/AutoQuickSlotLedger<TSlot>` (linked into
  the tests like `BotLoadBoostPolicy`): ids instead of objects, an occupied
  key adopts its current occupant, a key that empties is a clear iff the
  remembered id is still on the body. `ArenaRespawnDriver.RespawnProtectedTeamPlayer`
  settles the ledger against the dead body before `TryCommit` (the poll stops
  at the death, so its last second is unjudged) and remaps ids with
  `NextRuntimeByPreviousRuntimeId` only after a settled commit. The first
  review found that remapping an unsettled ledger turned a medkit used up in
  the last second before the death into a permanent player clear, because
  the map names every entry-loadout item; the settle closes that. Solo modes
  unchanged except one edge: an item the player bound over an owned key and
  later used up no longer counts as a clear. 17 ledger tests; mutants verified
  (adoption off: 2 red; emptied keys kept owned: 8 red).

*(handoff lines 4253-4274)*

The final unchanged-delivery check FAILED: client `7965FEB2...` and server
`4F8453B4...` replaced the initial files. Their recorded 07:10:49/07:10:58 UTC
write times are after both raid ends and the 07:06:06 main-menu image. This
supports attribution to the process started on the verified baseline but is
not a measurement of loaded assemblies; the later DLLs receive no GPU
acceptance here. `cbfc582` concerns protected-container handling and leaves
the killfeed/HUD/team-registry source unchanged. Preserve the report's drift
caveat and separate any later test rather than retroactively changing its hash.

*(handoff lines 4306-4313)*

"A normal deploying build's DeployArenaAudioAssets is consistent with this replacement ... The audition is therefore mixed until explicitly reapplied or fully rolled back" (also 4231-4233 "after deployment restored three original voices" and 4394-4396 "Deploying a later build can overwrite three trial clips but leaves the two added clips behind"). Durable form: every normal deploying build runs DeployArenaAudioAssets -> tools/Write-ArenaAssets.ps1, which rewrites every tracked owned asset in BepInEx/plugins/maschine-ArenaMode whose bytes differ and leaves untracked extra files in place. A local-only installed audio/asset override is therefore silently reverted by the next build, while files added by hand stay behind. Assets that must survive go into the repo; otherwise use NoDeploy.targets.

*(handoff lines 4314-4319)*

QuestTracker/startup RenderTexture, SAIN teardown and BotWeaponSelector errors
do not establish UI causation; no speculative fix to them is included.

*(handoff lines 4445-4446)*

- **GPU panel/native UI failures corrected (2026-09-11):** test report
  `734af45` and the user's follow-up log reject the original UI delivery.
  Independent native diagnosis found two separate regressions: inactive-parent
  TMP construction in ArenaInfoPanels, and rejected duplicate CanvasGroup
  additions on PointsPanel (1,826 lines in the captured user log). Build the
  panels synchronously while active, hide before returning; borrow and restore
  the existing list group's alpha/enabled values instead of adding a component.
  Missing/failing native groups now fail open and are quarantined per list/raid;
  once-only full exceptions and construction/masking markers aid diagnosis.
  Both regression harnesses now model the previously missed Unity contracts.
  Panel tests still substitute TMP views and do not certify rendering.

*(handoff lines 4574-4584)*

- **Grenade pick after widened reach (2026-09-11):** fixes the pre-existing
  defect handed over by the grenade-row review. A grenade bound and drawn from
  outside the tracked rig/pocket top level (the backpack without the installed
  UseItemsAnywhere mod; nested pouches with it) stayed
  `Equipment.TopPriorityGrenade` after it was thrown, so every tap of G fell back
  to the weapon until hold-G. `ArenaReachPatches.cs` adds a postfix on
  `FastAccessGrenadeItemView.OnItemRemoved` (the pick, or a container holding it,
  left and is no longer reachable: run the view's own `SetNewTopPriorityGrenade`)
  and a void prefix on `PlayerInputTranslator.vmethod_1` (clear an undrawable pick
  so the native no-pick branch runs).

*(handoff lines 4600-4610)*

Native clips top out at 1.30m vertical travel;
  Arena had admitted up to 1.70m. Additional airborne Climb now waits for at most
  1.31m remaining height, so held input can retry as the jump rises. Standing
  reach and Vault preference remain unchanged. This corrects a proven unsupported
  range, not a proven explanation for all eight drops. See AIR-MOVEMENT-REVIEW.md
  for native asset/grid-coordinate evidence and the frozen log hash.

*(handoff lines 4828-4834)*

Installed UIFixes routes R through native PullOutMag/InsertMag operations if
there is no free magazine slot in the rig. Those two exact operation types were
excluded from both speed and diagnostics; they are now included. Native swap
inventory/callback behavior remains, with owned speed restored before all five
completion/reset boundaries. No UIFixes dependency or setting change. This also
accelerates the same explicit inventory magazine actions inside Arena. Direct
chamber/cartridge work, unknown types, malfunctions, AI and normal raids remain
excluded. Additional native swap transitions can still make it slower overall.

*(handoff lines 4864-4872)*

All 47 detailed mantle dispatch refusals showed a valid Jump_Move/Sprint owner,
no transition, IsJumping=true and Landing=false, but FallingDown=true. The native
MovePlayerState can leave this bool set during a genuine jump; only FallDown.Exit
clears it. Removed only that unreliable bool guard; exact managed Jump/animator
ownership, landing, geometry, stamina and hands guards remain. Real FallDown
stays excluded and the bool is not modified. Alternating queue/refusal reasons
now have separate log throttles instead of consuming all 100 lines immediately.
See both movement/reload review documents for native sources and evidence.

*(handoff lines 4873-4881)*

The user subsequently confirmed reload acceleration works. Their fast/slow
weapon list compared different kits at one fixed setting, not matching reloads
at 1 versus 3. Weight does not appear in the inspected native reload-speed path;
weapon/magazine/mastery/skill and reload variant preserve native differences.
Both slow/fast groups bind SpeedReload. No speculative timing replacement was
made. New bounded `Arena reload diagnostic` lines record weapon template,
factor, original/written/readback values, real Unity hands speed multiplier,
refusals and completion. F12 is live; web config is not polled in active Arena.
Compare the SAME weapon/magazine/reload type with F12 1 and 3 before comparing
different kit weights. See `RELOAD-HANDLING-REVIEW.md`.

*(handoff lines 4918-4928)*

**Airborne mantle still fails:** this run uses UnityAnimatorWrapper, so the
previous FastAnimator-only fix rejected every logged attempt before queuing.

*(handoff lines 5105-5106)*

- **Movement correction and bot-log diagnosis (2026-09-09, later follow-up):**
  the user explicitly reports airborne mantle still fails, including a second
  Space press held after jumping. This supersedes the earlier broad acceptance
  below: absence of errors never proved an actual Jump-to-Climb transition.
  The installed animator graph has no Jump-to-Climb/Vault edges.

*(handoff lines 5124-5128)*

Known Customs invalid-nav spot `(464.91, 1.05, -43.64)`. It is not a spawn, and it lies inside today's Customs Factory Center polygon, about 25 m from the border. Bots were repeatedly stranded there for about 182 s while cached SAIN OnNavMesh reported on-mesh (the sampled NavMesh hit was about 0.94 m away). The geometry issue was kept open (lines 5064-5066, DURATION-DIFFICULTY-REVIEW.md line 92-95). Do not remove a spawn, shift the border or teleport bots on that evidence. An active-search stall there still needs SAIN ActivePath/corner diagnostics.

*(handoff lines 5150-5158)*

The sole error is a SAIN `BotComponent.OnDisable` NRE during
  native pool/world unloading at 12:42:17.339, after profile persistence; no
  movement hook is in that stack. Its precise null/cause remains unattributed.
  Retain the isolated activation and unload findings for follow-up, without
  speculative patches.

*(handoff lines 5181-5185)*

In the ordinary PMC control raid of a GPU acceptance, ending with LEFT loses gear and changes progression natively. Record that separately and never count it as an Arena profile-protection failure (also lines 5417-5418; GPU-RETEST-2026-09-08.md, GPU-ACCEPTANCE-2026-09-08.md).

*(handoff lines 5367-5369)*

**Build and dependency traps**

- Keep the normal layout when practical: `<SPT>/Development/ArenaMode`, sibling
  `Redeploy`, `_decompiled`, and the needed private `_reference` sources.
  ArenaMode does not contain those other repositories or game DLLs.

*(handoff lines 5855-5859)*

- The client references `../Redeploy/Redeploy.Core` relative to the Arena repo.
  Verified Redeploy checkout at handoff: `c938f5c43cb311a8a9ba1f81b2eb10573b99b936`,
  clean (stage 0 of the respawn hardening; older Core builds refuse nothing). Transfer compatible source separately if building; do not assume a DLL
  alone satisfies the project reference.

*(handoff lines 5860-5863)*

- `Shared.props` resolves the game root two directories above the repo and the
  server root under `SPT_Runtime`. In a different layout, verify/override both
  `SPTPath` and `SPTUserPath`. Local `Shared.props.user` is imported and ignored by
  Git, so it must be configured separately on a new machine. Redeploy has its own
  props/deployment target. Check its effective paths too. With both repos under
  `C:\SPT41\Development`, relative defaults already resolve to C:; only override
  where necessary, and inspect mirrored `.user` files for stale D: paths.

*(handoff lines 5864-5870)*

- Builds auto-deploy, including the server build triggered by `dotnet test`.
  A generic `DeployToSpt=false` switch is not honored by the current Arena targets.
  Do not build from an arbitrary worktree until paths, references and deployment
  ownership are settled. Never run concurrent builds into the same installation
  or shared Redeploy output. Prefer Release; rebuild the client last if another
  check rebuilt the shared dependency in Debug.

*(handoff lines 5871-5876)*

Many harnesses use local `_decompiled` sources, native DLLs or D: defaults;
inspect their parameters before running elsewhere. Set `SPT_DATABASE` to the
actual `SPT_Runtime/SPT_Data/database` **directory** for server tests. This is
separate from a harness's `-Database` parameter pointing at `templates/items.json`;
the nested server tests do not automatically inherit that parameter. Require
the expected tests to run, not silently skip. On the GPU host the environment
variable is `C:\SPT41\SPT_Runtime\SPT_Data\database`.

*(handoff lines 5881-5887)*

- Keep licensed audio and attribution together with client assets; use the
  build's asset deployment or a complete verified package, not a guessed DLL set.
  The full solution also needs the installed BigBrain reference DLL. Optional
  runtime BigBrain/PFT/SAIN dependencies must match the actual compatibility
  checks; re-read source and installed versions rather than trusting old notes.

*(handoff lines 5888-5892)*

## Verification runner (2026-11-13)

`tools/Run-ArenaVerification.ps1` keeps a result cache (`tools/ArenaVerificationInputs.ps1`, proved by
`tools/VerificationRunnerChecks`). Its argued exceptions, each a place where a wrong rule would return a stale PASS:

- **What a key leaves out.** Records under `docs/` (everything but `docs/licenses/`) that no reached file names by
  a docs/ path, a unique file name or a quoted "docs" segment; harness folders the step does not reach (a bare
  folder word reaches the whole folder, a folder plus a file path that file); loose `tools/*` entry scripts that
  nothing reaches; ignored files (build output, or regenerated per run); path literals in production code and in
  the xunit sources (test data); the runtime state mods write into their installed folders (logs, APBS
  `wwwroot`, Redeploy's `data`, PitFireTeam teammate databases, SAIN `EditorDefaults.json`). The reviewed payload
  scripts (`$ArenaPayloadLayoutFiles`) name the payload's docs folder, not records, and their enumerations walk
  payloads. `docs/README.md` is a record although two files name it, because both mean the payload copy of the
  root README (`$ArenaPayloadOnlyRecordNamers`); any other mention makes it an input again.
- **A reached file that enumerates directories makes its step wide** (every folder and record in the key), about
  50 of 220 folders. That is deliberate: an enumeration of the checkout cannot be told apart from one of an
  installation. `tools/Verify-PitFireTeamDeployment.ps1` walks the whole checkout (`git ls-files`, every project
  file); the adversarial review of 2026-11-14 found it wrongly listed as a payload script. Only the xunit sources
  can make every step wide; a shared helper does so for the steps that reach it.
- **Fixtures under `artifacts/` are inputs only when registered** (`$ArenaArtifactInputs`): PitFireTeam100Checks
  (the PFT 1.0.0 release), PortedCoverChecks (the decompiled installation), SvmCompatChecks and
  UseItemsAnywhereChecks (downloaded releases). Every other `artifacts/` path is output; keying output folders
  that gain a GUID folder per run would make those steps uncacheable. A harness that starts reading a fixture
  from `artifacts/` registers its prefix there.
- **Paths outside the key's literal scan**: a variable that names a path (`ARENA_APBS_TEST_ROOT`,
  `SPT_REWARD_RECOVERY_BASELINE`) keys that path's content; a quoted absolute path may contain spaces
  (`'C:\Program Files\Unity\...'`; a folder over 20 000 files makes the step uncacheable); the tools python, node,
  ffmpeg, ilspycmd and mono count by the file they resolve to; `Directory.Build.*`, `NuGet.Config` and `global.json`
  above the checkout and the user's NuGet configuration count by their bytes. Ignored `*.user` files are not
  read by any CLI build here and are not keyed.
- **Found by the first real pass with these rules (2026-11-14):** every one of 62 steps was uncacheable, because
  `BotEffectShaders/Build.ps1` names the Unity editor folder (over 20 000 files) and an asset's provenance
  manifest made that script shared. External paths now count for every step only where shared files name them,
  and an installed application under Program Files counts by its top three levels (a new Unity version is a new
  folder). The new register `tools/verification-steps.txt` names 144 harness folders and, reached as a shared
  file, pulled them all into every key: it is in no key now (`$ArenaNotStepInputs`), and the runner's own files
  are not followed, so `AirPitChecks/NoDeploy.targets`, which the runner passes to every build, is named
  explicitly (`$ArenaRunnerInputs`).
- **Registered fixtures travel with the workers.** A worker mirrors only non-ignored files; IcebergChecks and
  ChopShopChecks run PortedCoverChecks, which reads `artifacts/pft-installed`, and failed in every worker. The sync
  copies the registered `artifacts/` prefixes from the checkout under test. A checkout without the fixture fails
  those steps as before (`tools/Decompile-PitFireTeam.ps1` recreates it).
- **Changing these rules**: add a case to `tools/VerificationRunnerChecks/Verify.ps1` that changes the input and
  expects a rerun, and a fault that removes the input and must fail that case.
- **The key and the selection differ on purpose.** The key is coarse (every shared file, so any production change
  reruns every step) because a miss there is a stale PASS. `-ChangedSince` is fine-grained (evaluated MSBuild
  items of the step's projects, production paths its scripts name, the production projects it builds or tests)
  and only advisory, like a hand-made selection; a selection miss costs a check that did not run, never a
  stale PASS. The register `tools/verification-steps.txt` lists what may be selected; an unregistered harness
  folder that changed is reported, not run.
- **The runner's own files name paths only to state the rules** (`$ArenaKeyInfrastructureFiles`): their mentions
  are not followed. Following them once pulled 165 harness folders into the reach of VerificationRunnerChecks
  and made `docs/README.md` an input of every step.

Parallel steps and mutants (`-Parallel`, `tools/ArenaVerificationWorkers.ps1`, `Test-ArenaSourceFaults`):

- **Never run two steps side by side in one checkout.** About 30 harnesses run `dotnet test` on ArenaMode.Tests,
  13 build ArenaMode.sln and 9 build ArenaMode.Client through `DoubleJumpChecks/Build-Staged.ps1`, all into the
  production projects' own obj/ and bin/; several write fixed `artifacts/` paths. The workers are separate
  worktrees for that reason, and they must be SIBLINGS of the checkout: 31 harness paths leave the repository
  relatively (`..\..\..\..\BepInEx`, `..\_decompiled`, `..\Redeploy`). Only `../Redeploy` stays shared; steps that
  build the client or the solution (Redeploy.Core through the project reference) take the mutex
  `Global\ArenaVerification-RedeployCore`.
- **A copied file keeps its old write time.** `File.Copy` preserves it, and a source file edited yesterday is older
  than a worker's obj/ output from this morning, so MSBuild would call the stale build current. The sync sets the
  write time of every file it copies to now.
- **The live-install guard is shared.** A deploy fails every step that ran since the last clean check, and the
  steps still running when it was seen, even if someone restores the file before they end; the verdict names
  them all.
- **A mutant costs process start, not compilation**: about 2.9 s per `dotnet run`, of which the compiler takes
  0.17 s and the program 0.12 s. The helper restores once (the baseline `dotnet run`), then builds each mutant
  with `--no-restore` into its own `IntermediateOutputPath`/`OutDir` and runs the DLL, `ARENA_MUTANT_PARALLEL` at
  a time (default 8; the runner sets cores / (2 x workers)). Side by side cannot let a survivor through: each
  mutant still needs its own non-zero exit and its own `FAIL: <name>`.
- **PowerShell names are case-insensitive.** `$worker = Get-ArenaRepoSnapshot $Worker` inside a function with a
  `[string]$Worker` parameter assigns to the PARAMETER, and the type constraint turns the snapshot into a string.
- **Never start the runner inside a worker for a parallel run.** A worker checkout runs its steps sequentially,
  and a worker is never synced onto the checkout under test: the first version took slot vw2 for itself when it
  was started in ArenaMode-vw2, and the sync's `checkout --force` and `clean` wiped the uncommitted work there
  (review 2026-11-14). Edit and commit in your own checkout, never in a worker.
- **A fresh worker has no restore.** Several harnesses run `dotnet test ... --no-restore` on ArenaMode.Tests or
  `dotnet run --no-restore` on their own project; the sync restores those projects in a worker when their
  `obj/project.assets.json` is missing or older than the project file.
- **A step that does not start is a FAIL.** `$LASTEXITCODE` keeps the value of the last native command (0 from a
  git call), so a missing executable would have read as a PASS and been cached (review 2026-11-14). The runner
  sets it to -1 before every step, and a thread job reports an explicit exit code or -1.

Traps met while building it:

- **PowerShell passes `$null` to a .NET string parameter as `""`.** `[Environment]::SetEnvironmentVariable($k, $null)`
  therefore leaves an EMPTY variable behind instead of deleting it, and a child process inherits it. Use
  `Remove-Item Env:$k`; the environment fingerprint counts an empty variable as absent.
- **The comma binds before `+`.** `'-p:' + $name + '=' + $copy, "-p:Other=x"` is `'-p:' + $name + '=' + ($copy, "-p:Other=x")`:
  a single argument with both values joined by a space. Parenthesise the concatenation.
- **A function's `Write-Output` is its return value.** A helper that prints a verdict and returns `$true` used as
  `if (Helper)` swallows the verdict line; return the text and let the caller print it.
- **Installed folders move during a test day.** Mods and the server write logs and state into their own folders,
  ArenaMapPort deploys assets several times a day, and Syncthing mirrors both from the GPU hosts. A key over whole
  installed folders went stale every few minutes; the runtime-state pattern above is what was observed
  (`Get-ChildItem -Recurse | Where LastWriteTime -gt ...`), and the verdict of a rerun names the installed part that moved.
