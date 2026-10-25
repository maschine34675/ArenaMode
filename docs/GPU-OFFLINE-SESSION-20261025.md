# Private Arena with the normal server stopped: GPU-2 verification

Date: 2026-10-25. Contract: [offline ownership and recovery](ARENA-OFFLINE-SESSION-20261025.md).
This checks the schema 3 entry/return flow on local SPT 4.1.6. It supersedes the
earlier return test's assumption that normal SPT remains running during Arena.

## Builds and preservation

- `d1521d3`: initial native entry, offline reward control, competing startup,
  real combat and paid return.
- `0ab7c44`: final Release build, including recovery when an early new entry fails
  over a previously completed private tree. Build: zero errors, 41 existing warnings.
- Client, optional BigBrain companion, helper, gate, server and default policy are
  compared between the development installation and GPU-2 before starting fresh
  processes. The native artifacts record the actual process/loaded identities.

Both hosts were explicitly released for testing. GPU-1 was already stopped. Before
changing GPU-2, all six canonical profiles across the synchronized development/GPU-2
installations and their configuration/reward trees were archived. OS-held lock
handles were excluded; the complete archive contains 663 files. The earlier partial
archive which stopped on a locked handle is not used as preservation evidence.

No forced ordinary-server termination, profile rollback, save-hash reset, fabricated
receipt or manual profile/progression edit was used. Local native evidence is under
`artifacts/offline-arena-session/native/`; profiles, signing material and raw logs
remain ignored and are not published with this report.

## First switch and reward payment

The actual character-selection action offered START ARENA SESSION because installed
StanceSync was unlisted. The confirmed switch closed EFT, saved and stopped the
ordinary server, then copied the profile and started private SPT/EFT. The helper
recorded ordinary save/stop at 16:35:54 UTC and private-tree completion at 16:35:57.
The old normal PID was gone before private startup. Private MainMenu used port 6970;
normal port 6969 was absent.

PROFILE REWARDS was changed ON → OFF → ON through the actual private map menu.
Signed revisions advanced 6 → 7 → 8 while the normal server remained stopped.

One deliberately competing normal startup refused in Arena's pre-profile-load
startup callback because the private process owned the original lifetime lock.
Normal port 6969 never opened. SPT's native fatal console was closed with Enter;
the private game/server were unaffected. This expected refusal is evidence of the
ownership guard, not an unexplained gameplay error.

A two-participant Factory Deathmatch then produced one real ADAR headshot kill
(three fired shots, no round win). Godmode, NavMesh positioning and aim tracking were
test aids; the hit, death, result and receipt used the real gameplay path. Disconnect
restored the entry profile and the server confirmed saved restoration. The actual
RETURN TO NORMAL SESSION action ended private EFT/SPT and automatically started
ordinary SPT/EFT back into the main menu.

Normal startup completed the immutable recovery journal and removed its active
pointer. The payout independently measured from pre-entry/return profiles was:

| Observation | Before | After |
|---|---:|---:|
| PMC XP | 7,661 | 7,761 |
| Permanent payment entries | 9 | 10 |
| New payment | — | 1,000 RUB + 100 XP |

All nine previous payment records remained unchanged. The native Messenger showed
the reward mail. A previous mail happened to contain the same amounts, so text
occurrence counts are not used to prove uniqueness. Both unrelated canonical GPU-2
profiles remained byte-identical. The selected profile kept all 303 inventory items;
its complete PMC inventory, skills and quests matched the pre-entry profile.
The ordinary entry/logout save is distinguished
from the later startup payment; the private profile was never copied back.

## Final empty/OFF roundtrip

The final deployment was rechecked after synchronization (32.4 seconds): all six
payload/policy SHA-256 values matched between the development installation and GPU-2.
The first capture during synchronization still showed old GPU binaries and is not
accepted as final deployment evidence. Fresh processes use the completed deployment.
Normal MainMenu reported the loaded Arena client MVID
`683f6466-167a-41b1-9a6a-fd92b38e4c20`, identical to the installed binary; the
optional companion also matched. Final SHA-256 prefixes are:

| Payload | SHA-256 prefix |
|---|---|
| Client | `3066D2CC78E620D8` |
| Session helper | `26FCFE51F85E95B4` |
| Gate | `00D3BE8D86AA5706` |
| Server | `2A1D9B017ACFF7D6` |

A second actual private entry and return ran on this final build without a match.
The private menu switched rewards OFF. After return, normal MainMenu was reached
on port 6969 and normal consent persisted `enabled=false`, revision 9. XP remained
7,761 and the payment ledger remained at ten entries, byte-equivalent in its parsed
representation to the first return's ledger. The PMC inventory also remained
unchanged. Both session journals had completed, and no active pointer remained.
Evidence: `native/empty-off-return` (141 files with verified copy hashes).

The third private entry visibly inherited OFF. Its actual menu restored ON, and
another empty return persisted ON revision 10 in normal SPT. Normal Arena selection
remained correctly blocked by the unlisted mod, so restoring this setting used
the genuine private menu rather than a hidden API or policy bypass. XP stayed at
7,761, the ten payment records and PMC inventory remained unchanged, both empty
closures listed zero receipts, and all three journals had completed. Final normal
MainMenu was healthy on port 6969, with all 16 listed plugins and two libraries
matching the installed files. Evidence: `native/empty-on-private`,
`native/empty-on-restore` and `native/empty-on-return`.

EFT was then closed normally and the ordinary server stopped with Ctrl-C. Original
settings were restored. The ineffective temporary test-policy file was archived
and removed from both installations; it never changed the policy. Genuine earned
rewards and recovery journals were retained. Existing, previously refused Tarkova
Cola cache remnants were left untouched.

## Offline coverage and limits

The final unit run passed **3,194 tests**, with zero failures and the one known APBS
installed-environment skip. This includes 38 server ownership/recovery cases and
34 helper cases. Control transfer passed 194 assertions, the client session harness
748, and result transfer 117. Two result-transfer mutants and three focused client
entry/downgrade mutants were rejected. The broad client mutation run was not
completed; these figures do not claim it passed. Offline verification confirmed
installed payloads unchanged before the separately authorized deployment.

Native acceptance is bounded to the recorded local switches, one real kill and
the ownership refusal. Crash/power-loss, lost replies, stale identities and partial
recovery combinations are synthetic coverage, not injected native failures.
Cancel or helper loss after a held native save still preserves the quiesced server
for investigation; a new helper cannot take over that identity. Process locks
coordinate one machine, not independently running synchronized installations.
