# Direct-start native acceptance, 2026-10-28

Two free GPU hosts were tested in parallel, with their SPT41 synchronization
paused separately. Full evidence and exact candidate boundaries are recorded in
the [host1 match report](DIRECT-START-NATIVE-HOST1-20261028.md) and
[host2 lifecycle report](DIRECT-START-NATIVE-HOST2-20261028.md). Raw profiles,
credentials, receipts and logs stay in the ignored
`artifacts/direct-start-native-20261028/` directory.

## Measured outcomes

| Scenario | Result |
|---|---|
| Direct start, full Deathmatch with rewards ON/OFF, native end and normal return | Passed; real local kills, no round-end hang |
| Direct start, full Domination with underlying consent ON/OFF | Passed; point-limit defeat and victory, no hang. Domination itself still has no reward policy |
| Positive reward and subsequent normal-server restart | Two actual DM wins paid 22,000 RUB and 2,200 XP in total; next startup left the entire profile unchanged. A later Syncthing overwrite removed these test rewards; see below |
| Warm server, ordinary EFT previously used, repeated A/B/A profiles | Passed. Includes more than seven minutes without EFT before the final reverse-rights entry |
| Already-running EFT and duplicate helper | Correctly refused without starting a second game or handover |
| EXIT and controlled in-raid EFT crash, then immediate direct restart | Passed in a separate writable installation; crash restart overlapped the old watchdog. In-raid crash/restart also passed again with both final binaries |
| Medium server / High helper, same Windows user and session | Failure reproduced, fixed and native entry/return passed |
| High server / Medium helper, same Windows user and session | Failure reproduced, helper fixed and native entry/return passed against the same retained server |

Host1 completed six Factory rounds in five directly started sessions: the four
requested combinations, an extra zero-kill timer case, and a positive DM retest.
The positive runs used documented test assistance, including godmode, teleport,
aim tracking and native trigger presses. Neither kills, result receipts nor native
round-end calls were fabricated. This establishes lifecycle and data behavior,
not unaided balance or comprehensive bot-routing acceptance.

## Defects found and corrected

**Late restoration after an old reward discard — `6f154cd`.** A previously closed
case later received valid restoration proof; its guards became terminal while
the old profile marker was also missing. Strict historical validation then
blocked new rewards with `invalid-startup-state`. Completed discards now accept
only the exactly proven transition, retaining permanent rejection of the old
claim. Missing marker acknowledgement uses the existing startup-only mechanism
and only a fully validated prior decision. No native profile is reset or copied
back. [Detailed boundary](REWARD-RECOVERY-ENGINE-20261027.md).

The actual captured failing data was replayed independently in disposable
fixtures. Only the new claim paid; all 59 checkpoint/run/snapshot files remained
unchanged. Interruptions after marker write, readback and completion each resumed
without paying the old claim or paying twice. Native host1 then confirmed two
eligible receipts at 11,000 RUB/1,100 XP each, and an unchanged full-profile
repeat startup. The old historical write timing is consistent with cross-host
synchronization, but its writer was not established.

**Mixed Windows elevation — `c6db9c3`.** Managed process queries required more
access than identity/liveness checks need. Server authentication and all twelve
helper liveness sites now use limited process queries; exact creation time,
program path, capability and listener checks remain. Unknown access is never
treated as proof that a writer has exited. Both opposite integrity combinations
were reproduced and retested natively. The earlier uninstrumented ProtocolError
is not retroactively claimed to have this exact cause.
[Implementation and Windows API references](REWARDS-RETURN.md).

## Profile evidence

The parent independently recomputed the complete restored-profile fingerprints
for both eligible DM runs: `d073fcf2...` and `d3ae2aa9...`, matching their captured
checkpoint and persisted proof without ignoring any field. OFF/prototype modes
have no reward checkpoint: full backup comparisons and the successful native
expected/disk/live readback are reported separately. Their pre-snapshot backup
can differ by one hideout timestamp; Domination OFF was exactly equal.

All ordinary profile differences were inspected before classification. They were
native timestamps, two initial default stack-count fields, the automatic discard
marker, earned XP/mail/ledger entries and subsequent native level recalculation.
The two other host1 profiles stayed byte-identical during its native matrix.
Inventory RUB stayed 973,000; the earned 22,000 RUB were in two uncollected system
messages before the later synchronization overwrite. No manual profile or
authority repair was used to make a test pass.

Host2 used a fresh profile created through the native SPT APIs in an independent
byte copy. Original-profile JSON was exactly equal after the original crash
recovery and normal return. Later first-login initialization fields are fully
listed in its report; they are not hidden behind a field filter.
The final-candidate crash retest restored the entire profile exactly after
restart; only two native timestamps changed after its normal return. All five
fixture sessions closed and completed, with no active pointer or owned process.

## Observed synchronization interference

After testing, resuming synchronization merged divergent copies of the same
profile used by both hosts. Host1's last good profile capture at 17:12:48 UTC
contained 2,810 XP and two payments. At 17:23:52 UTC Syncthing replaced it with
host2's older-progress copy: 610 XP and no payments. Events identify host2's
device ID, and all three current copies matched that replacement byte-for-byte.

The complete comparison found seven changed paths: XP, derived level, two native
timestamps, the reward dialogue and two payment entries. Inventory, quests,
skills and unrelated mod data were unchanged. The lost amounts are only the
2,200 XP and 22,000 RUB earned in these tests. At the acceptance checkpoint, the
good profile was exactly preserved in three conflict backups; it was not copied
over the current profile. The later deletion request is recorded below.

At the acceptance checkpoint, synchronization was deliberately paused on both
GPU hosts and the local installation, as the user authorized when another host
interferes. The user subsequently requested cleanup and resumption, recorded
below. This is an observed multi-writer
limitation, not evidence that an Arena return failed to restore its snapshot.

Twenty-two additional Syncthing conflict copies inside reward metadata caused
strict startup validation to reject the directory contents. In an isolated copy
of the exact affected data, archiving only those duplicates left all 608
canonical metadata files unchanged. The final server then automatically closed
the missing old claims without payment. A new synthetic reward paid once and a
repeat startup left the entire profile unchanged. This proves future reward
handling in that isolated control; it is not an additional native match.

The subsequent native startup also exposed a duplicate server assembly left by
Syncthing: the exact conflict DLL had the known interim server hash `CA15D614...`.
SPT rejected the duplicate assembly and did not load ArenaMode in that attempt;
that attempt is retained as a failed loading check, not a successful discard.
Only that identified DLL was archived outside each installation, retaining the
canonical final `2262F020...` file. A recursive loader-directory check found no
other conflict DLLs in any of the three installations.

The native host1 retry loaded ArenaMode at 17:52:05 UTC and automatically closed
the missing old claims without payment at 17:52:09 UTC. The complete profile
comparison found exactly one added recovery acknowledgement; XP stayed 610 and
payment count stayed zero. A second ordinary start loaded ArenaMode again at
17:53:09 UTC; afterward the entire profile was byte-identical to the first
successful startup. Both servers were stopped gracefully and no EFT was started.
The parent independently recomputed these complete comparisons.

## Automated checks and installed files

The final complete unit run passed **3,364 tests**, zero failures, with two explicit
environment skips: installed Factory database geometry (`SPT_DATABASE`) and the
installed APBS native pipeline (`ARENA_APBS_TEST_ROOT`). Final recovery checks
passed 56/56, process/return checks 110/110, helper process checks 128/128, and the
shipping .NET Framework executable passed 11 desktop/process checks. The helper
Release build had no warnings or errors. Builds/tests used no-deploy targets;
only the verified helper and server files were subsequently installed.
The separate shipping-runtime socket/mutex harness also passed all nine checks
and rejected all six deliberate source mutations. This ran in copied source
trees; no installed payload was replaced.

| Final component | SHA-256 |
|---|---|
| Session helper | `D2CF68D3E54957A5E827426CFFB1B15B96B927B1991C32C191409A6601A2A5B5` |
| Server | `2262F02036AA20F93FC87CF9222BE1C27A6007E4E7C0BF5C9B7ED59AE185363B` |

The source manifest binds these pre-commit builds to the final source contents;
their informational stamp alone does not identify the complete change. All 16
compared client plugins and both compared libraries matched disk during native
tests. Server evidence binds installed hash, start path/time and observed behavior;
it does not claim an independently sampled loaded-server MVID. Local installation
is disk-only; native evidence comes from the two GPU hosts.

## Cleanup and limits

Both hosts' original temporary settings and helper session state were restored;
host1's 220 backed-up configuration values were independently rechecked and
original reward consent is ON. The writable crash fixture is retained for
inspection. At that checkpoint, synchronization remained paused because of the
measured interference above. On each installation, the 22 conflict metadata
copies and the one known conflict DLL were retained in an archive outside the
synchronized tree. Archive
operations left all 608 canonical metadata files and three profiles unchanged;
the later normal startup's single acknowledgement is accounted for above. The
parent independently read back all three archives and final binary hashes.
The earned profile backups were retained in place. No original profile was
manually restored and the discarded test claims were not paid again.

## User-requested cleanup and resumption

The user subsequently explicitly requested deletion of conflict copies and
resumption of synchronization, followed by merging and deleting the branch and
pushing the result. With all game/server/helper processes stopped, exactly 993
recognized conflict files were deleted from the active installations and the
three named temporary conflict archives: 354 locally, 285 on host1 and 354 on
host2. Individual path/hash manifests were recorded first; canonical profiles
and installed binaries remained unchanged during deletion. The rest of each
active installation contained no additional conflict files. Captured historical
test evidence under `artifacts/` was preserved.

SPT41 synchronization was resumed locally at 18:10:41 UTC, on host1 at 18:11:36
UTC and on host2 at 18:11:52 UTC. Follow-up evidence is in
`artifacts/sync-merge-20261028/`. A subsequent direct comparison found all three
canonical profile files byte-identical across the installations and no new
active conflict files. This change supersedes the earlier paused
disposition; it does not restore the lost test rewards or establish safe
concurrent profile writes.

Multiple installations under one Windows user still share the helper session
directory. At the time of this acceptance, switching to the isolated fixture
required archiving the positively closed original session and restoring it
afterward. That limit was a defect and is fixed in ArenaMode 0.4.1: a closed
session of another installation no longer blocks, and a session that is not
closed is refused with a message naming its installation (see
[offline transfer](ARENA-OFFLINE-SESSION-20261025.md)). No production path was
weakened to make that fixture start. Remote servers, multiplayer, real power loss, arbitrary
third-party writers and all ported maps are outside this acceptance.
