# Fast Grenade Throw boundary checks

`Verify.ps1 -PublishedDll <FastGrenadeThrow.dll>` first compiles the production adapter against the installation's actual EFT/Unity/BepInEx DLLs using `NativeCompile.csproj`, then builds the isolated .NET Framework executable. Both projects are independent and never deploy. The executable links the production compatibility source and uses the installation's real Harmony. Independent emitted foreign methods retain the reviewed selection/direct-call boundaries; explicit inventory, loader and session doubles delimit what this test can prove.

The executable covers absent plugin, 2.0.0, structurally matching newer version, old version, changed private API, missing hooks, native selection, changed user selection, inaccessible/unexamined/stale selections, foreign slot exclusions, physical body identity, all input/replacement guards and ordinary-raid controls. Four compiled mutations must fail assertions. `-SkipMutants` is a partial check, not the complete gate.

The optional published-DLL check pins the reviewed SHA-256, GUID/version, private method signatures, effective grenade query and direct Unity-key polling. It never loads the foreign plugin or game and does not copy foreign implementation. Native selection, animation, inventory operations and input delivery remain GPU acceptance gates.

## Native acceptance

Use the actual Fast Grenade Throw 2.0.0 release; capture loaded assembly location/hash, foreign settings, current Arena build and current Core version. Restart EFT after installing. Keep user preferences and canonical profiles unchanged, and restore temporary test files afterward.

1. Ordinary PMC: with two grenade types, record `Equipment.TopPriorityGrenade`, effective `GetThrowablePriorityGrenadesList`, GUID/ID/quantity and `FindGrenade` result. G/H must preserve FGT's original first-candidate selection outside Arena. Confirm both throws and unchanged FGT Enabled/key preferences.
2. Arena: use a real grant of the custom Arena Molotov alongside RGD-5. After grant, native `TopPriorityGrenade` must point to the Molotov; G must produce that exact item/template, remove it once and return to the previous firearm. Repeat H for underhand throw. Actual thrown item identity and inventory delta are required; a successful input acknowledgement alone is insufficient.
3. Explicitly change native grenade selection. FGT must follow that valid item rather than permanently force Molotov. Remove the selected grenade; stale selection must fall back to the effective query. If UIA is present, a grenade in a disabled slot must remain excluded.
4. Open Arena buy/pause/inventory UI and press G/H while `ArenaInputBlocker.Blocked` is true. No foreign `_throwInProgress`, `ForceLowThrow`, hands, inventory or world grenade delta is permitted. Close UI, wait for input drain and verify the next fresh G/H works. Check assembling/countdown where Arena actually owns input, not just a visual timer.
5. Interrupt a quick throw by death; wait for actual replacement and repeat G/H. Record foreign flags, physical player identity, hands controller and grenade count before/after. Run two replacements and raid exit/re-entry; do not mark success from source/API checks.

Integration: call `FastGrenadeThrowCompat.GetRejectionReason()` from Arena optional-mod admission. Hooks remain no-ops outside an exact active Arena player and never change foreign configuration. API drift rejects only Arena admission, not ordinary PMC/Scav.
