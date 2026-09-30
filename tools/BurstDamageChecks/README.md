# Burst damage presentation checks

Run from the repository:

```powershell
pwsh -NoProfile -File tools/BurstDamageChecks/Verify.ps1
```

The executable compiles the actual `DamageNumbersElement` and `ArenaDamage`.
Only Unity projection/time/rectangles, TMP measurement/submission, the shared
style and configuration are boundary doubles. Capture wiring is checked against
the production `victim.GetInstanceID()` assignment; a client build is still needed
to verify native API compilation. The script does not build or deploy that client.

Coverage includes disabled behavior, same-body and interleaved-target grouping,
fractional independent HP/armor totals, a sliding 0.25-second gap, unknown and
negative instance IDs, killing-hit closure, out-of-order times, lifetime/fade,
first-impact anchoring, live option changes, expiration without an intervening
frame, pool eviction, width growth and all twelve occupied lanes. Staggered
labels must stay apart throughout their rise beside a refreshed older burst,
at canvas scales 0.5/1/2 and after grouping is disabled. Collision envelopes
reserve that motion without changing visible rise or assigned lane offsets.
It runs the baseline, eleven compiled negative controls and the restored production source.
Compilation failure never counts as successful mutation rejection.

Per-run artifacts retain logs, source hashes and mutated copies under
`artifacts/burst-damage-checks/<id>/`. Neither the fixture font measurements nor
the projection double establish native appearance, camera behavior or Harmony
capture acceptance. Confirm automatic-fire bursts in a real Arena raid.
