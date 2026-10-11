# Arena default fire mode

2026-10-11. Arena owns the author's DefaultAutoFireMode behavior for native
full-auto-capable weapons. The separate plugin remains useful in normal raids;
its source, settings and package are not retired or modified by this integration.

## Contract

- Only the alive local player of the exact server-confirmed Arena game/world.
- At completed firearm draw, use native `fullauto` when actually supported.
  Synchronize the item component and hands animator using their native setters.
- Initial own gear can finish drawing before confirmation. A once-per-current-
  controller/body/arm-epoch catch-up handles this and published respawn hands.
- Manual fire-mode changes remain until the next draw. No per-frame forced auto.
- No invented modes, burst/doublet/revolver fallback, bot/template changes or
  dependency on the faster-movement mutator. Stationary guns, malfunctioning
  weapons, spectator/respawn/aborting/pending-area states are excluded.
- The standalone mod may coexist and has its own broader fallback rules; do not
  claim that an enabled standalone mod leaves burst/revolver selection unchanged.

## Native seam and ownership

`EFT.Player.FirearmController.SpawnOperation.WeaponAppeared()` ends the draw,
enters `Idling` and invokes ready callbacks. Native `FastForward` and
`OnIdleStartEvent` use it too. In the installed decompilation this is
`../_decompiled/EFT/Player.cs:10590`. Unlike `SetAnimatorValues`, it normally runs
after the hands controller has been published. The old operation's `Controller`
field survives operation transitions; a postfix can compare it to current hands.

The catch-up tick runs after `CompleteRaidStart` synchronously captured the
protected entry loadout. It never changes pre-confirmation own gear. Both paths
require exact game/world/local-body/controller identity and native idle state.
After `FireModeComponent.SetFireMode` invokes subscribers, identity is checked
again before animator writes. There are no inventory moves or persistent profile
writes. Existing Arena snapshot/restore remains responsible for raid isolation.

Use the explicit native `AvailableEFireModes` list, not `GetForceAutoFireMode`:
that helper also chooses burst/doublet/doubleaction. Cache unchanged and excluded
weapon decisions as observed so manual selection or repair while holding a
weapon cannot unexpectedly cause another automatic selection.

## Verification and limits

Production-linked harness: 46 assertions including initial hands, real draw
dispatch, manual selection, unsupported modes, foreign bot draw, pooled hands,
replacement body with unchanged profile ID, native callback invalidation and
raid cleanup. Eleven compiled negative controls must fail their intended
behavioral assertions. See `tools/DefaultFireModeChecks/README.md`.

Real staged client build: zero errors, four existing nullable warnings. An
initial compile exposed the missing `EFT.InventoryLogic` namespace import; both
production and engine doubles were corrected to the actual native namespace.
Installed client/server/Core stayed unchanged during staging. Independent
read-only adversarial review found no P0-P2 issues in native lifecycle, scope,
protected capture ordering, integration or dependency documentation.

Native follow-up passed on GPU2, client `9eaa904`, with the standalone plugin
disabled throughout the raid: M4 initial draw, manual single held, redraw and
respawn; SVDS retained single even after redraw. Held-trigger ammunition counts
confirmed actual automatic versus single fire. Settings restored, protected exit
and whole-profile audit completed. Eight pre-existing missing stack fields were
repaired by SPT before Arena armed, explicitly proven by server-log timestamps;
no generalized profile exception or tester rollback was used. See
`GPU-AUTO-FIRE-VEST-RESULTS-20261011.md` for exact build, evidence and limits.
Own gear/PFT, every weapon and a separate ordinary PMC raid were not natively
tested in this run. Future isolated tests must again disable the standalone mod.
