# Quiet Arena quest, achievement and XP notifications

Run from the repository:

```powershell
dotnet run --project tools/ArenaNotificationChecks -c Release
pwsh -NoProfile -File tools/ArenaNotificationChecks/Verify-InstalledContracts.ps1
pwsh -NoProfile -File tools/ArenaNotificationChecks/Verify-Regression.ps1
```

These tools never build a deployment project, change installed DLLs, modify
NotificationFilter configuration, or touch profiles. The contract checker uses
read-only Mono.Cecil metadata from the installed game and NotificationFilter.
The behavioral harness directly links production `ArenaProgressNotifications`;
native UI, quest/achievement callbacks and Harmony dispatch are small explicit
models, not a Unity or GPU test. No production progression method is skipped.

Native source evidence under `../_decompiled`:

- `EFT.Quests/QuestControllerClient.cs:115`: subtask notify only plays
  `QuestSubTrackComplete` and emits `NotificationQuest`; it does not advance
  quest progress. This is the direct audio path separate from popup sounds.
- `EFT.Quests/QuestControllerClient.cs:64`: quest status presentation emits a
  typed `NotificationQuest` (including empty-text sound-only cases).
- `EFT.Achievements/AchievementsControllerClient.cs:29`: achievement notify only
  displays a popup or loads its sprite first. `method_5` can complete after the
  raid; blocking the notification-only entry prevents that delayed Arena popup.
- `EFT.Communications/NotificationManager.cs:515`: static display feeds
  `AddNotification`, which retains history and notifies subscribers. Arena does
  not patch either method or the progression/network notification pipeline.
- `EFT/LocalStatisticManager.cs` and `EFT/OfflineStatisticManager.cs` emit XP
  toasts as `StatisticNotification` through that same display path. This type
  is now suppressed only at the two UI boundaries, including direct or queued
  display. Notification history and other subscribers still receive it. Skill
  level-up messages, Arena announcer text and ordinary warnings remain unchanged.
- `EFT.UI/NotifierView.cs:256`: the UI subscriber either displays immediately or
  enqueues. Its selective Arena gate prevents messages leaking out of the raid
  through an unprocessed queue.
- `EFT.UI/NotifierView.cs:157`: final `ShowNotification` creates the view and then
  plays its sound. A second gate catches direct and preexisting queued entries.
  It returns false so `ProcessQueuedNotifications` releases its busy flag.

Read-only reference: the user's MIT-licensed NotificationFilter
(`../NotificationFilter/LICENSE`, copyright 2026 maschine34675). Its
`SilenceRaidSubtaskSoundPatch` replaces native subtask sound with a typed popup
when volume is below one. Arena runs before that prefix and also blocks such a
replacement at both UI boundaries, without changing NotificationFilter's
settings or any global sound source. This is an original narrower integration;
no NotificationFilter code or binary is bundled.

Coverage: quest/achievement/XP types, immediate/queued/direct display, native
queue release, unchanged history/other subscribers/profile-change behavior,
unrelated notifications and Arena sounds, normal raids and stale game/world
pairs, shutdown/re-enable/partial-install failure, conditions and achievements
still progressing, delayed achievement sprites, NotificationFilter installed in
either order at volume 0/0.3/1, and independent replacement notifications. The
mutation runner changes only scratch copies under ignored `obj`, rejects compiler
failures as evidence, checks the original source hash and reruns the baseline.

Four passes: implement all presentation paths; verify native notification-only
contracts including delayed achievement work; review exact raid ownership,
NotificationFilter ordering, queue busy-state and partial-install cleanup; then
polish source/docs and rerun baseline, installed contracts and behavioral red
controls. Root performs a separate review before deployment. Actual HUD/audio
behavior with both mods installed remains a GPU playtest handoff.
