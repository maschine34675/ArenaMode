# Tactical opening rounds (2026-11-19)

Contract for the lighter enemy gear of the first two rounds of **solo Tactical**. It describes what was
**built**, in three stages on the branch `feature/opening-rounds-zombie-waves`: the shared policy, the wire and
the server (T1), the client gate, the reports and the squad hold (T2), and the pool purge, the buy-screen line
and the documents (T3). It corrects [DIFFICULTY-MODEL-20261118.md](DIFFICULTY-MODEL-20261118.md), which decided
the opposite one day earlier. **Nothing here has been played natively yet**; "Native acceptance" lists what a
GPU run has to show, and every claim about the game is a claim about source and offline harnesses until then.

Read [DIFFICULTY-MODEL-20261118.md](DIFFICULTY-MODEL-20261118.md) first (the step, the gear band, the stale pool
of Gun Game) and [SOLO-TACTICAL-SPAWN-REVIEW.md](SOLO-TACTICAL-SPAWN-REVIEW.md) (the squad request and its
dispatch) before changing anything below.

## Decision

The owner's decision of **2026-11-19**: the enemy squad of Tactical rounds 1 and 2 is kitted lighter than the
step's gear level, and the full level starts with round 3. This supersedes the decision of 2026-11-18 that
"Tactical gets no build-up". The reason is the economy, not the clock: in round 1 the player owns a free
starter kit or one kit of at most 250,000 roubles, and the first opponents of the native Tactical raids of
2026-11-19 already wore the top of the tier table (level 61..71 at Maximum). A round that only a top kit can
survive is not a choice.

What does not change: the AI level, the PMC bonus, the squad size and speed, protected PitFireTeam Tactical
(native, profile-relative gear and no APBS permit), every other mode, and ordinary raids.

## The numbers

One function decides every round, in `ArenaMode.Shared/ArenaDifficultyPolicy.cs`:
`TacticalGearLevel(step, startLevel, endLevel, round)`. The server band, the client pool purge and the text
the player reads are all defined through it, so they cannot disagree. `TacticalOpeningDiffers` and
`TacticalOpeningRoundsThatDiffer` are defined through it too (a round differs when its level is not the full
level).

- `full = GearLevel(step, start, end)`. From round 3 on the answer is `full`; a round of 0 or less counts as
  round 1.
- Otherwise the opening centre of the round, clamped into [the map's start level, `full`]. Maximum: round 1 =
  **25**, round 2 = **45**. Reduced is **derived** from them with the share the full level uses
  (`ReducedGearShare` 0.6, rounded away from zero): **15** and **27**. A test pins the derivation, so a tuned
  share cannot leave the openings behind. `botTierStartLevel` is the floor, as in Gun Game.
- An opening stage counts only when it is at least `TacticalOpeningMinGap` = **10** levels (one gear tier) below
  `full`. If `full - centre < 10` the round is full: the table would say "lighter" while the kit on the field is
  the same, and the player must never be told about a difference that is not there.

The band the server writes is `[centre - 5, centre + 5]` (`max(1, ...)` at the bottom), 11 levels, and every
tier of the bot system below the top one is 10 levels wide, so a band always overhangs one tier. Default map
(start 1, end 66):

| Step | Round 1 | Round 2 | Round 3 and later |
| --- | --- | --- | --- |
| Maximum | centre 25, band 20..30 | centre 45, band 40..50 | 66, band 61..71 |
| Reduced (full = 40) | centre 15, band 10..20 | centre 27, band 22..32 | 40, band 35..45 |

Maximum lands on the tier above its centre with one level of the tier below (20 in tier 2 for round 1, 40 in
tier 4 for round 2: the overhang is on the **weaker** side on purpose, the feature's error budget is "too
soft", not "too hard"). Reduced round 2 puts two of its eleven levels (31 and 32) in the next tier up: that is
the price of deriving it from the share instead of keeping a second table (the owner's 0.6 share, decided
2026-11-19; 25 would have avoided it and was not chosen). The Reduced full band 35..45 straddles tiers 4 and 5
and is old behaviour. Custom maps (every case is pinned by `TacticalOpeningPolicyTests`):

| Map numbers | Maximum, rounds 1 / 2 / 3+ | Reduced, rounds 1 / 2 / 3+ |
| --- | --- | --- |
| 1 / 66 (default) | 25 / 45 / 66 | 15 / 27 / 40 |
| 1 / 45 | 25 / 45 / 45 | 15 / 27 / 27 |
| 1 / 30 or 1 / 20 | full in every round (no opening) | full in every round |
| 40 / 66 (start raised) | 40 / 45 / 66 | 40 / 40 / 40 |
| 1 / 100 (clamped to 79) | 25 / 45 / 79 | 15 / 27 / 47 |

### What the stages are justified by

By what the player can afford, not by a claimed equivalence of gear. The money of solo Tactical (`Settings.cs`,
`TacticalMode`): 250,000 at the first buy phase, 35,000 per kill, 150,000 for a won round, 80,000 for a lost
one; the kit is charged at the round start; a survivor keeps it. The 20 stock kits cost 180,000 (drifter) to
669,000 (titan) by the handbook rule (`KitPriceRule`, recomputed from the shipped `kits.json` on 2026-11-19;
the server logs the same range). So:

- Round 1: the free cheapest starter, or one of the five cheapest kits (drifter, vanguard, wraith, reverend,
  minuteman reach 250,000; warlord costs 251,000).
- Round 2: at best 575,000 (no purchase in round 1, five kills and the win: 250k + 5 x 35k + 150k), which
  reaches reaper (555k), the fourth of the seven dearest kits (outlaw, 577k, is just out of reach). The
  realistic path (a 250k kit bought, a won round and, **assuming five kills**: 0 + 150k + 5 x 35k = 325k,
  about 330,000) is the middle of the catalogue (shadow 292k, crusader 338k); with fewer
  kills it is lower (two kills: 220k).
- Round 3: the first realistic round for the 500k+ kits; the top two (658k, 669k) at the earliest at round 3.

So rounds 1 and 2 are what a starter budget and a mid-catalogue budget face, and round 3, where the top kits
become affordable, meets the full level. **That the gear of a lighter stage "meets a 180k to 250k kit on equal
terms" is an assumption that was not measured** (the armour classes of the cheap kits were not compared with the
plate chances of the bot system's tiers); nothing in the player-facing text or the changelog claims it.

## What the problem was, and the two failure shapes

Two things made round 1 hard. The server wrote the full band from the first request, and the client serves a
spawn from its pool of already generated profiles (`BotProfileClient._simpleProfiles`) before it asks the server;
SPT rounds every requested (role, difficulty) pair up to `presetBatch` (15 for a PMC), so the pool made by the
raid-start preload and the prewarm holds about 30 PMC profiles of one single moment. Even a server that follows
the round would feed rounds 2 to 4 from the round 1 pool without a client purge: the staleness of Gun Game
([DIFFICULTY-MODEL-20261118.md](DIFFICULTY-MODEL-20261118.md), "Stale pooled gear in Gun Game") in a second mode.

The server only learns the round from the client, so the design has two failure shapes, both bounded:

1. **The report is lost** (it never reached the server): the server stays at the previous round, and the client,
   which expects the last **acknowledged** round and not `RoundNumber`, stays there with it. Consistent and
   softer than intended; the next round's report (a higher `seq`) repairs it.
2. **The ack is lost** (the server took the report and the answer never came back): the server is one stage
   **ahead** of what the client believes. The squad request goes out at the server's stage, the client's pool
   purge aims at the previous band, the purge budget runs out and one Warning says the band does not match the
   round. The resend of the same `seq` gets a `Duplicate` reply that counts as an ack, so this normally heals
   within seconds; a late ack that raises the acknowledged round moves the centre and starts that centre's
   purge budget fresh. The cost of this shape, in numbers: the batch the miss path returns after the first purge is
   at the **server's** band, outside the client's, so the second demand purges it too (budget 2) and costs one more
   15-profile round trip; the third demand is served and the Warning appears (`TacticalFreshGearChecks`, `Budget()`,
   "the lost-ack shape"). Two extra batches inside the 45 s assembly window at most.

**The invariant that always holds, in both shapes: the band is never above the step's full gear level.** A lost
message can make a round softer or cost a purge; it cannot make anything harder than the step already is.

The raid-start preload and the prewarm run before any report can exist and feed round 1, so the server's
**default without a report is round 1**, not the full band: defaulting to the full band would make the preload the
one source of full-gear opponents in round 1, the thing being removed.

## The wire and the server (T1)

- Route `Protocol.TacticalRoundRoute` = `/arenamode/tactical-round`, request `ArenaTacticalRoundRequest`
  (`armToken`, `round`, `seq`), reply `{ accepted, applies, round, seq, duplicate, reason }`. The session is the
  SPT session id of the request, and the arm token must match the exact live raid. `round` is 1..999 and `seq`
  1..`Protocol.TacticalMaxSeq` (1,000,000: one huge `seq` would make every later report a `Duplicate`).
- `ArenaSessionService.TryReportTacticalRound` stores the round and the seq in the session entry (`TacticalRound`
  = 1 and `TacticalReportSeq` = 0 on a fresh entry). A `seq` that is not above the stored one is a `Duplicate`
  and changes nothing; the round itself is **not** monotonic (a restart reports round 1 with a higher `seq`).
  The stored round and seq of the reply win over the request's. Refused, and changing nothing: no live raid, a
  wrong or empty arm token, not solo Tactical (a protected PitFireTeam raid is refused too), a round or `seq` out
  of range. `applies` is whether the map's `scaleBotTier` is on; an opted-out map is accepted with
  `applies:false`.
- The round and seq reset **where a raid starts** (the transition to InRaid in `ProcessLocalRaidStart`) and
  nowhere else: an aborted native end returns the same entry to InRaid (`AbortNativeEndFinalization`), and that
  raid's reports must survive. Both sites are covered by tests. While the raid is in its native end
  (FinalizingEnd) the refusal reads `Protocol.TacticalRoundRaidEndingRefusal` ("raid is ending"), one constant
  for server and client: it marks a **transient** refusal, because the raid may return to InRaid.
- `ArenaBotTierScalePatch` takes the Tactical round through the sibling accessor
  `GetTacticalRound(BotTierIdentity?)` (1 for a null or stale identity and for a protected raid) and writes
  `TacticalGearLevel` for it; `TryGetBotTierContext` keeps its seven parameters (its one production caller, two
  tests and the BEAQO add-on's reflection depend on them) and gets no overload. `MarkWave` and the APBS permit
  are unchanged and record exactly the range written, so a report arriving mid-generation cannot change a wave
  already in flight. `/status` carries `tacticalRound` and `tacticalSeq` (one read, as a pair).
- **The server is role-blind.** The band is written for any role of the generation request, so a scav squad
  (`Tactical/SquadIsPmc` off) is generated at the stage band too. Only the client is role-aware (below).

## The client gate and the reports (T2)

`TacticalOpeningBandGate` (`ArenaMode.Client/Modes`, pure: clock, sender, arm token, gear numbers and log are
seams), the published state `ArenaTacticalOpening` (`ArenaMode.Client/Bots`), the reply parser
`TacticalRoundReply` and the HTTP sender `ArenaTacticalRoundClient`.

- **Reports.** Round 1 at the match start (the handshake that tells the client whether the server applies the
  feature) and the new round right after `RoundNumber++` in `EndRound`, every round, solo only. The report
  precedes the next `StartRound` by at least the aftermath (6 s, `AftermathSeconds`; READY skips only the buy
  phase), far longer than a loopback request. `seq` is a static, process-wide counter (a second match in the
  same raid or a later raid never reports a `seq` the server holds).
- **The hold.** A squad request waits for the acknowledgement **only in a round whose gear level differs from
  the level of the last acknowledged round** (no acknowledged round counts as round 1): on the default map
  rounds 2 and 3. Round 1 and rounds from 4 on never wait, so a dead route costs no hold in a normal round. The
  hold is capped at 3 s from the moment the request would otherwise go out (`HoldCapSeconds`), measured on
  unscaled time. The hold sits in `DispatchOpeningSquad` between its guard and `RequestSoloSquad(retry: false)`,
  and `TickLive` sends the held request when the gate lets go. **`TickLive` must return before
  `SoloTacticalSpawnPolicy.Evaluate` while a request is held**: once the dispatch gate has released, nothing is
  requested and the deadline is the previous round's, so `Evaluate(0, 0, ...)` answers `Retry` and
  `RequestSoloSquad(retry: true)` would send the request ahead of the ack and again after it. A source assertion
  and mutants pin the order. `SoloTacticalSquadDispatch` itself is unchanged: the band hold is a second hold
  downstream of the body-move cap, so the worst case adds 3 s, never 45 + 45.
- **Failures.** A report is resent with the same `seq` every 2 s (`ResendSeconds`) up to 4 sends
  (`MaxSends`); a call without an answer counts as lost after 8 s (`InFlightLostSeconds`) and a late answer is
  still accepted if its `seq` is current. A `Duplicate` reply counts as an acknowledgement. **Any
  `accepted:false` and any `applies:false` at any report set `Applies = false` for the rest of the raid**,
  release every hold and stop all purging; an opted-out map (`applies:false`) gets no warning. The one exception
  is "raid is ending": not an ack, no `Applies` change, no warning, the report is resent within its budget. A
  failure of the **first** report that survives all sends (an old server without the route) also ends the
  feature with one Warning; a failure after an earlier success keeps the feature at the last acknowledged round.
  A blank arm token ends the feature without a request. `Report`, `Tick` and every query catch every exception
  (logged once), so nothing in `OnMatchStart` or the frame loop can throw because of this feature.
- **Scav squads.** The squad role (`Settings.TacticalSquadIsPmc`) is read live at every call. A scav squad is
  still reported and held for (the server is role-blind), but `CurrentGearLevel` is null: nothing is purged and
  no line is shown. Their pooled batch of 45 can therefore serve many rounds at the round 1 gear (open, below).
- **Old client against a new server** never reports, so the server stays at round 1 for the whole raid (too
  easy); a **new client against an old server** gets an error on the first report and ends the feature (full
  band, as before). Client and server ship together.

## The pool purge (T3)

`ArenaTacticalFreshGearPatch` (`ArenaMode.Client/Bots`) is a **sibling** of `ArenaGunGameFreshGearPatch`, a second
Harmony prefix on `BotProfileClient.CreateProfile(BotCreationData, CancellationToken, bool)` that returns early
on its own mode; Gun Game's file is not touched and `GunGameFreshGearChecks` stays green unchanged. In an
exact active solo Tactical raid (not a protected team) with a PMC squad and `Applies` true it removes the
**unused** pooled profiles of exactly the demanded (role, difficulty) whose level is outside
`ArenaDifficultyPolicy.GearBand(centre)` = `[max(1, centre - 5), centre + 5]` (`IsTacticalGearOutOfBand`; the
server patch writes the very same function's result, so the two cannot drift), both sides, because the band moves up from round 1
to 3. `centre` is `ArenaTacticalOpening.CurrentGearLevel`, the level of the **acknowledged** round, so a lost
report keeps client and server on the same stage. The native miss path then asks the server for a fresh batch at
the band it now writes. Nothing else is touched: no other role or difficulty, no assigned profile, no scav role,
no backup loader.

The budget is **two purges per (role, difficulty, centre)**; a new centre starts over. It replaces Gun Game's
30-second interval, which would skip round 3's purge after a short round 2; two instead of one absorbs the one
benign race (a backup-loader refill that lands just after the first purge). The dictionary is cleared when the
world object changes (a second raid with the same centre starts with a full budget) and by `Reset()`, which the
raid reset calls beside the pinned Gun Game reset together with `ArenaTacticalOpening.Reset()`. The budget exists
for the picture where the server does **not** write the stage (the bot system's adapter is off): every fresh
batch is then outside the band again, so without it every spawn would cost a 15-profile server round trip. A
skipped purge is one Debug line per pair and centre and one Warning per raid ("the server's gear band does not
match the round"). Latency of the first fresh batch is unmeasured (the 45 s assembly window absorbs it; an
eager purge at the ack is the planned fix if a native run shows more than a few seconds).

## What the player sees

One line on the buy screen, its **own** TMP label on the card screen and its **own** IMGUI label in the plain
window, never appended to the plan line (which already holds up to 102 characters on a 1000 px line), at most 45
characters, from the pure helper `TacticalOpeningText.Note`:

| Round | Text |
| --- | --- |
| at the opening level of round 1 | `Enemy squad this round: light gear` |
| at the opening level of round 2 | `Enemy squad this round: medium gear` |
| the first full round after a lighter one, that round only | `Enemy squad from now on: full gear` |
| every other round | no line |

It is shown only when the feature applies, the squad is PMC and the report of exactly the current round is
acknowledged (`TacticalOpeningBandGate.OpponentGearNote`, read as `TacticalMode.OpponentGearNote`): the player
is never told about a stage the server has not confirmed. The words describe the opponents' kit class, not a
measured equivalence. No F9 change, no change to `ArenaCompactModeRules` (UiBridge recognises that text), no new
setting, no toggle.

## Logging

Debug only for gameplay (docs/LOGGING.md): the server's `Tactical round N reported`, the band line with
`round=N`, the client's held-request line (real held seconds, released by the cap or not) and one line per
pool purge. Warnings: a failing report route (once per raid), a failing gate (once per raid), the purge's
band mismatch (once per raid), a failing purge (once per raid), a failing `Enable`. Nothing at Info; the report
call avoids the SPT wrapper's two Info lines per call.

## Checks

- xunit: `TacticalOpeningPolicyTests` (the table, the custom maps, monotonicity, the gap rule, the derivation,
  the band predicate, `GearBandSpread` equal to the server's private `Spread`), `TacticalRoundReportTests` and
  `TacticalRoundStatusTests` (the route, refusals, `Duplicate`, restart, both reset sites, `/status`),
  `TacticalOpeningTextTests` (the three texts, 45 characters, the sweep against the policy), and the Tactical
  rows of `BotTierDifficultyBandTests`.
- `tools/TacticalOpeningChecks`: the gate against a fake clock and a scripted sender (report, ack, hold cap,
  stage rule, READY at once, lost report, lost ack, late ack, `Reset`, every refusal shape, the `seq` counter,
  a blank token, no throw), the reply parser, the glue in `TacticalMode.cs` and the buy-screen labels as source
  text; 97 mutants, each dying on the assertion it names.
- `tools/TacticalFreshGearChecks`: the real patch, state, gate and policy against the balance fixtures: both
  sides, the round sequence, the budget, a new centre, a late ack, a second raid, `Reset`, every guard, scav
  squads, the wiring in `Plugin.cs` and `ArenaRespawnDriver.cs`; 50 mutants.
- `tools/TacticalPolishChecks` compiles the new property into its slice, runs the hold and the line round by
  round, with probes; `tools/SoloTacticalSpawnChecks` pins the gate between the guard and the request.
- `GunGameFreshGearChecks` and `DurationDifficultyChecks` stay green (Gun Game and `Describe()` untouched).

## Native acceptance (not run)

On a GPU host (SPT-GPU-2 preferred), dev build of both DLLs, loaded hashes equal to disk, `Tactical > BuySeconds`
5, `SquadSize` 3, short rounds, `Objective` on with a short delay or kill the squad, Factory or Bay 5. Read each
bot's `Profile.Info.Level` once per round (`uibridge_bots`, `uibridge_inspect`).

- Maximum, rounds 1 to 4: levels 20..30, 40..50, 61..71, 61..71; AI `impossible`; server Debug `Bot gear band`
  lines `20-30`, `40-50`, `61-71` with `round=1/2/3`; the client's purge lines at the stage changes only, none in
  round 1 (the round 1 pool is fed by the default stage); `/status` `tacticalRound` follows the round.
- Reduced: 10..20, 22..32, 35..45; AI `hard`.
- Both failure shapes (block the request; block only the response), a round of under 20 s (the stage change
  still purges), a protected PitFireTeam Tactical raid (refused, no purge, native levels), the other modes and an
  ordinary raid (no request at all), the buy line in rounds 1 to 4 and on a custom map, the cost of the first
  fresh batch at round 3.
- A native run also settles what offline checks cannot: that the stage-1 default really feeds the preload, that
  the rolled levels stay in the band and the APBS tier of the bots is the one above the centre, and the order
  report, hold, request in the real client log.

## Open

- **The one unproven dependency of the purge (release gate):** that a miss makes the server answer with a batch at
  the band it holds **now**. Gun Game's native run measured it for its clock-driven band
  ([DIFFICULTY-MODEL-20261118.md](DIFFICULTY-MODEL-20261118.md)); Tactical changes the band by event and has no
  native run yet. The native level read of rounds 2 and 3 (acceptance above) is therefore a release gate, as is the
  measured latency of the first fresh batch after a purge (the eager purge is built only if it exceeds a few
  seconds).
- Native acceptance (above) is outstanding; the server and client halves are installed together only after it.
- **Scav squads**: the server writes the stage band for them, the client neither purges nor announces
  (their gear barely follows the level and a batch is 45); a pooled scav batch can serve many rounds. Accepted
  and documented; an opt-in setting.
- The eager purge at the ack (rebuilds the pool during the buy phase) is not built; do it only if the first
  fresh batch is slow natively.
- The BEAQO add-on (`ArenaTacticalBalance`) caps every Tactical round at level 20 by reading the server's result
  back and would override these stages from round 2 on; it is already inert because its reflection call passes
  5 arguments to a 7-parameter method. Its cap should be retired once opening rounds ship (the author's
  decision, outside this repository).
- Against a server without the route the SPT HTTP client logs an Error per failed attempt on its own (up to 16
  lines for the first report); that comes from SPT, not from this feature (docs/LOGGING.md).
