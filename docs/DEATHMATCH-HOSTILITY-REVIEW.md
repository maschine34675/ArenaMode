# Deathmatch hostility negotiation

Implemented 2026-09-12 on `feature/deathmatch-menu-preview`. This is server and
protocol evidence, not live bot-AI acceptance.

## Frozen request contract

The canonical `deathmatch` raid-start request must carry `deathmatchHostility`
as exactly `ffa` or `all_vs_player`. The shared `DeathmatchSelection.TryResolve`
has no fallback for absent, empty, misspelled or differently cased values.
The mode id is case-insensitive like the existing replay identity. A supplied
choice on another mode is rejected; omitted/null remains the legacy contract
for `endless`, `rounds`, Gun Game, Tactical and independent Team DM.

The router validates the choice before existing-state replay, stale snapshot
recovery, PFT protection, arm preparation or profile capture. The session
service independently validates direct Arm/preparation/replay callers. Its
immutable entry stores the accepted value; same-token retries with another
choice cannot replay the accepted result or replace the entry. Initial/replay
responses and status echo the stored choice. Client acceptance must compare
that exact echo before releasing the native start; transport uncertainty still
belongs to the existing token-bound cancel/recovery protocol.

The value survives Preparing, Armed, InRaid and recovery/finalization, and is
cleared on each transition to Idle. Other sessions never expose it. No change
to profile snapshot contents, restore, native end identity or PFT persistence
guards is included here.

## Applying relationships

`ArenaAdjustBotHostilityPatch` reads mode and frozen choice under the same session
lock, and only for the exact reference-identical stashed live location clone.
Same-map objects, stale raids, preparing/armed entries and ordinary raids are
not eligible. The canonical Deathmatch branch never reads or edits mutable
`ArenaContext.Config.HostilityMode`; the legacy mode branches retain it.

Deathmatch overrides known map bot-role relations deterministically, ensuring
the four ordinary Arena roles (`pmcUSEC`, `pmcBEAR`, `assault`, `marksman`) have
entries even when the map omitted them. FFA makes the known bot roles enemies;
all-vs-player makes them friends. Human PMC sides stay unconditional enemies.
The role vocabulary also includes the source entries' other friends/enemies/
neutral/warn/chanced roles, so conflicting source relations cannot survive.
Native `BotSettings.GetEnemyBotTypes` adds enemies then removes friends,
neutral, warn and chanced roles: all conflicting lists must be cleared, rather
than merely raising chance percentages. Verified against
`../_decompiled/BotSettings.cs`, `GetEnemyBotTypes` and player-behavior methods.

Every edited record and nested collection is replaced. SPT's earlier native
rewrite can insert `pmcConfig` records by reference, so writing those objects
would leak rules into subsequent ordinary raids. Team DM's established faction
policy and the old non-Deathmatch behavior are unchanged and covered by their
existing real server tests.

Server records alone do not override native same-group/same-role checks.
`BotsGroup.IsPlayerEnemy` returns before reading backend enemy roles when
`IsSuitable` matches, and native wave spawning can create squads. Client-side
Deathmatch relationship/group handling is integrated separately by the parent
task. Native `BotCreatorClient.method_3` invokes the group callback once per
activated bot. A group lock alone on ordinary scav groups is insufficient:
`BotZoneGroups.Set` overwrites the per-role slot; the native `_freeForAll` path
registers those with `AddNoKey`. That registry's `GetGroups`/`AddPlayer` behavior
also needs explicit consideration when preserving old groups and respawns.
No external mod's implementation is copied, and no boss/wave configuration or
native constructor is replaced by these server changes.

## Offline evidence

Run `pwsh -NoProfile -File tools/DeathmatchHostilityChecks/Verify.ps1`.
The script redirects server/test output and `SPTUserPath` into a fresh ignored
artifact directory and checks the installed server DLL hash afterwards. It
does not deploy. Tests use the real route, snapshot capture and session service
with an isolated in-memory profile; native Unity/EFT behavior is not mocked as
server acceptance.

Initial run: **55 passed, zero skipped**. Includes exact request/status/replay,
invalid fresh/stale/armed requests without snapshot/restore side effects,
all four lifecycle endings, opposite global setting, missing role entries,
repeat application, detached source collections, exact object identity and
staleness. Existing Team DM server tests exercise unchanged faction policy and
legacy all-vs-player/FFA behavior.

Two production negative controls compile replacement source only inside the
staging directory, without changing the working source: removing the frozen
hostility replay comparison causes both replay tests to fail; removing the
exact-clone identity check causes the different-object case to fail. A compile
or discovery error cannot count as a successful negative control; actual failed
test results are required. A receipt and TRX files record every run under
`artifacts/deathmatch-hostility/<run>/`.

An independent read-only review by the `deathmatch_runtime` agent found no
actionable defect in validation order, arm/replay identity, idle cleanup or
clone/source isolation. It confirmed the existing null-modifier dependency and
the separate client same-group requirement. The latest self-check receipt is
`artifacts/deathmatch-hostility/267417881f8045dd81f401621b21a267/receipt.json`.
Parent re-verification/manual acceptance, integrated client behavior, packaged
hashes and GPU acceptance remain separate gates. No in-game claims follow from
these checks.
