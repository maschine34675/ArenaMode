# Arena availability and Adaptive Bot Culling

## Player-facing behavior

The character screen keeps the Arena choice visible when admission rejects it.
A passive, amber `ARENA UNAVAILABLE` panel above that choice explains the reason
without requiring a hover, click or log inspection. The native PMC/Scav
description, health panel and input remain unchanged. Successful selection and
screen closure remove the owned notice; reopening does not duplicate it.

An explicit selection failure, including a missing/unreachable Arena server,
also stays visible there. Retrying remains possible. Merely opening the screen
does not add a server request or cache an early connection failure.

Missing or outdated Redeploy.Core uses a separate, Core-free startup path.
It adds only native menu diagnostics, not gameplay or respawn hooks; the plugin's
runtime remains disabled. Its text asks the player to reinstall the complete
Arena package and keep one Core copy. Exact loader/version details remain in
the log. PFT's separate My Squad screen is excluded through its reflected
presentation flag, without loading Arena's Core-dependent PFT adapter.

The hint is presentation, not authorization: all existing selection, freeze,
post-settings-refresh and exact server-arm guards remain mandatory.

## Adaptive Bot Culling: Arena-only refusal

Reviewed [published 1.2.0](https://github.com/Hysocs/adaptivebotculling-spt/releases/tag/1.2.0),
GUID `com.hysocs.adaptivebotculling`, DLL SHA256
`67AE69DFC1DDA3F7DFB41CDB424348489D2EADF3E9531EC82BD6AA5964778ACB`.
[Forge entry](https://sp-mod.com/mod/2874/adaptive-bot-culling).

The released DLL was inspected, not only its tag: the tag points at
`66d0e50e39deba7191a45f09f11becb051d0e54d`, whose source still describes the older
1.1.0 implementation. The published 1.2.0 seams match the
[inspected current source](https://github.com/Hysocs/adaptivebotculling-spt/blob/b1273b6ffa06ecb96294c57ac75e6850f250c29f/Client/Plugin.cs).

- `ApplyBotSuppression` ends shooting, drops aim, stops movement and sets speed
  to zero. Manual/fixed/brain and perception callbacks can skip native work.
  A bot can still look Active to Arena's roster.
- `EvaluateBotVisibility` uses distance/optic direction and PerfectCulling room
  visibility. Nearby bots behind room boundaries can therefore also pause.
- `UpdateBotPresentation` changes animation/audio. `PrepareBotDeathAnimation`
  and `ReleaseDeathAnimation` change the animation graph during death/corpse
  creation; the latter has no local-player exclusion and is unconditional.

This conflicts with Arena's continuing offscreen combat, spectator phases,
replacement bodies and corpse ownership. F12 AI switches do not undo the
unconditional hooks. Loaded versions are therefore rejected for Arena, including
unreviewed releases; this is a conservative block, not a supported adapter with
an upper-version restriction. No foreign settings, patches or queues change.
Ordinary PMC/Scav starts bypass Arena admission and retain the mod.

Future support requires several exact-world/body guards, including preactivation
and retired bodies, rather than a global switch. Prior native PerfectCulling
errors occurred without this mod installed and are not attributed to it.

## Verification

`pwsh -File tools/ArenaAvailabilityChecks/Verify.ps1` runs the production-linked
native menu/lifecycle and optional-mod admission harnesses. ABC cases cover
late discovery, known/future/missing version metadata, removal and actionable
rejection; a compiled fault control must fail if its block is removed.
Existing tests preserve final admission and the ordinary PMC/Scav bypass.

Offline: 752 native menu/lifecycle assertions, 68 optional admission assertions
and 15 compiled behavioral fault controls pass. Staged client build succeeds
without changing installed files; its four nullable-context warnings predate
this change. BigBrainCompat builds without warnings.

## Native acceptance on SPT-GPU-2

SPT 4.1.6, EFT 0.16.9.5, 1920x1080. Temporary tests used the host's SPT-GPU-2
profile. No personal profile or foreign saved settings were edited.

Implementation `1621213`; layout correction `a6919d3`. The first screenshot
revealed overlap with the shared native description and deferred text-position
drift. The corrected panel follows the toggle's center in LateUpdate and sits
above the choice. The following passes used the corrected build:

| Case | Observed result |
|---|---|
| ABC 1.2.0 loaded, 10:40 client | Reason visible immediately, no hover/click required; Arena disabled. PMC/Scav remained selectable, original description/Next unobscured. Back/reopen retained one notice. |
| Core DLL absent at cold start, 10:42 client | Arena and standalone Redeploy rejected initialization. Arena's three diagnostic menu hooks still loaded; the installation advice was visible. PMC/Scav selectable; reopening clean. |
| Missing Core, PFT My Squad | Squad roster/add-teammate UI retained, no Arena toggle or notice. No companion was added/removed/invited. |
| Core restored, ABC removed, 10:44 client | No warning; Arena selection produced ARENA ZONE. All 41 plugins and six libraries matched between development, host disk and loaded process. |
| Positive Factory Gun Game | Eight participants selected, raid started 10:46:32; population at 10:47:03 showed seven living PMCs plus the human slot. Native Stop(Left) at 10:47:11, snapshot restored and protected completion persisted at 08:47:12–13 UTC. Returned to MainMenu. |

The final positive client log `LogOutput-SPT-GPU-2-2026-10-20_10-44-53.log`
contains no Error lines. Missing-Core testing intentionally produced the gate
errors and foreign assembly-scanner/Unity JSON converter type-load diagnostics;
the menu remained usable. This diagnostic UI is not a promise that a broken
installation can run an ordinary raid safely. Reinstall Core before playing.

Deployment (both roots):

- Client SHA256: `F25CE88281107E946032194D46980389F6617CEA864667D3806B7624C37A6210`.
- BigBrainCompat SHA256: `39C3AB6204A2E5A4819ADF621B2E950AC7059180DB8C2A960C9173A43EF06261`.
- Original Core restored, SHA256:
  `218967C32C58F83C9BA2044888F12F595655D752172049DE57FA9C51FD4B231C`.
- Arena server unchanged. No release or push in this task.

Raw local evidence: ignored `artifacts/availability-native-20261020/`, including
`abc-ui-r2`, `core-missing-ui`, `core-pft`, `restored-ui` reports/screenshots and
the original installed-file inventory. Temporary ABC DLL/config removed from
both roots. Game and server stopped after the positive test.

Independent final comparison (`cleanup-checksum-final-20261020-085228.json`):
all 1,151 recorded DLL/CFG/JSON files remain present; 1,145 are byte-identical
and four are the intended client/companion updates across both roots. No new
files of those types remain within the recorded plugin/config/Arena-server
trees; ABC's empty directories are gone too. Both Arena config files are
byte-identical, with all 232 settings unchanged. The two remaining changed
files are server `client-settings.json` exports: all 224 exported values match
the original settings. Their previous full JSON bytes were not captured, so an
exact metadata-only difference cannot be proven. This is not an inventory of
unrelated server mods or large map bundles, which were never changed.

Limits: no separate native test of outdated Core, missing Arena server, every
other blocked mod, other UI scales/resolutions or ordinary raids with ABC.
Existing admission/server failure regressions cover their decision paths, not
their individual visual/combined-mod acceptance. No ABC compatibility adapter
or performance comparison is claimed.
