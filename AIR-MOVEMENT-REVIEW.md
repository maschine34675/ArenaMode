# Arena landing, weapon handling and airborne actions

Implementation: 2026-09-09, on `feature/arena-air-movement`, inheriting the
independent TDM match flow. This is client-only work. Server settings, profiles,
AI, area barriers, loadouts and Redeploy are unchanged.

## Player controls and defaults

The F12 `Movement` section has five new settings, all covered by its existing
`Enabled` master switch:

| Setting | Default | Effect |
| --- | --- | --- |
| `LandingRecoveryMultiplier` | `0.5` | Halves the duration of the native hard-landing speed limit; does not reduce its strength or fall damage. Range 0.1–1. |
| `WeaponDrawSpeedMultiplier` | `1.5` | Speeds up firearm draw animations. Range 1–3. |
| `WeaponHolsterSpeedMultiplier` | `1.5` | Speeds up firearm holster animations. Range 1–3. |
| `AirVaultEnabled` | `true` | Allows an explicit native vault/climb request while jumping toward a valid obstacle. |
| `AirHipFireEnabled` | `true` | Allows conventional firearm hip fire during a genuine jump, without enabling airborne aiming. |

Use the game's existing vault/climb binding while moving toward the obstacle.
There is no new key, automatic ledge catch, teleport or artificial grounded flag.
A queued native mantle takes precedence over double jump and the new hip-fire
exception. The native animation graph still decides whether to enter the mantle.
An unaccepted request expires after 0.5 seconds and requires releasing the vault
binding before retrying. The ordinary standing vault path is unchanged.

Drawing and holstering are each 1.5 times their native speed by default, so the
animation portion takes roughly two thirds of its usual time. This does not
change global animator speed, rate of fire, reloading, malfunction clearing,
medicine/grenade handling or equip pipeline/loading time. Initial pre-raid and
in-flight replacement-body draws deliberately retain native handling when the
live local-body guard is not yet satisfied.

Walking off a ledge into the separate native falling state and the underbarrel
launcher operation are outside this first hip-fire extension. Native ammunition,
trigger, malfunction, inventory and weapon-specific restrictions remain in use.

## Native contracts and ownership

Read the installed decompiled sources under `../_decompiled/` before modifying
these hooks. `Player.LandingAdjustments` applies the height-based speed limit;
the separate `MovementContext.OnJumpEnd` penalty is not the requested hard-fall
duration and must not be shortened incidentally.

Firearm draw/holster operations use `FirearmsAnimator.SpeedDraw`, including native
ergonomics/fast-switch inputs. Only a currently executing local firearm operation
may own an adjustment. Native refreshes update its baseline without multiplying
an already scaled value. Operation change, setting change, teardown and raid end
release that ownership; restoration must preserve a subsequent foreign write.
Initial acquisition replaces calls inside the large native draw/holster methods
to avoid relying on the tiny common `Start` method being patchable on Mono. The
native speed-refresh wrapper is also small and could inline. If a mid-operation
injury/skill refresh bypasses its hook, the controller update recognizes the
foreign parameter write and relinquishes the boost safely; that particular
operation may finish at native speed. No global animator hook is added to hide
this unverified coverage limitation.

The native jumping state has no vault action. `VaultingComponent.CanVaulting`
also rejects airborne movement, and `GetVaultingStrategy` excludes the jump
animation. The additional request is scoped synchronously to the exact player,
movement context and vaulting component. It retains native obstacle geometry,
clearance, stamina and full hands/gameplay restrictions, including a final check
after the native manual-vault rescan. Pending requests must not execute stale
obstacle parameters or survive a body, world, session or eligibility change.
Once native vaulting has entered, its flags belong to the native state machine.

Air fire replaces exactly one `SuitableForHandInput` call inside
`Player.FirearmController.Idling.SetTriggerPressed(bool)`. It does not patch the
small shared getter or change the global unsuitable-state list. The original
predicate runs first and retains its result whenever already true. Only its
false result for the exact live local `JumpPlayerState` can be extended. Missing
or ambiguous call sites keep the method untouched and produce one diagnostic.
Native firearm operations still own shot execution and ammunition consumption.

`ArenaMovementScope` requires the current started local Arena game/world and
exact living local player body. It denies AI, stale bodies, respawn, spectator
and exit/stand-down contexts. Input extensions also require closed inventory
and unblocked Arena input. All temporary state must be released independently
of the movement master switch, so turning it off cannot prevent cleanup.

## Offline verification and its limits

The production-linked checks are executable with:

```powershell
pwsh -NoProfile -File tools/ArenaHandlingChecks/Verify.ps1
pwsh -NoProfile -File tools/ArenaAirVaultChecks/Verify.ps1
pwsh -NoProfile -File tools/ArenaAirFireChecks/Verify.ps1
pwsh -NoProfile -File tools/ArenaAirFireChecks/Verify-Integration.ps1
```

Each feature suite includes deliberate source faults that must fail named
behavioral assertions and a final run of unchanged production sources. Native
installed assembly contracts and a staged real client build supplement the
host-side models. The integration runner also covers the existing double-jump
behavior and actual TDM menu/settings. Run these sequentially because some
regression projects share their build output directories.

The host-side Harmony instruction/owner-access doubles do not execute Harmony
inside the game's Mono runtime. Extracted native methods do not execute Unity
physics or the animator graph. Neither offline success nor compilation proves
the airborne climb transition, animation timing or visible live shot behavior.
See the dated delivery entry in `HANDOFF.md` for final evidence and client hash.

Final parent verification (2026-09-09): handling 161 assertions/12 source faults,
air vault 70/13, hip fire and shared scope 92/10, double jump 144/8 and native
TDM menu/settings 80/8 all passed, including final production reruns. Parent
independently repeated both delegated suites. Evidence is retained in ignored
`artifacts/`:

- `arena-handling-checks/de3ccca726234be8afbe816287bdf7a9/`
- `air-vault-checks/ef9ec58df7ea422599c721714e3022f6/`
- `air-fire-checks/985dd9fa07954dc986921d003e1a97a9/`
- `double-jump-checks/e74ef2e66e7e4a65a6baf84e4a78d1f0/`
- `tdm-match-menu-checks/92b31c4bebec4c97b7ed302781cc3439/`

Independent four-pass review found no remaining P0–P2 after the pending-geometry,
exact-target cleanup and transactional weapon-speed corrections. The complete
final source also built against the installed client APIs with zero errors and
four existing nullable-context warnings, without changing installed artifacts.

## Pending GPU acceptance

Compare the installed `C:\SPT41` client hash against the dated handoff, restart
the game, then test an Arena with the same weapon/obstacle and movement settings:

1. Compare a hard landing at multiplier 1 and 0.5. The movement slowdown should
   recover in half the time; normal short jumps, fall damage and unrelated
   effects must retain their previous behavior.
2. Draw and holster at multipliers 1 and 1.5, including pistol/primary and fast
   weapon switches. Check reload, firing and malfunction animations afterward;
   toggle the settings during a switch and check for stuck or persistent speed.
3. Jump toward several valid low and high obstacles and explicitly use vault.
   Check insufficient stamina, crouch/prone/inventory, a disappearing or moving
   obstacle, a missed attempt and an area barrier. Native standing vault must
   still work; a failed aerial attempt must leave no flags stuck at landing.
4. Fire single and automatic shots while jumping, including a sprint jump.
   Check ammunition is spent normally, empty guns remain empty, reload and
   malfunction handling still work, and aiming is not unlocked in the air.
5. Combine double jump with attempted vault/hip fire; a pending mantle must not
   spend the extra-jump credit. Check another attempt after landing and respawn.
6. Disable movement during each action, end the raid, and enter an ordinary PMC
   raid. Verify native handling and movement there and unchanged bot behavior.

If air vault never enters despite valid geometry, collect its bounded timeout
diagnostic and a short recording. Inspect the actual native animator transition
before adding any alternative; do not bypass collision or force animation states.
