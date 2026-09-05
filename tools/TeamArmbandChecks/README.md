# Team DM armbands

Team DM generated bots use native USEC blue (`5b3f3af486f774679e752c1f`)
and BEAR red (`5b3f3ade86f7746b6b790d8e`) armbands. APBS settings, bot
templates, human gear, protected PFT gear, and other Arena/normal modes are not
changed. These are faction colors, not arbitrary tracer palette colors.

The patch surrounds **synchronous** `BotGenerator.PrepareAndGenerateBot`.
Its inner `GenerateBot` finishes `BotInventoryGenerator.GenerateInventory`
(including APBS's replacement and its `GenerateBot` postfix) before returning.
The outer `BotController.TryGenerateSingleBot` only then changes PMC side to
Savage and caches the bot. Native `BotProfileClient.CreateProfile` loads the
returned profile's `GetAllPrefabPaths` bundles before activation, so the cloth
armband is included in the normal resource load. No client inventory operation
or late asset creation is needed.

The prefix captures the exact live Arena session/map/mode's arm token. The
postfix rechecks that token and excludes protected PFT, player-scav, non-PMC,
event-role and inconsistent role/side generation. It replaces only the returned
bot/Inventory/list with a new list and a new cloth item; existing item objects
and generator aliases are not changed. If a mod uses ArmBand for a container,
that bot-only container and its contents are replaced together to avoid orphan
items. All other slots are preserved. Malformed roots/IDs fail open.

Evidence inspected locally (SPT 4.1 / installed APBS, September 2026):

- `../_reference/server-csharp-main/Libraries/SPTarkov.Server.Core/Generators/Bot/BotGenerator.cs`,
  `PrepareAndGenerateBot` and `GenerateBot`.
- Same tree `Controllers/BotController.cs`, `TryGenerateSingleBot`.
- `../_reference/progressivebotsystem-csharp-master/Patches/GenerateInventory.cs`
  and `GenerateBot.cs` (technique review only; no source copied).
- `../_decompiled/BotProfileClient.cs`, returned profile prefab loading at line 202;
  `../_decompiled/EFT/LocalGame.cs:163` creates a fresh profile client per raid.
- Installed SPT/APBS assemblies and native item template/prefab data, checked by
  `Verify-Contracts.ps1` without executing mod code or changing security metadata.

Run from the repository, using PowerShell 7:

```powershell
pwsh -NoProfile -File tools/TeamArmbandChecks/Verify-Contracts.ps1
pwsh -NoProfile -File tools/TeamArmbandChecks/Verify-Regression.ps1
```

The latter uses actual server policy, patch and session fixtures. Four temporary
source-replacement mutants must fail their intended assertions, followed by the
unaltered production armband, team setup and tier-context tests. Every test build
sets `SPTUserMods` to a verified nonexistent scratch path; nothing is deployed.
Scratch files are retained for diagnosis. Neither check proves cloth visibility
in Unity; inspect newly spawned bots of both factions in a GPU playtest after a
server restart.
