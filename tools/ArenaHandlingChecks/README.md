# Arena landing and firearm handling

Run `pwsh -NoProfile -File tools/ArenaHandlingChecks/Verify.ps1` from the repo.
It verifies installed native signatures/call sites with Cecil, then compiles the
unchanged production hooks against independent engine/session/config doubles.
No mod project or deployment targets are imported. Native operation calls in the
fixture execute the real hook bodies; the real landing transpiler output is also
emitted as a callable method. Deliberate source faults must compile and fail their
named assertion. Evidence and compiler-only mutants stay in ignored `artifacts/`.

The suite covers hard-fall duration versus ordinary jump/other penalties; exact
local scope rejection; invalid configuration and native values; draw versus
holster; no compounding; untouched reload/fix/fire/global animator values;
native skill/injury parameter refresh; completion and physical owner changes;
external parameter writers; pooling; raid-end/master-off/plugin cleanup; and
native failures before/after draw writes. It verifies operation-start call counts
and fails on a changed patch shape. The shared `ArenaMovementScope` is a controlled
external double here; root's air-movement suite verifies that helper's actual code.

Native anchors in `../_decompiled`:

- `EFT/Player.cs` `LandingAdjustments`: native fall threshold and curve calculation
  remain in place. Only its single `ChangeSpeedLimit(speed,Fall,duration)` call
  reaches the wrapper; `MovementContext.OnJumpEnd` remains ordinary. Native limits
  can overlap and preserve their longest outstanding expiry. This halves the
  hard-fall contribution, not every possible source of movement restriction.
- `FirearmController.SpawnOperation.Start(Action)` has one common native Start
  call; `Remove.Start(Action,bool,Item)` has two alternative calls (normal and
  simplified-skeleton branches). The acquisition transpilers replace those calls
  rather than patching the tiny, nonvirtual 25-byte common method itself.
- `SpawnOperation.WeaponAppeared` restores before its native chamber/malfunction,
  idle and callback processing. Controller update restores when operation/scope
  changes; Destroy restores before pool return. These do not speed reload/fire.
- `FirearmsAnimator.SetSpeedParameters(reload,draw)` writes independent native
  `SpeedReload` and `SpeedDraw`. Only draw is modified for an owned operation;
  a transparent finalizer reconciles actual writes and preserves native exceptions.
  `AnimationControllerParametersTable.GetFloatSpeedDraw/SetSpeedDraw` access only
  that parameter, never `Animator.speed`, SpeedFix, reload, ammo or firing logic.

The small native `SetSpeedParameters` refresh method may inline on Mono. Draw and
holster acquisition do not depend on that hook. If an inlined/foreign refresh
bypasses it, the next update relinquishes the changed parameter instead of
compounding or overriding it; that one operation can continue at native speed.

Operation-owned values restore only if the same native animator and our last
written value are still present; a different writer is preserved. Restoration is
best-effort if a destroyed native getter/setter itself fails. Mid-operation setting
changes update the owned multiplier; switching the master off releases it. Enabling
again takes effect at the next draw/holster. No third-party source or DLL is bundled.

Four passes completed: complete implementation; native/domain review; correctness
and ownership review (including transactional failures and tiny-method inlining);
polish/evidence pass. Independent reviewer inspected the revised native flow.

Offline tests do not establish native animator playback or visual acceptance.
GPU checks still need high versus ordinary landing, pistol/rifle fast and normal
switches, reload/fire/malfunction unchanged, mid-operation toggles, respawn and
ordinary PMC isolation. The animation parameter preserves native skill/injury
baselines; precise visual event alignment remains a GPU check.
