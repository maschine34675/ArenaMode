# Optional mod versions — 2026-10-19

The user requested forward-version admission like the existing BigBrain 1.5.0+
adapter. Version numbers no longer impose an upper bound on the four adapters
below. This permits attempting their existing APIs, not silently accepting
missing or changed contracts and not certifying unreleased implementations.

| Integration | Admitted versions | Version authority |
|---|---|---|
| SAIN | 4.5.1 or newer | Loaded assembly named `SAIN` |
| PitFireTeam | 0.10.0, or 0.10.3 or newer | Matching client plugin/assembly identity; loaded server assembly |
| APBS | 2.4.1 or newer | SPT mod metadata for `com.acidphantasm.progressivebotsystem` |
| DynamicMaps | 1.2.1 or newer | BepInEx metadata for `com.mpstark.dynamicmaps` |

The table's latest lower bounds are the reviewed baselines. A higher version
logs one advisory per process/integration (PFT has separate client/server
processes). Actual contract failures retain their own warnings and fail-safe
behavior. Missing optional mods remain optional and do not produce a new warning.
SAIN shares its advisory across six adapters, including later door-guard rebinds.
APBS/DynamicMaps advise after successful binding; advisory state is independent
of failure reporting and does not reset at each raid or adapter restart.

PFT 0.10.1 and 0.10.2 were not reviewed and remain excluded: opening upward from
the latest reviewed baseline does not fill older compatibility gaps. Numeric
three-part and four-part zero-revision identities compare equally; future
nonzero revisions and major versions are admitted. Unknown or malformed values
remain rejected. APBS uses native semantic-version precedence instead: a
2.4.1 prerelease is below the minimum, build metadata does not increase precedence,
and a later prerelease such as 2.4.2-beta.1 is admitted with the newer-version
warning. Its assembly version remains unrelated to the mod metadata version.

## Protection and scope

- All reflected types, fields, method signatures, synchronous-method checks,
  getter/method IL checks and required active Harmony prefixes remain required.
- PFT's `SetTeammateProficiency` persistence protection was previously required
  only for exactly 0.10.3. It is now required for **every version from 0.10.3**;
  future admission cannot skip that protected save route. Missing or altered
  contracts still prevent protected PFT admission. Raid, profile, body, roster
  and teardown identities are unchanged.
- APBS changes only Arena-owned level-range generation. Normal raids, protected
  PFT loadouts, native generation ownership and finalizer cleanup are unchanged.
- DynamicMaps still removes only exact successfully trimmed bodies' own markers,
  preserving provider lifetime, native body ownership and release safeguards.
- BigBrain already admits 1.5.0+. Killcam and MapPort use API/ownership checks
  without an upper-version gate. Core already has a minimum version. No further
  external upper-version gate was found. Own client/companion agreement, SPT
  platform requirements, area/map passports, protocols and the Fika exclusion
  remain deliberately unchanged.

No third-party source or DLL was added. README, Forge text, dependency inventory,
agent instructions and the APBS/DynamicMaps review notes describe this policy.
Older native-test reports retain their original reviewed versions.

## Verification

- Full server/unit suite through `tools/ApbsCompatibilityChecks/Verify.ps1`:
  **1,233 passed, one skipped** (installed Factory candidate, unrelated).
  The APBS installed integration test ran, rather than skipping. Evidence:
  `artifacts/apbs-compat/tests/c3fb1bfd91e3498aabbe1e43303994ce`.
- APBS installed contract and copied-source stale-identity negative control
  passed. New tests cover the semantic floor, actual generation through the
  installed API under synthetic future metadata, inactive prefix, missing API,
  duplicate/unknown identity and advisory/failure independence.
- SAIN: 85 version/door-guard assertions; five other adapters compiled under a
  synthetic 4.6.0.0 identity passed 176 / 118 / 4,315 / 149 / 104 assertions.
  Three new compiled controls detect a restored ceiling, removed floor and
  repeated advisory. All five existing safety regression scripts also passed.
- PFT: 79 initial targeted units, followed by two additional three-part-version
  resolver cases in the final full suite, including actual resolver rejection of
  missing, wrong-return, wrong-session and static proficiency contracts in
  future assemblies. Client harnesses: 370 compatibility, 500 lifecycle,
  262 ported map, 195 Zombie plus 19 rejected mutants, 1,130 goal handoff.
  Future 0.10.4.7 / 1.2.0.4 fixtures and installed native contracts passed.
- Independent adversarial reviews of PFT/APBS and SAIN/DynamicMaps left no
  unresolved finding. Existing protection checks were specifically reviewed;
  the final PFT source assertion was aligned with normalized revision comparison
  after review, and its 370-assertion harness reran successfully.
- DynamicMaps: installed contract checks, 84 assertions and 11 rejected compiled
  mutations passed, followed by the complete existing corpse-cleanup regressions
  (309 assertions plus negative controls). A restored upper bound and missing or
  repeated forward-version warnings are explicitly detected.

Future-version fixtures exercise production hooks against synthetic metadata or
assemblies. They prove that the numerical ceiling is gone and structural failures
are still rejected; they cannot prove compatibility with an actual future mod.

## Build, deployment and native startup

Release client, bundled BigBrain library and server builds passed. Existing
nullable-context/server analyzer warnings remain; no new build error. Offline
builds used `NoDeploy.targets` / an absent `SPTUserPath` and the APBS verifier
confirmed installed files unchanged. The final binaries were then deliberately
deployed locally and synchronized to SPT-GPU-2, with hashes verified before start:

| DLL | SHA-256 |
|---|---|
| Client | `5EA9C7F517338F8C14B0AE9890A8D4F3020427CEF2A3FD465BDFC1F9623C44FD` |
| BigBrainCompat | `0B9E4E7875A75F5E9AC10B3B4169C5790758E75E7A9D4B287AE5A777ABFA218A` |
| Server | `0816F9BFB5A4A72B9D850AC62228BBF6972B9CB478081560292A7D2CC2331246` |

The client MVID is `af18a08d-3956-4865-94be-cb2da3fee8c9`. Core, Killcam,
third-party DLLs and map assets were not changed. No release or push was made.

Fresh GPU2 process `78fdc1ab` reached MainMenu on the test profile, with 43
plugins loaded successfully and all 18 machine-owned plugins plus both libraries
identical between the development installation, host disk and loaded process.
Client log: `LogOutput-SPT-GPU-2-2026-10-19_12-10-47.log`:

- PFT 0.10.3 accepted at 12:10:51.638; Zombie PFT fast path installed.
- DynamicMaps 1.2.1 marker adapter ready at 12:10:51.682.
- SAIN 4.5.1.0 teardown/action/personality/search/bot-tempo adapters ready.
- BigBrain 1.5.0 pursuit/pickup layers registered and movement support ready.

Server log `spt-SPT-GPU-2-20261019.log` confirms APBS 2.4.1 range adapter enabled
at 10:08:56.884 UTC and PFT 0.10.3.0 persistence protection enabled at
10:08:57.270 UTC (11 raid, 20 management, 30 outer-operation and four recovery
contracts, plus global settings boundaries). No Arena warning or error appeared.
The merged client/server/EFT startup window had no errors. Existing third-party
serialization/Harmony, trader and missing-profile fallback warnings were left
untouched; no causal change from Arena was established or patched.

This is native **startup/binding** coverage of the installed baselines, not a new
combat/respawn acceptance run or a claim about actual future releases. No raid
was entered, no test settings changed. The game and server are stopped again
after the check, matching their initial state.
