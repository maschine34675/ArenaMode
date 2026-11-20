# DynamicMaps 1.3.0 and shared body compatibility

20 November 2026. Development changes; no new public release.

## Changes

- Review DynamicMaps 1.3.0 for both the client and server entries in the default whitelist.
- Update the corpse-marker adapter's reviewed version to 1.3.0 while keeping its 1.2.1 minimum. Actual installed contracts and both supported versions are checked; newer versions retain the existing advisory policy.
- Redeploy.Core 1.0.26 refreshes the active DynamicMaps quest and key/door subscriptions after physical body replacement. It also retires Advanced Movement 1.0.1 state belonging to the outgoing body. Both fixes live in Core and serve standalone Redeploy as well as Arena. See the [Core contract and native results](../../../../Redeploy/docs/compatibility/MAP-MOVEMENT-BODY-20261120.md).
- The pre-existing `fix/dynamicmaps-130` branch was fast-forwarded through `026d4c44` before the final build. This preserves the Domination marker API/style fix already installed from `794453e7`; it is not part of the new respawn adapter. The branch and its worktree were retained.

Advanced Movement was allowed only in the isolated native test policy. It is not added to the default Arena whitelist: general movement-feature overlap needs a separate review.

## Verification

Corpse integration passed 86 assertions, 11 rejected mutants and the installed DynamicMaps 1.3.0 contract. The corpse cleanup regression passed 312 assertions and 36 rejected source faults. Core's API/unit/golden-order suite passed all 637 tests, with the two new adapter suites and the shared coordinator also passing.

The final Arena xunit run passed 4,422 tests, with zero failures and the three existing environment-dependent skips (two reward recovery fixtures and the installed APBS native pipeline).

Final integrated verification against everything changed since `3b999c07`, including the preserved Domination integration: `tools/Run-ArenaVerification.ps1 -ChangedSince 3b999c07` passed **64 steps, zero failures, zero cached results**. Log: `artifacts/body-compat-20261120-integrated-verification.log`. No installation files changed during this no-deploy run.

On SPT-GPU-2, standalone Redeploy passed real Slide/Dive live-body replacement, death-to-PMC and death-to-own-Scav scenarios. The minimap stayed active, inventory/key updates followed the new controller, and the replacement could walk and shoot.

The final Arena bundle passed a real VSS death/respawn and three subsequent shots (11 to 8 rounds). An armed Slide replacement retained the living old body and removed its movement/gesture/audio state; the minimap remained active over 352 observed frames without interruption. Hiding the map before another real death left it hidden until explicitly reopened. An actual quest completion was not tested; the native delegate ownership and offline event delivery checks are the available evidence for that path.

## Log review and cleanup

No new body-compatibility exception or stuck respawn occurred. The final log's 26 EFT error entries concern existing muzzle-material/weapon animation and Factory map diagnostics, also found in older logs or the standalone control. These are reported in the native report, not suppressed by this change.

The temporary Advanced Movement package, observation probe, local whitelist override and disposable profile/storage were removed (28 owned files, including clone-only Bot Placement data). Both changed settings were restored byte-for-byte; 77 original profile backups rotated by SPT were restored additively. All 213 canonical profile/PFT file checks across the VM and both GPU hosts matched their before-start hashes. Startup reconciliation of five pre-existing reward journals produced zero eligible rewards and no profile change; its legitimate records and APBS diagnostic logs were retained. DynamicMaps and the previously retained optional mods stay installed.

## Accepted development files

| File | Version/build | SHA-256 |
| --- | --- | --- |
| Redeploy.Core | 1.0.26+5283c7c | `ef3f5e15f2c091374b027d1632526f9d46851ae0b770934115dff22ecb8b6034` |
| Redeploy.Client | 1.1.3+5283c7c | `6b576de6a7f54f906a4c027eb1e6113dd24eaf53a75567c27b391bcdd9dc6420` |
| ArenaMode.Client | 0.6.0+026d4c44 | `385305a694f39b3750077c3185eaa445eb69936b8820e6fba1e6a305e7a56e15` |
| ArenaMode.BigBrainCompat | 0.6.0+026d4c44 | `eeec21df64fdbc730f266b02dda5a673decfec3ef0c9069a1a378ee239c4f00d` |
| ArenaMode.Server | 0.6.0+026d4c44 | `35b1781599f3d13e5449176d889a51493d3f590d23d67b1140ea7e8b8c9c626d` |
| arena-whitelist.json | DynamicMaps reviewed 1.3.0 | `03a0879bce1630d64711ea54ed270fd512464911eda96bc071a8aa78a069a6a2` |

Core's loaded MVID was `09757c01-4ad6-445f-b60d-1066e70cb7d7`. Matching PDBs and all ten install-relative paths are recorded in `Redeploy/artifacts/body-compat-20261120/candidate-b/files.json`. Native receipts are in the adjacent `native/` folder. The accepted binaries were built before the evidence commits; the stamps above identify their exact bytes.

After all checks passed, these exact files were deployed to the VM and both GPU hosts. All ten hashes match on each root, synchronization is restored, and both games/servers are stopped. The final post-sync check preserved all 309 original profile/PFT/settings files per root and confirmed all 28 runtime test files absent. GPU1 was checked for deployment/data preservation only; native gameplay acceptance was on GPU2. No push or release was performed.
