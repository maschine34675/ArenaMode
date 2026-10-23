# Native GPU team-start follow-up, 2026-09-08

## Baseline and integration order

User request: apply the necessary fixes from GPU report commit `2327f63`, merging
the prepared double jump into the main branch first. `master` was fast-forwarded
to that report from the shared `feature/customs-contour` history, then received
`feature/double-jump` (`fcfcd71`) in normal merge commit `227fb10`. Only the handoff
needed conflict resolution; both the GPU failure report and feature state remain
documented. Subsequent corrections are developed on `fix/team-start-gpu-acceptance`
and integrated back into master before delivery. No automatic GitHub push.

## Reproduced start requirement error

The GPU test never reached bot activation in the new team-start preset. All five
attempts ended during the navigation preparation window. USEC runs reported no
connected BEAR PMC starts despite three or four connected BEAR bot starts.

The previous runtime required a PMC marker and N bot markers on each side. Native
team population actually requires one local PMC and N-1 allied bots, versus N
opponent bots. `ValidateTeamStarts` now checks those actual requirements for the
frozen local faction. A genuinely missing required marker still blocks readiness.
Native dispatch-zone membership, exclusions, blocked markers, the selected local
point, distinct positions and team separation remain enforced. No additional bot
category, invented spawn location or broader contour is introduced.

The capacity check alone would not address the observed 5v5 shortage (three of
five opponent bot markers). The same contour in the successful legacy GPU run
reported all 17 player and 11 bot markers connected. This disproves an intrinsic
shortage of native marker locations, but does not identify the exact reason for
each rejected new-preset path. See `AREA-CONNECTIVITY-REVIEW.md` for the contained
waypoint connectivity correction and the new diagnostics. Actual AI path corners
remain independently constrained; graph reachability is not a claim that native
AI will choose that route.

## Other report findings

The local scoreboard's `Scav` role came from native `ProfileSettings.Role=assault`
defaults. Commit `853a2ce` resolves human PMC identity from faction while retaining
AI role classification and points. See `ROLE-LABEL-REVIEW.md`.

The grass-render mismatch has a concrete native camera-replacement ordering
hazard: GPUInstancer checks texture dimensions before reacquiring a destroyed
camera. The exact active Arena replacement now binds before that native check;
native SSAA, render targets and shared command-buffer drawing remain in charge.
The installed PFT menu also queues coroutine warmup while its parent is inactive.
The optional Arena-only guard postpones that request without changing pending
state; the later active Show retries it. See `GPU-ACCEPTANCE-COMPAT-REVIEW.md` for
source evidence, bounded repairs and uncertainty (the original logs lack stacks).
Unrelated Unity/optional-mod warning output is not globally suppressed.
The generic native MIA/LEFT summary is not proof of a profile charge: all six
Arena endings in the GPU report restored protected fields. This correction does
not change the protected end pipeline or the generic native summary screen.

## Offline evidence

- Team runtime: 369 production-linked assertions, including both factions at
  sizes 2-5 with exactly sufficient points, plus 19 named negative controls.
  Evidence: `tools/AreaTeamRuntimeChecks/artifacts/30f77d8d5f84472782ca67cf258d3e80/`.
  The controlled navigation seam is explicit; graph behavior is tested separately.
- Independent capacity review (`tdm_role_label_fix`) found no unresolved P0-P2:
  the counts match `TeamDeathmatchPopulation.BotTarget`, safety guards and native
  dispatch membership remain, positive and negative cases cover both factions.
- Role correction: root reran 174 assertions and seven controls, including the
  actual failing classifier from `2327f63`; independent review found no open P0-P2.
- Merged double jump: 143 assertions and seven controls passed again. Evidence:
  `artifacts/double-jump-checks/e8b2779adea241f5858c5cceb62cddac/`.
- Legacy enclosure/runtime: 69,023 enclosure assertions and 13 area client
  contracts passed. This verifies geometry policy, not native navigation meshes.
- Existing integration: all 444 server tests passed with zero skips, followed by
  native-menu/map/mode, TDM/give-up, capture and scope checks and 12 area/polygon
  negative controls. Installed client/server/Redeploy hashes remained unchanged.
- Full staged client compilation passed against the installed game APIs with
  four pre-existing nullable warnings and zero errors. The build did not deploy.
- Render/PFT compatibility: 47 production-linked checks and eight controls passed;
  root reviewed the actual native GPU update ordering, PFT warmup/retry, exact
  Arena ownership and plugin lifecycle integration without an unresolved P0-P2.

- Connectivity: 630 production-linked graph/adapter assertions, ten source
  integration contracts and 13 named source controls passed. Independent review
  found the initial stale-readiness gap; the final parent-first verification now
  removes every descendant of a changed edge before publishing connected starts.
  Re-review found no unresolved P0-P2. Discovery is limited to 24 directed queries
  per frame, followed by a bounded linear final sweep (at most 56 queries for the
  current 28 markers). Placement still rechecks every used leg. Full details and
  remaining actual-AI-route limitations are in `AREA-CONNECTIVITY-REVIEW.md`.

Root reran all seven executable integration gates successfully after independent
review: team runtime, connectivity, roles, double jump, staged build, render/menu
compatibility and existing integration regressions. Exact output fingerprints
are recorded in `docs/ledgers/TeamStartAcceptanceChecks/GATES.md`. All nine gates are
now met including integration into master and local delivery from clean code
commit `e2859e8`. The delivered client SHA256 is
`DEFC7DA21EA311A5078F83037FEA56972C24C36CAE499F5D9FEF7FCC06CE973E`;
the previous-client backup and delivery manifest are recorded in `HANDOFF.md`.
Server and Redeploy remain unchanged. GPU mirror completion and native retesting
remain explicitly unverified; the build/graph checks do not prove AI movement.

## Required native follow-up

Use the new **Customs contour (team starts)** preset explicitly. Check both USEC
and BEAR, sizes 2v2 through 5v5, actual initial roster counts, repeated deaths and
refills, boundary confinement and purposeful movement after assembly. Record the
new per-marker navigation diagnostics if preparation rejects a point or a team
cannot refill. Keep normal PMC and legacy contour isolation checks.

The merged optional double jump needs its first GPU acceptance: normal takeoff,
one extra jump using the configured binding, no further jump before landing,
collision against the contour, and no extra jump during an ordinary PMC raid.
Check the corrected F9 role before and after respawn. This host has no GPU and
does not claim those live outcomes from a successful compile or offline harness.
