# Arena SAIN extraction guard checks

`ArenaSainExtractGuard` (ArenaMode.Client/Bots) answers false from SAIN's
`BotExtractManager.IsBotAllowedToExfil(BotComponent)` during an armed or exact active Arena
raid, so no bot leaves an Arena match through a map exit even when the SAIN preset's
`SAIN_EXTRACT_TOGGLE` is on. Contract: docs/reviews/EXTRACTION-HARDENING-20261113.md.

```powershell
pwsh -NoProfile -File tools/SainExtractGuardChecks/Verify.ps1            # baseline, mutants, installed contract
pwsh -NoProfile -File tools/SainExtractGuardChecks/Verify-Contracts.ps1  # installed SAIN.dll only (Mono.Cecil, read-only)
```

The program links the production adapter and `ArenaSainVersion.cs` into an executable that
deliberately carries SAIN's assembly identity (4.5.1.0); it is a stand-in, never deployed. A
fake Harmony records the hook, and each check calls SAIN's stand-in method and then the
registered production postfix, as Harmony would. Drifted SAIN builds (older version, changed
return type, missing method) are emitted as dynamic assemblies.

Coverage (33 assertions): one last-priority postfix and no prefix; SAIN's own method still
runs; refusal in the exact active raid on every Arena map and in the armed loading phase,
with case-insensitive map matching; SAIN's own refusal is kept; normal raids, a leftover map,
a stale active session (game or world), another map, a missing map, world or game keep SAIN's
answer; shutdown and rebinding; a failed installation and every drifted build leave SAIN
native with one bounded warning; a newer SAIN with the same contract binds.

Mutants (each must fail its named assertion): `guard-ignores-arena` (the unguarded
behaviour), `stale-active-raid`, `map-not-checked`, `foreign-postfix-can-reopen`,
`version-unchecked`.

`Verify-Contracts.ps1` fails when the installed SAIN changes what the guard relies on: the
admission method's shape and that it writes no field; `ExtractLayer.IsActive` requiring
`allowedToExtract`, which reads the toggle before it asks the admission method (IL order, not
the short-circuit itself); every
`OnItemTransferred`/`Proceed`/`RemoveFromMap`/`DoLeaveExternal` call in SAIN living in
`ExtractAction`; and `ExtractLayer.GetNextAction` being its only selector. It skips with a
message when no SAIN is installed under `-SptRoot`.

Limits: no Unity, BigBrain or Harmony detour runs here. A native check needs the toggle on in
a spare installation (see the contract's "what a native test needs").
