# Preparation voice lifetime (2026-09-19)

## Follow-up from the user's actual retest

`D:\SPT41\Development\LogOutput.log`, SHA256
`5252C0FC5324F9F7D3E8A43F91FB4886F566F0ECC335A1FE517AD7000B350F9D`,
provides the missing direct evidence: Prepare starts at 16:40:53.176 with all
64,742 frames, Deathmatch starts round 1 at 16:40:53.881, then the service logs
`status canceled` at 16:40:53.899. Only 723 ms of the 1,468 ms recording elapsed.
The previous fix handled phase advancement within one epoch, but the actual
Deathmatch rules increment their round number at combat start (0 -> 1).
Their snapshot therefore still triggered the deliberately hard epoch cancellation.

The DM adapter now assigns preparation/intermission to the round being prepared,
so its epoch stays stable when those real rules enter combat. Genuine new epochs,
invalid raid/readiness, pause, mute and terminal results still cancel normally.
A narrow first-live reset keeps a preceding intermission's Draw from consuming
the upcoming round's eventual terminal Draw. A Draw result already superseded
preparation at intermission before this change; that result priority is preserved,
not claimed as uninterrupted Prepare playback.

Regression coverage now drives the actual Deathmatch rules through the actual
snapshot/director/policy for first start, drawn and non-drawn intermission, and
the following final result. The old adapter fails the first-start case; restoring
its epoch, omitting intermission or losing the later Draw are rejected faults.
All 54 integration assertions and seven compiled fault controls pass; the root
review also reran the actual integration after the final changes. Evidence:
`artifacts/announcement-integration/cbf4091bb8434859a9cb87fa3bc5f77c`.
The earlier audio checks remain in place (309 assertions/four preparation faults),
as do 139 pure-policy assertions/22 faults. No recording, timing, scoring or
gameplay rule was changed. Native hearing remains a user restart/retest.

## Finding

The repository and installed `sounds/reserve/prepare_to_fight.ogg` are identical:
SHA256 `D71F7D74D43AC569895C40B049DDC9B6911C211DB0864410577279B97A9D0C1E`,
64,742 frames at 44,100 Hz (1.468 seconds). No resplit or asset replacement is needed.
The 2026-09-19 11-48-33 log requests this healthy recording at 09:57:36.678;
initial body replacement finishes about half a second later. The old policy
cancels active status on *any* preparation exit, including normal entry into
combat. The independent-volume output consequently stops the still-playing
sentence. A direct RoundStart can also replace preparation before it finishes.
The old log has no cancellation trace, so it cannot prove which path interrupted
the user's particular playback; the production-linked regression reproduces both.

## Change and limits

`AdvanceStatus` distinguishes normal preparation/countdown progress from actual
invalidation, without renumbering existing cues. It removes stale pending status
and obsolete countdown audio, but lets an already playing preparation finish.
Digits that arrive while the channel is occupied are dropped, never queued.
Only the RoundStart *voice* waits behind preparation. The original event still
reaches gameplay, awards and lighting immediately; no match timer or mode changes.
There is at most one pending start and it drains before an ordinary queued award.

Readiness loss, inactive/terminal state, epoch replacement, result/NoContest,
pause, mute and detach retain cancellation and discard any pending start.
Cancellation/start-deferral diagnostics explain future interruptions without
per-frame logging. The opt-in legacy interface-volume route still cannot safely
stop already submitted shared GUI audio; Arena never stops another mod's/UI's
shared source. Serialization prevents Arena's own normal-start interruption on
that route as well.

## Review of the six recordings completed by Claude

Reviewed commit `9fd93c3` against current source and assets. AIRBORNE replaces
TRICKSHOT with unchanged airborne hip-fire eligibility and score; NO SCOPE has
its own root clip. EMBARRASSING remains an actual own-hand-grenade self-kill;
NO CONTEST is a deduplicated technical outcome, not ordinary Give Up/Leave.
TRICKSHOT and AIR SUPERIORITY remain reserve recordings. All original 46 assets
are unchanged. Corrected two stale review documents, not the recordings.
Independent review found no concrete runtime defect in this six-cue scope.

## Verification

`tools/AnnouncerChecks/PreparationChecks.cs` links the actual policy and service,
uses the real preparation frame count, and checks both audio routes and both
RoundStart/director orderings, fast Gun Game entry without a start cue, countdown
freshness, readiness loss and eleven invalidation boundaries. The new check failed
against the unmodified production cancellation at the expected cutoff assertion.
`Verify-Preparation.ps1` also reintroduces policy/start interruptions and weakened
cancellation as compiled faults, requiring the corresponding behavior failure.

Existing audio regression, progress-policy and mode-adapter checks are retained.
Fresh checks: 309 audio assertions, four compiled preparation faults rejected,
139 progress assertions with 22 compiled faults rejected, and 34 mode-adapter
assertions. The historical audio fault suite and its final 309 assertions passed;
its original-pack Python verifier exposed a separate stale exact-file-set check
after the six-cue addition, repaired separately without relaxing asset identity.
The repaired verifier admits exactly the disjoint 46+6 recorded destinations,
pins the original manifest/hash table and validates all 52 decoded clips. Seven
metadata and eleven inventory/supplement faults are rejected, including unknown
root/reserve/nested OGGs and rewriting an original plus its recorded checksum.
All installed audio/assets match repository bytes except the two CC licence text
files: their text is identical after CRLF/LF normalization. The strict full-folder
`--deployment` check therefore still rejects those existing line-ending differences;
neither its equality rule nor the unrelated licence files were changed here.
Six-cue review checks: 194 combat, 80 self-grenade behavior, 35 native contracts,
and supplemental audio validation with five rejected asset faults.
Independent adversarial playback review found no concrete remaining blocker.
Controlled output checks do not prove audibility on the GPU host: restart EFT
and hear a fast initial start, normal countdown, pause/exit and next-round start.

## Delivery

Fix `64f0d7f` and strict asset-verifier follow-up `4bca31c` are on master. The
combined clean build has zero errors and four existing CS8632 warnings. Full
`Verify-Regression.ps1` now passes including 309 assertions, historical compiled
fault controls and the 52-clip verifier. Local transcription of the unchanged
preparation file returns "Prepare to fight."

Installed client SHA256:
`99C7F9DB982D1DCBF8727D096363620087FD958C57231558C1A8AD36CE888C56`.
The guarded transaction changed only client bytes, recopied three unchanged
package documents and preserved 129 other inventoried files, including
Core/server/UiBridge. `Verify-Deployed.ps1` passed. See the tracked
`ANNOUNCER-PREPARE-DEPLOYMENT-20260919.json`; remote mirror receipt and GPU hearing
are not claimed. Restart EFT only.
