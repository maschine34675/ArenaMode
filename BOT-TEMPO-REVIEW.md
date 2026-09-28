# Arena bot combat tempo — 2026-09-28

Runtime adapter for the installed SAIN 4.5.1.0. No third-party implementation,
DLL or preset is copied or redistributed. The three user-approved changes are
enabled by the existing `BotMovementEnabled` switch; no extra settings added.

## Behavior

- **Search wait:** In this SAIN version `WaitAtPoint` is reached during reload,
  not a generic pause at every corner. While reload, medical/self actions or
  danger remain active, Arena leaves it alone. Once safe for 0.5 continuous
  seconds, it may expire the remaining search wait. A shorter native wait wins.
  The original SAIN method owns `UnPause` and resetting its timer.
- **Search sprint:** Only the native `SearchAction`, with its current unseen
  search target and no visible/line-of-sight enemies, may receive a promoted
  `shallSprint` input. Heard-from-peace stealth and incoming fire are excluded.
  The complete existing enemy path must pass within 1.5m of the bot, have at least
  4m to the next corner and 8m remaining. Polyline projection allows progress
  along a periodically refreshed route without oscillating solely because its
  original start is old. No path calculation, target discovery or geometry
  override is introduced. A paused/cancelled/recalculating or foreign active
  SAIN path vetoes promotion. Native true inputs are never cancelled.
- **Combat strafing:** A narrow transpiler changes only the float read from
  `STRAFE_SPEED` in `DogFightMove`, flooring positive values below 0.7 to 0.7.
  Zero, invalid and already higher values are preserved. Melee speed and later
  chase speed writes remain native. No aim, fire, pose or animation acceleration.

Native search execution still decides actual sprint based on its facing, stamina,
navigation and steering checks. Input promotion is not a measured sprint result.
The existing displacement factor (up to 1.5), native-corner easing, zero-speed
stops and 0.3m per-step safety ceiling are unchanged.

## Ownership and failure behavior

`ArenaBotMovement.IsLiveBot` reuses the displacement adapter's exact active
game/world/body/controller admission and live-mode scoring phase. Local humans,
ordinary raids, loading/countdown/result phases, retired owners, obsolete bodies,
area setup and abort windows are excluded. The adapter also rejects shutdown,
paused time, airborne/vault/prone/mounted/platform/stationary-weapon states,
doors, reload, medication, grenade throwing/danger, flash, BTR and artillery.
Cached `_isFlashed` is read because the native `IsFlashed` getter runs cleanup.

The bot's actual SAIN component, cached action owner and active SAIN layer must
match. Foreign custom/PFT actions and Arena's own pursuit/pickup action owners
remain in control; ordinary SAIN actions on either team are eligible. No shared
personality, preset, skill, inventory or profile is written.

Wait shortening additionally requires the exact current paused `BotPathDataManual`,
its bot/pathfinder, equal native search/path deadlines, and stable path reference,
ID, `TimeStarted` and match identity. A new journey on the same pooled object,
foreign pause extension, callback gap over 0.5s or interrupted safety observation
restarts/invalidates the observation. Prefix changes only the search deadline;
an exception restores it only while that path and written value still belong to
the operation. Another writer's deadline is not undone.

Binding is exact SAIN assembly version 4.5.1.0 and validates required members.
Missing/unsupported SAIN or a changed/ambiguous strafe IL site leaves native
behavior; partial installation unpatches the whole adapter. Runtime read failures
retain the original input and warn once. SAIN is optional, never bundled.

## Diagnostics and checks

At most once per 60 gameplay seconds/world, tempo diagnostics summarize promoted
search inputs, strafe inputs and completed native wait releases. Motion diagnostics
separately count boosted, corner-limited and step-limited calls (overlapping counts).
These are observations of hooks, not traversal speed or AI effectiveness.

`tools/BotTempoChecks/Verify.ps1` links the production adapter **and** the actual
shared movement admission. Synthetic native/SAIN boundary types deliberately make
lazy/mutative getters fail. Tests cover live modes, excluded scopes/actions/danger,
route geometry, native release/exception ownership, pooling, cadence and partial
patch rollback. Eight compiled fault controls must fail at named assertions.
The installed-DLL check verifies reflection members, the unique float-load/setter
IL seam and native reload/wait ownership. It does not execute Unity or Harmony.
Existing BotMovement and TdmSearchTempo checks remain separate regression gates.
Final counts, build and deployment hashes are recorded in `HANDOFF.md`.

## Native acceptance still required

After restarting EFT, test a few minutes of Deathmatch/TDM with open search routes,
close firefights, reloads and narrow doors/corners. Toggle `BotMovementEnabled`
for comparison; no changes to player movement. Confirm minute summaries and no
new wait/sprint oscillation or collision loops. Check Tactical/PFT both before
and after player death, and a normal PMC raid without Arena movement diagnostics.
Offline checks cannot certify actual SAIN action coverage, sprint frequency,
foot sliding or performance under a full bot roster. All reflection/path-copy
cost and the conservative route thresholds still need this native observation.
