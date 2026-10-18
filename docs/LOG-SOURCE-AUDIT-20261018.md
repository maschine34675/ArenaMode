# ArenaMode, Redeploy and KillcamReplay log attribution — 18 October 2026

Status: source review, fresh three-scenario GPU2 log attribution and four-pass
review complete. The tests are not clean-log acceptance: the known PFT exit
defect recurred, and native asset/animation diagnostics remain unresolved.
No production source, sibling repository, logging configuration or GPU state was
changed by the source auditor.

## Scope and evidence rules

Reviewed ArenaMode baseline `d2e5f37`, Redeploy `bf227f2` and KillcamReplay
`ad9eea5`; both sibling repositories were clean when inspected. Neither sibling
has `AGENTS.md`; KillcamReplay's `CLAUDE.md` was read. Arena's `AGENTS.md` and
`docs/LOGGING.md` remain the logging contract. Source and native decompilation
establish call paths; they do not alone prove which runtime object caused an
exception. Loaded identities and the fresh run's acceptance belong to
[the native report](GPU-LOG-AUDIT-20261018.md).

Private emitter inventories are in `artifacts/log-source-audit/`. Raw profiles,
account IDs, private log exports and their paths are not release assets. Historical
evidence is the immutable exports under the sibling
`ArenaMode-pft-tactical-kits/artifacts/pft-tactical-native/`; the documented run
is [PFT Tactical kit verification](GPU-PFT-TACTICAL-KITS-20261018.md).

The merged log stream normalizes timestamps. On this date client local time is
Europe/Berlin UTC+02:00; raw SPT server timestamps are UTC. Bound the server by
the client process/test window, because one server file spans multiple clients.
Count unique original `(stream,file,line/seq)` records, then group signatures. The
same incident may be mirrored in EFT and BepInEx; those are not necessarily two
faults. A merged output is not evidence of complete coverage if it is truncated
or a source could not be read.

## Fresh GPU2 evidence through Deathmatch and AirPit TDM

The current BepInEx log begins at 13:56:45 local time, instance prefix `14e1597f`;
UiBridge reports its startup identity at 13:56:53.
The native agent's `through-tdm-logs/combined.json` contains **10,259 unique
original records** through 14:13:09.150: 21 pages, final page 259 records with no
continuation, all three sources available on every page, and no `cutOff` record
texts. Intermediate page/display truncation flags mean another page was needed;
they do not mean the final structured export has lost the record text. The EFT
traces source includes Application, Backend and errors. Exact same-second/text
cross-stream grouping found no mirrored records in this capture.

| Source | Warning | Error | Result in this bounded window |
| --- | ---: | ---: | --- |
| BepInEx | 27 | 0 | Five Redeploy stale-sampler recoveries, eight dependency/Harmony startup warnings, fourteen native Unity warnings. |
| EFT traces | 0 | 89 | Native asset, animation and scene-contract diagnostics; breakdown below. |
| SPT server | 20 | 0 | Four companion profile-preview fallbacks, fourteen Fence plate-slot warnings, two profile StackObjectsCount repairs. |

No ArenaMode or KillcamReplay Warning/Error occurred in these two matches; the
five Redeploy warnings align with two initial kit swaps and three real deaths.
This does not certify every gameplay path or the later PFT scenario. The native
agent observed real enemy deaths twice in Factory Deathmatch and once in AirPit
TDM, followed by killcam and successful respawn. Killcam playback spans were
2.96/2.92/2.95 seconds; the roughly eight-second respawn holds include the other
death/replay transitions and must not be described as eight seconds of playback.
Both raids returned normally to the menu. Native screenshots and profile checks
are owned by the linked native report, not inferred solely from log silence.

The 89 EFT Error records are: 44 `JetMaterial`, ten animation hash-zero errors,
seven Benelli foldable errors, two custom clipping-type errors, two utility-event
timeouts, four missing WeatherObstacle instances, two Transit diagnostics,
fourteen spawn reconciliation records, one wind-settings error, one spatial-audio
initialization diagnostic, and two missing season-support instances. The old
preview NRE and PlayerBody-disposal family did **not** recur before this snapshot.
PFT preview/exit coverage had not yet run in this snapshot; the final section
below records its outcome separately.

### Newly observed emitters and bounded attribution

| Family | Concrete emitter/path and current evidence | Conclusion |
| --- | --- | --- |
| `Unknown custom item type: 0` (2) | `_decompiled/CustomItem.cs:75` / `CustomItemSkin.cs:38` reject an undefined **clothing clipping** `EClippingCustoms` value. `EFT.Visual/Dress.cs` and `SkinDress.cs` initialize these components. Zero is not a defined enum flag. One entry follows a PMC bot arrival at 14:05:27; one follows the local Wraith-kit body replacement at 14:09:15. | This is not SPT's similarly named CustomItemService and is not evidence against Molotov item registration. Arena-triggered equipment/body initialization is a plausible indirect entry path; the faulty dress/prefab is still unidentified. Capture that object before changing item data. |
| `no foldable found on weapon_benelli_m3_s90_12g_container(Clone)` (7) | `_decompiled/EFT.Visual/FoldableStockView.cs:26–40` (and RetractableStockView) requires a FoldableComponent during non-animated `IDress.Init`. First occurrence is during kit preload; later entries occur around generated weapons and a respawn. | Native prefab/template contract mismatch. Preloading or generated gear can expose it, but no supplied stack identifies the exact caller or responsible mutation. Preserve error and isolate this weapon/prefab before a fix. |
| `Destroying stale PerfectCulling sampler FPS Camera` (5) | Redeploy `PerfectCullingFix.SamplerPatch` deliberately destroys the exact previously registered sampler while the replacement starts. Two kit swaps and three genuine respawns complete. | Proven Core recovery on Arena-triggered body replacement. Successful, expected eviction is a candidate for Debug; failed or ambiguous camera/sampler repair remains Warning/Error. |
| `BattleUIScreenShowPatch` Harmony warning | `_reference/SPT-DynamicMaps-main/Plugin/Patches/BattleUIScreenPatches.cs` owns this named patch. Other named warnings map to UIFixes DialogWindow/WeaponModdingScreenCursor and WTT CommonLib. | Not an Arena patch merely because it targets the same vanilla screen. Retain owner-specific compatibility diagnosis rather than globally muting Harmony warnings. |
| AirPit WeatherObstacle (4) | `_decompiled/DepthPhotograper.cs:159` expects a scene instance. ArenaMapPort `RainCoverRepairs.cs:65–70` explicitly documents that the port leaves it absent and leaves this once-per-camera-change Error visible. | Ported-map scene limitation. Redeploy's new camera causes one further check, but it does not create the missing scene contract. Not a reason to suppress all camera errors. |
| AirPit wind/spatial/seasons (4 total) | Native `WindController.cs:76`, `SpatialAudioSystem.cs:294`, and `SeasonsController.cs:97,112` report missing or fallback scene support during AirPit initialization. | Map integration diagnostics. No observed failure of the tested TDM flow; no proof that every environmental feature is correct. Current MapPort source is a reference, not a substitute for the loaded build identity. |
| AirPit three marker-position repairs | Native FixSpawns restores backend coordinates; the MapPort Debug records then show `SpawnPlacementRepairs.Apply` moving the same three common AirPit south markers by 1.5 m to valid positions. | Concrete scene/backend/placement-repair interaction, not a lost Arena team member. Native reconciliation is still required. Its three-deleted/38-added summaries must be read as batch diagnostics, not 41 independent exceptions. |

The Factory negative-scale BoxCollider warning and twelve missing-script warnings
remain native prefab/scene issues without a managed object-owner stack. The single
`Animator.GotoState` warning occurs at normal Factory exit. These timings support
scenario association; they do not prove Arena or Killcam created the faulty asset.

### Current output volume

There are 7,785 BepInEx Info records in this startup-plus-two-match window;
**6,064 HarmonyX + 1,193 ModulePatch startup records account for 7,257 of them**.
Arena emitted 13 Info and 570 Debug records, Killcam 59 Info, and Redeploy.Core
38 Info plus five Debug. These raw counts cover different responsibilities and
are not a performance measurement. With Debug included, MapPort contributed
another 462 records. Do not label already-Debug placement/probe output as an
unconditional Arena Info regression.

The server records **36 successful `/arenamode/client-settings` polling requests
at Info**, plus three separate `/push` requests. BepInEx has only the corresponding
six RequestHandler request/success lines for those three pushes; periodic GETs
already use Arena's quiet HTTP path. Remaining polling noise is thus in SPT's
middleware, not the old client wrapper. An endpoint-specific successful-request
policy could reduce it without changing polling cadence or hiding failures.

## Final capture and PFT result

The final export is **11,436 unique original records** from 13:56:45.293 through
14:22:41.063 local time. All three original files are archived unchanged by the
native tester under `raw-logs/`. The 23 paginated pages were followed by
`final-poll.json`: nine additional Info/Debug records, `continue:null`,
`hasMore:false`, no omitted-before-page flag. This resolves the previous final
page's `hasMore:true` display indication. Every source was available. One EFT
**Info** Control-settings dump exceeds the tool's 20k text limit; its full text
is in the archived EFT file. No Warning/Error text is truncated. This distinction
matters despite the tool's separate `cutOff` count being zero.

| Final source | Warning | Error | Delta from DM/TDM snapshot |
| --- | ---: | ---: | --- |
| BepInEx | 51 | 1 | One more successful Core sampler eviction; eighteen missing-script warnings; another Factory BoxCollider warning; three live Arena goal-handoff refusals; one false exit roster Error and its no-contest Warning. |
| EFT traces | 0 | 120 | Twelve JetMaterial records, four PlayerBody-disposal errors, six utility-event timeouts, eight Factory spawn reconciliation records and one Transit diagnostic. |
| SPT server | 27 | 0 | Seven more Fence plate-slot warnings. No Arena server exception or restore failure. |

No exact same-second/text cross-stream mirrors were found, and pagination added
no duplicate original keys. There are **121 Error-level records**, not 121
independent gameplay failures. Repeated native signatures and the exit
Error/Warning consequence pair must be assessed by their call paths.

The ordinary PMC four-companion confirmation and Arena four-companion
confirmation both showed complete models. The earlier four preview NREs did
not recur anywhere in the new process. The four `PlayerBody` disposal errors
**did** recur together at 14:19:05.749, immediately before Arena session
activation and well before local death/replay/respawn. At this entry Arena
explicitly skipped its initial body swap to preserve PFT's leader. This narrows
the event to the startup/model lifecycle; it still lacks object IDs/owners and
does not prove that either preview implementation destroyed those bodies.

PFT round 1 became live at 14:19:15.313. The native tester fired twice and used
UiBridge's native local-kill hook to exercise observation and round rebinding;
this is **not** a claimed fourth enemy-caused death. Round 2 became live at
14:20:32.241 with four freshly bound companions and five opponents, after exact
local-body replacement. The native report verifies the retained supplied kit,
unchanged companion gear and restored profile/PFT files.

Three **live** warnings at 14:19:52–54 are separate from the later exit defect:
`Team Arena native awareness rejected ... native-goal-setter-refused`.
`TeamArenaGoalHandoff.TrySetGoal` enters the PFT context, assigns
`Memory.GoalEnemy`, then validates the exact requested reference. It deliberately
does not clear or repeatedly force the field if another decision owner vetoes
the request. The logs show one follower declining three distinct requested
targets while retaining another goal. They prove rejected handoffs, not that
the follower or whole team was immobile. `TeamArenaAwarenessDiagnostics` reports
each new target/reason once, then repeated reports at most every 30 seconds per
source/channel; pending counts rise 1→3 in this sample. **Keep these warnings**
as live degradation evidence. A later movement fix needs the current goal and
PFT mission state, not removal of the veto or of its warning.

At 14:20:49.976 Arena committed intentional native `Stop(Left)`;
14:20:49.978 produced the known missing physical roster Error and
14:20:49.987 the false round-2 no-contest Warning. Restoration still completed.
This freshly reproduces the lifecycle defect described below, so the report
does not present the UI-only candidate as fixing it.

Final routine-output totals: Arena client 24 Info/695 Debug, Core 44 Info/six
Debug plus six recovery warnings, Killcam 64 Info and no Warning/Error.
HarmonyX and ModulePatch contribute 7,276 of 7,963 BepInEx Info records across
the whole process. Server periodic client-settings Info now totals 84; explicit
pushes remain three, with six client RequestHandler records. The GPU's unchanged
configuration confirms Disk/Console `LogLevels=All` and Harmony
`LogChannels=Info,Warn,Error,Debug`. Simply demoting technical Info to Debug
would respect the normal contract but would not reduce this particular full log.

Recommended order: (1) exact native-stop roster/progression guard; (2) opt-in
Core pipeline measurement and quieter successful Core/Killcam setup details;
(3) object-scoped capture for repeated PlayerBody, weapon-material, clipping and
animation failures. Preserve live awareness, protection, persistence and real
failure alarms. No runtime correction or logging change was made in this audit.

## Proven Arena issue: intentional PFT exit still polls a live roster

The earlier own-gear control emitted the physical-roster error just after native
`Stop(Left)` at 11:43:07; the supplied-kit run did the same at 11:59:39.953.
Both exits completed and restored the protected profile and all twelve PFT store
files. This is not specific to supplied kits. It is still a real Arena lifecycle
bug: it fabricates a no-contest after the user has already left, rather than an
actual in-match roster loss.

The exact failing emitter is
`ArenaMode.Client/Compat/PitFireTeamCompat.cs`,
`LatchRuntimeRosterProblem`: `Arena rejected the physical PitFireTeam roster`.
`TryGetLiveFollowers` compares the current wrappers with the frozen bodies and
latches `the living bound PitFireTeam body ... disappeared` when a formerly
living bound body is absent. Those checks are necessary while the match is live.

`PitFireTeamCompat.Tick` already avoids the poll during
`ArenaRaidExit.NativeStopOwnsCurrentRaid`. However, that excludes only one
caller. `TacticalMode.TickTeamLive` and `TickTeamStaging` call
`TryGetLiveFollowers` directly, and Tactical HUD/status getters call
`GetLiveFollowers`. `ArenaMatchService.Tick` keeps ticking the current match
until GameEnd even after native Stop has committed. Scoreboard synchronization
does not call that roster method, but separately reads the cached follower bodies
and can also observe normal disposal as unavailable identity.

Recommended correction: stand down live Tactical progression and physical roster
reads only after **exact current Arena LocalGame + world + native Stopping/Stopped**
prove teardown. An intent flag or `ArenaRespawnDriver.StandDown` alone is not
sufficient: a synchronous Stop failure can leave the game Started. Preserve
pending raw-creation completion draining, canonical identities, persistence
suppression, and real live-roster failures. Do not merely lower the Error level.

## Preview animator exceptions: a mechanism, not an attributed culprit

The previous corrected process contains four EFT NREs at 11:56:58, before the
PMC kit was issued at 11:57:14.986. The full stack is:

`UnityAnimatorWrapper.GetParameter` → `ObjectInHandsAnimator.SetAnimatorGetter`
→ `FirearmsAnimator.SetAnimatorGetter` → `WeaponPrefab.Parent/Init`
→ `PlayerModelLoader.CreateWeapon/Load` → `PlayerModelView.Show`.

Native source establishes the concrete failure mechanism:

- `_decompiled/AnimationSystem/UnityAnimatorWrapper.cs:392–405,534–541` populates
  `_parametersCache` only when the animator, controller and active hierarchy
  exist. `GetParameter` dereferences that cache unconditionally after attempting
  to populate it.
- `_decompiled/ObjectInHandsAnimator.cs:96–108` enumerates that wrapper while
  installing the animator getter.
- `_decompiled/EFT.UI/PlayerModelLoader.cs:190–210` parents the weapon under the
  menu model and calls `WeaponPrefab.Init(null, parent:true)`; its parent may be
  inactive even if the weapon's own active flag is set.
- `_decompiled/WeaponPrefab.cs:371–406` takes the no-player menu branch and
  installs the wrapper getter immediately.

An inactive preview hierarchy can therefore produce this NRE. The export does
not identify the model owner, parent activity, weapon prefab or pool instance.
Four concurrent squad previews are plausible but not established merely because
there are four companions. Arena's custom `ArenaCharacterPreview.Load` invokes
`CreatePlayerBody/CreateWeapon` directly and reports its own unavailable warning;
the captured stack instead ends in native `PlayerModelView.Show`. Arena also
intercepts/reuses confirmation/loading previews, so an indirect UI lifetime
contribution cannot be excluded. Redeploy's body replacement and Killcam replay
had not begun at that timestamp.

Do not suppress `GetParameter` exceptions globally or fabricate animator
parameters. If repeated in a future run, capture the exact preview/weapon
instance, hierarchy activity, cancellation/lifetime and request owner at the
throw. Then fix only that owner's cancellation/visibility ordering.

## Other native error families

| Family | Source-backed meaning and ownership limit | Treatment |
| --- | --- | --- |
| `PlayerBody destroyed without being disposed` | `_decompiled/EFT/PlayerBody.cs:886–920`: OnDestroy finds `_active` still true; normal Dispose clears it. The earlier four occurrences are at initial match startup, before first replay/respawn, so text alone does not implicate Killcam or Redeploy. Arena's preview Release calls native loader Clear first; Clear calls PlayerBody.Dispose before Destroy. Killcam strips inactive clones, but clone presence alone does not establish these four objects. | Keep visible. Need destroyed object identity/owner and stack before selecting a fix. |
| `UtilityOperationEvent not found on ...` | `_decompiled/EFT/Player.cs:7147–7175` and `11467–11495`: native utility operation has been Executing for >2.5 seconds without its event, then falls back to Ready/Idle. Seven earlier entries were different weapon models. | It is a genuine missed animation event, not routine success. Weapon operation/body/event identity needed. Arena reload/handling/movement can influence timing; direct cause not proved. |
| `FATAL There is no handler for hash:0` | `_decompiled/AnimationControllerEventsTable.cs:125–140`: native dispatch has neither a delegate nor a known function name for the supplied event hash. | Missing/invalid event identifier. Do not claim pool corruption or a specific mod without the originating animator/event. Preserve alarm. |
| `Calling Animator.GotoState on Synchronize layer` | Unity animator warning; usually no managed stack/owner in available logs. It appeared in older Zombie shutdown runs. | Timing after Arena-triggered teardown supports an indirect interaction, not exact ownership. Keep; do not blanket-filter native warnings. |
| `JetMaterial == null` | `_decompiled/FirearmsEffects.cs:36–52`: weapon's MuzzleManager lacks its jet material during Init. Earlier run: one entry at 11:56:52 before kit issuance, nine at 11:57:36–45 after issuance while bots assembled. | Native asset/pool/model condition. No identified Arena mutation or proof of harmlessness. |
| `SpawnPointMarker ... fix message: Categories ...` / `SpawnPointMarkers fixes:N` | `_decompiled/EFT.Game.Spawning/SpawnPointMarker.cs` and `SpawnPointsCollection.cs:247`: scene marker differs from received location parameters; native code repairs it and logs Error. | Often expected scene/database reconciliation. Establish which parameter owner changed it before calling it Arena-caused; do not disable repairs or global errors. |
| `[Transit] Flag:Common ... Count:0 ...` | Native transit setup diagnostic appears as EFT Error; no exception in the earlier record. | Count as a recorded Error, distinguish from an exception. No evidence of profile damage or Arena failure from this line alone. |

Arena's `ArenaDefaultFireMode` does not call native CheckFireMode/ExamineWeapon
operations: it sets the item's selector and updates the local idle hands animator
after exact-body guards. The draw/holster and reload-speed leases also require
the exact local operation/body and do not acquire for bot hands. That does not
prove all animation interactions safe, but the source does not support naming
them as the cause of these utility timeouts without identifying the affected body.

The seven historical Factory marker-category changes can be narrowed further:
all seven exact IDs already contain `[Bot, Boss, BotPmc]` in the currently
installed on-disk `SPT_Data/database/locations/factory4_day/base.json`, before
Arena's runtime location mutation.
Arena's location mutator assigns categories only when constructing its separate
private additional markers, not those seven native IDs. The messages match
native scene-versus-base-data reconciliation. This checks today's local database;
it is not a historical database hash or proof that every future marker warning
has the same origin.

## Redeploy and KillcamReplay ownership

Arena deliberately invokes Redeploy.Core for kit swaps and deaths. Therefore a
Core recovery during such a swap is **indirectly triggered by Arena**, while the
recovery implementation and its log are Core's. This is distinct from a new
Arena bug. In particular, `PerfectCullingFix.SamplerPatch` identifies an older
registered sampler when a replacement starts and logs/destroys that exact stale
component. `CullingCameraGuard` and DynamicMaps helpers rebind replaced cameras
and own-player markers. Failure, fallback, missed repair and unavailable contract
warnings must remain; normal successful handoff details can be quieter.

Core's `RespawnEngine.RunRespawn` has explicit failure outcomes, 30-second create
timeout, external-stop handling and failure publication. Preserve incomplete
death-interception, missing dead-body guard, safe pre-teardown refusal, failed
condition reset and protection/owner errors. None should be demoted to make
short tests look clean.

Killcam's `OnDead` accepts only the exact current MainPlayer and recorded killer;
Arena's frozen replay duration owns whether it requests a replay. No recorded
killer or too-short track is a player-visible degraded replay, so the bounded
warning is appropriate. ShotRecord, BoneRecorder, ghost construction and replay
exceptions remain errors. A successful silhouette fallback after puppet failure
does not turn the failed real-model construction into routine output.

One existing observability limit deserves explicit mention: Killcam's
`GhostPuppet` temporarily disables `Debug.unityLogger.logEnabled` only around
the synchronous inactive body-clone Instantiate, restoring it in `finally`.
Its comment explains the known Odin-deserialization failures in immediately
discarded clone scripts. That global synchronous window also hides any other
Unity message produced inside the call; a clean stream cannot establish that
none occurred. This audit did not introduce or widen the suppression. A future
cleanup should prefer avoiding those script clones or narrowly identifying the
known duplicate error, rather than expanding global suppression.

## Unnecessary routine output: concrete candidates

Arena's recent logging split is substantially present: ordinary technical setup,
placements and audio policy are Debug, detailed probes opt-in Developer, and
Info is retained for match lifecycle/protection. The new PMC-kit issuance Info is
one player-relevant event. Do not remove successful profile-restore confirmation.

Sibling output has not received the same split:

| Candidate | Current emission | Suggested bounded change |
| --- | --- | --- |
| Killcam `SnapshotRecorder.PruneStaleTracks` | Info for removed tracks, already rate-limited by `PruneLogInterval`. | Debug or opt-in recorder diagnostics; retain actual pruning regardless of logging. |
| Killcam `BoneRecorder` | Info for body reinit and transform count/memory each replacement. | Debug; retain missing pelvis/failure Warning/Error. |
| Killcam puppet/weapon/shader setup | Info for mapped bones, stripped components, meshes/pivot, renderer-hide counts and shader selection. | Debug. Keep concise replay start/end and unavailable visible replay warnings. |
| Redeploy `RespawnPipelineTiming` | Two long Info lines per successful replacement; always collects reflection/state counters and schedules following-frame measurement. | Add an independent opt-in technical timing switch, guarding measurement work only. Keep one concise successful respawn event in driver and all actual failure outcomes. Changing only severity would not avoid measurement cost. |
| Redeploy normal camera/marker/freecam rebind | Several Info lines per replacement for successful expected restoration. | Debug or one summary; keep unexpected recovery/failure warnings distinct. |
| Arena `/arenamode/client-settings` server middleware | Fresh DM/TDM window has 36 periodic GET Info records in SPT middleware. Client GET already uses the lower-level HTTP client (`ArenaServerClient.GetClientSettings`); its six RequestHandler lines belong to three explicit pushes. | Scope any successful-request treatment to this endpoint and preserve errors. No client-side polling regression; do not disable polls. |

Killcam's stale-track Info is already limited to one message per 30 seconds,
so it should not be described as per-frame spam. Conversely, its BoneRecorder
sample catch and ShotRecord catch currently log every failure at their sampling
or shot cadence. No such storm is established in the historical kit exports.
If it occurs, preserve the first full Error and a visible repeated-error count,
then rate-limit identical diagnostics by body/raid instead of silencing the
alarm or tying it to the verbose switch. Arena's independent
`LogDamageExceptions` already caps at 32 reports per raid with a cap notice and
returns the original exception; that safety contract should remain unchanged.

The GPU's archived BepInEx configuration confirms `LogLevels=All`, so Info → Debug
alone does not remove those lines from that disk log. Do not silently change
global user preferences.
HarmonyX/ModulePatch patch-trace startup messages are also separate emitters and
must not be presented as Arena's own runtime spam. The measured totals above
establish their dominance in this run, rather than raw call-site counts.

## Separate CraftQueue failures in the earlier run

Historical final exports contain 176 client HTTP NotFound errors and 224
server `UNBEHANDELT` records for `/craftqueue/Poll` and `/craftqueue/ChannelInfo`
in a wider capture window. They are not 400 independent failures: repeated HTTP
retries and two different capture endpoints account for duplicated views/counts.
CraftQueue client source owns both requests (`CraftQueue.Client/Plugin.cs`), and
its server router declares both paths (`CraftQueue.Server/CraftQueueRouter.cs`).
They already occur before Arena selection. No source-supported ArenaMode,
Redeploy or KillcamReplay cause has been established. Missing/mismatched active
server routing is a separate integration diagnosis; do not hide its errors or
assume installation state from source alone.

The new process does not load CraftQueue; the native tester reports no removal
during these tests. Its old 404 storm therefore cannot be presented as repaired
by the current Arena UI candidate or compared as an equal installed-mod control.

## Four-pass review record

1. Scope/ownership: source-only leaf; sibling sources read, no sibling edits,
   builds, deployments, GPU actions or global log changes.
2. Call paths: native emitters and Arena/PFT callers traced; known no-contest
   teardown defect distinguished from unresolved model/animation ownership.
3. Adversarial attribution: timestamp correlation is not causation; historical
   error-only exports do not certify absence of warnings; duplicate stream
   records and wider server windows are not independent incidents.
4. Fresh evidence: independently recounted 11,436 unique records, all source
   availability and pagination/tail checked, one non-alarm text-limit exception
   retained and raw original archived. Loaded-process status reports 41 plugins
   and six libraries matching build/host/loaded identities. Native scenario and
   restoration evidence distinguishes real DM/TDM enemy deaths from PFT's
   deliberate kill hook, successful round rebind from the defective later exit,
   and missing preview NRE reproduction from recurring PlayerBody errors.
