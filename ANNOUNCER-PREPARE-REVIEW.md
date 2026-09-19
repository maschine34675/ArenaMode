# Preparation voice lifetime (2026-09-19)

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
Six-cue review checks: 194 combat, 80 self-grenade behavior, 35 native contracts,
and supplemental audio validation with five rejected asset faults.
Independent adversarial playback review found no concrete remaining blocker.
Controlled output checks do not prove audibility on the GPU host: restart EFT
and hear a fast initial start, normal countdown, pause/exit and next-round start.
