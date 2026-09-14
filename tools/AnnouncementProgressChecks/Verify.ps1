$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'AnnouncementProgressChecks.csproj'
$sourcePath = Join-Path $repoRoot 'ArenaMode.Client\Audio\ArenaAnnouncementPolicy.cs'
$source = [System.IO.File]::ReadAllText($sourcePath)
$sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
$snapshotPath = Join-Path $repoRoot 'ArenaMode.Client\Audio\ArenaAnnouncementSnapshot.cs'
$snapshotHash = (Get-FileHash -LiteralPath $snapshotPath -Algorithm SHA256).Hash
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('ArenaAnnouncementProgress-' + [guid]::NewGuid())
[System.IO.Directory]::CreateDirectory($scratch) | Out-Null
$script:FaultCount = 0

function Assert-Positive {
    $output = & dotnet run --project $project -c Release 2>&1
    $result = $LASTEXITCODE
    $output | ForEach-Object { Write-Output $_ }
    if ($result -ne 0 -or ($output -join "`n") -notmatch 'ANNOUNCEMENT_PROGRESS_POLICY_PASSED') {
        throw "Production progress policy failed (exit $result)."
    }
}

function Reject-Fault([string]$name, [string]$original, [string]$replacement, [string]$scenario) {
    if ([regex]::Matches($source, [regex]::Escape($original)).Count -ne 1) {
        throw "Ambiguous or missing mutation anchor for $name; refusing unproved fault coverage."
    }
    $candidate = $source.Replace($original, $replacement)
    $copyPath = Join-Path $scratch ($name + '.cs')
    [System.IO.File]::WriteAllText($copyPath, $candidate)
    $binPath = Join-Path $scratch ($name + '-bin\')
    $objPath = Join-Path $scratch ($name + '-obj\')
    $output = & dotnet run --project $project -c Release "-p:AnnouncementPolicySource=$copyPath" `
        "-p:OutputPath=$binPath" "-p:IntermediateOutputPath=$objPath" -- $scenario 2>&1
    $result = $LASTEXITCODE
    $transcript = $output -join "`n"
    [System.IO.File]::WriteAllText((Join-Path $scratch ($name + '.log')), $transcript)
    if ($result -eq 0 -or $transcript -notmatch [regex]::Escape("ANNOUNCEMENT_PROGRESS_POLICY_FAILED: ${scenario}:") -or
        $transcript -match 'error (CS|MSB|NETSDK)\d+') {
        $output | ForEach-Object { Write-Output $_ }
        throw "$name was not rejected by its compiled runtime scenario (exit $result). Evidence: $scratch"
    }
    $script:FaultCount++
    Write-Output "REJECTED COMPILED FAULT: $name ($scenario)"
}

Assert-Positive
Reject-Fault 'inactive-leak' 'if (!snapshot.Active)' 'if (false && !snapshot.Active)' 'lifecycle'
Reject-Fault 'epoch-leak' 'bool newEpoch = _active && snapshot.Epoch != _epoch;' `
    'bool newEpoch = false;' 'time'
Reject-Fault 'epoch-threshold-memory' '_countdownSpoken = _timeSpoken = _killsSpoken = 0;' `
    '_countdownSpoken = 0;' 'time'
Reject-Fault 'prepare-repeat' 'if (preparing && !_prepareSpoken)' 'if (preparing)' 'countdown'
Reject-Fault 'countdown-repeat' 'if ((_countdownSpoken & bit) == 0)' 'if (true)' 'countdown'
Reject-Fault 'countdown-restart-silent' 'if (countdownEnded) _countdownSpoken = 0;' '' 'countdown'
Reject-Fault 'countdown-not-cancelled' 'countdownEnded || (_preparing && !preparing)' `
    'false || (_preparing && !preparing)' 'countdown'
Reject-Fault 'threshold-catchup' 'previous.HasValue && previous.Value > threshold' `
    '(!previous.HasValue || previous.Value > threshold)' 'time'
Reject-Fault 'expired-warning-wins' 'current > 0d && !cue.HasValue' 'current > 0d' 'time'
Reject-Fault 'threshold-repeat' 'current > threshold || (spoken & bit) != 0' 'current > threshold' 'kills'
Reject-Fault 'zero-remaining-warning' 'current > 0d && !cue.HasValue' '!cue.HasValue' 'kills'
Reject-Fault 'warnings-before-live' 'if (snapshot.Live && !snapshot.Terminal)' 'if (true)' 'kills'
Reject-Fault 'lead-no-cooldown' 'lead == _spokenLead || now < _nextLeadAt' 'lead == _spokenLead' 'lead'
Reject-Fault 'lead-latest-lost' 'if (lead == _spokenLead || now < _nextLeadAt) return;' `
    'if (now < _nextLeadAt) { _spokenLead = lead; return; } if (lead == _spokenLead) return;' 'lead'
Reject-Fault 'lead-scoreless-announcement' 'if (!lead.HasValue)' 'if (false && !lead.HasValue)' 'lead'
Reject-Fault 'series-midround-announcement' 'if (snapshot.Live && !snapshot.Terminal && !_enteredLive)' `
    'if (snapshot.Live && !snapshot.Terminal)' 'series'
Reject-Fault 'duplicate-deciding-round' 'else if (snapshot.MatchPoint)' 'if (snapshot.MatchPoint)' 'series'
Reject-Fault 'ladder-clear-loses-completion' 'else if (cycle.Value > _highestCycle.Value)' `
    'else if (cycle.Value > _highestCycle.Value && snapshot.WeaponTier.HasValue)' 'ladder'
Reject-Fault 'final-weapon-early' 'snapshot.WeaponTier == snapshot.WeaponCount - 1' `
    'snapshot.WeaponTier == snapshot.WeaponCount - 2' 'ladder'
Reject-Fault 'final-weapon-repeat' '_finalWeaponCycle < cycle.Value)' 'true)' 'ladder'
Reject-Fault 'terminal-invents-draw' 'if (snapshot.Draw && !snapshot.Live && !_drawSpoken)' `
    'if ((snapshot.Draw || snapshot.Terminal) && !snapshot.Live && !_drawSpoken)' 'draw'
Reject-Fault 'draw-repeat' 'if (snapshot.Draw && !snapshot.Live && !_drawSpoken)' `
    'if (snapshot.Draw && !snapshot.Live)' 'draw'

if ((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -ne $sourceHash -or
    (Get-FileHash -LiteralPath $snapshotPath -Algorithm SHA256).Hash -ne $snapshotHash) {
    throw 'Production policy or snapshot changed during verification.'
}
Assert-Positive
Write-Output "ANNOUNCEMENT PROGRESS CHECKS PASSED ($script:FaultCount compiled faults rejected; policy SHA256 $sourceHash; evidence $scratch)"
