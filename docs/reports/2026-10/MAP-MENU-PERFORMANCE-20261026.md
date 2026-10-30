# Arena map-menu performance — 2026-10-26

The Domination follow-up fixes repeated compatibility inspection while drawing
the Arena map selection. The user reported approximately 2 FPS. On GPU-2 the
reproduced idle baseline averaged 14 FPS; this is not a measurement of the exact
reported session.

## Cause and change

The updater already refreshes every 0.2 seconds. Materializing all native map
rows made each row independently reevaluate Arena admission, including the
Redeploy body-replacement compatibility/Harmony checks. The optional-mod
filesystem whitelist was already cached and was not the repeated work addressed
here.

`ArenaMapAvailabilitySnapshot` now carries one eligibility result through one
synchronous display pass. Map rows, selected-area preparation and area display
share it. The next refresh replaces the snapshot after mode synchronization;
closing/restoring the screen clears it. The existing strict and roster-only
providers remain separate. In the production-linked eight-row fixture this
reduces compatibility checks from 20 to four per refresh.

Native clicks, claim and final roster freeze still use fresh admission. This is
not a process-wide cache or an authorization bypass. A rejected final start does
not become accepted because the previous display pass was valid.

## Native comparison

GPU-2, SPT-GPU-2 profile, 1920x1080, idle Arena SelectLocation with Domination
selected. Both ten-second windows followed at least twelve seconds of settling,
without menu interactions during the sample. UiBridge supplied the frame data.

| Metric | Before | After |
|---|---:|---:|
| Frames sampled | 141 | 560 |
| Average FPS | 14 | 56 |
| Mean frame time | 71.59 ms | 17.87 ms |
| P95 frame time | 228.79 ms | 27.27 ms |
| Maximum frame time | 370 ms | 73.1 ms |

The severe stall is substantially reduced. This short comparison does not prove
locked 60 FPS, long-session stability or identical results on other hardware.
The client was explicitly restarted; all 16 GPU-run tracked deployment identities
matched host, GPU disk and loaded process. Other Domination presentation fixes
were included in that candidate but do not run in the map menu.

## Verification and evidence

- Production-linked menu checks: 13 assertions and four rejected source mutants
  (row inspection, selected-map inspection, stale refresh and stale final start).
- Native menu regressions: 876 assertions; native area hooks: 202 assertions.
- Shared unit run: 3,213 passed, one existing APBS-version-specific skip.
- Independent read-only review found no blocking admission/lifetime issue.

Local evidence is under `artifacts/domination-followup-20261026/`:
`before-menu-perf.json`, `after-menu-perf.json`, the corresponding map/status
snapshots, `menu-parent-recheck.log`, `menu-independent-review.md` and
`root-verification.log`. Raw evidence can contain private profile details.

Measured client SHA-256:
`E4385856B891A8A31D4A14F4A5A9315C8DD69F9724A2F18506938F86DC2CE5B5`.
No public version bump or release was made.
