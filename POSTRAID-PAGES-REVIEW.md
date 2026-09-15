# Arena post-raid warning and experience page

The first post-raid `SessionResultExitStatus` page remains available with status,
time, kill-list/statistics navigation and the native Main Menu action. Only its
`_warningPanel` is hidden for Arena; the native Left/Killed/Missing warning about
lost items is inaccurate because the protected Arena profile is restored.
Native `Show` sets that panel on each display, so later ordinary raids need no
special restoration. Returning from another result page hides it again.

The XP page is skipped by calling its existing Next continuation,
`SessionResultShowOperation.ShowHealthTreatmentScreen`, directly from statistics.
The native optional treatment/menu completion remains intact. The Main Menu
button has a second path: it queues KillList, Statistics, Experience, Health.
Only when every controller reference matches the bound operation is this array
replaced with KillList, Statistics, Health. Flags, order, original caller array,
treatment availability, cancellation prompt and all other sequences remain
unchanged. Back from treatment therefore returns to statistics rather than XP.

## Identity and lifecycle

`IsActive` is already false at these screens. This is an explicit presentation
exception, bound more narrowly than the existing `LastRaidWasArena` name display:

- Before ArenaSession clears its proven game/world pair at OnRaidEnded, capture
  the exact LocalRaidSettings reference, profile ID and arm epoch. No body or
  GameWorld is retained. A missing/inexact capture leaves the native pages intact.
- The native GameEnd callback waits at least one yield (Utils.Co_Wait uses
  WaitForSeconds, including delay zero). Core's GameEnd postfix and Arena capture
  therefore precede the application callback.
- In TarkovApplication.OnGameEnd **prefix**, require that captured settings
  reference and profile. The application immediately clears _localRaidSettings
  in TryUnloadGame; waiting until ShowSessionResult would always miss it.
- Preserve the distinct application RaidSettings reference for the result flow.
  Bind one exact SessionResultShowOperation in its Init prefix, before any awaits.
  Same-operation ReInit is allowed, foreign operations cannot steal ownership.
- Every UI action checks completed-Arena state, no active/armed raid, unchanged
  epoch, settings reference and profile ID. ConcludeOperation clears ownership;
  every new raid-end capture clears old state too. Ordinary/faulted/non-Arena
  application callbacks fail open. This does not use the old warning text as a
  profile or UI identity.

Do not skip TarkovApplication.ShowSessionResult, alter exit status/XP/profile
values, invoke GoToMainMenu prematurely, or replace the asynchronous result task.
Native GetProfiles, SelectProfile, health checks, completion, event unsubscription
and server profile protection all remain unchanged.

## Evidence and pending user test

`tools/ArenaPostRaidChecks/Verify.ps1` executes the entire production coordinator
against native boundary doubles, including stale/ordinary/foreign scope, repeated
Show/ReInit, both XP routes, history-array ownership, completion and capture faults.
Six compiled defects must fail their intended assertions. It also checks the
installed native callback/panel contracts and existing Arena raid-exit regression.
The full isolated client build verifies Harmony/Unity API compilation. Independent
read-only review verified native callback ordering and navigation; no concrete
blocker remained. None of these checks executes the native rendered UI.

GPU test after restarting EFT:

1. Leave an Arena raid. Raid Ended remains, but its red item-loss block is absent.
2. Continue through kills/statistics. XP must not appear; the next page is native
   treatment if applicable, otherwise the main menu. Check the profile is restored.
3. Use Main Menu on Raid Ended too. If treatment appears, Back must lead to
   statistics, never XP. Native treatment cancellation still works.
4. In an ordinary raid, confirm that the warning and XP page still behave normally.

Only the client is changed. Server restart is unnecessary.
