# Unified deployment and canonical plugin folder

Date: 2026-10-17. The user requested identical build/deploy and ZIP destinations,
then renamed the client asset folder to `maschine-ArenaMode`.

## Layout

```text
BepInEx/plugins/
  maschine-Redeploy.Core.dll
  maschine-ArenaMode/
    maschine-ArenaMode.Client.dll
    maschine-ArenaMode.BigBrainCompat.dll
    sounds/, hitmarkers/, pickups/, MapPreviews/, Ui/
    docs/
SPT_Runtime/user/mods/ArenaMode/
  maschine-ArenaMode.Server.dll
  config/
```

Shared Core remains flat, with no additional copy in the Arena folder. The
server location and policy of preserving existing kit/ladder JSON are unchanged.
Runtime audio, `Pickups.json`, `AreaDrafts` and `PickupDrafts` resolve beside
the grouped DLL. Legacy grouped `ArenaMode` and flat installs are still readable;
flat installs prefer canonical assets when present, otherwise existing legacy
assets. New deployments never create the old folder.

The client DLL's compiler output remains in `bin/Release` (or Debug). Pure
build/publish targets stage `maschine-ArenaMode/` assets alongside it. These
directories are not installable release archives. Release ZIPs still contain
only `BepInEx/` and `SPT_Runtime/` at the root; their layout identifier is now
`spt-root-folders-v3`, retaining manifest schema 2.

## One asset writer and bounded migration

`Write-ArenaAssets.ps1` serves build, publish, deploy and ZIP. Its 80 owned
asset/notice files include rewritten local Markdown links, commit-pinned source
links and announcer JSON paths relative to `docs/`, while preserving audio/legal
bytes. Unknown local/untracked source files are not shipped from a checkout.
It validates the full source payload before writing, skips byte-identical files
and never removes custom destination files. INSTALL and the full archive manifest
remain release-only documents.

`Deploy-ArenaClient.ps1` accepts only the two named own assemblies with matching
assembly identities. It checks migration paths, rejects junctions/reparse points,
and checks locks before changing either layout. One legacy asset folder is
renamed directly; if both folders exist, legacy-only files are merged and the
original folder archived. Different custom files at the same path abort before
writes. Archived legacy folders/conflicts, replaced DLLs, flat DLL/PDBs and stale
Release symbols remain recoverable under
`BepInEx/backups/maschine-ArenaMode-layout/<unique>/`, outside plugin scanning.
The shared writer refreshes canonical standard assets without backing up every
previous asset; their shipped versions are recoverable from source/package.
Core, server and BepInEx config are not modified by this helper.

It copies and verifies a DLL before replacing its target; unchanged binaries do
not create redundant backups. This is recoverable migration, **not a transaction
across the entire tree**. If interrupted after the folder move or asset copying,
rerun the deployment. Do not promise rollback to a byte-identical installation.

The existing three no-deploy target overrides remain authoritative, including
Core's separate `SPTClientDeploy` and server `DeployToSpt`. New Stage targets only
write build/publish output. PowerShell 7 is required for these build tools.

## Tooling and validation

Active deployment checkers use the canonical folder. Offline helpers additionally
observe all twelve possible Client/Compat DLL/PDB paths (flat, old grouped and
canonical), including absence. This catches accidental new installs instead of
only comparing old files. Historical reports keep their original paths.

The historical AnnouncerDelivery helper now uses the same payload/migration
helpers and binds a current staging manifest. Its earlier four-file rollback
contract no longer describes the full payload migration; it shares the recovery
limits above and was not used for this deployment.

- Shared-writer fixtures: identical 80-file deployment/release payloads; repeated
  writes preserve custom layouts/drafts/audio/docs and avoid timestamp churn.
  Corrupt audio, destination directories and junctions fail before writes.
- Package fixture (old DLLs, layout-only): 88 files, 36 local links, 52 announcer
  provenance records. Bad root/path/foreign DLL/audio/source-link controls fail.
- Existing-helper fixtures: 57 assertions; all changed PowerShell scripts parse.
- Deployment fixture runs the real MSBuild client target against scratch roots,
  including its shared asset hook, never writing the actual game installation.
  It checks fresh/legacy/coexisting layouts, Debug/Release, user files, Core/other
  mods, conflict/lock/junction refusal, repeat deployment and NoDeploy overrides.
  All 32 checks pass (`deploy-layout-check-d695d6423f0b450e9b0a70f28b5f058e`).
- Actual client/companion Release build with all deployment targets suppressed:
  passed, with four existing nullable warnings and no errors.
- Resolver tests: 12 passed. Announcer production checks: 335 passed. Hitmarker
  production checks: 54 passed. Independent adversarial review: no blocking finding.

Final build, package and installed-file evidence is recorded below when complete.
These are deployment/file checks, not native gameplay acceptance.
