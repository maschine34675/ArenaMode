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
the whole suite and the team-box oracles; it is not registered, as `ChopShopTeamBoxChecks` is not
(it runs the complete xunit suite itself).

The installed MapPort build `0.6.0+1d8faa8` carries the map under the same version string as the
published 0.6.0, which has none. The harness decides by the installed `base.json`, never by the
version.

## Scenarios

`scenarios/` holds the six gpu-run scenario files for the native run on GPU-2, twins of the files of
`tools/ChopShopChecks/scenarios`. Nothing in them has run yet; the numbers and coordinates below come
from the installed data and the production catalog, not from a native run.

| File | Runs |
|---|---|
| `block-dm.json` | Deathmatch with eight participants, no team-box, formation or Domination line |
| `block-tdm.json` | Team DM in the pockets (USEC south / BEAR north): markers restored, formation, gates held then opened, routes to both capture points and to the donor row, kill and respawn |
| `block-dom.json` | Domination 4v4 with the two capture points A and B ("Domination points verified: A=" and "; B=") |
| `block-zombie.json` | solo Zombies under godmode, from the south Player row and again from the north Player row |
| `block-pmc.json` | ordinary PMC control raid, no ArenaMode line expected; ends through the pause-menu leave |
| `block-tactical-solo.json` | solo Tactical, three rounds (the `map` parameter defaults to `BLOCK`) |

Run from the UiBridge checkout (they need an ArenaMapPort build that ships `arena_yard` and a gpu-run
that knows the tile `BLOCK`; `mapFromMenu` is set, so a catalog drift in gpu-run does not stop them):

```text
node ../UiBridge/tools/gpu-run/gpu-run.mjs scenario <file> --host host2
```

Team DM at ten per side takes two parameters:
`--param participants=20 --param "formationLine=Team DM formation ready: 10v10"`.
Before the Tactical scenario set Tactical/BuySeconds to 5 and Tactical/RoundSeconds 60 through the
bridge with persist=false (the scenario does not set them itself). Leave about 40 s between two Arena
raids ("PitFireTeam post-raid finalization is still settling" otherwise).

Coordinates: the Team DM routes go to capture point A (-29.3245, 6.1262, 71.6287), capture point B
(-62.5245, 6.1262, 72.1887) and the donor row (-12.88, 6.17, 87.03); the Zombies scenario places the
player on the installed `arena_yard-player-usec` row (-14.86, 7.27, 13.3) and later on the
`arena_yard-player-bear` row (-10.83, 6.18, 145.09). Chop Shop's "yard centre" start has no Block
equivalent that was measured, so the Zombies scenario starts from a Player row instead. The PMC control
does not leave through "Block South Gate": gpu-run has no in-world exfil step and no exit coordinate is
recorded.
