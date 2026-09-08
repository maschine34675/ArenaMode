# ArenaMode working handoff

Snapshot: 2026-09-08. Read `AGENTS.md` first. This is a compact handoff, not the
conversation transcript or a live synchronization channel. Verify current files,
commit IDs, installed artifacts and logs before treating a recorded result as
current. User-facing communication is normally German.

## Baseline and responsibilities

- Native GPU retest of `e2859e8` completed (2026-09-08, `C:\SPT41`): installed
  client `DEFC7DA2...`, server `D7879AEF...` and Redeploy `7D950A47...` match
  the delivery. Both separate USEC and BEAR profiles reached actual full new-preset
  rosters at all sizes 2v2–5v5; the previous navigation abort did not recur. BEAR
  5v5 took 7:03 to fill and BEAR 4v4 3:48, with recovered timed-out requests.
  Bot movement/combat remain inconsistent: both original USEC 2v2 samples, the
  original BEAR 2v2 and BEAR 5v5 stayed quiet in their observation windows. The
  later user-requested BEAR 2v2 firing trial showed movement, a kill and a refill
  BEFORE three own shots; it does not establish that shots awaken stalled bots.
  USEC 5v5, BEAR 3v3/4v4 and legacy contour show combat/refills. Legacy fixed 2v2
  ran 8:06 with three own respawns (1.44/1.49/1.48s), both-side bot refills and
  visible sights. All eleven Arena ends restored and persisted protected profile
  fields unchanged. Human faction labels, sampled replacement rendering, menu
  transitions, PFT Escape/settings and bounded double-jump cases passed. Precise
  movement/geometry and actual PFT group/buy/round/spectator coverage remain
  incomplete; no complete GPU approval. Normal PMC Customs/USP-1 ADS and F9
  separation passed the observed cases; its ordinary LEFT 2:13 gear loss and
  progression changes are documented separately from Arena protection. Two Unity
  archive-close errors before BEAR 3v3 arming are unattributed; no new raid
  camera exception observed. RDP interruptions and restored input bindings are
  documented. Final client setting and Controls comparisons are zero; server
  configuration unchanged. Final RDP display is 1920×1080 (initial 1920×945),
  with DLSS Balanced and other rendering options unchanged. Native game exit
  and owned test-server stop completed; both absent at 18:36:23 MESZ.
  See [GPU-RETEST-2026-09-08.md](GPU-RETEST-2026-09-08.md) and ignored evidence
  `artifacts/gpu-acceptance/2026-09-08_125900-retest/`. Only documentation changed;
  no code/build/deploy, manual profile repair or push during this acceptance.
- GPU report follow-up (2026-09-08): the user authorized implementation from
  report `2327f63`, with double jump merged into the main branch first. `master`
  includes that merge as `227fb10`; all three reviewed fixes are now integrated
  into `master` through code commit `e2859e8` (the source of the delivered DLL).
  `853a2ce` fixes the human PMC scoreboard role; `77469cc` guards the replacement
  grass-camera ordering and optional PFT inactive-menu warmup. Team-start
  readiness now requires the actual 1 PMC + N-1 allies versus N opponents and
  verifies contained bidirectional waypoint connections between existing native
  markers. Fresh final and placement checks reject stale paths; actual bot path
  corners are still checked independently. Legacy areas retain direct validation.
  See `TEAM-START-ACCEPTANCE-REVIEW.md`, `AREA-CONNECTIVITY-REVIEW.md`,
  `ROLE-LABEL-REVIEW.md` and `GPU-ACCEPTANCE-COMPAT-REVIEW.md`.
  Final verification: all seven executable gates passed, plus independent review
  and verified delivery (9/9 gates). Targeted checks: team runtime 369, connectivity
  630, role 174, double jump 143, render/PFT 47 assertions and 54 named source
  controls; ten connectivity source contracts. All 444 server tests passed with
  zero skips and the existing menu/team/polygon regressions passed. Full client
  build: zero errors, four existing nullable warnings.
  **Client delivered locally** in `D:\SPT41` from clean `e2859e8`, SHA256:
  `DEFC7DA21EA311A5078F83037FEA56972C24C36CAE499F5D9FEF7FCC06CE973E`.
  Server and Redeploy hashes remain identical to the GPU baseline below. Staging:
  `artifacts/double-jump-build/c96304a3078740c78c60610cf5757cca/manifest.json`;
  previous client and verified `delivery.json`:
  `artifacts/team-start-acceptance/delivery-267f829d91bf4dd98c88287380fef267/`.
  GPU mirror completion is not newly verified; check the delivered hash there.
  No GitHub push or native GPU success is claimed. Restart only the game.
  Retest new team starts on both factions at sizes 2-5, initial roster and refills,
  actual movement/boundaries, F9 role, upscaled rendering after respawn and PFT
  Escape/settings menu. Double jump also needs its first GPU acceptance. Only
  the client changes; no server/Redeploy rebuild is required for this follow-up.
- Native GPU acceptance (2026-09-08, `C:\SPT41`): **new team-start preset fails**.
  Expected DLL hashes matched before/after testing at documentation HEAD `62c7b70`.
  All four sizes are selectable in the native 1920x945 menu; 2v2 (including its
  sole retry), 5v5, 3v3 and 4v4 abort after 13 seconds during navigation validation:
  `BEAR starts: PMC 0`. No active new-preset teams, sector/boundary or refill pass.
  Legacy contour 2v2 played for 3:45 with three player respawns, friendly refills
  and working observed optics; preload guard diagnostic occurred, no camera NRE.
  All six Arena ends restored and persisted protected profile fields unchanged.
  Normal PMC/PSO test passed the observed start/ADS/hotkey separation; its ordinary
  LEFT gear loss is documented separately. PFT group and other-faction tests remain
  unexecuted. At that point the Unity render/menu errors were unattributed; the
  follow-up above records source-supported repairs and remaining uncertainty. See
  [GPU-ACCEPTANCE-2026-09-08.md](GPU-ACCEPTANCE-2026-09-08.md) for exact limits/times.
  At the end of this test run, game/server were stopped and only documentation
  had changed; no fixes/build/deploy/push occurred during the acceptance itself.
  Raw evidence and private test-profile backups are ignored under
  `artifacts/gpu-acceptance/2026-09-08_102800/`.
- Integrated into `master` before the navigation fix (2026-09-08):
  `feature/double-jump` / `fcfcd71` adds an optional extra
  jump after a normal local Arena takeoff, using the configured jump binding.
  F12 Movement: `DoubleJumpEnabled` (default true), `DoubleJumpHeightMult`
  (default 1, range 0.25-1.5). Native collision, lateral motion and stamina remain
  in charge; bots/ordinary raids and ledge-fall rescue are excluded. See
  `DOUBLE-JUMP-REVIEW.md` and `tools/DoubleJumpChecks/GATES.md`.
  At feature preparation: 143 production-linked assertions and seven deliberate source faults,
  full staged client build (four existing nullable warnings), independent review
  without unresolved P0-P2. The live test baseline stayed unchanged at that time.
  **Now delivered with `e2859e8`; GPU acceptance still pending.** The user
  authorized integration before correcting the report's navigation failures.
  See the current delivery entry above; server unchanged by the double jump.
- Latest implementation (2026-09-08): `2407351` scopes Arena's cold optic-pool
  callbacks; `6843e1e` adds the separate **Customs contour (team starts)** preset
  with native **2v2–5v5** selection. Choose Arena / Team Deathmatch / Customs /
  that new preset explicitly. USEC starts west and BEAR east; after initial
  assembly, respawns use connected in-area points ranked against live enemies.
  Position/identity reservations protect asynchronous bot and player handover.
  Legacy contours stay fixed 2v2; full-map/PFT/ordinary raid paths are unchanged.
  See `AREA-TEAM-SETUP-REVIEW.md`, its linked scope reports and
  `OPTIC-PRELOAD-REVIEW.md` for limitations and user test steps.
  The final implementation passed 444/444 server tests (zero skips), focused
  production-linked runtime/menu/optic checks and deliberate fault controls,
  full legacy area/polygon regressions and independent adversarial reviews.
  No native GPU raid or actual menu rendering is claimed for this update.
  Both DLLs were rebuilt from clean code commit `6843e1e` into isolated staging
  and installed locally in `D:\SPT41`, with matching SHA256 hashes:
  client `49762EB638B244F3E2D45ADB3BC040CA6A42FBAF895B7A2A3C80B1CC006839E9`;
  server `D7879AEFD593B8CA2915B8F8FAF7106C9F007A6E6BE2D02425D3892AF253AAF2`.
  Previous DLLs, prepared outputs and `delivery.json` are in ignored
  `artifacts/area-team-setup/prepared` in the shared repository.
  **Restart game and SPT server** before testing 2v2 and 5v5, initial team sides,
  several deaths/refills and optics during background kit preloading. Distance
  scoring is not a cover/visibility guarantee, and the broad start sectors still
  need a gameplay fairness check. GPU mirror completion is not newly verified.
  Both code commits are integrated in the shared checkout; continue there, not
  in the temporary `Documents/Codex/AreaTeamSetup` author clone. No GitHub push
  was performed for this follow-up. Normal mirrored delivery needs no repeated
  stop-confirmation prompt; the existing user-authorized workflow is unchanged.
- Latest user acceptance (2026-09-08): `714b80b` now loads and plays successfully.
  The 07:26-07:36 local log confirms contour READY with 17 player/11 bot spawn
  candidates, 2v2 readiness, 9.6 minutes of play, 8 kills/3 deaths, no reported
  boundary or activation failure, and completed server snapshot restoration.
  See `CUSTOMS-CONTOUR-REVIEW.md` for exact observations and the two isolated
  optic-camera/menu errors still worth tracking. This is one successful user
  raid, not exhaustive geometric or long-duration acceptance. No new DLL needed.
- Previous implementation: `fix/contour-corner-carvers`, `714b80b`, based on `922d656`. The user's
  07:04:53 local attempt on 2026-09-08 identifies the exact cause: corner 0's
  navigation capsule reports height 27.7499981 instead of assigned 55.4999962.
  The previous small-rounding hypothesis was insufficient. Physical capsules
  and edge-box checks pass. Corner navigation now uses full-height box obstacles
  via the same size API as edges; physical limits, grid, area/spawn rules and
  server remain unchanged. The boxes carve slightly more conservatively at
  diagonals (up to 0.125 m extra); all navigation readiness checks stay strict.
  See `CUSTOMS-CONTOUR-REVIEW.md` for implementation and subsequent user success.
  The delivered change required only a game restart, no server restart.
  Offline checks: 69,023 enclosure assertions, 13 deliberate fault controls,
  staged Release client build and independent review without open P0-P2.
  Locally installed client SHA256:
  `20D76AA252C3B2939DC17B6FC4E7A2A3B21C614F324D8EBC7512C7B640714167`.
  Prepared DLL/backup/manifest and failure excerpts are in ignored
  `artifacts/corner-carvers/prepared`. Continue in the integrated shared checkout,
  not the old isolated author clones; GPU synchronization is not newly verified.
- Previous follow-up: `fix/contour-integrity`, implementation `922d656`, based on `63affca`. The first user
  polygon raid failed at 06:45:16 local on 2026-09-08: all 28 markers were
  accepted, but the aggregate enclosure-integrity guard failed before navigation
  readiness. Exact capsule dimension comparisons are a suspected cause only;
  native getter values were absent from that log. The client now allows bounded
  0.1-mm scalar roundoff against planned capsule dimensions and propagates precise
  component/property diagnostics through preparation and active-match failure.
  Safety guards, server, contour and spawn policy are unchanged. See the dated
  follow-up in `CUSTOMS-CONTOUR-REVIEW.md`; the next GPU raid remains unverified.
  Follow-up verification passed 67,002 enclosure assertions, 11 fault controls
  and a staged Release client build; independent review has no open P0-P2.
  The client is installed locally, hash `326405FD7021EC2E9A7E60075EE70198851443370307549DA1FBA6D7754C891E`;
  backup/prepared DLL/manifest are in ignored `artifacts/contour-integrity/prepared`.
  Restart the game for this update; no server restart is needed. Continue from
  the integrated shared checkout, not the temporary isolated author clone.
- Previous follow-up: `feature/customs-contour`, implementation `4a1d0f4`, based
  on clean `235b285`. The
  user's 15-point Customs survey is registered as a NEW selectable
  `customs-factory-center-polygon-v1` / **Factory Center (contour)**, independent
  TDM 2v2 only. Choose it explicitly; legacy area IDs/defaults remain unchanged.
  The new preset has only lateral containment: no actor/path/spawn height band,
  physical floor/ceiling or vertical cap carving. Scene-derived side walls cover
  overlapping NavMesh triangles and spawn anchors with padding. The elevated
  sniper marker is excluded by exact identity on both client and server.
  See `CUSTOMS-CONTOUR-REVIEW.md` for scope, staging checks and user GPU tests.
  The implementation is integrated in the shared checkout; continue there,
  not in the old isolated author clone. Client AND server need a restart after deployment.
  Offline verification passed 386/386 tests (zero skips), focused real-data and
  native-menu checks, enclosure/fault-control checks and staged Release builds.
  Review has no open P0–P2. On 2026-09-08, both reviewed DLLs were installed in
  D:\SPT41 and their SHA256 hashes matched the prepared build. Retained client
  and server DLLs plus manifest are in ignored `artifacts/customs-contour/prepared`
  in the shared checkout. See the contour review for hashes; local installation
  does not by itself prove GPU synchronization or in-raid acceptance.
- Current follow-up: `fix/pmc-area-capture`, based on clean `83a26cf`. The user
  found Arena-only capture impractical because they cannot walk outside the old
  contour. F12 `Development - Area outline` now offers `AllowInNormalPmc`
  (default false), in addition to `Enabled`, for measuring in a normal local PMC
  raid. This is a read/draw/explicit-export exception only; normal raid/profile
  consequences remain and Arena borders stay closed. No server/geometry change.
  The production-linked capture checks pass 292 assertions plus nine deliberate
  fault controls; independent review has no open P0-P2. Release client compilation
  uses isolated deployment staging, with live DLL hashes unchanged during tests.
  That capture follow-up needs a game restart, not a server restart; its
  historical offline checks are distinct from user GPU testing.
- Current work: polygon boundaries and an opt-in scene capture/export helper on
  `feature/polygon-arena-areas`, based on clean `e61b6e3`. See
  `POLYGON-AREA-REVIEW.md` for controls, validation and pending user GPU checks.
  The user has no Customs vertices yet and explicitly chose capture help first;
  both existing presets stay unchanged. No draft is loaded into a live match.
  Work was prepared in an isolated checkout outside Syncthing; continue from the
  safely integrated shared checkout after delivery, not temporary author clones.
- Previous work: native area selection on `feature/native-area-menu`, based on
  the clean `ec11aa8` handoff snapshot. See `NATIVE-AREA-MENU-REVIEW.md` for the
  selection rules, verification and pending GPU menu checks. The source was
  prepared outside Syncthing in an isolated checkout; use the integrated shared
  checkout for subsequent work, not that temporary clone.
- Previous gameplay baseline: bounded Customs `a690ce2` on
  `feature/customs-arena-area`, based on `011beb5`. The subsequent user playtest
  reports overall trouble-free operation, with boundary refinement still needed.
  See `CUSTOMS-AREA-REVIEW.md`; this acceptance update changes documentation only.
  Prior Factory prototype `0c54b7745534798a09662511281978ffbb20acc4` includes the
  earlier kits/GPU menu redesign; its core behavior is user-confirmed below.
- Development host: `D:\SPT41\Development\ArenaMode`; no GPU raid execution there.
- User-confirmed GPU host installation: `C:\SPT41`, a mirror of this host's
  `D:\SPT41`. If Development is included, the corresponding repository is
  `C:\SPT41\Development\ArenaMode`. Check that checkout before editing. Mirror
  inspection on the GPU host confirmed bidirectional Syncthing of all `C:\SPT41`
  without exclusions, matching installed hashes, and working native capture/input.
- **The user performs actual in-raid gameplay tests.** Do not assign autonomous
  raids to the GPU session. Its next intended work is UI redesign and automated
  visual/menu checks, with local development/build/review here as useful.
- Use the existing mirrored installation for UI checks; no second full game copy
  is needed solely for testing. Before editing source on both machines, establish
  separate writable checkouts/branches outside any conflicting mirror scope.
- For simultaneous implementation, assign a separate feature/fix and branch to
  each session. Agree who integrates changes and who owns each test deployment.
- Commit every completed change with tests/documentation and
  `Co-authored-by: Codex <codex@openai.com>`. Do not push without authorization.
- Preserve normal raids and profiles. Protected PFT Tactical uses the PMC entry
  loadout and must not receive standard Arena kits or grenade additions.

### User clarification on the GPU host (2026-09-07)

- **Artifact delivery update (2026-09-08):** do not ask again whether the game
  and SPT server are stopped before routine delivery into the existing mirrored
  installation. The user states that their synchronization defers files while
  they are in use and explicitly removed the stop-confirmation prerequisite.
  An offline GPU host is not a reason to hold back local deployment. Keep local
  hash verification and report remote synchronization separately; source/build
  ownership rules below still apply. Restart changed components before testing
  the new version; copying a DLL is not proof a running process has loaded it.
- EFT/SPT is never run on both PCs simultaneously. The user accepts immediate
  mirroring/overwriting of logs and saves; these profiles are disposable test data
  and even their loss is acceptable. Routine menu checks do not require pausing
  Syncthing or restoring all runtime changes for test-data protection. The local
  `SPT41` folder was resumed at 11:03:21 MESZ after the initial menu inspection.
- This test-data policy does not change Arena's profile-isolation behavior or
  authorize autonomous raids. Concurrent source edits/builds still need the
  coordination described below; single-host game execution does not imply that
  two source checkouts cannot be edited concurrently.
- The baseline contains 22 screenshots covering menus, maps/times, all five modes
  and return to normal PMC selection; no raid was started. The initial report's
  claim that BACK was incomplete was incorrect and has been withdrawn. Do not
  treat missing BACK text or other unverified glyph gaps as an established bug.

## Current behavior and recent work

The surveyed Customs contour is now a separate selectable area alongside the
legacy rectangle. It is the first height-unlimited preset; existing box/polygon
presets and capture-export defaults keep their previous semantics. Source draft
JSON remains review data and is never autoloaded. Installed database checks give
17 PMC/11 bot candidate points, not yet a proof of loaded scene connectivity.

Polygon support now accepts simple concave XZ outlines plus enclosing heights.
Walls/grid, floor/ceiling caps, spawn containment and full path-segment checks
share that contour. F12 `Development - Area outline` optionally captures ordered
scene points (Ctrl+F6 aim, Ctrl+F10 feet, Ctrl+F7 undo, Ctrl+F8 export). JSONs go to
`BepInEx/plugins/ArenaMode/AreaDrafts`; capture is scoped to the exact local raid
and does not change settings/geometry for it. Normal PMC capture additionally
requires `AllowInNormalPmc`; Scav/Hideout and ending/invalid Arena identities are
rejected. Ordinary raids retain ordinary equipment/profile consequences; this
helper grants no Arena protection. Export before leaving/disabling.
The user subsequently supplied the Customs draft, now integrated as described
above. Waypoints remains the spawn viewer. CTF, team-size changes and autonomous
raids remain out of scope. See the polygon and contour reviews for test evidence
and what still needs actual Unity/GPU confirmation.

Historical polygon-stage verification at `83a26cf`: 363 complete database-backed tests (no skips),
focused geometry/capture/enclosure and native/TDM regressions, plus 14 deliberate
fault controls. Independent review closed the body/world ownership and visual
layer findings. The user confirmed server and game stopped before deployment.
Final Release outputs are hash-compared with the local installation; this is not
a fresh GPU host hash comparison or an in-raid test.

The previous native picker received the user's "ja passt" acceptance. This is
broad functional acceptance, not proof of every individual PFT/menu transition.

Native area selection now appears below the five Arena mode rows on the map
screen. Choose Arena -> Team Deathmatch -> Customs: the map is directly offered
and selects Factory Center automatically, with fixed 2v2 displayed. No F12
preselection is required. Factory daytime offers Whole map / Factory South;
South remains fixed 2v2. Factory nighttime, Labs and Labyrinth offer Whole map.
Area selection shares the existing F12 `ExperimentalArea` setting. Map/time/mode
changes preserve compatible choices and normalize incompatible ones during menu
selection; leaving independent TDM hides Customs and clears its selected map.
There is no whole-map Customs Arena or PFT area support. Final client/server
arm validation remains strict and never repairs settings. Configured native rows
are constructed before the mode view's PFT repair, avoiding an empty picker from
a stale F12 mode. No repeated native ShowInternal, geometry or gameplay changes.
Edited allowlists and explicit server disables remain authoritative. Restart the
game after this client change; server source and area protocol are unchanged.

This first Customs candidate spans X305..420 / Z-135..-65, with 15 PMC and seven
bot positions in installed data. These are coordinate checks, not live NavMesh
evidence. The northern face can cut a building footprint. In the subsequent
2026-09-07 playtest the user reports that Customs also works without overall
problems, but the boundaries should eventually be refined by hand. This confirms
basic practical operation, not every detailed test case below. Boundary polish,
explicit repeated-respawn/edge checks, Escape/re-entry, menu setting changes and
teardown into ordinary raids remain to be confirmed individually. Its live
readiness gate must reject an unusable component rather than spawn outside.
Factory South geometry remains unchanged; loading/readiness/respawn now share an explicit
PMC-side check so Savage-only Player markers cannot become human spawn choices.
`CUSTOMS-AREA-REVIEW.md` records checks, deployment evidence and the test list.
Historical Customs-stage local verification: 330 server/data tests passed without skips; targeted
client regressions and nine negative controls passed, independent review has no
open P0-P2 findings. Release outputs match all four DLLs installed on D:\SPT41.
At delivery the local Syncthing query reported SPT-GPU disconnected (not paused).
The later user playtest confirms the Customs preset ran there; it is not a fresh
remote hash comparison or a current synchronization-status check. No code,
boundary coordinates, DLLs or installed settings changed in this acceptance update.

Gameplay prototype `0c54b77` on `feature/bounded-arena-prototype`, based on `7298980`
including the GPU menu redesign: opt-in Factory South, Factory daytime,
independent TDM fixed 2v2. See [BOUNDED-AREA-REVIEW.md](BOUNDED-AREA-REVIEW.md)
for the exact area, server/client contract, verification and required user raid
checks. Enable before the raid via F12 -> Team Deathmatch -> `ExperimentalArea`
= `factory-south-v1`; default/disable is `full-map`. Other modes are unchanged.
Server and game need a restart after a gameplay deployment. User GPU playtest
on 2026-09-07 confirms: visible impassable grid, player and bot spawns always
inside the zone, and bots staying inside without observed stalls. This is the
user's report, not a new agent raid or log inspection. Remaining explicit checks:
Escape/give-up, another bounded match, full-map TDM and a subsequent ordinary
PMC/Scav raid with clean teardown/profile behavior. No gameplay code changed
in this acceptance update.
The Customs candidate above follows this successful core small-map test;
CTF, an editor and PFT region support remain unimplemented. The native area picker
is now implemented; its current verification is in `NATIVE-AREA-MENU-REVIEW.md`.

The user confirmed game and development work on the GPU host were stopped and
new DLLs may be installed. This change was prepared in an independent local
clone outside the Syncthing tree, then integrated into the shared repository;
do not resume that old isolated clone as a second source writer. Deployment
hash checks on D:\SPT41 do not by themselves prove completed GPU synchronization.
The local Syncthing query reported SPT-GPU disconnected at initial delivery.
The later user playtest confirms the prototype ran there; no fresh remote hash
or current synchronization-status verification is implied by that report.

Five modes: Endless Deathmatch, Rounds, Gun Game, Tactical, Team Deathmatch.
Team Deathmatch is independent of PFT; protected Tactical 5v5 is a separate path.
Maps: Factory day/night, Labs, Labyrinth; Customs only with its bounded independent
TDM preset. Factory night uses the native time selection on the Factory tile,
not a separate night tile.

GPU menu redesign on `feature/arena-native-menu-options`: Arena's five modes now
appear as five permanently visible native button rows in a left-side panel on
the map screen. Three short rules/respawn/equipment facts replace the mode
paragraph. The selected row keeps a marker after hover ends; native map points,
right-hand location information and navigation stay in place. Factory time
selection is back at its original native position. Player count/difficulty remain
future implemented options, with no placeholders or gameplay changes.

`MODE-SIDEBAR-REVIEW.md` records native row ownership, corrected mode copy, 123
passing selection-state checks and the GPU pass: all five modes, Factory day/night,
Labs/Labyrinth, clean Back/normal PMC maps and exactly one sidebar on Arena reentry.
The disabled PFT row/tooltip presentation and true 16:9 geometry were not tested
on the GPU. Actual captures are 1920 x 945 at native UI scale 0.875. Private
comparison artifacts are in the GPU task's `outputs/Arena-Modusauswahl` and logs
in `work/arena-mode-sidebar`; the final runtime log has no error entries or Arena
warnings. The earlier upward-opening dropdown is historical and documented in
`NATIVE-MENU-REVIEW.md`; its source/licensing research remains relevant.

The earlier character-selection polish at `328665d` aligned the Arena toggle with
SCAV/PMC and added a native-style `SEPARATE MATCH` subheading. At that historical
stage only Arena's wrapper moved; `CHARACTER-SELECTION-REVIEW.md` records its
contracts and evidence.

The character preview at `70a65c0` adds the current PMC's native posed appearance above
ARENA, with a separate fixed M4 USEC / AK-74M BEAR example kit. It moves the complete
native PMC/SCAV columns outward together, restoring them on Close/Show and before
PFT squad management. Model, camera, lights, render texture and bundle retention
have their own owner; no profile inventory or gameplay kit is used or changed.
Preview setup/load failure preserves the working Arena choice. See
`CHARACTER-PREVIEW-REVIEW.md` for native contracts, independent review, production
and fixture build identities, logs and exact GPU coverage. The 29 side/53 flow
checks still pass but do not execute the new Unity/async owner.

On `WIN-Q470OL2A14A`, the production menu run covered USEC/M4 rendering, label
selection, Arena and normal SCAV map/Back routes, repeated reopening, populated
MY SQUAD without Arena, and clean ordinary M60 inspection afterward. A temporary
fixture also rendered the BEAR kit on an unchanged USEC character and verified
closing during delayed bundle loading; the final production source/DLL contains
neither fixture override nor delay. Real BEAR profile appearance remains untested.
Final production standard-M4 smoke passed: all three figures, ARENA label and
description, then clean Back to the main menu. Its saved client log contains no
error-level entries, Arena warnings or fixture markers; game and owned server
were stopped. No raid was started. Captures are 1920 x 945, native UI scale 0.875; private
screenshots/manifest are in the GPU task's `outputs/Arena-Charaktervorschau`.

| Commit | Change | Verification still needed (raid work belongs to user) |
| --- | --- | --- |
| `20462c0` | Standard kits request two RGD-5s, no automatic Zarya. Explicit rig/pocket placement bypasses UseItemsAnywhere's backpack-first `GrenadeThrowingSlots` patch. Custom authored supplies and protected PFT gear are retained. | In-game placement and grenade-key use, including respawn/Gun Game. |
| `1119b56` | Labyrinth Way Up (`labir_exit`) has a 10-second extraction countdown on the Arena raid clone; availability timers remain zero. | Walking past the doorway must not immediately end a match. |
| `f17ae7b` | Read-only diagnostics for stationary foreign helmets/equipment in the item/weapon preview after Arena, in menu/Hideout. **No causal rendering fix yet.** | Reproduce affected preview and collect `Arena preview probe:` plus screenshot. |
| `50d2a89`, `fae3a43` | Restored Tactical preset descriptions and excluded generated reflex sights on empty scope rings. | UI appearance and generated weapon samples. |

The last two investigated Solo Tactical endings were native Labyrinth extractions,
not opponent-spawn failures; the user confirmed walking past the exit door.
Do not reopen that spawn diagnosis without new evidence.

Latest grenade verification: 222 server tests passed, zero skips; 1,605
production-linked supply assertions; both old-placement negative controls failed
at their intended assertions. Default and installed 20-kit catalogs each packed
40 RGD-5s in rig/pockets without displacing original supplies in the offline
grid model. Full grids still skip optional grenades, never spill into backpack.
Independent review found no open issues. These are not GPU test results.

GPU Release/installed hashes after the mode-sidebar client build
(2026-09-07; menu pass completed at the recorded window geometry).
The server binary remains the prior gameplay build; Redeploy.Core was rebuilt
from unchanged source `58abf5876629bb51be808d78ee2cd7a5d34af77a`:

| Artifact | SHA-256 |
| --- | --- |
| ArenaMode.Client | `67B55E0A532BFACE875AF556D6760B1AD454698AB9FE4B4125A8A99350390986` |
| ArenaMode.Server | `FCE6C394DF9184E81FE6D43B0D236380DE71E3BB110708497E2471D395B2DBE8` |
| Redeploy.Core | `F923B0D709908F75969B3101E9F8290CD2BB06DF457F64B1FFF8876A7BBFEA00` |

Hashes identify these built files. A fresh build on another machine may differ;
always record that build's hashes and compare them with its installed copies.

## Continue on the GPU host

1. Open a new session in `C:\SPT41\Development\ArenaMode` if present. Read this
   file and `AGENTS.md`, inspect branch/commit/uncommitted files, and verify that
   references/deployment resolve to `C:\SPT41` and `C:\SPT41\SPT_Runtime`.
   Confirm the latest handoff/commits arrived through the existing mirror or an
   agreed Git transfer; do not create a competing sync mechanism. A new session
   must not assume access to the original conversation or live agent memory.
2. For UI checks only, use a fixed mirrored build with its commit/hash record.
   A complete development toolchain is unnecessary for simply running the game.
   For development, transfer/clone the repositories and dependencies listed below.
3. Use the existing disposable test profile and compare relevant mod versions/settings
   between hosts. The user permits live bidirectional mirroring of this test data
   under the single-host execution policy above; routine menu checks need no
   mandatory profile backup/restore. Keep test configuration changes explicit so
   a difference in mods is not mistaken for an Arena regression.
4. Keep one deployment owner and deliver one coherent build through the existing
   mirror without an additional game/server stop question. Verify installed
   hashes locally and synchronization separately. The user manages restart and
   test timing; identify the actually loaded build when recording test results.
5. Begin with the UI workflow below, not the pending raid tests. For the existing
   preview-contamination investigation, the user supplies the post-match state
   after their manual raid; the agent can then inspect the affected item in
   menu/Hideout and collect the probe. Reopen after ten seconds if throttled.
   Compare clean/affected previews; do not destroy guessed scene objects.
6. Return a report per test run: hostname, source commit, actual DLL hashes,
   mod versions/config changes, mode/map/kit, local time and time zone, steps,
   expected/observed behavior, pass/fail/blocked, and exact log/screenshot paths.
   Save logs before the next launch only when needed as diagnostic evidence;
   routine menu checks do not require log backups. Raw logs/screenshots belong
   in a private artifact location (local `artifacts/` is ignored); commit a small
   redacted report if useful. Never commit profiles, tokens or unrelated logs.

## Next work: UI redesign with automated visual checks

- Run the UI-focused session directly on the GPU host. First inspect available
  tools; if native Computer Use is available, verify that it can capture EFT's
  window and perform one harmless menu interaction. Browser-only tools cannot validate the native Unity
  UI. Native game capture/input were verified on the GPU host on 2026-09-07.
  RDP-disconnect behavior remains untested; do not infer it from the presence of
  a GPU or a mirrored installation.
- Native Computer Use on Windows requires the active unlocked desktop, a visible
  target window and app permission. Do not compete with the user's mouse/keyboard
  during a run. See [official Computer Use documentation](https://learn.chatgpt.com/docs/computer-use).
- Start with reachable menu flows: PMC/Scav/Arena selection, map/time/mode
  selection, back navigation, and overlap/readability checks at recorded
  resolution/UI scale. Stop before starting a raid. Save before/after screenshots
  and associate them with the tested build.
- Raid HUD, scoreboard, Tactical buy panel and spectator UI depend on live match
  state. For their redesign, prefer a development-only preview using the same
  rendering code with fixed sample data (long names, empty/full lists, different
  scores), without spawning bots or simulating a real match. This preview is a
  proposed implementation, not an existing tool or completed feature. Keep it
  opt-in and isolated from profiles, normal raids and production gameplay gates.
  Do not fake `ArenaSession.IsActive` or call match-start hooks just to show a
  panel. Preview buy/ready controls must not purchase, start or end anything;
  preview teardown must not stop a real spectator service. The existing
  `ArenaPreviewDiagnostics` is unrelated item-preview diagnostics, not this UI
  preview facility.
- Distinguish visual results (layout, clipping, fonts, hover/click, scaling) from
  gameplay integration. A preview pass does not prove scoring, respawn, purchase
  transactions or raid input ownership; the user verifies those in actual raids.
- Iterate one agreed screen at a time: capture baseline, implement, review,
  build/deploy between runs, reopen and compare. No autonomous raid playtest or
  scheduled/unattended loop was configured by this handoff update.

## Parallel development without collisions

- Use a separate clone on each machine; exchange reviewed commits using Git
  through the agreed remote or a Git bundle. Do not live-mirror two writable
  working directories or `.git` folders. If the existing SPT mirror includes
  Development, exclude/separate the GPU working checkout before parallel edits;
  otherwise the next mirror pass may overwrite local UI changes. Do not change
  synchronization configuration without inspecting it and agreeing its scope.
- Within one machine, worktrees can isolate feature edits, but they share Git
  metadata and do not isolate Arena's deployment directories or external projects.
- Agree the starting commit, scope and touched areas before parallel work. Avoid
  having both sessions independently fix the same code. Integrate selected commits,
  rerun affected checks, build once, then test that exact integration build.
- When moving a whole task, finish/commit local work and hand over the current
  state. When keeping both sessions active, one handles integration and updates
  this shared summary; each test run can have its own report to avoid conflicts.

## Build and dependency traps

- Keep the normal layout when practical: `<SPT>/Development/ArenaMode`, sibling
  `Redeploy`, `_decompiled`, and the needed private `_reference` sources.
  ArenaMode does not contain those other repositories or game DLLs.
- The client references `../Redeploy/Redeploy.Core` relative to the Arena repo.
  Verified Redeploy checkout at handoff: `58abf5876629bb51be808d78ee2cd7a5d34af77a`,
  clean. Transfer compatible source separately if building; do not assume a DLL
  alone satisfies the project reference.
- `Shared.props` resolves the game root two directories above the repo and the
  server root under `SPT_Runtime`. In a different layout, verify/override both
  `SPTPath` and `SPTUserPath`. Local `Shared.props.user` is imported and ignored by
  Git, so it must be configured separately on a new machine. Redeploy has its own
  props/deployment target. Check its effective paths too. With both repos under
  `C:\SPT41\Development`, relative defaults already resolve to C:; only override
  where necessary, and inspect mirrored `.user` files for stale D: paths.
- Builds auto-deploy, including the server build triggered by `dotnet test`.
  A generic `DeployToSpt=false` switch is not honored by the current Arena targets.
  Do not build from an arbitrary worktree until paths, references and deployment
  ownership are settled. Never run concurrent builds into the same installation
  or shared Redeploy output. Prefer Release; rebuild the client last if another
  check rebuilt the shared dependency in Debug.
- Usual commands from the verified repo layout:
  `dotnet build ArenaMode.sln -c Release` and
  `dotnet test ArenaMode.Tests/ArenaMode.Tests.csproj -c Release`.
  `tools/KitPolishChecks/Verify-Supplies.ps1` covers the latest grenade change.
  Many harnesses use local `_decompiled` sources, native DLLs or D: defaults;
  inspect their parameters before running elsewhere. Set `SPT_DATABASE` to the
  actual `SPT_Runtime/SPT_Data/database` **directory** for server tests. This is
  separate from a harness's `-Database` parameter pointing at `templates/items.json`;
  the nested server tests do not automatically inherit that parameter. Require
  the expected tests to run, not silently skip. On the GPU host the environment
  variable is `C:\SPT41\SPT_Runtime\SPT_Data\database`.
- Keep licensed audio and attribution together with client assets; use the
  build's asset deployment or a complete verified package, not a guessed DLL set.
  The full solution also needs the installed BigBrain reference DLL. Optional
  runtime BigBrain/PFT/SAIN dependencies must match the actual compatibility
  checks; re-read source and installed versions rather than trusting old notes.

## Where to look next

Client log: `<SPT>/BepInEx/LogOutput.log` (local time).
Server log: `<SPT>/SPT_Runtime/user/logs/spt/sptYYYYMMDD.log` (UTC).
Kits/config: `<SPT>/SPT_Runtime/user/mods/ArenaMode/config/`.

- `KIT-POLISH-REVIEW.md`: grenade conflict, placement rules, evidence.
- `FUTURE-WORK.md`: deferred difficulty controls, further bounded map areas and
  objective-based modes such as Capture the Flag; the area section also records
  current prototype progress and links its review/test evidence.
- `PREVIEW-DIAGNOSTICS-REVIEW.md`: unresolved post-match preview issue.
- `CHARACTER-PREVIEW-REVIEW.md`: separate third menu figure, ownership, GPU/fixture
  evidence and missing genuine-BEAR-profile coverage.
- `MODE-SIDEBAR-REVIEW.md`: five visible map-screen modes, compact facts and
  GPU coverage; PFT presentation and true 16:9 appearance remain untested.
- `tools/ArenaLabyrinthAccessChecks/README.md`: Way Up and Arena-only map access.
- `PRESET-DESCRIPTION-REVIEW.md`, `OPTIC-MOUNT-REVIEW.md`: latest kit/UI fixes.
- `TEAM-DEATHMATCH-REVIEW.md`, `PITFIRETEAM-MULTIROUND-GATES.md`: team modes.
- `ANNOUNCER-REVIEW.md`, `ANNOUNCER-AWARDS-REVIEW.md`: audio behavior/licensing.
- `CORPSE-CLEANUP-REVIEW.md`, `PFT-GOAL-HANDOFF-REVIEW.md`: bot lifecycle/movement.

Read the relevant topic document and recent commits when starting a change,
rather than loading every historical review. Maintain this file as a concise,
dated snapshot of decisions, open evidence gaps and the next owner's scope.
