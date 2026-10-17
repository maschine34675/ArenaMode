param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Rules', 'Server', 'Integration', 'Regression', 'Navigation', 'Build')]
    [string]$Stage,
    [string]$SptRoot = 'D:\SPT41'
)
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaSpt = (Resolve-Path -LiteralPath $SptRoot).Path
$arenaRuntime = Join-Path $arenaSpt 'SPT_Runtime'
$arenaResults = Join-Path $arenaRepo 'artifacts\area-checks'
New-Item -ItemType Directory -Path $arenaResults -Force | Out-Null
$arenaBuildArgs = @('-c', 'Release', "-p:SPTPath=$arenaSpt", "-p:SPTUserPath=$arenaRuntime", '--nologo', '-v:minimal')
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
        '--logger', "trx;LogFileName=area-$Stage.trx", '--results-directory', $arenaResults)
    if ($Filter) { $arenaTestArgs += @('--filter', $Filter) }
    if ($NoBuild) { $arenaTestArgs += '--no-build' }
    Invoke-ArenaDotnet $arenaTestArgs
    [xml]$arenaTrx = Get-Content -Raw -LiteralPath (Join-Path $arenaResults "area-$Stage.trx")
    $arenaCounts = $arenaTrx.TestRun.ResultSummary.Counters
    # xUnit dynamic skips can appear as a NotExecuted result while VSTest's
    # aggregate notExecuted counter remains zero. Require every result to pass.
    $arenaUnpassed = @($arenaTrx.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -ne 'Passed' })
    if ([int]$arenaCounts.total -le 0 -or [int]$arenaCounts.passed -ne [int]$arenaCounts.total -or $arenaUnpassed.Count -ne 0) {
        throw "Expected executed, passing, unskipped tests; counters: $($arenaCounts.OuterXml)"
    }
    Write-Output "Area test evidence: total=$($arenaCounts.total), passed=$($arenaCounts.passed), unpassed=$($arenaUnpassed.Count)"
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
