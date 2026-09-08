# TDM catalog scheduling checks

Run `pwsh -NoProfile -File tools/TdmWarmupChecks/Verify.ps1` from the repository.
This links the actual ArenaController and KitBundlePreloader against
small host doubles. It verifies deferred TDM catalog start, missing/mismatched
match handling, player replacement, rewarm/reset behavior, fixed-kit exclusion,
protected PFT exclusion and the unchanged eight-second schedule in other modes.
Ten controlled production-source faults must fail their specific assertions.
Production code is never modified by the runner; artifacts remain ignored.

The pool tests use controlled asynchronous native-task doubles to verify
overlapping loads, fault/cancellation/guard errors, reset and late old-raid
completion. Disposing a polling coroutine must not pretend its native task ended.
The host doubles do not execute Unity coroutines or load real bundles. The
production TeamDeathmatchMode readiness property and the native spawn lifecycle
are checked separately by the TDM runtime harness and the full staged build.
These checks establish scheduling, not reduced GPU load times or live combat.
