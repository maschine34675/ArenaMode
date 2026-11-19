# Tactical opening rounds: client gate checks

Run `pwsh -NoProfile -File tools/TacticalOpeningChecks/Verify.ps1` from the repo (or the harness through
`tools/Run-ArenaVerification.ps1 -Harness TacticalOpeningChecks`). Nothing here builds into or touches the game.

The program compiles the production `TacticalOpeningBandGate`, `TacticalRoundReply`, `ArenaDifficultyPolicy` and
`Protocol` and drives the gate on a fake clock against a scripted sender: report and ack, the 3 second hold cap
measured from the held request, the stage rule (only a round whose gear level differs from the last acknowledged
round waits: round 4 with a dead route releases at once), READY pressed at once, a lost report (resend after 2 s,
four sends at most), a lost ack (an unanswered send counts as lost after 8 s and the resend gets a Duplicate reply),
a late ack, an ack of an older seq, `Reset`, every refusal shape (a refusal or `applies:false` ends the feature, the
"raid is ending" refusal is transient), the process-wide seq counter and its 1..1,000,000 bound, a blank arm token,
and that nothing throws into the match start or the frame loop. It also reads the glue in `TacticalMode.cs`, the HTTP
sender and the server's use of the shared refusal constant as source text (the game's types cannot be linked).

`Verify.ps1` runs the baseline and then about seventy mutants through `Test-ArenaSourceFaults`; each must fail the
assertion it names. The stage after this one (pool purge, buy-screen line) reads `AckedRound`, `Applies` and
`CurrentGearLevel` of the gate through `ArenaTacticalOpening`.
