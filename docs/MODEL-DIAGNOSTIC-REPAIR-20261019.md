# Native fault identity and utility context repair — 2026-10-19

This repairs the confirmed diagnostic attribution defect from
[the animation investigation](MODEL-ANIMATION-INVESTIGATION-20261019.md).
It does not repair, suppress or claim ownership of the eight historical utility
timeouts, hash-zero events, null-hands message or exit-time animator warning.
The previous `currentWorldBody=unregistered` records with an empty body profile
must not be used as evidence that live bots were missing from the world.

## Identity contract

`Player.Init` passes an empty optional ID to `PlayerBody.Init` in the installed
native path (`Development/_decompiled/EFT/Player.cs:28204`). The previous observer
queried the world only with that optional body ID. It ignored an explicitly
known `operation.Player` and printed `unregistered` even when no lookup ran.

The observer now preserves `bodyProfile` unchanged and reports `ownerProfile`,
`ownerBodyMatch`, `lookupProfile` and `lookupSource=owner/body/none` separately.
Only a Unity-valid owner whose `PlayerBody` is reference-identical to the valid
reported body can supply `owner.ProfileId`. A stale parent, clone or destroyed
owner cannot borrow the current player's identity. A valid raw body ID remains
usable when owner matching fails. Empty/unknown identity and destroyed bodies
are `not-checked`, without registry reads. A real attempted lookup with no result
is still `unregistered`.
An actual lookup result that is a destroyed Unity Player wrapper is instead
`destroyed-player`; native `GetEverExistedPlayerByID` returns the retained value
from its dictionary without pruning it. The list-membership field also retains
exact managed body matches in such wrappers and does not imply a living player.

The existing `currentWorldBody` field remains the ever-existed-player relation;
new `aliveWorldBody` and `registeredBody` fields record live lookup and exact
body membership independently. The first two compare body references, never
just profile strings. An explicit destroyed owner stays visible as destroyed
rather than silently being replaced by a hierarchy owner.

## Fault-time utility data

The two native utility timeout branches and their 2.5-second threshold remain
unchanged. Their lazy details now record:

- Utility kind, operation state and elapsed time; actual arms update mode/queue.
- Exact current hands, controller operation, item and firearm animator identity;
  managed controller disposal, emitter/animator association and event consumer.
- Emitter mode, queued-event and bound-behaviour counts. Queues are not consumed.
- Animator backend/name, enabled/update/culling settings and speed; Unity object
  identity and initialization; layer count and at most layers 0 and 1, each with
  current state hash/time and transition target only when transitioning.

This is enough to separate stale hands/operation binding, an unavailable animator
or an unexpectedly disabled/scaled animator at a real timeout. It cannot prove
how the state evolved during the preceding 2.5 seconds, whether an event was
absent from an asset, or which mod caused a setting. Speed is a reported setting,
not a measurement of effective manual animation delta. No state names, parameter
dump, runtime-controller getter, Update, Play or event dispatch are added.

Native `FastAnimatorProcessor.runtimeAnimatorController` resets state and logs
from its getter; this observer never reads it. Its chosen getters and state-info
methods were checked directly. Unity animator state calls are skipped before
layer/settings queries when the underlying animator is null, destroyed or not
initialized; native getters can emit warnings without throwing. A disposed
controller also stops the snapshot. A utility-only read failure preserves the
collected fault context and permits later observations. General diagnostic read
failures retain the existing one-notice/disable protection.

DeveloperMode, exact active game/world identity and confirmed matching-map
loading admission are unchanged. Normal PMC/menu/stale contexts remain silent.
All new reads occur after the existing two-per-family/object and 24-per-raid
budget; respawn does not renew it. The existing 2,200-character detail ceiling
and seven-ancestor path limit remain. No gameplay or native error is budgeted.

## Verification

Production-linked checks:

```powershell
pwsh -NoLogo -NoProfile -File tools/NativeModelDiagnosticsChecks/Verify.ps1 -Mutants
```

The fixture passed **138 assertions**, including exact owner/empty/raw mismatch,
stale/absent/destroyed ownership, real raw-ID lookup, unknown identity, separate
world/live membership, disabled and expired-budget reads, controller disposal,
Unity initialization/destruction, partial utility failures, invariant numeric
formatting, and unchanged native instructions/errors/exception object/state.
It executes both transformed fault branches and a non-fault branch. Installed
`Assembly-CSharp.dll`, `AnimationSystem.Types.dll` and
`UnityEngine.AnimationModule.dll` metadata validate the added API paths.
All **17 behavioral mutants were rejected**, and the unchanged production source
was rerun successfully afterwards. Final command output:

```text
NATIVE_MODEL_DIAGNOSTICS_CHECKS_PASSED (138)
NATIVE_MODEL_DIAGNOSTICS_MUTANTS_PASSED (17 rejected; production restored)
```

The fixture does not run Unity player models.

Real build command:

```powershell
dotnet build ArenaMode.Client/ArenaMode.Client.csproj -c Release -p:CustomAfterMicrosoftCommonTargets=D:\SPT41\Development\ArenaMode-model-fixes\tools\AirPitChecks\NoDeploy.targets --nologo
```

The build succeeded with **0 errors / 4 existing CS8632 warnings** in
`ArenaMode.Shared/ClientSettingsModels.cs`, producing
`ArenaMode.Client/bin/Release/maschine-ArenaMode.Client.dll`. The explicit
override empties `DeployToSpt`, `SPTClientDeploy` and `DeployArenaAudioAssets`;
asset staging writes only to the build output. Across the final build, installed
`D:\SPT41\BepInEx\plugins\maschine-Redeploy.Core.dll` retained both its SHA-256
`218967C32C58F83C9BA2044888F12F595655D752172049DE57FA9C51FD4B231C`
and write time `2026-10-18T21:21:15.5667059Z`. The flat installed Arena client was
absent before and after. No deployment, GPU action or commit belongs to this leaf.
The final candidate DLL SHA-256 is
`51725EDFEFD24C82FF5F03D4800499B2A63461C4F4CF7C43053A4AFDF16397F1`.
`git diff --check` passes; this leaf's six modified/new files all fall inside its
explicit ownership. Parallel native-control documents belong to the parent.

## Four-pass review and acceptance boundary

1. Scope: only the owned diagnostic source, harness and these two documents;
   unchanged hook set, guards, budgets, native instruction instances and exception
   return. No animation, controller, profile or world mutation.
2. Native contracts: actual player/body initialization and owner identity, Unity
   fake-null, controller disposal, emitter/consumer API, Unity initialization and
   FastAnimator getter behavior checked against decompiled source. DLL metadata
   and full client compilation independently verify the callable API.
3. Adversarial controls: empty identity is not absence; raw ID disagreement and
   stale ownership cannot substitute another body. Read guards cover disabled
   diagnostics, ordinary raids, stale worlds, loading mismatch, per-object/raid
   exhaustion, disposed controllers and unavailable animators. Behavioral mutants
   challenge the original eight controls and nine newly repaired boundaries.
4. Evidence/polish: provenance fields remain separate, unavailable states are
   explicit, numeric output is locale-independent, optional state reads fail
   locally, and documentation does not overclaim causality or gameplay acceptance.

Leaf D1 passes the identity controls above; D2 passes the fault-only/lazy-budget
controls, 17 mutants, real DLL checks and explicit NoDeploy compilation. These
are implementation/offline gates. Parent review, deployment and fresh native
evidence remain separate. No fixture result here is an in-game acceptance claim,
and the underlying animation errors remain unresolved pending evidence.
