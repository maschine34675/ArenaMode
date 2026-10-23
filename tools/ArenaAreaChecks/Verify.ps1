param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Rules', 'Server', 'Integration', 'Regression', 'Navigation', 'Build')]
    [string]$Stage,
    [string]$SptRoot = 'D:\SPT41'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Assert-ArenaTestRun.ps1')
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaSpt = (Resolve-Path -LiteralPath $SptRoot).Path
$arenaRuntime = Join-Path $arenaSpt 'SPT_Runtime'
$arenaResults = Join-Path $arenaRepo 'artifacts\area-checks'
New-Item -ItemType Directory -Path $arenaResults -Force | Out-Null
$arenaBuildArgs = @('-c', 'Release', "-p:SPTPath=$arenaSpt", "-p:SPTUserPath=$arenaRuntime", '--nologo', '-v:minimal')
# SPTUserMods is a global MSBuild property; the server's deploy target copies into it, so a path that
# must not exist keeps the test runs from writing the live user/mods (the test project builds the
# server). The Build stage's own `dotnet build` calls deploy on purpose and compare the hashes.
$arenaNoDeploy = Join-Path $arenaResults 'must-not-exist-no-deploy'
if (Test-Path -LiteralPath $arenaNoDeploy) { throw 'The no-deploy sentinel must not exist.' }
$env:SPT_DATABASE = Join-Path $arenaRuntime 'SPT_Data\database'
$env:SPT_RUNTIME = $arenaRuntime

function Get-ArenaHash([string]$Path) {
    # Avoid PowerShell module auto-loading differences in a child process
    # launched from PowerShell 7 through Windows PowerShell 5.1.
    $arenaStream = [IO.File]::OpenRead($Path)
    $arenaSha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($arenaSha.ComputeHash($arenaStream)).Replace('-', '') }
    finally { $arenaSha.Dispose(); $arenaStream.Dispose() }
}
function Invoke-ArenaDotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE): $($Arguments[0])" }
}
function Test-ArenaServer([string]$Filter, [switch]$NoBuild) {
    $arenaTestArgs = @('test', 'ArenaMode.Tests\ArenaMode.Tests.csproj') + $arenaBuildArgs + @(
        "-p:SPTUserMods=$arenaNoDeploy", '--logger', "trx;LogFileName=area-$Stage.trx", '--results-directory', $arenaResults)
    if ($Filter) { $arenaTestArgs += @('--filter', $Filter) }
    if ($NoBuild) { $arenaTestArgs += '--no-build' }
    Invoke-ArenaDotnet $arenaTestArgs
    # Every result must pass; only the known environment-gated skips (APBS root, symlink
    # privilege) are tolerated, see tools/Assert-ArenaTestRun.ps1. Before 2026-10-23 any skip
    # failed this stage, so the Build stage was red since the APBS SkippableFact of 503540e.
    $arenaRun = Assert-ArenaTestRun -TrxPath (Join-Path $arenaResults "area-$Stage.trx")
    Write-Output "Area test evidence: total=$($arenaRun.Total), passed=$($arenaRun.Passed), skipped=$($arenaRun.Skipped) ($($arenaRun.SkippedNames -join ', '))"
}

Push-Location $arenaRepo
Start-Transcript -Path (Join-Path $arenaResults "stage-$Stage.log") -Force | Out-Null
try {
    switch ($Stage) {
        'Rules' { Test-ArenaServer 'FullyQualifiedName~ArenaAreaGeometryTests' }
        'Server' { Test-ArenaServer 'FullyQualifiedName~ArenaAreaServerTests' }
        'Integration' {
            Test-ArenaServer 'FullyQualifiedName~ArenaAreaServerTests'
            Invoke-ArenaDotnet @('run', '--project', 'tools\ArenaAreaChecks\ClientContracts\ClientContracts.csproj', '-c', 'Release')
            & (Join-Path $PSScriptRoot 'NegativeControls.ps1')
        }
        'Navigation' {
            Invoke-ArenaDotnet (@('build', 'ArenaMode.Client\ArenaMode.Client.csproj') + $arenaBuildArgs)
        }
        'Regression' {
            foreach ($arenaHarness in @('NativeMenuChecks', 'LabsBotSpawnChecks', 'TeamDeathmatchRuntimeChecks', 'TeamGiveUpChecks')) {
                Invoke-ArenaDotnet @('run', '--project', "tools\$arenaHarness\$arenaHarness.csproj", '-c', 'Release', "-p:SPTPath=$arenaSpt")
            }
        }
        'Build' {
            Invoke-ArenaDotnet (@('build', 'ArenaMode.sln') + $arenaBuildArgs)
            Test-ArenaServer '' -NoBuild
            # A solution build can choose Debug for the external Redeploy reference.
            # Build the actual Release client + companion last, serially.
            Invoke-ArenaDotnet (@('build', 'ArenaMode.BigBrainCompat\ArenaMode.BigBrainCompat.csproj') + $arenaBuildArgs)
            $arenaPairs = @(
                @('ArenaMode.Client\bin\Release\maschine-ArenaMode.Client.dll', (Join-Path $arenaSpt 'BepInEx\plugins\maschine-ArenaMode\maschine-ArenaMode.Client.dll')),
                @('ArenaMode.BigBrainCompat\bin\Release\maschine-ArenaMode.BigBrainCompat.dll', (Join-Path $arenaSpt 'BepInEx\plugins\maschine-ArenaMode\maschine-ArenaMode.BigBrainCompat.dll')),
                @('build\server\Release\maschine-ArenaMode.Server\maschine-ArenaMode.Server.dll', (Join-Path $arenaRuntime 'user\mods\ArenaMode\maschine-ArenaMode.Server.dll')),
                @('..\Redeploy\Redeploy.Core\bin\Release\maschine-Redeploy.Core.dll', (Join-Path $arenaSpt 'BepInEx\plugins\maschine-Redeploy.Core.dll'))
            )
            $arenaHashes = foreach ($arenaPair in $arenaPairs) {
                $arenaBuilt = Get-ArenaHash (Join-Path $arenaRepo $arenaPair[0])
                $arenaInstalled = Get-ArenaHash $arenaPair[1]
                if ($arenaBuilt -ne $arenaInstalled) { throw "Deployment mismatch: $($arenaPair[0])" }
                [pscustomobject]@{ Name = [IO.Path]::GetFileName($arenaPair[0]); SHA256 = $arenaBuilt }
            }
            $arenaHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $arenaResults 'deployment.json')
            $arenaHashes | Format-Table -AutoSize
        }
    }
    Write-Output ('ARENA_AREA_' + $Stage.ToUpperInvariant() + '_PASSED')
}
finally { Stop-Transcript | Out-Null; Pop-Location }
