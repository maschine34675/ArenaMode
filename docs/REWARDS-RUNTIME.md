# Optional profile rewards: runtime integration

This extends the settlement core, guarded startup consumer and result validator
described in REWARDS-CORE.md, REWARDS-SERVER.md and REWARDS-RESULTS.md. It is a
development change after release 0.2.0, not part of that existing download.

The optional [Tarkova Cola extension](TARKOVA-COLA-COMPAT.md) adds catalog-bound
Zombie research and achievement rewards to this same authority and startup
boundary. It does not import foreign/private profiles or permit live payment.

## Player flow

- **PROFILE REWARDS** in Arena's map menu defaults on for previously unconfigured,
  initialized normal profiles and belongs to the selected normal profile. It is separate from
  gameplay mutators. The full card above the map preview shows rates and current
  state directly. The old character-screen card and Mutators-header button are
  removed. START ARENA SESSION and compatibility notices contain no reward
  control. The same map-menu card can enable or disable rewards from the separate
  Arena session, through a limited capability issued by the normal server.
  Background status checks keep the confirmed label and button state;
  only initial loading or an explicit consent change shows a waiting state.
- The first normal server start with this version records the default and
  registers eligible profiles before native loading. The old manual first
  opt-in/restart cycle is unnecessary. Existing explicit OFF choices remain OFF.
  Profiles created or initialized after the server started are eligible at the
  next normal start. Explicit first activation of an unregistered profile still
  records metadata only: return to the normal session, then re-enter Arena. The supported
  local return starts the normal server automatically. Existing enrolled profiles can
  switch immediately even while the normal server is stopped: new local schema-3
  sessions use [signed private consent](ARENA-OFFLINE-SESSION-20261025.md).
- The server freezes 1,000 RUB + 100 XP per eligible local kill, 10,000 RUB +
  1,000 XP per round win, capped at 200,000 RUB and 20,000 XP per match.
  These are the user's selected starting values (`arena-v2`).
  The client cannot select an amount or payout recipient.
- Enemy final kills count; assists, friendly kills, self-kills and environmental
  deaths do not. DM/TDM/Tactical wins use the actual configured scoring rule.
  Gun Game and Zombies currently award kills only, not ladder/wave victories.
- A normal exit can retain kills from the last unfinished combat round without
  inventing a win. Aborted/no-contest reports receive no payment for the run.
- **START ARENA SESSION** saves and stops the supported local normal server before
  copying the profile. **RETURN TO NORMAL SESSION** stops the private server and starts
  normal SPT; its startup imports the final choice and signed results. EFT starts only
  after that server is ready, so enabled RUB mail and PMC XP are available on return.
  No extra manual restart is needed. This lifecycle also applies with no results or
  rewards OFF. Payment itself remains at the guarded
  **normal SPT server start**. Direct Arena raids without a private session therefore
  still pay at the next normal server start. Remote/unsupported or refused automatic
  return paths retain their queued results; the menu reports the applicable path.
  See [automatic return and its safety limits](REWARDS-RETURN.md).
  RUB is system mail with a 30-day collection window; XP is added directly to
  PMC Experience in the same atomic profile transaction. The mail states that
  XP has already been credited; it has no XP claim event. Level and trader loyalty
  are derived by native login using the final XP curve. No stash, skill or quest
  changes are imported. Old `arena-v1` results remain RUB-only.
- The menu reports ready, pending, paid or withheld state. Turning rewards off
  stops new participation and defers queued payment; enabling again uses the
  existing profile generation and payment history.
  After a private match, already published local receipts remain visible as
  pending until return transfers them. This presentation total is separate from
  payout authority and cannot issue or duplicate a payment.

## Capture and authority

XP uses the same permanent journal/ledger and duplicate/crash protection as RUB.
An invalid, missing, negative or overflowing native XP value withholds the entire
new payment; it is never replaced with a default. See
[XP settlement and native progression](REWARDS-XP.md) for the reviewed boundaries
and verification.

### Default-on startup registration

`ArenaRewardStartup` creates only the ordinary metadata directory before taking
the existing startup coordination lock. Private markers return before this
bootstrap. Profile discovery and every default/enrollment write run only after
the unchanged native-version, process, callback, unloaded-profile and lifetime
lock checks have admitted the startup window.

Discovery considers only top-level canonical 24-hex profile filenames, excluding
backup and synchronization-conflict files, and retains the 64-profile bound.
Only initialized, clean profiles with no reward extension and no existing
per-profile reward directory receive a persisted ON preference at revision 0.
The existing enrollment plan then performs its guarded profile update. Existing
directories, even empty or control-only ones, are not treated as first use;
missing or malformed old consent/history is never repaired by the default.
Explicit OFF, enrollment identities, journals and prior payments remain intact.
No default is applied by a live route or a private server.

The shared coordination path stays fail-closed for real contention only: a
`.startup.lock` that is still busy after two minutes, a held `.server.lock`, a live
private owner, a recorded Arena session that cannot be resolved and cancellation stop startup, because another process may still own
settlement and native profile loading would race it. A reward folder that merely
cannot be used does not: when the SPT folder or one of its parents is a link
(junction, symbolic link, mount point, cloud placeholder), when access is denied or
the path is unavailable, a process that reaches the install by the same path hits the
same failure and cannot be holding the lock either. Startup then does no reward work
at all, writes no reward data (an empty metadata folder or lock file may have been
created before a later step failed), logs one Warning that rewards are unavailable and
why, and SPT starts normally; the menu shows "Rewards are off: this installation's
reward folder cannot be used. See the server log." (a refusal that a restart may clear
keeps "temporarily unavailable"). Only a held lock counts as contention (a
sharing or lock violation, `RewardStartupLifetimeLock.IsContention`); a missing
folder, a too-long path or a disk fault is an unusable location. A private Arena
tree (session marker, lease file or active-session pointer present) is never skipped,
because it owns the original runtime's lifetime lock; the pointer
(`user/arena-rewards/offline-active.json`) is looked up without the link refusal, so
an install that a private session runs from stays protected when SPT is started
through a link to it. Residual case: a second NORMAL server started through a linked
spelling of an install that another normal server already runs from is no longer
stopped by the reward lock (SPT's own port check still applies); the old behaviour
refused that start. Other refused reward cases continue through the existing
reward-only failure reporting.

### Lost reward authority

The ledger lives in the profile (`com.maschine.ArenaMode.rewards`), its authority
beside it in `user/arena-rewards/<profileId>/`, bound to the installation root. The
two can part: SPT's profile wipe builds a new profile (same `info.id`, no extension
block), a restored pre-enrollment backup or an editor drops the block, and a moved or
copied install keeps an enrollment for the old root. Rewards for that profile cannot
be paid then. What must NOT happen is the rest of the product breaking with them.

**Arena entry never depends on reward authority.** Before the native stop sequence
pauses anything, the shutdown listener runs `RewardOfflineSession.PreflightReady`:
everything publication would refuse, without a write. A missing or mismatching
enrollment, an unreadable ledger or a missing consent file is not a refusal; the
session lease is then published WITHOUT authority, exactly like a never-enrolled
profile, and a Warning names the profile and the cause. Anything else deterministic
(`offline-entry-mismatch`, a still-active session, an unreadable `consent.json`,
which the return path would refuse to go on over) declines the entry while the
normal server is still alive: reply `return-not-ready` with `canResumeNormal`
(stage `entry-preflight`, which the helper names in its log), and the helper reopens
the normal session. A file error that may clear on its own while the authority is
read (a scanner holding `enrollment.json` or the profile: a sharing or lock
`IOException`, `UnauthorizedAccessException`) is treated the same way before the stop,
instead of costing the session its rewards. A file or folder that is simply missing
(`FileNotFoundException`, `DirectoryNotFoundException`, a path that cannot open) is
not: nothing would recreate it, so it stays "no authority for this session". The same read after the stop cannot decline any
more: the session then starts without authority, and the Warning names the cause
(the cause is logged once, at the preflight when that already saw it). On return, `ApplyOfflineConsent` stores the
acknowledged choice and skips re-publishing a handoff for an enrollment that no
longer matches this root or cannot be read at all (damaged, oversized: the same
classes entry lets through), instead of stopping normal startup. Both ends are
equally tolerant on purpose: an entry that starts a session without authority must
not leave behind a return that wedges every later normal start.

**A failure after the stop sequence ends in the graceful shutdown.** Once the update
loop is stopped (native `StopAsync` cannot be undone) and requests are held, the
normal server can never serve again. If the entry proof then cannot be written
(`RewardEntryPublication`: three attempts for transient file errors), the server
logs a Warning and registers the same `StopApplication` the success path uses, and
replies `accepted:false, held:false, entry-proof-unpublished` (stage
`publish-entry-proof`, which the helper names in its log). The helper reports a
declined entry and copies nothing (`held:true` would keep its Retry/Cancel dialog
open against a server that is about to exit). It recognises the reason and tells the
player that the SPT server saved the profile and is shutting down because Arena entry
could not be prepared, and that SPT must be started again; it does not ask to keep the
server open. It does not try to reopen a normal session: the server is exiting, holds
no lease and has no proof to resume from, so a restart would be a second server start
without recovery state. No profile data is lost: the final save was verified before
this point. Start SPT again normally.

**Startup recovery** (`RewardAuthorityRecovery`, first step of every profile's
startup pass, inside the admitted pre-load window). It acts on an enrolled profile
when the profile file lost the extension block while `info.id` still matches, or the
enrollment's `RuntimeRoot` no longer equals the current root (moved or copied
install). If NOTHING in the old directory is unsettled, it archives the directory by
rename (`<profileId>.archived-<utc>-<id>`, never deleted; the profile-keyed run,
snapshot and checkpoint evidence goes along as `evidence/`), carries the player's
consent choice over unchanged (an opted-out player stays opted out, revision kept)
and lets the ordinary enrollment create a fresh epoch in the same pass. For a moved
install the old ledger block is first copied to `lost-ledger-extension.json` in the
archive and then removed from the profile (its payments stay in the profile's mail;
only the ledger block is replaced). Crash safety: the choice is staged first
(`<profileId>.reenroll-<id>`), and the next startup finishes an interrupted second
rename before anything reads the missing directory as a new profile.

Unsettled means: any journal entry not committed or carrying Tarkova Cola
progression, or a `cola-plans` folder (the Tarkova Cola history is never
archived); a received receipt or allocation without its `imported` marker; an
approval, candidate or decision not matched by a committed journal entry; a pending
recovery request; an unresolved Arena run or restoration; an active offline session;
an unrecognised file; a moved install whose ledger and journal disagree. A record
that a validated recovery closure (the automatic discard of an uncertain session, or
a confirmed recovery request) ended WITHOUT payment is settled, because the settlement
code leaves its files unmarked on purpose: that covers a pending run or restoration,
a retained receipt (verified with the key of its own old delegation first), a
candidate and a decision. Closures are read only for the moved-install trigger (the
profile still holds the markers); a profile that lost its ledger block lost the
markers too, so nothing counts as closed there. A closure that cannot be proven
under the new root (a late restoration needs delegations that still name the old
root) cannot be read and keeps the profile unsettled: fail toward no payment. Then
nothing changes: the quarantine stays, no payment is possible, and the Warning
carries the cause. A fresh epoch makes every receipt and key of the old one
unusable, so neither a copy of the install nor a restored backup can be paid twice;
a profile whose ledger is an older revision of the same epoch (a backup behind the
journal), or that carries another epoch's block, is not recovered and keeps going
through the settlement core's divergence checks.

The Info line that reports a recovery says the old records were archived; it claims
that rewards were enrolled again only when the same startup pass really did so,
otherwise it says that enrollment follows at the next successful start (or that
rewards are switched off and nothing was enrolled). A player who switched rewards
off sees the ordinary "Optional profile rewards are off." in the menu while old
records stay unsettled (the Warning is still logged at every start); "records no
longer match" appears once they switch rewards on.

**Tarkova Cola history and lost authority.** The Cola companion recovery
(`RewardColaJournal.RecoverAsync`, next in every profile's pass) used to read the
native ledger and, with the block gone, stopped the whole server (a wiped profile
that held paid Cola progress then blocked SPT on every machine sharing it). A
moved or copied install did the same: the synchronised reward folder names the
root that created it, every plan is bound to that root, and the first plan was
rejected as not belonging to this installation. Now it first asks the same
question as the startup recovery
(`RewardAuthorityRecovery.LostRewardBlock`: `info.id` still matches and the profile
root has no `com.maschine.ArenaMode.rewards` member, decided on the JSON text
without building native objects), and, with the block present, whether the
enrollment's `RuntimeRoot` is this installation's root. A profile without the block
cannot be a half-written payment, because every payment writes a whole profile that
carries the block before and after; and the native side of each recorded payment
went with the wipe, so nothing native can be confirmed or finished. On a moved
install the records belong to the other root, which alone can finish them. Two
outcomes:

- The companion side is complete: every plan in `cola-plans` is readable and, when
  the journal records its reward as possibly paid (an entry that is not merely
  Prepared), has its matching `.done.json`; no completion record lacks a plan; no
  unknown file lies in the folder; every attempted Cola payment of the journal is
  committed with a matching plan. A plan the journal does not know, or knows only
  as Prepared, never reached a native write and is inert, like a stray temporary
  file (the ordinary recovery skips both as well). `RecoverAsync` returns without
  writing anything: no Cola progress, no payment, no file changed or moved. The
  profile stays quarantined (a Cola progression is unsettled), startup goes on,
  the profiles load, and for a wiped profile or a moved install the one Warning
  above says the reward records hold Tarkova Cola history that stays unchanged and
  rewards stay off. A deleted profile file whose Cola plans remain is treated the
  same, but has no authority to report and logs at Debug level only. The check
  reads the plans without the installation root (the reward folder synchronises
  between installations); it never locates or touches the Tarkova Cola mod's own
  files.
- The companion side is not complete. Startup stops, because a half-finished
  companion write could leave the Cola files inconsistent and nothing here can
  finish it. The Error names the actual cause and the remedy that is true for it:
  - A possibly paid plan has no completion record, or the journal records a Cola
    payment as not committed (an unfinished transaction): restore the profile file
    from a backup taken after that reward (by default SPT keeps backups in
    `user/profiles/backups`; `SPT_Data/configs/backup.json` names the folder), and
    the ordinary recovery completes the transaction at the next start. On a moved
    install start the installation that created the records once instead.
  - Damaged reward records (an unreadable plan, journal or enrollment, an unknown
    file, a completion record that does not match its plan, a plan or payment
    without its counterpart): a profile backup does not repair these, because the
    ordinary recovery stops on the same file. Restore
    `user/arena-rewards/<profileId>` from a backup or from an installation that
    still has it intact.
  A file that was only locked clears by starting again. Do not delete or move the
  journal or the plan files.

With the block present and the root unchanged nothing changed: the ordinary
recovery runs exactly as before.

Manual remedy for an unsettled profile: start the installation at the root recorded
in `enrollment.json` once, so its pending rewards are delivered, and move it again;
or, accepting the loss of those unpaid rewards, stop the server and rename
`user/arena-rewards/<profileId>` aside (a profile that still carries the ledger block
also needs that block removed from its file, which is why this is a last resort).
For a wiped profile whose Tarkova Cola history is complete, the same last resort
starts the reward ledger afresh: the Cola progress belongs to the Tarkova Cola mod
(`user/mods`, not the reward folder) and the transactions are already finished, so
only the audit trail (journal, plans) is lost to the live folder. The rename sets
the WHOLE folder aside, though, not just that trail: the player's reward choice
(`consent.json`) and the enrollment go with it. The next start finds a profile with
no reward folder, treats it as new and enrolls it with the default, rewards ON, so a
player who had switched rewards off must switch them off again (the automatic
archive carries the choice over; a manual rename does not). Do it only with the
server stopped, and never while a Cola reward is unfinished. This is the one
exception to "do not move the journal or the plan files" above, and it is a
last resort.

### Private-session consent

New schema-3 local sessions use the separate entry capability, exclusive original
runtime ownership and signed private consent described in
[the offline-session contract](ARENA-OFFLINE-SESSION-20261025.md). They do not contact
the stopped normal server. Ordinary startup recovers the final revision before payout.
The rest of this subsection documents the retained schema-1/2 live-server relay:

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
and is invalid after a normal-server restart. A separate return capability handles
the supported automatic restart; it never reuses the consent token. If the normal
server was already restarted manually and the capability became stale, restart it
after leaving the private session, then re-enter Arena. Restarting the normal server while the private server still
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

### Default-on revision (2026-10-25)

`RewardStartupDefaultsTests` adds 26 synthetic cases: first startup without
reward state, idempotency, pre-enrollment and enrolled OFF, missing/malformed
consent, copied history, private/loaded/closed windows, unsafe or uninitialized
profiles, conflict backups, per-profile failures, capacity and linked paths.
An additional native-load fixture runs default registration followed by the
real referenced SPT `SaveServer` load/migration/save pipeline and checks intact
inventory, foreign data and persisted enrollment. All input profiles are generated
temporary fixtures; this does not claim a new installed-server or in-game run.

The full no-deploy suite passed **2,804 tests**, with the one known APBS environment
skip. Installed binaries remained unchanged during verification. The preceding
private-session native test below predates this default change.

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
