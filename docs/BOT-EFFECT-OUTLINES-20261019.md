# Bot spawn/pickup effect contours — 2026-10-19

The former three-ring cages are replaced with a shared, narrow inner contour
of the bot's visible geometry. Spawn protection remains pale cyan/white; pickup
shield remains blue and double damage pink. Simultaneous effects use fixed
eight-pixel segments of those colors on one contour, preserving independent
effect owners without concentric spatial cages. No extra setting is introduced.
The existing mode/powerup damage authorities are unchanged by presentation.
Separate team-box-only spawn-shield admission is documented in
[the scope review](BOT-SPAWN-SHIELD-SCOPE-20261019.md).

## Rendering and ownership

One private `ArenaBotEffectRenderer` is owned by the exact active Arena
game/world/match and arm epoch. It accepts only independent live Team DM or
Deathmatch phases, current AI bodies, exact captured health controllers, and
native alive-world registration. PreActive replacements retain their existing
gameplay protection but do not create a view until active. Caller renewal keeps
each presentation lease fresh for at most two frames; losing a pickup owner
cannot remove a concurrent spawn owner. No profile ID can lend a contour to a
different body. Death, phase end, game/world/epoch changes, retirement and final
owner disposal detach the camera pass and release owned references/resources.

Existing native renderers are read through `PlayerBody.GetRenderersNonAlloc`;
the render pass references them directly. There are no cloned bodies/scripts,
native mesh/material writes, `BakeMesh`, forced offscreen updates or camera flag
changes. Disabled/inactive/shadows-only/culled renderers are excluded. Geometry
cache refresh uses retained lists at 0.5 seconds, also refreshing immediately on
PlayerBody identity change; there are no deliberate per-frame managed allocations.

The independently authored embedded shader first masks body fragments against
scene depth. Its composite requires a visible center mask pixel and repeats
the depth rejection. It therefore draws an **inner** contour, never an expanded
shell. There is no positive depth bias, through-wall switch, depth-independent
fallback or stencil bit borrowing. A full-float mask avoids half-depth rounding
between passes. A missing/default one-pixel depth texture produces no contour.
The pass runs on the authoritative game camera before opaque image effects;
temporary target dimensions resolve at execution, not from `Screen.width`.

Missing shaders, unsupported mask formats and command failures disable only
presentation. The failure path clears/detaches commands and releases owned
materials/bundle immediately, while retaining the failed owner to avoid retrying
every frame. Buffs and immunity are not refused or granted by rendering. Native
GPU evidence is still needed for actual depth production, DLSS viewport mapping,
skinning/LOD rendering, thin cover and full lifecycle behavior.

## Reference and provenance

[BreakoutOutlines](https://github.com/harmonyzt/BreakoutOutline/tree/c990b9f98f084d6909d9833e68a032215208486d)
was inspected for technique only: existing renderer draws, mask and edge passes.
Its corpse-specific 0.5 m depth tolerance and native renderer mutations are not
used here. Forge labels the reference MIT but its inspected source tree has no
license/copyright notice. No external code or compiled shader was copied.
The Arena shader, editor source and exact bundle/source hashes are tracked in
`tools/BotEffectShaders` and `ArenaMode.Client/Assets/BotEffects/build-manifest.json`.

## Implementation checks (not native acceptance)

- Unity 2022.3.43f1 Windows64 shader build: passed, no shader warnings/errors.
  Bundle: 19,127 bytes, SHA256
  `E8495E5C29FF40ABA6B9D8C180DEF3604E53031F6CA9E13CB0B19F5644C830E8`.
- Production-linked renderer/lease/adapter harness: 73 assertions; nine compiled
  defects rejected. Evidence:
  `artifacts/bot-effects-20261019/outline-checks-5d15c897ffc2440e9172499672f57981`.
- Existing powerup harness updated to test combined presentation leases:
  410 assertions, 45 installed native contracts and 17 compiled defects rejected.
  Evidence: `artifacts/powerup-checks/7a5d9dc1befc45faa5b462840ba7ef99`.
- Real client Release build with `tools/AirPitChecks/NoDeploy.targets`: passed,
  zero errors and four pre-existing nullable annotations warnings.

No deployment/GPU operation was performed by the implementation leaf. Native
visual, cover and cleanup evidence belongs to the separate GPU report and is
not implied by these compile/ownership checks.
