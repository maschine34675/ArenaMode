# Team start survey — 2026-09-08

The installed Customs contour has enough **database candidates** for two opposing
start sectors and 2v2 through 5v5 with either local PMC faction. This is static
identity/category/geometry evidence, not a NavMesh, physical-clearance, visibility
or balance certificate. Keep the existing contour and legacy presets unchanged;
the implementation uses the new `customs-factory-center-teams-v1` preset.

## Reproduce

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/AreaTeamSpawnSurvey/Verify.ps1 -SptRoot D:\SPT41
```

The standalone executable links the production shared area geometry and exclusion
policies. It reads the installed `SPT_Runtime/SPT_Data/database/locations/*/base.json`
without modifying them, and never references or builds a mod/deployment project.
It reports **every accepted candidate** with exact ID, float coordinates,
categories, native side labels, zone and separate PMC/bot eligibility. Full output
and deliberate fault reports are retained under
`tools/AreaTeamSpawnSurvey/artifacts/` (ignored). Exact selected IDs are also stored
in `tools/AreaTeamSpawnSurvey/Proposal.json` for repeatable selection checks.

On 2026-09-08 this command passed, including four deliberate failures: missing
selected bot, wrong native PMC mask, duplicate physical bot coordinates, and an
excluded sniper introduced into the proposal. Expected marker:
`ARENA_TEAM_SPAWN_SURVEY_PASSED`.

The survey rejects changed counts, unknown side labels, duplicate source IDs or
usable coordinates, missing/excluded selected IDs, insufficient per-side capacity,
lost separation and changed height/exclusion assumptions. Source/candidate hashes
are reported for audit; they are not rigid whole-file version requirements, since
unrelated database changes need not invalidate a feasible proposal.

## Eligibility and corpus

`ArenaAreaDefinition.AllowsSpawn` applies the actual contour, 0.75 m horizontal
inset, unrestricted height for the contour, and exact sniper exclusion. The survey
also applies shared bot point/zone exclusions. Native side/category meanings were
checked against `EFT/EPlayerSideMask.cs`, `EFT.Game.Spawning/ESpawnCategoryMask.cs`
and `SpawnCategoryExtension.cs` in `D:\SPT41\Development\_decompiled`:

- A local PMC requires `Player` plus its faction in `Sides`; `Pmc` permits both
  USEC and BEAR and `All` also permits Savage. `Player` alone does not mean PMC.
- A runtime bot candidate requires a nonempty zone and `Bot` **or `BotPmc`**.
  Do not discard the three Customs `BotPmc`-only candidates. All eleven contour
  bot markers carry native `Sides=Savage`; that does not make them player starts.
- `Boss` alone is not enough for `ArenaAreaRuntime.IsBotPoint`. The older server
  capacity check accepts `Boss` too, but that difference adds no candidates in
  these three measured corpora. New team setup must use the runtime-compatible
  rule rather than count unsupported candidates.

| Existing area | All accepted | Local USEC choices | Local BEAR choices | Runtime bot choices | Recommendation |
|---|---:|---:|---:|---:|---|
| Customs contour | 28 | 17 | 17 | 11 | New opposing-sector preset, 2v2–5v5 |
| Customs rectangle | 22 | 15 | 15 | 7 | Preserve existing 2v2 |
| Factory South | 29 | 20 | 20 | 9 | Preserve existing 2v2 |

Legacy bot totals cannot supply two disjoint pools of five. No new team-start
semantics or team-size increase is justified there by this survey. All full-map
and legacy behavior should remain unchanged.

Source SHA256:

- Customs `bigmap/base.json`:
  `495820FDBBC2993D786599F3E28DF2AF6243D6EBE2008CDBBA35B68A85B37873`.
- Factory `factory4_day/base.json`:
  `9826CA3EFFD4F925E8DC123CA1BAFD8469B24BEDF3C0F2C2D41270550ABA430D`.

Canonical accepted-corpus SHA256 (ID-sorted IDs, exact float values, categories,
sides and zone; excludes unrelated base fields):

- Customs contour: `153A44AFDDE3614469CC1B07D5EAB963A00213A9AA50849D54C28CCC9FE924EB`.
- Customs rectangle: `67644A751AE9DBD74A8E4DEBF82D59CD812874788972DB87169E664AAB92F339`.
- Factory South: `3A777C4388DEDDCF0A57C882227710E9C2BDBE9112CE33CF5E66F8E53C56CC35`.

## Selected sectors

Use explicit immutable spawn-ID pools, split by initial **player** and **bot**
eligibility, under the parent contour. These pools are team start data, not extra
walls or two rectangles that must fit inside the polygon. All coordinates below
come from the installed database; none are fabricated scene anchors. The root
integration accepted **USEC west / BEAR east**. Every selected player marker has
the native `Pmc` mask, so the local USEC or BEAR can enter its respective sector.

| West role | ID | X | Y | Z |
|---|---|---:|---:|---:|
| PMC | `2051eb7b-3ae8-49b8-adcb-c13f0ddc7972` | 317.338 | 1.128 | -78.756 |
| PMC | `f8fa00b6-4941-4c21-b10b-77ff30b0c382` | 314.86798 | 1.158 | -93.116005 |
| BotPmc | `046309a0-3d13-48c4-a3e6-9bb509ac339d` | 363.14087 | 1.3200657 | -109.22453 |
| BotPmc | `570f30b0-d2da-4f91-9270-45783c7c6a88` | 327.3948 | 1.5201291 | -102.241684 |
| Bot | `75558193-c46a-490f-aa68-15650a803d6f` | 323.28787 | 1.6489422 | -98.48353 |
| Bot | `785e4aeb-03eb-45f8-a521-2a870204a7d3` | 356.54138 | 1.1500976 | -24.188492 |
| Bot | `cffefb6b-e763-4386-9625-8c002b16d4f3` | 365.1455 | 1.3200623 | -108.90036 |

| East role | ID | X | Y | Z |
|---|---|---:|---:|---:|
| PMC | `2cb9950d-20ec-484e-919c-5489bb7abd5c` | 478 | 2.7 | -83.14 |
| PMC | `4d2e6d0c-078c-4918-8227-8df098a47a39` | 478.86 | 2.7 | -79.23001 |
| PMC | `582b9ea6-4404-4af1-8443-da3db48fcaad` | 480.55 | 2.7 | -81.46 |
| PMC | `6d8626ec-449b-4d63-a066-38e802756ec9` | 480.24 | 2.7 | -84.47 |
| PMC | `b54ce786-74b8-41b1-889f-0fe62c36d8e0` | 481.06 | 2.7 | -79.22 |
| Bot | `13adbe1b-21b0-4422-90a9-eab3ee8313fb` | 391.85657 | 1.5050216 | -86.186775 |
| Bot | `295048b1-140d-47c3-85f8-ab5bdaaafa67` | 471.19672 | 2.6568923 | -54.298454 |
| Bot | `75c4714c-1d9f-411a-8a42-e9d5ee16a77a` | 463.7923 | 1.207964 | -117.402084 |
| Bot | `dd28bf37-44a2-4407-a30b-2b0e9ed732eb` | 454.29303 | 2.6759148 | -81.539024 |
| Bot | `e880fea7-94e8-440f-874b-fd131ac2d998` | 410.90927 | 1.3883524 | -112.0087 |

Each side has five unique bot positions. At 5v5, local USEC uses one of two west
PMC points plus four west bots and five east bots; local BEAR uses one of five
east PMC points plus four east bots and five west bots. Smaller teams need fewer
of these same candidates. Player and bot pools are disjoint across both sides.

The eleventh bot, `bec5bf22-afff-4cc6-bb26-b2c33e75d4be`, is intentionally not an
initial start. It is very close to the selected east marker `13adbe1b...`; keeping
it offers no additional required 5v5 capacity. It remains an ordinary area respawn
candidate. Other unselected PMC points also remain ordinary respawn candidates.
The roof sniper `6e771bc5-ce82-4ab8-88e2-d50dedf4e34e` remains excluded from **all**
spawn phases.

Measured XZ separation, considering every possible local start and every bot:

| Measurement | Result |
|---|---:|
| Closest opposing selected start pair | 35.063 m |
| Empty X band between selected west/east starts | 26.711 m |
| Closest potentially simultaneous west pair | 2.031 m |
| Closest potentially simultaneous east pair | 23.761 m |
| Maximum west span | 85.292 m |
| Maximum east span | 89.475 m |

Alternative local PMC markers never coexist, so their pairwise distance does not
represent an actor collision. The 2.031 m west bot pair is real and constrains any
startup reservation/spacing threshold: arbitrarily requiring 5 m would silently
destroy five-bot west capacity. These are broad sectors, especially the isolated
northern west bot; they are **not compact squad formations** and are not claimed
to be balanced. Do not move that point to an invented neighboring location just
to make a nicer arrangement. Future measured markers or separate player-marker
bot placement could improve formation, but require their own verified lifecycle.

## Runtime conditions still required

- Before snapshot/initial actor release, validate the exact selected area and
  team size, both side pools, current IDs and supported native masks. The old
  whole-area `17 player / 11 bot` count does not prove either start sector usable.
- After scene loading/carving, require sampled, complete inside-contour NavMesh
  paths to every selected candidate actually needed for that raid. A database
  coordinate and the preceding successful 2v2 raid do not prove the new 5v5 setup.
- Recheck `SpawnBlocked`, active-body occupancy, pending activation reservations
  and actual positions immediately before dispatch. Do not reuse one initial bot
  point for simultaneous pending activations. Do not silently reduce team size or
  choose a marker from the wrong sector when a required pool fails readiness.
- Once the initial team is assembled, respawns may use all connected permitted
  area candidates, choosing against live enemies and occupied positions. Initial
  sector separation is not a promise of safe later respawns. Preserve the no
  off-area fallback rule and player body-handover position preflight.
- Keep normal raids, PFT, legacy 2v2 and whole-map team behavior unchanged. This
  survey contributes start data only; it provides no new general game permissions.

The user still needs a real raid with both local faction arrangements and chosen
team sizes, observing formation, successful full assembly, deaths/respawns and
absence of boundary or navigation failures. No raid was run by this survey.
