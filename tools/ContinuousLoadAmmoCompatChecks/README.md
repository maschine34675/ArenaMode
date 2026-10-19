# Continuous Load Ammo adapter checks

Run `pwsh -NoProfile -File tools/ContinuousLoadAmmoCompatChecks/Verify.ps1` from
the repository. The published 1.2.0 DLL is read with Mono.Cecil only. Supply
`-PublishedAssembly` when it is not present in the ignored acquisition directory.
No test downloads, installs, starts EFT or changes foreign settings.

The executable compiles all three real adapter files and Protocol. Instrumented
boundary types and an independent foreign fixture expose physical controllers,
task completion, event fan-out, native side effects and Harmony receipts. The
fixture assembly deliberately has the reviewed identity so that the production
reflection contract runs rather than a parallel policy implementation. A small
dispatcher applies the production prefixes/postfixes/finalizer; this is desktop
contract evidence, not proof of Mono/Harmony detours or Unity UI behavior.

Compiled faults remove individual ownership, lifecycle or cancellation guards.
Each must build and fail its named behavioral assertion; compile errors and
unrelated failures do not count. Production sources are checked for changes
during each run. Logs stay in ignored artifacts.

The native test still needs ordinary loading, Arena live-kit/respawn loading and
a following ordinary raid, with real UI and exact loaded build identity.
