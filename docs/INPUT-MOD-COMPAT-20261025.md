# Input and animation compatibility — 2026-10-25

This review covers Auto Shoulder Swap, Increase Look Direction and Magazine
Check Interrupt on SPT 4.1.6 / EFT 40743. Native testing uses SPT-GPU-1 and its
test profile; GPU-2 is reserved for concurrent work. The controlled Arena
client/server source is `ab62cd2`, separate from the main checkout's unfinished
Tarkova-Cola work. Source findings are distinguished from native observations.

## Exact releases

| Mod | Reviewed release | Actual plugin GUID | DLL SHA-256 |
|---|---|---|---|
| [Auto Shoulder Swap](https://github.com/minihazel/StanceSync/releases/tag/1.1.1) | 1.1.1 | `hazelify.StanceSync` | `96FBF674BFD39E428033A75E8791CDFBE108EC9D13EE75298533219CA668F8F2` |
| [Increase Look Direction](https://gitlab.com/flir063-spt/increaselookdirection/-/releases/v1.3.0) | 1.3.0 | `com.flir.increaselookdirection` | `1388339E1286CD142614879D995FBCA867F91AA747BBFD8EFFB221503C50299A` |
| [Magazine Check Interrupt](https://github.com/ozen-m/SPT-MagCheckInterrupt/releases/tag/v1.1.1) | 1.1.1 | `com.ozen.magcheckinterrupt` | `CDA9126EFEBF12B3811B0D0167AFCC3179762CD37A22B23869BC127224EDA0B5` |

Source revisions are respectively `fddb7171527c1eea9a91a394767f0d9d7cd8345e`,
`4f428a46d47c51a88c18211aa1a1bdaa1e4a4211` and
`e92f868eec7cc75809afb3a9c4ce5ce084ce379f`. Released DLLs were inspected alongside
the exact source, rather than relying on listing metadata or default branches.
Auto Shoulder Swap's Forge GUID differs from its actual plugin metadata.
Magazine Check Interrupt's Net DLL is a library, not a second whitelist unit.
None of these packages contains a server mod or profile schema extension.

## Source findings

Auto Shoulder Swap reads the current main player on each call and has no active
body cache requiring a handover. Its lean postfix, however, casts the hands to
a firearm controller and reads `IsAiming` without checking the cast result.
Native lean commands are also available with non-firearm hands. GPU1 confirmed
the failure with a real knife controller and left-lean input: the preceding
`ProceduralWeaponAnimation.CurrentScope` read throws `ArgumentOutOfRangeException`
inside the mod's lean postfix. Firearm lean/reset worked before and after body
replacement. Version **1.1.1 is therefore not whitelisted**. This ordinary
non-firearm bug does not justify a Core body-replacement adapter. Its HollywoodCam
incompatibility is an upstream declaration and is not bypassed here.

Increase Look Direction writes the global horizontal limits and modifies the
current procedural weapon animation's camera target. Its static state contains
configuration and value types, with no cached body or camera. No body adapter
is indicated. Its experimental camera lift defaults off.

When Fontaine FOV Fix 4.1.0 is also installed, that mod replaces the local
firearm free-look path and uses its own **Free Look Angle**. Increase Look
Direction's left/right limits then do not determine the armed horizontal range;
configure FOV Fix's value for that case. This overlap also exists in ordinary
raids and is not an Arena respawn defect. The optional camera lift is separate.

Magazine Check Interrupt's floating panel is a process singleton attached to
the old player. It captures the FPS camera only when created. Core replaces
that camera while the corpse may remain, leaving a live panel with a retired
camera. Its deferred hide callback also lacks body/display generation checks.
GPU1 reproduced this on Core 1.0.11: panel and parent retained, cached camera
destroyed, then repeated `FloatingPanelController.LateUpdate` errors on the next
check. With only the floating display disabled, check-to-reload and shooting
worked. The correction is implemented in **Redeploy.Core 1.0.12**, shared with
standalone Redeploy; Arena has no duplicate hooks.

Its normal check-to-reload transition uses the native reload operation. Its
UIFixes **Reload in Place** route uses a custom swap operation outside Arena's
deliberately narrow reload-speed hook. That custom animation therefore does not
receive Arena's reload multiplier. This tempo limitation alone does not prove
inventory loss or justify broadening the hook to arbitrary foreign operations.

Detailed local evidence lives under the isolated checkout's ignored
`artifacts/compat-three-input/`: release identities, source/IL, test snapshots,
configuration/profile backups and native reports. Foreign binaries and profile
data are not distributed.

## Native acceptance and whitelist decision

SPT-GPU-1 ran protected Factory daytime Deathmatch with two participants and
supplied kits. Actual Core code was `1.0.12+1a3eb10`, SHA-256
`D4ED82087D8C9B2D44EA0A7D8249E50E4325A72489DB660C204ADFC9172B6451`;
host disk and loaded assembly matched. Arena client/server were the controlled
`ab62cd2` build, intentionally separate from concurrent development files.

| Mod | Observed result | Decision |
|---|---|---|
| Auto Shoulder Swap 1.1.1 | Firearm lean/reset worked on two bodies; knife/left lean threw an upstream `CurrentScope` error. | Keep unlisted. No global deny or ordinary-raid change. |
| Increase Look Direction 1.3.0 | Genuine `Player.Look` measured ±90°, asymmetric +80/−60°, right cap −80°, and native ±40° when disabled. Body facing stayed unchanged. Lift changed camera target Y from 0.04 to 0.07 and back, including after replacement and weapon switches. | Whitelist 1.3.0. No Core adapter. |
| Magazine Check Interrupt 1.1.1 | Two real deaths during active checks removed old panels. New checks created visible panels attached to each replacement and its new FPS camera. Check→R→real shots and repeated normal-speed Show/Hide passed; reload-skip flags stayed false. | Whitelist 1.1.1 with Core 1.0.12. |

The temporary freelook probe invoked native input on the game thread and read
actual head/PWA/camera values; it did not assign expected angles. Its request
was cancelled on death and restored through native ResetLookDirection only on
the same living body. It is test tooling, not a shipped feature.

The candidate raid produced no warnings/errors. At the menu, Core's magazine
scope was inactive, its active-panel list was empty and no floating panel
remained. One diagnostic appeared during teardown because vanilla returns
players to their pools before Core's end callback, destroying the Player
component while child UI can still briefly exist. Follow-up `5952741` keeps
this specific already-disposed-owner case at Debug without changing destruction
or ownership behavior; live-owner ambiguity still warns. A final real magazine
check and normal raid exit on `1.0.12+5952741` passed with zero warnings/errors,
the expected Debug message, inactive scope and zero remaining panels. The final
loaded Core SHA-256 was
`1BFB4B129D2E6AD653C056E16D236AB9F808321C7DA0148D201E27EC1E6E889D`.

No new standalone Redeploy, Fika, killer-replay, quick-reload, forced UIFixes
SwapReloadOperation or combined FOV Fix/ILD acceptance is claimed. These are
bounded tests of the shared Core path in Arena, not a guarantee for every mod
combination or future release.

## Offline verification

- Core/driver Release builds: no warnings or errors.
- Redeploy unit suite: **251 passed**, including 14 magazine ownership/callback
  cases. Shared lifecycle harness: **19 cases / 412 assertions**, all **9 fault
  mutants** rejected.
- Arena suite: **2,960 passed**, one known environment-gated APBS test skipped.
  The verification wrapper confirmed the installed files were unchanged by
  testing. Whitelist identities and reviewed versions use actual DLL metadata.

## Cleanup

All five temporary DLLs (three mods, magazine network library, test probe),
their generated configs and the temporary user whitelist were removed. The
original synchronisation file was restored exactly; Arena settings match the
current canonical installation. The concurrent Tarkova-Cola client/server
folders were restored with every saved file hash matching. Current canonical
Arena client/server files were restored, and the accepted Core 1.0.12 was kept.

The test profile still contains all **425 original inventory items**, with no
added, missing or changed item records. The profile was not replaced wholesale.
GPU1 game/server are stopped, as at task start. GPU2 received no test input and
has no temporary test folder. No downloaded mod DLL or profile is committed.
