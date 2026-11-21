# BlockChecks

`Verify.ps1` runs the Block integration checks against the installed ArenaMapPort data
(`user/mods/ArenaMapPort/db/arena_yard/base.json`: 4 Player rows, all with Infiltration
`ArenaYard`, and at least the 18 Bot rows of ArenaMapPort 0.6.0+1d8faa8, all in `BotZone_Main`,
the donor row `arena_yard-bot-core-04` with its exact floats; no list of ids, the Tactical rule
and the default of eight are pinned in `MapModeAdmissionChecks`), the filtered Block xunit
classes (`BlockMapTests`, `BlockTeamSpawnTests` and the neighbour classes the Chop Shop filter
names) and the PFT compiled faults; it fails loudly when the installed MapPort build ships no
`arena_yard`. The location id is MapPort's, the player-facing name is Block (Arena's own name;
it has nothing to do with the Customs yard area). The sibling `tools/BlockTeamBoxChecks` runs
the whole suite and the team-box oracles (the installed Player rows inside their boxes, no Bot
row inside one, the donor row exact); it is registered as well, as `IcebergTeamBoxChecks` is.

The installed MapPort build `0.6.0+1d8faa8` carries the map under the same version string as the
published 0.6.0, which has none. The harness decides by the installed `base.json`, never by the
version.

## Scenarios

`scenarios/` holds six gpu-run scenario files for the native run on SPT-GPU-2, twins of the files of
`tools/ChopShopChecks/scenarios`. All six ran on 2026-11-21 against the installed build `0.6.0+fb2b3af6`
(`block-dm` first on build `0.6.0+c071cb81`, where it failed, then in its present form on the installed
build); the numbers below are from those runs
(`artifacts/yard-20261121/native/<run>/`, gitignored) and from the installed data.

| File | What it does | Native result of 2026-11-21 |
|---|---|---|
| `block-dm.json` | Deathmatch with eight participants; waits up to 150 s for the first `Arena burst spawn: ... arrived`, then 25 s; expects no team-box, formation or Domination line, at least three bots, no `no usable spawn point` | passed in this form (14 steps, 6 expectations) as the first raid after a server start: first arrival 38 s after the match start. Its earlier form with a fixed 30 s wait failed on such a raid (0 bots at 30 s) |
| `block-tdm.json` | Team DM in the pockets (USEC south / BEAR north): 20 markers restored, formation ready, gates held then opened with all twenty paths revalidated, routes to both capture points and to the donor row, a test-hook death and the respawn at a north marker | passed at 4v4 (22 steps) and at 10v10 |
| `block-dom.json` | Domination 4v4 with the two capture points A and B (`Domination points verified: A=` and `; B=`), the gate sequence, objective routes from both opened pockets, 60 s of play | passed (15 steps) |
| `block-zombie.json` | solo Zombies under godmode, from the installed `arena_yard-player-usec` row (south) and again from the `arena_yard-player-bear` row (north) | passed (15 steps): 32 of 32 infected alive after 35 s in the south and 20 s after the move north |
| `block-pmc.json` | ordinary PMC raid with few bots as the isolation control: no Team DM, Domination, pickup or team-box line, no error; the raid ends through the map's own exit "Block South Gate", for which the scenario places the player in the exit box on the south platform at (-20.75, 7.48, 11.25) | passed (12 steps): no ArenaMode line in the raid, the raid ended by extraction 12 s after the placement |
| `block-tactical-solo.json` | solo Tactical, three rounds (the `map` parameter defaults to `BLOCK`): five opponents live in each round, the burst spawn plan, the objective, a lost round and a round ended by the test hook | passed (20 steps, 10 expectations) |

Run from the UiBridge checkout (they need an ArenaMapPort build that ships `arena_yard` and a gpu-run
that knows the tile `BLOCK`; `mapFromMenu` is set, so a catalog drift in gpu-run does not stop them):

```text
node ../UiBridge/tools/gpu-run/gpu-run.mjs scenario <file> --host host2
```

The tile entry in gpu-run's catalogue is an uncommitted change in the UiBridge checkout (2026-11-21).

What the scenarios assume, and what to set before them:

- The BepInEx log level includes Debug. The expectations read ArenaMode's Debug lines.
- Before `block-tactical-solo`, set three Tactical timings through the bridge with persist=false:
  `Tactical/BuySeconds` 5, `Tactical/RoundSeconds` 60 and `Tactical/ObjectiveAfterSeconds` 20. The scenario does
  not set them itself, and nothing is saved.
- Team DM at ten per side takes two parameters:
  `--param participants=20 --param "formationLine=Team DM formation ready: 10v10"`. The native 10v10 run also set
  `Spawns/SpawnGateDriveScale` from 0.5 to 1.0 through the bridge with persist=false and set it back afterwards;
  that is not needed for the scenario to run.
- Leave about 40 s between two Arena raids ("PitFireTeam post-raid finalization is still settling" otherwise).
- The first raid after an SPT server start can show no bots for about half a minute: on 2026-11-21 the first
  Deathmatch raid had 0 bots after 30 s (`Arena solo population: total=8 ... pending=7`, while the server was building
  "150 bot profiles"), and the scenario's three-bot check, then after a fixed 30 s, failed for that reason. A raid
  by hand right after it had its first bot after 9 s. `block-dm` waits for the first arrival since.
- gpu-run changes `Match/GameMode` (and `Map participants/arena_yard_team` for the 10v10 run) in the saved
  configuration. Set them back after EFT is closed.

Coordinates: the Team DM routes go to capture point A (-29.3245, 6.1262, 71.6287), capture point B
(-62.5245, 6.1262, 72.1887) and the donor row (-12.88, 6.17, 87.03); the Zombies scenario places the
player on the installed `arena_yard-player-usec` row (-14.86, 7.27, 13.3) and later on the
`arena_yard-player-bear` row (-10.83, 6.18, 145.09). Chop Shop's "yard centre" start has no Block
equivalent that was measured, so the Zombies scenario starts from a Player row instead.

Not run natively: a PitFireTeam squad on Block, Gun Game, Deathmatch above eight participants, a match played to
its result, the local player in the south pocket (the test profile is BEAR, so the player always started north),
a held gate walked into by a player, and anything heard.
