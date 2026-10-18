# BigBrain library and Redeploy server boundary

Date: 2026-10-18. Scope: remove the extra BepInEx plugin registration without
merging optional BigBrain types into the main client. No live deployment or
publication is part of this change.

## Responsibilities

Redeploy.Core is a plain local respawn library. RespawnEngine uses the existing
EFT session/player factory and caller-supplied loadout/protection callbacks;
ArenaRespawnDriver supplies those callbacks. Core neither calls `/redeploy/ping`
nor checks for Redeploy.Server.

The standalone Redeploy.Client's ServerModCheck performs that ping. Its server
patches repair the equipment root after native inventory/postraid imports.
Confirmed Arena raids instead bypass normal PMC postraid processing through
PostRaidPmcSkipPatch and restore their protected snapshot through
ArenaProtectedRaidEndService/ProfileSnapshotService. These are different owners
of the two workflows, not a missing Core dependency.

Inspected sources: sibling Redeploy's `Redeploy.Core/Engine/RespawnEngine.cs`,
`GameAccess/LocalGameAccessor.cs`, `Engine/DeathInterception.cs`,
`Redeploy.Client/Services/ServerModCheck.cs`, `Services/RespawnService.cs`,
`Redeploy.Server/Patches/SetInventoryPatch.cs`, `PostRaidPatch.cs` and
`RedeployRouter.cs`; Arena's `Respawn/ArenaRespawnDriver.cs` and the server
patch/services named above. An independent read-only agent confirmed this trace.

## Library contract

- Keep `maschine-ArenaMode.BigBrainCompat.dll` alongside the main Arena DLL.
  It now exports `Library.Initialize`, `Tick`, `Shutdown` and API version 1.
  No BepInPlugin, BaseUnityPlugin or own plugin GUID remains.
- The former GUID string survives only as an internal pursuit lease identity,
  preserving ownership checks. It is not registered with BepInEx.
- Main Arena adds a soft BigBrain load-order dependency and owns the library from
  Start/Update/OnDestroy. Start matters: BepInEx sets PluginInfos.Instance after
  Awake. Installed BigBrain initializes its patches in Awake.
- Without loaded BigBrain the client does not access/load the library at all.
  With it, the client checks the adjacent assembly identity/version and API,
  then caches delegates. It never references BigBrain types or enumerates all
  optional library types. The typed registration boundary retains NoInlining.
- BigBrain 1.5.0 remains the tested minimum; newer versions warn and must pass
  the retained layer type/priority/brain membership checks. Older/failed APIs
  leave capabilities disabled. Exact Arena version remains checked.
- Registration happens at most once per process. Failures and shutdown revoke
  capabilities and only the owned consumer before releasing pickup claims.
  No changes to movement policy, exact body leases, paths or normal raids.

## Upgrade and package

Update both Arena client DLLs together and restart EFT. The filename/path stays
the same, replacing the former plugin. Remove old duplicate/flat copies. A loaded
legacy GUID blocks a second owner and produces an actionable error. Client-only
updates with an old DLL are intentionally unsupported.

The package continues to include Arena.Client, Arena.Server, Redeploy.Core and
the plain BigBrainCompat library. Neither standalone Redeploy component nor
third-party BigBrain is bundled. The packager also inspects compiled metadata,
so source-only guards cannot accidentally ship a companion plugin again.

## Validation

Pre-commit runs passed 214 isolated bootstrap/absence assertions and nine
fresh-process loader cases (39 assertions). The metadata check accepted the
real library and rejected the legacy plugin fixture. Affected harnesses passed:
pickup behavior 585, Molotov 138 and team expansion 895 assertions. Initial full
client build had only the four existing nullable-context warnings; incremental
client/library builds had no errors or warnings.

Results are recorded in [the gates](BIGBRAIN-LIBRARY-GATES.md). Offline tests cover
real bootstrap source with absent/broken APIs, actual disk-based client loading
with malformed/legacy fixture DLLs, compiled production metadata, affected bot
harnesses and a frozen release package. Installed Arena/Core files are compared
before/after checks. Independent review found no production blocker and inspected
installed BepInEx/BigBrain lifecycle code directly.

These offline checks do not certify Unity/Mono startup or live AI. At the end of
this package run, native acceptance was pending and prior gameplay tests used the
plugin. The later paired rollout below records the additional native evidence.

## Frozen package result

Implementation: `5bed649a3356a5e69a50fe9c9899c63ea1a5dbe6`.
All five gates met; three command gates rerun successfully, none unmet or abandoned.
The package build passed **1,159 unit tests**, with two optional installed-mod
checks skipped. Package layout/hash verification passed, as did the compiled
no-plugin/optional-reference check. Installed Arena/Core files stayed unchanged.

Local artifact:
`artifacts/test-releases/8624315b548548d0bba50bb1e99500b3/ArenaMode-0.1.0-test-5bed649.zip`.
It contains 77 files / 3,264,556 ZIP bytes, no root files and no reserve audio.
Exactly four DLLs: Arena.Client, Arena.Server, plain BigBrainCompat and Core.
SHA256: `2274917f63ea38be53ca8267cd93a63ebd9a188bac783f62929811c8adf92b1f`.
Gitleaks scanned the complete payload with the default rules and an empty ignore
file: zero findings (`package-gitleaks.redacted.json` in that run directory).

Build, test and scan logs are local artifacts, not shipped. No deployment, push
or publication was performed. Parallel thumbnail commits are independent of this
frozen runtime/package revision.

## Subsequent paired native rollout

The PFT Tactical equipment work deployed both client DLLs from `6fc7ca4` and
verified their exact loaded hashes in fresh GPU2 process `fe952cf9`. The plain
library initialized through Arena, registered pursuit layer 9078, and reported
bot movement support ready. After native PMC death, several Tactical bots used
Arena Combat Pursuit, moved and continued combat; the next round rebuilt its
leader and nine bot bodies. See [the dated native report](GPU-PFT-TACTICAL-KITS-20261018.md)
for deployment hashes, cleanup and evidence limits. This is startup and sampled
PFT Tactical gameplay acceptance, not a new package release or exhaustive
acceptance of every mode, missing-library case or failure path.
