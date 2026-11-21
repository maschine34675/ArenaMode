# WTT Armory whitelist and shared respawn repair

21 November 2026. Development change after ArenaMode 0.6.0; no release or push.

## Admission and implementation

The default whitelist now reviews `com.wtt.armory` client/server **2.0.5**, the actual installed assembly metadata. Forge labels the distribution **3.0.0**. The earlier Content Backport **2.0.7** and CommonLib **3.0.8** update remains in place; see the [initial WTT audit](WTT-WHITELIST-20261121.md). This follow-up supersedes that report's pending Armory admission.

Armory was not safe to admit as content-only. Its cached player survives while Core retains the outgoing body, so the old hands can control the new AN-94's shared Hyperburst counters. Core **1.0.27** now rebinds that state to the exact committed replacement and fences delayed callbacks from retired bodies. The repair is in Redeploy.Core, for standalone Redeploy and Arena alike. Arena has no duplicate adapter; its rebuilt client stamps the new minimum Core version.

The implementation preserves the public Core API and engine statement order, adds no frame polling or scene lookup, and leaves ordinary unadopted bodies unchanged. Future Armory versions still require matching contracts and produce an advisory warning. The complete contract and native evidence are in the sibling Redeploy repository's `docs/compatibility/WTT-ARMORY-20261121.md`.

## Verification

- Core: 637 unit/API/order tests; Armory adapter: 81 production-linked, native-signature and Harmony assertions plus eight rejected behavioral mutants; coordinator: 23 scenarios, 1,048 assertions, 19 rejected mutants. Independent adversarial review completed.
- Arena affected selection: `tools/Run-ArenaVerification.ps1 -ChangedSince 552abd3a` selected 50 harnesses plus xunit. The first run had 50 passes and one failure in an unchanged server-start test; the full xunit suite passed 4,422 tests, with three known environment skips.
- The failed case, `ANoteThatDoesNotNameTheStartedServerNeverExcusesAnUnsettledSession("none")`, expected one fake server start but saw zero. Its six variants passed in isolation, and the complete 568-case `OfflineStartupChecks` harness passed on a sequential retry. `FreeEndpoint` releases a port before `StartNormal` checks/rebinds it, so parallel port reuse can cause this result, but the original exception was swallowed by `Assert.ThrowsAny` and the actual cause is unproved. No production or test code was changed to hide the failure. Both original and retry logs are retained in `artifacts/wtt-whitelist-20261121/`.
- Native GPU2 comparison: unfixed Core 1.0.26 retained the wrong Armory player after a live body swap and reset counters during the held trigger (9/9 shots in two 250 ms bursts, versus the initial 4/4/4). With the candidate, real death/respawn and retained-live-body swaps passed both standalone and in Arena; all four post-transition runs fired 4/4/4 and reset after release. The first pre-respawn Arena baseline had a 5-shot trigger followed by 4/4, with two shots in one frame and no premature reset; that variation is not hidden.
- Armory entered Arena through the new default entry, not a user override. Arena rewards were explicitly disabled and read back before entry. The test probe observed state and used native input, never invoking the fix or writing Armory state.

Native testing covers the AN-94 body transition, not every weapon/content feature in Armory. Own-Scav and forced lifecycle failures were checked offline only. No third-party code or assets are bundled by this change.

## Candidate identity

Loaded and disk MVIDs/hashes matched in each fresh test process. Exact candidate files and production source hashes are recorded under `Redeploy/artifacts/armory-body-20261121/candidate-a/`.

| Component | Product version | SHA-256 |
| --- | --- | --- |
| Redeploy.Core | 1.0.27+42839bd | `5740571b7c07f7b427762393f1f00cbea4586be9b88fd5f38e41b0491df73d3d` |
| Redeploy.Client | 1.1.3+42839bd | `35ac03bea19628a5b05ea9df82bb72b46c21f2e5413f05bb6ae9246639bdfaf0` |
| ArenaMode.Client | 0.6.0+552abd3a | `eaff0bd25daf4e2dabfd590463af8c790db890cdc230c56f29f849370a8edcf8` |
| ArenaMode.BigBrainCompat | 0.6.0+552abd3a | `7a8e2e9f91349c4485fe82a3ef1bf19f51c35643533f06c571645a1c336f5060` |
| ArenaMode.Server, unchanged | 0.6.0+026d4c44 | `35b1781599f3d13e5449176d889a51493d3f590d23d67b1140ea7e8b8c9c626d` |
| Default whitelist | runtime metadata above | `bee97d361cc21f827d37cb5385aefde1dd9e9af468ccec56f1c66ad04119d359` |

The corresponding PDBs are part of the manifest. Later commits record the source and evidence without rebuilding those accepted files.

## Preservation and deployment

The complete Arena clone profile differs after native restore only in its PMC Hideout bookkeeping timestamp. All original profiles, PitFireTeam files, profileData sidefiles and settings match their initial hashes on all three installations. The 3,593-file baseline per host differs only in three expected APBS generation logs. Two temporary setting changes were restored exactly; 99 historical backups removed by SPT rotation were restored additively, without rolling back any canonical profile. Normal new backups and Armory's new default config were retained.

Cleanup removed 22 owned runtime files, three clone-only registry keys and both disposable profile identities from user data. Armory's 954-file payload on GPU2 is unchanged. Both GPU game/server processes are stopped and synchronization is restored. All ten accepted candidate files now match on the VM, GPU1 and GPU2. Native execution was on GPU2; GPU1 received deployment/preservation verification only.

The existing standalone inactive-MenuScreen exit diagnostic and ArenaMapPort/DynamicMaps integration warning were observed but are outside the Armory repair. No Armory/Core compatibility exception was observed.
