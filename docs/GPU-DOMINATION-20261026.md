# Domination native acceptance — 26 October 2026

Status: native prototype acceptance passed within the boundaries below.
GPU-2 (`SPT-GPU-2`, 192.168.178.109), native profile
`SPT-GPU-2`, 1920×1080. All starts use UiBridge gpu-run. Source/build validation
is recorded separately in `.unlazy/domination-20261026`; this report records real
game observations only. Raw local evidence is under
`artifacts/domination-20261026/gpu/` and must not be published unsanitized.

## First candidate: Factory day

Fresh EFT process `e6972508`, 13:32:17 host local time. Verified loaded process,
host disk and workspace artifacts: client
`5832F455BC3331DBD6EE37D017A37D159B160F71A10CD1DFAD5F0F6193E0B867`, companion
`3262F1E49EF49181C8EB7B7A6E11321011055E13FEF6F3E39B2E20AA43D7A759`, server
`12127A1B6F61801F939E26EBAE37D39149772B00AE3FE62EA45532349D9C7A8F`.

- Native menu offers six playable modes, DOMINATION (WIP), and disabled CTF.
  Selecting DOM leaves Factory only; whole map, USEC/BEAR, population and both
  time choices remain available. The reward control is disabled and explicitly
  says this prototype has no rewards. Its initial white background made the
  label difficult to read; the final candidate below includes the UI correction.
- Factory day started normally. Native placement verified all three points
  against real geometry/navmesh: A=(-5.890, 0.031, -27.460),
  B=(-6.770, 0.031, 10.010), C=(-20.756, 0.031, 22.769).
- 5v5 roster assembled. Without any local kills or movement, bots walked on
  `Arena Combat Domination`, yielded to SAIN combat/avoid-threat, captured B and
  C, held A, fought and were replaced. Read-only bot snapshots and detailed
  objective logs record assignment/reassignment, arrivals and bounded rejected
  paths. No bot was teleported by the tester.
- The local actor was teleported through UiBridge to A and then 5 m outside it
  (both full-path/clearance checks passed). Ownership changed and contested
  states were observed. The read-only state/HUD showed both sides earning
  holding points. Local kills remained zero throughout.
- Explicit UiBridge godmode and infinite-ammo/keep-condition were enabled for
  observation. Godmode was disabled and the explicit local `kill` test hook used
  once: native death, killcam and replacement returned an alive player after
  8.2 s; objective points were retained and continued accumulating. Godmode was
  reenabled for continued observation. No scoring/capture method was invoked.
- Natural score-limit conclusion: BEAR 200 : USEC 107 after 3:08 of live play.
  Result separately displayed team kills 11 : 5 and match points. The game
  automatically exited; gpu-run advanced ExitStatus → KillList →
  SessionStatistics → MainMenu. Objective bridge then had `_match=null`,
  `_world=null`, `_live=false`; test hooks reported off outside raid.
- World label A was visibly depth-occluded by nearby water containers. The
  rotating Double Damage pickup occupied the same anchor, creating severe
  marker/text/beam overlap. Evidence: `day-a-ring.png`; corrected and verified
  with the final candidate below.
- Merged warning/error stream contained existing native weapon-model asset
  diagnostics (JetMaterial, missing extractor script, shotgun attachment bone,
  UtilityOperationEvent, no-foldable and one custom-item type), but no DOM
  runtime exception or aborted match. Full causal attribution of those existing
  models is outside this prototype's native acceptance.

Evidence: `start-status.json`, `day-menu.json/png`, `day-bots-01/02.json`,
`day-domination-log.json`, `day-state-02.json`, `day-after-respawn.json`,
`day-terminal.json/png`, `day-merged-warnings.json`.

Inventory isolation: pre-process and post-raid inventories both contain 303
items; their raw JSON hashes differ. The pre-process baseline stored only a
hash/count, so this does not establish an item difference. Server evidence
records two native ProfileFixer additions of missing `StackObjectsCount=1`
before the first raid, and then successful snapshot restore and protected-end
persistence at 13:38:13.254/.300. The stronger final-candidate comparisons below
confirm exact preservation across both Arena raids. No profile backup was
restored over the live server.

## Final candidate: Factory night and regression controls

Fresh process `c8d58fc2`, 13:49:20, all 16 workspace/host/loaded plugin hashes
identical. Client
`612025302F213FFB37E8FCDE04FA1856A1C872E4C44DA3ECB02157F27A99DA4E`, companion
`537E1C0B2C25A2ACC1362F869E9B3BE70C01E106D2E1F857119B29F72D6A7FDC`;
server unchanged from the first candidate.

- The reward card is now readable on its dark background (`final-menu.png`).
  Factory night was selected with the actual 03:28 toggle; it loaded
  `factory4_night`, and all three point placements passed at the same positions
  as day. The 5v5 roster became live and autonomously captured all three points.
- The height negative used a real reachable tunnel position
  (-6.77, -2.6, 12.0): only 2 m horizontally from B but 2.6 m below it. B stayed
  BEAR while the local USEC remained there; no attack/capture occurred. Fresh
  runtime HUD/status snapshots and ownership logs are the evidence, not cached
  `DominationPointState` inspect handles (those are value-type snapshots).
  Moving the player onto B's actual floor then neutralized B at 13:52:46 and
  captured it for USEC at 13:52:51, matching the separate five-second phases.
  All teleports passed actual navmesh, room and route checks.
- The corrected world label at A is compact, raised and clearly visible above
  the actors. A Shield pickup still has its model, short ring and label but its
  tall beam is absent within the objective. `night-a-final.png` shows the
  previously obscured location with the final rendering. Nearby point actors
  and contested ownership remain visible/readable.
- Night gameplay was observed for more than two live minutes, including moving,
  holding, contested states, kills and replacements; it was then cleanly left
  via the native disconnect/results flow. Bridge `_match`/`_world` were null,
  `_live=false`, `Assignments.Count=0` afterward. Complete inventory snapshots
  taken after native login, before this raid and after return were **exactly
  identical**, with 303 items (`inventory-night-comparison.json`).
- Original TDM then assembled 5v5 and reached Live with its usual 50-kill /
  10-minute rules. Autonomous kills advanced its team score to 2:0. Read-only
  checks: `IsDomination=false`, `_domination=null`,
  `ObjectiveHudVisible=false`; zero `Arena Domination point` GameObjects, no
  Domination brain layer in the bot snapshot. Its native return again preserved
  the complete inventory exactly (`inventory-tdm-comparison.json`).
- Ordinary PMC Factory was entered with native bots=None. `Current` Arena match
  remained null, the objective bridge was empty/inactive and no point GameObject
  existed. The screenshot shows the vanilla raid HUD without Arena panels.
  Native Gate-3 extraction succeeded after 11.1 seconds following a test teleport
  to the enabled extraction volume; the ordinary results included the XP page.
  No forced death/disconnect or profile restore was used for this control.
  The post-PMC inventory still has all 303 items. Sorting by identity and
  recursively comparing all fields found only removal of two redundant
  `upd.StackObjectsCount=1` fields from the equipped dogtag and bayonet/scabbard.
  These are the same two items repaired by SPT's ProfileFixer before the first
  startup. Item identities, templates, parents, slots, positions and all other
  fields were unchanged. This is ordinary serialization, not an Arena loss.

Evidence: `final-start-status.json`, `final-menu.png`,
`night-under-b-player.json`, `night-under-b-state.json`, `night-under-b.png`,
`night-domination-log.json`, `night-a-final.png`, `night-state-final.json`,
`night-bots-final.json`, `tdm-control-state.json`, `tdm-control-bots.json`,
`tdm-control.png`, `pmc-match-null.json`, `pmc-points-none.json`,
`pmc-control.png`, `pmc-extracted.png`, `final-server-arena.json`.

## Logs, cleanup and limits

The first match recorded 78 assignments, 136 movement starts, nine hold entries,
15 ownership changes and 28 bounded trip rejections: 26 no-complete-route and
two no-progress. Some surviving bots could hold C but rejected later A/B
destinations; the fallback reassigned them without teleporting or stopping the
match. This remains useful navigation-tuning evidence, not a claim of perfect
path success or a reason to remove route validation.

Merged client/server/EFT inspection (`day-merged-warnings.json`,
`final-merged-warnings.json`) found no Domination runtime exception, NRE or
protected-end failure. Native asset errors remain, as do spawn-marker category
repairs and transit diagnostics also seen in the ordinary PMC control. The
preexisting startup reward-withheld warning preceded all Domination testing.
No reward was requested for either Domination raid. Existing native weapon model
errors are recorded, not claimed fixed or comprehensively root-caused here.

All Arena settings were preserved as the full original configuration file and
restored after EFT shutdown, verified SHA-256
`22351512AC918FB45E7D4130FD826F79B52EAA6789716E1D035C5B9324B60C3D`.
Temporary DeveloperMode was never persisted. Godmode/infinite-ammo test hooks
were explicitly disabled or automatically ended with their raid; no walk,
tracking, time or weather overrides remain. EFT quit gracefully; SPT server
stopped through gpu-run/Ctrl-C. Final status confirms neither is running and no
other EFT/server/launcher process exists (`cleanup.json`, `host-stopped.txt`,
`end-menu-status.json`).

Boundaries: no exhaustive Factory navigation/performance sweep, no time-limit
draw test, no separate same-floor solid-wall capture negative, and no full
second score-limit match on the final rendering-only update. Day supplied the
natural 200-point terminal and local-death/respawn evidence; the final candidate
supplied night capture, height exclusion, rendering, cleanup and both regression
controls. The first wrong-floor candidates were rejected by UiBridge; only the
accepted tunnel location above was used. All scoring and captures came from
normal runtime occupancy; inspection remained read-only.
