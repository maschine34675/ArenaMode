# Arena scoreboard review

## Scope

Arena matches now keep match-local scores for every observed contestant. The
compact HUD gives the local score a comparison, while **F9** opens the full
in-raid scoreboard. This state lives only in the client process and is discarded
at raid end; it does not write the SPT profile or change the protected raid-end
pipeline.

## Identity rules

Bot replacement is not hidden behind a made-up identity:

| Contestant | Scoreboard identity | Across replacement bodies |
|---|---|---|
| Local PMC | local match identity | score is retained |
| PitFireTeam follower | authenticated `AccountId` | score is retained |
| Generated opponent or ordinary bot | physical `ProfileId` | a new body gets a new row |

For Tactical, the friendly and opponent team totals remain match-long even
though each freshly generated opponent has a fresh row. The current opposition
table therefore shows this round's bodies, followed by a separate summary for
points and kills earned by earlier opponent bodies. This makes the match total
reconcilable without pretending that a newly generated bot is the same person.

For Endless, Rounds and Gun Game, F9 shows the leading twelve runs and always
keeps the local row visible. The top HUD names the leading rival. Finished
ordinary bot runs are compacted to a fixed history of 32 rows (the best and the
most recent); exact side totals and the number of earlier participants remain
available after compaction. Identical four-Hz roster snapshots do not mutate the
board or force a new sort.

## Score and lifecycle rules

- Every contestant uses the existing role points, headshot bonus and five-kill
  streak bonus.
- Local deaths still come from the existing respawn/death signal, so an
  aggressor event and the death signal cannot double-count one death.
- A posthumous grenade kill adds score without marking its dead owner alive.
- Old PitFireTeam profile aliases remain attached to their stable follower, so a
  delayed grenade or burn after a round rebind is credited to the right team.
- Technical body retirement uses a profile-scoped administrative-death gate.
  EFT may retain the last aggressor for `Kill(Undefined)`; those cleanup deaths
  do not create a killfeed line, score, reward or phantom death.
- Invalid or incomplete roster snapshots leave the last valid board intact and
  never turn a valid Tactical round into a no-contest.

## UI behavior

- F9 opens only over the native `BattleUI`; it will not stack over Inventory or
  the raid menu.
- Escape and the Close button release both the cursor and the gameplay-input
  block.
- The scoreboard is drawn above the Tactical buy panel and remains on-screen
  after a resolution or fullscreen change.
- Dynamic names are stripped of rich-text delimiters and line-breaking control
  characters before display.
- The existing Rounds intermission auto-open remains unchanged; other modes use
  F9 so the panel never freezes a vulnerable player unexpectedly.

## Automated evidence

- `dotnet test ArenaMode.Tests/ArenaMode.Tests.csproj -c Release`: scoreboard
  identity, scoring, local death, delayed PFT kill, administrative retirement,
  bounded 10,000-life history and production wiring.
- Native side, map, mode and menu harnesses: Arena selection behavior remains
  unchanged.
- Solo Tactical, PitFireTeam, multi-round 5v5, pursuit, loadout and Labs-spawn
  harnesses: existing roster and spawn contracts remain unchanged.
- `dotnet build ArenaMode.sln -c Release`: compiles against and deploys to the
  installed SPT 4.1 environment.

## GPU playtest still required

This machine cannot launch EFT. On the gaming system, check:

1. In each mode, earn one body kill and open F9; the local row, rival row and
   compact HUD comparison should agree.
2. In Tactical 5v5, let a follower score, finish the round and start the next;
   the follower keeps their score, new enemy bodies start fresh, and the earlier
   opponent summary reconciles with the match total.
3. Open F9 during the Solo Tactical buy phase; the scoreboard must sit above the
   buy panel, and Escape must close it without trapping input.
4. Use Give up after taking damage; the last attacker must not receive a kill or
   points from the technical death.
5. Confirm an ordinary PMC or Scav raid has no Arena HUD, input blocking or
   profile changes.
