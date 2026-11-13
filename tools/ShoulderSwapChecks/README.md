# Auto Shoulder Swap guard checks

`pwsh -NoProfile -File tools/ShoulderSwapChecks/Verify.ps1 [-PublishedDll <hazelify.StanceSync.dll>]`

Links the production guard into a .NET Framework process with actual Harmony and
minimal EFT doubles. Calls the patched foreign callbacks to verify that native
lean/camera work continues while unsafe foreign callbacks are skipped. Covers
non-firearm hands, firearm draw with no scopes, dead bodies, missing native
components, ordinary raids, exact world/body/camera identity and replacement.
Binding checks include absent/unready/mismatched plugins, rejected-instance
caching and target-specific cleanup. Seven intentional faults must fail their
named behavioral assertions. Optional Cecil checks inspect the actual released
DLL's BepInEx identity, both static callback signatures and scope access sites.

Does not deploy or replace game input. Unity behavior and shoulder animation
still require the native acceptance scenario in the compatibility report.
