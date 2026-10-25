# Native automatic reward return — 2026-10-25

Native automatic return passed on GPU2 with SPT 4.1.6 and EFT 0.16.9.5. The real
return button restarted the normal server, paid two genuine signed match results
at startup, and opened normal EFT with two new RUB packages and 2,200 additional
PMC XP. A subsequent private visit without a match returned without another server
restart or payout. Earlier failed attempts remain documented below.

## Accepted final run

The acceptance boundary began after deployment with four old payments, four reward
mails, 4,861 PMC XP, level 3 and 203,494 inventory RUB. One actual earlier win was
still queued. Rewards had temporarily been switched OFF through the native card
for the intervening technical deployment; the final private entry switched them
back ON through that card. No queued result was fabricated, edited or paid during
that deployment.

The accepted payload SHA-256 identities are:

| Payload | SHA-256 |
|---|---|
| Server | `D77A2A3E49C02FD378A78477DFC392D2FBB8B42B475CE61D6DB555F601AC443B` |
| Client | `7CE6EE382E400B352D376B6F683D0DD3F6222913743C867037DFC3DBD55E9521` |
| Session helper | `8DDDD841ABC4C2E72C3242B1FB133FCBA8B54390E463F5C865DFC061247DCEA6` |

All sixteen checked client assemblies and both checked libraries matched the
development files, host disk and loaded process before entry, in private Arena,
and after both returns. The private menu reported automatic return available.

The final build ran a one-round Factory Deathmatch with a one-kill target. Those
settings were memory-only. Three real opponents loaded. The test used temporary
godmode, ammunition preservation, aiming and valid nearby teleports. The player
died once and respawned before the successful shot; hooks were re-enabled for that
body. Ten actual shots were observed: one SKS shot and nine SR-25 shots. The final
SR-25 headshot produced the native **VICTORY** scoreboard: one kill, one death,
one round won, zero lost. Normal result screens completed the exit.

Independent HMAC verification confirmed a second distinct `Completed` result,
one local combat kill, the target reached, and exact `Persisted` restoration.
The private menu visibly displayed **22,000 ₽ + 2,200 XP pending — credited when
you return to the normal session**, combining the earlier genuine result with
the new one. Both result fingerprints match the two new permanent payment keys.

The only return action was the native two-click **RETURN TO NORMAL SESSION**.
The helper log independently records private shutdown, transfer of the new
receipt, automatic normal-server restart and readiness before launching EFT.
At 11:55:22 UTC normal PID 6824 was replaced by PID 16172; the new server accepted
connections 7.1 seconds later. Its startup log reports the aggregate **22,000 RUB
delivered by mail; 2,200 XP credited**. There was no manual restart, queued-payment
dialog or retry in this accepted return.

Normal EFT reached its main menu with six permanent payment records, six reward
mails and 7,061 PMC XP. The live client also reported 7,061 XP. The native messenger
visibly showed both new system packages at 12:55 local time, each describing
11,000 RUB and 1,100 XP already credited. Neither package was collected; inventory
RUB remained 203,494.

The second private entry played no match and had an empty receipt outbox. Its
native return kept PID 16172 and the same process start time, six payments, six
mails and 7,061 XP. EFT again reached the normal main menu. Four old payment
records and all old message contents remained exact; the two new mails and their
attachments also remained exact.

Complete comparisons cover all six canonical profile copies at both post-return
boundaries. All four unrelated copies remained byte-identical. In the selected
profile the only changes were the checked XP/payment/mail additions, native health
and Hideout timestamps, and the unread counter cleared by opening the observed
SYSTEM dialogue. Native health update and Hideout update code substantiate those
two timestamp fields. Inventory, skills, quests, Scav and every other field stayed
equal. All 126 saved configuration hashes remained exact.

A temporary user-policy deny exposed the private START control after the default
policy began admitting the installed animation mod. Its exact bytes were backed
up, then removed from both installations after the second private entry. The final
normal launch used the original absence of that override. No mod files were
removed. Rewards are ON; temporary gameplay settings expired with private EFT;
godmode, ammunition preservation, tracking and held fire are off; the control lease
was released. GPU2 remains at the normal main menu.

The read-only `artifacts/reward-return/gpu/verify-native.mjs` passed with
`RETURN_NATIVE_ACCEPTANCE_PASSED`. It derives process/order assertions from
captured helper logs and process snapshots, validates loaded identities, checks
the actual startup log and live XP, matches paid keys to both signed match proofs,
and performs complete profile/config comparisons against captured and live files.

## Initial attempt and preserved state

The normal profile started with four payment records, 4,861 PMC XP, level 3 and
203,494 RUB in inventory. Its recorded lifetime rewards were 44,000 RUB and
1,100 XP. The native two-click **START ARENA SESSION** control successfully
opened the private session. **PROFILE REWARDS: ON** was visible, but the live
`ArenaRewardMenuState.Status.AutomaticReturn` property was false and the normal
runtime had issued no return capability file. That is a failed admission result,
separate from the subsequent game crash.

A bounded Factory Deathmatch was requested with one round and a one-kill target.
These settings were applied to memory only. EFT crashed during map loading,
before the test's godmode, ammunition, aiming or firing steps ran. The helper
watchdog stopped only the private server; the original normal server remained
running. No match result, reward receipt or automatic payment was manufactured.

Windows Error Reporting identifies an `APPCRASH` in `d3d11.dll` version
10.0.26100.9711 with exception `c0000005` at offset `0x171feb`. Unity also records
its native crash handler and a UnityPlayer stack frame. The renderer was Direct3D
11 on an RTX 4070 Ti. The available logs do not establish the underlying cause;
they contain no preceding out-of-memory, audio or graphics-device failure
message. This crash is not evidence of a normal quit or of a reward return.

After the failed attempt, payment count, XP, inventory, skills, quests and Scav
data were preserved. A complete normal-profile comparison found only the native
Hideout update timestamp changed. Every saved Arena configuration value matched
the pre-test copy.

The earlier backup comparison also found one unrelated profile changed on both
synchronized installations: health and Hideout timestamps advanced, and three
skills' session fatigue counters became zero while permanent skill progress
remained equal. Those changes precede the corrected-deployment acceptance
boundary and are preserved. Native code supports health timestamp advancement
on game start and skill fatigue reset on raid start; it does not by itself prove
which external activity caused these particular unrelated-profile changes.

After the entry-hook correction, a second native entry successfully created and
copied the return capability. A direct normal-server status read reported
automatic return available, while the private menu still reported false. No
match was started with that inconsistent state. The native return button brought
EFT back to the normal menu, kept the normal server process, and preserved the
four payments, XP and inventory balance. This empty return is recorded as a
diagnostic run; it does not substitute for the required post-payment replay test.

## Real match and deferred return

After the hosted-service admission correction, the private menu's live automatic
return property was true. Factory loaded with three active enemies. The test
temporarily used godmode, ammunition preservation, a nearby valid teleport and
head tracking; damage came from thirteen actual Mk 17 shots. The native scoreboard
showed one kill, one headshot, one round won and zero lost. The round returned
through the native result screens to the private main menu.

Independent receipt verification found one `Completed` Deathmatch result, one
local combat kill, the one-kill target reached, a valid HMAC and a persisted
restoration with the exact expected profile revision. Before return the normal
profile still had the original four payments and 4,861 XP. The private map detail
continued showing the previous payout, rather than the newly pending amount;
this presentation issue is captured separately from receipt validity.

The native two-click **RETURN TO NORMAL SESSION** control stopped the private
game and server and transferred exactly one signed receipt. The normal server
then reported `preflight-save/RewardDataException`; the helper displayed
**ArenaMode - rewards queued**. The original normal server stayed alive, and its
profile still had four payments, 4,861 XP and 203,494 inventory RUB. The normal
inbox independently verifies the newly transferred receipt. No automatic payout
or successful normal-server restart is claimed for this attempt.

## Evidence handling

Ignored evidence is retained under `artifacts/reward-return/gpu/`, including
structured loaded-state and live-property reads, menu images, the failed raid
scenario report, original and post-attempt profile snapshots, configuration and
profile hashes, Unity's log and Windows' crash report. Signing keys, return
capabilities, profile identifiers and raw profiles are excluded from this report.

The accepted run proves the supported local happy path and duplicate-safe empty
return. Failure and timeout branches remain separately covered by offline tests;
this run does not claim that every recovery dialog was exercised natively.
