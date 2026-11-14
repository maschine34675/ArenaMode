# ChopShopChecks

`Verify.ps1` runs the Chop Shop integration checks against the installed ArenaMapPort data
(`user/mods/ArenaMapPort/db/arena_autoservice/base.json`: 4 Player rows and at least the 16
Bot rows of ArenaMapPort 0.6.0, all in `BotZone_Main`, the donor row
`arena_autoservice-bot-core-03` with its exact floats; no list of ids, the Tactical rule and
the default of eight are pinned in `MapModeAdmissionChecks`), the
filtered Chop Shop xunit classes and the PFT compiled faults; it fails loudly when the
installed MapPort build ships no `arena_autoservice`. The location id is MapPort's, the
player-facing name is Chop Shop. Contract: `docs/reviews/CHOP-SHOP-INTEGRATION-20261112.md`.

## Scenarios

`scenarios/` holds the five gpu-run scenario files of the native cycle on GPU-2 of 2026-11-12
([report](../../docs/reports/2026-11/GPU-CHOP-SHOP-20261112.md)) and `tactical-solo-ported.json` of 2026-11-13
([report](../../docs/reports/2026-11/GPU-SPAWN-ROWS-20261113.md)):

| File | Runs |
|---|---|
| `chopshop-dm.json` | Deathmatch with six participants, the parameter default (13 steps, 5 expectations); the recommendation is eight since 2026-11-13 |
| `chopshop-tdm.json` | Team DM with held gates, formation, capacity, respawn (21 steps, 4 expectations) |
| `chopshop-dom.json` | Domination 4v4 with the one capture point (14 steps, 5 expectations) |
| `chopshop-zombie.json` | solo Zombies from the yard centre (15 steps, 3 expectations) |
| `chopshop-pmc.json` | ordinary PMC control raid, no ArenaMode line expected |
| `tactical-solo-ported.json` | solo Tactical on a ported map whose refusal was lifted on 2026-11-13, three rounds (20 steps, 10 expectations) |

Run from the UiBridge checkout (they need an ArenaMapPort build that ships `arena_autoservice`
and a gpu-run that knows the Arena menu texts of 2026-11-09):

```text
node ../UiBridge/tools/gpu-run/gpu-run.mjs scenario <file> --host host2
```

Team DM at ten per side takes two parameters:
`--param participants=20 --param "formationLine=Team DM formation ready: 10v10"`.

The Zombies scenario runs under godmode (an idle player dies within half a minute) with the
player in the yard centre from the start. Until ArenaMapPort's rows of 2026-11-12 no Bot row
lay beyond the 25 m floor there and every request was dropped (the failed-dispatch backoff held
them to about one per 2 s); with the 16 rows the first wave has to fill (20 infected) without a
dropped request ("no usable spawn point" must be absent), at the centre and again after the
player moved to a pocket. Native on GPU-2, 2026-11-13: 32 of 32 with "points 5 preferred".
Leave about 40 s between two Arena raids: a start 5 s after the previous one was refused
with "PitFireTeam post-raid finalization is still settling".

`tactical-solo-ported.json` shows solo Tactical on a ported map for three rounds, every round live with five
opponents, no dropped request and no partial squad. Its `map` parameter defaults to `CHOP SHOP` and takes
`EQUATOR` or `ICEBERG` (`--param map=EQUATOR`). Before the run, set Tactical/BuySeconds to 5 and
Tactical/RoundSeconds to 60 through the bridge with persist=false (the scenario does not set them itself), so
a round is short and the saved settings stay as they were. Round 1 starts from a Player row and ends by the
timer or by the player's death, whichever the bots manage; round 2 is ended by the kill hook (a test-hook
death); round 3 starts after the respawn that follows. It ran on GPU-2 on 2026-11-13 on all three maps: 20
steps each, every round live with five opponents (the report is
`docs/reports/2026-11/GPU-SPAWN-ROWS-20261113.md`). Leave about 40 s between two Arena raids, as above.
