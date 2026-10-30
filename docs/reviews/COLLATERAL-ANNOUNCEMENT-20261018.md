# Same-bullet COLLATERAL announcement

The local player receives **COLLATERAL** after one physical bullet directly kills
two distinct eligible enemies. It is text-only for now: no new recording,
fallback sound, missing-clip diagnostic or score bonus. The normal kill, streak,
assist and mode scoring rules remain unchanged. The second death chooses this
label ahead of DOUBLE KILL and other ordinary kill awards. The first death may
still announce First Blood; a third victim does not repeat Collateral.

Both aimed and hip-fired single-projectile ammunition qualify. This does not
loosen AIRBORNE or NO SCOPE: those still require their existing firing-time
conditions, including no ADS. Shotgun pellets, ricochets, independent fragments,
explosions, bleeding and separate rapid rounds never combine into this award.
A single-projectile shotgun slug can qualify through real penetration.

## Native identity and ordering

Verified against the installed EFT assembly and the local decompilation:

- `Player.FirearmController.InitiateShot` dispatches the single-bullet path only
  when `Ammo.ProjectileCount == 1`; the template default is also one. Cartridge
  IDs and firearm fire indices alone are not unique physical bullet identities.
- `Shot.Create` obtains a pooled object. Every observed creation invalidates the
  old entry and gets a new process-local token. Unknown/bot creates erase any
  previous facts as well. A new object with the same FireIndex cannot reuse a
  lineage.
- `Shot.CreateDeviatedFragment` is native penetration continuation. It creates
  a child with the same FragmentIndex, ammunition object, weapon, FireIndex and
  physical shooter, and passes the parent Shot. A dedicated prefix/finalizer
  observes only this exact call; it does not change ballistics.
- `CreateBulletFragments` and `CreateRicochetedFragment` increment FragmentIndex
  and are not continuation scopes. A generic parent reference is insufficient.
- `BallisticsCalculator.UpdateShots` calls `shot.Tick` before `_shotDelegate`.
  The penetrating child therefore already exists when the parent's first lethal
  callback occurs. Parent and its sole admitted continuation share a tiny
  managed ledger; copying a kill count into the child would lose that first kill.
- The existing synchronous ClientGameWorld impact, ApplyShot and ApplyDamageInfo
  contexts must agree on the exact generation, ammunition, shooter and direct
  lethal bullet. Nested/foreign/reused contexts fail closed. World, local game,
  body and award-round epoch must still match.

Capture creates an `ArenaCollateralProof`, containing only scalar projectile
facts and the managed lineage (first victim ID and one awarded flag). It retains
no Player, Shot, Weapon or world reference and adds no accumulating dictionary.
`ArenaCombatAwards` settles that proof only after `ArenaMatch` has admitted the
mode event and Team DM has classified the physical teams. An unscored, friendly
or rejected first death cannot seed a later award. A duplicate victim cannot be
the second victim. Round/raid reset invalidates even a proof already captured.

Continuation inherits a lineage only once from each parent generation. The
child keeps valid scalar lineage after the parent object returns to its pool;
the recycled parent itself receives a fresh lineage. Finalizers preserve native
exceptions and cannot restore invalidated scopes after a reset.

## Presentation and compatibility

COLLATERAL takes the existing one-label-per-kill presentation slot. It is selected
before the normal special/multi/posture/spree labels, so an ordinary DOUBLE KILL
cannot hide it. AnnouncerService explicitly ignores this known text-only key
before audio admission: it neither replaces a queued phrase nor probes for a
nonexistent sound. Existing clips and their volume/enable settings are unchanged.

Ordinary raids, scoring, damage, ammunition consumption, weapons, player profiles
and native projectile behavior are untouched. No additional log stream is added;
the existing once-bounded shot-observation warning handles unexpected failures.

## Verification

The production hook/history adapter and full match/audio harnesses cover the
native-order double kill, aimed/hip fire, duplicates, third victim, independent
rounds with equal or different FireIndex, pellets, fragments, ricochets, changed
ammo, duplicate continuation branches, nonbullet deaths, shooter/self identity,
pool recycling, stale world/game/body/round, disabled/ended matches and exception
cleanup. Full match checks specifically reject a first event before mode
admission and a first kill rejected by Team DM classification, while still
allowing two subsequent legitimate deaths. Score/kill/streak totals match the
ordinary two-kill control. Audio checks prove silence without missing-asset noise
and preserve an unrelated pending recording.

Final offline evidence on 2026-10-18:

| Scope | Result | Evidence under ignored `artifacts/` |
|---|---|---|
| Shot history | 115 assertions; 6 compiled faults rejected; pending proof does not retain a collected Shot | `shot-award-history-checks/efd09dec775545a4820730b5b505dbf2/manifest.json` |
| Native adapter | 231 executable assertions; 71 metadata/source contracts; 12 compiled faults rejected | `shot-award-integration/dc9ceef779c34858b1de18fd94539051/manifest.json` |
| Full match/awards | 214 assertions; 1 historical and 10 compiled fault controls rejected | `announcer-combat-checks/e61ef75a1a2240f2952f2b84aba3d0bf/source-binding.json` |
| Focused audio/award regression | 337 audio and 214 combat assertions; 8 compiled faults rejected | `shot-award-audio/e98c03ee948747eba2a04dd1404cc25e/source-binding.json` |

The adapter also verified installed Arena client/server/Core binaries unchanged.
The historical full audio verifier separately exposed a checkout-only CRLF/LF
manifest hash mismatch, unrelated to runtime audio or Collateral. The dedicated
harness correction normalizes only the text manifest's line endings; source audio
hashes stay byte-exact. Temporary LF and CRLF positive controls plus 8 metadata
and 11 inventory negative controls passed. No audio assets were edited.

Reproduce:

```powershell
pwsh -NoProfile -File tools/ShotAwardHistoryChecks/Verify.ps1
pwsh -NoProfile -File tools/ShotAwardIntegrationChecks/Verify.ps1
pwsh -NoProfile -File tools/AnnouncerChecks/Verify-ShotAwards.ps1
```

These checks execute production logic with explicit native boundary doubles and
separately validate installed metadata/decompiled ordering. They are not a
Unity/Harmony penetration playtest; deployment and native observations belong in
the combined dated GPU report. No native acceptance is claimed by this document.
