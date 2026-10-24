# Optional profile rewards: runtime integration

This extends the settlement core, guarded startup consumer and result validator
described in REWARDS-CORE.md, REWARDS-SERVER.md and REWARDS-RESULTS.md. It is a
development change after release 0.2.0, not part of that existing download.

## Player flow

- **PROFILE REWARDS** in Arena's map menu is off by
  default and belongs to the selected normal profile. It is separate from
  gameplay mutators. The full card above the map preview shows rates and current
  state directly. The old character-screen card and Mutators-header button are
  removed. START ARENA SESSION and compatibility notices contain no reward
  control. The same map-menu card can enable or disable rewards from the separate
  Arena session, through a limited capability issued by the normal server.
  Background status checks keep the confirmed label and button state;
  only initial loading or an explicit consent change shows a waiting state.
- The first opt-in records consent only. Restart the normal SPT server once to
  register the profile before it is loaded. The menu shows the required restart.
  When opting in from a private session for the first time, return to the normal
  game, restart its server, then enter Arena again. Existing enrolled profiles
  can switch immediately; the normal server must remain running.
- The server freezes 1,000 RUB per eligible local kill, 10,000 RUB per round win
  and a 200,000 RUB match cap. These are the user's selected starting values.
  The client cannot select an amount or payout recipient.
- Enemy final kills count; assists, friendly kills, self-kills and environmental
  deaths do not. DM/TDM/Tactical wins use the actual configured scoring rule.
  Gun Game and Zombies currently award kills only, not ladder/wave victories.
- A normal exit can retain kills from the last unfinished combat round without
  inventing a win. Aborted/no-contest reports receive no payment for the run.
- Completed results wait for the next **normal SPT server start**. Returning from
  a private Arena session while that server keeps running does not pay immediately.
  Payment is system mail with RUB and a 30-day collection window, not a direct
  change to stash, XP or quests.
- The menu reports ready, pending, paid or withheld state. Turning rewards off
  stops new participation and defers queued payment; enabling again uses the
  existing profile generation and payment history.

## Capture and authority

### Private-session consent

Before a normal-to-Arena switch, the client generates a fresh non-secret control
ID and asks the normal server to prepare its grant in the server's reward metadata;
the helper copies it only for the matching profile, source installation and new
private-session marker. The credential is never returned to the game client.
Grant preparation or copy failure keeps the game usable with reward control
unavailable. New markers that require a grant cannot fall back to an older ON
handoff when the grant is missing or invalid.

The private server relays status and explicit ON/OFF requests to a bounded,
loopback-only HTTPS endpoint on the normal server. It pins that server's public
certificate and does not follow redirects or retry writes. This separate HTTP
listener avoids SPT's generic debug body logging. Public client responses contain
only status and a consent revision, never grant or signing keys.

Only the normal server writes consent metadata. Both sides keep their existing
raid/snapshot guards. Every change advances the persisted revision; a stale
click or request retry cannot overwrite a newer choice, including one made in
the normal menu. The grant expires after twelve hours, rotates on the next entry,
and is invalid after a normal-server restart. Recovery order is: return to the
normal session, restart the normal server after the private server has stopped,
then re-enter Arena. Restarting the normal server while the private server still
runs deliberately holds startup enrollment/payment with
`another-server-or-unknown-process`; a fresh entry alone does not clear that
startup hold. An unreachable normal server disables changes and new reward capture.

The private server refreshes authority before arming and starting a new run,
outside the profile snapshot lock. Already accepted runs retain their exact
authority and idempotent receipt/restoration path. Opt-out retains the old origin
key needed to return earlier receipts; re-enable must preserve its binding and
key. First enrollment and payouts still run exclusively before ordinary profile
loading; no live profile, stash, progression or ledger is replaced by a toggle.
Older helpers without a grant retain their previous read-only consent behavior.

### Combat capture

`ArenaRewardCapture` observes the actual mode's combat phase, active clock,
native participant IDs, physical body generations and fatal events. Ready
Tactical rounds require all five opponents; a partial spawn is not treated as a
smaller victory. Surviving bodies can participate in a new round under a separate
round life. Countdown and intermission time are excluded.

The server's `/arenamode/rewards/begin` route requires the exact confirmed native
Arena session and its snapshot. It binds the server's mode/team/hostility and PMC
identity to a random run ID. The client's selected limits are bounded and frozen.
The combat route accepts bounded observations, not a reward claim, profile path,
snapshot or restoration assertion. Server-issued round/life identities and the
existing validator enforce life uniqueness, timing, sides, event ordering and
terminal results. Report active time cannot exceed elapsed server time plus a
small scheduling tolerance.

SPT does not independently simulate combat. Roster and kill observations still
come from the local game; consistency checks and a server-owned rate policy are
not an anti-cheat system against the owner of the local machine.

The actual `ProfileSnapshotService` reports its detached clone and the successful
exact-object restore. Native `SaveProfileAsync` is followed by a bounded disk
read and comparison of the **entire JSON profile** to that clone, ignoring only
JSON formatting/property order. No health, inventory, statistics or timestamp
fields are discarded from the comparison. The installed SPT 4.1.6 save method
does not modify profile fields while serializing. Save completion alone is not
proof: native saves can skip invalid profiles or reuse a hash cache.

Only this positive chain creates an immutable signed earned-result receipt.
Finalization retains the exact session ownership until proof publication finishes.
Reward errors never cancel or weaken the existing protected profile restoration.

## Private-session transport

After startup enrollment the normal server creates a per-profile delegation with
a random signing key. The session helper copies only this bounded handoff into
`arena-rewards-origin.json` in the private server tree. Keys never appear in client
responses or logs. The ordinary profile's payment history is not used as an
incoming client envelope.

The private server publishes receipts under `user/arena-rewards-outbox`. After
proving that its private server has stopped, the helper checks the source marker,
normal installation, profile generation and delegation, then copies the opaque
receipt into the normal profile's `user/arena-rewards/<profileId>/inbox`. It reads
back the destination before deleting the source. Identical retries are harmless;
different bytes at an existing run ID, linked paths or mismatched origins retain
the private evidence and refuse transfer. An undrained outbox prevents rebuilding
the private tree. Return, watchdog shutdown and next-entry cleanup share this path.

No private profile is copied back. Disabling consent while a private copy exists
still allows return of its evidence, but the ordinary server defers payment.

## Startup enrollment and queued settlement

Consent is metadata only while the server runs. Enrollment uses the existing
pre-load startup gate, a durable enrollment plan, full-profile revision checks,
an empty journal and verified profile read-back. Existing history is never reset
to recover a missing file. Ordinary raids and their loaded profile objects do not
receive a reward-driven replacement.

The startup boundary is verified for SPT **4.1.6**; other native versions defer
payment and expose the refusal in the menu. Its native idle `inraid: {}` object
is accepted, as are null/absent members and the older `none` values. Active values
and malformed objects remain refused. This is only a profile-shape check; the
separate positive restoration proof and unresolved-run guards remain mandatory.

Several matches can accumulate before restart. Each verified signed source is
retained with an immutable allocation record. A **new** allocation binds the
latest clean ordinary profile once, so legitimate normal progress since the
private session is preserved. This is separate from the private snapshot's
restoration proof. Existing allocations and approvals retain their original
revision; retry never rebases a partially executed payment. The approval producer,
journal, profile ledger and native mail then settle receipts sequentially.

Imported markers, allocations, decisions and paid receipts cross-check one
another. Missing histories, changed sources and unresolved ordinary-run recovery
defer/quarantine rewards instead of guessing. No automatic pruning or repair is
performed. Histories are bounded to 4,096 entries per profile generation,
including snapshot attempts cancelled before combat. The limits of 100 rounds
per run, 20,000 lives and 24 hours of active time remain. Shared rollback of all
authority/profile files and unrelated external writers remain outside the local
cooperative-writer guarantee.

## Verification

### Private consent revision (2026-10-25)

`RewardConsentControlTests` covers synthetic ordinary/private profiles, first
activation and startup enrollment, enrolled OFF/ON/OFF, stale/duplicate revisions,
grant expiry/replacement/restart, invalid/missing markers and sidecars, retained
receipt keys, offline recovery and idempotent accepted runs. Real loopback TLS
tests exercise certificate pinning before credentials, rejected redirects,
bounded replies, lost responses, no write retries and the four-second timeout.
The helper harness additionally checks path links, exact marker/export binding,
bounded atomic copying and unchanged profile sentinels. The client harness checks
bounded optional preparation and late-response cancellation. Its request check
deserializes the actual outgoing body with the public DTO's case-sensitive
contract, matching SPT's native JSON binding.

The final no-deploy unit suite passed **2,777 tests**, with the one existing APBS
environment skip. This includes **71 new control tests**. Production-linked
checks passed **194 helper transfer**, **734 session client**, **39 reward menu**,
**2,538 map-menu** and **117 existing receipt-transfer** assertions; both seeded
receipt-transfer faults were rejected. Client, server and helper Release builds
passed. Installed binaries remained unchanged throughout this offline run.
All **173 session-client**, **35 map-menu** and **two receipt-transfer** injected
faults were rejected by their named assertions. Two existing coroutine mutation
anchors were narrowed after preparation added a second `yield return null` site;
the complete named inventory was then verified across isolated compiler runs.
The native test exposed and fixed the prepare request's initially lower-case
field name; the added mutation check rejects that regression.
Native acceptance is recorded separately in
[GPU-PRIVATE-REWARD-CONSENT-20261025.md](GPU-PRIVATE-REWARD-CONSENT-20261025.md).

### Previous verification (2026-10-24)

- The preceding menu revision moved the full card above the Arena map preview
  and removes both the old character-screen card and the Mutators duplicate.
  `RewardMenuStateChecks` passed 36 assertions for quiet polling, serialized
  consent, late responses and close/reopen cancellation. Its blocked-entry
  fallback harness passed 21 assertions; that fallback and its dedicated harness
  were subsequently removed in favor of private-session consent.
  The session-client harness passed 700 assertions and all 171 named injected
  faults (isolated compiler runs, combined identity inventory checked).
  `TdmMatchMenuChecks/Run-Checks.ps1` passed 2,538 assertions and its 35 injected
  faults, covering the card's placement, ownership and raid lock as well as the
  existing mode controls. These tests compile production sources with external
  Unity/HTTP stand-ins; they do not establish native rendering acceptance.
  [The separate GPU-2 menu test](GPU-REWARD-MENU-20261024.md) confirms native
  placement, map selection, return/reopen and isolation from the ordinary PMC
  menu. Status samples spanning 49 seconds retained their text and enabled state;
  the blocked-entry fallback itself was checked offline, not by changing GPU policy.
- The complete no-deploy unit suite passed: 2,699 tests passed; one existing APBS
  test remains skipped. This includes 63 runtime reward tests and four tests of
  the actual server snapshot/session lifecycle. Installed files were unchanged
  throughout the offline run.
- The separate client journal checks passed 26 assertions. The private transfer
  harness passed 117 assertions and rejected both seeded transfer/rebuild faults.
- Eleven affected gameplay/UI harnesses passed 5,681 assertions, with another
  126 in the linked burst and zombie service checks. Their existing regression
  wrappers rejected 180 injected faults and one historical Announcer regression.
  The final Tactical change received a targeted Announcer rerun; the parent
  independently reviewed logs and input hashes and repeated the 11 baselines.
- The focused reward suite passed all 373 tests after the native profile-shape
  and native session-identity corrections, using the final reward rates.
  Tactical polish passed 74 assertions and four historical/injected
  fault checks; the solo spawn checks passed 57 assertions and five mutations.
- [Independent integration review](REWARDS-RUNTIME-REVIEW-20261024.md) records the
  TDM give-up, Tactical cleanup/timing and startup-status findings and corrections.
- On SPT-GPU-2, real Factory Deathmatch victories on the ordinary and private
  servers each earned 11,000 RUB from one local kill and one round win. The
  helper returned the private receipt; startup delivered two messages totaling
  22,000 RUB. Native Messenger collection added exactly that amount to inventory.
  A further startup left the entire saved profile semantically identical and
  created no new reward or attachment. This is bounded DM acceptance, not a
  native certification of every mode, PFT outcome or crash-recovery branch.
- Native gameplay results and final regression evidence are recorded separately
  in the [GPU test report](GPU-REWARDS-RUNTIME-20261024.md); offline checks alone
  do not establish gameplay acceptance.
