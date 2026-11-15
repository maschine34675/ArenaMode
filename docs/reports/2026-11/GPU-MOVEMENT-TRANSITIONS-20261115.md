# Movement transitions — native validation

2026-11-15, branch `codex/movement-transitions-20261115`, base `4b3bf6c8`.
This is a development change, not a release. The client alone is deployed;
Redeploy.Core remains the released 1.0.24 (`aa46ec84…`). No external movement-mod
code or assets were imported. See the [source contract](../../reviews/MOVEMENT-TRANSITIONS-20261115.md).

## Evidence and method

Artifacts live under `artifacts/movement-transitions-20261115/`. Native plans,
per-frame read-only observations, captures, profile copies and installed/source
manifests are under `native/`. UiBridge runs native commands and reads actual
movement state, accepted motion, contact, weapon authority and aiming. A completed
script or delivered Jump is not evidence of takeoff: the checks require native
Jump entry and increasing height. Fire checks use actual shot events.

SPT-GPU-2 runs solo Factory Deathmatch with two participants and no score/time
limit. Test-only godmode and infinite ammunition keep the short movement samples
repeatable. These hooks do not establish ordinary damage/ammunition behavior.
The other host is not used for gameplay in this run. Syncthing remains enabled.

## Measured causes and corrections

| Observation | Correction |
|---|---|
| Released sprint-jump capture has no usable first-person hip-fire weapon; its native weapon-authority curve stays at 1. | Substitute the single visual curve read only for the scoped ready first-person sprint-jump firearm. No stored transform or animator override. |
| Crouching can begin the slide while the animator still reports Sprint. The first sprint-exit motion then precedes component Tick. | Bind the exact native sprint-exit instance at both boundaries against the same witnessed Sprint predecessor. |
| Logical standing pose reaches 1 immediately, but MOVE → Jump requires smoothed animation Level above 0.6. Gradual standing outlasts the 250 ms jump reservation. | For an admitted slide jump, use native immediate standing, preserve headroom/physical maximum, and synchronize its native smoothed-pose property even on the equal-pose early return. |
| A fresh descending crouch is confirmed by physical contact at about 6.76 m/s. Native Sprint entry then requests zero XZ before the queue is consumed. | Retain that contact's accepted velocity within the existing deadline; subsequent requested movement can only reduce it by actual collision loss. |
| Sprint release, a standing Tick, native sprint exit and later crouch arrive in separate frames. The old observer has already cleared its sprint flag. | Also accept the existing witnessed sprint-end timestamp within the same 300 ms window; retain exact predecessor, state, sample and support checks. |

Immediate standing uses native force-pose semantics: it omits gradual standing
stamina and PushUp skill accumulation. Native Jump entry still charges the jump
once. This is an intentional, narrow behavior change, not a claim that every
pose-related stamina effect is unchanged.

## Candidate evidence

- Released baseline: client `4b05b3e5…`, instance `0509c2bd`;
  `20261115-093034-baseline-sprint-jump-2`, frame 47190 was visually inspected.
- C: client `9601e296…`; `c-sprint-jump.json` records 32 airborne Jump samples
  with ADS false and weapon authority 0, plus ten actual shots. Capture frame
  38666 shows the hip-held HK416 firing. `c-idle-jump.json` separately records
  grounded ADS and refused airborne idle ADS. This is historical evidence,
  not acceptance of a later client.
- D: `d-slide-pose.json` isolates the failed MOVE takeoff with ground true:
  the native request expires at smoothed pose about 0.399; Level reaches 0.607
  only afterwards. Installed animator extraction independently confirms the
  `Level > 0.6000000238` condition and no IsGrounded condition on that edge.
- E: client `0e270cbd…`, instance `b54b1274`; `e-slide-jump.json` records the
  owned sprint-exit slide, buffered jump, native standing and actual rising Jump.
  A brief native support loss ends the slide before Jump entry, so this case
  does **not** prove preservation of slide carry.
- E: `e-land-slide.json` crouches too early (roughly 510 ms before contact) and
  correctly expires. `e-land-slide-late.json` supplies a valid fresh crouch and
  exposes the zero-motion landing reset above, corrected and retested below.
- E: `e-vault-jump.json` records held vault timer 0.83 s, above native 0.6 s,
  with Jump refused. After release resets the timer, a fresh tap enters Jump.
  This does not create an unheld stale timer and therefore does not prove that
  separate recovery branch in the running game.

## Final G: native results

Client SHA256:
`8078CCC3E93F8EBF432EA23B4B956491942246D1951FF4AC49EF417269D92746`.
Loaded instance `e0beebcb`, started 11:10:15 local time. `candidate-g-status.json`
and `g-respawn-status.json` report 16/16 plugins and 2/2 libraries identical
between VM build, host disk and loaded process. `candidate-g-source-manifest.json`
binds all ten changed production files to this client and the base commit.
The source is a working-tree development build stamped with the base commit;
the source manifest, not that stamp alone, identifies the tested implementation.

| Scenario | Observed result |
|---|---|
| Sprint release → delayed crouch → slide → jump | `g-slide-jump-standing.json`: release at 1185 ms, crouch at 1234 ms, 38 owned slide samples, Jump command at 1316 ms, 66 airborne Jump samples beginning at 2067 ms, height reaches 1.606 m. The native sprint-exit animation completes before takeoff; no forced animator transition. |
| Air crouch → landing slide → jump | `g-land-slide-late.json`: slide at 1905 ms, fresh Jump at 1927 ms, airborne Jump at 2015 ms, height reaches 1.221 m. Native horizontal seed is 4.41048 before the existing motion scale. F independently showed the direction handoff matching the accepted slide, with seed 4.00341 and direction (-0.998909, 0, 0.046698). |
| Sprint-jump firing and ADS refusal | `g-sprint-jump-standing.json`: 29 airborne Jump observations, all ADS false and weapon authority 0; ten actual shots, frames 8296–8368. Aim command is delivered at frame 8309 while firing. Capture frame 8317 was visually inspected: normal hip-held HK416, no ADS. |
| Ordinary jump and ground aiming | `g-idle-jump.json`: eight grounded ADS observations; all 41 airborne Jump observations have ADS false, including the idle aiming attempt; three actual shots. |
| Held vault and released fresh jump | `g-vault-jump.json`: held timer 0.80043 s exceeds native 0.6 s; the attempted jump remains Idle. Release resets the timer; the next fresh input enters Jump and rises. |
| Inventory cancels queued slide jump | `g-inventory.json`: ten observations contain the pending request before opening. During/after the inventory there are no pending requests and no delayed Jump, including after closing. |
| Movement master off | `g-master-off.json`: starts standing, records 82 native Jump observations; slide owner, contact witness and slide request remain null. Master is restored afterwards. This is an Arena feature-off control, not an ordinary PMC-raid test. |
| Death and respawn | Native event 5, frame 20518: body 2 → 4. `g-respawn-bindings.json` binds slide, double-jump credit and contact witness to body 4; active slide is false, command/slide/native-entry requests are null. Test hooks are automatically off after death. |

`g-bepinex.log` contains no Arena Warning/Error/Fatal. The client was returned
through the normal Arena result screens to MainMenu and quit gracefully.
Both GPU hosts subsequently report EFT closed. No server, shared Core, session
helper or companion binary was deployed by this change.

## Offline verification and restoration

Final coverage is **60 distinct verification steps, all passing**, including the
full xunit suite: **3,918 passed, three known environment-gated skips**. No runner
step deployed into the live installation. The three focused leaves also pass
their independent source reviews and parent verification. Final slide checks
contain 186 assertions and 51 compiled source faults.

| Run | Result | Evidence under `artifacts/movement-transitions-20261115/` |
|---|---|---|
| Full automatic selection from `4b3bf6c8` | 53 PASS, four FAIL, 57 steps | `first-full/full.log`, `first-full/step-logs/` |
| Automatic selection for the subsequent test/document delta, including every failed step and the full suite | 49 PASS, zero FAIL | `delta-verification.log`, `first-full/delta-plan.json` |
| Combined coverage | 49 rechecked steps plus 11 unaffected first-run PASS; zero unresolved | `combined-verification.json` |

All four first-run failures included an existing menu-layout source scanner
crossing from `Bind(this)` into later methods. Its scan now ends at the actual
call; a regression retains detection of unknown config bindings and the existing
roster exclusion. All 13 focused menu-layout tests pass. The Iceberg wrapper also
had one transient helper process-probe failure: its four focused cases and the
complete wrapper subsequently passed. The helper source was not changed; the
original failure's precise cause remains unproven.

Before the second run, all four real worker working trees matched byte-for-byte.
The only root delta was the scanner test and three documentation files; all ten
G production-source hashes still matched. The existing selector chose 48
harnesses, including three additional steps and the verification runner's own
checks. The 11 retained PASS have no installed config/profile/log dependencies.
Full snapshots, selection reasons and original failure logs are retained; no
cache entry was fabricated. The second run also finished with the same G client
installed. Only this result record and HANDOFF prose were updated afterwards.

`native/after-final-20261115-102444/verification.json` verifies all three
installations: seven whole profile JSONs each differ from the first actual Arena
entry only in allowed Health/Hideout timestamps; all 61 PitFireTeam files per
installation are byte-identical; the original Arena config hash is restored
(`575cdcc0…945e4`). Client G matches on all three disks; Core remains
`aa46ec84…b915b`. No profile was manually changed, restored or deleted, and the
four pre-existing conflict copies are unchanged.

A later read-only recheck repeats these results for all three installations in
`native/after-final-20261115-111148/verification.json`.

The earlier pre-login backup is retained separately. SPT's `ProfileFixerService`
fills two missing `StackObjectsCount` fields with their default 1 at game start:
server log `spt-SPT-GPU-2-20261115.log`, lines 655–658, 08:22:47 UTC. The first
Arena entry snapshot is at 08:23:59 UTC, before `/client/match/local/start` at
08:24:31 UTC (line 777). Thus those two additions precede every tested raid;
they are not Arena reward or inventory changes. The initial aggregate snapshot
was also taken before the final synchronized restoration arrived and is retained
as intermediate evidence; the timestamped final snapshot above supersedes it.

## Explicit limits

The 150 ms ledge-grace path has production-linked positive and negative checks.
The tested native outer-court walks do not establish its positive case: the
ground-proximity ray was true before the small drops, but no fresh physical
controller contact was recorded. `d-edge-expired.json` does show an expired
contact attempt with no upward impulse and Double Jump disabled. Keep the
positive native ledge case open; do not substitute a proximity ray for contact.

The stale **unheld** vault-timer repair, low-headroom refusal, wall-loss bounds,
expiry/repetition, Jump Boots, Double Jump/Bunnyhop combinations, foreign input
owners, ordinary raids/AI and other maps have source-linked checks or unchanged
scope guards; this run does not claim separate native acceptance of each branch.
Reload/draw/holster/malfunction presentation was not separately exercised here.
The first F fire attempt and one F ground-slide repeat began crouched and are
invalid positive controls; their explicitly standing repeats are the cited cases.
