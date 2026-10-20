# Arena availability and Adaptive Bot Culling

## Player-facing behavior

The character screen keeps the Arena choice visible when admission rejects it.
A passive, amber `ARENA UNAVAILABLE` panel below that choice explains the reason
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

Native UI evidence and deployment identities are recorded after the GPU test;
offline tests alone do not establish Unity layout or missing-assembly behavior.
