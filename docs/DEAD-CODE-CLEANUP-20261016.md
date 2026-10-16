# Confirmed unused-code cleanup — 16 October 2026

Implemented after the source audit at `6827829`, with **36 reviewed removal
targets**: 29 Client declarations and seven Server methods across 21 source
files. The retired `EndlessDeathmatchMode` class counts as one target, including
its obsolete implementation. This is maintenance, not a measured performance
improvement. Nothing was deployed, pushed or published.

## Scope and audit correction

The [original candidate table](DEAD-CODE-REVIEW-20261016.md) is the detailed
inventory, with one explicit correction: **`ArenaEquipmentChoiceState.Label`
is used by `ArenaEquipmentChoiceTests.cs:151,153`**. The initial audit missed
those consumers. The full unit build caught this; the property was restored
exactly and its tests remain unchanged. It is not part of the cleanup.

Removed the other 27 Client candidates, including the retired Endless mode,
unused getters and short adapters. Removing the unused lifecycle `Initialize`
also orphaned its private `PluginGuid` and `SetUnavailable(string)` helper;
these two dependencies were removed as well, giving 29 Client targets.
The unrelated live `PitFireTeamCompat.PluginGuid` remains.

All seven Server candidates were removed with their live replacement overloads
and state machines intact. Useful XML comments were retained on the live
bot-tier overload; the ladder comment now describes the active Redraw path.

No persistence/profile protection, wire/config data, legacy `endless` migration,
framework/Harmony/Unity entry point, test fixture or regression assertion was
deleted. The separately test-used restore-gate `IsPoisoned` remains. Historical
manual audio tooling was not classified as globally dead or removed.

Fresh semantic Client analysis: **398 sources, 179 references, 8,319 symbols,
zero compilation errors**. Independent Roslyn comparisons reconstruct each exact
removal and verify that **all retained non-comment C# tokens are identical**.
Client comparison was repeated against `53803de` after the separate menu-copy
fix below; Server comparison remains pinned to `6827829`. Source diffs were also
read independently and `git diff --check` passed.

## Separate existing menu-check defects

Commit **`53803de`** repairs issues exposed while running the affected harness:

- The standalone TDM/menu project linked `ArenaFirePolicy` but omitted the
  already-used Shared `ArenaMolotovItem` constant, causing CS0103. Added that
  source link; no substitute implementation or weakened assertion.
- Two descriptions still expected companions-only Zombie refill, superseded
  by the October 15 surviving-PMC entry-gear restoration. They now assert that
  everyone is included, retaining the defeat/fallen-member checks.
- The actual PFT description was 83 characters against the unchanged 80 limit;
  it is now 76. The Molotov description also exceeded its unchanged 36-character
  budget and now reads `One extra Molotov; G selects first.` No layout or game
  behavior changed. Character budgets do not certify native font rendering.

The corrected harness passes **2,494 assertions**. A compiled negative control
restoring only the old companions-only wording fails the intended behavioral
assertion; the production inputs were rebuilt and passed afterward. No test was
deleted, disabled or loosened to make the dead-code cleanup pass.

## Final affected checks

Full `ArenaMode.Tests`, Release, with the installed item database and deploy
targets disabled: **969 passed, 1 skipped, 0 failed** (970 total). The skipped
test is the separately opt-in installed-APBS integration requiring
`ARENA_APBS_TEST_ROOT`; no APBS-native integration result is claimed here.

All sixteen additional direct harness executables passed:

| Harness | Assertions |
|---|---:|
| DeathmatchChecks | 163 |
| DeathmatchMenuChecks | 35 |
| PitFireTeamChecks | 340 |
| PitFireTeamRoundLifecycleChecks | 500 |
| TeamArenaExpansionChecks | 893 |
| TeamArenaLoadoutChecks | 142 |
| WeaponVariantChecks, including real installed item/kit coverage | 23,060 |
| RaidInfoPanelChecks | 368 |
| ArenaCharacterPreviewChecks | 52 |
| SpectatorHealthChecks | 49 |
| MolotovChecks | 138 |
| ZombieTeamChecks | 176 |
| ZombieSoloChecks | 85 |
| ZombieWavePendingChecks | 49 |
| TdmMatchMenuChecks | 2,494 |
| KitPolishChecks, including real front-armor coverage | 303 |

These are direct executable checks, not a claim to have rerun every project's
full compiled-mutation matrix. Raw final logs, TRX, return codes, token verifiers,
the menu negative control and the guarded runner are in the ignored local
`artifacts/dead-code-removal-20261016/` directory. The first failed run is kept
separately; it is not counted as final passing evidence.

## Build and deployment boundary

`tools/MolotovItemChecks/Build-Staged.ps1 -SptRoot D:/SPT41` passed for Client,
Server and BigBrainCompat, with **zero errors**. Existing warnings remain:
four Client CS8632 annotations in Shared settings, Server CS8620 in bot hostility
and MUD0002 for the Blazor PanelClass attribute. BigBrainCompat has no warnings.

The staged manifest is
`artifacts/double-jump-build/8ddd3637e2554dbf9ca45c2c6ff0e7d3/manifest.json`.
It records `53803de` plus the then-uncommitted cleanup source list; no production
source changed between this successful build and the cleanup commit. Later
documentation changes do not alter the built code. These staged binaries are
local verification artifacts, not a deployed/release package.

| Staged assembly | SHA256 |
|---|---|
| Client | `DA928512DF2A62F49CF018D8FC98B866A9B9FD1E499CB1E5BD022B580A18B192` |
| Server | `9BD8344DF26AA7B66C1AA2522534AB94C7C3F9698ED9F824BFABBDFEDFBA9B90` |
| BigBrainCompat | `C1A5432CA6CFAA852C40464289238110CE9F45580158C65D90728150F5E25B4E` |

Installed Client, Server, BigBrainCompat and Redeploy.Core hashes were checked
before/after the tests and staged build and remained unchanged. No game/server
process or player profile was operated. Offline/static success is not native
gameplay or visual acceptance, and cannot rule out unknown external reflection
consumers of internal APIs. The existing GitHub repository remains private.
