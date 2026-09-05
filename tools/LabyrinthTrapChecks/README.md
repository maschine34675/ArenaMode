# Labyrinth trap and toxic-pool checks

The toxic pool was outside the original ten entrance-trap identities: it is
the separate `WaterRemove_2612876931` puzzle. A successful old setup therefore
did not prove that the pool was safe. The Arena-only trap option now also
disables its shallow/deep damage and requests its native drain animation.

Run from the repository root:

```powershell
python tools/LabyrinthTrapChecks/Inspect-InstalledScene.py D:/SPT41/EscapeFromTarkov_Data
powershell -NoProfile -ExecutionPolicy Bypass -File tools/LabyrinthTrapChecks/Verify-Regression.ps1
```

The scene inspector uses local Python with UnityPy 1.25.x. It reads installed
assets only; it never edits, exports or bundles them. It verifies the exact
`Labyrinth_DesignStuff.unity` scene, twelve hazard identities, six independent
health-effect handlers, six native off-controls, four Boolean animator actions
and five ambient loops. The pool has no additional `HandlerEffect` component.

| Pool component | Exact contract |
|---|---|
| `DamageZone1` / `HandlerDamage` | `toxicZoneEnter1_2612876931`, `toxicZoneExit1_2612876931`, `INTERACTIVE_Labyrinth_water_01_soft` |
| `DamageZone2` / `HandlerDamage` | `toxicZoneEnter2_2612876931`, `toxicZoneExit2_2612876931`, `INTERACTIVE_Labyrinth_water_01_deadly` |
| Each zone / `HandlerTriggerState` | Same input `WaterRemove_2612876931`, exactly its own enter ID, `_targetState=false` |
| `INTERACTIVE_Labyrinth_water_02` / `HandlerAnimator` | Own GameObject's animator, `WaterRemove_2612876931`, only `IsRemoved=true` |
| Same pool / `HandlerPlaySoundSequenced` | Empty play ID, stop `WaterRemove_2612876931`, autoplay/once true |

The inspector follows the pool animator's installed controller: `IsRemoved`
enters the `REMOVED` state, whose sole clip is `Labyrinth_water_02_REMOVED`.
Its animation events only call native `StopSound`. Two separate drain
one-shots remain native and are not started by Arena setup, because no global
switch event is emitted.

Native evidence in `../_decompiled/`:

- `EFT.GameTriggers/HandlerDamage.cs`: a start event immediately starts a
  damage coroutine; finish stops all that component's pending coroutines.
  Both pool volumes repeat every 0.5 seconds in the installed scene. Global
  configuration can override damage values, so Arena blocks exact handlers
  rather than changing shared damage values.
- `EFT.GameTriggers/HandlerTriggerState.cs` and `LocalTriggersEmitter.cs`:
  direct off-control invocation only disables its explicit trigger IDs.
- `EFT.GameTriggers/HandlerAnimator.cs`: the inspected action sets its own
  animator's Boolean parameter directly, without publishing a puzzle event.
- `EFT.GameTriggers/HandlerPlaySoundSequenced.cs`: existing native stop/fade
  callbacks remain responsible for source release. Arena only cancels the
  pending play coroutine, preserving earlier end timers.

The harness links the production trap implementation against modeled Unity,
emitter, coroutine and patch interfaces. It verifies early pool entry is
blocked before the drain completes; both controls with the shared ID are
applied; pending damage stops; drain state and loop stop are requested;
off/ordinary/stale/foreign raids remain untouched; malformed or ambiguous
scene contracts fail open; and retries, setting latching and teardown work.
Unrelated water volumes, spikes and map-wide damage remain native.

Seven source-copy mutants must fail their intended assertion: missing raid
ownership, skipped visuals, recursive audio release, cancelled end timers,
unrecognized pool entry, missing pool drain, and the old assumption that a
control's trigger ID is globally unique. The production file is hash-checked
and rerun after all controls. These tests do not launch or deploy the client.

Offline evidence cannot prove Unity animation playback or in-game contact.
After restarting the client, enter both shallow and deep pool areas after
respawn protection expires, check visible drainage and sound, then repeat
after a respawn/round transition. Also check a normal Labyrinth raid or a new
Arena raid with the option disabled retains vanilla hazards. Setup now logs
`12 hazards ... 4 animations, 5 audio loops; toxic pool drain requested`.
The existing `GATES.md` records the original ten-trap implementation; the
current root review records this subsequent toxic-pool extension.
