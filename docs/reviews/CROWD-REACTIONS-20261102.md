# Crowd reactions and murmur intensity (ArenaMode side)

Date: 2026-11-02. Branch `feature/crowd-reactions` from `65007e74`. Phase B of
`D:\ArenaMapPort\docs\PLAN-CROWD-REACTIONS.md` (section 6, "Change list: ArenaMode"); the
ArenaMapPort side (phase A) is a separate project. It was written against the plan before
MapPort implemented its side; phase A is now **implemented in ArenaMapPort `d70acd2`** on its
branch `feature/iceberg` (2026-11-02, not released; planned with MapPort's next release; section
"ArenaMapPort phase A as implemented"). Status: implemented and checked offline, against
stand-ins and against the installed MapPort client's real contract and queue (no Unity); no
native run with ArenaMode, nothing listened to. Three
independent reviews (2026-11-02) found one major issue (a teammate's kill reacted, against D5)
and minor ones (a Tactical deciding kill one tick before its result, a dropped result in a solo
Tactical round without RoundStart, NoContest on the 0.4.0 murmur, binder presence and proof
gaps, documentation); all are fixed in `83d4aee3`..`4d4a9444` and reflected below. A review of
the revision (`484b095a`) found two minor issues (a kill right after a Lull's decision did not
withdraw it; the compile evidence was not scoped to its commit); both are fixed in the commit
after it.

**Source of the values.** The plan as revised in ArenaMapPort commit `08964c1` (2026-11-02,
"revised with the user's subtitles of every crowd clip": automatic German subtitles of all 252
exported crowd files, read by text, not listened to; plan 2.4), plus the two user decisions
recorded in the plan after that commit (D1, D14). The contract (plan 4.3) is unchanged by the
revision. Applied on this side afterwards: a lost lead now disapproves at 0.3 instead of 0.5, and
ArenaMode owns the Lull trigger (kind 6), which stays dormant until a MapPort advertises it.

## What it does

On an ArenaMapPort map with spectators (Equator, Bay5, Air Pit, Bowl) the crowd follows the
Arena match:

- **Murmur intensity.** The looping crowd murmur that ArenaMapPort 0.4.0 already plays gets
  louder under match pressure (final round, match point, the last minute, the last five kills)
  through the published `SpectatorCrowd.SetExcitement(float)`. **Works with ArenaMapPort 0.4.0.**
- **Reactions.** Cheers for the player's own kills, awards and lead, anger at the player's own
  deaths and laments at a lost lead, a call at every round start and round or match result,
  through the four reaction members of plan 4.3. **Needs an ArenaMapPort with phase A**
  (implemented in `d70acd2`, not released yet); against 0.4.0 this part is absent and stays
  dormant (no subscription, no claim, no call). A complaint about a dull match after a minute of
  live combat without a kill (Lull,
  decision D14) needs a later MapPort that also ships the Lull bank; until then it stays dormant
  even against phase A.

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
| Reactions | `int ReactionKinds`, `int ClaimReactions(string)`, `bool React(int, int, float)`, `bool ReleaseReactions(int)` | phase A: ArenaMapPort `d70acd2` (`feature/iceberg`, not released) |

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
  the contract): Approve 1, Disapprove 2, TakeCover 3, Start 4, Finish 5, Lull 6; PurifiedTeam 7
  is reserved and never sent (no ArenaMode event corresponds to it, plan 2.4). TakeCover is
  MapPort's own sensor and never sent from here. Bits are `1 << kind` after a 1..31 range check
  (C# masks shift counts to five bits).
- **Lull is dormant until MapPort ships it.** MapPort builds the Lull bank only after phase A
  and leaves bit 6 out of `ReactionKinds` until then (plan 4.3, "unimplemented kinds"). The
  service sends a Lull, and reads the snapshot for it, only while the claimed mask holds bit 6.
  No contract change.
- **Claim.** Once per match, owner `"AM crowd "` plus 31 hex digits of a fresh Guid: exactly 40
  characters. The plan expected MapPort to deduplicate owners after truncating them at 40, as
  for the score monitors (`ScoreMonitorModel.OwnerMax` is 40); phase A keeps up to 64 printable
  ASCII characters (`CrowdReactionQueue.CleanOwner`), so the 40 reach MapPort unchanged (the
  real-surface stage reads `HolderOwner` back). Claimed only when `ReactionKinds` holds one of
  Approve, Disapprove, Start, Finish or Lull. A Lull-only mask is claimed too and then carries
  the Lull alone: `ReactionKinds` lists what MapPort can play in this raid, so standing its own
  Approve, Disapprove and Start sensors down for an external claim (plan 4.2) silences nothing
  that could have been heard. Phase A has no such sensors; only its developer demo waits while
  another owner holds the claim (`CrowdReactionQueue.ExternalClaimed`).
- **Retry window.** From the first gated tick (the game is `Started` then) for 30 s, at most one
  attempt per second, 31 at most: each attempt reads `ReactionKinds` and calls
  `ClaimReactions` only when the mask holds our kinds. A mask without our kinds after the window
  means no crowd (Iceberg, a vanilla map, no reaction set, reactions off in MapPort): no claim,
  one Debug line. `ClaimReactions` is never called on such a mask. Nothing is retried when the
  plugin, the type or the reaction part is absent or incompatible, or a member threw. Phase A
  publishes the kinds during raid loading (`ArenaSpectators.Create` in its `CreateFromScene`
  postfix joins the clip worker, then `SpectatorAudio.Create` publishes them), before the first
  gated tick, and an audio reset keeps the kinds and the queue, so attempt 1 finds them. The
  window only covers kinds republished shortly after the start (MapPort's developer reaction key
  switched back on).
- **Lost claim.** `React` returning false (stale generation, foreign owner, refused) or throwing
  releases the claim (`ReleaseReactions` is still attempted) and ends reactions for the match; it
  is never claimed again. MapPort's `React` also returns false for a refused request on a live
  claim (a kind without a bit in the current `ReactionKinds`, a strength below 0.05, NaN), so
  ArenaMode cannot tell the two apart and treats both as lost (section "ArenaMapPort phase A as
  implemented").

## ArenaMapPort phase A as implemented (`d70acd2`)

MapPort implemented its side in `d70acd2` ("Crowd reactions, phase A ...", branch
`feature/iceberg`, 2026-11-02; not released, planned with its next release). The contract is
the plan's 4.3 unchanged: the four reaction members are appended to `SpectatorCrowd` and
`ContractVersion` stays 1. The state behind them is `CrowdReactionQueue` in
`ArenaMapPort.Client/CrowdReactionModel.cs`:

- **Kinds.** Phase A implements 1 to 5: `ReactionKinds` is 62 (`CrowdReactionKind.ImplementedMask`)
  once the crowd's reaction set is ready, published by `SpectatorAudio` during raid loading.
  6 Lull and 7 PurifiedTeam are reserved and have no bit, so ArenaMode's Lull stays dormant
  against phase A. `SetKinds` clears bit 0 and empties the request ring on 0. The kinds are
  withdrawn mid-raid only by MapPort's developer key `Spectators/Reactions` (honoured only in
  DeveloperMode) or a disabled `SpectatorAudio` (`PublishKinds`, `OnDisable`), and reset when the
  crowd is removed (`ArenaSpectators.OnDestroy`, `SpectatorAudio.OnDestroy`: `Reset` voids every
  handle and sets the kinds to 0). A withdrawal keeps the holder.
- **Claim.** Refused (0) for a null or blank owner, while the kinds are 0, or while another owner
  holds the reactions, unless that holder is the developer demo (`"developer-demo"`), which yields
  to any other owner and never takes the reactions from one. The same owner gets the same handle
  back. Owners are cleaned to at most 64 printable ASCII characters; handles come from one
  process-wide counter and are never reused.
- **React.** False for a handle that is not the holder's, a kind outside 1..31 or without a bit in
  the current kinds, NaN and a strength below 0.05 (`MinStrength`); a strength above 1 is clamped.
  True means queued (the crowd may still drop it by cooldown, rate or its 2 s age).
- **Release.** True only for the holder; requests already queued still play.
- **MapPort's own host run** (GPU-2, 2026-11-02 21:09 to 21:20, from its commit message): its
  developer demo on Equator, Bay5, Air Pit and Bowl, every kind, 0 warnings, Iceberg
  `ReactionKinds` 0, a plain-raid death and the hideout. Not listened to; ArenaMode was not
  involved.

What that means for ArenaMode, checked against the installed client by the real-surface stage
of `tools/CrowdReactionChecks` (section "Verification"):

- ArenaMode's kind numbers, its 40-character owner and its weakest strength (a lost lead under
  the announcer, 0.15) match MapPort's numbers, owner limit and 0.05 floor.
- **Kinds withdrawn mid-match (accepted behaviour).** When MapPort's developer key withdraws the
  kinds and ArenaMode pushes before they are back, `React` returns false; ArenaMode releases the
  claim (MapPort still counts it as the holder, so the release returns true) and does not claim
  again for the rest of the match, even once the kinds are republished; the next match claims
  anew. Accepted: the path exists only through a developer key (or a disabled component), and
  re-claiming would need ArenaMode to tell a withdrawal from a lost claim, which `React` does not
  say. A withdrawal and republication without a push in between leaves the claim working.
- After the crowd is removed (`Reset`), the next push returns false, the release returns false
  (the handle is void) and nothing is pushed or claimed for the rest of the match.
- A strength below 0.05 would likewise end the claim for the match instead of dropping one
  reaction; nothing ArenaMode sends is below 0.15, and the stage pins every strength against
  MapPort's floor.

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
  (fewer voices); Start, Finish and Lull never are (the Lull answers a minute without a kill, not
  a moment the announcer comments on). It is read once per flush, after the announcer has
  handled that frame's callouts. Against MapPort's bands (plan 2.4, 4.1: up to 1/3 one voice, up
  to 2/3 two, above three; from 0.8 the first Approve voice from the strong-only shouts; below
  0.4 Disapprove draws laments only; below 0.05 `React` refuses, which ends ArenaMode's claim for
  the match): a kill 0.35 becomes 0.175 and a strong
  kill 0.6 becomes 0.3 (two voices to one); an award 0.9 becomes 0.45 (three to two, no
  strong-only first voice); lead taken 0.5 becomes 0.25; lead lost 0.3 becomes 0.15 (one voice
  and laments either way, still three times the 0.05 floor); an own death 0.5 becomes 0.25,
  which falls below the 0.4 band and draws laments only. That last one is intended: a
  second-person heckle of the dead player spoken over the announcer is the worse outcome.
- **LostTheLead 0.3** (was 0.5; plan table 6.3 as revised in `08964c1`). Below 0.4 Disapprove
  draws only the 10 lament clips of the Angry bank; its 8 heckles speak to the dead player in the
  second person and fit an own death only, which keeps 0.5 and the whole bank. TakenTheLead stays
  Approve 0.5.
- **D14, Lull (user, 2026-11-02, "wie empfohlen").** The trigger is ArenaMode's: no kill for 60 s
  of live combat gives Lull 0.4, at most one per 90 s (section "Lull" below). The plan's
  alternatives (the 14 clips into MapPort's random call pool, or no Lull) were not taken.
- **D1 (user, 2026-11-02), MapPort side.** Phase A exports only the 70 "keep" clips of the six
  phase-A banks; the 19 "listen" clips stay out until the user has listened, the 2 clips with a
  slur in their automatic subtitles stay out. Nothing changes here: the kinds and strengths are
  the same, MapPort's sub-pools are smaller.

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
| Cue `TakenTheLead` | Approve 0.5 |
| Cue `LostTheLead` | Disapprove 0.3 (below MapPort's 0.4 band: laments only) |
| Cues `LastManStanding`, `Clutch`, `FinalWeapon`, `LadderComplete`, `Shutdown` | Approve 0.9 |
| Cue `NoContest` | none; silences the match (below) |
| Cues `PrepareToFight`, `Three`, `Two`, `One`, `CancelStatus`, `AdvanceStatus`, `TiedForTheLead`, `OneMinuteRemaining`, `ThirtySecondsRemaining`, `TenSecondsRemaining`, `FiveKillsRemaining`, `OneKillRemaining`, `MatchPoint`, `FinalRound`, `Embarrassing` | none |
| No `OnKill` (and no local death) for 60 s of live combat, at most once per 90 s, only with bit 6 in the claimed mask (D14) | Lull 0.4 |

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
  crowd must not react twice. It drops a Lull too (a complaint about a dull match at a round
  start or result contradicts the moment); a Lull is no round event and drops nothing;
- **Approve and Disapprove wait one tick.** Tactical decides its round only in its mode tick
  (`ArenaMatchService.Tick`, early in `ArenaController.Update`), while `ArenaFireRuntime.Tick`
  (burns, tracked throws) can kill after it and before the crowd's tick in the same Update; that
  kill's `OnKill` and the `RoundWon` of the next frame's mode tick would otherwise be two
  reactions. Team DM and Deathmatch publish their results synchronously in `mode.OnKill`. One
  frame of delay is not audible. The Lull waits the same tick: it is decided after a flush and
  goes out two ticks later unless a Start or Finish arrives first (it is dropped) or a kill or a
  local death does (it is withdrawn, section "Lull");
- send order Finish, Start, Approve, Disapprove, Lull (plan 4.1's priority); at most one push per
  kind per flush; nothing when nothing happened;
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

### Lull (D14)

`ArenaCrowdReactions.TickLull`, constants in `ArenaCrowdReactionPolicy` (`LullStrength` 0.4,
`LullQuietSeconds` 60, `LullGapSeconds` 90), on the service's clock (`Time.unscaledTime`):

- **Live combat** is `ArenaCrowdReactionPolicy.IsLiveCombat`, the murmur's own definition on the
  same snapshot: `Active` (the mode has a snapshot; Zombies and an ended mode have none), `Live`
  (Team DM and Domination `MatchPhase == Live`, Deathmatch the rules' Live phase, Tactical the
  Live phase once the solo squad is combat-ready, Gun Game after the initial kit swap) and not
  `Terminal` (a result exists). Preparation, countdown, intermission and the result hold are not.
- **The quiet clock** runs only while the once-per-second snapshot poll reads live combat: any
  other poll stops it, the first live poll starts it. That also covers a round that goes live
  without a Start (Gun Game raises none, a solo Tactical round can start without its cue) and a
  claim taken late in the retry window.
- **Resets.** Every `OnKill` the service hears resets a running clock: every admitted kill,
  bot-versus-bot and unscored ones included, because the Lull is about a dull match, not about the
  local player. A local death without a kill record (`ArenaEvents.OnPlayerDied`) resets it too.
- A **Start** (`RoundStart` cue, Deathmatch `OnRoundStarted`) restarts it; a **Finish** (any result
  cue, every Deathmatch `OnRoundEnded`) stops it until the next live poll, so no Lull is decided
  between a result and the snapshot leaving live combat. NoContest, every release and every
  teardown clear it.
- When the clock reaches 60 s and the last Lull that went out is 90 s or more ago, the poll
  queues Lull 0.4 (`source=no kill for 60 s`) and restarts the clock, so the next one needs a new
  quiet minute and the 90 s gap. The gap is stamped when the Lull is pushed (`Flush`): a Lull
  dropped by a Start or Finish, or withdrawn, was never heard and delays nothing.
- **Withdrawal.** A decided Lull waits up to two ticks (the one-tick hold). A kill (every
  `OnKill`, as for the resets) or a local death without a kill record in that window withdraws
  it from either frame (`ResetQuiet`): "no kill for 60 s" no longer holds, and it would go out in
  the same flush as that kill's own cheer or groan, or the one after (plan 2.4: a jibe right after
  a cheer contradicts it). A lead or award cue without a kill does not withdraw it: Domination's
  score moves by objective points (`TeamDeathmatchMode.FriendlyTeamScore`), so its lead can
  change without a kill; the premise still holds, D14 counts kills only, and the two meet only
  when the cue falls within the two ticks around the Lull's decision. Both then go out, the Lull
  last.
- Never decided outside live combat, in Zombies (nothing is attached there), or without bit 6 in
  the claimed mask; against such a mask nothing is read for it. Not interlocked with the
  announcer.
- No allocation: a nullable float, a float and a flag; the snapshot read is the murmur's (one
  read per second for both, nothing read while neither needs it).

### NoContest

A technical teardown (failed start, abort), once per match. No reaction; like the announcer,
which latches it until detach, ArenaMode sends nothing new for the rest of the match: the claim
is released at once and the murmur calms to 0. The cue is heard whichever part is bound, so this
holds with ArenaMapPort 0.4.0 too, where only the murmur is bound: a failed Team DM
(`TeamDeathmatchMode.Fail`) raises NoContest but leaves its snapshot Live without a result, so
without the cue the clock would keep raising the level. The reset is written from the cue
handler (the tick stops at once); writing 0 is harmless in any state.

What MapPort already accepted is not withdrawn: its release only clears the holder ("Requests
already accepted still play", `CrowdReactionQueue.Release`), and its reaction voices stop only on
a kinds withdrawal, a raid that is no longer `Started` or a disabled component. A request pushed
in the 2 s before the cue (MapPort's age limit; a RoundStart's Start at 1.0 with three voices, or
a kill's Approve, for example when `TeamDeathmatchMode.Fail` reports NoContest shortly after the
start) and voices already playing still finish; the real-surface stage shows the accepted Start
still waiting after the release. No fix without a MapPort withdraw call; accepted. A raid end is
not affected: once the raid is no longer `Started`, MapPort drops the ring and the planner and
stops the voices.

## Murmur intensity

`ArenaAnnouncementDirector.ReadSnapshot(match.Mode)` is polled once per second (one read feeds
the murmur and the Lull; a failing read stops both for the match with one warning);
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
Warnings: an incompatible or unbindable contract, once per process per reason; and at most once
per match each, an exception from a MapPort member (claim, react, release, excitement), from the
ArenaMode snapshot read that drives the murmur and the Lull, from Attach or the tick, or from an
event handler. LOGGING.md lists every prefix. A Lull has the same reaction line as every other
kind (`kind=Lull, strength=0.4, source=no kill for 60 s`).

## Rules for later changes

- Never add a Harmony patch for this feature; it lives on `ArenaMatch`/`ArenaEvents`
  subscriptions inside an attached Arena match.
- Handlers buffer only and never throw into the match or block. The only lines they write are
  the NoContest release (Debug) and the first handler failure (Warning), each at most once per
  match; the only MapPort call they make is the NoContest release and its excitement reset.
  Every reaction push happens from the gated tick. A failed reaction never changes match state.
- No push outside the gate above, in particular not in a plain raid on a ported map.
- No claim may outlive any exit path in the table above; a new exit path needs a release.
- Never subscribe to the process-global `SpawnGates.GatesOpened`
  ([AREA-RULES.md](../AREA-RULES.md), spawn gates).
- Never send every frame: one flush per frame, at most one push per kind, nothing when nothing
  happened; excitement only on change at about 1 Hz.
- The Lull is the one reaction decided from time rather than from an event: never decided outside
  live combat, never without bit 6 in the claimed mask (and then nothing is read for it), at most one
  per 90 s from the last one sent, and never past a Start, Finish, kill or local death in its flush
  window.
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
  retry window, excitement pins and monotonicity, key off, Zombies, the interlock (lead lost
  0.3 halved to 0.15, own death 0.5 to 0.25), the one-tick hold (a Tactical kill one tick before
  its RoundWon), the epoch-keyed Finish latch (a solo Tactical round without a RoundStart),
  NoContest on the murmur-only 0.4.0 surface, logging, and text pins of the wiring that cannot be
  linked (`ArenaMatchService`, `ArenaController`, `Plugin`, `ArenaSession`, `Settings`,
  `ArenaConfigMenuLayout`). The Lull: one Lull 0.4 after 60 s of live combat without a kill, none
  before; no second one within 90 s, the next at 90 s; a bot-versus-bot kill and a local death
  without a kill record reset the clock; nothing outside live combat (preparation, a result, no
  snapshot), the clock starting at the first live poll; a Start restarts, a Finish stops (and the
  next live poll starts again), NoContest, a release and a failing snapshot read end it; a Start
  or Finish in the Lull's tick or the tick after drops it; a local kill or a local death in the
  Lull's tick and a bot-versus-bot kill in the tick after withdraw it (only the kill's Approve or
  the death's Disapprove goes out), and the next Lull then comes 60 s after the kill, not held
  back by the 90 s gap; a lead cue without a kill keeps it, the Approve sent first; a mask without
  bit 6 reads nothing for it and sends none, a Lull-only mask is claimed and sends only the Lull,
  which the interlock leaves at 0.4; Zombies with bit 6 stays silent.
- **Real surface** (last stage of the same program, `RealMapPort.cs`): the installed ArenaMapPort
  client (`D:\SPT41\BepInEx\plugins\maschine-ArenaMapPort\maschine-ArenaMapPort.Client.dll`,
  ProductVersion `0.4.0+08964c1`, built from MapPort's phase A work before `d70acd2` was
  committed) is loaded read-only and executed, since its contract and `CrowdReactionQueue` and
  `CrowdBedState` are Unity-free. `BindContract` binds both parts of the real `SpectatorCrowd`
  with the exact signatures and no warning. ArenaMode's kind numbers equal MapPort's
  `CrowdReactionKind` constants, the implemented mask is 62 without the Lull and PurifiedTeam
  bits, the 40-character owner passes MapPort's 64-character cleaning unchanged, and every
  strength ArenaMode sends is at or above MapPort's `MinStrength` 0.05. The service then runs
  against the real queue (kinds published as 62 through `SetKinds`, the bed's `Available` set):
  the first gated tick claims with ArenaMode's owner (handle above 0, `ReactionKinds` read 62); an
  own kill's Approve 0.35 and a lost lead under the announcer at 0.15 are queued (`React` true,
  `Accepted` grows, the drained requests carry MapPort's kind numbers); 65 s of live combat send
  no Lull; a Start and a Finish are queued at 1.0; `SetExcitement` reaches `State.Excitement`
  (0.6) and returns to 0 on release; detach releases (`ReleaseReactions` true, `HolderOwner`
  null). Further: the developer demo yields to ArenaMode's claim and cannot take it back;
  another plugin's claim refuses ArenaMode's for the whole retry window; a second match
  releases and claims a new handle; after a real `Reset` (crowd removed) the next push returns
  false, the release returns false and nothing is pushed or claimed again; a no contest right
  after a Start releases (true) and sends nothing more, while the Start MapPort accepted before
  it still waits in the queue (see NoContest); kinds withdrawn and
  republished without a push leave the claim working, and with a push between they end it for
  the match (release true, no re-claim) while the next match claims anew. A missing DLL, member
  or internal model type fails the run naming MapPort's requirement (phase A, `d70acd2` or
  later); `Verify.ps1` proves that with an absent client and checks the DLL's hash before and
  after. Only state is touched; nothing of MapPort's Unity side (`SpectatorAudio`, the planner's
  playback, the bed) runs.
- `Verify.ps1` runs the baseline, that fault control and 51 hand-made mutants, each anchored on
  exactly one LF site and each failing its named assertion, with the no-deploy targets. One of
  them (Start and Finish swapped in the binder's kind constants) passes every stand-in check and
  is rejected only by the real surface.
- `TdmWarmupChecks` and `ZombieKitWarmupChecks` link `ArenaController.cs`; their stand-ins gained
  `ArenaCrowdReactions.Tick`.
- The complete client source (all of `ArenaMode.Client` plus its linked shared files) compiled
  with 0 errors at `11682885` (2026-11-02, recorded in that commit's message) against the
  installed game DLLs and the installed Core 1.0.19 in an isolated scratch project outside the
  repository (no deploy target, no build of Redeploy; the only warnings were four pre-existing
  CS8632 in `ArenaMode.Shared/ClientSettingsModels.cs`). No later commit records that compile.
  After `11682885` the branch changes only the three crowd files under `ArenaMode.Client` and
  nothing in `ArenaMode.Shared` (`git diff --stat 11682885 -- ArenaMode.Client ArenaMode.Shared`);
  those changes are compiled only by `tools/CrowdReactionChecks`, against its stand-ins, not
  against the game DLLs.

## Native run (2026-11-02, GPU-2)

ArenaMode `8d21e362` with the installed ArenaMapPort phase A client, record:
[GPU-CROWD-REACTIONS-20261102.md](../reports/2026-11/GPU-CROWD-REACTIONS-20261102.md). Team DM on
Bowl (two runs and one with a 5 min limit), Deathmatch on Equator over three rounds, Iceberg and
Zombies: claim on the first attempt with kinds 0x3E; Start, Approve (own headshot kill, lead
taken), Disapprove (own death, lead lost), Finish (match result, Deathmatch round ends, one Finish
for the terminal round) all sent once and played by MapPort (one dropped by MapPort's cooldown);
excitement peaks 0.6 (Team DM clock) and 0.85 (Deathmatch final round); release on leaving and
after the result hold; Iceberg no claim, Zombies off; no ArenaMode warning. Two observations for
the listening check, unchanged: the announcer interlock halves nearly every announced event (lead
changes, HEADSHOT, awards), so awards never reach MapPort's 0.8 band for the strong shouts while
the announcer is on; Start at 1.0 exhausts MapPort's thin Start pool (MapPort relaxes its
repetition rules).

## Not claimed

- The native run covers Team DM, Deathmatch, Iceberg and Zombies on GPU-2 only; Gun Game,
  Tactical (solo and PitFireTeam), Domination, the death replay and the team spectator camera
  were not run with the crowd. MapPort's own host run of phase A used its developer demo.
- Nothing listened to: strengths, the interlock factor, the excitement curve and their audibility
  over the announcer are proposals from the plan (the native run proves that they are sent and
  played, not how they sound).
- The reaction part needs an ArenaMapPort release with phase A: `d70acd2` is not released and may
  still change before MapPort's next release (the real-surface stage fails when what it checks
  changes, nothing more). With the
  published 0.4.0 only the murmur intensity can be heard. The Lull needs a MapPort after phase A
  that ships the Lull bank and advertises bit 6; no such MapPort exists, so its 60 s, 90 s and
  0.4 are unheard proposals too.
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
  a truncation at 40, as the score monitors do, could not shorten it. Phase A keeps 64
  characters, so the margin is wider than needed; the 40 stay.
- **Lull, readings of D14.** "No `OnKill` for 60 s while `Live`" is read as 60 s of live combat:
  the clock runs only while the poll reads live combat and starts at the first live poll, not
  only at a Start (rounds without a Start cue, a late claim). A Lull restarts the clock. A local
  death without a kill record resets it as well as every `OnKill` (the bank is LowDeathIntensity,
  and the crowd has just groaned at that death). A failing snapshot read stops the Lull together
  with the murmur. The Lull rides the one-tick hold like Approve and Disapprove, and it is not
  interlocked with the announcer. A kill or a local death in that hold withdraws it, and the 90 s
  gap counts from a Lull that went out, so a dropped or withdrawn one delays nothing (found by
  the review of `484b095a`, where a kill right after the decision still let the Lull follow the
  kill's Approve). A lead or award cue without a kill does not withdraw it (section "Lull").
