
## Native scenarios (gpu-run)

`scenarios/iceberg-dm.json`, `scenarios/iceberg-tdm.json` and `scenarios/iceberg-pmc.json`
are the gpu-run scenarios of the GPU-2 cycle of 31.10.2026
(`docs/reports/2026-10/GPU-ICEBERG-20261031.md`). Run them from `../UiBridge/tools/gpu-run`
with `node gpu-run.mjs scenario <file> --host host2 --out <dir>`; the Team DM file takes
`--param participants=20 --param "formationLine=Team DM formation ready: 10v10"` for the
10v10 run. They need gpu-run with the "Mode description" readback (UiBridge `1f0a7ae` or
later) and an installed ArenaMapPort build that ships `arena_iceberg`. Domination and the
Tactical refusal are single `gpu-run raid --mode <mode> --map-from-menu --map ICEBERG`
calls; the report quotes the expected outcome of each.
