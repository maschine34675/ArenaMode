param([string]$SptPath = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Support.ps1')
$tauntRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$tauntSource = Join-Path $tauntRoot 'ArenaMode.Client/Combat'
$tauntMatch = Join-Path $tauntRoot 'ArenaMode.Client/Modes/ArenaMatch.cs'
$tauntScratch = Join-Path $tauntRoot ('artifacts/self-grenade-taunt/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($tauntScratch)
$tauntFiles = @('ArenaSelfGrenadeTaunt.cs','ArenaShotAwardHistory.cs','ArenaHitHistory.cs','ArenaAdministrativeDeathGate.cs')
$tauntBound = @($tauntFiles | ForEach-Object { 'ArenaMode.Client/Combat/' + $_ }) + @(
    'ArenaMode.Client/Combat/CombatPatches.cs','ArenaMode.Client/Modes/ArenaMatch.cs',
    'ArenaMode.Client/Hud/ArenaHud.cs','ArenaMode.Client/Audio/AnnouncerService.cs'
) + @(Get-ChildItem -LiteralPath $PSScriptRoot -File | ForEach-Object { 'tools/SelfGrenadeTauntChecks/' + $_.Name })
$tauntHashes = @{}
foreach ($path in $tauntBound) { $tauntHashes[$path] = (Get-FileHash -LiteralPath (Join-Path $tauntRoot $path)).Hash }
$tauntInstalled = @{}
foreach ($path in @('BepInEx/plugins/maschine-ArenaMode.Client.dll','SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll',
    'BepInEx/plugins/maschine-Redeploy.Core.dll','EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll')) {
    $tauntInstalled[$path] = (Get-FileHash -LiteralPath (Join-Path $SptPath $path)).Hash
}
function Run-Taunt([string]$name, [string]$source, [string]$suite = 'all', [string]$failure = '', [bool]$audioFault = $false) {
    $dir = Join-Path $tauntScratch $name; [void][IO.Directory]::CreateDirectory($dir)
    $matchSlice = Join-Path $dir 'ActualMatchText.cs'
    Export-ActualMatchText $tauntMatch $matchSlice $audioFault
    $build = & dotnet build (Join-Path $PSScriptRoot 'SelfGrenadeTauntChecks.csproj') -c Release --nologo "-p:TauntSourceRoot=$source" "-p:MatchTextSource=$matchSlice" "-p:BaseIntermediateOutputPath=$dir/obj/" "-p:OutputPath=$dir/out/" 2>&1 | Out-String
    $code = $LASTEXITCODE; [IO.File]::WriteAllText((Join-Path $dir 'build.log'), $build)
    if ($code -ne 0) { throw "$name must compile before behavioral verification: $build" }
    $run = & dotnet (Join-Path $dir 'out/SelfGrenadeTauntChecks.dll') $suite 2>&1 | Out-String
    $code = $LASTEXITCODE; [IO.File]::WriteAllText((Join-Path $dir 'result.log'), $run)
    if ($failure) {
        if ($code -eq 0 -or !$run.Contains('FAILED: ' + $failure)) { throw "$name did not reject intended behavior: $run" }
        Write-Output "Rejected $name at: $failure"
    } elseif ($code -ne 0 -or !$run.Contains('SELF_GRENADE_PRODUCTION_PASSED:')) { throw "$name failed: $run" }
    else { Write-Output $run.Trim() }
}
try {
    $native = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Verify-Native.ps1') -SptPath $SptPath 2>&1 | Out-String
    $nativeCode = $LASTEXITCODE; [IO.File]::WriteAllText((Join-Path $tauntScratch 'native.log'), $native)
    if ($nativeCode -ne 0 -or !$native.Contains('SELF_GRENADE_NATIVE_CONTRACT_PASSED:')) { throw $native }
    Write-Output $native.Trim()
    Run-Taunt 'production' $tauntSource
    $faults = @(
        @{ Name='wrong-source-admitted'; Suite='sources'; Find='&& string.Equals(info.SourceId, expected, StringComparison.Ordinal)'; Replace='&& true'; Failure='wrong blast or fragment source rejected' },
        @{ Name='old-body-admitted'; Suite='identity'; Find='|| origin.BodyId != ArenaHitHistory.BodyId(local)'; Replace=''; Failure='old throwing body cannot taunt same-profile respawn successor' }
    )
    foreach ($fault in $faults) {
        $faultRoot = Join-Path $tauntScratch ($fault.Name + '/source'); [void][IO.Directory]::CreateDirectory($faultRoot)
        foreach ($file in $tauntFiles) { Copy-Item -LiteralPath (Join-Path $tauntSource $file) -Destination (Join-Path $faultRoot $file) }
        $path = Join-Path $faultRoot 'ArenaSelfGrenadeTaunt.cs'; $source = [IO.File]::ReadAllText($path)
        if ($source.Split(@($fault.Find), [StringSplitOptions]::None).Length -ne 2) { throw "Ambiguous fault anchor: $($fault.Name)" }
        [IO.File]::WriteAllText($path, $source.Replace($fault.Find, $fault.Replace))
        Run-Taunt $fault.Name $faultRoot $fault.Suite $fault.Failure
    }
    Run-Taunt 'text-routed-to-combat-audio' $tauntSource 'positive' 'proven own grenade emits exactly the HUD text' $true
    Run-Taunt 'production-final' $tauntSource
    foreach ($path in $tauntBound) {
        if ((Get-FileHash -LiteralPath (Join-Path $tauntRoot $path)).Hash -ne $tauntHashes[$path]) { throw "Bound source changed; rerun required: $path" }
    }
} finally {
    foreach ($path in $tauntInstalled.Keys) {
        if ((Get-FileHash -LiteralPath (Join-Path $SptPath $path)).Hash -ne $tauntInstalled[$path]) { throw "Installed binary changed: $path" }
    }
}
[ordered]@{ sourceHashes=$tauntHashes; installedHashes=$tauntInstalled; sourcesUnchanged=$true; installedUnchanged=$true;
    compiledFaultCount=3; completedUtc=[DateTime]::UtcNow.ToString('o'); evidenceDirectory=$tauntScratch;
    limitation='Full production taunt and actual extracted ArenaMatch text dispatch use explicit native boundary doubles. Metadata/source checks are not Unity/Harmony runtime or deployment proof.'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $tauntScratch 'manifest.json')
Write-Output "SELF GRENADE TAUNT CHECKS PASSED; three compiled faults rejected; source and installed binaries unchanged; evidence: $tauntScratch"
