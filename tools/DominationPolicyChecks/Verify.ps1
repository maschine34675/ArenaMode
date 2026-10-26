$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$source = (Resolve-Path (Join-Path $PSScriptRoot '../../ArenaMode.Client/Modes/DominationPolicy.cs')).Path
$faults = @(
    @{ Name='contested-keeps-scoring'; Find='if (!contested)'; Replace='if (true)'; Failure='contested points pause income' },
    @{ Name='empty-owner-stops-scoring'; Find='Income(point.OwnerUsec.Value, deltaSeconds, ref usecIncome, ref bearIncome);'; Replace='Income(point.OwnerUsec.Value, 0d, ref usecIncome, ref bearIncome);'; Failure='empty owned points earn income' },
    @{ Name='crowd-capture-boost'; Find='point.Progress + direction * deltaSeconds / _captureSeconds'; Replace='point.Progress + direction * deltaSeconds * (current.Usec + current.Bear) / _captureSeconds'; Failure='headcount does not accelerate progress' },
    @{ Name='no-neutralization'; Find='point.OwnerUsec = null;'; Replace='{ /* owner never neutralizes */ }'; Failure='enemy first neutralizes' },
    @{ Name='attacker-earns-before-capture'; Find='ownedAtStart ? deltaSeconds : Math.Max(0d, deltaSeconds - captureIn)'; Replace='(ownedAtStart || (point.OwnerUsec.HasValue && point.OwnerUsec.Value != usec)) ? deltaSeconds : Math.Max(0d, deltaSeconds - captureIn)'; Failure='enemy presence stops old owner income' },
    @{ Name='capture-pays-retroactively'; Find='ownedAtStart ? deltaSeconds : Math.Max(0d, deltaSeconds - captureIn)'; Replace='ownedAtStart ? deltaSeconds : (point.OwnerUsec == usec ? deltaSeconds : 0d)'; Failure='capture interval never pays retroactively' },
    @{ Name='no-post-capture-remainder'; Find='ownedAtStart ? deltaSeconds : Math.Max(0d, deltaSeconds - captureIn)'; Replace='ownedAtStart ? deltaSeconds : 0d'; Failure='only actual post-capture remainder pays' },
    @{ Name='instant-capture'; Find='if (deltaSeconds + RoundingTolerance >= captureIn)'; Replace='if (true)'; Failure='USEC captures forward' },
    @{ Name='empty-loses-ownership'; Find='Income(point.OwnerUsec.Value, deltaSeconds, ref usecIncome, ref bearIncome);'; Replace='point.OwnerUsec = null;'; Failure='empty owned points earn income' },
    @{ Name='score-rounding-lost'; Find='Math.Floor(seconds + RoundingTolerance)'; Replace='Math.Floor(seconds)'; Failure='float partition preserves whole-second income' },
    @{ Name='unbounded-time'; Find='deltaSeconds > MaxStepSeconds'; Replace='false'; Failure='invalid time rejected' },
    @{ Name='non-atomic-sample-publish'; Find='_sample[i] = current;'; Replace='_sample[i] = current; _views[i] = new DominationPointState(null, 0f, current);'; Failure='invalid sample cannot partially mutate points or score' },
    @{ Name='reversed-score-factions'; Find='if (usec) usecIncome += seconds; else bearIncome += seconds;'; Replace='if (usec) bearIncome += seconds; else usecIncome += seconds;'; Failure='friendly recapture keeps owned income' },
    @{ Name='no-analytic-cap'; Find='double duration = Math.Min(deltaSeconds, Math.Min(usecCrossing, bearCrossing));'; Replace='double duration = deltaSeconds;'; Failure='analytic cap stops at first income crossing' },
    @{ Name='cap-forgets-fractions'; Find='scoreLimit - _usecSeconds'; Replace='scoreLimit - UsecScore'; Failure='cap includes prior fractional holding income' },
    @{ Name='cap-counts-contested'; Find='if (current.Usec > 0 && current.Bear > 0) continue;'; Replace='if (false) continue;'; Failure='cap excludes hostile and contested owner income' },
    @{ Name='cap-forgets-earlier-income'; Find='remaining -= intervalIncome;'; Replace='remaining -= 0d;'; Failure='staggered capture changes scoring slope at completion' }
)
foreach ($fault in $faults) { $fault.Path = $source; $fault.Property = 'PolicySource' }
$matchSource = (Resolve-Path (Join-Path $PSScriptRoot '../../ArenaMode.Client/Modes/DominationMatchPolicy.cs')).Path
$matchFaults = @(
    @{ Name='countdown-released-early'; Find='if (now < _countdownEndsAt) return;'; Replace='if (false) return;'; Failure='countdown does not capture' },
    @{ Name='readiness-loss-ignored'; Find='if (!rosterReady) { Phase = TdmMatchPhase.Assembling; return; }'; Replace='if (false) { Phase = TdmMatchPhase.Assembling; return; }'; Failure='lost readiness cancels countdown' },
    @{ Name='pre-live-time-charged'; Find='_lastAdvancedAt = now;'; Replace='_lastAdvancedAt = _countdownEndsAt;'; Failure='live capture consumes full duration before scoring' },
    @{ Name='deadline-not-clipped'; Find='double end = Math.Min(now, deadline);'; Replace='double end = now;'; Failure='deadline clips capture and income before result' },
    @{ Name='terminal-not-frozen'; Find='Phase != TdmMatchPhase.Live || !ValidTime(now)'; Replace='(Phase != TdmMatchPhase.Live && Phase != TdmMatchPhase.Finished) || !ValidTime(now)'; Failure='terminal state and local result cannot change' },
    @{ Name='clock-integrated-twice'; Find='double duration = end - _lastAdvancedAt;'; Replace='double duration = end - StartedAt;'; Failure='live scores map to selected local faction' },
    @{ Name='gap-not-rebased'; Find='_lastAdvancedAt = end;'; Replace='_lastAdvancedAt += DominationPolicy.MaxStepSeconds;'; Failure='sampling resumes from rebased time after long gap' },
    @{ Name='local-faction-reversed'; Find='_localUsec = localUsec;'; Replace='_localUsec = !localUsec;'; Failure='live scores map to selected local faction' }
)
foreach ($fault in $matchFaults) { $fault.Path = $matchSource; $fault.Property = 'MatchSource' }
$faults += $matchFaults
Test-ArenaSourceFaults -Project (Join-Path $PSScriptRoot 'DominationPolicyChecks.csproj') -Token 'DOMINATION_POLICY_CHECKS_PASSED' -Properties @() -Faults $faults
