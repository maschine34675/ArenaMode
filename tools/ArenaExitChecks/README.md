# Arena exit safeguards (r3)

Run `pwsh -NoProfile -File tools/ArenaExitChecks/Verify-Contracts.ps1` against
the installed SPT/SAIN files, then `pwsh -NoProfile -File tools/ArenaExitChecks/Verify.ps1`.
Neither command builds or deploys any mod project. The small executable is named
SAIN solely to exercise the real optional reflection binder; never deploy it.

The harness links both actual production prefixes and ArenaBotLifecycleScope.
External objects are I/O spies. Pickup cases cover ordinary raids, PreActive,
retired/current/replaced worlds, foreign body/taker/owner/delegate, multicast,
missing context, no transaction invocation and preservation of native Dispose.
SAIN cases cover absent/rejected binding, changed cleanup body, initialized and
uninitialized components, ordinary/retired/foreign owners, retained coroutine
cleanup, and visibility of an unrelated live activation exception.

69 assertions pass; nine deliberately broken compiled copies must fail their
specific behavior assertions. The full IL contract matcher is exercised by the
fixture, while the independent Cecil check verifies installed call targets,
native captured fields and the disposal boundary. The fixture's error positive
controls are modeled failures, not execution of the native animator/SAIN engine.
It does not prove Unity destruction order or GPU absence of either historical
exception. Same-GameObject owner lookup at pool unload still needs native testing.

No foreign source file is copied or distributed. The installed DLLs and local
decompilation are read only. Native profiles, corpse/player/loot registries,
inventory transactions and match protection are outside these guards.
