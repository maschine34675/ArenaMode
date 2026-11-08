# ArenaMode area rules

Per-area constraints, learned the expensive way. **Before changing an area, read its section here and the review documents it names**;
the general rules are in [../AGENTS.md](../AGENTS.md), the recurring pitfalls in [TRAPS.md](TRAPS.md).

The paragraphs under each area heading are the wording of `AGENTS.md` as it stood on 2026-10-29, grouped by area on 2026-10-30
with only links and document paths rebased; the "From the handoff" entries are excerpts of the working handoff of the same day,
verbatim or (where marked Quoted:/Rule:/Durable form:) restated. A paragraph may therefore mention a date, a version or a "below"
that referred to the old file; statements the text itself calls historical stay historical. When a paragraph and the repository
disagree, the source and the review document it names win: fix the paragraph. Add new area rules to the matching section, not
to `AGENTS.md`.

**Contents:** [Overview](#overview) · [Rewards and profile protection](#rewards-and-profile-protection) · [Arena session, gate and whitelist](#arena-session-gate-and-whitelist) · [Optional-mod compatibility](#optional-mod-compatibility) · [Modes: Deathmatch and Gun Game](#modes-deathmatch-and-gun-game) · [Modes: Team Deathmatch and Domination](#modes-team-deathmatch-and-domination) · [Modes: Tactical and Zombies (PitFireTeam)](#modes-tactical-and-zombies-pitfireteam) · [Maps and areas](#maps-and-areas) · [Spawns, team boxes and spawn gates](#spawns-team-boxes-and-spawn-gates) · [Bots and population](#bots-and-population) · [Pickups, powerups and the Molotov](#pickups-powerups-and-the-molotov) · [Movement and combat](#movement-and-combat) · [HUD, menus, audio and voice](#hud-menus-audio-and-voice) · [Performance and corpse cleanup](#performance-and-corpse-cleanup) · [Logging and diagnostics](#logging-and-diagnostics) · [Build, deploy and release](#build-deploy-and-release) · [Testing and verification](#testing-and-verification) · [Process and conventions](#process-and-conventions)

## Overview

**Loading-time and static exceptions to non-negotiable 2** (the complete list, as argued in the old `AGENTS.md`):

   Loading-time exceptions are documented here and at their call sites:
   Protected PFT Tactical may issue one PMC-only Arena kit after the exact
   native local-start identity binds, before LocalGame creates the first body.
   Read docs/reviews/PFT-TACTICAL-KITS-20261018.md: exact arm token/epoch, profile,
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
   docs/reviews/ZOMBIE-MODE-REVIEW.md for the full restore-path argument.
   Post-raid UI runs after IsActive is cleared. ArenaPostRaidScreens captures the
   exact ended LocalRaidSettings/profile/epoch and binds one native result
   operation before changing its warning/XP presentation. This is a UI-only
   exception; native profile refresh and result completion must remain intact.
   See docs/reviews/POSTRAID-PAGES-REVIEW.md for both forward and health-back navigation.
   Dedicated-item metadata is registered before the native database/profile
   snapshot: the private Molotov template, locales/cache and private loot/flea
   exclusions exist at server startup. This adds no normal loot, trader stock or
   profile item and never edits the RGD-5 donor. Exact-template model/icon/pool
   restoration and native harmless cleanup likewise do not require an active
   raid; fire and supplies retain their separate strict Arena admission gates.
   See docs/reviews/MOLOTOV-ITEM-REVIEW.md for this static item-registration boundary.

### From the handoff (snapshot of 2026-10-29)

Working notes of 2026-10-29 that carry a still-valid constraint: verbatim excerpts, or restatements where marked Quoted:/Rule:/Durable form:. Line numbers refer to `handoff-archive/HANDOFF-2026-10-29.md`.

Arena side: `668619f` (LiveKitRespawn tolerates a refusal — `IsStillWired`
leaves an untouched body alone, the rewire steps are idempotent, and the swap
stands down while a pipeline runs), `bb82ce8` (death path publishes only the
verified body; `OnKitApplied` after success instead of inside the engine's
equipment strategy), `3a5123c` (`ArenaEvents` per-subscriber isolation).

*(handoff lines 3864-3868)*

"`ef53c2f` (per-side contour grid fade, original opacity within 6 m and invisible beyond 20 m, physical/navigation blockers unchanged)", review pointer AREA-GRID-VISIBILITY-REVIEW.md (line 4096). Durable form: the enclosure grid fades per polygon side by the MainPlayer's horizontal distance (full within 6 m, linear to 20 m, transparent beyond). Only vertex alpha changes. Renderer, colliders, NavMesh obstacles and carving stay unchanged and enabled, and invalid observations restore full visibility.

*(handoff lines 4060-4062)*

## Rewards and profile protection

Before changing reward persistence, read [docs/REWARDS-CORE.md](REWARDS-CORE.md)
and [docs/REWARDS-SERVER.md](REWARDS-SERVER.md), then
[docs/REWARDS-RESULTS.md](REWARDS-RESULTS.md) and
[docs/REWARDS-RUNTIME.md](REWARDS-RUNTIME.md). The ordinary-server consumer
runs only before native profile loading, with separately enrolled/approved state;
the startup result validator/approval producer requires separately trusted run
and restoration evidence. Recorded preferences, default-on startup enrollment, client combat
capture and signed private-session receipts are now wired. Never accept its whole
candidate envelope from a client. Never infer
restoration from empty RAM state, replace a live profile for payout, bypass the
startup coordination gate, or use a client-supplied amount as payout authority.
Reward work does not relax Arena's full-profile restoration protection. Read
docs/reviews/REWARD-RECOVERY-ENGINE-20261027.md and docs/reviews/REWARD-RESTORATION-20261027.md
before changing recovery. The user replaced explicit review with automatic
discard on 2026-10-27; read docs/reviews/REWARD-DISCARD-20261027.md. No recovery UI or
client approval route remains. Old-case closure is startup-only metadata,
never a payment or restoration proof. Preserve exact case acknowledgement and
permanent replay rejection. Full native restore
readback is mandatory even with rewards OFF; private checkpoint publication and
archival precede immutable offline closure and tree retirement.
The separate stopped-session discard in docs/reviews/OFFLINE-STARTUP-RECOVERY-20261028.md
handles lost disposable marker/lease/state before closure. It requires intact
ordinary ownership and positive private-process exit, preserves the ordinary
profile and sets unknown consent OFF. Never invent closure/restoration proof.
Native end and failed/stale-start recovery must drain native update callbacks
before the actual profile swap. Use the protected end service's owned persistence
window through save/readback/completion; never restore directly in a router or
resume the session-return owner's update hold. Read docs/reviews/RAID-END-PERSISTENCE-20261027.md.

The legacy schema 1/2 private map-menu consent relay uses a normal-server-issued, expiring control
grant, pinned loopback TLS and explicit revision-checked settings. Keep the
secret-bearing private-control listener separate from SPT's debug-logging router;
refresh before snapshot locking, never within profile restoration. Missing,
revoked or stale grants do not authorize new capture or normal-profile writes.
Enrollment and payment remain startup-only. See docs/REWARDS-RUNTIME.md.
Fresh, initialized normal profiles default ON at admitted startup. Preserve
every existing OFF choice, and never infer a fresh profile from missing consent
inside an existing reward directory or from a profile carrying reward history.
Private sessions and already-loaded profiles must never bootstrap enrollment.
The one profile write besides enrollment is the startup authority recovery
(`RewardAuthorityRecovery`, first step of the admitted pre-load pass): only when nothing
is unsettled, it archives the reward directory by rename (never deletes), carries the
consent choice over unchanged and never pays an uncertain record. See
docs/REWARDS-RUNTIME.md, 'Lost reward authority'.
The server owns currency values; physical combat remains client-observed. New receipt
allocations may bind the latest clean ordinary profile at startup, preserving
intervening ordinary progress; an existing allocation/approval must never be rebased.

Read docs/REWARDS-XP.md before changing reward amounts or profile progression:
old results remain RUB-only, XP fields must preserve legacy serialization, and
native login owns Level/trader recalculation from the final XP curve.
Read docs/REWARDS-RETURN.md before changing session return: local automatic payout
uses normal-server startup after private shutdown and signed-result transfer.
Schema 3 reuses the same native drain/save proof at entry, independently of rewards:
a lost or mismatching reward authority yields a lease without authority and never
leaves the stopped normal server paused (docs/REWARDS-RUNTIME.md, "Lost reward authority").
Keep the separate process-bound return capability, request/update drain, native
save plus full disk/RAM readback, exact process exit and no forced normal-server kill.
A failed final save retains the quiesced process/RAM for an explicit retry; never
resume native StartAsync, clear native save hashes or replace its live profiles.

### From the handoff (snapshot of 2026-10-29)

Working notes of 2026-10-29 that carry a still-valid constraint: verbatim excerpts, or restatements where marked Quoted:/Rule:/Durable form:. Line numbers refer to `handoff-archive/HANDOFF-2026-10-29.md`.

Native save/readback and request/update quiescence precede shutdown; failures
preserve live RAM. Legacy schema 1/2 retain the older live-server fallback only
when no offline capability was issued. New
startup allocations preserve intervening normal progress; existing allocations
and payment history must never be reset/rebased. Native SPT 4.1.6 idle
`inraid: {}` and the space before its raid timestamp are intentional formats.

*(handoff lines 341-346)*

In an exact active Arena raid, Esc -> Disconnect now skips the entire red item-loss confirmation and invokes the native Stop(Left) continuation. A running respawn/missing owner or paused game defers exit until safe; it never forces an owner rebuild or bypasses protected profile restoration. Normal raids retain their confirmation. Notification faults cannot discard an exit. `CHARACTER-MODEL-CLICK-REVIEW.md` and `ARENA-RAID-EXIT-REVIEW.md` explain scope, native seams, failure handling and the short user GPU acceptance steps.

*(handoff lines 3251-3257)*

## Arena session, gate and whitelist

Read docs/reviews/ARENA-OFFLINE-SESSION-20261025.md before changing session ownership: schema 3
stops the ordinary server before copy, holds its lifetime lock privately, uses
signed local consent and recovers only before normal profile loading. Missing
activated evidence must never fall back to legacy authority or restore old ON.

Arena runs only with whitelisted mods: a normal session with other mods offers
START ARENA SESSION, which restarts the game with only the allowed plugins
(preloader gate `ArenaMode.Gate`) against a second server with a private tree
(helper `ArenaMode.Session`).

The user policy file is a combined
allow/deny list: `"unlisted": "allow"` turns the whitelist into a blacklist. The shipped default file
allows every installed mod and has its own `deny` list (ORBIT, Questing Bots, Adaptive Bot Culling,
ISB Special Forces, Aishi: ArenaMode refused them by code before the whitelist existed; that code was
removed on 2026-10-22, so this deny list alone refuses them). Keep the per-unit order (platform/Arena, user deny, user allow, DEFAULT deny, default entries,
`unlisted`: a user allow lifts a default deny, a user deny always wins, default denies hold in
blacklist mode), an unknown `unlisted` value (or, in blacklist mode, an unreadable `deny` list)
invalidating the whole user file, a malformed default `deny` invalidating the default file, the server
mods' `ModDependencies` in the closure, and the mode out of the shipped default file (spec sections 3
and 4). A deny also wins over hard dependencies: the closure never pulls a denied unit in, and a unit that
needs one (directly or through other units, client and server) is excluded with `requires-denied:<guid>`;
platform/Arena units are the only exception. Never turn that back into a pull-in with a warning. The refusal names denied mods ("Blocked for Arena") apart from unlisted ones.
The user's testing override is that blacklist file and no second mechanism; keep it visible (the decision's one
`policy override` warning per side, the gate summary suffix, `unlistedAllowed`; spec section 3).

The private tree's copy of SPT's core config always switches `fixes.removeModItemsFromProfile` and
`removeInvalidTradersFromProfile` on (SPT ships them off): a profile copy holding items, suits or trader records of a mod
the session leaves out is otherwise marked invalid and the session cannot start (0.2.1; spec section 6). Never drop that,
and never write anything of the private tree back to the real runtime or profile.
The game quits for a session switch only after the helper's `<request>.accepted` acknowledgement (a coroutine looks
every 250 ms for at most 15 s; a helper that is gone or silent leaves the game running and the action available, and
the client deletes the request file on every failure: the helper stamps that file and ends its wait for the game when
it is gone or replaced, so a late helper cannot switch the session behind the player's back).
An Arena session refuses Arena when it cannot load the policy, when a plugin the game loaded or a mod the server runs
is one the current policy excludes, when a preloader patcher the gate let run (the report's `patchersAllowed`) is one
the current policy excludes, when the ping's `policyHash` is not the gate's, or when the gate report's
`patchersRanBeforeGate` is not empty; a normal session judges every
preloader patcher below `BepInEx\patchers` (a `.dll` with a `TargetDLLs` member in its bytes; a support library is no
unit) by file name like a plugin (spec sections 6 and 7). Never turn any of these back into a
mere notice, and never quit the game on `Process.Start` alone. The gate fails closed: a patcher it cannot place (no
file) is neutralised and reported, and an unknown gate position reports every neutralised patcher with an initializer
as having run early. The client and the gate read the policy files only through `ArenaPolicyFiles` (one limit, 256 KB:
a limit on one side only locks the session out through the policy hash check), and the policy hash covers the user's
denied names in a whitelist as well.

Failed starts queue their native Stop after the startup frame and revoke both
ambient burst work and the separate managed solo population leases.
The delayed native raid-intro fade can otherwise cancel Stop's GameEnd callback;
see docs/reviews/FAILED-START-INTRO-REVIEW.md. Preserve exact captured identity and native
teardown checks; do not suppress general fades or replace native GameEnd.

### From the handoff (snapshot of 2026-10-29)

Working notes of 2026-10-29 that carry a still-valid constraint: verbatim excerpts, or restatements where marked Quoted:/Rule:/Durable form:. Line numbers refer to `handoff-archive/HANDOFF-2026-10-29.md`.

The installed default
  policy must be updated along with the client/server DLLs; restart both to
  refresh the loaded policy and adapter. User policies remain untouched.

*(handoff lines 316-319)*

Session identifiers. Quoted: 'the helper `maschine-ArenaMode.Session.exe` restarts the game with `-arena-session`, the preloader patcher `maschine-ArenaMode.Gate.dll` ... a private SPT server tree under `%LOCALAPPDATA%/maschine-ArenaMode/session` (port 6970, disposable profile copy)' and 'user file `BepInEx/config/com.maschine.ArenaMode.whitelist.json` (allow/deny)'. Keep as one line: the constants live in ArenaMode.Shared/Session/ArenaSessionContract.cs (CommandLineFlag L23, ArenaPort 6970 L37, HelperFileName/GateFileName L61-62, UserPolicyFileName L71); the spec adds fallback ports 6971-6979; the shipped default comes from assets/arena-whitelist.json; the helper log is %LOCALAPPDATA%/maschine-ArenaMode/session/helper.log (README L110).

*(handoff lines 483-489)*

'the arena server runs from its own copy of the server binaries in the tree, so mods that resolve paths from `AppContext.BaseDirectory` (PitFireTeam's settings.json) write into the tree, not the real runtime (measured: zero arena-server writes in the real SPT_Runtime); bundle-only mod folders are junctions and the bundle hash cache is carried over (WTT's 8 GB are no longer copied; server ready in 11 s).' Spec ARENA-SESSION-20261021.md section 6 (around L584-666).

*(handoff lines 494-498)*

## Optional-mod compatibility

The accepted versions in `ArenaMode.Shared/PitFireTeamVersions.cs` are 0.10.0 and
0.10.3 or newer; 0.10.1/0.10.2 remain excluded. Forward admission was explicitly
requested on 2026-10-19. Keep metadata/assembly identity and every reflected
profile-protection contract; the proficiency contract is mandatory from 0.10.3
onward. SAIN 4.5.1+, APBS 2.4.1+ and DynamicMaps 1.2.1+ likewise attempt their
existing APIs and warn once for newer unreviewed versions. This is not a promise
of future compatibility. See docs/reviews/OPTIONAL-MOD-VERSIONS-20261019.md.
For PFT 1.0's LiteDB storage, queued karma/insurance reports and mode semaphore,
read docs/reviews/PITFIRETEAM-1.0.0-20261024.md. The shipped policy now reviews 1.0.0;
the adapter's reviewed baselines are 0.10.0, 0.10.3 and 1.0.0. Do not raise
MinimumForwardVersion: 0.10.3 still owns admission and mandatory proficiency
protection, and other unreviewed builds still warn. Preserve external persistence protection
for solo Arena with PFT installed, separately from team roster/kit semantics;
never wait for async PFT work while holding the profile-boundary writer lock.
Arena-only optional-mod admission is `ArenaOptionalMods.GetRejectionReason`: the whitelist
first, then the failure the server reports in its ping, then the remaining adapters. It
never disables a foreign mod or changes ordinary PMC/Scav behavior. Keep only
explicitly requested integrations, including Tarkova Cola protection
(2026-10-25; see docs/TARKOVA-COLA-COMPAT.md) and the follow-ups below.

The per-mod refusals of ORBIT, Questing Bots, Adaptive Bot Culling, ISB Special Forces/Aishi and the adapters for MoreBotsAPI, AI Limit, Looting Bots, Remove The Dead, Smoothie, Late To The Party, Classic Movement, StatTrack, Deminvincibility, Skills Extended, Pause Continued and EFT Effects Manager / Broken Limbs were removed on 2026-10-22; their dated reviews in docs/ are history. Preserve the
process-lifetime admission provider across menu cleanup; do not replace it with global
BepInEx incompatibility declarations or user-config edits. Keep the rejected-Core notice
path free of Core-dependent types, exclude PFT's My Squad screen, and never replace final
admission with UI (docs/reviews/ARENA-AVAILABILITY-20261020.md, historical for its mod checks).
The map screen evaluates that admission once per display pass (`ArenaMenuSelection.BeginDisplayPass`,
opened only around `ScreenState.Refresh`); outside a pass nothing is memoised, so clicks, selection
setters, the claim, the roster freeze and the raid start stay fresh. A pass never wraps a click, claim,
roster freeze or raid start; the only writes inside a pass are the existing display repairs of
`ScreenState.Refresh` (saved-mode repair in `Modes.Synchronize`, area repair of `TryPrepareMapArea`,
deselection of a map that became invalid). Refresh made them before the pass existed: the last two on
the shared 26.10 snapshot, the mode repair on its own mode read. An adapter whose optional mod is
absent must return without `Harmony.UnpatchSelf()`, which walks every patched method of the process: guard
it with a flag raised before the first `Patch` (Core 1.0.19, `FastGrenadeThrowCompat`). See
docs/reviews/MAP-MENU-ADMISSION-20261031.md.

Read docs/ARENA-SESSION-20261021.md before changing the policy, the gate's install timing (`Finish`, never `Initialize`), the helper's process-identity rules or the request contract the client checks before it quits
the game. New foreign adapters require an explicit task; Tarkova Cola is the approved
exception for external progression and protected reward transfer. Never permit its native
state writes, achievement grants or bottle/grenade refunds during a protected Arena scope.
The explicitly requested follow-up integrations are documented in
docs/reviews/ITEMS-HUD-COMPAT-20261025.md and docs/reviews/REMAINING-MOD-COMPAT-20261026.md:
Arena owns Use Items Anywhere slot composition, Fast Grenade Throw priority/input
and suppression of PiP callbacks for Arena's withheld kit preloads. General body
and camera lifetimes remain in Redeploy.Core (current source minimum 1.0.19).
Amand's Graphics remains unlisted because its initial Labyrinth scene setup
fails before any Core replacement; Factory camera tests do not remove that limit.

Reviewed-version differences are advisory in direct play and new private sessions
(user decision, 2026-10-27; docs/reviews/MOD-VERSION-FOLLOWUP-20261027.md). Attempt recognized
mods such as UIFixes despite version drift; retain identity/deny/dependency checks
and actual adapter/profile-protection requirements. Never automatically create a
version-exclusion plan for a new session. The version-filtered restart described
in docs/reviews/PITFIRETEAM-1.0.0-20261024.md is historical. Preserve strict legacy
request/marker/process-flag agreement and old-helper refusal for existing plans.

Recoil Rework Legacy, Continuous Load Ammo and Continuous Healing body-replacement
adapters live in sibling Redeploy.Core from 1.0.9 (0.2.0 bundles and requires Core 1.0.10, the library of Redeploy 1.0.1); read
docs/reviews/SHARED-BODY-COMPAT-20261023.md before changing them. Do not restore duplicate
Arena foreign hooks. Arena registers only ArenaMedicalBodyScope for its medical
bot fallback; Core's normal replacement scope must never own unrelated ordinary bots.
The dated adapter documents below describe the earlier Arena-owned implementation.
Continuous Load Ammo 1.2.0+ and SVM 2.2.3+ have separate scoped adapters. Read
docs/reviews/AMMO-RAID-HEALTH-COMPAT-20261020.md and its leaf reviews before changing them (the
Deminvincibility part of that document is history). Preserve CLA's physical
controller/inventory/task epochs and SVM's actual Harmony order and one-use ordinary
callback bridge. Never replay unknown outer raid-end hooks, make a stale Arena end look
successful, or globally disable foreign settings. Bounded Factory gameplay evidence is
recorded in docs/reports/2026-10/GPU-OPTIONAL-COMPAT-20261020.md (it also covers adapters since removed).
Preserve its untested CLA timing/UI, Tactical PFT, killer-replay and world-change debounce
boundaries; it is not blanket compatibility certification.

**Recoil Rework Legacy** (the adapter now lives in `../Redeploy/Redeploy.Core/Compat/RecoilLegacy*.cs`; the bounds still hold for any Arena-side body replacement):

Recoil Rework Legacy 2.0.0+ has a physical-body singleton handoff; read
docs/reviews/RECOIL-REWORK-COMPAT-20261020.md before touching it. Its original Awake can
destroy an entire newly created player if the old component still exists.
Preserve both singleton transfers before foreign initialization, deferred
authoritative-body commit/recovery, retired controller guards and ordinary-raid
pass-through. Never destroy a player object or replay foreign world startup.

Read `docs/DEPENDENCIES.md` for hard/conditional dependencies, exact optional-mod
contracts, absorbed own mods and release package boundaries. An installed mod
is not automatically required or exhaustively certified compatible.
For Continuous Healing, read `docs/reviews/HEALTH-LOOT-COMPAT-INTEGRATION-20261019.md` (its Late
To The Party half is history) and the adapter contract. Preserve the live local
player's healing and exact retired-body leases. Never turn the adapter into a global
config switch or a profile-ID exemption.

BigBrainCompat is owned by Arena's Start/Update/OnDestroy, not an independent
plugin. Preserve lazy named-type loading and the NoInlining typed boundary;
do not add BigBrain types or a hard companion reference to the main client.
Update both Arena client DLLs together. Read docs/reviews/BIGBRAIN-LIBRARY-20261018.md
before changing this contract. Arena needs Arena.Server for profile protection;

### From the handoff (snapshot of 2026-10-29)

Working notes of 2026-10-29 that carry a still-valid constraint: verbatim excerpts, or restatements where marked Quoted:/Rule:/Durable form:. Line numbers refer to `handoff-archive/HANDOFF-2026-10-29.md`.

Quoted: "Shared Core 1.0.13 preserves Radar/Crosshair through body/camera replacement; Arena carries no duplicate adapters." Rule: the Radar 1.3.4 and Simple Crosshair 1.4.0 adapters live in Redeploy.Core (Redeploy.Core/Compat/Radar/*.cs and Compat/SimpleCrosshair/SimpleCrosshairCompat.cs), and ArenaMode.Client has none. Radar, Simple Crosshair and Better BepInEx 1.0.0 are on the default policy (assets/arena-whitelist.json:37-39). See docs/reviews/HUD-MOD-COMPAT-20261025.md.

*(handoff lines 290-292)*

Adaptive Bot Culling is on the default deny list because review of the published 1.2.0 DLL/source found that 'its F12 toggles do not remove all animation hooks'. See docs/reviews/ARENA-AVAILABILITY-20261020.md before lifting that deny entry.

*(handoff lines 640-642)*

The advisory-version policy does not relax own client/companion agreement, SPT platform requirements, area/map passports, protocols or the Fika exclusion: 'own package agreement, platform/map contracts and Fika exclusion remain strict'. Arena refuses while Fika is loaded (FikaCompatCheck.IsFikaLoaded in ArenaClaim.cs:29, ArenaSession.cs:405). See docs/reviews/OPTIONAL-MOD-VERSIONS-20261019.md:45-49.

*(handoff lines 835-837)*

Killcam integration: Arena reads Killcam's public static `DeathReplayActive` by reflection (ArenaMode.Client/Spectator/KillcamCompat.cs:149), and its N/A reason only asks 'Enable DeathReplay in Killcam.' (L38). Killcam commit 8c8ee45 (2026-10-18, shipped in v1.0.1 on 2026-10-21) removed the com.local.killanddamageinfo guard and kept the property because ArenaMode reads it. Do not reintroduce a KillAndDamageInfo-based refusal.

*(handoff lines 884-886)*

Arena binds the optional Killcam by GUID `com.maschine.Killcam`. A leftover `com.maschine.KillcamReplay` DLL is refused as an incompatible camera owner (KillcamCompat.cs:13,123-127). Read docs/reports/2026-10/KILLCAM-RENAME-20261018.md. docs/reviews/KILLCAM-MODES-REVIEW.md, which AGENTS.md cites for M8, still describes the former KillcamReplay plugin.

*(handoff lines 905-907)*

Real registration/type/priority/brain checks remain mandatory; all
  capabilities stay off on registration failure. See [package rules and evidence](reports/2026-10/package-layout-20261017.md)
  and [optional-loader harness](../tools/BigBrainCompatibilityChecks/README.md).

*(handoff lines 1171-1173)*

DynamicMaps removal uses only its own marker UI methods and exact successful
trim evidence; native corpses/Players/loot registries remain intact.

*(handoff lines 4066-4067)*

The APBS 2.4.1+ adapter (`ApbsBotTierCompatibility`) is server-only and keyed on APBS's semantic metadata version, not its assembly version. It restores Arena's request-owned bot level range after APBS's global raid-level (Weighted/Delta) selection. Native deferred parallel waves and per-bot clones keep exact session-entry/arm ownership. Normal raids, protected PFT, saved companions and APBS's own config stay untouched; APBS migrates its root config at its own startup. The installed-APBS xunit test runs real native wave/clone/level processing with inventory/cache stubbed. It runs only through `tools/ApbsCompatibilityChecks/Verify.ps1`, which sets ARENA_APBS_TEST_ROOT; otherwise it skips. See `APBS-COMPATIBILITY-REVIEW.md` and `tools/ApbsCompatibilityChecks/`.

*(handoff lines 5026-5042)*

## Modes: Deathmatch and Gun Game

Five modes: Deathmatch, Gun Game, Tactical, Team Deathmatch, Zombies. Deathmatch merges
legacy Endless/Rounds with frozen individual FFA or all-vs-player rules and
optional finite/unlimited scoring rounds. See docs/reviews/DEATHMATCH-MENU-REVIEW.md and
docs/reviews/DEATHMATCH-MODE-REVIEW.md before changing score admission or terminal death holds.
Deathmatch FFA group/relationship hooks also operate during a confirmed arm on
its matching loading map, since native preactivation fixes group membership.
They never apply to an ordinary/unarmed map; server rejection after that seam
shares the documented preload exception's limits. Individual native groups must
remain registered and must be retired only after membership ended (not merely
AfterCreation, which is still PreActive). See docs/reviews/DEATHMATCH-GROUPS-REVIEW.md.

Damage assists add proportional base points without changing the killer reward,
kill limits or Tactical money. Read docs/reviews/ASSIST-SCORING-REVIEW.md before changing HP
capture, settlement or scoreboard compaction: actual HP deltas precede native
kill callbacks, and assist identity must use immutable keys across archived rows.

### From the handoff (snapshot of 2026-10-29)

Working notes of 2026-10-29 that carry a still-valid constraint: verbatim excerpts, or restatements where marked Quoted:/Rule:/Durable form:. Line numbers refer to `handoff-archive/HANDOFF-2026-10-29.md`.

Quoted: "Loading-screen InitialKit includes the actual first three selected/validated Gun Game stages in the opening batch. Both full kits and weapon-only swaps halve loose reserves with total rounding; loaded rounds/detachable magazines stay intact." Pointer: docs/reviews/TRANSITS-GUNGAME-AMMO-20261024.md. This doc also explains the transit IsActive=false rule in item 351-353, which carries no pointer.

*(handoff lines 353-361)*

User confirmed individual FFA winners and local-player targets in all-vs-player.
Both limits off means continuous play; finite rounds require an end condition.
Legacy settings migrate once. Rules freeze before the exact protected arm,
including server-acknowledged hostility. Result ownership preserves native
death/respawn/exit and profile boundaries.

*(handoff lines 4190-4194)*

FFA needs individual native groups as well as server role hostility: confirmed
same-map loading admission, exact spawner ownership, delayed PreActive-safe
cleanup and missing reciprocal enemy repair are documented and tested. Do not
simplify these to an IsActive-only hook or dispose an empty newly created group.

*(handoff lines 4196-4199)*

## Modes: Team Deathmatch and Domination

Domination (2026-10-26 development) is a constructor variant of independent TDM
with separate objective policies; read `docs/reviews/DOMINATION-PROTOTYPE-20261026.md`.
It requires the registered BigBrain objective layer. Factory day/night uses three
per-match randomly selected, scene-verified points from its original eight-seed
pool, with 12m horizontal separation; the independent original navigation anchor
must not depend on the selected triple. Read docs/reviews/DOMINATION-OPTIONS-ROUTES-20261027.md.
Domination owns separate score/time preferences, frozen at match start. Bot
approaches must be capture-valid and fully reachable; keep the last successfully
owned route destination separate from any failed alternative for exact cleanup.
The five ported maps use original anchors: Equator and Bay5 one each, AirPit and
Bowl two each (CapturePointEntity), Iceberg three, A/B/C. Read docs/reviews/DOMINATION-PORTED-20261026.md.
Never use the old MapPort modes.json capture_point centroid for AirPit/Bowl: it
averages two distinct objectives (Iceberg's is null). Iceberg's A/B/C are the
CheckPoint CAPTURE_POINT_LIGHT_02 trigger centres; A (in the pool) and B lie .5 m
and .4 m below their floors. Never lift them, add a relocation or loosen the
shared projection to make them pass: the native check decides, and a refused
point aborts the match. See docs/reviews/ICEBERG-INTEGRATION-20261031.md. Ported Domination shares independent TDM's
team boxes, gate completion, physical roster and spawn-shield ownership.
Never turn its holding points into kill-based rewards or widen PFT pursuit leases.
The original TDM kill policy stays separate; all physical roster/respawn boundaries
remain shared. The September single-control-point proposal is historical.

Team Deathmatch is independent USEC vs BEAR combat with continuous respawns;
it is separate from protected PitFireTeam Tactical. See docs/reviews/TEAM-DEATHMATCH-REVIEW.md.
Its selectable virtual team is frozen in ArenaTeamSideSelection; never mutate
Profile.Info.Side. Both team-box and bounded-area local spawns/respawns use the
exact-raid ArenaSession team facade. Read docs/reviews/TDM-SIDES-MONITORS-20261025.md before
changing affiliation, faction colors or optional MapPort screen ownership.

Independent TDM also has a ready/countdown/live/result lifecycle and client-owned
kill/time limits, frozen per match. See docs/reviews/TDM-MATCH-FLOW-REVIEW.md before changing
scoring or death handling: terminal death interception must remain armed until
the native player owner is verified and the intentional protected exit starts.

Scoreboard seats (2026-11-08, docs/reviews/FORK-QUICK-WINS-20261108.md): a TDM/Domination bot's
scoreboard identity is its seat (`usec-n`/`bear-n`, the lowest seat no living bot of the faction
holds), passed as the roster `StableId`; a replacement body continues the seat's row, name and
stats, a vacant seat is passed as OUT with its last profile id. The seat name reaches the
killfeed through `TeamSeatNames` (AI only, reset at match start and end); never write a bot's
`Profile.Info.Nickname` for it, and keep kill classification on profile ids. A refused native
arrival is retired with its failing gate named (`ArrivalRejection`); keep the gate order and
the relations registration last, it has a side effect. Generated TDM/Domination bots lose
their backpack in the server's armband pass by user decision, without a switch.

### From the handoff (snapshot of 2026-10-29)

Working notes of 2026-10-29 that carry a still-valid constraint: verbatim excerpts, or restatements where marked Quoted:/Rule:/Durable form:. Line numbers refer to `handoff-archive/HANDOFF-2026-10-29.md`.

Native exit requires the exact game/world/profile and a valid current owner;
terminal factory failures never route through the generic unsafe-owner abort.

*(handoff lines 5233-5234)*

TDM bot preparation (profile/pool loading) is timed separately from native arrival. The 45-second arrival deadline (`TeamDeathmatchPopulation.RequestTimeout`) starts only at native submission. Preparation is bounded only by the 300 s terminal watchdog, with one preparation per faction and at most three outstanding tickets. The SAIN hearing search cue is one guarded cue per native contact episode. Optional kit-catalog warmup waits until the initial TDM teams and the current native kit loading/respawn work have settled. See `TDM-RETEST-FOLLOWUP.md`, `TDM-PREPARATION-REVIEW.md` and `TDM-AWARENESS-REVIEW.md`.

*(handoff lines 5316-5327)*

Early combat can
  delay that latch, but required kits/refills never wait for optional warmup.
  Preserve the profile checks. The 20-second first cold Vanguard load is not a
  proven timeout defect; the two Unity archive-close errors remain unattributed
  and were not suppressed or patched speculatively.

*(handoff lines 5347-5351)*

## Modes: Tactical and Zombies (PitFireTeam)

No Fika dependency. Arena is solo by default; the optional API-validated
PitFireTeam compatibility path supports protected Tactical (PMC + 1-4 companions versus five enemies) and
Zombie squad survival; read docs/reviews/PFT-SQUAD-SIZE-REVIEW.md for the frozen roster contract; read docs/reviews/ZOMBIE-PITFIRETEAM-REVIEW.md before changing the latter.

Zombies offers solo or a protected one-to-four-companion PFT squad, with endless waves of EFT's own infected reactivated for exactly
one armed raid, with Arena's own wave director replacing the native crowd
spawner. Solo has one life: death freezes survival and exits protected after any
selected replay; only the PFT squad restores fallen members between cleared waves.
See docs/reviews/ZOMBIE-MODE-REVIEW.md before touching the native Halloween-event
hooks, the wave policy or its stuck-wave watchdog.
The opening wave now waits for player/kit/team readiness and then the configured
intermission countdown. PFT wave breaks restore the surviving PMC's entry gear
through protected live-body replacement as well as restoring fallen members.
Read docs/reviews/ZOMBIE-WAVE-REFILL-20261015.md before changing these ownership seams.

participants, Tactical and Zombies own their fixed/wave rosters. Read
docs/reviews/CUSTOMS-ALL-MODES-REVIEW.md and docs/reviews/PFT-PORTED-MAPS-REVIEW.md for bounded PFT placement,
deferred native follower startup and exact-world area readiness before mode clocks.
PFT changes the local entry category to Coop; only the exact frozen squad's
Tactical/Zombie loading request may use that category for the bounded PMC start.
Initial roster discovery waits until the deferred bounded mode can bind its
leader; any prior leader/profile/body history retains the strict live checks.
Bounded duplicate IDs and raw creation leases belong to the exact native raid
creator; never clear old-creator tombstones while delayed creation can resume.
Protected Tactical keeps its five frozen opponent bodies until roster retirement
because identity validation still reads them after death. Read
docs/reviews/TACTICAL-CORPSE-REVIEW.md before changing physical release; normal hiding/stripping
and strict unexpected live-body loss checks remain active.
Read docs/reviews/PORTED-COVER-REVIEW.md for Bay5/Equator's empty-cover combat guards: retain
real native NavMesh results, never manufacture cover points or successful paths.

**PitFireTeam 5v5 boundaries** (old `AGENTS.md` State bullet; the GPU playtest it asks for has since run, see docs/reports/2026-10/GPU-PFT-TACTICAL-KITS-20261018.md):

- **PitFireTeam 5v5** — the optional 0.10.0/0.10.3+ compatibility path now has a guarded
  best-of-N round lifecycle and still needs a GPU playtest. It preserves the PMC entry
  loadout after a death, rebuilds fresh follower/opponent bodies between rounds, offers
  post-death third-person spectating, refreshes both teams' pursuit knowledge, and uses
  a bundled optional companion for BigBrain 1.5.0+ (newer versions warn) to own bounded movement toward live opponents while
  the PMC is down. It excludes teammate bots/corpses from Arena cleanup/counts and blocks
  both SPT-profile effects and PitFireTeam's separate teammate persistence. Every
  teardown/rebind step fails closed as no-contest when the exact reflected contract or
  body identities drift.

### From the handoff (snapshot of 2026-10-29)

Working notes of 2026-10-29 that carry a still-valid constraint: verbatim excerpts, or restatements where marked Quoted:/Rule:/Durable form:. Line numbers refer to `handoff-archive/HANDOFF-2026-10-29.md`.

Once the exact native raid has committed Stopping/Stopped (`ArenaRaidExit.NativeStopOwnsCurrentRaid`), ArenaMatchService.Tick, SynchronizeScoreboard and PitFireTeamCompat.TryGetLiveFollowers stop mode/roster progression and new reflected PFT roster discovery. Native bot disposal comes before Arena's GameEnd cleanup, so that window is not a live roster loss. A leave request or StandDown does not prove the stop. A native callback that throws before commitment stays Started and keeps live validation. Read docs/reviews/NATIVE-STOP-FOLLOWUP-20261018.md (commit `f64d74f`).

*(handoff lines 913-915)*

PFT Tactical equipment choice: the map screen offers Own gear (default) or Arena kits, for the local PMC only. PFT companions keep their stored equipment. One base kit is issued before the first native player/leader body. The existing protected round restore reissues that entry kit after the PMC dies. There is no team buy phase, and PFT Zombies keeps own gear. Read docs/reviews/PFT-TACTICAL-KITS-20261018.md and docs/reports/2026-10/GPU-PFT-TACTICAL-KITS-20261018.md for acceptance limits.

*(handoff lines 964-971)*

- **Evening player-session follow-ups (2026-10-15):** `f0a6805` adds the first
  Zombie-wave countdown after player/kit/team readiness and restores a surviving
  protected PFT leader's entry gear between waves using the existing guarded
  live-body replacement. Dead-player authorization remains separate. It also
  stops obsolete live-roster polling during the exact terminal Zombie result;
  pending completion retirement and persistence guards remain active.

*(handoff lines 1354-1359)*

- **Optional Zombie Crowd fixes (2026-10-14):** All four reviewed defects fixed
  in `ff28b75`: no Crowd renderer/animator/pooled-skin mutations; bounded recall
  uses the actual validated sample with physical clearance and occupancy checks;
  skipped avoidance clears both native offsets. New configs default Crowd OFF;
  saved choices remain intact. A map-screen ZOMBIE CROWD switch replaces FAST
  BOTS only in Zombies, retaining 22 visible controls. OFF restores pre-Crowd
  behavior without Crowd teleports; it does not undo previous placements.

*(handoff lines 1449-1455)*

Equator restrictions and protected persistence remain unchanged. Zombie fresh
defaults are **32 / 50 / 20 / own gear**, explicitly chosen by the user despite
the stricter 60-FPS recommendation below. Saved preferences are preserved.

*(handoff lines 1494-1496)*

Native integration follow-ups: `27fdf80` accepts the exact PFT Coop entry,
`9aad58c` preserves native NavMesh behavior without absent cover-graph fallback,
`91db119` defers failed-start Stop and revokes pending ambient/managed spawns,
`e55d6a7` defers initial roster discovery until bounded mode leader binding,
`21b6af6` scopes duplicate/late spawn ownership per native raid creator, and
`f462bfb` retains five frozen Tactical opponent bodies until roster retirement.

*(handoff lines 1672-1677)*

- **Zombie pending-wave fix and retreat measurement (2026-10-09, latest):**
  `5637ce7` shares the pending-arrival clearance barrier across solo/PFT and
  watchdog retirement; pending requests no longer expire into free capacity
  after 20 s. Known preparation failure retries exactly once; unresolved work
  ends protected after separate 300 s preparation / 60 s native arrival limits.

*(handoff lines 1739-1743)*

Solo Tactical waits for Active opponents (recognizing early eliminations) before Round Start and combat timing. Existing partial-ready timeout remains 45 seconds plus activation grace for late bodies. Zero-ready retries do not rebuy/swap or announce. Logical award reset happens before dispatch; delayed audio skips that reset, preserving early First Blood and kill chains. [...] Solo Tactical uses an explicitly free cheapest valid starter without a purchase. [...] Preloading uses the same starter. Initialization failures use protected abort, and PFT keeps its entry gear. Countdown Esc routes the exact local owner to native menus, closes the buy overlay and pauses countdown progress without dropping death/profile protection. See `TACTICAL-START-POLISH-REVIEW.md`.

*(handoff lines 3519-3530)*

- **Solo Tactical: the raid-start kit now counts as worn (2026-09-11, 07:28):**
  this commit on `feature/arena-mantle-reload`. Pre-existing defect found by
  the adversarial review of `af3647d`, not caused by it: the solo kit provider
  fell through to `GetBaseKit` for the raid-start swap, but nothing recorded
  that kit in `_worn`, so a round-one survivor who bought nothing was handed a
  NEW random draw at every round break (RandomKit on; `PickRandomKit` never
  repeats the last draw, so a different kit was certain) under the "you keep
  your gear" toast. A configured kit hid it, which is why the first rounds
  played never showed it. The provider now records the base-kit draw as worn
  at hand-out time; the dead `_bought = Affordable()` store at match start is
  gone (EnterBuy cleared it before anything read it), so Tactical still opens
  on the base kit as `KitPreloadPlan.OpeningKit` and its test require.

*(handoff lines 4760-4771)*

Playtest: solo Tactical, RandomKit on, survive round 1 without buying; round 2 must re-issue the same kit, and the client log must show one `Arena kit drawn:` line per match, not one per round.

*(handoff lines 4775-4777)*

## Maps and areas

Equator remains unavailable for Tactical and is also excluded from PFT Zombies
because the five-person squad obstructs itself; solo Zombies stays supported.
Iceberg is unavailable for Tactical, solo and PFT, for Equator's other reason: from
about 48 m² of its centre only four of its nine bot markers lie beyond the 25 m
floor. Solo and PFT Zombies stay supported. The block sits in the shared resolver
next to Equator's; do not remove it to claim the map fully accepted. Iceberg needs an
ArenaMapPort build that ships it (the unreleased feature/iceberg, 0.4.0+32fda33);
detect it by the live location, never by the MapPort version. See
docs/reviews/ICEBERG-INTEGRATION-20261031.md.
Air Pit is an optional full map for all current modes, using its existing safe
generic spawn markers and the same exact-raid PFT coverless adapters as Bay5.
Independent AirPit Team DM requires MapPort 0.2.2 navigation and passport gates
for its original north USEC / south BEAR boxes; see docs/reviews/AIRPIT-TEAM-BOXES-REVIEW.md.
See docs/reviews/AIRPIT-INTEGRATION-20261017.md for migration and native-test boundaries.

The September 28 contours use new versioned IDs; all earlier area IDs remain
invalid on the wire. Config migration preserves exact recognized preferences
only. Yard adds twenty private per-raid spawn records around two native ground
anchors using NEW IDs (native FixParams does not change existing marker zones).
Never repair extras during final response validation or mutate native donor rows.
All areas retain unlimited height and fail-closed live navigation/occupancy checks.
See docs/reviews/CUSTOMS-AREAS-REVIEW.md and docs/reviews/AREA-TEAM-SETUP-REVIEW.md; database candidate
capacity and offline checks are not GPU acceptance of the new Yard offsets.

**User-authorized survey helper exception (2026-09-07)**

The area-outline development tool may read scene positions, draw its preview and
explicitly export draft files in a normal local PMC raid when BOTH `Enabled` and
`AllowInNormalPmc` are enabled. The user needs to walk beyond the existing Arena
contour. This does not authorize ordinary-raid gameplay/profile changes, Arena
activation, invulnerability, Scav/Hideout capture or weakening any Arena barrier.
Both switches default off. See `docs/reviews/POLYGON-AREA-REVIEW.md` for the exact boundary.

### From the handoff (snapshot of 2026-10-29)

Working notes of 2026-10-29 that carry a still-valid constraint: verbatim excerpts, or restatements where marked Quoted:/Rule:/Durable form:. Line numbers refer to `handoff-archive/HANDOFF-2026-10-29.md`.

- **Transits, Gun Game warm-up and loose ammunition (2026-10-24):** confirmed
  Arena location clones copy transit records with `IsActive=false` (never clear
  their IDs). Ordinary raids and extracts stay intact.

*(handoff lines 351-353)*

- **PFT ported maps and unified Customs areas (2026-10-09, latest):** `54d9f7e`
  fixes the installed PFT empty-cover startup dereference using an existing real
  core only in exact protected Bay5/Equator raids. Customs now requires one of
  its three surveyed areas in every Arena mode; ordinary PMC is unchanged.

*(handoff lines 1668-1671)*

Equator Tactical remains deliberately blocked: five opponent points at the
existing minimum distance are not available from every possible surviving
player position. Do not remove that guard to claim the map fully accepted.

*(handoff lines 1702-1704)*

Customs areas: the 2026-09-28 contours use new versioned geometry IDs, so old and new bounds never mix between client and server. Known saved Center/Side selections are migrated, and old IDs stay invalid on the wire. Yard adds 20 private per-raid start candidates (2 player + 8 bot per faction) around native ground anchors, with NEW IDs and correct zone/core in the private raid clone. Original markers and global data stay untouched. Native navigation, complete in-contour paths, occupancy and team size decide admission, with no unsafe fallback spawns. See docs/reviews/CUSTOMS-AREAS-REVIEW.md.

*(handoff lines 2692-2702)*

Native Customs area map preview (ArenaMode.Client/Session/Maps/ArenaAreaMapPreview.cs, ArenaAreaMapGraphic.cs, ArenaAreaMapProjection.cs): read docs/reviews/AREA-MAP-PREVIEW-REVIEW.md before changing it. Image and polygon share one calibrated projection; no runtime DynamicMaps dependency, live tracking, draft loading or new setting/arm side effects; normal menus and other maps keep native information. Map artwork (ArenaMode.Client/Assets/MapPreviews/) is CC BY-NC-SA 4.0, with source, licence and provenance packaged separately from code terms.

*(handoff lines 3619-3636)*

Corner navigation now uses full-height box obstacles via the same size API as edges; physical limits, grid, area/spawn rules and server remain unchanged. The boxes carve slightly more conservatively at diagonals (up to 0.125 m extra); all navigation readiness checks stay strict. See `CUSTOMS-CONTOUR-REVIEW.md` for implementation and subsequent user success.

*(handoff lines 5477-5481)*

Area selection shares the existing F12 `ExperimentalArea` setting. Map/time/mode changes preserve compatible choices and normalize incompatible ones during menu selection; ... Final client/server arm validation remains strict and never repairs settings. Configured native rows are constructed before the mode view's PFT repair, avoiding an empty picker from a stale F12 mode. ... Edited allowlists and explicit server disables remain authoritative.

*(handoff lines 5633-5640)*

## Spawns, team boxes and spawn gates

Independent TDM bots on catalog-backed team-box maps (Equator, Bay5, Air Pit, Bowl and Iceberg)
own finite exact-body spawn damage shields with first-attack release. Other maps
receive no bot spawn shield; local-player protection is unchanged. Read
docs/reviews/BOT-SPAWN-SHIELD-SCOPE-20261019.md and docs/reviews/TDM-BOT-SPAWN-SHIELD-20261017.md
before changing damage admission, attack hooks or shield lifetime. Never reuse
historical team IDs as shield authority or modify their DamageCoeff.

**Bowl team boxes and gate hold** (the paragraph starts with the Bowl map admission):

Bowl is optional in all five modes, including PFT Tactical/Zombies, with the
same exact-raid coverless adapters (default eight for DM/Gun Game/TDM).
Independent TDM uses its original east USEC / west BEAR boxes, oriented interior
bounds, twenty private markers and the existing box-only bot shield. Its owned
release waits for MapPort's optional public GatesOpen signal AND all twenty
complete outside paths and floor/capsule checks before combat. Only an absent
GatesOpen member retains the legacy 13.5-scaled-second Bowl minimum; malformed
metadata or a throwing getter fails closed through protected match abort. The
twenty-second watchdog remains. Never use IsHeld=false or elapsed time alone as
proof of open navigation. Other modes use generic spawns and no hold. Read
docs/reviews/BOWL-TEAM-BOXES-20261023.md and docs/reviews/SPAWN-GATE-COMPLETION-20261024.md; revision-4
migration is documented in docs/reviews/BOWL-INTEGRATION-20261023.md.

**Iceberg team boxes** (the paragraph starts with the Iceberg map admission):

Iceberg is optional in Gun Game, DM, Team DM, Domination and solo/PFT Zombies, not
Tactical (default eight for DM/Gun Game/TDM). Independent TDM and Domination use its
CheckPoint pockets, USEC west / BEAR east, with twenty private markers. The box
interiors are not the authored SPAWN_BOUNDs, which reach past the closed gates:
each world-aligned box ends .35 m short of its gate plane, the USEC box runs north to
the rotating CheckPoint gate and the BEAR box height is cut to -.4..5 m. Keep that
clipping: an interior that reaches past a closed gate would excuse a held path or a
pickup seed outside it. Gate opening has no Iceberg case (no legacy minimum, the
ordinary ten-second watchdog): every build that ships Iceberg has GatesOpen. Donor
and outside destination is the installed arena_iceberg-bot-core-01. Revision-5
migration, the box derivation and the native-test boundary are in
docs/reviews/ICEBERG-INTEGRATION-20261031.md.

Independent Equator, Bay5, AirPit, Bowl and Iceberg Team DM use their original team boxes for initial
placement and respawns (4–20 total participants). Equator is north USEC / south
BEAR; Bay5 is east warehouse USEC / southwest depot BEAR. Read
docs/reviews/BAY5-TEAM-BOXES-REVIEW.md for the map-keyed catalog/runtime. The exact
raid clone adds twenty private versioned markers; never move or overwrite the
common MapPort markers. Read docs/reviews/EQUATOR-TEAM-SPAWNS-REVIEW.md before changing the
MapPort repair ordering, initial physics/live navigation gates, reservations or
body-arrival validation. No map-wide fallback is allowed after box negotiation.
With MapPort's optional SpawnGates API, those booths stay closed through assembly
and countdown. Only the exact owned hold permits a complete intra-booth path
instead of the temporarily cut atrium route; Live still requires full navigation
after opening. See docs/reviews/EQUATOR-SPAWN-GATES-20261017.md for gate ownership,
historical preparation extraction deferral and cleanup.
Since 2026-10-25, ArenaExtractionIsolation owns extraction blocking across every Arena mode/map;
team-box opening must never replay deferred native extraction entries.
Ordinary PMC/Scav extraction and manual Arena match exit remain native.
See docs/reviews/TDM-SIDES-MONITORS-20261025.md for the scope and validation.
The completion contract is polled
only after the owned release and while the exact game/world/arm/token still
matches; never subscribe to MapPort's process-global GatesOpened event. Keep all
twenty navigation checks and the simulation-time watchdog (20 seconds on Bowl,
10 on the other box maps). Never re-close during live respawns.

### From the handoff (snapshot of 2026-10-29)

Working notes of 2026-10-29 that carry a still-valid constraint: verbatim excerpts, or restatements where marked Quoted:/Rule:/Durable form:. Line numbers refer to `handoff-archive/HANDOFF-2026-10-29.md`.

- **Equator Team DM team boxes (2026-10-11, deployed and GPU-tested):** `381f784`
  and nullable-annotation follow-up `96ed83b`; the exact
  per-raid clone adds twenty private original TeamFight markers (USEC north,
  BEAR south) without changing common MapPort points. The client restores only
  those records after MapPort's conservative booth repair; initial placement,
  bot activations and player respawns use the corresponding box, with real
  floor/clearance/path/occupancy checks and persistent native reservations.
  Total participants are 4–20, default four. Tactical/PFT Zombies remain blocked;
  other modes and ordinary raids retain their general starts. Delayed respawn
  continuations now also require the exact current native game/world pair.

*(handoff lines 1617-1626)*

Team-start readiness requires the actual 1 PMC + N-1 allies versus N opponents. It verifies contained bidirectional native paths between the existing native markers. Fresh final and placement checks reject stale paths, and actual bot path corners are still checked independently. Areas without team starts keep direct validation. See `TEAM-START-ACCEPTANCE-REVIEW.md` and `AREA-CONNECTIVITY-REVIEW.md` (also `ROLE-LABEL-REVIEW.md`, `GPU-ACCEPTANCE-COMPAT-REVIEW.md`).

*(handoff lines 5384-5390)*

## Bots and population

Map-screen participant totals include one reserved human slot. Negotiated
Deathmatch/Gun Game owns its single-bot roster and optional Labyrinth boss slot;
native waves and unowned creator calls are suppressed only for the exact selected
Arena raid, including its confirmed matching-map loading phase. Never reset
ArenaSoloPopulation during the player's respawn or expire dispatched reservations
into free capacity. New full-map totals require exact quiet-clone validation on
the server, not just the existing bounded-area check. See docs/reviews/PARTICIPANTS-REVIEW.md
and docs/reviews/SOLO-POPULATION-REVIEW.md before changing this admission contract.

Bot locomotion has independent Arena-only walk/sprint controls. Read
docs/reviews/BOT-MOVEMENT-REVIEW.md before changing the native motion/cap bracket or SAIN door
guard; preserve exact live body identity, native collision and finalizer cleanup.
The 2026-10-27 bot speech/tempo follow-up is documented in
docs/reviews/BOT-TALK-TEMPO-20261027.md: Normal combat-taunt eligibility is a scoped
operand override, never a shared SAIN preset write; combat strafe floors at 0.85
and independent TDM/Domination search delay inputs cap at three seconds.
Normalized movement targets alone do not provide the displacement bonus.

### From the handoff (snapshot of 2026-10-29)

Working notes of 2026-10-29 that carry a still-valid constraint: verbatim excerpts, or restatements where marked Quoted:/Rule:/Durable form:. Line numbers refer to `handoff-archive/HANDOFF-2026-10-29.md`.

SAIN tempo adapter (`ArenaBotTempo`, SAIN 4.5.1+): after a completed reload, the search wait tail is cut to 0.5 s of safe observation; sprint is promoted only on complete existing search routes with >=4 m to the next corner and >=8 m remaining; positive native `DogFightMove` strafe values are raised to a floor (now 0.85, see BOT-TALK-TEMPO-20261027.md). Native reload/heal/danger actions, doors, navigation and real sprint conditions stay native; never write a SAIN preset and add no extra F12 controls; `BotMovementEnabled` gates it (via `ArenaBotMovement.IsLiveBot`); foreign PFT/Arena pursuit actions stay untouched; the displacement maximum of 1.5 and the 0.3 m step ceiling stay unchanged. Details: BOT-TEMPO-REVIEW.md.

*(handoff lines 2747-2754)*

Bot pickup objectives (BigBrain companion): before changing them read PICKUP-OBJECTIVES-REVIEW.md for behaviour, acceptance cases and limits. Keep the review-fixed invariants: a round change during a callback, door return and final claim invalidation.

*(handoff lines 2914-2917)*

Participant totals (map screen minus/value/plus, including the player, stored per map and mode under BepInEx 'Map participants'): DM/GG 2-24. Independent Team DM uses even values, 4-24, or 4-20 on team-box maps (`ArenaParticipantPolicy.MaximumFor`); team-box TDM requires an explicit count. Defaults come from `ArenaParticipantPolicy.DefaultTotal`: Equator 4; Bay5/AirPit/Bowl/Iceberg 8; Labs 16; Labyrinth 8; Factory and others 10.

*(handoff lines 2966-2969)*

Participant admission: a counted Labyrinth boss slot needs the native boss initialization, and a second failed spawn callback must not release the reservation again. Play-test steps: PARTICIPANTS-REVIEW.md. Menu, server and solo-population details: PARTICIPANT-MENU-REVIEW.md, PARTICIPANT-SERVER-REVIEW.md, SOLO-POPULATION-REVIEW.md.

*(handoff lines 2984-2991)*

Finite independent TDM now linearly scales existing pressure and equipment
against frozen live-play duration, reaching full curve at 80% (5min -> 4min).
Assembly/countdown are excluded; streak surge/caps remain. PMC impossible
starts at 64% absent streak. Existing bots are not retroactively re-equipped.
Native provider-scoped requests carry the exact arm clock without polling;
one-profile demands, unused cache-reference exclusion and exact PMC backup
suppression prevent early equipment stockpiles. Partial hook installation
leaves generation native. Normal raids/unlimited modes/PFT preserve their
documented boundaries (unlimited pressure live time, gear server raid time).

*(handoff lines 5068-5076)*

TDM/Domination search tempo (`TdmSearchTempo`, SAIN 4.5.1+) caps only the delay argument of SAIN's two native search predicates, now at 3 s (not the 5 s at line 5100), and only for adopted live independent-TDM opponents. All native predicates still run. Native priorities, shared SAIN Info/personality, forget timers, contact and movement state are never written. No teleport or path rewrite; normal raids and PFT stay outside. See `TDM-SEARCH-TEMPO-REVIEW.md` and `BOT-TALK-TEMPO-20261027.md`.

*(handoff lines 5096-5104)*

## Pickups, powerups and the Molotov

The dedicated Arena Molotov is a private, natively harmless item; ordinary RGD-5s
are never converted. Read docs/reviews/MOLOTOV-ITEM-REVIEW.md and
docs/reviews/MOLOTOV-PRESENTATION-REVIEW.md before changing its early database registration,
confirmed private-item capability, one live rig/pocket supply, native custom flea exclusion, bottle pool ownership
or local-only attributed fire. Registration must precede SPT's profile snapshot;
never weaken the harmless read-back to accommodate another mod's database changes.
No downloaded reference sound is licensed for this feature; the shipped effects
are synthesized by ArenaFireSound. Native test boundaries are recorded separately
in docs/reports/2026-10/GPU-MOLOTOV-ITEM-RESULTS-20261012.md. The current integration and single
client supply authority are documented in docs/reviews/MOLOTOV-NATIVE-INTEGRATION-20261015.md.

Arena-owned powerups and automatic weapon pickups are limited to live Deathmatch
and independent TDM. Read docs/reviews/PICKUPS-REVIEW.md before changing damage, scene or native
inventory ownership: only confirmed transfers consume a pickup, ambiguous/pending
failures never rearm it, and pooled weapon models must never be destroyed or parented
by the marker view. Gun Game, Tactical/PFT and ordinary loot are excluded.

The accepted pickup prototype extends this read-only exception to its separate
`Development - Pickups / Enabled` capture tool (both Development sections and
Diagnostics are F12 advanced entries, see docs/reviews/CONFIG-MENU-20261018.md) plus the same explicit
`AllowInNormalPmc` switch. It may preview coordinates and export new PickupDrafts;
it must never spawn loot, grant effects or automatically install a layout there.
See `docs/reviews/PICKUPS-REVIEW.md` for per-map/area layout admission and capture shortcuts.

### From the handoff (snapshot of 2026-10-29)

Working notes of 2026-10-29 that carry a still-valid constraint: verbatim excerpts, or restatements where marked Quoted:/Rule:/Durable form:. Line numbers refer to `handoff-archive/HANDOFF-2026-10-29.md`.

Pickup modes remain DM/independent
  TDM; Factory positions/custom layouts stay intact. The three selected Customs
  areas have separate pools, never a full-map fallback.

*(handoff lines 1049-1051)*

- **Dedicated Molotov correction (2026-10-15):** Integrates the existing
  `feature/molotov-item` work through `93d614e`, which was absent from the prior
  master build. The extra is now the private Arena Molotov with bottle, icon,
  original fire visuals and synthesized audio. No ordinary RGD conversion or
  fallback remains. Only the client live-grant supplies it; the old branch's
  server kit addition is intentionally omitted to prevent duplicates and retain
  the current mutator/own-gear behavior. Raid confirmation binds availability to
  the exact registered server object and active token; client template readback
  also guards grants and presentation/explosion hooks against foreign ID clashes.

*(handoff lines 1325-1334)*

`0cd9a48` adds one separately tracked Molotov in free rig/pocket space, preserving
all original RGD-5s and preferring the extra once for G. No free slot means no
bonus or displacement; the prototype still has the RGD-5 model/icon/name.
Zombies remain excluded from Molotov. Own-gear donation waits for native pools
and follows entry-snapshot capture; each fresh restored equipment tree gets
one bonus, never an extra on toggle or after consumption.

*(handoff lines 1360-1365)*

Both remain unavailable in Zombies; Molotov needs the live matching BigBrain companion.

*(handoff lines 1528-1529)*

Health pickup (white cross): up to 150 HP on surviving damaged parts. `ArenaHealthPickup.Amount` is the one value that the marker label, help text, log and bot target utility all read. Native maxima, destroyed/blacked parts and wounds (bleeds/fractures) remain, and it respawns after 20 s. See PICKUPS-REVIEW.md and PICKUP-OBJECTIVES-REVIEW.md.

*(handoff lines 2776-2778)*

Built-in Factory day/night: six pickup entries per round, drawn from one shared pool of eight candidate positions; Domination additionally extends that built-in layout with objective-safe candidates (`ExtendFactoryForObjectives`) while the original eight seeds remain the navigation authority.

*(handoff lines 2779-2780)*

Pickup placement diagnostics: each attempt logs player/anchor, alternatives, geometry probes and all refusal counts at Debug (not Info, per LOGGING.md). Final per-point/anchor warnings appear only when the 15 s window expires. Native test limits: PICKUPS-REVIEW.md.

*(handoff lines 2801-2803)*

The final placement diagnosis must keep a not-yet-ready body/anchor ('no safe placement anchor within 15 seconds ...') separate from an actual per-point path/geometry failure.

*(handoff lines 2809-2810)*

The exact-instance examination exception for pickup weapons writes nothing to the profile or templates, keeps reference identities and phase boundaries, and must use weak keys/references (ConditionalWeakTable/WeakReference) so that grants never keep retired weapons, bodies or worlds alive.

*(handoff lines 2859-2860)*

When a weapon pickup is refused, read the Debug line 'Arena pickup weapon not taken ... planRejected=..., reasons=[slot=ErrorType: detail]'. It gives the native error class and description for each target slot; the old `incompatible` counter did not tell causes apart. These diagnostics must catch formatter exceptions, cap their output (512 chars per error), and never loosen ownership, round, hands or inventory checks or create a new transaction.

*(handoff lines 2879-2890)*

Keep all four user-accepted Factory pickup positions in the eight-position candidate pool (see the comment in `ArenaPickupLayout.Factory`).

*(handoff lines 2999-3000)*

Gold weapon pickup now uses the copied M60E6 FDE `retzrestdfg` from the actual
`spt41dev` profile, MRS/blue laser/RK-1/Fortis RED and 100 M993 (40+40+20).
Fresh native IDs, no normal catalogue insertion or runtime profile access.
Source profile SHA256 stayed byte-identical; native examination stays required.
M60 requires a free compatible **inactive** weapon slot and never auto-draws.

*(handoff lines 3143-3147)*

## Movement and combat

Team Friendly Fire is a raid-frozen mutator: Team DM and PFT Tactical default on,
PFT Zombies off; solo is unavailable. Read docs/reviews/FRIENDLY-FIRE-REVIEW.md before changing
damage admission, physical team identities or attributed grenade effects. Do not
use historical tracer IDs as authority to grant immunity to replacement bodies.

Arena landing recovery, firearm draw/holster and deliberate airborne vault/hip
fire are documented in docs/reviews/AIR-MOVEMENT-REVIEW.md. Preserve exact local-body guards,
native geometry/firearm restrictions, temporary-state cleanup and double-jump
precedence; offline checks cannot certify native animator transitions.
Optional timed Bunnyhop is documented in docs/reviews/BUNNYHOP-REVIEW.md. Preserve actual
controller contact, one input owner, native landing/Jump entry, collision-reduced
momentum and single-application displacement scaling; never grant hops from the
ground-proximity ray alone or cancel every ordinary native Fall limit.

Arena's local default full-auto rule is documented in `docs/reviews/DEFAULT-FIRE-MODE-REVIEW.md`.
Preserve manual selection while held, native available modes and exact confirmed
raid/body/hands ownership; the standalone DefaultAutoFireMode must be disabled
when testing Arena's own rule in isolation.

### From the handoff (snapshot of 2026-10-29)

Working notes of 2026-10-29 that carry a still-valid constraint: verbatim excerpts, or restatements where marked Quoted:/Rule:/Durable form:. Line numbers refer to `handoff-archive/HANDOFF-2026-10-29.md`.

The exact local Arena surgery-walking exception also covers leg-fracture (splint) treatment, with 'the same 1 m/s cap and FAST MOVEMENT gate' (ArenaSurgicalWalk.MaximumSpeed = 1f). It only extends the native walking predicates: HealingLegs/UsingMeds still block sprint, jump, prone and vaulting, and no speed-limit slot, medical effect or player state is rewritten (ArenaSurgicalWalk.cs:16-24). See docs/reviews/SURGICAL-WALK-REVIEW.md and docs/reports/2026-10/ISOLATION-REVIEW-20261019.md.

*(handoff lines 793-795)*

'Skill restoration now stops new boosts on failure, restores serializable mastery first, retires successful writes and retries only outstanding values without overwriting original baselines or ordinary XP.' (ArenaSkills.cs:77-79 'Never replace an unrecovered baseline with a boosted value'; the restore loop around L135-175 keeps retry state until ArenaRestoreCommit.ShouldDropRetryState.)

*(handoff lines 795-798)*

- **Dependencies / default full-auto / vest HUD (2026-10-11, deployed/native passed):** `DEPENDENCIES.md` records
  actual hard/conditional/optional dependencies, absorbed own mods and release
  boundaries. DefaultAutoFireMode was previously separate only. Arena now owns
  native full-auto selection for its exact living local player at each completed
  draw, plus once per published initial/replacement controller. Manual switching
  while held stays intact; unsupported modes, ordinary raids and bots are not
  altered. Standalone remains independent for ordinary raids and retains its
  broader fallback rules.

*(handoff lines 1591-1598)*

- **Equator PFT Zombies and Friendly Fire (2026-10-11, deployed/native follow-up):**
  `d265b4a` excludes Equator only from PFT Zombies in addition to its existing
  Tactical block; solo Zombies remains available. `100c003` adds independent
  raid-frozen Friendly Fire preferences: Team DM/PFT Tactical ON, PFT Zombies OFF,
  solo N/A. Health/armor and attributed native grenade effects use exact physical
  team admission; self/enemy/environment damage and AI hostility remain native.

*(handoff lines 1645-1650)*

Local player only, Arena movement must be enabled; bots do not consume them.
Refresh never stacks; expiry/death/scope reset remove the bonus. Native gravity,
collision, stamina, ordinary raids and existing air-vault limits stay intact.

*(handoff lines 3140-3142)*

Preserve the working `9a550a5` implementation and 0.5m actual approach gate;
greater remaining height would be a separate physical/animation extension,
not an unverified limit increase. See AIR-MOVEMENT-REVIEW.md for frozen log
path/hash and scope of acceptance.

*(handoff lines 4786-4789)*

Implementation `9a550a5` on `feature/arena-mantle-reload` additionally requires
actual planar body-to-selected-surface distance <=0.5m before extra airborne
Climb. Held input retries, including final dispatch refresh; height remains
separate. Native Vault preference, standing reach, physics/global grid and
reload handling stay untouched. The selected sample need not be the nearest
edge: fast approaches may wait for wall contact and a slower refreshed scan.
No forced positioning or physical success claim.

*(handoff lines 4803-4810)*

F12 Movement `WeaponReloadSpeedMultiplier` defaults to **1.5**, range 1-3.
All six reviewed regular reload families (including quick reload, internal
magazines and revolvers) use separate native arms/body reload baselines with
skill/injury refresh preserved. Owned values restore on completion, interruption,
death, controller change and raid end. Ordinary raids, AI, firing, inventory
round loading, malfunction clearing and underbarrel launchers remain unchanged.
UBGL has an additional animator and needs separate synchronization validation.
See `RELOAD-HANDLING-REVIEW.md` for limits and the GPU test checklist.

*(handoff lines 4959-4967)*

A potentially inlined native mid-operation speed refresh safely relinquishes
acceleration for that operation; do not replace this with global speed hooks.

*(handoff lines 5220-5221)*

Double jump: F12 Movement `DoubleJumpEnabled` (default true) and `DoubleJumpHeightMult` (default 1, range 0.25-1.5) give the living local Arena PMC one extra jump per airborne episode with the normal jump binding. Since version 2 (2026-09-12) this also works after walking or falling off a ledge; the old 'ledge-fall rescue is excluded' wording at line 5431 is outdated. Native collision, lateral motion and stamina stay in charge. Bots, ordinary raids, spectators and respawn handover are excluded, and a pending Arena mantle keeps precedence. See `DOUBLE-JUMP-REVIEW.md` and `tools/DoubleJumpChecks/GATES.md`.

*(handoff lines 5426-5432)*

## HUD, menus, audio and voice

Automatic local speech and TDM speaker-only voice rotation are documented in
docs/reviews/PMC-VOICELINES-20261026.md. Keep exact current-body/raid guards, announcer
priority, private bundle ownership and native queued/pain/death speech intact.
The selected bank remains retained through result holds until native raid end;
never change saved faction/customization or unload the shared voice loader.
Since 2026-10-28, TDM/Domination faction identity is independent of the automatic
PMC-comment switch; OFF must retain the faction bank and suppress only Arena's
own cues. See docs/reviews/FACTION-VOICE-20261028.md for lifecycle and acceptance evidence.

Spawn and pickup buff indicators share a private, depth-occluded inner body
contour. See docs/reviews/BOT-EFFECT-OUTLINES-20261019.md before changing rendering or
camera/lease cleanup. Never clone native player components, mutate pooled meshes
or materials, add a positive depth bias, or draw an unchecked through-wall halo.

`docs/reviews/HUD-MUTATORS-REVIEW.md` documents the scoped ARENA HUD switch: preserve individual
HUD preferences, essential match notices and the separate audio controls.

Hold-to-peek table (2026-11-08, docs/reviews/FORK-QUICK-WINS-20261108.md): `ArenaInventoryKeyNode`
joins EFT's input tree with `InputTree.Add` (visited before every screen and the player owner) and
returns an explicit Block only for `ToggleInventory`, only in the battle UI of an active Arena
session, outside the respawn pipeline, while no Arena panel blocks input and while the peek is
healthy; everything else is Ignore, so the inventory screen keeps its own close key. Both press
types of the binding are handled (down: block and forward a tap to the game's own started
`EftGamePlayerOwner` of the living main player; up: block after a hold); a mouse- or
joystick-bound inventory key is left to the game. `ArenaScoreboardPeek` draws
without a window, cursor vote, input block or controls and reuses `ArenaScoreboardRows` and
`ArenaScoreboardPresentation` with the F9 panel; never give it controls or an input block, and
never let it outlive the per-match HUD object.

Active announcer OGGs live in announcements/, the four effect WAVs in sounds/.
Audio records are consolidated under docs/licenses/audio; see
docs/reviews/AUDIO-LAYOUT-20261018.md. Keep source-only reserve audio out of payloads.

The spectator crowd of ArenaMapPort maps (`Audio/ArenaCrowdReactions`, binder
`Compat/ArenaMapPortSpectatorCrowd`, key `Audio/CrowdReactions` frozen at arm) is presentation
only; read docs/reviews/CROWD-REACTIONS-20261102.md before changing it. It adds no Harmony patch
and lives on `ArenaMatch`/`ArenaEvents` subscriptions inside an attached Arena match. Its handlers
buffer only and never throw into the match or block; they log at most once per match (the
NoContest release at Debug, the first handler failure as a Warning), and every reaction push
happens from the gated tick. A failed reaction never changes match state. No push outside an
armed Arena match (this exact current match, `Status ==
Started`, the exact active raid, no pending start abort) or in a plain raid on a ported map. No
claim may outlive an exit path (failed start, raid end, new-raid recovery, both early returns of
`ArenaMatchService.Tick`, plugin shutdown, a failed gate, NoContest, a refused `React`). Never
subscribe to the process-global `SpawnGates.GatesOpened`, never send every frame (one flush per
frame, at most one push per kind, excitement only on change at about 1 Hz), never make the
`SetExcitement` binding require the reaction members or assume that `ContractVersion` changes
when members are appended, and do not depend on `ArenaEvents.OnLocalDamageDealt` or on Fika.
Kinds sent: Approve 1, Disapprove 2, Start 4, Finish 5 and Lull 6; TakeCover 3 is MapPort's own
sensor, PurifiedTeam 7 is reserved. The Lull (decision D14) is the one reaction decided from
time: 60 s of live combat (the murmur's snapshot reading) without a kill, at most one per 90 s
(counted from the last Lull that went out), only while the claimed mask holds bit 6 (MapPort
sets it only once it ships the Lull bank after phase A); a Start or Finish in its flush window
drops it, and a kill or a local death there withdraws it. Pins: `tools/CrowdReactionChecks`.

### From the handoff (snapshot of 2026-10-29)

Working notes of 2026-10-29 that carry a still-valid constraint: verbatim excerpts, or restatements where marked Quoted:/Rule:/Durable form:. Line numbers refer to `handoff-archive/HANDOFF-2026-10-29.md`.

Quoted: "Removed repeated character/version notices, objective/protection rows and inline mutator explanations; one private-session return action remains." and "New configurations start with TDM; saved choices remain." Rule: the user asked for no large blocks of explanatory text in the menus (docs/reports/2026-10/UX-SIMPLIFICATION-20261029.md:3-4). Version notes belong in the tooltip, and the private-session strip holds the only return action. New configurations default to TDM: Settings.cs:1175-1178 binds GameMode with the default ArenaModeCatalog.TeamDeathmatchId. Read docs/reports/2026-10/UX-SIMPLIFICATION-20261029.md before changing menu copy.

*(handoff lines 64-73)*

Quoted: "human faction labels now use the exact body's frozen TDM/Domination affiliation, with native fallback. No saved side or scoring changes." RoleClassifier.Label (ArenaMode.Client/Combat/RoleClassifier.cs:66) reads TeamDeathmatchRelations.Faction(player) and falls back to the native human side. The profile faction, role buckets and scoring stay unchanged. Offline checks: tools/Verify-KillfeedFaction.ps1 -Stage Labels and -Stage Regression. See docs/reports/2026-10/KILLFEED-ROUND-FACTION-20261027.md.

*(handoff lines 146-148)*

Quoted: "A compact session strip keeps return available in menus; trader/inventory detours preserve the validated Arena setup. Restart confirmation counts down." With line 176, "the session strip now appears only in private Arena sessions". Rule: show the session strip only in a private Arena session. After a trader or inventory detour, continue the same Arena setup after a fresh admission check. Restart confirmation shows the remaining seconds. Details are in docs/reports/2026-10/COMPACT-UX-20261026.md (German); only GPU-COMPACT-UX-20261026.md and UX-FOLLOWUP-20261027.md link to it, and docs/README.md does not.

*(handoff lines 190-193)*

Quoted: "map display shares one eligibility snapshot per refresh while final admission remains fresh." Rule: ArenaMapAvailabilitySnapshot (ArenaMode.Client/Session/ArenaMapAvailabilitySnapshot.cs) shares one eligibility result per display refresh. Native clicks, the claim and the final roster freeze must still use fresh admission (docs/reports/2026-10/MAP-MENU-PERFORMANCE-20261026.md:16-23).

*(handoff lines 212-213)*

- **F12 settings menu schema (2026-10-18, merged `1014a29`, GPU2 checked):** all 224
  ArenaMode entries get readable names and a deliberate order (General, Match and the
  modes first, Performance last), Diagnostics and both Development sections are
  IsAdvanced (shown with F12 Advanced settings or a search), seven internal markers
  plus the map-screen participant totals are hidden. Display only: .cfg sections,
  keys, values unchanged. The bind calls stay plain `config.Bind` (harnesses compile
  them against stubs); `ArenaConfigMenu.Apply(Config)` at the end of
  InitializeRuntime adds the tags and orders the sections. New settings need a row in
  `ArenaConfigMenuLayout` (tests enforce it). UiBridge now refuses to set the hidden
  entries. Read [CONFIG-MENU-20261018.md](reviews/CONFIG-MENU-20261018.md).

*(handoff lines 890-899)*

`200b1b2` adds text-only COLLATERAL for two admitted enemy deaths from the same
physical bullet and its native penetration continuation, settled only after
mode admission. No damage, ammunition, score, asset or profile changes.

*(handoff lines 1012-1014)*

HUD/killfeed artwork is original and static per category. User decisions of 2026-10-16: static categories instead of native per-weapon icons, and generated render-style weapon pictures instead of geometric guns. Never reintroduce extracted game PNGs. The game-icons.net motifs are CC BY 3.0 (Lorc/Skoll) and credited separately from the MIT images. Their raw SVGs, per-asset provenance and licence text travel through the reproducible packer and the client notices. Read docs/reviews/UI-ARTWORK-REPLACEMENT-20261016.md, docs/reviews/WEAPON-RENDERS-20261016.md (weapon field 64x32; feed 540 / rail 80 keep 436 text pixels) and docs/reviews/UI-GAME-ICONS-20261016.md.

*(handoff lines 1277-1293)*

Redundant Play Area summaries/availability and Customs blue-boundary caption removed; attribution remains.

*(handoff lines 1498-1499)*

- **HUD/utility mutators + helmet (2026-10-11, deployed/native checked):**
  `5eaeda3` adds the attributed Skoll Stahlhelm, `a51b34d` adds AUTO QUICKSLOTS,
  INSTANT SEARCH and scoped ARENA HUD. The latter controls combat feedback and
  HP/weapon panels through HUD/CombatElements, default on; individual preferences,
  score/notices/team markers/audio stay independent. Existing global HUD/Enabled
  still overrides it. See `HUD-MUTATORS-REVIEW.md` for boundaries.

*(handoff lines 1571-1576)*

Colored tracers are team-only, like laser colors. `Settings.TracerEntry(mode, protectedTeam)` returns `Effects/TeamTracers` only for TDM, Domination and protected Tactical/Zombie squads. Every other mode gets null: the native renderer runs and the mutator row shows N/A. The removed keys `Deathmatch/ColoredTracers`, `Gun Game/ColoredTracers`, `Tactical/SoloColoredTracers` and `Zombie/ColoredTracers` stay as harmless orphans in saved cfgs; do not add a migration step. Killfeed name colors keep `ArenaTracerTeams.Classify` in every mode. Pins: tools/TdmMatchMenuChecks and tools/ArenaTracerChecks. Details: docs/reviews/MUTATORS-MENU-REVIEW.md (Nachtrag 2026-10-08).

*(handoff lines 1951-1960)*

`Modes.ArenaEquipmentChoice` is the pure per-mode availability table for the left-panel EQUIPMENT row (`Session/ArenaEquipmentChoiceView`/`ArenaEquipmentChoiceState`). It offers Arena kits / Own gear for solo Zombies and for protected Tactical only. Writes go only through `Settings.TrySelectEquipmentChoice`, which refuses while `ArenaSession.IsActive` or `ArmedForNextRaid` and writes `Settings.ZombieOwnGear` or `TacticalTeamOwnGear`. The raid reads only the values frozen at arm (`ArenaSession.ZombieOwnGearSelection` / `TacticalTeamOwnGearSelection`). `ArenaMutator.OwnGear` no longer exists.

*(handoff lines 2221-2231)*

Own deaths without a foreign attacker (accidents, self-kills) and GAVE UP get killfeed lines only (`ArenaUncreditedDeathPatch`, 8fd3e03). They create no ArenaKill, kill points, assists or announcer event, invent no attacker, distance or headshot, and self-grenades keep their existing announcer path. Read docs/reviews/UNCREDITED-DEATH-REVIEW.md (the contract; the same patch also hosts the Barbed display) and GPU-HITAREAS-SELF-DEATHS-RESULTS-20261001.md before changing it.

*(handoff lines 2444-2447)*

Per-mode Killcam (b6fb823): the map-menu `KILLCAM: ON/OFF` is per mode (ON = 3 s, F12 0-10 s), shows N/A without the optional Killcam mod, and is frozen at arm. Respawn waits for lead-in, playback and the actual camera return, with no extra fixed pause. Tactical/PFT keeps its round rules, and long replays must not use up the protected respawn window early. Dead bodies lose only their visual health effects. Read docs/reviews/KILLCAM-MODES-REVIEW.md and docs/reports/2026-10/KILLCAM-RENAME-20261018.md: KillcamReplay became Killcam (`com.maschine.Killcam`), and the old GUID is explicitly incompatible.

*(handoff lines 2529-2535)*

Kill skull: confirmed, scoring own kills show the Arena skull in addition to the hit cross (0.15 s hold, 0.65 s fade), with its own lifetime. It is governed by the existing Hitmarker switch, so add no new setting. The killfeed must not generate `HS` text, while the separate headshot icon stays. The user accepted the display, and its position is +42 HUD units ABOVE the crosshair (HitmarkerElement.cs:67, HUD-BLEED-POLISH-REVIEW.md:6). docs/reviews/KILL-SKULL-REVIEW.md still says 42 below and is outdated on position only.

*(handoff lines 2638-2642)*

Score panel: the rival comparison sits in a shared bottom row that reserves the measured number width, and only the name may be shortened. `ScorePanel.PanelHeight` stays 84 and the offsets stay unchanged. Without a comparison (TDM) two status lines are allowed. See UNIFIED-UI-REVIEW.md.

*(handoff lines 2773-2776)*

The HUD's loose-round counts stay unchanged, including their separate backpack slot: no Arena patch makes backpack cartridges loadable, so never merge them into usable reserves.

*(handoff lines 2829-2830)*

HUD, scoreboard, Tactical shop, spectator and Arena menu surfaces share one subtle Arena style with no extra controls and unchanged offsets and game rules. Read UNIFIED-UI-REVIEW.md before restyling. Open visual cases (PFT spectator, area map, DLSS) are listed in GPU-UNIFIED-UI-RESULTS-2026-09-22.md.

*(handoff lines 2922-2928)*

A preceding round's
  Draw does not consume the following terminal Draw; existing result priority
  remains intact. See `ANNOUNCER-PREPARE-REVIEW.md` for evidence and limits.

*(handoff lines 3088-3091)*

The existing 1.468 s
PREPARE TO FIGHT clip is complete. Normal preparation-to-countdown/live
progress used to stop it early; direct RoundStart could replace it as well.
Preserve its spoken duration and defer only the start voice, never gameplay.
Real cancellation/epoch/result/mute/exit still discard pending speech. See
`ANNOUNCER-PREPARE-REVIEW.md` and `tools/AnnouncerChecks/Verify-Preparation.ps1`.

*(handoff lines 3125-3130)*

Clicking the Arena figure now changes the existing native Arena toggle, retaining all eligibility checks. The layout owns/removes its separate input target before preview transfer; later menu screens remain input-transparent.

*(handoff lines 3246-3249)*

Direct lethal bullets use the exact local body's firing snapshot, not ADS/grounded/held-weapon state at death. Pooled projectile, parent/fragment, accepted native hit and round/body identities are checked. [...] First Blood/Payback/Shutdown/multi-kills precede these awards; sprees and Headshot follow. No score, gameplay, asset bytes or profile changes. Read `SHOT-AWARDS-REVIEW.md` for boundaries and user-run tests.

*(handoff lines 3416-3425)*

Multi-kills now precede sprees in selection and queue; First Blood/Payback/Shutdown remain higher. Original win events/recordings and all score/profile/respawn rules are retained. See `ANNOUNCER-EXPANSION-REVIEW.md` for triggers, priority and the legacy shared-GUISounds cancellation limitation.

*(handoff lines 3447-3451)*

Branch `fix/arena-panel-close-input`, source/build commit
`f64df5be499d06d29cc4e6b717dd52ea0931725a`, clean tree at build. F9 and Tactical
buy-panel close now keep command/axis filtering until one fresh actual input
batch with released mouse buttons finishes delivery. Existing owner votes,
Escape/release whitelist, exact Arena game/world, and normal-raid behavior
are preserved; stale/reentrant/smooth dispatch cannot release a newer close.
Input-manager replacement and failed patch cleanup cannot strand the latch.

*(handoff lines 3978-3984)*

- **Killfeed team colors and extended r6 (2026-09-11):** source `41bb6e3`
  replaces role/local-death name colors with the existing match-owned tracer
  registry for both attacker and victim. Solo: local friendly, other verified
  actors opposing; TDM: admitted relative teams; PFT: exact frozen rosters and
  retained same-side historical IDs. Missing/invalid/unbound teams stay gray.
  The synchronous HUD event formats each row once, before mode.OnKill; no body
  lookup, scoring-field writes, roster changes or normal-raid hooks were added.
  FriendlyTracerColor/OpponentTracerColor also tint the names, normalized from
  the shared HDR palette like friendly markers. Effects switches do not gate
  the name colors. Settings changes apply to new entries, not existing rows.
  Default names are cyan/red; roles, distance, layout and lifetime are unchanged.

*(handoff lines 4322-4332)*

Distance was still deliberately formatted only for local kills, explaining
its absence from the observed bot-kill entries. Now all kill directions show
measured meters, placed before weapon details. Capture keeps the original
aggressor/victim body positions and existing Distance value; a separate
DistanceKnown flag distinguishes a successful finite measurement from missing
positions. Unknown/invalid distance displays `? m`, a real zero displays `0 m`.
This is separation at death, not shot-origin distance or projectile path length.
No scoring, bot or profile changes; wrapping/layout code is unchanged from
the r5 build, whose actual wrapping cases remain untested.

*(handoff lines 4410-4418)*

WAVs reload at the next
  Arena match; no game/server restart needed for this asset-only replacement.
  GPU mirror and in-game audibility remain unverified here.

*(handoff lines 4482-4484)*

New source hides Arena canvas drawing on non-BattleUI screens, without
deactivating its GameObject or stopping ticks. Panels also hide on the early
native inventory flag and refresh immediately on return. Existing positions
remain unchanged. Header target independently resolved from installed level46:
`_mainDescription` contains Image/Icon/State, numeric Clock is a sibling;
runtime hierarchy guards preserve that separation. Only header activeSelf is
borrowed/restored, existing successful list masking retained.

*(handoff lines 4548-4554)*

Three new icons with their own hash record `ARENA-ICONS.json`;
  `SOURCE-IMPORT.json` untouched, never rerun the imported generators.

*(handoff lines 4658-4659)*

Weight warnings use the body's actual Arena limits.
  Armored rigs already count through `TacticalVest`; the old "Armor-Vest-Grids"
  note referred only to the separate `ArmorVest` slot. Clarified and covered
  by the production-counter regression. Parent verification: 180 assertions,
  seven controlled source faults; real client build/resource checks pass.
  See RAID-INFO-PANELS-REVIEW.md and tools/RaidInfoPanelChecks.

*(handoff lines 4682-4688)*

Three adapted Kenney CC0 hit/headshot/kill WAVs, unchanged original pack
notices and source/hash/conversion manifest are bundled. F12 Audio:
`HitmarkerSounds=true`, `HitmarkerVolume=0.5`, independent of Interface,
announcer and visual HUD. Exact local hostile direct damage only, 40 ms
coalescing, kill priority, no queued stale audio or paused tails. Shared Master
adapter has separate announcer/impact instances; existing announcer tests
and fault controls pass. See HITMARKER-AUDIO-REVIEW.md and
tools/HitmarkerAudioChecks for tests and pending user listening/GPU checks.

*(handoff lines 4709-4716)*

PFT squad management. Model, camera, lights, render texture and bundle retention have their own owner; no profile inventory or gameplay kit is used or changed. Preview setup/load failure preserves the working Arena choice. See `CHARACTER-PREVIEW-REVIEW.md` for native contracts, independent review, production and fixture build identities, logs and exact GPU coverage.

*(handoff lines 5720-5725)*

- Raid HUD, scoreboard, Tactical buy panel and spectator UI depend on live match
  state. For their redesign, prefer a development-only preview using the same
  rendering code with fixed sample data (long names, empty/full lists, different
  scores), without spawning bots or simulating a real match. This preview is a
  proposed implementation, not an existing tool or completed feature. Keep it
  opt-in and isolated from profiles, normal raids and production gameplay gates.
  Do not fake `ArenaSession.IsActive` or call match-start hooks just to show a
  panel. Preview buy/ready controls must not purchase, start or end anything;
  preview teardown must not stop a real spectator service. The existing
  `ArenaPreviewDiagnostics` is unrelated item-preview diagnostics, not this UI
  preview facility.

*(handoff lines 5820-5830)*

## Performance and corpse cleanup

**Performance: the respawn pipeline, corpse release and pool prewarm** (the old `AGENTS.md` State bullet; its measurements are dated, the rules inside it stand):

- **Performance** is now measured, not guessed: see docs/reports/2026-09/LONG-RAID-REVIEW-2026-09-16.md,
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
  objects, 303 ms to count them). Read docs/reviews/CORPSE-CLEANUP-REVIEW.md, section
  "Releasing a trimmed corpse", before touching corpse lifetime: the step order
  is load-bearing, PitFireTeam bodies are never released, and a body is
  never destroyed while DynamicMaps still holds its marker or while anybody is
  still owed a death by it. Measured on Labyrinth against a same-evening
  control raid: the pipeline's growth per raid minute fell from 20.6 to 13.2 ms
  and the live player-object count from 415 at 23 min to about 63 at 24 min.
  What was left there was dominated by the player's OWN corpses, which were
  exempt by design at the time. Since 2026-09-18 ("Own corpses release too",
  docs/reviews/CORPSE-CLEANUP-REVIEW.md) only the NEWEST own body keeps that exemption: an
  older one releases like any other corpse, once nothing still needs it —
  including a refinement so the live player's own current combat cannot hold
  an older own body open forever just because it shares that body's profile
  id (`CorpseDamageSources.StillNamedInRegistry`, keyed on whether
  `GameWorld`'s own ever-existed registry still resolves to that exact body).
  The own-corpse-release path itself has NOT had its own GPU raid — see
  docs/reviews/CORPSE-CLEANUP-REVIEW.md's "What this does not claim". Do not read the GPU
  acceptance below as covering it; it predates it.
  Zombie mode additionally sweeps at a 1 s cadence (`ZombieCadenceSeconds`)
  instead of 5 s and drains releases between sweeps at up to two per frame
  (`ZombieMaxReleasesPerFrame`/`DrainBetweenSweeps`) rather than waiting for
  the next sweep's eight-per-sweep cap (2026-10-07; see
  docs/reviews/ZOMBIE-MODE-REVIEW.md, "v3"); every other mode's cadence, release cap and
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
  `_allPlayersEverExisted` or its bridge twin; read docs/reviews/CORPSE-CLEANUP-REVIEW.md,
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
  that never crosses it; see `docs/reviews/GC-THRESHOLD-REVIEW.md` and
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
  See `docs/reviews/POOL-PREWARM-REVIEW.md`.

### From the handoff (snapshot of 2026-10-29)

Working notes of 2026-10-29 that carry a still-valid constraint: verbatim excerpts, or restatements where marked Quoted:/Rule:/Durable form:. Line numbers refer to `handoff-archive/HANDOFF-2026-10-29.md`.

- **Bot-profile batches under a per-frame budget (2026-10-17, merged `4c5ceb9`):**
  Performance `ProfileBuildBudgetMs` (default 3, 0 = native). In the exact live
  Arena raid a low-priority prefix replaces `ClientBackendSession.LoadBots` with
  the same request followed by one `Profile(descriptor)` per frame (more only
  while the running average fits the budget; one guaranteed build per open batch
  per frame; honours `__runOriginal`). GPU2 test raid: the ~90 s batch is 240
  profiles / 687 ms of construction, now spread over 240 frames with a 5.2 ms
  longest share instead of one 625 ms frame; horde refilled to 32, no warnings.
  Spawns arrive about 3.4 s later at ~100 fps. Read
  [docs/reviews/PROFILE-BUILD-BUDGET-20261017.md](reviews/PROFILE-BUILD-BUDGET-20261017.md).

*(handoff lines 1086-1095)*

- **Zombie body-culling and bot hot-path switches (2026-10-17, merged `b8695b4`):**
  Four F12 switches from docs/reports/2026-10/ZOMBIE-PERFORMANCE-IDEAS-20261017.md. Performance
  `TrimDeadBrainList` and `SkipBotFovCoroutine` (default on, any Arena raid,
  behavior-preserving: empty AICoreController's dead-brain queue after applying
  it; run a third-person bot's FOV coroutine statement directly while its
  ribcage scale is one). Zombie `CullOffscreenAnimators` and
  `HideOffscreenShadows` (default OFF): measured on GPU2 with 50 infected held
  constant (watchdog pinned to 0, godmode, 90 s A/B phases, relocation every
  25 s) and found no gain inside the run's own drift (base 11.7-13.6 ms,
  switch phases 12.6-13.3 ms; ArenaMode's share +0.1-0.3 ms). They stay off as
  documented measurement switches; an outdoor low-sun map was not tested.

*(handoff lines 1102-1112)*

- **Zombie voice budget and PFT fast path (2026-10-09, latest):** `ae6031a`
  skips only PFT's redundant hostile-layer prefix for confirmed native infected;
  `4220c05` handles native duplicate wait-layer entries without changing their
  contents. Standby wake/custom brains/followers retain PFT. `66ab250` limits
  repetitive zombie playback to six voices/four distant voices; nearby attack
  and critical phrases, native/SAIN signals and other event subscribers pass.
  Default-on immediate comparison switches: Zombie/PftFastPathEnabled and
  Zombie/VoiceBudgetEnabled.

*(handoff lines 1709-1716)*

SAIN global pairs deliberately unchanged: required hearing/companion consumers
and lifetime prevent a verified safe Arena-owned skip. See
`GPU-ZOMBIE-VOICE-PFT-RESULTS-20261009.md`, `ZOMBIE-PFT-PERFORMANCE-REVIEW.md`,
`ZOMBIE-VOICE-BUDGET-REVIEW.md` and `ZOMBIE-SAIN-GLOBAL-REVIEW.md`.

*(handoff lines 1734-1738)*

Temporary probe `f860fc6` measured native retreat at about 0.0036 ms/frame
with 56 mixed infected, including close shooting checks: no production
retreat optimization justified; all native planning and population limits
stay unchanged.

*(handoff lines 1753-1756)*

Zombie performance trims are bounded. `ZombieKitWarmupPolicy` (bc21205) skips only the unused optional kit-catalogue prewarm; the frozen opening kit and the required refill loads stay. `ZombiePerformancePatches` (415c5aa) skips delayed footsteps and peaceful social searches only for confirmed friendly infected. Native combat, navigation, global pair registration and all population limits stay unchanged, and the measured savings (first minute: 461 delayed steps and 3097 social searches avoided) support no causal FPS percentage and no higher supported cap. See docs/reviews/ZOMBIE-PERFORMANCE-REVIEW.md, docs/reviews/ZOMBIE-KIT-WARMUP-REVIEW.md and docs/reports/2026-10/GPU-ZOMBIE-PERFORMANCE-RESULTS-20261009.md.

*(handoff lines 1762-1765)*

## Logging and diagnostics

### From the handoff (snapshot of 2026-10-29)

Working notes of 2026-10-29 that carry a still-valid constraint: verbatim excerpts, or restatements where marked Quoted:/Rule:/Durable form:. Line numbers refer to `handoff-archive/HANDOFF-2026-10-29.md`.

Native model errors remain unresolved;
  context includes both inactive pooled objects and active AI views. Do not
  infer one root cause or silence them.

*(handoff lines 928-930)*

Native
  startup/model root causes remain open. Do not mute live goal-handoff refusals
  or protection alarms to obtain a clean log.

*(handoff lines 960-963)*

- **Logging cleanup (2026-10-15):** Implementation and regression fixtures in
  `6dc2e59`; no gameplay rules changed. Info now carries player-relevant events,
  ordinary technical details use Debug, and detailed probes require the new
  default-OFF F12 `Diagnostics/DeveloperMode`. Its labelled `[dev]` Info can be
  toggled live without changing BepInEx globally. Damage-exception alarms remain
  independent and on; all original Error sites remain visible. Failed ladder
  draws/attachments/ammo fallback now have a separate Warning sink. Expected
  protected stops after an acknowledged `StandDown` use Debug; unexpected stops
  during respawn still warn. See [LOGGING.md](LOGGING.md) and its audit links.

*(handoff lines 1416-1424)*

New read-only `Arena mantle motion #...` diagnostics capture accepted geometry,
requested versus actual movement, capsule/rise and exit/0.75s post-exit state.
Twelve attempts per world, eight-second observation timeout; no position,
collider, gravity or native-state correction. Future logs can distinguish a
blocked displacement from an over-distant ledge or post-animation drop. The
separate optic-icon and SAIN unload NREs do not establish a mantle connection.

*(handoff lines 4835-4841)*

## Build, deploy and release

Core admission: both clients fail closed before runtime initialization when the loaded
Core assembly is missing, unknown or older than RedeployCoreBuiltAgainst (1.0.20 in the
source and in the 0.5.0 package; stamped from the sibling repository's CoreVersion.props at build time). Keep Plugin.Awake free of Core types and InitializeRuntime NoInlining. Arena
Start/Update/OnDestroy must stay guarded, including cleanup of rejected plugins.

### From the handoff (snapshot of 2026-10-29)

Working notes of 2026-10-29 that carry a still-valid constraint: verbatim excerpts, or restatements where marked Quoted:/Rule:/Durable form:. Line numbers refer to `handoff-archive/HANDOFF-2026-10-29.md`.

Quoted: "Native sweep evidence remains tied to its earlier loaded binaries. Subsequent documentation commits record publication without moving the verified source tags or changing uploaded packages." Rule: a release tag stays on the verified build source. A later documentation commit that records publication must not move the tag or change the uploaded packages. Example: v0.4.1 is 2a4438c9, and the only later commit is 287af0e1 'Record verified ArenaMode 0.4.1 publication'.

*(handoff lines 40-41)*

Quoted: "the session helper has three unresolved labels, explicitly disclosed rather than described as confirmed false positives." Rule: release notes disclose the VirusTotal labels on the unsigned session helper as unresolved and never call them confirmed false positives. RELEASE-0.4.0.md:65, RELEASE-0.4.1.md:82 and RELEASE-VERIFICATION-20261025.md:116 all follow this wording.

*(handoff lines 277-278)*

Publish the final verified Core source
  revision with the release and rebuild through the isolated package tool.
  Version stamps, release ZIP/scans and final-package acceptance remain separate.

*(handoff lines 302-304)*

Release version identities to bump together: Shared.props <Version>, `Plugin.PluginVersion` (ArenaMode.Client/Plugin.cs:29), ArenaMode.Server/ArenaMode.Server.csproj <Version> (L18), ArenaMode.Server/ModMetadata.cs Version (L15), ArenaMode.Session/ArenaMode.Session.csproj <Version> (L38), and the player README headline (README.md:3, plus its release links L7-8). `RedeployCoreBuiltAgainst` is stamped from ../Redeploy/Redeploy.Core/CoreVersion.props at build time. All currently read 0.5.0.

*(handoff lines 546-549)*

The packaging pipeline does not deploy; installed gameplay binaries remain the
previously tested build unless a separate deployment is performed. The GitHub
release records the exact source, package hash and per-DLL VirusTotal reports.

*(handoff lines 561-564)*

Final staged build used implementation `c8acf084b3eaf4e98455a70eacaa90ba56349207`,
with zero compilation errors. Only later test/handoff documentation changes
differ from that implementation; do not rebuild merely to make its recorded
source commit equal the documentation HEAD.

*(handoff lines 4213-4216)*

"do not rebuild or overwrite parallel gameplay changes just to repeat already observed cases." Related: 4144-4145 ("this preview correction did not revert or deploy it"), 4086-4087 ("ArenaMode.cfg and server client-settings.json changed concurrently ... were left intact"), 4248-4249 ("They were not reverted.") and 4447-4449 ("Built that clean combined commit to preserve its neutral config description"). Durable form: when concurrent work has delivered DLLs, configs or settings, never revert or redeploy over them. Build from the clean combined commit, and record foreign changes in the receipt or handoff instead of undoing them.

*(handoff lines 4304-4305)*

Hashes identify these built files. A fresh build on another machine may differ;
always record that build's hashes and compare them with its installed copies.

*(handoff lines 5769-5771)*

## Testing and verification

### From the handoff (snapshot of 2026-10-29)

Working notes of 2026-10-29 that carry a still-valid constraint: verbatim excerpts, or restatements where marked Quoted:/Rule:/Durable form:. Line numbers refer to `handoff-archive/HANDOFF-2026-10-29.md`.

"do not reuse r2's server hash or native acceptance ... Keep r1/r2 reports immutable." The same rule recurs at 4179 ("keep r1 files immutable"), 4229 ("No r6 result carries over to these DLLs"), 4350 ("Do not blend earlier r6 hashes/results into this run"), 4431 ("Earlier reports stay unchanged") and 4461 ("All four prior GPU reports remain unchanged"), and 4115-4116 add "Restart both processes and verify GPU-side hashes before testing. This is a local file check, not GPU/runtime acceptance." Durable form: each new build hash gets a new instructions revision, test ID and separate results file. Earlier GPU reports are never edited, and acceptance or hashes never carry across builds. Verify the GPU-side installed hashes after restarting before testing.

*(handoff lines 4149-4154)*

Cosmetics are recorded as failures but do not stop independent tests. Safety,
exceptions, uncertain mode/rules and hash drift still require stopping. Missing
recording tools leave that optional subcase open. Prioritize untouched menu,
downstream-preview and bounded raid cases; jitter video only if it recurs.

*(handoff lines 4180-4184)*

"Each raid's recorded protected-field comparison has 16 equal and zero unequal results, not whole-profile equality." Also 4408-4409: "Profile checks remain limited to the reported fields; no full-profile or normal-PMC proof is implied." Durable form: a GPU run's protected-field comparison proves only the listed fields. Never report it as full-profile restoration or normal-PMC proof.

*(handoff lines 4294-4296)*

## Process and conventions

### From the handoff (snapshot of 2026-10-29)

Working notes of 2026-10-29 that carry a still-valid constraint: verbatim excerpts, or restatements where marked Quoted:/Rule:/Durable form:. Line numbers refer to `handoff-archive/HANDOFF-2026-10-29.md`.

- **Documentation layout (2026-10-11):** All 217 former root Markdown documents
  and 43 historical JSON manifests now live in `docs/`. Root `AGENTS.md` stays
  discoverable and points here; actual relative Markdown links have been rebased.
  New documentation belongs here too. Commands/source paths in older reports
  remain relative to the repository root, and absolute historical build paths
  keep their original meaning. See `README.md` in this directory for navigation.
  This move changes no gameplay source, build configuration, deployment or profile.

*(handoff lines 1552-1559)*
