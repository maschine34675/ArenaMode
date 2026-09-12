# Arena bot lifecycle safeguards

Run `pwsh -NoProfile -File tools/ArenaExitChecks/Verify-Contracts.ps1` against
the installed SPT/SAIN files, then `pwsh -NoProfile -File tools/ArenaExitChecks/Verify.ps1`
for the pickup/SAIN negative controls and
`pwsh -NoProfile -File tools/ArenaExitChecks/Verify-Weapon.ps1` for the weapon controls.
None of these commands builds or deploys a mod project. The small executable is named
SAIN solely to exercise the real optional reflection binder; never deploy it.

The harness links all three actual production safeguards and ArenaBotLifecycleScope.
External objects are I/O spies. Pickup cases cover ordinary raids, PreActive,
retired/current/replaced worlds, foreign body/taker/owner/delegate, multicast,
missing context, no transaction invocation and preservation of native Dispose.
SAIN cases cover absent/rejected binding, changed cleanup body, initialized and
uninitialized components, ordinary/retired/foreign owners, retained coroutine
cleanup, and visibility of an unrelated live activation exception.

Weapon cases cover active, PreActive, temporarily NonActive, explicitly dead,
Disposed and manager-disposed owners; missing/foreign managers; ordinary raids;
and replaced worlds/bodies despite a repeated profile ID. Each case checks both
initial values of IsWeaponReady and preserves that value. Only admitted retired
callbacks clear IsChanging. See [the weapon review](../../BOT-WEAPON-CALLBACK-REVIEW.md) for the
diagnosis, exact ownership boundary and evidence limits.

The current shared baseline has **118 assertions**. The separate scripts require
**nine pickup/SAIN** and **eight weapon** deliberately broken compiled copies to
fail their specified assertions. Verify-Weapon explicitly rejects compiler errors
as negative-control evidence. Each verifier reruns the unmodified baseline last.
The dated weapon review records which checks were actually run for that change;
the nine existing controls are not silently counted as rerun by Verify-Weapon.

The SAIN IL contract matcher is exercised by the fixture. The independent Cecil
check reads installed call targets, captured fields, native retirement members,
empty selector disposal, manager teardown order and the surrounding callback's
method ordering. It emits ARENA_WEAPON_CALLBACK_INSTALLED_CONTRACTS_PASSED and
ARENA_EXIT_INSTALLED_CONTRACTS_PASSED. This ordering is static evidence: the harness
does not execute a native Result, weapon callback, manager Dispose, animator, or
outer continuation. Its existing pickup/SAIN error controls are modeled failures.
It does not prove Unity destruction order or GPU absence of either historical
exit exception or the F3 weapon exception. Same-GameObject owner lookup at pool
unload still needs native testing. A complete native build and GPU acceptance are
separate from these helpers.

No foreign source file is copied or distributed. The installed DLLs and local
decompilation are read only. Native profiles, corpse/player/loot registries,
inventory transactions and match protection are outside these guards.
