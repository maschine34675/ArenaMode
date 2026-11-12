# ChopShopChecks

`Verify.ps1` runs the Chop Shop integration checks against the installed ArenaMapPort data
(`user/mods/ArenaMapPort/db/arena_autoservice/base.json`, 4 Player and 7 Bot rows, exactly
the seven the Tactical refusal and the default of six participants were argued from), the
filtered Chop Shop xunit classes and the PFT compiled faults; it fails loudly when the
installed MapPort build ships no `arena_autoservice`. The location id is MapPort's, the
player-facing name is Chop Shop. Contract: `docs/reviews/CHOP-SHOP-INTEGRATION-20261112.md`.

## Scenarios

`scenarios/` holds the five gpu-run scenario files of the native cycle on GPU-2 of 2026-11-12
([report](../../docs/reports/2026-11/GPU-CHOP-SHOP-20261112.md)):

| File | Runs |
|---|---|
| `chopshop-dm.json` | Deathmatch with the default of six (13 steps, 5 expectations) |
| `chopshop-tdm.json` | Team DM with held gates, formation, capacity, respawn (21 steps, 4 expectations) |
| `chopshop-dom.json` | Domination 4v4 with the one capture point (14 steps, 5 expectations) |
| `chopshop-zombie.json` | solo Zombies (18 steps, 3 expectations) |
| `chopshop-pmc.json` | ordinary PMC control raid, no ArenaMode line expected |

Run from the UiBridge checkout (they need an ArenaMapPort build that ships `arena_autoservice`
and a gpu-run that knows the Arena menu texts of 2026-11-09):

```text
node ../UiBridge/tools/gpu-run/gpu-run.mjs scenario <file> --host host2
```

Team DM at ten per side takes two parameters:
`--param participants=20 --param "formationLine=Team DM formation ready: 10v10"`.

The Zombies scenario runs under godmode (an idle player dies within half a minute) and
expects dropped spawn requests while the player stands in the yard centre, where no Bot row lies
beyond the 25 m floor (a known limit; the failed-dispatch backoff holds them to about one per
2 s). Leave about 40 s between two Arena raids: a start 5 s after the previous one was refused
with "PitFireTeam post-raid finalization is still settling".
