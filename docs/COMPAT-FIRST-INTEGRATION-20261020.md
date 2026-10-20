# First candidate compatibility batch — 2026-10-20

This batch implements the first recommendations from the
[twelve-candidate review](COMPATIBILITY-CANDIDATES-20261020.md), plus the reported
[Recoil Rework Legacy conflict](RECOIL-REWORK-REVIEW-20261020.md).
It does not implement all remaining candidates from that review.

## Changes

| Integration | Result |
|---|---|
| ISB Special Forces / Aishi | Loaded client or server components reject Arena with a menu-visible reason. The server rejects before snapshot preparation. Ordinary raids and recovery/end routes remain available. |
| Skills Extended 3.1.4+ | Suspend Arena Signals reward receipts and the three initial medical/weapon mastery XP handlers. Keep other features, existing skills, unrelated subscribers and ordinary progression. |
| Recoil Rework Legacy 2.0.0+ | Transfer recoil/camera singleton ownership before creating new components, preserve old components until authoritative replacement/recovery, and bind recoil values to the physical equipped weapon. |

Skills Extended and Recoil accept matching newer contracts with a warning.
Missing or changed protection contracts reject Arena only. No foreign
configuration, journal or source file is modified. No foreign DLL is bundled.
The separate Recoil server-package JSON/List mismatch remains an upstream
concern; this client adapter does not repair it.

## Verification

| Check | Result |
|---|---|
| Full unit suite | 1,236 passed, one existing installed-APBS integration skip, zero failed |
| Native menu source harness | 759 assertions passed, including server-only admission and ordinary-raid controls |
| Optional admission harness | 113 assertions passed; 19 compiled behavioral mutations rejected |
| Skills Extended production-linked harness | 93 assertions passed; 16 compiled behavioral mutations rejected; published 3.1.4 DLL and native event metadata checked |
| Existing skill-isolation harness | 27 assertions passed; three behavioral negative controls rejected |
| Recoil production-linked harness | 114 assertions passed; four compiled behavioral mutations rejected; published 2.0.0 DLL metadata and critical IL checked |
| Production builds | Client, BigBrain companion and server build successfully with deployment explicitly disabled |
| Independent review | No unresolved material findings in ISB admission, both adapters or startup/admission integration |

The production client build retains four pre-existing nullable-context warnings;
the server retains its existing nullable/MudBlazor warnings. The Recoil fixture
has an unused-name warning. These are not test failures.

Evidence is ignored under `artifacts/compat-first-20261020/`. The two adapter
harnesses link the actual production files and check published metadata separately.
They do not reproduce Mono detours, Unity destruction or real gameplay.

## Deployment and remaining acceptance

**No deployment, GPU test, push, release or version bump was performed.**
Installed client, companion, server and Redeploy.Core DLL hashes were compared
with their pre-build values and remain unchanged. Ordinary raids are preserved
by the scoped implementation and offline controls; that is not a new native
compatibility certification.

Before release, test the following on a GPU host:

1. ISB client-only and server-only refusal, then an ordinary raid; removing ISB
   and restarting both processes must restore Arena entry.
2. Skills Extended Signals rewards on Labs/Customs, with external journal and
   profile checks; initial kit replacement, repeated respawns and PFT wave
   refill; subsequent ordinary medical/mastery XP and Signals receipts.
3. Existing Skills Extended First Aid/Strength perks with Arena movement and
   surgery. This batch deliberately does not rebalance these features.
4. Recoil initial kit replacement, death respawns, live swaps, failed replacement
   recovery, PFT refill, ADS/left shoulder, Akimbo and weapon changes; then an
   ordinary raid in the same process. Verify live and spectator/replay cameras.

Detailed boundaries:
[ISB](ISB-ADMISSION-20261020.md),
[Skills Extended](SKILLS-EXTENDED-COMPAT-20261020.md),
[Recoil Legacy](RECOIL-REWORK-COMPAT-20261020.md).
