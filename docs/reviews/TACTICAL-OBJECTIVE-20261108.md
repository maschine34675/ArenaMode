# Tactical objective package: capture point, opening lanes, round door reset (2026-11-08)

> **2026-11-13: decision 5 superseded in part by [SPAWN-ROWS-FOLLOWUP-20261113.md](SPAWN-ROWS-FOLLOWUP-20261113.md).**
> Equator and Iceberg no longer refuse Tactical, and Chop Shop admits it solo, so the point applies there too
> (Equator X, Iceberg one of A/B/C per round, Chop Shop X); not run natively on those maps when this note was
> written.

Adopted from the fork survey ([report](../reports/2026-11/FORK-SURVEY-THATHEATHENJAY-20261108.md),
recommendation 2: the fork's "TeamFight" rounds layer), re-implemented against our Tactical mode;
no fork code was copied. This document is the contract; the implementation follows it; the
dated record of the native run goes to `docs/reports/2026-11/` once it has run (see Verification).

## Decisions

1. **Both Tactical variants.** Solo (1 vs N) and the PitFireTeam team arena get the same package.
   The point is a second way to win a round, never a replacement for elimination.
2. **Four settings.** `Tactical/Objective` (bool, default on) freezes at arm as
   `ArenaSession.TacticalObjectiveSelection` (it decides the reward rule, so it cannot move during
   a match); `Tactical/ObjectiveAfterSeconds` (int, default 60, 0 to 300) and
   `Tactical/ObjectiveHoldSeconds` (int, default 10, 3 to 60) are read at every round start, so a
   live F12 change applies from the next round; `Tactical/CloseDoorsAtRoundStart` (bool, default
   on) is read at every round start too. One Warning per match names an appear time plus hold
   time beyond the round timer (no hold could complete before it). No map-screen row (the mutator panel is at capacity, the
   left panel's free band would need new pins); the F12 menu carries all four.
3. **Rewards: kills only while the objective is on.** A round that ends on a capture is not a
   `SoloElimination`/`TeamElimination` round (the server derives those wins from deaths and the
   timer and voids an unfinished round). With the objective on, Tactical declares `KillsOnly`,
   exactly as Domination does since 2026-11-08: eligible kills pay, no round-win bonus, no hold
   bonus (AREA-RULES: holding points never become kill-based rewards). With the objective off,
   the elimination rules and their win bonus are unchanged. The server admits `KillsOnly` for
   `tactical` (`RuleFitsMode`, `RuleAllowed`). The in-match roubles (win/loss bonus, kill
   reward) are untouched in both cases.
4. **Only the point changes a round's end.** The solo timeout still loses, the team timeout still
   decides on survivors, elimination still ends the round first, the draw cap and NoContest are
   unchanged. The capture is evaluated after elimination and before the timer.
5. **Maps.** The point comes from the Domination layout of the map (Factory day/night, Bay 5,
   Air Pit, Bowl; Equator and Iceberg stay refused for Tactical), one point per round chosen at
   random among the scene-verified floors (Factory: the survey seeds through the same native
   floor and path checks as Domination; ported maps: the authored anchors with the team-box
   destination as navigation anchor). Labs, Labyrinth and the Customs areas have no point: the
   round plays as today and the status line shows `no capture point here` for the round. A refused point is a Warning and a
   round without a point, never an aborted match (Domination aborts because the point is the
   mode; here it is an option).
6. **Bots.** The enemy squad (solo: the squad; team: the five frozen opponents) is driven through
   the existing objective bridge and BigBrain layer; PitFireTeam followers are never driven (the
   layer at priority 75 would override PFT's own follow and order layers). In the team arena the
   enemies hold objective leases only while the local PMC is alive; once the PMC is down the
   reviewed post-death pursuit owns them as before. Without BigBrain the bots keep their native
   behaviour and the point still works for the player.
7. **Doors.** At every round start (solo `StartRound`, team go-live) every `Door` that is open now
   but was not open when the scene placed it (`InitialDoorState`), unlocked, operable and intact is
   shut immediately and without a sound (`SetDoorState(Shut, force: true)`; the state is read back,
   because the call is a silent no-op while an interaction toward Shut still settles); doors the
   map itself starts open, locked, interacting, breaching, broken, inoperable doors, doors that
   forbid interaction and doors with a map-script trigger on Shut (it would fire with a stale or
   missing interacting player) are skipped. MapPort spawn gates are not `Door`s and are untouched; Tactical never holds a gate
   lease.

## Behaviour

**Round clock.** `_phaseEndsAt` stays the one round clock (default 240 s, minimum 30 s, set in the
three existing places). The objective has its own pure clock, `TacticalObjectivePolicy`, begun at
the moment combat counts: solo at `_soloCombatReady`, team at the go-live block of
`TickTeamStaging`. It is reset at every round end and teardown.

**Point.** `ObjectiveAfterSeconds` after combat start the point becomes active (ring, beam and
label appear; the HUD line counts down to it). A side that stands on it alone (radius 4 m, height
tolerance 1 m, line of sight from the centre, as Domination counts occupancy) accumulates hold
progress; `ObjectiveHoldSeconds` alone completes the capture. Both sides present: progress frozen.
Nobody present: progress reset. The holder changes: progress restarts for the new holder. A step
over 30 s (a stall) awards nothing. Occupants are the exact round identities: solo the living main
player and the living squad bots; team the bound local body, the bound followers and the bound
enemies. Captured by the friendly side: solo `EndRound(won: true)`, team
`EndTeamRound(Won, "...")`; by the enemy: `EndRound(won: false)` / `EndTeamRound(Lost, "...")`.

**Bots.** From combat start until `ObjectiveAfterSeconds` the squad gets opening lanes: one point
per bot across the middle between the two groups, perpendicular to the line joining their
centroids, 6 to 18 m apart, each sampled onto the NavMesh (unsampled lanes are dropped; no lanes
at all means no opening objective). From activation on the squad's objective is the point itself
(owner = the current holder, contested flag). The bridge is begun per round and stopped at round
end, so no lease survives a round. Lanes and point are published only while the round is live
and combat counts.

**HUD.** The Tactical status line gets a short tail: `POINT in 42s`, `POINT free`, `POINT you 40%`,
`POINT enemy 70%`, `POINT contested`, `no capture point here`. The F9 panel and the held table are
unchanged. The announcer gets no new cue; the round-won/lost cues apply.

**Menu copy.** With the objective on and a map with a point selected, the Tactical description
reads solo `Eliminate the squad or hold the point.` / `Endless rounds; death / timeout loses.` and
team `First to N round wins; hold the point.` / `On death, spectate until next round.`; otherwise
the texts of 0.5.0. gpu-run's catalogue (UiBridge repo) learns the two new variants.

## Lifecycle and teardown

`TacticalMode.Objective.cs` owns the state: `InitializeObjective` at `OnMatchStart` (frozen flag,
the two numbers, the layout check), `BeginObjectiveRound` at combat start (placement, view,
lanes, bridge begin), `TickObjective` from `TickLive` (solo, after readiness) and `TickTeamLive`
(team, after the elimination checks, before the timer), `EndObjectiveRound` from `EndRound`,
`EndTeamRound`, `FailTeamTransition` and `OnMatchEnd` (view disposed, bridge stopped, policy
reset). `ArenaMatchService.OnRaidEnded` reaches `OnMatchEnd`; a native stop leaves nothing: the
view lives on a GameObject destroyed with it, the bridge checks the exact raid on every call.

## What this does not change

- The PitFireTeam round lifecycle (frozen rosters, retirement order, rebind steps, served
  preflight), the spectator, the pursuit leases and their gates, the corpse retention, the
  staging damage block.
- The solo buy phase, kits, money, the kit swap hold, the spawn policy and its 45 s window.
- Domination: the bridge keeps its contract (count match, exact raid, lease identity, 8 s
  assignment retention, 12 s reject); only the mode query moved behind `IArenaObjectiveHost`
  (`ObjectivesLive`, `ObjectiveCount`, `TryClassifyObjectiveBody`), which `TeamDeathmatchMode`
  answers in `TeamDeathmatchMode.ObjectiveHost.cs` exactly as the bridge used to ask it
  (`IsDomination && IsSearchActive`, the admitted PMC bodies, USEC as team A); the interface lives
  in `IArenaObjectiveHost.cs` so the bot harness links both production files beside its doubles.
  The bridge no longer looks at a body's faction itself: the host names the team, which is what
  lets a scav Tactical squad take leases.

## Limits

- Solo opponents spawn 25 to 70 m from the player and the player stays where the last round
  left them, so the point's distance to either side is whatever the map gives; the lanes are
  computed from the live positions at combat start.
- The team arena's enemies leave the objective when the PMC dies (pursuit takes over); a point
  held at that moment is simply no longer held.
- A door shut by force teleports its leaf; a bot standing exactly in the leaf is pushed like any
  door push. Bots open doors natively on their way (a few seconds each).
- Without BigBrain the squad ignores lanes and point.
- Rewards with the objective on are kills only (decision 3).

## Verification

Offline: `tools/TacticalObjectiveChecks` (43 checks, twelve mutants on the pure policy and lanes),
`DominationBotChecks` (bridge anchors moved to the host interface; a single-team host contract with
scav bodies; its mutant `faction-not-host`, while `host-not-live` is killed at the Team DM case), `TacticalPolishChecks` (sliced methods with the new
hooks stubbed), `SoloTacticalSpawnChecks`, `TeamArenaExpansionChecks`, `CompactModeUxChecks`
(description variants), `TdmMatchMenuChecks`, `ArenaConfigMenuLayoutTests` (layout rows),
`RewardResultTests` (tactical KillsOnly), the xunit suite, the no-deploy solution build; UiBridge
`npm test` for the catalogue. Results on 2026-11-08: xunit 3,734 (3,731 passed, 3 expected skips),
the ten harnesses green (DominationBotChecks 165 assertions, 21 mutants; TacticalObjectiveChecks
43 checks, 12 mutants; CompactModeUxChecks 330), gpu-run `npm test` 2,961; a 41-agent adversarial
review found nine low findings and four critic notes, all addressed in this document and the code
(door reset by `InitialDoorState` and without Shut triggers, per-round numbers, the clock Warning,
reward docs, a coordinator begin test, the harness redundancy, wording).

Native, three solo Tactical raids on Factory on GPU-2 the same day
([record](../reports/2026-11/GPU-TACTICAL-OBJECTIVE-20261108.md)): the new menu copy, the point
placed at five different Factory seeds with four or five lanes, all five bots on the BigBrain layer
"Arena Combat Domination" moving to the lanes and then to the point, the HUD tails `POINT in 46s`,
`POINT enemy 79%`, `POINT contested`, `POINT free`, the enemy capture ending rounds, the friendly
capture ending a round as a win (round 4 of the first raid, hold 3 s test value), zero warnings or
errors from ArenaMode. Two findings came out of the raids and are fixed in the committed state:
`GameWorld.WorldInteractiveObjects()` throws in a local game (its `World` component is absent),
and `LocationScene.GetAllObjects<Door>` answers nothing because the scenes register their doors
under `WorldInteractiveObject` (an exact-type dictionary); the reset now enumerates that base type
and keeps the `Door`s, and logs one Debug line per round start (`door reset saw 77 door(s), shut 1,
skipped 23` in the third raid: a door a bot opened in round 1 was shut at the round 2 start).

PitFireTeam team arena, two matches with two companions on Factory on GPU-2 the same evening (same
record): the team menu copy `First to 3 round wins; hold the point.`, the followers never on the
objective layer (`pitTeam.FollowerPatrol`) while all five opponents were, the lanes in a line across
the midpoint until the point came up, the enemy capture ending rounds as defeats (`continuation
RebindNextRound`, then `EndMatchLost` at 2-3 and 0-3), the player side capturing twice with the
followers beside the PMC (1-2, 2-2), the door reset shutting the doors the squads had opened (`shut
2`), the point's clock running on while the PMC was down (`YOU ARE DOWN POINT in 11s`) with every bot
on `Arena Combat Pursuit` and the round decided by elimination, the server's `/arenamode/rewards/begin`
accepted for the team mode with KillsOnly and both captures posted; one observation, a lane ending
inside the ring but behind cover does not count until the bot steps around it (the clear-line check,
as in Domination). Not covered natively: Bay 5 / Air Pit / Bowl, a refused point, the reward
settlement of a KillsOnly Tactical run, a follower capture without the PMC, the beam and label in a
picture.
