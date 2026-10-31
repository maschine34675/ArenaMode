# Iceberg native acceptance — 2026-10-31

Host: SPT-GPU-2 (`192.168.178.109`, `C:\SPT41`), test profile `SPT-GPU-2`
(`6ab7db1c…`), 1920×1080, SPT 4.1.6, EFT 0.16.9.5. Driven from this machine
through UiBridge 0.9.7 and gpu-run (`../UiBridge`, branch
`gpu-run/arena-menu-20261031`, see "Tooling"). Branch under test:
`feature/iceberg`. Contract and offline checks:
[ICEBERG-INTEGRATION-20261031.md](../../reviews/ICEBERG-INTEGRATION-20261031.md).
Raw evidence (reports, stills, log exports, probes) is under the ignored
`artifacts/iceberg-20261031/gpu/`.

## Build and delivery

Three development builds were deployed during the cycle; the results below name
the build they were taken with. Every build was `dotnet build ArenaMode.sln -c
Release` with the Redeploy checkout detached at the Core release commit
`232af19`, followed by `gpu-run deploy-wait --host host2 --server --game --files
BepInEx/plugins/maschine-ArenaMode,SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll,BepInEx/patchers/maschine-ArenaMode,BepInEx/plugins/maschine-Redeploy.Core.dll`
(72 files identical on the host, server stopped, game restarted, "build 16/16
identical" = VM build, host disk and loaded process). Redeploy.Core: the rebuild
writes different bytes under the same `1.0.18+232af19…` ProductVersion, so after
every build the released Core bytes (`98620898…`, from
`Redeploy/artifacts/publish-111/package-20261029-173842419/stage`) were copied
back before `deploy-wait`; the loaded Core is the released one throughout.

| Build | Commit | Client SHA-256 | Server SHA-256 | Used for |
|---|---|---|---|---|
| 1 | `c487f80e` (A–D + review fixes) | `5260002B…0323` | `58E80A87…23D2` | menu check only (ICEBERG tile under the Arena panel) |
| 2 | `68d036fc` (marker note) | `39906463…E96E` | `FF8C42BD…6A32` | Domination attempt 1 (refused), Deathmatch probe raid |
| 3 | `a6ed8d93` (Domination floors) | `27339CAE…DD30` | `B5BDDDAA…88C` | all accepted runs below |

Full hashes of build 3: client
`27339CAED6EC97524BC7FA931755E7FE6D84887B346FCB6414AC241A70F2DD30`, BigBrain
companion `18C9A47B7C3BD7096494B151C8ED270EE88507DA29C5116C2EDD49F49880C8BD`,
server `B5BDDDAA98070C0E8F82C490FAA1530CA9537340A4599ED1C8BEA37B1962688C`, Gate
`EE3BB99D2452C01661E25877E8B242E14F5045F43A472458F877D7FBC94ACAC8`, Session
`267DD929B6EC2F45C1BEA0278646DEBD04847913D128E7E6CA2FA83C0DE6D259`. ArenaMapPort
stayed at its installed feature build `0.4.0+32fda33` client / `db/arena_iceberg`
(not a published release), Redeploy.Client `1.1.1+232af19`, UiBridge
`0.9.7+f9f85a7`.

The client migrated the map allowlist on first start: `General/Maps` gained
`arena_iceberg` and `OptionalMapsRevision` went 4 → 5 (read back with
`uibridge_config`); the menu offered the ICEBERG tile afterwards.

## Results

| Run (build) | Result |
|---|---|
| Menu (1–3) | Arena flow shows the gold ICEBERG tile and, once picked, the map info "ICEBERG" with the Iceberg thumbnail. The tile sits at MapPort's native overview position, which is half under the Arena mode panel (only "BERG" visible; click point (425, 315) is just clear of the panel and works). ArenaMode cannot move it: MapPort's `ArenaModeMapPositionIntegration` restores the native anchor after `ArenaPortedMapPresentation.Create` (an Arena-flow anchor was tried and reverted). Recorded as a MapPort follow-up. |
| Domination 4v4, attempt 1 (2) | **Refused as designed.** `Arena match start failed: Domination point A is unsafe or unreachable: no nearby floor on the navigation mesh` → `Arena start is invalid; protected exit queued after the startup frame` → raid ended after 1 s (ExitStatus "Missing in Action", 0:00:01), `Arena session ended`. The authored light root A (y 3.9) lies in the pool basin. |
| Deathmatch probe (2) | Manual menu walk. 7 bots of both factions within 40 s, pickups 6/6 placed from 9 candidates, gates parked open (no hold in DM), lighting director armed, HUD "Continuous Deathmatch \| Free-for-all". Teleport dry runs from the player: A snaps 0.6 m up to floor y 4.5, B 0.5 m to 3.2, C 0.3 m to 3.2; with y 4.5 / 3.13 / 3.15 the snap is 0.0 m. All 20 pocket markers (west-01..10 at 150–156 m, east-01..10 at 2–7 m) and the donor `bot-core-01` (110.5 m) are reachable with complete paths; Bear house (12) included. |
| Domination 4v4 (3) | **Passed.** `Domination points verified: A=(16.770, 4.403, -91.390); B=(10.614, 3.130, -77.727); C=(21.910, 3.107, -47.300)`; `Iceberg Team DM formation ready: 4v4`; team-box capacity USEC 10/10 and BEAR 10/10; local BEAR at `east-03` (90.75, 1.89, -94.35) = Bear house (7); team bot spawns and arrivals at west-10 / east-01 / east-08 with "actual" equal to the point; HUD "ASSEMBLING TEAMS — BEAR 2/4 \| USEC 1/4 \| Damage and scoring disabled"; `Team DM gates open: all twenty formation paths revalidated; 3.01s since release (MapPort GatesOpen)`; `Domination objective routes from both opened team boxes verified`; ownership moved through all three points for both sides (A/B/C = BEAR/neutral/BEAR … USEC/BEAR/USEC … BEAR/BEAR/USEC); `Team Deathmatch result: Won; Score limit reached; BEAR 200:105 USEC; played=161.2s`; scoreboard "VICTORY, BEAR 200 : 105 USEC, Control points A USEC \| B BEAR \| C NEUTRAL 3%, kills 3–6"; protected native end (`Arena match result exit committed (Survived)`). The local player neither moved nor fired. |
| Deathmatch 8 (3) | **Passed** (`iceberg-dm` scenario, 12 steps, 4 expectations): raid in 20 s (location 4.4 s, game started 14.9 s), local spawn at the native USEC player marker (-44.6, 3.2, -90.5), 7/7 bots of both factions on SAIN combat and PatrolFollower layers, no "Team DM formation", "team-box capacity" or "Domination points" line, 0 errors; still `dm-ingame.png`. |
| Team DM 4v4 (3) | **Passed** (`iceberg-tdm` scenario, 19 steps, 4 expectations): raid in 19 s; `Iceberg Team DM formation ready: 4v4, USEC west / BEAR east`; `Team DM gates open: all twenty formation paths revalidated; 3.01s since release (MapPort GatesOpen)` 22 s after the raid step; at that moment 4 USEC bots stood in the west pocket (x -41…-55, z -88…-92) and 3 BEAR bots in the east pocket (x 100–101, z -82…-96), the local BEAR at Bear house (14); 20 s later all 7 bots were in the field (x -2…32). Routes to A/B/C from the player complete (84/87/94 m). Test-hook kill → death → `Iceberg team player respawn: arena-iceberg-tdm-v1-east-01, (97.94, 0.73, -83.73)`, player read back at (97.9, 0.7, -83.7). 0 errors. |
| Team DM 10v10 (3) | **Passed** (same scenario, participants 20): `Team DM formation ready: 10v10`; gates open 3.02 s after release, 32 s after the raid step; 10 USEC bots in the west pocket and 9 BEAR bots in the east pocket plus the local BEAR at Bear house (9) (89.3, 1.9, -86.5); 19/19 bots later, most already in the field; routes to A/B/C complete; respawn at `east-08` = Bear house (12) (97.75, 0.73, -96.15), the marker whose 0.53 m tunnel-wall clearance was an open question; player read back at (97.8, 0.7, -96.1). 0 errors. |
| PMC control (3) | **Passed** (`iceberg-pmc` scenario, 12 steps, 5 expectations): ordinary PMC raid through the native flow (tile "ARENA: ICEBERG", bots Low): spawn at the native USEC player marker (-39.8, 3.2, -91.2), 1 PMC and 3 Scav bots, no "Team DM", "Domination", "Arena pickup" or "team-box" line in the log, 0 errors. |
| Tactical (3) | **Refused as designed**: `gpu-run raid --mode tactical --map-from-menu --map ICEBERG` fails before any raid: `map tile "ICEBERG" is not offered after 4 more looks (tiles: THE LAB, BOWL, FACTORY, CUSTOMS, THE LABYRINTH, AIR PIT, BAY 5)`; Equator is hidden under Tactical as before. |

## Offline verification of the tested commit

`tools/Run-ArenaVerification.ps1 -Harness DominationRuntimeChecks,IcebergChecks,AreaMapPreviewChecks`
on `a6ed8d93` (after the Domination heights): xunit 3,623 total / 3,620 passed /
3 known skips, DominationRuntimeChecks (597 assertions, 13 faults incl. the new
`iceberg-pool-height` mutant), IcebergChecks (4 Player / 9 Bot installed rows, 180
filtered tests, 6 PFT faults rejected) and AreaMapPreviewChecks pass;
`ARENA_VERIFICATION_SUMMARY passed=4 failed=0`, live install unchanged by the run.
The earlier full runs of the review round are in the review document.

## Tooling

- gpu-run's Arena flow proved a mode click by reading the panel's "OBJECTIVE
  value"; ArenaMode 0.4.1's menu (commits of 27–29 October) has no such node, so
  every Arena raid failed with `OBJECTIVE shows "nothing"`. UiBridge now reads the
  panel's composed "Mode description" (the two-sentence text per mode from
  `ArenaCompactModeRules`, matched as a whole with its rule- and map-dependent
  variants; the legacy OBJECTIVE stays the fallback for older builds) and knows the
  `domination` mode: commits `4cfdd08`, `b5c8f8b` (playbooks) and `1f0a7ae` on
  branch `gpu-run/arena-menu-20261031`, fast-forwarded into UiBridge `main`
  (2,780 offline tests, HelpToolTests 28/28; not pushed; the running proxies serve
  the old playbook text until `bin\Release` is rebuilt). Iceberg is driven with
  `--map-from-menu` (catalog drift noted in every result line).
- Final smoke with that gpu-run on build 3: `raid --mode deathmatch|teamdeathmatch|domination
  --map-from-menu --map ICEBERG` each started in 21 s with 0 warnings/errors and read
  back `Everyone fights everyone. No round winner without limits.`, `USEC versus BEAR.
  Most team kills wins.` and `USEC versus BEAR. Hold 3 points to earn team score.`;
  each raid was left through `gpu-run leave` (DISCONNECT → … → MainMenu).
- The first Domination attempt and the probe raid were driven with single
  `gpu-run click` calls; the accepted scenario runs use the files kept in the
  evidence folder.

## Profile and configuration

- Profile `SPT-GPU-2`: after the PMC control raid the whole-profile comparison
  against the pre-cycle copy showed the ordinary-raid bookkeeping (skills, stats,
  in-game time) and a 12,924 RUB payment to Prapor matched by PitFireTeam's
  follower-insurance lines in the server log; that is the ordinary raid, not Arena.
  Against a copy taken after that raid, the three following Arena raids (DM, Team
  DM, Domination smoke) changed nothing but `Hideout/sptUpdateLastRunTimestamp`;
  the PMC inventory (372 items) is identical.
- ArenaMode cfg: the intended migration remains (`General/Maps` + `arena_iceberg`,
  `OptionalMapsRevision` 4 → 5, new `arena_iceberg_solo = 8`). `Match/GameMode` was
  put back to `teamdeathmatch` with `uibridge_config_set`; `arena_iceberg_team`
  (hidden entry, menu-only) was set back to 8 through the map screen.

## Not claimed

- Night raids only (game time 02:00–03:30); no daytime, weather or performance
  measurement; no long-session or balance claim (Domination reached 200 points
  in 2 min 41 s with the default income).
- PFT Zombies on Iceberg and the monitors were not run.
- A single host, a single profile; the whole-profile comparison and the config
  restoration are recorded at the end of this report.
