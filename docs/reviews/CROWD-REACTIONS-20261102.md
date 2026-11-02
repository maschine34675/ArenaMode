# Crowd reactions and murmur intensity (ArenaMode side)

Date: 2026-11-02. Branch `feature/crowd-reactions` from `65007e74`. Phase B of
`D:\ArenaMapPort\docs\PLAN-CROWD-REACTIONS.md` (section 6, "Change list: ArenaMode"); the
ArenaMapPort side (phase A) is a separate project and is **not implemented** at the time of
writing. Status: implemented and checked offline; no native run, nothing listened to.

## What it does

On an ArenaMapPort map with spectators (Equator, Bay5, Air Pit, Bowl) the crowd follows the
Arena match:

- **Murmur intensity.** The looping crowd murmur that ArenaMapPort 0.4.0 already plays gets
  louder under match pressure (final round, match point, the last minute, the last five kills)
  through the published `SpectatorCrowd.SetExcitement(float)`. **Works with ArenaMapPort 0.4.0.**
- **Reactions.** Cheers for the player's own kills, awards and lead, anger at the player's own
  deaths and a lost lead, a call at every round start and round or match result, through the
  four reaction members of plan 4.3. **Needs the ArenaMapPort version that ships phase A;**
  against 0.4.0 this part is absent and stays dormant (no subscription, no claim, no call).

Presentation only: no Harmony patch, no match, score, profile or bot state, no new asset. The
code lives entirely on `ArenaMatch`/`ArenaEvents` subscriptions inside an attached Arena match.

Files: `ArenaMode.Client/Compat/ArenaMapPortSpectatorCrowd.cs` (binder),
`ArenaMode.Client/Audio/ArenaCrowdReactionPolicy.cs` (pure mapping, coalescing, excitement),
`ArenaMode.Client/Audio/ArenaCrowdReactions.cs` (service), harness `tools/CrowdReactionChecks`.

## Contract with ArenaMapPort

`ArenaMapPort.Client.SpectatorCrowd`, found by reflection from the plugin
`com.maschine.ArenaMapPort` (no `BepInDependency`), exact signatures only, bound once per match
(`ArenaMapPortSpectatorCrowd.Bind`; `BindContract(Type)` is the entry for the harness, like
`ArenaMapPortScoreMonitors.TryClaimContract`).

| Part | Members | Shipped in |
|---|---|---|
| Version gate (both parts) | `int ContractVersion` == 1 | 0.4.0 |
| Excitement | `void SetExcitement(float)`; optional `bool IsAvailable` | 0.4.0 |
| Reactions | `int ReactionKinds`, `int ClaimReactions(string)`, `bool React(int, int, float)`, `bool ReleaseReactions(int)` | phase A (not released) |

- The two parts bind **independently**: the excitement part never requires the reaction members
  (plan 6, "must not"), the reaction part never requires `SetExcitement`.
- **Absent** members (no member of that name at all; presence uses the flags of
  `ArenaMapPortSpawnGates.BindCompletion`: public or not, static or instance, inherited) mean an
  older MapPort: that part is off, no warning, one Debug line per match. An incomplete reaction
  set with no changed member counts as absent.
- A member that **exists with another signature** (another property, parameter or return type, a
  field instead of a property, a contravariant `object` parameter, not public, not static) is an
  incompatibility: that part is off and one Warning is written per process and reason. An extra
  overload next to the exact member is not.
- `ContractVersion` other than 1, or no `int ContractVersion` property: both parts off, one
  Warning per process for each of the two reasons. A throwing member is contained (Warning, part
  off).
- Kinds (plan 4.1, mirrored as internal consts because MapPort's pin test forbids constants on
  the contract): Approve 1, Disapprove 2, TakeCover 3, Start 4, Finish 5; Lull 6 and
  PurifiedTeam 7 are reserved and never sent. Bits are `1 << kind` after a 1..31 range check
  (C# masks shift counts to five bits).
- **Claim.** Once per match, owner `"AM crowd "` plus 31 hex digits of a fresh Guid: exactly 40
  characters, because MapPort deduplicates owners after truncating them at 40 (as for the score
  monitors). Claimed only when `ReactionKinds` holds one of Approve, Disapprove, Start or Finish.
- **Retry window.** From the first gated tick (the game is `Started` then) for 30 s, at most one
  attempt per second, 31 at most: each attempt reads `ReactionKinds` and calls
  `ClaimReactions` only when the mask holds our kinds. A mask without our kinds after the window
  means no crowd (Iceberg, a vanilla map, no reaction set, reactions off in MapPort): no claim,
  one Debug line. `ClaimReactions` is never called on such a mask. Nothing is retried when the
  plugin, the type or the reaction part is absent or incompatible, or a member threw.
- **Lost claim.** `React` returning false (stale generation, foreign owner, refused) or throwing
  releases the claim (`ReleaseReactions` is still attempted) and ends reactions for the match; it
  is never claimed again.

## Decisions as taken

Taken by the orchestrating session from the plan's recommendations (section 8):

- **D5, local-centric events.** Own kills, own deaths, whitelisted awards, lead changes, round and
  match start and result. Ignored: every bot-versus-bot kill and every kill record with
  `ScoreEligible == false` (friendly fire and unattributed fire in a live Team DM or Domination
  match). The local player is the only human in an Arena match (Team DM and Domination build the
  friendly side from their own bots), so a teammate's kill of an opponent is a bot-versus-bot kill
  and stays silent too: the plan's optional row "Teammate kill (Team DM flags), Approve 0.25" was
  not taken. **Zombies: no reactions and no excitement, not even a claim.** Domination capture
  events are deferred (plan 6.5 "Later"); Gun Game tier-ups reach the crowd only through the
  FinalWeapon and LadderComplete cues.
- **D7, no win/loss colouring.** Every Finish has strength 1.0. NoContest, CancelStatus,
  AdvanceStatus, the countdown (PrepareToFight, Three, Two, One) and the clock and kill milestone
  cues give no reaction.
- **D10, one ArenaMode key.** F12 `Audio/CrowdReactions` ("Crowd Reactions (ArenaMapPort Maps)"),
  default on, covers reactions and excitement, frozen at arm into
  `ArenaSession.CrowdReactionsSelection` like the announcer's per-mode switch and cleared at the
  same six sites as `AnnouncerEnabledSelection`. Not a map-screen mutator (plan 6.7).
- **D12, reactions while dead.** Nothing gates on `IsAlive`: the respawn wait, the death replay
  and the team spectator camera still react. ArenaMapPort keeps its own living-player guard for
  its random calls.
- **Announcer interlock (plan 6.6).** While `AnnouncerService.IsPmcVoiceBlocked` is true (the
  announcer is speaking or has speech queued), Approve and Disapprove are sent at half strength
  (fewer voices); Start and Finish never are. It is read once per flush, after the announcer has
  handled that frame's callouts.

## Mapping as implemented

| ArenaMode source | Kind, strength |
|---|---|
| `OnKill`, `KilledByLocal`, `DamageKind` other than below, no headshot | Approve 0.35 |
| `OnKill`, `KilledByLocal`, `DamageKind` `Melee`, `Explosion` or `GrenadeFragment`, or `Headshot` | Approve 0.6 |
| `OnKill`, `LocalIsVictim` (scored) | Disapprove 0.5 |
| `OnKill`, `ScoreEligible == false`, or any bot-versus-bot kill (a teammate's kill of an opponent included) | none |
| `ArenaEvents.OnPlayerDied` without an earlier decision for that body | Disapprove 0.5 |
| Callouts FIRST BLOOD, PAYBACK, COLLATERAL, SHUTDOWN, AIRBORNE, NO SCOPE, DOUBLE/TRIPLE/ULTRA/MONSTER KILL, KILLING SPREE, RAMPAGE, DOMINATING, UNSTOPPABLE, GODLIKE | Approve 0.9 |
| Callout HEADSHOT (single-hit headshot award) | Approve 0.6 (stays on the kill's own path) |
| Any other text (NICE THROW, GENIUS!, WAVE n, ...) | none |
| Cue `RoundStart` (Team DM, Domination, Tactical); Deathmatch `OnRoundStarted` (also round 1) | Start 1.0 |
| Cues `RoundWon`, `RoundLost`, `MatchWon`, `MatchLost`, `Draw`; Deathmatch `OnRoundEnded` of a round that leaves the match running | Finish 1.0 |
| Cue `TakenTheLead` / `LostTheLead` | Approve / Disapprove 0.5 |
| Cues `LastManStanding`, `Clutch`, `FinalWeapon`, `LadderComplete`, `Shutdown` | Approve 0.9 |
| Cue `NoContest` | none; silences the match (below) |
| Cues `PrepareToFight`, `Three`, `Two`, `One`, `CancelStatus`, `AdvanceStatus`, `TiedForTheLead`, `OneMinuteRemaining`, `ThirtySecondsRemaining`, `TenSecondsRemaining`, `FiveKillsRemaining`, `OneKillRemaining`, `MatchPoint`, `FinalRound`, `Embarrassing` | none |

- The `Shutdown` cue is raised nowhere today; it means what the SHUTDOWN callout means (the local
  player ended an opponent's streak) and maps the same. `Embarrassing` is an own-grenade death,
  whose death already disapproves through `ArenaEvents.OnPlayerDied`. A future cue is silent by
  default (the mapping is a whitelist; the harness walks every enum member).
- Callout strings are the exact labels of `ArenaCombatAwards.ReportKill` and `ScoreRules`; the
  award labels are raised only for the local player's own kills.
- `Clutch` is raised by `ArenaMatch.ReportAnnouncerCue` immediately before its `RoundWon` or
  `MatchWon`, so the coalescing below always folds it into that Finish; it stays in the table
  for a future caller that raises it alone.

### One local death, one decision

A death with a kill record is reported twice. EFT's `Player.OnDead` runs `OnBeenKilledByAggressor`
(the kill capture postfix, `ArenaMatch.OnKill`) before the `DiedEvent`'s later `CG_Spawn` handler,
where Redeploy's driver raises `ArenaEvents.OnPlayerDied` (Redeploy `RespawnApi.DeathClaimed`
documents the same order). Both carry the same physical body: `ArenaKill.VictimBodyId` is
`ArenaHitHistory.BodyId` of the dying `Player`, which is `game.LocalPlayer` at `CG_Spawn`. The
service keeps the bodies it has decided (`TakeDeath`): the first notification decides, in either
order and whatever frame the second arrives in. An unscored kill record (friendly or
unattributed fire in a live Team DM or Domination match) decides "no reaction", so such a death
stays silent on both paths. A kill the mode does not admit at all (Team DM, Domination and
Deathmatch outside their live phase or after the result, where `TryBeginScoredEvent` refuses it)
is dropped by `ArenaMatch.ReportKill` before `OnKill`; a local death there reaches the crowd only
through `ArenaEvents.OnPlayerDied` and disapproves like any death without a kill record (plan
6.3). Those modes protect the local player in most of those phases (`ShouldBlockMatchDamage`,
the Deathmatch intermission's respawn or protection), so this is rare. An unknown body (id 0)
cannot be deduplicated; both notifications then land in one tick and the flush keeps one
Disapprove.

### Coalescing (one flush per tick)

Handlers only buffer (`ArenaCrowdReactionPolicy.Frame`, two fixed frames, no allocation); the
service's tick flushes once per frame:

- per kind the strongest entry (a callout and its kill give one Approve at the award strength);
- a Start or a Finish goes out in the tick it arrives in and drops the Approve and Disapprove of
  that tick and of the tick before: the deciding kill's `OnKill` arrives before its result cue
  (`ArenaMatch.ReportKill` raises `OnKill`, then `mode.OnKill` publishes `MatchWon`), and the
  crowd must not react twice;
- **Approve and Disapprove wait one tick.** Tactical decides its round only in its mode tick
  (`ArenaMatchService.Tick`, early in `ArenaController.Update`), while `ArenaFireRuntime.Tick`
  (burns, tracked throws) can kill after it and before the crowd's tick in the same Update; that
  kill's `OnKill` and the `RoundWon` of the next frame's mode tick would otherwise be two
  reactions. Team DM and Deathmatch publish their results synchronously in `mode.OnKill`. One
  frame of delay is not audible;
- send order Finish, Start, Approve, Disapprove; at most one push per kind per flush; nothing
  when nothing happened;
- **one Finish per round**, where a round is the announcement epoch
  (`ArenaAnnouncementSnapshot.Epoch`, the announcer's own round identity), read when the Finish
  arrives; a Start re-arms too. Deathmatch reports a non-terminal draw twice (`OnRoundEnded`,
  then the director's `Draw` cue), both in the intermission's epoch (round + 1), so it reacts
  once. Solo Tactical raises `RoundWon`/`RoundLost` before it advances `RoundNumber` (its epoch)
  and can start a round without a `RoundStart` cue (a squad eliminated while it assembled); that
  round's result is a new epoch and still reacts. Team DM keeps epoch 1 for its one result. A
  mode without a snapshot, or a failing read, counts as one epoch (one Finish until the next
  Start). `RoundsMode.PublishRoundEnd` raises the match result cue, then `OnRoundEnded`, in one
  call: a terminal round end therefore sends nothing of its own (the result already reacted,
  NoContest silenced the match, or a terminal draw reacts on its `Draw` cue); only a round that
  leaves the match running sends its own Finish.

### NoContest

A technical teardown (failed start, abort), once per match. No reaction; like the announcer,
which latches it until detach, the crowd stays silent for the rest of the match: the claim is
released at once and the murmur calms to 0. The cue is heard whichever part is bound, so this
holds with ArenaMapPort 0.4.0 too, where only the murmur is bound: a failed Team DM
(`TeamDeathmatchMode.Fail`) raises NoContest but leaves its snapshot Live without a result, so
without the cue the clock would keep raising the level. The reset is written from the cue
handler (the tick stops at once); writing 0 is harmless in any state.

## Murmur intensity

`ArenaAnnouncementDirector.ReadSnapshot(match.Mode)` is polled once per second;
`SetExcitement` is called only when the level changes, in steps of 0.05, and never while a bound
`IsAvailable` reads false (no bed: a vanilla map, Iceberg, the bed switched off; the level is
process-global and only a crowd removal resets it, which such a map never has). Mapping
(`ArenaCrowdReactionPolicy.ExcitementStep`), monotone in every input:

- 0 when the snapshot is not Active, not Live (preparation, countdown, intermission) or Terminal;
- parts: final round 0.6, else match point 0.4; the clock rises linearly from 0 at 60 s left to
  0.6 at 0 s; the kills rise from 0.12 (0.1 after rounding) at five left to 0.6 at one (or
  zero) left;
- combined as `1 - (1 - phase)(1 - clock)(1 - kills)`, so they stack and never reach above 1
  (MapPort's factor `1 + 0.6 * level` stays within its 1.6 and the baked clip gain).

Pinned values: final round 0.6, match point 0.4, 30 s left 0.3, 0 s left 0.6, three kills left
0.35, final round with 30 s left 0.7, final round with 0 s and one kill left 0.95. The snapshot
does not cover Domination objectives, the Tactical round score or Zombies waves (plan 6.4). The
level is set back to 0 when the crowd presentation ends with a non-zero level sent.

## Lifecycle

| Event | Where | What |
|---|---|---|
| Attach | `ArenaMatchService.cs:150`, with announcer, hitmarker and lighting, before `mode.OnMatchStart` | Key and Zombies gates, bind; `OnAnnouncerCue` whenever a part is bound (NoContest), `OnKill`, `OnCallout`, `OnPlayerDied` and the Deathmatch round events only with a bound reaction part |
| Failed match start | `ArenaMatchService.cs:165` (catch) | Detach |
| Raid end | `ArenaMatchService.cs:199` (`OnRaidEnded`, before the null-match return) | Detach |
| New-raid recovery | `ArenaMatchService.cs:228` (`RecoverAtNewRaidStart`, before its early return) | Detach |
| Failed-start abort pending | `ArenaMatchService.cs:43` (`Tick` early return) | `OnNativePresentationStop` |
| Native Stop owns the raid | `ArenaMatchService.cs:58` (`Tick` early return) | `OnNativePresentationStop` |
| Plugin shutdown | `Plugin.cs:278` | Detach |
| Every tick | `ArenaCrowdReactions.Tick` | Gate; a failed gate detaches for the rest of the match |
| NoContest cue | `ArenaCrowdReactions.OnAnnouncerCue` (either part bound) | Release, calm, silent until detach |
| `React` false or throwing | `ArenaMapPortSpectatorCrowd.ReactionClaim.Push` | Release; reactions end, excitement continues |

- **Tick source:** `ArenaController.Update` (`ArenaController.cs:51`) calls
  `ArenaCrowdReactions.Tick` every frame right after `AnnouncerService.Tick`, whose director emits
  that frame's lead, draw and ladder cues. It runs whether the local player is alive, dead, in a
  death replay or in the spectator camera. `ArenaMatchService.Tick` returning early does not stop
  it; both early returns call `OnNativePresentationStop` (as `TeamDeathmatchMode.Monitors.cs`
  does for the score monitors), and the tick's own gate refuses the same states independently.
- **Push gate** (as `ArenaAnnouncementDirector.Tick`): this exact match is
  `ArenaMatchService.Current`, the game is a `LocalGame` with `Status == Started`,
  `ArenaSession.IsExactActiveRaid(game, world)`, and additionally no pending failed-start abort.
  None of these comes back for the same match, so a failed gate releases the claim, calms the
  murmur and detaches at once instead of waiting for `GameEnd`.
- Teardown is idempotent: a claim is released exactly once, the excitement reset is written only
  when a non-zero level was sent, and every subscription (`ArenaMatch` events,
  `ArenaEvents.OnPlayerDied`, Deathmatch round events) is removed.

## Logging

Debug only, nothing per frame (see [LOGGING.md](../LOGGING.md)): one line per match for attach
(or why the crowd is off) and detach, one per claim, per release and per window outcome without
a claim, and one per sent reaction (`Arena crowd reaction: kind=…, strength=…, source=…`).
Warnings only for an incompatible contract (once per process per reason) and for an exception
from a MapPort member or an event handler.

## Rules for later changes

- Never add a Harmony patch for this feature; it lives on `ArenaMatch`/`ArenaEvents`
  subscriptions inside an attached Arena match.
- Handlers buffer only: no throw into the match, no blocking, no log line. A failed reaction never
  changes match state.
- No push outside the gate above, in particular not in a plain raid on a ported map.
- No claim may outlive any exit path in the table above; a new exit path needs a release.
- Never subscribe to the process-global `SpawnGates.GatesOpened`
  ([AREA-RULES.md](../AREA-RULES.md), spawn gates).
- Never send every frame: one flush per frame, at most one push per kind, nothing when nothing
  happened; excitement only on change at about 1 Hz.
- The excitement binding must never require the reaction members, and nothing may assume that
  `ContractVersion` changes when members are appended.
- Do not depend on `ArenaEvents.OnLocalDamageDealt` (raised only with the hitmarker settings on)
  or on Fika (Arena refuses it).

## Verification

- `tools/CrowdReactionChecks` (production-linked: binder, policy and service compile unchanged
  against stand-ins of the game, BepInEx and the ArenaMode services; MapPort surfaces: phase A,
  0.4.0, absent, wrong parameter, return and convertible signatures, an internal member, a field,
  an extra overload, version 2, no version, throwing): binding outcomes per variant, the mapping
  table per source including every
  `ArenaAnnouncerCue` member, coalescing, the death deduplication in both orders and across
  frames, Deathmatch round ends, gating, every exit path, refused and throwing reactions, the
  retry window, excitement pins and monotonicity, key off, Zombies, the interlock, the one-tick
  hold (a Tactical kill one tick before its RoundWon), the epoch-keyed Finish latch (a solo
  Tactical round without a RoundStart), NoContest on the murmur-only 0.4.0 surface, logging, and
  text pins of the wiring that cannot be linked (`ArenaMatchService`, `ArenaController`, `Plugin`,
  `ArenaSession`, `Settings`, `ArenaConfigMenuLayout`). `Verify.ps1` runs the baseline and 36
  hand-made mutants, each anchored on exactly one LF site and each failing its named assertion,
  with the no-deploy targets.
- `TdmWarmupChecks` and `ZombieKitWarmupChecks` link `ArenaController.cs`; their stand-ins gained
  `ArenaCrowdReactions.Tick`.
- The complete client source (all of `ArenaMode.Client` plus its linked shared files) compiled on
  2026-11-02 with 0 errors against the installed game DLLs and the installed Core 1.0.19 in an
  isolated scratch project outside the repository (no deploy target, no build of Redeploy; the only
  warnings were four pre-existing CS8632 in `ArenaMode.Shared/ClientSettingsModels.cs`).

## Not claimed

- No native run: the binder has never seen a real ArenaMapPort, the service has never run in EFT.
- Nothing listened to: strengths, the interlock factor, the excitement curve and their audibility
  over the announcer are proposals from the plan.
- The reaction part cannot work before ArenaMapPort ships phase A; its contract is the plan's
  proposal (4.3) and may still change there. With the published 0.4.0 only the murmur intensity
  can be heard.
- Where the AudioListener sits in the death replay and in the spectator camera (plan 4.4) is a
  host test item of the MapPort side.

## Deviations from the plan and clarifications

- **Retry on a crowd-less map.** Plan 6.2 says "on Iceberg the outcome is no claim and no
  retries". `ReactionKinds == 0` cannot tell "not yet published" from "never", so the window still
  reads that property at most once per second for 30 s (31 reads at most); `ClaimReactions` is never
  called on such a mask and nothing is retried after the window.
- **Gate.** The tick also refuses a pending failed-start abort, so the release does not depend on
  `ArenaMatchService.Tick`'s early return alone.
- **NoContest silences the match** (release and calm), mirroring the announcer's latch; the plan
  only says "no reaction".
- **One Finish per round** (keyed on the announcement epoch, re-armed by a Start too) in addition
  to the plan's coalescing rule, for the Deathmatch draw that is reported twice.
- **Approve and Disapprove wait one tick**, so a Tactical result published one tick after its
  deciding kill still sends Finish only (the plan's rule "when the deciding kill and its result
  cue arrive together, send Finish only").
- **IsAvailable** gates excitement writes (skipped while false), so a map without a bed is never
  written; the plan leaves the use of `IsAvailable` open.
- The owner is 40 characters (31 hex digits) instead of the plan's `"AM crowd " + Guid` (41), so
  MapPort's truncation cannot shorten it.
