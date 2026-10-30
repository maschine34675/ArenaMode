# Classic Movement: Arena movement authority

Status: implementation and independent source review complete. The
production-linked checks pass **189 assertions and 23 compiled negative
controls**. Native acceptance is recorded separately; this document does not
claim an in-game pass.

## Contract

Classic Movement remains optional. Reviewed client/server: **1.1.3**. Arena's
adapter targets `com.boogle.classicmovement` and accepts newer plugin versions
only after the same API, configuration and actual Harmony registration checks,
with one warning that the newer runtime has not been reviewed. Missing or unsafe
contracts reject **Arena only**, never ordinary PMC/Scav entry.

Classic Movement replaces several native movement-state classes. Arena's
double-jump and Bunnyhop require the real native classes and their collision,
contact and animation contracts. Accepting arbitrary subclasses would hide the
incompatibility. Instead, Arena skips precisely two foreign operations for owned
Arena bodies:

- `OldTarkovMovement.StateReplacer.Prefix`, registered on
  `MovementContext.GetNewState(EPlayerState, bool)`: return `true` from the skipped
  foreign prefix, allowing EFT's original factory to create and cache the state.
- `OldTarkovMovement.SmoothSpeedFix.Prefix` (despite its name, a postfix on
  `MovementContext.ManualUpdate(float)`): skip its competing smoothing write;
  native movement and Arena's own scoped updates still run.

This applies to the player **and bots**, including replacements. No foreign
global setting, patch owner, state dictionary or cached state is rewritten.
No movement implementation or foreign DLL is copied/bundled. Existing exact-type
checks, native collision/contact/input, stamina, crouching/prone rules and
Arena's movement settings are unchanged.

The native cache is selected for the whole owned Arena body, including when
**FAST MOVEMENT is off**. Turning that mutator off removes Arena's bonuses through
their existing gates; it does not replace already-cached states mid-raid. Classic
Movement's independent aiming, bushes, tilt and optional rotation preferences
remain active according to its settings. The adapter does not promise that
disabling an Arena mutator disables other mods' preferences.

## Exact loading and lifetime ownership

Native source was checked in `../_decompiled`:

- `TarkovApplication` receives the accepted local-raid response and assigns its
  `serverId` before constructing/publishing `LocalGame` and calling `Run`.
- `BaseLocalGame.Run` marks `Running`, sets `GameWorld.LocationId`, then creates
  its local player **before** `vmethod_1` initializes `BotsController`/`IBotGame`.
- `Player.Create` already assigned `GameWorld`. `LocalPlayer.Create` assigned
  `IsYourPlayer`; `Player.Init` assigned `Profile` before movement initialization.
- `MovementContext.Create` assigns its protected `_player` before
  `InitMovementStates` invokes the state factory. `Player.MovementContext` is
  assigned only after `Create` returns; the state's cache already exists then.

Every new lease requires an actual `LocalPlayer` body with an initialized profile
and exact body/world/game identity, matching PMC settings and map. It requires
either the exact active Arena pair or the matching armed/pending token in the
**server-confirmed end identity**, never an outgoing start marker or map alone.
Only Running/Starting/Started may acquire leases.

Before the initial local owner and bot game exist, the constructor exception is
limited to Running + missing native `game.LocalPlayer` + `IsYourPlayer` + the
**same Profile object as `game.Profile`** + still-unassigned body movement
context. All other newly captured bodies require the exact current `IBotGame`.
This is not a profile-ID comparison. Menu/scene preview `Player` objects,
uninitialized bodies, mismatching worlds/contexts and ordinary following raids
do not qualify. Initial bots do not yet have `AIData`, so an `IsAI` requirement
would incorrectly miss their construction.

A weak context lease records its exact body and world. It keeps old Arena
contexts native through delayed teardown callbacks; a new body/context, another
world or a different non-null body movement context cannot inherit it. The
adapter does not retain global scene/profile references. Clearing the binding
releases the weak registry and only the adapter's own Harmony hooks. Failed
unpatching leaves inert prefixes because binding authority is revoked first.

## Optional contract and limitations

The reviewed release's plugin version is 1.1.3, while its assembly version is
**1.0.0.0**. Validation accepts that known legacy assembly version, or an assembly
version that exactly matches the reported/attributed newer plugin version. It
does not accept arbitrary metadata mismatches. Both foreign methods must have
the exact signatures and actual native hook receipts. `NoInertia=false` correctly
means the factory hook is absent, while the speed postfix still exists. Unexpected
extra foreign hooks on those native entry points reject admission.

**`NostalgiaMode=true` is not supported in Arena.** The current server configuration
does not offer this old option, but the field and old asynchronous interaction/
animation patches remain in the client. If explicitly enabled, Arena explains
that the legacy option must be disabled and the game restarted. Ordinary raids
remain available; Arena never edits this setting itself.

The adapter verifies API/identity/hooks, not every instruction of unknown future
releases or arbitrary third-party movement overhauls. An API-compatible newer
version is not a blanket compatibility promise. Config changes made externally
after a foreign mod has installed hooks require a restart. In-game testing must
check native Harmony-on-Harmony detours, initial caches, bots, two respawns,
FAST MOVEMENT off/on, and ordinary movement before/after Arena.

Diagnostic setup success is Debug. With DeveloperMode enabled, at most four
state/speed × local/bot lines per binding identify actual guard execution.
Protection does not depend on the diagnostic switch.

## Sources and reproducible checks

- [Classic Movement source](https://github.com/TheBoogle/ClassicMovement/tree/4875053b1a4a1c7aeea38053c1a894cff85d8805)
- [Server/config source](https://github.com/TheBoogle/ClassicMovementServer/tree/fbbf68edf6a9afcb8e934ea5047c999d2ff40b48)
- [Forge entry](https://sp-mod.com/mod/1860/classic-movement)

Published client SHA256:
`2F3039C34E2800CC082C908F72B8896DBAB88B2A69FBA13BDFE90540E0A37A00`.

`pwsh -NoProfile -File tools/ClassicMovementCompatChecks/Verify.ps1` links the real
adapter and lifecycle protocol. Independent foreign method fixtures expose
native/foreign execution and cache identity; they contain no copied foreign
implementation. The published DLL is inspected with Mono.Cecil, not executed.
The runner also compiles deliberate faults and requires their exact assertion
failures. Neither these tests nor a successful no-deploy build certify Unity
movement or physical gameplay.

Initial full passing evidence:
`artifacts/classic-movement-checks/c37a9b9a51d54d53bf57c12e851fd861/`.
An earlier runner attempt rejected the correct retired-context mutant at a
different expected assertion label; only that test label was corrected. It is
not counted as a successful full run. Independent adversarial review found no
remaining blocker against the frozen production sources. Final integrated
build, native probes and parent re-verification belong to the integration report.
