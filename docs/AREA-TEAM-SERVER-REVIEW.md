# Area team setup — server review

2026-09-08, isolated `feature/area-team-setup` checkout, baseline `62d610e`.
This leaf implements the server half of the separately versioned Customs team
setup; shared catalog, menu and live placement are owned by other leaves.

## Behavior and isolation

- `areaTeamSize` is nullable on the incoming raid-start DTO. The shared policy
  requires an explicit 2..5 for `customs-factory-center-teams-v1`; full-map and
  older areas retain omitted/zero and reject nonzero overrides.
- The router validates area and size before replay, snapshot recovery or
  preparation. Session preparation and direct arming independently validate the
  same policy. The selected integer is stored with the exact arm, replay compares
  it, and initial/replayed answers plus status echo it. Idle/unrelated sessions
  expose zero. Recovery/finalization retain the frozen value until idle.
- The per-raid location patch passes the frozen value into mutation. Before
  assigning any filtered list, the new-area check requires at least one native
  compatible PMC start and `selectedSize` bot starts on **each** exact surveyed
  side. Either side can be all enemies when the local PMC belongs to the other.
  A 2v2 choice therefore does not require the 5v5 maximum or incorrectly accept
  only one opposing bot.
- Counts use exact catalog identities, existing contour/sniper/global exclusions,
  native categories and PMC side flags. No marker category, side, position or
  zone is rewritten. The complete allowed contour list remains available for
  later movement/respawns; it is not truncated to initial start pools.
- Simultaneous starts require distinct positions, at least 1.5m horizontal
  spacing between bot pairs and each possible PMC/bot pair, and at least 25m
  between opposite sectors. Alternative PMC choices do not coexist and are not
  subject to the same-side 1.5m rule. Vertical offsets cannot evade these checks.
- Existing exact-clone validation repeats these checks after native/other-mod
  location work and before client-start acknowledgement. Failure retains the
  existing protected failed-start recovery path; there is no area fallback.
  Ordinary raids and legacy area mutation retain their previous behavior.

## Native evidence and limits

Read directly from `D:\SPT41\Development\_decompiled`:

- `EFT/EPlayerSideMask.cs`: Usec=1, Bear=2, Savage=4, Pmc=3, All=7.
  `EFT/PlayerSideExtension.cs` checks the required bit. The client area's own
  `ArenaAreaSpawnPolicy` also rejects unknown side bits. The server accepts known
  native names, with the requested PMC faction, Pmc or All present.
- `EFT.Game.Spawning/ESpawnCategoryMask.cs` and `SpawnCategoryExtension.cs`:
  Player, Bot, Boss and BotPmc are separate flags. All contains Player/Bot/BotPmc;
  Boss alone is not a generic Bot marker. New team counts mirror the existing
  client area's Bot/BotPmc-plus-zone rule instead of the older broad legacy count.
- Installed `SPT_Runtime/SPT_Data/database/locations/bigmap/base.json` supplies
  five surveyed bot starts on each side. These bot markers have native
  `Sides: [Savage]`, frequently alongside category Player. Bot eligibility must
  not be incorrectly tied to the combatant's USEC/BEAR side; PMC eligibility is
  checked separately. The retained contour corpus is 28 markers.

These are static database/protocol checks. They do not prove live NavMesh,
obstacle state, initial actor placement, AI behavior, visibility or team balance.
Real raid acceptance remains the user's GPU test and the separate runtime leaf.

## Verification

Root explicitly permitted this leaf to run the no-deploy helper. Final run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/AreaTeamServerChecks/Verify.ps1
```

Result: **167 passed, zero failures, zero skips**: 58 new AreaTeam cases and 109
existing area/contour regressions. Evidence:
`artifacts/area-team-server/area-team-d870efe028ec4376a942de9cb8af7fb3.trx`.
The helper requires the installed database directory, uses explicit `SPTPath` and
`SPTUserPath`, and overrides `SPTUserMods` to a nonexistent sentinel so the normal
AfterBuild deployment target cannot run. All three installed Arena/client/core
DLL hashes matched before and after. No solution build, deployment or commit was
performed by this leaf. Existing MudBlazor and unused combat-field warnings remain;
no new warning remains in this leaf's code or tests.

Negative fixtures alter actual deserialized location records after successful
baseline validation: either side missing a bot/PMC, unknown side/category,
Boss-only, missing zone, duplicate/changed identity or position, small horizontal
distance despite a large Y offset, sectors moved within 25m, outside positions
and wrong location. They must block the actual client-start confirmation. The
distance fixtures additionally assert the specific 1.5m/25m diagnostic. This is
deliberate input tampering, not a claim that source-code mutants were executed.

Four self-review passes completed: implementation/caller coverage; native mask
and profile-boundary review; adversarial replay/lifecycle/data scenarios; final
compatibility, nullable-warning, test-oracle and diff review. No known remaining
server issue. Independent review and integrated client/server/GPU acceptance are
still separate requirements owned by root.

Independent review by area_team_spawn_survey (2026-09-08): no unresolvedP0–P2. Checked strict choice/token/map/mode replay before snapshot preparation; final exact location clone validation; bothlocal factions and simultaneous1.5m/opposing25m spacing; immutable survey IDs and ordinary/PFT/legacy isolation. Tests source reviewed independently, root re-ran167/167 withzero skips. Static clone capacity validation happens after snapshot (clone exists in native start), protocolchoice validation before it. No live navigation claim.
