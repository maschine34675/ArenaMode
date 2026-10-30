# Use Items Anywhere and shared HUD compatibility

Development changes tested on SPT-GPU-2 on 2026-10-25/26. Arena's public version
remains 0.3.0; the shared Redeploy.Core dependency advances to 1.0.14. This is
not a new release announcement.

## Scope

- **Use Items Anywhere 2.1.6:** Arena's three magazine-query patches compose with
  the foreign transpiler in either load order. They retain the native call for
  the foreign patch to recognize; no constructed generic method is detoured.
  When the foreign mod is installed, its slot settings own reach, binding and
  reload access, including settings that disable a feature. Arena's magazine
  panel uses the same candidate query instead of counting inaccessible magazines.
- **ImpactCue 2.0.3:** Core follows the replacement physical player for telemetry.
  Retained corpses and delayed callbacks cannot take the health/ammo display back.
  Raid history, kill feed and foreign settings are retained.
- **Enemy Markers 1.4.0:** Core rebinds existing billboards to the replacement FPS
  camera and excludes the replacement local body from markers. Owned component
  teardown safely removes subscriptions/markers before upstream cleanup, which
  otherwise reads destroyed player objects and aborts with an exception.

The two body fixes live entirely in Redeploy.Core and also serve standalone
Redeploy. They require Core's exact physical body/world ownership; they do not
change unrelated normal raids. Missing optional mods require no adapter action.
Future versions still require matching reflection contracts; whitelist version
review and runtime structural checks are separate protections.

## Native evidence

The released foreign DLLs, including ImpactCue's WebConfig server component,
were installed temporarily. Loaded plugin identities and candidate Core hashes
were checked after restarting EFT. A temporary read-only probe recorded existing
body/camera/marker identities and HUD values; it did not repair foreign state.

- Standalone Redeploy, Arena client absent: two deaths and actual respawn-prompt
  choices. Both physical player and camera changed. ImpactCue showed damage to
  the new body after respawn; an existing enemy marker retained its object and
  followed the new camera. Screenshots were inspected.
- Arena Deathmatch: two automatic respawns with retained old bodies. ImpactCue
  health and ammunition followed the new body; existing enemy markers retained
  their objects and current camera; no local-player marker appeared. Arena kill
  score/history continued across the replacement.
- Real shotgun and M4 firing/reloading worked. With foreign reload slots set to
  Backpack only, R left the partial magazine unchanged while all spare magazines
  were in the rig. Native inventory movement of one magazine into the backpack
  made R work. The Arena counter changed from zero to one and back to zero.
  Restoring rig/pockets returned the expected full/partial magazine counts.
- Native grenade draw/throw consumed one grenade. The test uses
  `PressThrowGrenade`, `TryHighThrow`, then `FinishHighThrow`; `ThrowGrenade`
  alone selects a grenade and is not proof of a completed throw.

The first exit exposed Enemy Markers' destroyed-body cleanup exception in both
drivers. It was fixed before final acceptance; see the final verification below.

## Verification and limits

Offline checks cover both transpiler orders, exact foreign metadata and API
drift, absent mods, foreign slot restrictions, destroyed bodies/cameras,
preparation cancellation, failed replacement, retired worlds, old callbacks,
original teardown completion and preservation of unrelated subscribers.

Local raw evidence is under `artifacts/compat-items-hud-20261025/native` and is
intentionally untracked: logs/profile backups contain private test data.

ImpactCue's pre-existing startup warning about `EftBattleUIScreen.Show` being
unpatchable still occurs; its fallback telemetry display worked in these runs.
Other baseline third-party/Unity material warnings are not silently suppressed.
Enabling both HUDs can overlap panels; select the desired HUD via existing
settings. These tests do not establish every foreign configuration or game mode.

## Final verification and cleanup — 2026-10-26

- A further standalone respawn/exit and Arena respawn/exit passed with the final
  Core DLL. Both returned to the main menu without Enemy Markers' exception or
  a Core compatibility warning. This brings the total to three respawns per
  driver. The standalone exit still logged Unity's inactive MenuScreen coroutine
  message, also seen before the cleanup correction; its cause was not established.
- Arena: 3,209 passing unit tests, one known environment-gated APBS skip. Redeploy:
  283 passing unit tests. Targeted checks: UIA's four 80-assertion load/priority
  permutations plus absent/drift/future-version and mutation cases; backpack
  reload 54 assertions/6 mutations; HUD 382/37; ImpactCue 81; Enemy Markers 64;
  shared coordinator 504/9. Release builds succeeded; Arena retains four existing
  nullable-context warnings in ClientSettingsModels.
- Default whitelist entries include the three reviewed clients and ImpactCue's
  separate Web Config server GUID. User deny overrides still win. The temporary
  probe is not part of the default policy or any distribution.
- Removed all 33 temporary test files, both testmod directories, generated test
  configs, the user whitelist override and the disabled old Arena DLL. Original
  Arena/Redeploy configs and sync exclusions were restored and hash-checked.
- All nine canonical profile copies were checked. Six unrelated copies were
  unchanged. The GPU2 test profile was restored to its pre-raid synchronized
  state, preserving two earlier legitimately settled rewards and their existing
  payment ledger. No other profile was overwritten; raw before/after backups
  remain local. EFT and SPT are stopped on GPU2.
- Final Core, standalone client, Arena client and default policy are deployed
  identically under D:\SPT41 and SPT-GPU-2. Server/map DLLs were not replaced.

| Artifact | SHA-256 |
|---|---|
| Core 1.0.14, final native candidate and deployment | `FA7289C96FCBAE7FEE97A3D06B2209F3D0AAAFFA2035C7CCF2F1017B93D4841C` |
| Redeploy.Client 1.0.2, final native candidate and deployment | `A1EFADEBB2348A8337978F8C1380D2172A28166EA578F7A4D6D3A77B41D2AA54` |
| Arena.Client 0.3.0, native candidate | `1280122FBA2686F7771B66AF003EBBA007B8DBDAFC68BC848B0EAD527AB4C3F4` |
| Arena.Client 0.3.0, final rebuild/deployment | `423EC5F519203270F944B475A0BA353A5DE92F2F2B31DBF74FDDED5F31B6536D` |
| Default whitelist | `1190944139ADE337D7E73DF313789EDB2F62E29F611B54A6058ED57E869FBF83` |

The final Arena DLL was rebuilt against the completed Core after the exit tests;
Arena runtime source did not change between these two binaries. Loaded/disk
MVID equality was verified for the native candidates. No release or push was made.
