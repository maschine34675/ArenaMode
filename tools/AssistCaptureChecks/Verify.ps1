param([string]$SptPath = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))))
$ErrorActionPreference = 'Stop'
$assistRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$assistSource = Join-Path $assistRoot 'ArenaMode.Client/Combat'
$assistScratch = Join-Path $assistRoot ('artifacts/assist-capture/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($assistScratch)
$assistFiles = @('ArenaAssistCapture.cs','ArenaKill.cs','ArenaCombatAwards.cs','ArenaHitHistory.cs','ArenaShotAwardHistory.cs','ArenaAdministrativeDeathGate.cs','ScoreRules.cs','CombatPatches.cs')
$assistBound = @($assistFiles | ForEach-Object { 'ArenaMode.Client/Combat/' + $_ }) +
    @(Get-ChildItem -LiteralPath $PSScriptRoot -File | ForEach-Object { 'tools/AssistCaptureChecks/' + $_.Name })
$assistHashes = @{}
foreach ($path in $assistBound) { $assistHashes[$path] = (Get-FileHash -LiteralPath (Join-Path $assistRoot $path)).Hash }
$assistInstalled = @{}
foreach ($path in @('BepInEx/plugins/maschine-ArenaMode.Client.dll','SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll',
    'BepInEx/plugins/maschine-Redeploy.Core.dll','EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll')) {
    $assistInstalled[$path] = (Get-FileHash -LiteralPath (Join-Path $SptPath $path)).Hash
}
function Run-Assist([string]$name, [string]$source, [string]$suite = 'all', [string]$failure = '') {
    $dir = Join-Path $assistScratch $name; [void][IO.Directory]::CreateDirectory($dir)
    $build = & dotnet build (Join-Path $PSScriptRoot 'AssistCaptureChecks.csproj') -c Release --nologo "-p:AssistSourceRoot=$source" "-p:BaseIntermediateOutputPath=$dir/obj/" "-p:OutputPath=$dir/out/" 2>&1 | Out-String
    $code = $LASTEXITCODE; [IO.File]::WriteAllText((Join-Path $dir 'build.log'), $build)
    if ($code -ne 0) { throw "$name must compile before behavioral verification: $build" }
    $run = & dotnet (Join-Path $dir 'out/AssistCaptureChecks.dll') $suite 2>&1 | Out-String
    $code = $LASTEXITCODE; [IO.File]::WriteAllText((Join-Path $dir 'result.log'), $run)
    if ($failure) {
        if ($code -eq 0 -or !$run.Contains('FAILED: ' + $failure)) { throw "$name did not reject intended fault: $run" }
        Write-Output "Rejected $name at: $failure"
    } elseif ($code -ne 0 -or !$run.Contains('ASSIST_CAPTURE_PRODUCTION_PASSED:')) { throw "$name failed: $run" }
    else { Write-Output $run.Trim() }
}
try {
    $native = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Verify-Native.ps1') -SptPath $SptPath 2>&1 | Out-String
    $nativeCode = $LASTEXITCODE; [IO.File]::WriteAllText((Join-Path $assistScratch 'native.log'), $native)
    if ($nativeCode -ne 0 -or !$native.Contains('ASSIST_CAPTURE_NATIVE_CONTRACT_PASSED:')) { throw $native }
    Write-Output $native.Trim()
    Run-Assist 'production' $assistSource
    $faults = @(
        @{ Name='drops-unattributed-denominator'; Suite='sources'; Find='match.RecordAssistDamage(damage);'; Replace='if (damage.AttackerBodyId != 0) match.RecordAssistDamage(damage);'; Failure='ownerless source preserves actual damage denominator' },
        @{ Name='same-profile-body-alias'; Suite='sources'; Find='ReferenceEquals(world.GetEverExistedPlayerByID(body.ProfileId), body)'; Replace='world.GetEverExistedPlayerByID(body.ProfileId) != null'; Failure='replaced old physical source has no invented contributor' },
        @{ Name='friendly-bot-contribution'; Suite='sources'; Find='if (!rounds.FreeForAll && !ReferenceEquals(attacker, local)'; Replace='if (false && !ReferenceEquals(attacker, local)'; Failure='all-vs-player bot friendly damage has no invented contributor' },
        @{ Name='early-death-cleanup'; Suite='lifecycle'; Find='=> __state = _enabled && ArenaSession.IsActive ? ArenaMatchService.Current : null;'; Replace=' { __state = _enabled && ArenaSession.IsActive ? ArenaMatchService.Current : null; __state?.ForgetAssistVictim(1); }'; Failure='death entry does not clear contributions before scoring' },
        @{ Name='friendly-terminal-cashes-out'; Suite='terminal'; Find='&& TryHostileSource(match.Mode, world, local, victim, aggressor, damageType, out _, out _)'; Replace='&& true'; Failure='all-vs-player friendly bot final cannot cash out assist' }
    )
    foreach ($fault in $faults) {
        $faultRoot = Join-Path $assistScratch ($fault.Name + '/source'); [void][IO.Directory]::CreateDirectory($faultRoot)
        $path = Join-Path $faultRoot 'ArenaAssistCapture.cs'; $source = [IO.File]::ReadAllText((Join-Path $assistSource 'ArenaAssistCapture.cs'))
        if ($source.Split(@($fault.Find), [StringSplitOptions]::None).Length -ne 2) { throw "Ambiguous fault anchor: $($fault.Name)" }
        [IO.File]::WriteAllText($path, $source.Replace($fault.Find, $fault.Replace))
        Run-Assist $fault.Name $faultRoot $fault.Suite $fault.Failure
    }
    Run-Assist 'production-final' $assistSource
    foreach ($path in $assistBound) {
        if ((Get-FileHash -LiteralPath (Join-Path $assistRoot $path)).Hash -ne $assistHashes[$path]) { throw "Bound source changed; rerun required: $path" }
    }
} finally {
    foreach ($path in $assistInstalled.Keys) {
        if ((Get-FileHash -LiteralPath (Join-Path $SptPath $path)).Hash -ne $assistInstalled[$path]) { throw "Installed binary changed: $path" }
    }
}
[ordered]@{ sourceHashes=$assistHashes; installedHashes=$assistInstalled; sourcesUnchanged=$true; installedUnchanged=$true;
    compiledFaultCount=5; completedUtc=[DateTime]::UtcNow.ToString('o'); evidenceDirectory=$assistScratch;
    limitation='Production capture adapter and data/history link against explicit native/mode boundary doubles. Installed IL contracts prove native ordering, not Unity/Harmony execution or deployment.'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $assistScratch 'manifest.json')
Write-Output "ASSIST CAPTURE CHECKS PASSED; five compiled faults rejected; source and installed binaries unchanged; evidence: $assistScratch"
