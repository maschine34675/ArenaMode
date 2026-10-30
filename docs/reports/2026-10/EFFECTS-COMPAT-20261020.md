# EFT Effects Manager / Broken Limbs compatibility

Implemented 2026-10-20. This document describes the adapter and offline checks.
Native acceptance is recorded separately by the integrating session.

## Reviewed inputs

- [EFT Effects Manager 1.1.0](https://github.com/harmonyzt/EFTEffectManager/releases/tag/1.1.0):
  source `7ec87db63f77a4ac066c0c523b32156fe22c2624`, published DLL SHA256
  `CEC8B4A666DA448CDFBC42CA428A70274327BFAC13EC51BCA2C75C7D051DBF62`.
  Assembly version is **1.0.0.0**, MVID
  `09338347-931e-4943-8d90-4fa7b05af4c0`. There is no `BepInPlugin`.
- [Broken Limbs Don't Heal 1.1.0](https://github.com/harmonyzt/BrokenLimbsDontHeal/releases/tag/1.1.0):
  source `1691d5e57e324fabf86959241b30753c4f9e1f4d`, published DLL SHA256
  `415F05A729F49E63FDF80CE3251252391C6EF467CE8D8185F7E880A7C6BDC1C2`.
  Its GUID is `com.harmonyzt.BrokenLimbsDontHeal`; assembly version is 1.7.0.0,
  unlike its plugin version.
- Native `Assembly-CSharp.dll` SHA256
  `EE25CEE1259777B38ED8B3E7841FDC2DB3C98540B1469FA539B1FF183476E436`.

Sources, release downloads and decompilations remain in ignored
`artifacts/candidate-audit-20261020/health/`. Foreign DLLs, icons, source and
configuration are not part of Arena's package.

## Problem and scope

Effects Manager binds one `RaidPlayer`, one health controller and seven native
callbacks at world start. An Arena kit swap, death respawn or living PFT wave
refill creates another physical player without starting another world.
Broken Limbs' healed-fracture creation, re-fracture checks, movement and
ergonomics penalties therefore continued to refer to the old body.

The adapter retains the foreign effect and penalty registrations, public event
subscribers, existing configuration and native ordinary-raids behavior. It does
not disable Broken Limbs' injuries or configure their severity.

The manager is discovered by its actual loaded assembly, not a fabricated
plugin GUID. An assembly-load notification only schedules discovery; Unity
work happens on the main thread. Menu admission retries a library whose
dependent plugin had not finished initialization. Broken Limbs is an optional
soft dependency so its normal startup can complete first.

Required fields, exact method signatures, callback attachment/removal and the
native `OnGameStarted`/`Dispose` Harmony receipts are checked. The callback
bridge must still have exactly the seven native subscriptions and only the
reviewed body/controller writes. C#'s compiler-generated delegate caches are
allowed only for the exact seven handlers and their real delegate types,
including `Player.DamageDelegate`. Unknown contracts reject Arena only.
Matching future builds remain eligible with a one-time unreviewed-build
warning. The release number cannot be inferred from the library's unchanged
assembly version.

## Replacement and teardown

1. Capture the library's exact native body/world binding in the admitted local
   PMC raid. A confirmed pending Arena arm requires its matching map and native
   server identity; an ordinary same-map raid, Scav or Hideout is insufficient.
2. At Core's synchronous `Replaced` event, verify the old lease and the new
   `GameWorld.MainPlayer` / `LocalGame.LocalPlayer`, physical profile, world and
   living health controller. The event precedes disposal of an old living kit
   body. Initial kit replacement also works when it completes before the
   adapter's start observer has captured the foreign initial binding.
3. Remove only that controller's temporary skill contributions and
   effect-ledger entries, then detach its six health callbacks and damage callback.
   Reset only its own foreign speed-limit context. Never clear the global
   registration, provider, skill or effect dictionaries.
   Skill entries retire individually after their subtraction is confirmed.
   If a native skill listener throws after the field was written, read-back
   prevents double subtraction on teardown retry; a before-write failure keeps
   its contribution and old callbacks. Such failures remain visible and reject
   further Arena starts rather than claiming the match safely continued.
4. Bind the new player/controller, reattach callbacks and evaluate only the
   replacement body's existing effects and penalties. Do not copy effects from
   its predecessor. The shared bullet-whizz subscription is idempotent.
5. Retain weak retired-body identities to reject duplicate old events.
   An idle tick also repairs a settled authoritative body change; it does not
   rebind while Core is replacing a body.
6. Exact Arena teardown performs the same scoped cleanup and removes the whizz
   subscription. Native ordinary cleanup remains untouched. A different
   world's disposal cannot unbind the current body.

Neither `GameWorld.OnGameStarted` nor `EffectsManager.NotifyRaidStarted` is
replayed. The latter invokes `ClearAllSkillBonuses`, which also erases unrelated
controller entries. Simply changing `RaidPlayer` would leave old event handlers
and temporary state behind.

## Arena elite skills

Native replacement players normally share `Profile.Skills`. Effects Manager's
temporary contribution must therefore be separated from Arena's absolute elite
boost and its restore snapshot:

- `ArenaSkills.Apply` snapshots the incoming buff minus the exact Effects
  contribution and applies the elite boost plus that contribution.
- `ArenaSkills.Restore` adds only the contribution still present in the foreign
  ledger. An expired or replaced-body bonus is not resurrected.
- The lookup accepts the captured physical lease after Arena's active flags
  have already cleared during teardown. It returns zero for ordinary or
  unrelated skill objects. Reflection failures throw into ArenaSkills' existing
  retry/restore path rather than pretending the foreign contribution was zero.

This preserves Arena's existing sticky-elite snapshots. It cannot reconstruct
an earlier sticky value that another mod changed before Arena took its
snapshot. Broken Limbs registers no `SkillBuffs`; other dependent mods whose
temporary Charisma bonuses cross elite still require their own audit. This is
not a compatibility claim for all users of the shared Effects Manager library.

## Verification

Run `pwsh -NoProfile -File tools/EffectsCompatChecks/Verify.ps1`.
The harness links all three production adapter files, production
`ArenaSkills.cs`, `ArenaRestoreCommit` and `Protocol`.
The final offline run passed **118 assertions**, before and after **21 compiled
negative controls**, each rejected by its intended behavioral assertion.
Evidence: `artifacts/compat-second-20261020/effects/checks/ee5cc4c411994253ae6e53f883c46289`.

Covered behavior includes initial binding, all seven callback paths, initial
kit observer ordering, living and dead replacements, duplicate events, retired
body callbacks, pending replacement holds, authoritative fallback, both teardown
orders, foreign-world disposal, ordinary raid controls, Scav/wrong profile/wrong
world/wrong bot game/stopping cases, separate foreign skill/effect pools,
unknown speed ownership, late library initialization and native-hook removal.
Actual ArenaSkills tests include existing/expired/post-boost effects, shared
profile managers and restoration after the Arena flags have cleared. Native
setter failures before/after writing and a later skill failing after an earlier
successful subtraction verify per-entry retry ownership.

Cecil checks the actual published libraries and native game types separately
from the executable fixture, including version mismatches, compiler delegate
caches, callback types, registration initialization, Broken Limbs'
`IsRaidController` guards and nonpersistent healed-fracture effect.

Four passes:

1. **Published identity and source:** compared both release DLLs with tagged
   source and the native type/callback contract.
2. **Lifecycle design:** traced Core's successful replacement before old-body
   disposal and Arena's delayed start/skill-restore order.
3. **Implementation and negative controls:** production-linked fixtures exercise
   physical ownership, scoped state cleanup and actual ArenaSkills interaction;
   compiled mutants must fail their intended behavioral assertions.
4. **Independent adversarial review:** covered global-vs-controller cleanup,
   compiler delegate cache shapes, native `DamageDelegate`, shared profiles and
   skill restoration ordering. Review evidence belongs to the integration run.

Offline checks do not certify Unity destroyed-object behavior, actual injury
icons, movement penalties, native re-fracture chances or the full PFT refill.
The original GPU checklist covered supplied-kit start, genuine death, a living
PFT refill, fracture treatment and a normal PMC control, plus profile restoration
and clean teardown. The follow-up below records which cases were subsequently
completed; fracture treatment remains outside that evidence.

## Native follow-up

The [2026-10-20 GPU2 run](GPU-COMPAT-SECOND-20261020.md) tested the actual
published library and Broken Limbs plugin. The library's player, lifecycle
health controller and movement context matched the current physical body after
the initial supplied kit, two Deathmatch respawns, two living PFT-Zombie refills
and one dead-player refill. The registered Broken Limbs effect remained present.
Ordinary Factory raids before and after Arena also bound the correct body.

This confirms native ownership and successful transitions, not the full injury
mechanic. No real fracture followed by splint/Surv12 treatment and re-fracture
was exercised; inspecting a registration or a blacked limb does not establish
that behavior. Foreign-controller bonus separation and throwing native skill
listener recovery retain their production-linked offline evidence.
