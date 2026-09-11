# Native Arena HUD checks

`pwsh -NoProfile -File tools/NativeArenaHudChecks/Verify.ps1` links the production
`ArenaExtractionList` service into a small executable with controlled Unity,
Harmony, game/UI and lifecycle boundaries. It checks ordinary/exact/foreign
session handling, the already-running startup list, borrowed native alpha and
enabled-state restoration, late/replaced UI, repeated frames, native single-O,
double-O suppression, failed-list quarantine and shutdown with failed unpatching.
It also checks the separately restored banner activeSelf, an initially hidden
banner, and rejection/recovery when the clock is inside that banner. These
hierarchy stand-ins reflect the installed scene; they do not render it.
The GameObject contract now rejects a second CanvasGroup by returning null and
recording the engine error, as observed in the 2026-09-11 GPU test. The earlier
fake incorrectly allowed duplicates and could not catch that regression.
Copied source faults disabling the prefix, reintroducing duplicate AddComponent
or omitting alpha restoration must compile and fail their behavioral assertions.
Banner omission and removing the independent-clock guard are also rejected.
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
  main-timer/list animation; only `_timersPanel`'s existing CanvasGroup is borrowed.
- `EFT/AbstractGame.cs:229`: actual extraction countdown uses the separate
  BattleUIPanelExitTrigger and is untouched.

The list's existing startup coroutine and its temporary raid-clock movement
are allowed to finish normally; no private coroutine state, visited status,
extraction subscription, timer value, hierarchy or input setting is changed.
For the list, only its existing group's alpha/enabled values are overridden
and synchronously restored at release. No component is added or destroyed.
Missing/failing groups leave the native path open and quarantine that list
until a new list or raid, with one exception+stack diagnostic instead of retries.

The r2 GPU report validated the list but exposed its separately rendered green
banner. Installed `EscapeFromTarkov_Data/level46` is GameUIScene.unity (SHA256
`9F87BA3A435D50ABFD289F1F46BC47895360748D4ADDB7EBA568F84293DD8E16`).
ExtractionTimersPanel component 1293 references `_mainDescription` Transform884,
GO62: MainTimer/DescriptionMask/Description, containing Image, Icon and State
(component1407). Numeric TimeLeft (component1415) is under sibling Clock with
its own CanvasGroup1220. DescriptionMask does not draw its mask graphic.
Only Description.activeSelf is now temporarily disabled/restored; runtime
IsChildOf guards validate State inside and Clock outside before every write.
Native Co_MovePanel still runs on the unchanged ExtractionTimersPanel owner.
An altered/failed banner leaves the separately working list mask intact.
