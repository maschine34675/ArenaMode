# Arena polish and bleeding NRE review — 2026-09-06

Six changes are implemented as separate commits on
`feature/arena-visibility-and-labyrinth`:

| Change | Commit | Behavior |
|---|---|---|
| XP gain toasts | `3389988` | Suppress StatisticNotification at exact Arena presentation boundaries; XP processing, history and other subscribers remain native. |
| Round result severity | `4b7c976` | Ordinary Tactical victory/defeat is Info; genuine no-contest failures remain Warning. |
| Armor summary | `9d3bb6c` | Actual installed front plate, with physical frontal soft protection only when no front plate exists; stronger side/back plates do not count. |
| Retired-body cleanup | `d847f92` | Remove the old living body's visual bleeding registration before its owned Arena replacement destruction. |
| Visible laser beam | `99b7803` | Separate F12 Effects / VisibleLaserBeamBrightness, default 2, range 1–5; 1 disables the addition. Point brightness remains separate. |
| Optional grenade supplies | `207f29d` | One RGD-5 and one Zarya per normal kit rebuild, space permitting after existing gear/ammo. No protected 5v5 entry-gear change or Gun Game promotion refill. |

## Current log diagnosis

The inspected `D:/SPT41/BepInEx/LogOutput.log` is the 2026-09-06 snapshot ending
20:21:25, 26,606,630 bytes. Of 30,164 Error entries, 30,156 share the native
`EffectsCommutator.UpdatePlayersBleedings -> Player.Position ->
BifacialTransform.position -> Transform.position` NRE. The storm runs
20:15:29.003–20:19:07.928 and starts roughly two seconds after a surviving-player
solo Tactical round replacement.

Native heavy-bleed removal/death stops its exact IPlayer registration, while
Dispose/Unregister/Destroy do not. Arena's two live-body retirement sites could
destroy a still-living bleeding body without that final visual cleanup. The
offline replay reproduces this failure using the actual extracted native methods.
The fix closes this concrete lifecycle gap; the historical stack cannot identify
which exact old actor remained registered. Only a new raid can confirm absence
of recurrence in the game.

The other eight Error entries are four BotWeaponSelector failures, one
BotFirstAid/ContinuousHealing callback, one explosion-fragment failure and two
inactive MenuScreen coroutine errors. Some occurred around raid teardown, so an
indirect lifecycle interaction is possible, but the traces do not prove Arena
caused them. They are neither hidden nor broadly patched. Detailed timing,
native evidence and cleanup failure behavior are in BLEEDING-NRE-REVIEW.md.

## Executed verification

- Exact Arena notification checks: 99 assertions, nine intended runtime
  mutations rejected, and installed native producer/presentation contracts.
- Laser: installed assembly, material and DX11 shader contracts; production-linked
  draw/restore replays and 13 existing plus six beam-specific runtime mutations.
  Shader inspection proves the distinct factor path, not perceived GPU output.
- Armor: 52 assertions with both real 20-kit catalogs and the old max-class
  behavior rejected at the actual summary caller.
- Supplies: both real catalogs receive 40 optional requests each; the DB-backed
  placement model retains original items/ammo and places those requests in
  quick-use containers. Full/filtered grids, ammo priority, native preload,
  promotion behavior and backpack regression checks pass. Nine actual server
  cases cover catalog transformation and Armed/protected/session lifecycle.
- Bleeding: installed native API/IL contracts, native-method failure reproduction,
  exact identity/scope/callback/rollback replays and five intended mutations.
- Full server suite: **221 passed, 0 failed, 0 skipped**, also rechecked from the
  built Release test assembly after final deployment without rebuilding.
- Existing tracer, give-up, TDM runtime, Tactical expansion and goal-handoff
  regressions passed through Verify-Integration.ps1. (The `tools/ArenaPolishChecks` scripts were
  retired on 2026-10-23: their baseline oracle no longer matched the install and the integration
  wrapper deployed the server DLL; the regressions live on in the leaf harnesses and
  `tools/ArenaNotificationChecks`.)
- Independent adversarial reviews found no open P0–P2 findings for all six
  production changes. Initial harness name/import/parser errors were corrected
  before the successful checks; compile failures did not count as mutant passes.

All production Release builds succeeded. Deployment verification builds server,
optional companion, then client last so the linked Redeploy.Core Release stays
deployed. A fresh read confirmed these built/live SHA256 pairs match:

| Deployed assembly | SHA256 |
|---|---|
| ArenaMode.Client | `B70BE71B678BDE7268D33EBB6645BC72140EE9C174AE97FEE24712B552BF847A` |
| ArenaMode.BigBrainCompat | `C923FA9FBB01E01F293438559AA534483A429CE029EBB4328D6F8D5FAAEF55D8` |
| ArenaMode.Server | `99ADEFC278427DB03B866135E857A22ABA75D802F314C6A7B7BED23F347B0459` |
| Redeploy.Core | `3973761FDE09BBDBBD924AA893F3D351ECA50EFE14B93183A6E1C8CE947BAA8C` |

All **154** tracked external-mod/config/kit/ladder files match the before-build
baseline. No profiles or external mod settings were changed by this work.
BrightLasers is absent in this baseline; coexistence is a contract replay against
the previously inspected version, not a new live third-party installation test.

## Required GPU handoff

Restart **server and game** for the new server supplies and client changes.
There is no GPU on this host; offline contracts, models, native-method replays and
deployed hashes are not a live raid. Verify:

1. Beam setting 1/2/4 at near and far distances with point brightness fixed;
   team colors, NVG/IR, weapon changes, death/rebind and an ordinary raid.
2. Front-class buy labels, actual quick-throw frag/flash availability and
   consumption, full-grid skips, and unchanged protected 5v5 entry gear.
3. Quiet XP gain presentation and Info ordinary round outcomes, with real
   failures still visible.
4. Survive a solo Tactical round while bleeding, then cross several living
   replacements and a death respawn. Check for recurrence of the bleeding NRE
   and verify current player/bot blood effects still work normally.

The local `.unlazy/polish-nre` ledger retains command/output evidence and the
explicit GPU handoff; it is not a claim that these in-game checks were run.
