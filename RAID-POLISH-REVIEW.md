# Arena visibility, notifications and Labyrinth access

Reviewed and installed on 2026-09-06 against the local SPT 4.1 assemblies.
These changes do not alter persistent profiles or ordinary raid behavior.

This is the initial implementation snapshot, including its historical DLL hashes.
The following user playtest found the friendly diamonds invisible. The corrected
UI rebuild timing, virtualized head reads and expanded regression evidence are in
`tools/FriendlyMarkerChecks/README.md`; the initial offline checks below did not
prove rendering and must not be read as a successful marker playtest.

## Changes

- Small team-colored diamonds identify current living friendly bots in Team
  Deathmatch and protected Tactical 5v5. F12 `HUD / ShowFriendlyMarkers` defaults
  to enabled and follows `FriendlyTracerColor`. Fresh physical head line-of-sight
  prevents markers through blocking geometry. Dead, unknown, historical and enemy
  bodies are excluded. Eleven pooled views, 100m maximum distance; no labels,
  offscreen arrows or input handlers. Disposing the HUD removes its render callback.
- A persistent contrasting banner below the score displays Tactical team assembly
  and round rebuilding. Five arrived opponents do not clear it before actual
  combat readiness. Solo Tactical displays it until the first opponent arrives.
- Quest and achievement popups and associated sounds are suppressed only in the
  exact active Arena raid. Progression, history, unrelated notices and Arena
  announcers remain intact. The achievement presentation entry is blocked before
  asynchronous sprite loading can cause a delayed popup after the raid.
  NotificationFilter's replacement subtask popup is covered without changing its
  configuration or audio volume.
- Arena gets a separate Labyrinth menu location object with enabled visibility,
  the visible native icon position and the forced online-PVE requirement removed.
  The server applies access metadata only to its existing exact Arena raid clone.
  Existing Arena card bypass remains scoped to the current eligible PMC selection.
  Suitable EnableLabyrinth metadata is reused unchanged; optional keys, Scav
  behavior and extra exits are preserved. No global unlock, extra Scav exit,
  boss-key loot, upstream binary, asset or Blazor interface is imported. Exiting
  Arena restores the original location reference, including external changes.

## Review and offline verification

Each implementation received native-contract, behavioral, adversarial and cleanup
passes, followed by an independent parent review and re-execution of all returned
checks. Shared HUD/Plugin integration and the banner received a separate agent
review. No unresolved correctness findings remain from this review.

The four production-linked client harnesses passed 268 assertions: assembly 32,
markers 99, notifications 87 and Labyrinth access 50. All 25 deliberate regressions
were rejected by their intended checks. Native signature/consumer checks read
the installed EFT, Unity, SPT and NotificationFilter assemblies.

The full server suite passed **194 tests, zero skipped**. All sixteen integration
harnesses passed, including Team Deathmatch population/runtime, give-up scoring,
team lasers, PitFireTeam, team expansion/SAIN, solo Tactical spawning, announcers
and native menu/side/map/mode selection. Test-only server builds used a verified
nonexistent deployment target. Final server and client Release builds passed;
the explicit client build ran last to preserve the Release Redeploy dependency.
Existing warnings remain: four client CS8632 annotations, server MUD0002 on
`MudTabs.PanelClass`, and unused fields in test stand-ins.

Feature-specific evidence and executable checks:

- `tools/ArenaAssemblyChecks/README.md`
- `tools/FriendlyMarkerChecks/README.md`
- `tools/ArenaNotificationChecks/README.md`
- `tools/ArenaLabyrinthAccessChecks/README.md` (includes upstream attribution/license)

Release output and installed DLLs were SHA-256 identical:

| Artifact | SHA-256 |
|---|---|
| Arena client | `308375F62B76F8E7F98FF5CA771DA4E9F22E9F6EA663890A168B872D29AA02E8` |
| Arena server | `95D91AD9FC88CAD23A77E674B4A6BE5B093584FB274166F8F8ED62BF113DCD52` |
| Redeploy.Core | `3973761FDE09BBDBBD924AA893F3D351ECA50EFE14B93183A6E1C8CE947BAA8C` |

## Required playtest

This host has no GPU. Offline models and builds do not prove Unity rendering,
Harmony execution order, live audio or actual map loading. Restart **server and
game** before testing:

1. Check diamonds in TDM and Tactical spectator view; move behind cover, closed
   doors and glass, then verify death, replacement and round rebind hide old marks.
   Geometric LOS does not model smoke, fog or darkness as visual occlusion.
2. Check assembly banner readability at the chosen HUD scale, including full
   opponent arrival while still waiting for readiness and next-round rebuilding.
3. Trigger quest/achievement progress: no popup or sound, while Arena announcements
   still play; verify normal-raid presentation afterward and with NotificationFilter.
4. Start Arena Labyrinth with EnableLabyrinth absent and present, including Back
   navigation, card-free entry and local raid loading. An explicitly selected online
   preference is preserved. Returning to ordinary PMC/Scav must retain stock or
   upstream-mod map behavior respectively.
