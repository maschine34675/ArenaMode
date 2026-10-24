# Reward runtime native acceptance — 2026-10-24

Result: the consent, normal/private result capture, receipt return, native mail
claim and repeated-startup gates passed. Production changes were built and
deployed separately by the parent task; this report records actual native tests.

## Scope and isolation

- Host: SPT-GPU-2; SPT 4.1.6; EFT 0.16.9.5-40743; Unity 2022.3.43f1.
- Only the host's designated test profile was used. All 25 profile files on each
  installation had been archived before testing: three canonical profiles and
  22 existing synchronization conflict copies per host. No whole
  profile was restored or copied back from a private session.
- UiBridge 0.9.6+0978cfe supplied observable UI, real-shot and process evidence.
  Real test hooks are stated separately from reward capture; no fabricated
  reward request or server approval was submitted.
- Raw snapshots and logs stay in ignored `artifacts/rewards-runtime/gpu/`;
  this document contains no signing keys or complete profiles.

## Enrollment and player consent

The initial ordinary character-selection screen showed **PROFILE REWARDS: OFF**
and the then-provisional 500 RUB/kill, 5,000 RUB/round win and 100,000 RUB/match
rates. During testing the user selected 1,000 / 10,000 / 200,000 instead; the
subsequent earned-result tests use that final policy. At 1920×1080 the
card was readable in the upper-left corner and did not cover native controls.
A real button click changed it to ON and displayed the required normal-server
restart. The only reward file was consent metadata. The profile still had no
reward extension, ledger or reward mail. Two unrelated native timestamps changed
between disk snapshots (`Health.UpdateTime` and `Hideout.sptUpdateLastRunTimestamp`).

Two native mismatches were found and corrected before positive reward acceptance:

- The installed native profile uses `inraid: {}`. The original startup check
  required the older explicit `location: "none", character: "none"` shape and
  withheld enrollment. After the bounded native-shape correction, startup added
  exactly the owned reward extension; the independent JSON comparison found no
  other semantic change.
- The real native raid identity contains a space before its Unix timestamp.
  The original result prefix validator excluded that space. A real one-kill DM
  victory was therefore withheld. Its profile was restored and its durable run
  was closed as `unrewarded-restored`. No payment was retroactively manufactured.
  The corrected format is checked at begin as well as final validation.

## Verification evidence

### Final builds

The positive matches used these exact SHA-256 values. UiBridge also verified
that the loaded client matched both the host's file and the development build.

| Artifact | SHA-256 |
| --- | --- |
| ArenaMode.Client.dll | `80CBA7D4DD0709C0BC70D6943727CB46A533E702B7108A3F1BAD9698337B78E3` |
| ArenaMode.Server.dll | `FC7FE453A2F8D6EBDC63DEFF1B840C79E304C6A42FCEF033A4998DECC56A7FE4` |
| ArenaMode.Session.exe | `080DFBD0D669BE9C5F234BC2784862D7B9FB726D4F2AFD3104EF0632C217E6C2` |

### Native combat and private return

Both final tests used Factory day, Deathmatch, four participants, all opponents
against the player, one scoring round and one kill to win. Only those match
settings were changed temporarily with `persist=false`; none of those test values
was persisted. Godmode, infinite ammunition/condition, safe teleport near a live
opponent and head tracking made the test repeatable. Actual native weapon shots
caused the deaths; no kill, result or receipt was injected.

| Run | Native result | Verified capture |
| --- | --- | --- |
| Normal server, 21:38 CEST | Four MP-153 shots, one headshot kill, one round won | Completed, LocalTarget, 83,687 ms, four actors, one death, one local kill |
| Private server, 21:44 CEST | One ADAR shot, one headshot kill, one round won | Completed, LocalTarget, 20,111 ms, four actors, one death, one local kill |

The independent receipt reader verified both HMAC signatures, the server-owned
1,000/10,000/200,000 policy and exact equality between entry, expected and
persisted restoration revisions. Both snapshots were fully restored; comparisons
after the subsequent menu return found only the native hideout update timestamp.
The normal UI showed 11,000 RUB pending after its match and 22,000 after return
from the private match. Before startup settlement there were no reward payments
or reward messages, and inventory currency remained 464,221 RUB.

The private transition used the actual **START ARENA SESSION** confirmation,
then **RETURN TO NORMAL SESSION**, through the installed helper. Private startup
took 53.0 seconds and return 42.8 seconds. The private backend used port 6970 and
the final server hash above; normal play used port 6969. The helper stopped the
private server before moving its one receipt. The private outbox became empty,
the normal inbox held both original signed receipts, and the already-running
normal server did not pay immediately.

There is also a concrete negative copy-back witness: the private version-filtered
session omitted PitFireTeam, and native ProfileFixer removed its trader from the
private PMC and Scav. Both corresponding trader entries remained in the normal
profile after return. The entire normal-profile comparison across the switch
found only `characters/pmc/Hideout/sptUpdateLastRunTimestamp` changed.

### Startup delivery, native claim and duplicate protection

The normal server restarted at 21:46:58 CEST. At 21:47:00 it logged delivery of
**22,000 RUB**. The independent snapshot taken before the subsequent game login
contained exactly two ledger entries and two System messages with two 11,000-RUB
attachments. The full semantic profile comparison showed precisely three
additions: the new System dialogue and `payments[0]` / `payments[1]`. No existing
profile field changed during settlement, and inventory currency was still
464,221 RUB because the money had not yet been claimed.

Both messages displayed correctly in EFT's native Messenger. **RECEIVE ALL**
opened the native transfer page with the two cash stacks; its own **RECEIVE ALL**
and **ACCEPT** transferred them normally. The UI then marked both messages
`received`. After SPT's ordinary save at 21:49:04, the profile contained
**486,221 RUB** in inventory, zero attached reward currency, two old messages and
the same two ledger entries. Native changes included the cash stack merge,
message read/collected flags, the money-transfer reset time and background
Health/Hideout timestamps. No manual profile editing was used to accelerate
the save or claim.

A second normal server startup at 21:49:58 was compared before the next login:
the **entire profile was semantically identical** to the saved post-claim state
(zero changed paths). It still held 486,221 RUB, two payments totalling 22,000 RUB,
two messages and zero attachments. The server emitted no second delivery event.

### Cleanup and remaining boundaries

The temporary consent was switched OFF through the native character-selection
control after acceptance. The earned money and durable enrollment/payment
history remain intact. The game is back at the normal MainMenu on backend 6969;
the private server was stopped and its outbox is empty. Test hooks ended on raid
exit. Temporary match settings reverted on restart to Tactical, zero DM kill and
round limits, time limit zero and FFA enabled. A comparison of every configuration
setting against the pre-test copy found zero differences. BepInEx rewrote only
11 numeric-range comment lines using locale commas instead of decimal points;
that harmless comment formatting was not rolled back.

All 24 other profile files on **each** installation remain byte-identical to the
backups (48 checks total). The normal PMC and Scav still contain the PFT trader.
Final profile changes after the duplicate-startup snapshot were only ordinary
Health/Hideout timestamps.

The final warning review found the expected unreviewed-PFT-version notice, native
Fence plate-slot warnings, the PFT trader temporary-update warning, missing Fika
references in optional foreign assemblies, existing Harmony declaration warnings,
native friend-profile fallback warnings and `JetMaterial == null` in the menu.
No reward error or new payout was present. These unrelated/native observations
were not fixed here, and this test does not establish their root causes.

This is native acceptance of ordinary and private **Deathmatch** capture with
real combat, controlled test hooks and one short round each. It is not native
acceptance of every scoring mode, PFT squad lifecycle, long matches, crashes or
adversarial clients. Those cases retain their separate offline/negative test
coverage and require targeted native tests when their contracts change.

The read-only tools in `tools/RewardRuntimeGpuChecks/` snapshot a selected test
profile into ignored evidence, summarize reward ledger/mail totals and report
semantic JSON differences without exposing profile values. They do not write to
an installed profile. Process and visible UI evidence comes from `gpu-run` JSON
exports and the game's captured frames.

Key ignored evidence groups:

- `02-consent-off.*`, `03-consent-on-restart.json`, `07-map-header.png`, and the
  enrollment before/after profile snapshots record the initial UI and guard.
- `23-normal-receipts.json`, `28-private-receipts.json`,
  `30-returned-receipts.json` independently verify signed results;
  `31-return-helper.log` records actual transfer.
- `before-payout-startup.*` / `after-payout-startup.*` isolate settlement;
  `35-native-reward-mail.*`, `36-native-transfer.png`, `37-native-claimed.png`
  and `after-claim-saved.*` prove native claim.
- `before-repeat-startup.*` / `after-repeat-startup.*` prove the exact no-op;
  `38-other-profiles-unchanged.json`, `41-final-loaded.json`,
  `42-final-match-settings.json`, `43-final-warnings.json` and
  `44-final-cleanup-checks.json` record final preservation and cleanup.

## Final verdict

G1–G4 passed for the scope above. No further production change was needed after
the native clean-state and raid-identity corrections. No private profile was
copied back, no payout was manually manufactured, and no profile was rolled back.
