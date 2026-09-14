param()
$ErrorActionPreference = 'Stop'
$shotRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$shotScratch = Join-Path $shotRoot ('artifacts/shot-award-history-checks/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($shotScratch)
$shotSources = @{
    ShotHistorySource = 'ArenaMode.Client/Combat/ArenaShotAwardHistory.cs'
    HitHistorySource = 'ArenaMode.Client/Combat/ArenaHitHistory.cs'
}
$shotBound = @($shotSources.Values) + @(Get-ChildItem -LiteralPath $PSScriptRoot -File | ForEach-Object { 'tools/ShotAwardHistoryChecks/' + $_.Name })
$shotHashes = @{}
foreach ($path in $shotBound) { $shotHashes[$path] = (Get-FileHash -LiteralPath (Join-Path $shotRoot $path)).Hash }
$shotTexts = @{}
foreach ($property in $shotSources.Keys) { $shotTexts[$property] = [IO.File]::ReadAllText((Join-Path $shotRoot $shotSources[$property])).Replace("`r`n", "`n") }
function Invoke-ShotProbe([string]$name, [string]$property = '', [string]$source = '', [string]$part = 'all', [string]$failure = '') {
    $dir = Join-Path $shotScratch $name
    [void][IO.Directory]::CreateDirectory($dir)
    $arguments = @('build', (Join-Path $PSScriptRoot 'ShotAwardHistoryChecks.csproj'), '-c', 'Release',
        "-p:BaseIntermediateOutputPath=$dir/obj/", "-p:OutputPath=$dir/out/", '--nologo')
    if ($property) {
        $file = Join-Path $dir 'Source.cs'; [IO.File]::WriteAllText($file, $source)
        $arguments += "-p:${property}=$file"
    }
    $build = & dotnet @arguments 2>&1 | Out-String
    [IO.File]::WriteAllText((Join-Path $dir 'build.log'), $build)
    if ($LASTEXITCODE -ne 0) { throw "$name did not compile: $build" }
    $run = & dotnet (Join-Path $dir 'out/ShotAwardHistoryChecks.dll') $part 2>&1 | Out-String
    $code = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $dir 'result.log'), $run)
    if ($failure) {
        if ($code -eq 0 -or !$run.Contains($failure)) { throw "$name failed to reject intended behavior: $run" }
        Write-Host "Rejected $name at: $failure"
    } elseif ($code -ne 0 -or !$run.Contains('SHOT_AWARD_HISTORY_PRODUCTION_PASSED')) { throw "$name failed: $run" }
    else { Write-Host $run.Trim() }
}
Invoke-ShotProbe 'production'
$shotFaults = @(
    @{ Name='reuse-old-pool-entry'; Property='ShotHistorySource'; Part='identity';
       Find='_shots.Remove(shot);'; Replace='if (_shots.TryGetValue(shot, out _)) return;';
       Failure='unknown pooled create removes previous shot facts' },
    @{ Name='ambient-fragment-facts'; Property='ShotHistorySource'; Part='parent';
       Find="airborneHipFire = inherited.AirborneHipFire;`n            boltActionHipFire = inherited.BoltActionHipFire;";
       Replace='// Deliberate fault: keep ambient flags instead of parent snapshot.';
       Failure='child copies frozen parent facts rather than ambient booleans' },
    @{ Name='old-round-facts'; Property='ShotHistorySource'; Part='parent';
       Find='_epoch = checked(_epoch + 1);'; Replace='// Deliberate fault: epoch does not advance.';
       Failure='new round invalidates copied old facts' },
    @{ Name='wrong-weapon-admission'; Property='ShotHistorySource'; Part='identity';
       Find='|| !string.Equals(entry.WeaponId, weaponId, StringComparison.Ordinal)'; Replace='';
       Failure='weapon identity mismatch rejected' },
    @{ Name='unrelated-lethal-type'; Property='HitHistorySource'; Part='contact';
       Find='body == null || shooter == null || !lethalBullet'; Replace='body == null || shooter == null';
       Failure='nonbullet lethal context rejected'; Occurrences=2 },
    @{ Name='cross-body-nested-contact'; Property='HitHistorySource'; Part='nesting';
       Find='|| _shotDepth != 1 || _damageDepth != 1'; Replace='';
       Failure='nested shot invalidates outer contact False' }
)
foreach ($fault in $shotFaults) {
    $source = $shotTexts[$fault.Property]
    $expected = if ($fault.Occurrences) { $fault.Occurrences } else { 1 }
    if ($source.Split(@($fault.Find), [StringSplitOptions]::None).Length -ne $expected + 1) { throw "Ambiguous fault anchor: $($fault.Name)" }
    Invoke-ShotProbe $fault.Name $fault.Property ($source.Replace($fault.Find, $fault.Replace)) $fault.Part $fault.Failure
}
foreach ($path in $shotBound) {
    if ((Get-FileHash -LiteralPath (Join-Path $shotRoot $path)).Hash -ne $shotHashes[$path]) { throw "Bound source changed; rerun required: $path" }
}
Invoke-ShotProbe 'production-final'
[ordered]@{ sourceHashes=$shotHashes; faultCount=$shotFaults.Count; sourcesUnchanged=$true;
    evidenceDirectory=$shotScratch; completedUtc=[DateTime]::UtcNow.ToString('o') } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $shotScratch 'manifest.json')
Write-Host "Evidence: $shotScratch"
Write-Host 'SHOT AWARD HISTORY CHECKS PASSED'
