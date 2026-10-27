# Arena end persistence and background updates

The reported Air Pit end on 2026-10-27 failed at 12:27:56 UTC with
`return-profile-save-not-confirmed`; EFT then delayed its retry by 47.90865 seconds.
The complete saved profile reproduced the captured checkpoint fingerprint after
changing only `characters.pmc.Hideout.sptUpdateLastRunTimestamp` in memory from
the later native update value to its pre-raid value. No original file was edited.
Native converter/cloner round trips did not reproduce a serialization difference.

SPT's periodic hideout callback updates resources/crafts and that timestamp. It
could acquire the restored profile while Arena was still saving and verifying
it. Switching back to the out-of-raid interval before this boundary also made
the callback eligible sooner. A file-write semaphore does not exclude that writer.

## Change

The protected service now acquires its own update hold, drains running native
callbacks, restores, saves, verifies the complete profile and completes the exact
transaction before releasing its hold. Native admission only claims the end.
Failed-start, stale-start and crashed-client recovery use the same boundary.
The service never drains its own HTTP request or reacquires the caller's raid gate.

Scoped holds compose with other profiles and the session-return owner's permanent
hold. Cancellation before restoration releases only the caller's hold. A failure
after the swap retains the hold and detached witness across retries, so background
updates cannot make the expected profile irrecoverably stale. Started native saves
remain uncancellable. Missing native update protection refuses a new Arena arm.
The gate is independent of rewards; no profile fields are excluded from comparison.

## Verification

Deterministic tests include the old timestamp-only failure as a positive control,
an already-running writer, blocked subsequent writers, native nested profile
serialization, optional reward failure, native write failure/cached no-op/retry,
caller cancellation, overlapping holds and actual foreign mutation refusal.
The focused suite, full regression and PitFireTeam lifecycle harness passed.
Independent review found no blocking defect.

Native acceptance and deployment evidence will be recorded after the playtest.
