# MoreBotsAPI Arena compatibility — 2026-10-19

This adapter is optional and Arena-only. Ordinary PMC/Scav raids retain MoreBotsAPI's
spawning, hunt dispatch, faction hostility, revenge recording and end sender. It
does not edit another mod's configuration, ship its code/DLLs, or add server routes.
Source review and offline checks are complete; native gameplay acceptance remains
separate and has not yet been performed for this adapter.

## Reviewed contracts and failures

Arena baseline: `c825892e25f042848b82695925f26b881abeda8c` plus this compatibility
change. MoreBotsAPI release **2.1.1**, commit
[`0986311f75279fc4e6d98228ff2ecf89a8ad4d93`](https://github.com/TacticalToaster/MoreBotsAPI/tree/0986311f75279fc4e6d98228ff2ecf89a8ad4d93),
client assembly `MoreBotsPlugin`, plugin `com.morebotsapi.tacticaltoaster`, type
`MoreBotsAPI.Plugin`. The separate prepatcher is not bundled or modified.

Three independent issues require protection:

1. [`HuntManager.OnBotCreated`](https://github.com/TacticalToaster/MoreBotsAPI/blob/0986311f75279fc4e6d98228ff2ecf89a8ad4d93/Plugin/Components/HuntManager.cs#L37-L45)
   dereferences `SpawnParams.Id_spawn` without a null check. Both Arena burst
   creation paths previously supplied a default/null marker. Native BotSpawner
   invokes this subscriber **before** `AfterCreation`, the caller's arrival
   callback and spawn-counter settlement, so the exception can strand activation.
2. [`FactionManager.OnKill` and `SendRevenges`](https://github.com/TacticalToaster/MoreBotsAPI/blob/0986311f75279fc4e6d98228ff2ecf89a8ad4d93/Plugin/Components/FactionManager.cs#L121-L153)
   collect human kills synchronously and send `/morebotsapi/updaterevenge` at
   [native raid end](https://github.com/TacticalToaster/MoreBotsAPI/blob/0986311f75279fc4e6d98228ff2ecf89a8ad4d93/Plugin/Patches/FactionRaidEndPatch.cs).
   The end patch has no PMC, end-status or Arena filter. Even an empty update
   [decrements existing revenge and saves external profile data](https://github.com/TacticalToaster/MoreBotsAPI/blob/0986311f75279fc4e6d98228ff2ecf89a8ad4d93/Server/Services/FactionService.cs#L80-L120)
   for profiles active within ten minutes. Release 2.1.1 uses SPT's
   `ProfileDataService`, key `MoreBotsAPI`, resulting in
   `user/profileData/<profile-id>/MoreBotsAPI.json`. Arena's SaveServer/SptProfile
   snapshot cannot restore this separate file. Default factions disabling revenge
   do not prevent that empty-request save/decrement.
3. [`BotsGroupIsPlayerEnemyPatch`](https://github.com/TacticalToaster/MoreBotsAPI/blob/0986311f75279fc4e6d98228ff2ecf89a8ad4d93/Plugin/Patches/BotsGroupIsPlayerEnemyPatch.cs)
   can turn an otherwise friendly group's relationship with the human hostile
   because of an ordinary raid's persistent revenge. Arena's protected PFT ally
   branch deliberately preserves its native friendly result, so it cannot safely
   accept this foreign override during Arena.

MoreBots is consequently not treated as a passive API or globally blocked. Its
reviewed seams are adapted, while Arena fails admission when that protection
cannot be validated and installed.

## Ownership and lifecycle

`ArenaMoreBotsCompatibility` validates runtime GUID, metadata version, declared
BepInPlugin identity, assembly/type identity, and all required reflected method
and queue-field shapes. Minimum reviewed version is 2.1.1; newer versions attempt
the same contract and produce one warning, not a claim of future compatibility.
All four Harmony prefixes require actual owner/method receipts. A partial install
is rolled back and rejects Arena before a kit is issued. Absence is unrestricted.
If Harmony itself fails to remove hooks, readiness is revoked before cleanup:
remaining prefixes are inert, ordinary raids continue unchanged, and the original
Arena refusal is retained. Startup/shutdown does not propagate that cleanup
exception. Its warning is emitted at most once per process.

`ArenaLocalRaidStartedPatch.ObserveLocalStart` binds the **exact** settings object
only after the server's native-end identity is accepted. The lease includes both
protocol tags, arm token, returned server identity, map, selected side and local
profile ID. `ArenaMoreBotsRaidPolicy` uses weak keys, not a global "last raid was
Arena" bit. End callbacks remain protected after Arena's active flag or live world
is cleared, including delayed/repeated callbacks while a later ordinary raid runs.
A copied settings object, wrong end identity, map or side never inherits the lease.

The native reference chain has been checked in `../_decompiled/`:

- `EFT/TarkovApplication.cs:2983–3004` creates one `LocalRaidSettings`, passes it to
  `LocalRaidStarted`, writes the returned server ID on that same object and passes
  it to `LocalGame.Create`.
- `EFT/LocalGame.cs:139–142` forwards the object; `EFT/BaseLocalGame.cs:442` stores
  it in `_raidSettings`, and `:1089` passes it to `LocalRaidEnded`.
- Redeploy Core's `LocalGameAccessor.GetLocalRaidSettings` reads that field.
  Respawn swaps physical bodies and does not replace the native settings object.

The producer prefix also requires the current LocalGame and GameWorld to agree
and expose that bound settings object. It suppresses only the bound local profile's
revenge producer. It does not depend on `ArenaSession.IsActive` or a present
MainPlayer, so a grenade kill during body replacement remains protected. The
reviewed MoreBots OnKill producer is synchronous; there is no queued/delayed
producer to drain into the next ordinary raid. The sender prefix suppresses the
exact protected request independently of the live world.

**The foreign revenge queue is never cleared or rewritten.** A pending ordinary
update must survive an Arena interlude or late Arena end. Arena prevents its own
entries from being added; the next genuine ordinary sender retains normal behavior.
No side-route block was added: the MoreBots payload contains no raid identity, so
session-wide/last-Arena route suppression would wrongly affect legitimate updates.
An independent foreign mod sending that route directly is outside this reviewed
producer/sender contract.

## Spawns and faction relationships

Both Arena-owned `BotSpawnParams` constructors now set `Id_spawn` to
`arenamode-owned`. This non-hunt identifier avoids the null dereference without
joining MoreBots hunts. No foreign/PFT spawn record is rewritten.

PFT requires an additional boundary. Installed `pitFireTeam.dll` 0.10.3.0 has
SHA256 `0f875f824215f68a47ffd9183fb0bdd92cba72762e1702f458a0ee614cc6dbb9`.
A read-only Mono.Cecil scan found seven BotSpawnParams constructors and **no**
store to `BotSpawnParams.Id_spawn`; its group creator calls the native activation
callback. The reference `SPT-PitFireTeam-main/client/Patches/BotsControllerPatch.cs`
shows the same missing marker at 962–965 and native activation at 1115–1119.
The Arena-only HuntManager prefix therefore skips the foreign handler when:

- the exact current LocalGame/GameWorld/settings belongs to the protected lease;
- BotSpawnParams exists and its hunt marker is null or empty;
- the owner refers to an AI Player, its AIData points back to the same owner, and
  GameWorld's native registry resolves the profile ID to that exact body.

Nonempty markers, explicit hunts, unknown bodies and all ordinary raids retain
the original handler. There is no PMC-role assumption: a valid PFT boss follower
needs the same null-marker safety. Active state is intentionally not required.
Native registration already exists at this seam: `LocalPlayer.Create:72` awaits
`Player.Init`, which registers at `Player.cs:28272`; `GameWorld.cs:2026` records the
body. Only afterward does `BotCreatorClient.cs:135–146` create BotOwner (including
AIData ownership, `BotOwner.cs:480–483`) and invoke the activation callback.
`BotSpawner.cs:999–1013` then invokes OnBotCreated before AfterCreation.

The separate enemy-postfix prefix skips MoreBots' revenge override only in the
exact protected current raid. It changes no result itself; native and Arena team
rules remain authoritative. Ordinary PMC/Scav faction rules are untouched.

## Checks and limits

Reproduce the source-linked policy/prefix checks and compiled fault controls:

```powershell
pwsh -NoProfile -File tools/MoreBotsCompatibilityChecks/Verify.ps1
pwsh -NoProfile -File tools/SoloPopulationChecks/Verify.ps1 -BaselineOnly
pwsh -NoProfile -File tools/TeamDeathmatchRuntimeChecks/BurstIntegration/Run.ps1
```

The MoreBots harness uses authored signature-only fixtures and a controlled
Harmony boundary. It executes the production adapter/policy and extracts both
production spawn constructors. It does not load MoreBots, simulate Mono's detours,
or certify native bot/PFT behavior. It covers ordinary PMC and Scav → Arena →
ordinary; persistent ordinary queue preservation; repeated and late Arena end;
identity/map/side/profile mismatches; body replacement; registered-body/owner
identity; null/empty/custom/hunt markers; missing/partial patch installation;
unsupported or mismatched metadata. Each compiled mutant must fail its intended
behavioral assertion, not merely fail compilation.

Final MoreBots run: **93 assertions and 17 rejected compiled mutants**, evidence
under ignored `artifacts/morebots-checks/09e3becde31a4acb93aa734210502afa/`.
Mutants cover both lost spawn markers, lost revenge producer/end protection,
live-world-only end ownership, reused identities, missing installation receipts,
missing hunt/enemy hooks, registered-body and explicit-hunt mistakes, wrong-world
admission, the revenge relationship override, escaping cleanup exceptions,
unbounded cleanup warnings, and missing readiness revocation after failure.
Cleanup tests leave real fixture patch receipts installed and verify that every
remaining production prefix is inert for ordinary PMC/Scav and previously bound
Arena settings; both failed initialization rollback and shutdown are exercised.

The SoloPopulation check passed 141 source-linked assertions and 17 native-IL
contracts against installed Assembly-CSharp SHA256
`ee25cee1259777b38ed8b3e7841fdc2db3c98540b1469fa539b1ff183476e436`.
The TDM burst integration check passed 93 assertions and all compiled fault controls.
Parent integration separately passed 752 NativeMenu checks, including failure
before kit issue and ordinary start bypass. Central full-client/server builds and
native acceptance are reported separately, not implied by these offline checks.

Native acceptance should exercise a normal PMC control, protected Arena kills
with a configured revenge faction, an Arena end after respawn, PFT follower
activation (including friendly behavior), then another normal PMC/Scav control.
Compare the external MoreBots profile-data file before and after Arena and ensure
ordinary revenge still updates. No native test or deployment was performed by
this adapter's implementation task.
