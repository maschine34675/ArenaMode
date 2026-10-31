# Ported-map PFT cover checks

Run through the verification runner from the repository:

```powershell
pwsh -NoProfile -File tools/Run-ArenaVerification.ps1 -SkipUnitTests -Harness PortedCoverChecks
```

The program links the production adapter
`ArenaMode.Client/Compat/PitFireTeamPortedMapCombat.cs` against minimal game and Harmony
stubs; it neither builds nor deploys the client. `Verify.ps1` runs the unmodified
production, then every compiled fault in its list (each must fail at its named assertion),
then production again. Background: [PORTED-COVER-REVIEW.md](../../docs/reviews/PORTED-COVER-REVIEW.md).

## Source contracts

The program also reads native and installed-mod sources; a missing source fails the run,
it is never skipped:

- `../_decompiled/` next to the repository (`BotPathFinderCorePoints.cs`, `CoverPointMaster.cs`).
- The installed PitFireTeam decompile, `pitTeam.Utils/Covers.cs`, resolved in this order:
  1. `<ARENA_PFT_INSTALLED_SOURCE>/pitTeam.Utils/Covers.cs`, when the variable is set;
  2. `artifacts/pft-installed/pitTeam.Utils/Covers.cs` in this repository (gitignored).

  When neither exists the run fails and names the script below. The run prints the file
  it used as `INSTALLED_PFT_SOURCE`.

Create or refresh the default decompile with

```powershell
pwsh -NoProfile -File tools/Decompile-PitFireTeam.ps1
```

It reads `D:\SPT41\BepInEx\plugins\pitFireTeam\pitFireTeam.dll` read-only (`-SptRoot` for
another install; the DLL's SHA256 is compared before and after), runs `ilspycmd -p` with the
game, BepInEx and SPT reference folders (without them ILSpy drops named arguments the check
reads) and writes `decompile.log` and `decompile-receipt.json` into the output folder. It
refuses a non-empty output folder: after a PitFireTeam update remove
`artifacts/pft-installed/` first, or pass `-OutputRoot` and point
`ARENA_PFT_INSTALLED_SOURCE` at it.
