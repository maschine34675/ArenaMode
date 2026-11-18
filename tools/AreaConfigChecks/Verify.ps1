param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$areaConfigRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$areaConfigProject = Join-Path $PSScriptRoot 'AreaConfigChecks.csproj'
$areaConfigRun = Join-Path $PSScriptRoot ('artifacts\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $areaConfigRun -Force | Out-Null
function Build-AreaConfigHarness([string]$Name,[string]$SettingsSource,[switch]$Historical) {
    $areaConfigDestination = Join-Path $areaConfigRun $Name
    $areaConfigArguments = @('build',$areaConfigProject,'-c','Release','--nologo','-v:q',"-p:SptRoot=$SptRoot",
        "-p:OutputPath=$areaConfigDestination\out\","-p:BaseIntermediateOutputPath=$areaConfigDestination\obj\")
    if ($SettingsSource) { $areaConfigArguments += "-p:SettingsSource=$SettingsSource" }
    if ($Historical) { $areaConfigArguments += '-p:HistoricalSettings=true' }
    & dotnet @areaConfigArguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Standalone config harness failed to compile: $Name" }
    return Join-Path $areaConfigDestination 'out\AreaConfigChecks.dll'
}
Start-Transcript -Path (Join-Path $areaConfigRun 'verification.log') | Out-Null
try {
    $areaConfigSettingsPath = Join-Path $areaConfigRepo 'ArenaMode.Client\Config\Settings.cs'
    $areaConfigBefore = (Get-FileHash -LiteralPath $areaConfigSettingsPath -Algorithm SHA256).Hash
    $areaConfigBaseline = Build-AreaConfigHarness 'current' ''
    & dotnet $areaConfigBaseline
    if ($LASTEXITCODE -ne 0) { throw 'Production config checks failed.' }

    # Mutants of the difficulty migration against the real-ConfigFile legacy cases: one that never
    # runs, one that never records its revision and one that runs on every start. Each has to be
    # rejected by the case that names its defect.
    $areaConfigMigrationSource = (Get-Content -LiteralPath $areaConfigSettingsPath -Raw).Replace("`r`n","`n")
    $areaConfigGuard = 'if (difficultyRevision.Value < ArenaDifficultyMigration.Revision)'
    foreach ($areaConfigMutant in @(
        @{Name='no-migration';Find=$areaConfigGuard;Replace='if (false)';Marker='DIFFICULTY-MIGRATION'},
        @{Name='revision-not-recorded';Find='difficultyRevision.Value = ArenaDifficultyMigration.Revision;';Replace='{ }';Marker='DIFFICULTY-MIGRATION-REVISION'},
        @{Name='migration-every-start';Find=$areaConfigGuard;Replace='if (true)';Marker='DIFFICULTY-MIGRATION-HANDSET'})) {
        if ($areaConfigMigrationSource.Split([string[]]@($areaConfigMutant.Find),[StringSplitOptions]::None).Count -ne 2) { throw "Migration mutant anchor missing or ambiguous: $($areaConfigMutant.Name)" }
        $areaConfigMutantPath = Join-Path $areaConfigRun ('Settings-' + $areaConfigMutant.Name + '.cs')
        [IO.File]::WriteAllText($areaConfigMutantPath,$areaConfigMigrationSource.Replace($areaConfigMutant.Find,$areaConfigMutant.Replace))
        $areaConfigMutantBuild = Build-AreaConfigHarness ('mutant-' + $areaConfigMutant.Name) $areaConfigMutantPath
        $areaConfigMutantOutput = & dotnet $areaConfigMutantBuild 2>&1
        if ($LASTEXITCODE -eq 0 -or ($areaConfigMutantOutput -join "`n") -notmatch ('AREA CONFIG REJECTED: ' + $areaConfigMutant.Marker + ':')) {
            throw "Migration mutant was not rejected by its legacy-difficulty case: $($areaConfigMutant.Name)"
        }
        Write-Output "Mutant rejected: difficulty migration $($areaConfigMutant.Name) [$($areaConfigMutant.Marker)]"
    }

    # Compile the complete actual historical source, not a hand-built imitation
    # of its missing entries. The failed GPU report names this repository commit.
    $areaConfigHistoricalRevision = '588090fe2c7cf6f391ee02e720d57a8bf62cfa43'
    $areaConfigHistoricalText = & git -C $areaConfigRepo show ($areaConfigHistoricalRevision + ':ArenaMode.Client/Config/Settings.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read the historical GPU-report Settings source.' }
    $areaConfigHistoricalPath = Join-Path $areaConfigRun 'Settings-588090f.cs'
    [IO.File]::WriteAllText($areaConfigHistoricalPath,($areaConfigHistoricalText -join "`n")+"`n")
    $areaConfigLegacy = Build-AreaConfigHarness 'historical-588090f' $areaConfigHistoricalPath -Historical
    foreach ($areaConfigProbe in @(@{Name='binding';Marker='ACCEPTABLE-AREA'},@{Name='selection';Marker='SELECT-AREA'})) {
        $areaConfigOutput = & dotnet $areaConfigLegacy $areaConfigProbe.Name 2>&1
        $areaConfigExit = $LASTEXITCODE
        $areaConfigOutput | Set-Content -LiteralPath (Join-Path $areaConfigRun ('historical-' + $areaConfigProbe.Name + '.log'))
        if ($areaConfigExit -eq 0 -or ($areaConfigOutput -join "`n") -notmatch ('AREA CONFIG REJECTED: ' + $areaConfigProbe.Marker + ':')) {
            throw "Historical source did not reproduce its specific missing-East defect: $($areaConfigProbe.Name)"
        }
        Write-Output "Historical 588090f defect reproduced: $($areaConfigProbe.Name) [$($areaConfigProbe.Marker)]"
        & dotnet $areaConfigBaseline $areaConfigProbe.Name
        if ($LASTEXITCODE -ne 0) { throw "Fixed source did not pass the same focused probe: $($areaConfigProbe.Name)" }
    }
    & dotnet $areaConfigBaseline
    if ($LASTEXITCODE -ne 0) { throw 'Final clean config verification failed.' }
    $areaConfigAfter = (Get-FileHash -LiteralPath $areaConfigSettingsPath -Algorithm SHA256).Hash
    if ($areaConfigBefore -ne $areaConfigAfter) { throw 'Settings source changed during verification; rerun against final code.' }
    [ordered]@{
        settingsSourceSha256=$areaConfigAfter
        historicalRevision=$areaConfigHistoricalRevision
        historicalSettingsSha256=(Get-FileHash -LiteralPath $areaConfigHistoricalPath -Algorithm SHA256).Hash
        bepinexSha256=(Get-FileHash -LiteralPath (Join-Path $SptRoot 'BepInEx\core\BepInEx.dll') -Algorithm SHA256).Hash
        configImplementation='Actual installed BepInEx.dll; no configuration doubles'
        historicalExpectedFailures=@('ACCEPTABLE-AREA','SELECT-AREA')
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $areaConfigRun 'receipt.json')
    Write-Output "Evidence: $areaConfigRun"
    Write-Output 'AREA CONFIG CHECKS PASSED'
}
finally { Stop-Transcript | Out-Null }
