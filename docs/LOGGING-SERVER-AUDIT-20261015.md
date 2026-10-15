# Server and BigBrain logging audit — 2026-10-15

Scope: all C# log sites in `ArenaMode.Server` and `ArenaMode.BigBrainCompat`,
starting at `9f29f49`. This report covers source classification, not a fresh
server/game log or native gameplay acceptance. Client diagnostics and current
error attribution have separate reports.

## Policy and measured counts

Info retains short availability/configuration notices and completed profile
protection results. Routine setup, route/clone/session mechanics and successful
kit generation belong to Debug. Repeated per-bot objective/fire-escape details
require the client developer switch before their messages are constructed.
Genuine exceptions, incompatible contracts, rejected protected starts and failed
profile persistence remain visible without either Debug or DeveloperMode.

Counts are direct logging call sites, not emitted messages or observed errors.
The two server delegate wiring sites are included; the eight messages emitted
inside `LadderPicker` are listed separately below rather than counted twice.

| Component | Baseline Info / Debug / Warning / Error | After Info / Debug / Warning / Error | Guarded developer calls |
|---|---|---|---|
| Server | 45 / 0 / 24 / 20 | 4 / 41 / 26 / 20 | 0 |
| BigBrain companion | 5 / 0 / 4 / 2 | 1 / 3 / 4 / 2 | 3 |

The server Warning increase is two new warning delegates at the existing ladder
creation/redraw call sites. It does not add two new runtime failure conditions.
The companion adds one concise startup notice; its old two setup notices move
to Debug. The pursuit rejection helper now has separate Debug/Warning branches.

`ISptLogger<T>.Debug(string, Exception? = null)` is present in the locally
available SPT source:
`../_reference/server-csharp-main/Libraries/SPTarkov.Common/Models/Logging/ISptLogger.cs`.
`Logger/SptLogger.cs` implements it as the native Debug level. The server project
references `SPTarkov.Common` 4.1.0, whose restored package is present locally.
No replacement server logger or global filter is introduced.

BigBrain already references the client and is an existing `InternalsVisibleTo`
friend; the three developer messages use the shared `ArenaLog` helper. This does
not introduce another assembly dependency or require making the helper public.

## Every former Info site

Paths in this table are relative to `ArenaMode.Server` unless prefixed with
`BigBrain/` (meaning `ArenaMode.BigBrainCompat`). A count covers all Info calls in
that file at the baseline, including logger delegates.

| File | Count | Classification and reason |
|---|---:|---|
| `ConfigLoader.cs` | 5 | First-time default file creation stays Info, since the user may need its path. Map list, successful kit/ladder reads and missing optional ladder notice become Debug; the runtime generator can replace the optional ladder anyway. Actual missing kit/error paths stay Warning/Error. |
| `PatchManager.cs` | 1 | Info: one server integration enabled notice. Individual patches are not listed at Info. |
| `Patches/AdjustBotHostilityPatch.cs` | 4 | Debug: applying the selected hostility rule or retaining native infected hostility is ordinary setup. |
| `Patches/ApbsBotTierCompatibility.cs` | 2 | Debug: adapter installation and changing level-range details. An unavailable adapter remains Warning. |
| `Patches/BotTierScalePatch.cs` | 1 | Debug: difficulty clock/level ramp details, not an actionable player notice. |
| `Patches/GenerateLocationAndLootPatch.cs` | 1 | Debug: exact location clone, caps, spawn strategy and area/participant parameters. |
| `Patches/PitFireTeamDynamicPersistencePatches.cs` | 8 | Debug: absent optional assembly, successful patch counts and six normal protected-write/transaction suppression paths. |
| `Patches/PitFireTeamFileMutationPatches.cs` | 1 | Debug: expected protected-file mutation suppression. |
| `Patches/PreRaidWipeSkipPatch.cs` | 1 | Debug: expected inventory-wipe suppression. |
| `Routers/ArenaModeRouter.cs` | 2 | Debug: idempotent raid-start replay and end-request deferral while the native result is pending. |
| `Services/ArenaKitSupplies.cs` | 1 | Debug: successful grenade supply preparation. |
| `Services/ArenaProtectedRaidStartService.cs` | 1 | Debug: native start identity binding detail. |
| `Services/ArenaProtectedRaidEndService.cs` | 1 | Info: the protected end completed and the restored snapshot was persisted. |
| `Services/ArenaSessionService.cs` | 7 | Debug: arm reservation/completion, kit variants/supplies and the two routine ladder sinks. Failure messages from those sinks are separated below. |
| `Services/KitPricer.cs` | 1 | Debug: generated kit price range. |
| `Services/LadderGenerator.cs` | 3 | Debug: template index, generated pool and excluded-template counts. |
| `Services/PitFireTeamOperationGate.cs` | 2 | Debug: expected delayed invite/mail suppression during profile protection. |
| `Services/ProfileSnapshotService.cs` | 3 | Snapshot creation/discard become Debug. Successful restoration stays Info, including recovery paths which do not reach normal raid-end finalization. This line proves an in-memory restore, not disk persistence. |
| `BigBrain/ArenaFireEscape.cs` | 1 | Developer only: individual bot escape lease and coordinates. |
| `BigBrain/ArenaPickupObjective.cs` | 2 | Developer only: objective reservation/release for individual bots. |
| `BigBrain/Plugin.cs` | 2 | Debug: layer IDs, priorities and brain counts. One new Info line confirms bot movement support is ready only after registration succeeds. |

## Ladder callback severity

`LadderPicker.Redraw` now accepts an appended optional warning callback. The
server supplies Debug for routine messages and Warning for failures. Existing
callers supplying only one sink continue to receive every message because the
warning sink falls back to that original sink. The draw algorithm, random
selection, returned catalog, exclusion semantics and fallback data are unchanged.

| Message family | Effective server severity |
|---|---|
| Attachment variant report | Debug |
| Attachment draw exception, original drawn builds retained | Warning |
| Borrowed weapon from a neighboring band | Debug |
| Repeated permitted weapon because all other permitted choices were taken | Debug |
| Entire pool lacks permitted ammunition; original excluded load retained | Warning |
| Number of excluded pool weapons passed over | Debug |
| Successful rung/pool draw summary | Debug |
| Draw/JSON exception, original pool retained | Warning |

Previously all eight families shared an Info sink. Merely downgrading that sink
would have hidden the two exceptions and the prohibited-ammunition fallback.

## Warning and Error review

No existing server Warning or Error is downgraded. All 24 original Warning sites
and 20 Error sites remain in the same control paths with the same messages and
exception details. The following inventory covers every file with those levels;
the session row includes the two newly separated ladder warning sinks.

| Server file | Warning / Error | Retained reason |
|---|---|---|
| `ConfigLoader.cs` | 2 / 1 | Invalid/repaired configuration, missing kits and load failure materially affect play. |
| `PatchManager.cs` | 0 / 1 | Rollback of partial PFT protection failed. |
| `Patches/ApbsBotTierCompatibility.cs` | 1 / 0 | Exact adapter contract unavailable or application failed; one-shot warning retained. |
| `Patches/BotTierScalePatch.cs` | 1 / 0 | Difficulty scaling exception; actual fallback rather than normal progression. |
| `Patches/GenerateLocationAndLootPatch.cs` | 0 / 2 | Required mutation failed or solo fallback to vanilla spawns. |
| `Patches/PitFireTeamDynamicPersistencePatches.cs` | 5 / 0 | Discovery, missing persistence guards, unsupported installed version, patch/rollback and exact-contract failures. Absence of the optional mod itself is Debug. |
| `Patches/PitFireTeamFileMutationPatches.cs` | 1 / 0 | Path check threw and failed open; protection diagnosis must remain visible. |
| `Patches/PostRaidPmcSkipPatch.cs` | 0 / 1 | Protected native end identity mismatch. |
| `Patches/TeamDeathmatchArmbandPatch.cs` | 1 / 0 | Armband application exception, not normal equipment generation. |
| `Routers/ArenaModeRouter.cs` | 3 / 6 | Stale snapshot, rejected PFT start, externally disabled bots; failed lifecycle recovery and snapshot/persistence operations. |
| `Services/ArenaKitSupplies.cs` | 1 / 0 | Failed grenade supply preparation. |
| `Services/ArenaProtectedRaidEndService.cs` | 0 / 1 | End transaction failed and is retained for an exact retry. |
| `Services/ArenaSessionService.cs` | 8 / 1 | Delayed PFT drain timeout; expired, wrong-map or invalid admission; kit/supply exceptions; two ladder warning sinks; missing exact native arm identity. |
| `Services/KitPricer.cs` | 1 / 0 | Kit pricing failed and leaves the buy menu degraded. |
| `Services/LadderGenerator.cs` | 2 / 2 | Variants unavailable or empty pool; exclusion-index or pool-generation exception. |
| `Services/OrdinaryRaidLifecycleService.cs` | 0 / 1 | Native ordinary-end failure remains retryable; ordinary raid behavior is unchanged. |
| `Services/ProfileSnapshotService.cs` | 0 / 4 | Missing native store contract, null clone, snapshot exception and failed restore. |

Companion failures retain four Warning and two Error call sites:

- `ArenaFireEscape`: both selection/update exceptions remain Warning.
- `ArenaPickupLayer`: failed selection remains its existing one-shot Warning.
- `ArenaPursuitLogic`: only `route-acquire`, `route-refresh` and
  `unresolved-arrival` use Debug. These are bounded replanning/expired-contact
  outcomes; rejecting the lease and stopping owned movement are unchanged.
  Target-binding/SAIN handoff failures, exceptions and unknown future categories
  retain Warning. Existing once-per-epoch/source/category deduplication remains.
- `Plugin`: dependency/version rejection and registration failure remain Error.

## Verification boundary

Self-review inspected all former Info sites, every Warning/Error path and the
mixed ladder callback. Searches for alternate `Console`, `Log`, `Success`,
`Critical` and `LogWithColor` output found no additional production emitters in
this scope. `git diff --check -- ArenaMode.Server ArenaMode.BigBrainCompat`
passed. The only non-severity structural changes are the ladder warning callback,
the three guarded developer messages and the pursuit logging-only branch; no
raid, profile, spawn, kit, navigation or scheduling decision changes.

The parent owns independent review, regression checks, the shared `ArenaLog`
helper and all builds/deployment. This leaf did not build, deploy, control a GPU
host, modify shared tests or create a commit.
