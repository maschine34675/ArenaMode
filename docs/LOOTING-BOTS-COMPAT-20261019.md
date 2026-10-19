# Looting Bots: Arena-only compatibility

## Policy and integration

Looting Bots is optional. Arena pauses autonomous looting for its own combatants
without changing Looting Bots settings, the normal PMC/Scav behavior, third-party
Harmony patches or server data. TDM pauses both teams. Authenticated PFT companions
in Tactical/Zombies retain their commanded looting entries. The 1.8.1-RC automatic
spawn inventory cleanup is separately blocked for those companions as well, so
it cannot rearrange their entry gear before the first command.

`ArenaLootingBotsCompat.Initialize()` installs checked, process-lifetime prefixes;
`GetRejectionReason()` rechecks optional-plugin identity and hook ownership for
Arena admission; `Shutdown()` first revokes the contract, then unpatches only its
own Harmony owner. No Tick hook or per-raid restoration handler is required.
Parent integration owns initialization, menu/final-start admission and shutdown.

An absent plugin is allowed. A malformed, incomplete or failed contract rejects
Arena only, with a player-visible reason. Normal raids keep running the original
foreign methods. Newer versions are attempted with checked identity/signature
contracts and one warning, not a claim of native compatibility. A newly thrown
runtime identity or scheduler exception latches a rejection for subsequent Arena
admission and logs a warning; it cannot undo work already performed in the current
raid. No hot-load recovery or transaction rollback is claimed.

## Sources and pins

The current SPT 4.1 source is **ozen-m/SPT-LootingBots**, not the older Skwizzy
repository. No foreign source or binary is included in the deliverable. Source
and published-binary inspection used ignored local audit artifacts.

| Input | Pin |
|---|---|
| [Forge release page](https://sp-mod.com/mod/812/looting-bots) | 1.8.0, SPT 4.1.6 |
| [Stable source/release](https://github.com/ozen-m/SPT-LootingBots/releases/tag/v1.8.0-spt-4.1) | `c6f21614e3feb4df11b50cbd4f2ed2e0e81ca015` |
| Stable client DLL | `22b4a0845d3971de935456e7df3f387c92fb1a864604e7e52931a9feab3cbfc4` |
| [1.8.1-RC1 source/release](https://github.com/ozen-m/SPT-LootingBots/releases/tag/v1.8.1-spt-4.1-rc1) | `f6e126aa764cc25551a8ff0f9c443339af4a6027` |
| RC client DLL | `6ddc3124de468976bd3bec9d0351d3ad0596e739f7fb0054f19c1e7f9913336c` |
| Plugin identity | `me.skwizzy.lootingbots`, assembly `Skwizzy.LootingBots` |

The adapter checks the plugin attribute, Chainloader metadata, assembly name and
assembly version together. Minimum is 1.8.0. It requires the actual BigBrain layer,
Unity component inheritance, exact EFT owner/inventory field types and all guarded
method signatures/bodies. Init/Start must remain synchronous. Versions 1.8.1+
must expose the coherent OnSpawnAsync/CleanupInventoryAsync/Init path; a half
present or renamed path is rejected. Matching method names cannot prove that a
future release introduces no other producer; the advisory deliberately retains
that limit.

## Guarded seams

| Foreign entry | Arena action |
|---|---|
| `LootingLayer.IsActive()` | Return false for autonomous Arena combatants. |
| `LootFinder.BeginSearch(int ticket)` | Return that acquired scheduler ticket exactly once, then skip the search. |
| `LootingBrain.StartLooting()` | Skip creation of new async corpse/container/item looting work. |
| `LootFinder.EnqueuePriorityCorpse(string)` | Skip strong-reference queue admission while autonomous looting is paused. |
| `LootFinder.OnAirdropLanded(LootableContainer)` | Skip the corresponding container queue admission. |
| RC `LootingInventoryController.CleanupInventoryAsync(CancellationToken)` | Return an already-completed Task before mutation, also for PFT companions. |

The first five entries preserve PFT commandable followers after exact raid/body
proof. The RC entry is different: the audited source and complete published-DLL
caller scan find exactly one caller, `OnSpawnAsync.MoveNext`. Commanded looting
does not call it. The adapter does not skip OnSpawnAsync or Start; their finally
block still initializes inventory statistics/subscriptions and clears
LootTaskRunning.

Relevant source:

- [Layer constructor and arbitration, stable lines 19–45](https://github.com/ozen-m/SPT-LootingBots/blob/c6f21614e3feb4df11b50cbd4f2ed2e0e81ca015/LootingBots/LootingLayer.cs#L19-L45).
- [Scan ticket acquisition, stable lines 35–51](https://github.com/ozen-m/SPT-LootingBots/blob/c6f21614e3feb4df11b50cbd4f2ed2e0e81ca015/LootingBots/Logic/FindLootLogic.cs#L35-L51) and [scheduler return, lines 45–75](https://github.com/ozen-m/SPT-LootingBots/blob/c6f21614e3feb4df11b50cbd4f2ed2e0e81ca015/LootingBots/Utilities/ScanScheduler.cs#L45-L75).
- [Search and corpse queue, stable lines 99–162](https://github.com/ozen-m/SPT-LootingBots/blob/c6f21614e3feb4df11b50cbd4f2ed2e0e81ca015/LootingBots/Components/LootFinder.cs#L99-L162); [airdrop/kill callbacks, lines 629–642](https://github.com/ozen-m/SPT-LootingBots/blob/c6f21614e3feb4df11b50cbd4f2ed2e0e81ca015/LootingBots/Components/LootFinder.cs#L629-L642).
- [RC OnSpawnAsync, lines 242–270](https://github.com/ozen-m/SPT-LootingBots/blob/f6e126aa764cc25551a8ff0f9c443339af4a6027/LootingBots/Components/LootingBrain.cs#L242-L270) and [inventory cleanup entry, line 248](https://github.com/ozen-m/SPT-LootingBots/blob/f6e126aa764cc25551a8ff0f9c443339af4a6027/LootingBots/Components/LootingInventoryController.cs#L248).
- [Public PreventBotFromLooting API, stable lines 53–72](https://github.com/ozen-m/SPT-LootingBots/blob/c6f21614e3feb4df11b50cbd4f2ed2e0e81ca015/LootingBots/External.cs#L53-L72): finite timer, can report success while the performance-disabled brain does no work, no transaction settlement guarantee. It is not used as the sole protection.

Arena's existing `ArenaNoCorpseLootingPatch` targets native
`BotDeadBodyWork.UpdateCheck`. Looting Bots' custom layer and async inventory
transactions bypass that native decision point, which is why a separate adapter
is necessary. Disabling a layer alone also misses the RC spawn cleanup and
kill/airdrop queue callbacks.

## Exact raid/body boundary and first-frame ordering

Every entry recomputes its scope; there is no cached profile-ID or map-only lease.
It requires the current native LocalGame and its exact singleton GameWorld,
PMC settings, matching settings/world map and Running/Starting/Started status.
Authority is either Arena's exact active game/world pair or the armed map plus
pending valid arm token carried by the native **END** identity. A start request
marker or merely selecting Arena is insufficient.

For each bot it further requires the actual AI Player, matching `AIData.BotOwner`,
the game's `IBotGame.BotsController` and an exact physical-body lookup from that
world. Unity components must belong to the same player GameObject. The RC
inventory controller must be the player's actual native InventoryController.
Old bodies with a reused profile ID cannot borrow a replacement's authority.

The local SPT 4.1 native source establishes the early ordering:

1. `D:\SPT41\Development\_decompiled\EFT\BaseLocalGame.cs:519–525` sets
   Running and LocationId before loading/spawning the bots.
2. `EFT\LocalGame.cs:335–344` and `BotCreatorClient.cs:135–141` await
   LocalPlayer creation before creating BotOwner.
3. `EFT\Player.cs:28272` registers the physical player during Init.
   `EFT\GameWorld.cs:2012–2032` stores the exact Player in the ID lookup.
4. `EFT\BotOwner.cs:480–483` sets AIData owner, controller and Player;
   Brain.Activate only follows at line 991. BigBrain CustomLayerWrapper then
   constructs LootingLayer, whose synchronous constructor initializes both
   components before Unity Start dispatches the RC OnSpawn task.

Consequently the guard does not wait for Arena IsActive, the mode roster, a
subsequent update tick or an Active bot state. It protects both native initial
bodies and replacement activations using the same physical proof.

PFT exemption additionally requires an authenticated `TeamArenaActive` roster,
Tactical/Zombie mode and that exact bot's frozen companion account. Historical
accounts alone grant nothing. The account-list getter is only consulted on those
PFT paths, avoiding its sorting/allocation cost in DM/TDM. PFT already has its own
[peace-behavior handling](https://github.com/pitAlex/SPT-PitFireTeam/blob/fdbc24e51e8433da53097a80da0247a388b44db1/client/Components/BotFollowerPlayer.cs#L3454-L3485);
Arena does not override its command policy or its saved configuration.

## Validation and limits

Run `pwsh -NoProfile -File tools/LootingBotsCompatChecks/Verify.ps1`. The isolated
harness links all three production files and the real arm protocol; it exercises
the installed prefixes with positive ordinary-raid controls, loading/active and
replacement cases, PFT commands versus spawn cleanup, malformed contracts,
failure rollback and same-map repeat raids. Twelve separately compiled mutants
must fail their intended behavioral assertions. Published stable and RC DLLs are
inspected without executing or installing them when available.

Leaf verification on 2026-10-19 passed **437 assertions**, all **12 compiled
mutants**, and both published DLL metadata checks. Evidence:
`artifacts/looting-bots-checks/0f1670178ead4e898022ca70a411103a/`.
The command printed `LOOTING_BOTS_COMPAT_VERIFIED`; parent integration/build and
native acceptance are separate gates.

This is source, published-metadata and compiled-policy evidence. It is not native
Unity/Harmony acceptance. Required native checks remain: ordinary PMC then Arena
then ordinary PMC in one process; first activation and replacement bots with
Looting Bots; PFT corpse commands and RC entry-gear preservation. Existing async
foreign transactions cannot be safely rolled back by a later config toggle or
timer; protection is installed before the confirmed Arena world starts them.
Unexpected future direct transaction producers remain a source/native review
obligation.
