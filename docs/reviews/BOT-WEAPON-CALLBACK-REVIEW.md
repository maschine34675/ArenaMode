# Retired bot weapon callback safeguard

Date: 2026-09-12. This review concerns F3-01 only. Native pause-menu input and
air-control work are separate changes. The implementation is in
`ArenaMode.Client/Bots/ArenaWeaponCallbackSafety.cs`, enabled and shut down by
`Plugin.cs` alongside the existing bot lifecycle safeguards.

## Observation and limits of attribution

`GPU-ARENA-REFINEMENTS-TEST-RESULTS-r3.md` (`bd95b72`) records one new Unity NRE
at **16:09:25.212 UTC / 18:09:25.212 MESZ**. The original client log is
`artifacts/gpu-arena-refinements/arena-refinements-r3/2026-09-12T16-00-17-789Z/logs/final-all-stopped/client.log`,
lines 13754-13780. `BotWeaponSelector.OnWeaponTaken` was entered from
`CG_TryChangeToSlot.method_0` through the native Player/Hands/animator completion
path. DMD identifies a detoured method, not the responsible mod or null receiver.

The failing test ran source/build `f64df5be499d06d29cc4e6b717dd52ea0931725a`,
client SHA256 `7DCAA94CFDF5F0C7E6736796901BE4C3DCBADCEC6F74C76E1668C22B81201F11`.
It did not contain this safeguard. During independent Customs TDM, a USEC
replacement was requested at 18:09:24.960, 252 ms before the exception; that
replacement arrived PreActive at 18:09:27.219. The previous replacement arrived
at 18:09:22.902. This is compatible with ongoing death/replacement activity, but
does not identify which owner failed or prove that this particular NRE occurred
after disposal. The F3 window contains no corpse-trim event, invoked SAIN teardown
guard or pending-pickup cancellation. Native raid exit started later, at
18:11:28.575. No connection to DLSS or the separate ammunition observation is
established.

## Confirmed native lifetime gap

The local decompilation and installed Assembly-CSharp IL agree:

- `BotWeaponSelector.TryChangeToSlot` captures the selector in an asynchronous
  hands callback. The surrounding callback updates its timer/slot and invokes
  the slot event before `OnWeaponTaken`, then invokes an optional caller callback.
- `BotWeaponSelector.Dispose()` is empty. `BotWeaponManager.Dispose()` clears
  `Selector`, `Melee`, `info` and other services before setting `_disposed` true.
  Normal `BotOwner.Dispose` retains the manager reference.
- `OnWeaponTaken` clears `IsChanging`, then passes a successful result to
  `UpdateHandsController` before checking bot state. The firearm path calls
  `UpdateFirearmsController`, whose `_disposed` early return does not protect the
  following `Melee.UpdateKnifeController` call in the outer method. A retired
  manager therefore has a concrete reachable null-service path.
- A non-Active/non-PreActive owner also receives a new delayed `TakeMainWeapon`
  task from the native callback, including when its result failed.
- Native `BotOwner.Disable` and `Deactivate` set NonActive without disposing the
  owner. NonActive alone is not proof of retirement. `OnDied` calls Dispose before
  setting IsDead, and Dispose uses Disposed while tearing down its services.

This establishes a defensible guard target, not retrospective proof of the
historical null object. The new diagnostic and a fresh native test are required
to establish whether the observed exception is prevented.

## Ownership and behavior

Only the base `BotWeaponSelector.OnWeaponTaken(Result<IHandsController>)` receives
a prefix. The installed method is public, virtual, returns void and takes a
by-value `Comfort.Common.Result<EFT.IHandsController>` struct. `_owner` is the
public field inherited from BotData; `_disposed` is a public manager boolean.
BigPipe inherits this callback. Infected has a separate override that does not
call the base implementation and is not claimed as covered by this change.

The prefix uses `ArenaBotLifecycleScope.Owns` on the selector's exact owner. That
existing scope admits exact current Arena bodies (including a confirmed matching
map arm) and retains weak stamps across death/GameEnd for the same managed body.
It rejects a replacement body or a different current world/game, even when a
profile ID repeats. Ordinary unarmed owners without such a stamp remain native.
The existing confirmed-arm/preload scope is unchanged; this is not a new global
bot exception handler.

After ownership, retirement requires at least one explicit fact: `IsDead`,
`BotState.Disposed`, or `_disposed` on a manager whose `_owner` is the same object.
Missing components, null health and NonActive alone do not qualify. No current
`manager.Selector == selector` check is imposed because native disposal itself
clears that reference before a retained callback can finish.

For an admitted retired callback, only `IsChanging` becomes false, matching the
native method's first instruction. `IsWeaponReady` remains unchanged. The prefix
does not replace the result, update hands, mutate inventory, reinitialize
services, schedule a task, or invoke/cancel the outer callback. Native code
outside this specific method remains responsible for its own completion.
There is no exception finalizer; live/unknown errors remain visible. A diagnostic
is capped once per selector using a weak-key table. Shutdown disables the prefix
before unpatching and clears its diagnostic table; a remaining hook fails open.

## Verification and remaining acceptance

The shared offline executable links the actual production safeguard and scope;
external Harmony, Unity, EFT and SAIN objects are minimal I/O spies. It checks
retirement admission, ordinary/replaced-owner rejection, both initial readiness
values and IsChanging cleanup. It does not execute native weapon methods or
Result processing and does not simulate the animator. In particular, there is
no modeled continuation counter presented as native completion evidence.

`tools/ArenaExitChecks/Verify-Contracts.ps1` reads installed metadata and IL. It
verifies the public signature and retirement members, Result value type, the
empty selector disposal, manager null stores, inner firearm early return and
the relative order of slot/event, OnWeaponTaken and caller continuation. This is
static contract evidence, not native callback execution or Harmony dispatch.

Completed isolated checks on 2026-09-12:

- `Verify-Contracts.ps1`: both
  `ARENA_WEAPON_CALLBACK_INSTALLED_CONTRACTS_PASSED` and
  `ARENA_EXIT_INSTALLED_CONTRACTS_PASSED`.
- `Verify-Weapon.ps1`: **118 assertions**, then all **8 compiled faults** rejected
  at their intended behavior assertion, then a fresh unmodified **118-assertion**
  pass. Faults cover the three retirement reasons, a foreign manager, temporary
  NonActive, ordinary-raid leakage, missing IsChanging cleanup, and unwanted
  IsWeaponReady promotion. All eight logs show runtime assertion failure and no
  C# compiler error. Artifact directory:
  `artifacts/weapon-callback-checks/602281ab4533484e98ab843140780318/`.
- Guard source SHA256:
  `27BFE92B02C11CCAD96EEE100393B777BBF5A9382AC1A6E4BE4447D066E30CF4`.
  Installed Assembly-CSharp SHA256:
  `EE25CEE1259777B38ED8B3E7841FDC2DB3C98540B1469FA539B1FF183476E436`;
  Comfort: `D175D2E4CA94B7FEFB7D7FC17C2A815AC8825D64A08E4E1635156F65C0CE5CBC`;
  SAIN 4.5.1.0: `C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489`.

No mod build, deployment, game run or profile change was part of these checks.
The existing nine pickup/SAIN negative controls remain available in their
separate verifier and were not rerun by the weapon script. Their shared baseline
assertions were included in both 118-assertion passes.

Parent verification then reran the installed contracts and the 118-assertion
baseline, followed by all nine existing pickup/SAIN compiled controls and a
fresh baseline: `artifacts/exit-checks/fa13fa18ef63464997c8f023d911d308/`.

GPU acceptance remains open: exercise independent TDM replacement with normal
weapon switching, inspect any capped retired-selector diagnostic, and stop on
new exceptions. A missing diagnostic means the guarded path was unobserved;
it is not evidence that the guard was exercised. Preserve the original F3
report/logs and do not claim all weapon/animation exceptions are fixed.
