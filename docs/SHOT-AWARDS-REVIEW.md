# Shot-time announcer awards (2026-09-14)

Presentation-only extension of `ANNOUNCER-EXPANSION-REVIEW.md`, developed on
`feature/additional-announcer-lines` and integrated on main. No score bonuses,
weapon behavior, movement restrictions, damage, profile or respawn changes.
See dated HANDOFF entries for actual deployment bindings; older counts below
describe the original shot-award implementation.

## Behavior

| HUD award | Required state at firing |
| --- | --- |
| AIRBORNE | Local firearm bullet, at least 0.5 seconds continuously airborne (jump or fall) before firing, without aiming |
| NO SCOPE | Local firearm bullet, native `Weapon.BoltAction`, without aiming |

The bullet must directly kill an eligible enemy. No headshot or one-hit kill is
required. Both physical grounding checks must say airborne; this excludes
ambiguous grounded/near-ground states. A scope attachment is not required for
NO SCOPE. Underbarrel weapons and grenade launchers are excluded. A later
landing, ADS change or weapon swap does not change the recorded facts. Bleeding,
explosions and other delayed damage never borrow the previous shot's award.

The stricter flight clock observes `MovementContext.CheckFlying(float)` after
native ground refresh, independently of Arena movement settings. It starts with
the first confirmed airborne observation; either grounded flag, vaulting,
supported/stationary weapon use or a dead body clears it. Double-jump without
ground contact does not reset it. Scaled game time excludes time spent paused.
Body, movement context, game, world and award-round identities must still match
at firing. Invalid/backwards clocks and stale observations restart measurement.
Freshness tolerates two actual movement/render timesteps (at least 0.1 seconds),
so low framerates still work; this is an observation-based clock, not continuous
physics proof between updates. Native `FreefallTime` is intentionally unused:
it can reset near the jump apex or stalled vertical motion.

Threshold revision checks: 177 executable adapter assertions, 55 native/source
contracts, six compiled fault controls including restoration of instant-airborne
eligibility. The 0.499/0.500 boundary, landing, pause, slow updates, identity and
round changes, early shot/late impact and unchanged No Scope behavior passed.
Independent adversarial review found no blocking issue; native raid feel remains
a user test. The 2026-09-17 supplement renamed the former TRICKSHOT HUD/key to
AIRBORNE / `airborne` and supplied its own recording. The 0.5-second shot-time
condition remains unchanged; no additional award trigger was introduced.

One label per kill, in this order: First Blood, Payback, Shutdown, multi-kill,
AIRBORNE, NO SCOPE, spree, single-hit Headshot. An airborne bolt-action hip-fire
kill selects AIRBORNE. Multi-kills keep the higher queue priority introduced by
the preceding announcer change. First Blood and other higher awards can still
replace either special-shot announcement.

Both logical keys now use their supplied recordings at
`BepInEx/plugins/ArenaMode/sounds/airborne.ogg` and `no_scope.ogg` through the normal
root-file loader. Their HUD labels and queue priorities stay separate (70/65,
below multi-kills at 80 and above sprees at 60). The supplement's provenance and
licence status are recorded in `ANNOUNCER-ADDITIONS-20260917.json` and
`AUDIO-LICENSES.md` beside the assets. `reserve/trickshot.ogg` and
`reserve/air_superiority.ogg` are packaged but have no active trigger. Missing,
undecodable or invalidated optional clips use Godlike; mute/output failures
preserve the existing no-play behavior. The first fallback per logical key and
match is logged, without per-kill spam. Existing independent volume applies.
The existing clip diagnostics can also report `missing_clip` once for the absent
logical recording before the provisional-recording message. That message alone
does not mean Godlike was silent; the actual play request is logged as `godlike`.

## Native boundary and lifetime

- Base `Player.FirearmController.InitiateShot` captures scalar body/weapon,
  airborne and bolt-action facts. The native `ClientFirearmController` override
  calls this base method. The actual held weapon and controller must belong to
  the exact live local body in the active Arena world/local game. This does not
  depend on enabling Arena's movement changes.
- `Shot.Create` clears metadata on every observed pooled creation, including
  unknown/bot shots. A verified parent copies its frozen scalar flags to a fresh
  child token for penetration/ricochet/fragments; an unknown parent cannot borrow
  ambient firing state. Weak-table values retain no native objects.
- Offline `ClientLocalGameWorld` inherits `ClientGameWorld.ShotDelegate`;
  patching only `GameWorld.ShotDelegate` would miss these impacts. Its prefix and
  finalizer enclose the direct native collider hit. The projectile reference is
  retained only for this synchronous call, then cleared.
- Accepted `ApplyShot` and `ApplyDamageInfo` contexts must agree on projectile
  token, physical shooter, weapon, fire index, ammunition source and ShotId.
  Metadata is re-read at each contact to reject in-stack pool reuse. Native kill
  capture also requires both unnested bullet contexts and an actual lethal bullet.
  Unknown, reentrant, friendly, administrative or unscored deaths do not gain an
  award. Existing Headshot and scoring rules are unchanged.
- Raid reset and the real award-round boundary invalidate all projectile facts.
  Firing scopes carry an epoch, preventing an in-flight old scope from creating
  new-round facts after a reset. Scoped finalizers preserve native exceptions and
  cannot resurrect a cleared scope. Old respawn bodies cannot borrow a new local
  body's profile identity.

## Checks and later user test

The source-linked history harness exercises frozen facts, pool reuse, descendants,
round reset and accepted death contexts. The native-adapter harness runs the
actual hook class against explicit engine doubles and verifies native metadata;
this is not a Unity/Harmony raid test. Audio/combat harnesses execute the full
production award, match and playback code, including deliberate compiled faults.
Existing unit, PFT, Tactical, TDM and give-up checks remain part of validation.
The staged native build resolves installed EFT/Core assemblies while disabling
deployment and verifying installed DLL hashes unchanged.

Final offline validation: 113 history assertions and six compiled faults;
128 adapter/history conjunction assertions, 46 native metadata/wiring assertions
and five compiled faults; 236 audio and 194 full-match/combat assertions, six new
shot/audio faults, plus all existing audio/metadata/combat fault controls and
46 unchanged audio hashes/decodes. Unit suite: 621 passed, two existing fixture
skips. Existing PFT 248, team expansion 830, give-up 618 (16 faults), Tactical
polish 74 (three historical/one fault) and TDM match/exit 227 (15 faults) pass.
Independent adversarial review found no remaining actionable defect. The first
native build exposed the incorrect field name `Shot.WeaponItem`; native metadata
confirmed `Shot.Weapon`, and the corrected client builds with four existing
nullable warnings and no errors. No audible or GPU runtime acceptance is claimed.

Commands for repeating the new checks:

```powershell
pwsh -NoProfile -File tools/ShotAwardHistoryChecks/Verify.ps1
pwsh -NoProfile -File tools/AnnouncerChecks/Verify-ShotAwards.ps1
pwsh -NoProfile -File tools/ShotAwardIntegrationChecks/Verify.ps1
pwsh -NoProfile -File tools/DoubleJumpChecks/Build-Staged.ps1
```

Use the current combined client binding from HANDOFF. Test a normal grounded
hip-fire kill, short-step/early-jump kills below 0.5 seconds, a sustained airborne
hip-fire kill after 0.5 seconds (including double-jump without landing), an
airborne ADS kill, and bolt-action hip-fire/ADS kills. Consume First Blood first
and leave more than four seconds between kills when testing the special labels,
so a higher-priority award does not mask them. Repeat after respawn and a round
change. Also test firing airborne then landing before impact, and a delayed
bleeding death. Confirm the AIRBORNE and NO SCOPE recordings, independent volume
and normal-raid silence; Godlike is only the missing/invalid-clip fallback.
Do not install the isolated staged DLL over a newer combined/respawn build.
