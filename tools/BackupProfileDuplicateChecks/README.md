# Backup profile duplicate checks

Run `pwsh -NoProfile -File tools/BackupProfileDuplicateChecks/Verify.ps1` from the repo (or the harness through
`tools/Run-ArenaVerification.ps1 -Harness BackupProfileDuplicateChecks`). Nothing here builds into or touches the game.

The program compiles the production `ArenaBackupProfileDuplicatePatch` and `BackupProfileDuplicatePolicy` against the
stand-ins in `Stubs.cs`. The stand-in `BotProfileClient.LoadedByBackup(List<Profile>)` is patched with real Harmony
(Lib.Harmony 2.3.3, so the project targets net8.0: that Harmony version fails on the net10.0 runtime) and, like the game's
callback, appends whatever it receives to the pool. The checks therefore see what the original really receives.

Covered: a reload the pool holds leaves the callee an empty list and the pool unchanged; a reload nothing of which is
pooled is passed on as the same list object; a mixed reload keeps the unpooled profiles in order; identity is the object
reference (two distinct profiles that compare equal are both kept); every case outside the exact active raid (inactive
session, another game or world, another profile client, a game that is not a LocalGame, an empty pool, a null or empty
list); an exception inside the prefix leaves the argument untouched and is reported once per raid; the Harmony target,
the `__0` by-ref binding, `EnablePatch` never throwing, and the lines in `Plugin.cs` and `ArenaRespawnDriver.cs`.

Not covered: the game's own `BotProfileBackuploader`, the draw from the pool and `BotCreationData.Create` (the mechanism is
read from the decompile and measured natively, not run here). The `IsActive` test is proved by an inactive session that
reads none of the game singletons (the stand-in singleton throws on read), because the real `IsExactActiveRaid` also
requires `IsActive` and the test would otherwise be redundant in behaviour. The guards `reloaded.Count == 0` and
`pool.Count == 0` are equivalent to the policy's own result (no duplicates, nothing replaced) and have no mutant.

`Verify.ps1` runs the baseline and then the mutants through `Test-ArenaSourceFaults`; each must fail the assertion it names.
