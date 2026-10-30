# Preset and optic fixes: integrated verification

2026-09-06, branch `feature/arena-visibility-and-labyrinth`.

- `50d2a89` restores full kit descriptions after the native VestTemplate
  null-array regression. It preserves front-plate classification and isolates
  unexpected armor metadata failures from the other description sections.
- `fae3a43` excludes newly generated reflex paths over empty scope rings,
  including secondary paths through authored ring caps. Direct, offset, prism
  and valid primary scope paths remain; authored gear is not removed.

Detailed causes, contracts and boundaries are in PRESET-DESCRIPTION-REVIEW.md
and OPTIC-MOUNT-REVIEW.md. Both production changes and their oracles received
independent adversarial reviews with no remaining P0–P2 findings. The description
test's same-ID cache reuse between catalogs was corrected before the final
run; integration tests now receive the same explicit deployment target properties
as the final builds.

## Executed checks

Root re-executed every runnable gate in `.unlazy/preset-optics`:

- Descriptions: installed native getter/type contracts; **303 assertions** on
  fixtures and both complete 20-kit catalogs; two targeted description mutants,
  the old max-armor regression and existing supply/backpack controls. Supply
  placement/preload checks retain **1,562 assertions** and nine server cases.
- Optics: native filter categories, whole kit/Gun Game draws, seven actual scope
  ring topologies plus four positive direct/offset/prism routes over 12 seeds,
  authored preservation and three assertion-bound mutation controls. The actual
  server JP/DeltaPoint regression and existing weapon/session tests pass.
- Full server suite: **222 passed, 0 failed, 0 skipped**. A final `--no-build`
  execution repeated that result without disturbing deployed Release assemblies.
- Release builds succeeded in server → companion → client order. Four fresh
  built/live SHA256 comparisons match. All **154** tracked external mod/config/
  kit/ladder files match this task's captured baseline.

| Deployed assembly | SHA256 |
|---|---|
| ArenaMode.Client | `468313C25FD07067D56EEAD6EAC285C030D7A94795809FA6190E75C5D6A911A5` |
| ArenaMode.BigBrainCompat | `CABA94878368A3182F68AC787D1B8200EEAFE12454C63E116FA53D11BEF30347` |
| ArenaMode.Server | `D5B9356690CA3C9A3EC7D09C44D29300549EF87DD8CB29B0123BC916850BD186` |
| Redeploy.Core | `3973761FDE09BBDBBD924AA893F3D351ECA50EFE14B93183A6E1C8CE947BAA8C` |

## Playtest handoff

Restart **server and game**. Check that each Tactical preset again shows its
weapon, front armor, helmet/headset and magazine information, then inspect new
kit and Gun Game weapon draws for standalone reflex sights above empty scope
rings. Existing authored builds are deliberately preserved; the new filter acts
on generated additions and does not rewrite catalog files or previously created
weapons. Optional complex multi-child builds are still outside the linear
generator's scope, so another valid mounting path or the source can be retained.

This host has no GPU. Native IL/contracts, offline boundary models, generated
item trees and deployment hashes do not prove actual menu rendering or physical
weapon presentation in a raid. The ledger explicitly hands that check to the
user rather than recording it as passed.
