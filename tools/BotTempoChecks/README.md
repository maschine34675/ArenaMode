# Bot tempo checks

Run `pwsh -NoProfile -File tools/BotTempoChecks/Verify.ps1` from the repository.
Only offline build/output under tools and ignored artifacts; no deployment.

Links `ArenaBotTempo.cs` and `ArenaBotMovement.cs`. NativeDoubles extends this
repository's motion-harness boundary shapes; Boundaries contains synthetic SAIN
API shapes, not third-party implementation. The minimal wait boundary models
only the native timer expiry/release ownership contract. Real Unity/Harmony,
SAIN steering, performance and navigation are not simulated.

Installed metadata/IL checks inspect SAIN 4.5.1.0 and EFT with Mono.Cecil.
Compiled negative controls exercise ordinary-raid admission, reload ownership,
short routes, strafe effect, both old 0.7 floor/threshold regressions, premature wait release, foreign pause, restoration
and the mutative native flash getter. Production is rebuilt before and after;
source/harness hashes must remain stable. Evidence goes to
`artifacts/bot-tempo-checks/<run-id>/`.

The 0.85 combat-strafe floor has boundary checks below, at and immediately above
the threshold, including inputs between the former 0.7 floor and 0.85. Zero,
negative and nonfinite inputs remain native; every call leaves the preset intact.
Existing reload, doorway, danger, physical-owner and ordinary-raid guards remain
covered. These are input contracts, not measured in-game movement speed.
