# Labs bot spawn checks

Run from the ArenaMode repository:

```powershell
dotnet run --project tools/LabsBotSpawnChecks/LabsBotSpawnChecks.csproj -c Release
pwsh -NoProfile -File tools/LabsBotSpawnChecks/Verify-Regression.ps1
```

This console harness compiles the actual `ArenaBotSpawnFilter`,
`ArenaBotSpawnPatches`, and shared `ArenaBotSpawnPolicy` sources. It invokes the
private production Harmony prefix/postfix methods by reflection. Game, Unity,
Harmony and logging types are explicit stand-ins; no game, GPU, server, profile
or deployment is needed.

The checks cover:

- Registration and exact signatures of all three bot hooks against stand-ins
  copied from the installed game's decompiled method declarations.
- Armed/loading and active Arena Labs, ordinary raids, stale map state with both
  Arena flags off, and other Arena maps.
- Removal of Gate1/Gate2 while keeping all three normal Labs zones. Outside the
  restricted session, the original enumerable and list objects pass unchanged.
- Direct AI selection of a gate, mixed selector results, and forced dispatch
  through the final bot-only boundary. Rejection keeps whole batches paired
  with their profile counts rather than shortening caller-owned lists.
- Unchanged point identity, category, blocked status, cooldown, core ID,
  position, and shared player collection. Player-tagged markers passed into a
  bot dispatch are still rejected at the bot boundary without editing them.
- Null results, safe fallback requests, and reuse of shared creation data after
  a rejected request.

`Verify-Regression.ps1` separately disables exclusions in a temporary copy of
the shared policy and disables the session guard in a temporary copy of the
client filter (with the real shared policy). Both must fail at the armed/loading
zone-filter boundary, before direct policy assertions. The script then reruns
the unchanged production checks. Source files are never overwritten;
temporary mutation/build evidence stays in the printed directory.

Limitations: this does not execute Unity, Harmony detours, actual spawn cooldown
updates, live asynchronous bot creation, AI navigation or player spawn selection.
The accounting stand-in observes whether the production prefix allows the
native boundary; it does not claim to reproduce the native bot lifecycle. Real
method binding must also be checked against the deployed DLL, and a raid remains
necessary to validate actual placement and normal-player behavior.
