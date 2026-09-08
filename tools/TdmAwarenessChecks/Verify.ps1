param()
$ErrorActionPreference = 'Stop'
$awarenessRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$awarenessProject = Join-Path $PSScriptRoot 'TdmAwarenessChecks.csproj'
$awarenessWork = Join-Path $awarenessRepo ('artifacts\tdm-awareness-checks\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $awarenessWork -Force | Out-Null
$awarenessPath = Join-Path $awarenessRepo 'ArenaMode.Client\Bots\TdmSearchAwareness.cs'
$relationsPath = Join-Path $awarenessRepo 'ArenaMode.Client\Bots\TeamDeathmatchRelations.cs'
$awarenessSource = [IO.File]::ReadAllText($awarenessPath).Replace("`r`n", "`n")
$relationsSource = [IO.File]::ReadAllText($relationsPath).Replace("`r`n", "`n")
$awarenessMutants = 0

function Invoke-Checks([string[]]$Extra = @()) {
    $awarenessBuild = @(& dotnet build $awarenessProject -t:Rebuild -c Release --nologo @Extra 2>&1)
    if ($LASTEXITCODE -ne 0) { throw ($awarenessBuild -join [Environment]::NewLine) }
    $awarenessOutput = @(& dotnet run --project $awarenessProject -c Release --no-build 2>&1)
    return @{ Exit = $LASTEXITCODE; Text = ($awarenessOutput | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine }
}
function Assert-Production([string]$Name) {
    $awarenessResult = Invoke-Checks
    [IO.File]::WriteAllText((Join-Path $awarenessWork ($Name + '.log')), $awarenessResult.Text)
    Write-Output $awarenessResult.Text
    if ($awarenessResult.Exit -ne 0 -or $awarenessResult.Text -notmatch 'TDM_AWARENESS_CONTRACTS_PASSED') { throw 'Production-linked awareness checks failed' }
}
function Test-Mutant([string]$Name, [string]$Source, [string]$Property, [string]$Before, [string]$After, [string]$Assertion) {
    if (-not $Source.Contains($Before)) { throw "Missing mutation anchor: $Name" }
    $awarenessMutantPath = Join-Path $awarenessWork ($Name + '.cs')
    [IO.File]::WriteAllText($awarenessMutantPath, $Source.Replace($Before, $After))
    $awarenessResult = Invoke-Checks @("-p:$Property=$awarenessMutantPath")
    [IO.File]::WriteAllText((Join-Path $awarenessWork ($Name + '.log')), $awarenessResult.Text)
    if ($awarenessResult.Exit -eq 0 -or -not $awarenessResult.Text.Contains('ASSERT: ' + $Assertion + ';')) {
        Write-Output $awarenessResult.Text
        throw "Mutant did not fail its named behavioral assertion: $Name"
    }
    $script:awarenessMutants++
    Write-Output "Awareness negative control rejected: $Name -> $Assertion"
}

try {
    Assert-Production 'production-before'
    Test-Mutant 'cue-omitted' $awarenessSource 'AwarenessSource' '_setHeard.Invoke(hearing, new[] { report, (object)Time.time })' 'null' 'cold opponent receives search cue'
    Test-Mutant 'heard-contact-ignored' $awarenessSource 'AwarenessSource' '(bool)_seen.GetValue(enemy) || (bool)_heard.GetValue(enemy)' '(bool)_seen.GetValue(enemy) || false' 'existing heard timer ages without refresh'
    Test-Mutant 'visible-guard-omitted' $awarenessSource 'AwarenessSource' '(bool)_visible.GetValue(enemy)' 'false' 'existing visible is never reseeded'
    Test-Mutant 'seen-guard-omitted' $awarenessSource 'AwarenessSource' '(bool)_seen.GetValue(enemy)' 'false' 'existing seen is never reseeded'
    Test-Mutant 'synthetic-gunshot' $awarenessSource 'AwarenessSource' 'Enum.Parse(sound.FieldType, "Generic")' 'Enum.Parse(sound.FieldType, "GunShot")' 'cue uses generic hearing report'
    Test-Mutant 'danger-cue' $awarenessSource 'AwarenessSource' '_danger.SetValue(report, false)' '_danger.SetValue(report, true)' 'cue stays non-danger and private'
    Test-Mutant 'squad-cue' $awarenessSource 'AwarenessSource' '_squad.SetValue(report, false)' '_squad.SetValue(report, true)' 'cue stays non-danger and private'
    Test-Mutant 'ignored-called-successful' $awarenessSource 'AwarenessSource' 'cue.Admitted = result != null && (bool)_heard.GetValue(enemy)' 'cue.Admitted = true' 'ignored cues are not reported as admitted'
    Test-Mutant 'null-result-admitted' $awarenessSource 'AwarenessSource' 'result != null && (bool)_heard.GetValue(enemy)' '(bool)_heard.GetValue(enemy)' 'unconfirmed null result is not admission'
    Test-Mutant 'heard-readback-omitted' $awarenessSource 'AwarenessSource' 'result != null && (bool)_heard.GetValue(enemy)' 'result != null' 'unconfirmed no-readback result is not admission'
    Test-Mutant 'permanent-wrapper-latch' $awarenessSource 'AwarenessSource' 'if ((bool)_seen.GetValue(enemy)' 'if (cue.Admitted || (bool)_seen.GetValue(enemy)' 'native forgetting permits same-wrapper reacquisition'
    Test-Mutant 'raid-owner-gate-omitted' $relationsSource 'RelationsSource' 'if (!IsCurrentSainOpponent(source, target, controller) || native == null)' 'if (native == null)' 'ownership rejects ordinary raid'
    Test-Mutant 'native-stop-ignored' $relationsSource 'RelationsSource' 'Enabled && _game.Status == GameStatus.Started' 'Enabled' 'ownership rejects native stopping'
    Test-Mutant 'target-body-gate-omitted' $relationsSource 'RelationsSource' '&& Register(source.GetPlayer) && Register(target)' '&& Register(source.GetPlayer) && target.HealthController.IsAlive' 'ownership rejects replaced target'
    Test-Mutant 'source-body-gate-omitted' $relationsSource 'RelationsSource' '&& Register(source.GetPlayer) && Register(target)' '&& source.HealthController.IsAlive && Register(target)' 'ownership rejects replaced source'
    Test-Mutant 'sain-wrapper-pair-omitted' $relationsSource 'RelationsSource' '&& IsExactEnemy(enemy, target, native)' '&& true' 'ownership rejects foreign SAIN player'
    Test-Mutant 'sain-native-property-omitted' $relationsSource 'RelationsSource' 'ReferenceEquals(_sainEnemyInfo.GetValue(enemy), native)' 'true' 'SAIN wrapper must reference the exact independently supplied native pair'
    Test-Mutant 'post-hearing-owner-check-omitted' $awarenessSource 'AwarenessSource' "if (!TeamDeathmatchRelations.OwnsAwareness(source, target, controller, enemy, native)) return;`n                        cue.Admitted" 'cue.Admitted' 'native stop at hearing-set blocks success readback'
    Test-Mutant 'diagnostic-cadence-omitted' $awarenessSource 'AwarenessSource' 'if (now < observation.NextLog) return;' '' 'diagnostics bounded to thirty seconds'
    Test-Mutant 'old-position-refresh-removed' $relationsSource 'RelationsSource' '_sainLastSeen.Invoke(enemy, new object[] { target.Position, Time.time });' '' 'existing position refresh retained'
}
finally {
    Assert-Production 'production-after'
}
@{
    Source = @((Get-FileHash $awarenessPath).Hash, (Get-FileHash $relationsPath).Hash)
    Mutants = $awarenessMutants
    Utc = [DateTime]::UtcNow.ToString('o')
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $awarenessWork 'evidence.json')
Write-Output "Awareness evidence: $awarenessWork"
Write-Output "TDM_AWARENESS_CHECKS_PASSED: $awarenessMutants named negative controls"
