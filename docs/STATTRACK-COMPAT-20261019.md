# StatTrack compatibility

2026-10-19. Optional client adapter for StatTrack **2.1.1+**. Ordinary raids,
existing statistics, already pending requests, menu loads and insurance delivery
remain under StatTrack's control. Arena does not include StatTrack binaries or
foreign source. This is an isolation adapter, not an Arena weapon-statistics UI.

## Reviewed boundary

Primary source: [acidphantasm/stattrack-csharp](https://github.com/acidphantasm/stattrack-csharp),
commit `3e46c819bd22a7b95bf1e8df22daf8249e615d69`, and the author's released 2.1.1 package.
The ignored audit copy is under `artifacts/forge-top200-20261019/leaf-3/source/stats`.

| Published component | SHA256 |
|---|---|
| Client | `837D8E099379A839CB707AD366C1BCF1ABD0D6704EBB5BA4C253322639BBFDC0` |
| Server | `9E46661AEB51593D7FC74967AB6C7A4029B4BE4CC541B2C3084074CD43B0C131` |

The client plugin GUID is `com.acidphantasm.stattrack`; its BepInPlugin version is
2.1.1 although its assembly version is **1.0.0.0**. Admission accepts that known
legacy assembly version or an assembly version matching the plugin metadata,
never an arbitrary mismatched higher number. Newer plugin versions warn once
and must retain the checked method shapes, registered native Harmony seams and
collection/save call graph. This cannot certify arbitrary future semantics.
An absent StatTrack does nothing; an unrecognized loaded contract blocks only
Arena. No BepInEx-wide incompatibility is declared.

## Producers and persistence

| Foreign path | Native origin | Arena handling |
|---|---|---|
| `WeaponOnShotPatch.PatchPostfix` | `Weapon.OnShot` | Skip only the foreign statistic producer for the exact Arena-owned weapon/body. Native firing, ammunition and durability still run. |
| `PlayerPatch.PatchPostfix` | `Player.OnBeenKilledByAggressor` | Exclude an owned victim, aggressor, damage-source body or damage weapon. Native kill handling still runs. |
| `PlayerPatch2.PatchPostfix` | `Player.OnHealthApplyDamage` | The upstream code collects bot-versus-bot hits too. Check victim, damage-source body and weapon; do not rely solely on the local player. |
| `GameWorldPatch.PatchPostfix` | `GameWorld.UnregisterPlayer` | Skip the foreign merge for an owned body, including respawn retirement, repeated unregister and final teardown. Native unregister remains unchanged. |
| `WeaponPatch.PatchPostfix` | Weapon constructor | Attribute/tooltip creation remains enabled. It reads previously saved statistics. |
| `MenuLoadPatch.Postfix` | Backend builds request completion | Read-only StatTrack load remains enabled. |
| `InsurancePatch.Postfix` | Insurance-return message | Read-only StatTrack load remains enabled, including delivery while Arena is running. |

All three client producers call `TemporaryAddData` for both item instance and
template. The one merge caller is the unregister postfix. `EndRaidMergeData`
merges the global in-raid dictionary into permanent data, then starts an async
save. The save serializes its payload **before** awaiting `/stattrack/save` and
clears the in-raid dictionary **after** completion. A second Arena-triggered merge
would therefore be harmful even if Arena had not produced a single statistic:
it could re-merge a preceding ordinary raid still waiting on its save.

The adapter does not call any of those utility methods, replace/clear either
dictionary, cancel tasks, alter a route, or delay an ordinary request. A normal
raid's already captured save can complete during Arena, including its own native
clear. Since Arena producers and Arena merges never enter the foreign utility,
that clear cannot consume Arena data or require an Arena-specific queue repair.
A following ordinary raid uses the original collection/merge/save path.

Runtime validation scans all managed methods, including nested state-machine
types. It rejects new callers of `TemporaryAddData`, `EndRaidMergeData` or the
save method. Persistent-field and dictionary-accessor uses are limited to the
reviewed utility operations and its exact save/load state machines; a new helper
or nested writer is not implicitly trusted. New foreign HTTP write producers
outside the reviewed save state machine are rejected as well. No foreign method
is executed during discovery.

## Exact ownership and early failures

The normal proof requires the current `LocalGame`, `GameWorld` and `IBotGame`
instances to agree, PMC settings/map identity, and either Arena's exact active
game/world pair or the server-confirmed pending arm token. A start-request marker
alone is insufficient. `OnGameStarted` captures all registered bodies even if
they never shoot, get hit or die. `RegisterPlayer` captures replacements.

There is a narrow earlier construction exception. `BaseLocalGame.Run` creates its
initial player while status is `Running`, before `BotsController.Init` publishes
`IBotGame` and before `OnGameStarted`. `LocalPlayer.Create` sets `IsYourPlayer`
and immediately calls `Player.SinglePlayerInventoryController(Player, Profile,
bool isBot, bool examined)`. Its prefix captures only that exact `LocalPlayer`,
native world, `game.LocalPlayer == null`, and constructor **Profile reference**
equal to `game.Profile`, with the same accepted arm/end identity. This happens
before quest setup or `Player.Init` can fail, when the body's own Profile field
may still be null. The four CLR parameters are checked against the installed
game DLL; optional C# call arguments do not shorten the runtime signature.

This matters because `Player.Dispose` calls `GameWorld.UnregisterPlayer` even
for an incompletely initialized body, and StatTrack's postfix runs even if the
native method returns early for an unregistered body. The constructor lease
prevents that failure from touching an ordinary pending merge. Post-registration
capture remains an independently tested fallback. An ordinary failed load with
no matching arm, a different profile object, a published local owner, another
world, another map or a Scav body does not gain this exception.

Ownership uses weak tables keyed by the physical body and weapon, never IDs or
templates. A body lease retains its original exact world after Arena/session
cleanup. A reused body whose GameWorld changes loses its old lease. Weapon
association uses `PlayerInventoryController.Player`, and a transferred weapon
loses its exemption when its actual owner becomes ordinary. Only an already
observed exact weapon can retain an orphaned/old-owner association for a delayed
shot. Copying its ID or template does not grant ownership. Shutdown releases
only Arena's own hooks/tables.

## TimesLost and the server

StatTrack 2.1.1 produces `TimesLost` only in its server `ReplaceIDsPatch`, wrapping
`SPTarkov.Server.Core.Extensions.ItemExtensions.ReplaceIDs(IEnumerable<Item>)`.
It copies known instance data to a newly generated item ID, increments the loss
count, and starts its own save. This is distinct from
`Helpers.Items.ItemHelper.ReplaceIDs`, called by PitFireTeam loadout normalization.

Arena's server sources contain no calls to either ReplaceIDs API. Its existing
exact end handler suppresses the native `LocationLifecycleService.EndLocalRaidAsync`
pipeline before insurance, restores the protected snapshot, and does not enqueue
Arena loss/return mail. The extension's reviewed native callers instead serve
mail/rewards, loot and preset generation. Ordinary insurance already queued
before Arena may still be delivered during Arena and must retain its ID-copy,
loss accounting and load/save behavior. No broad server patch was added.

This is the reviewed call-path explanation for Arena not generating a loss;
it is not a guarantee against another mod sending the player's equipment through
arbitrary mail/ID-copy routes during Arena. The client adapter cannot infer raid
ownership from an identity-free StatTrack HTTP payload and deliberately does not
try to block that endpoint globally.

## Verification

Run `pwsh -NoProfile -File tools/StatTrackCompatChecks/Verify.ps1` with the author's
separately obtained published package available at the default ignored artifact
paths, or provide `-PublishedClient`, `-PublishedServer`, and `-NativeAssembly`.
The harness links all three production adapter sources and production Protocol.
It uses independent executable counter fixtures and inspects author/game DLLs
as metadata only; foreign binaries are never executed or distributed by it.

Coverage includes ordinary instance/template saves, an ordinary request pending
across Arena, AI-versus-AI damage, all four guarded producer seams, two physical
local bodies, delayed former-body/victim/weapon callbacks, owner transfer,
zero-event teardown, failed loads before registration and before raid start,
normal failed-load negative controls, native/own patch receipts, partial patch
cleanup, forward versions, and future helper/nested/accessor bypasses. Compiled
negative controls must fail the intended semantic assertion, not fail to build.

Leaf run: **127 assertions and 20 compiled negative controls pass** with
`STATTRACK_COMPAT_CHECKS_OK`. Evidence:
`artifacts/stattrack-compat-checks/7ef9c479973543eb87f028668776a771` and
`artifacts/movement-stats-compat-20261019/stattrack-checks-final.log`.
The first attempt exposed an assertion that only noticed a missing own-hook
receipt on the following admission refresh; the assertion was corrected to
verify the immediate installation failure, and the complete final run passed.
Production files remained unchanged throughout the final run. Independent
review found no remaining blocker after the four-argument constructor and
future-helper/accessor checks were corrected. Parent re-verification and native
acceptance are separate steps.

Native acceptance is recorded separately by the parent integration report. The
offline harness does not prove actual Mono patch-of-postfix dispatch, successful
network traffic, UI tooltip refresh or an actual ordinary save race on the GPU.
The bounded native test should demonstrate ordinary save -> unchanged Arena
statistics across respawn/end -> a new ordinary save; an earlier ordinary request
completing during Arena is legitimate traffic, not an Arena leak.

DeveloperMode logs the first excluded shot, kill, hit and body-end/merge per
binding. Routine binding goes to Debug; unsupported contracts are warnings.
