# Local test release ZIP — 2026-10-17

Historical first package. Its layout and manual BigBrain instructions are
superseded by [package layout v2](package-layout-20261017.md); do not use this
older archive to test the new grouped plugin layout.

The user published/renamed the existing GitHub repository to
`maschine34675/ArenaMode`, then requested a **local trial release ZIP**. This
task does not upload a release, change Git history or deploy into an installation.

## Inputs and result

- Arena source: `a72880214750f33486e06c5f2800dd2ec57c4a8f`.
- Redeploy source: `bf227f2c1aa00060d68136c159a16bc13addc0c8`.
- Arena DLLs: **0.1.0**, Redeploy.Core: **1.0.7**, all **Release** builds.
- Archive: `ArenaMode-0.1.0-test-a728802.zip`.
- Size: **3,651,293 bytes**, **88 files**.
- SHA256: `586f1e3fba9e18d2fa13bd2fb096d06eba87354370501ea830de9e7a1e7a7b47`.

The archive and its `.sha256` sidecar are under the ignored local directory
`artifacts/test-releases/660f67708d4a47809c1a5ff638ec54c3/`.
Only the named ZIP is a player package. Source snapshots, build outputs, logs,
test results and deliberately invalid test ZIPs beside it are not distributables.

The fixed Arena revision deliberately excludes another session's uncommitted
failed-start changes. Its explicit hash remains authoritative if `master` moves.
This does not claim a new in-game test of the assembled ZIP.

## Package contents

```text
BepInEx/plugins/
  maschine-ArenaMode.Client.dll
  maschine-Redeploy.Core.dll
  ArenaMode/                            audio/assets and their notices
    MapPreviews/                        map image, source and attribution
    Ui/                                 readable icon notices
SPT_Runtime/user/mods/ArenaMode/
  maschine-ArenaMode.Server.dll
  config/kits.json
  config/ladder.json
Optional/BigBrain-1.5.0/BepInEx/plugins/
  maschine-ArenaMode.BigBrainCompat.dll
INSTALL.txt
README.md
LICENSE
REDEPLOY-CORE-LICENSE.txt
THIRD-PARTY-NOTICES.md
PACKAGE-MANIFEST.json
```

Only copy the optional companion into the real plugin directory when BigBrain
**1.5.0** is installed. BigBrain and other optional mods/maps are not bundled.
Ui/HUD artwork is already embedded in the client. The complete tracked audio
asset set includes its eleven reserve recordings, keeping the supplied manifests
and notices consistent.

User settings, profiles, snapshots, logs, diagnostic tools, PDBs and copied
game/Unity/SPT/BepInEx/foreign-mod DLLs are excluded. The supplied kit/ladder
defaults can overwrite customized catalogs: back them up before installation.
Retain the notices; the aggregate package is not wholly MIT. This local test
package does not resolve the separate publication/history/asset-rights review.

## Rebuild and verification

Requires PowerShell 7, Python 3, .NET 10, installed SPT 4.1 game references and
BigBrain 1.5.0 for compiling the optional companion. The sibling Redeploy checkout
is required. The script uses committed snapshots, not installed mod binaries:

```powershell
pwsh -File tools/Package-TestRelease.ps1 -SptPath D:\SPT41
```

By default, tracked source changes must be committed first. To intentionally
package a specific committed Arena baseline while unrelated work is ongoing:

```powershell
pwsh -File tools/Package-TestRelease.ps1 -SptPath D:\SPT41 -ArenaRevision a72880214750f33486e06c5f2800dd2ec57c4a8f
python tools/Verify-TestRelease.py <path-to-created-zip>
```

The packager creates a unique output directory without deleting previous work.
It suppresses all three automatic deployment targets, checks installed Arena/Core
file hashes and the unused server deployment sentinel, and stamps each assembly
with its own frozen source revision. Direct project-graph builds avoid the
solution's fallback to Debug for the external Core project. The initial trial
caught that mismatch before a ZIP could be accepted; it was corrected in the
packager, not by changing gameplay/build project files.

Final checks:

- Build: zero errors; four existing client warnings, two server warnings and
  thirteen warnings from test/linked rule sources.
- Existing unit suite: **1,016 passed / 2 skipped / 0 failed**. The skips are
  native APBS and installed-PFT integration checks, not gameplay acceptance.
- Reopened ZIP: exact four-DLL set, safe unique paths, manifest coverage,
  per-file SHA256/length, JSON defaults, required notices and CRCs pass.
- Seven negative controls rejected: foreign DLL, missing Core, changed DLL,
  traversal path, case-colliding entry, user config and optimized Python.
- Binary-package Markdown references have valid local targets or encoded source
  URLs, including the license filename with spaces.
- Gitleaks **8.30.1**, official default rules, full redaction, no package ignore
  entries: **0 findings / exit 0** on the exact package tree. This is a scoped
  payload scan, not a new scan of the public repository's history; standard
  binary/media exclusions still apply.
- Independent packaging review completed; installed files unchanged.

Detailed evidence stays in the ignored run directory. No upload, deployment or
new GPU session was performed.
