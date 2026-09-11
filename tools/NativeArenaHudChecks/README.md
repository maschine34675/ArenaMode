# Native Arena HUD checks

`pwsh -NoProfile -File tools/NativeArenaHudChecks/Verify.ps1` links the production
`ArenaExtractionList` service into a small executable with controlled Unity,
Harmony, game/UI and lifecycle boundaries. It checks ordinary/exact/foreign
session handling, the already-running startup list, own versus borrowed masks,
late/replaced UI, repeated frames, native single-O, double-O suppression,
failure recovery and shutdown with failed unpatching. A copied source fault
disabling the double-O prefix must compile and fail its behavioral assertion.
Production is rerun afterward; nothing is deployed or changed in game config.

This is not a native Harmony call, a Unity-rendered mask hierarchy test, or a
pixel-fit test of the short menu copy. The real client build verifies installed
APIs. The separate GPU job supplies actual input, rendering and normal-raid
restoration acceptance.

Native references used for the implementation:

- `EFT/GamePlayerOwner.cs:779`: DisplayTimerAndExits calls ShowTimer(true,true),
  DisplayTimer calls ShowTimer(false).
- `EFT/BaseLocalGame.cs:781`, `EFT/TarkovApplication.cs:3012`: native SetTime
  starts the list before GameWorld.OnGameStarted / confirmed Arena activation.
- `EFT.UI/ExtractionTimersPanel.cs:477` and `:533`: display requests and separate
  main-timer/list animation; the owned CanvasGroup masks only `_timersPanel`.
- `EFT/AbstractGame.cs:229`: actual extraction countdown uses the separate
  BattleUIPanelExitTrigger and is untouched.

The list's existing startup coroutine and its temporary raid-clock movement
are allowed to finish normally; no private coroutine state, visited status,
extraction subscription, timer value or native CanvasGroup is overwritten.
