#Requires -Version 7.0
param([ValidateSet('Routes','Limits','Factory','Menus','Regression','Final')][string]$Stage = 'Regression')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence = Join-Path $repo "artifacts/domination-options-routes-20261027/$Stage"
[void][IO.Directory]::CreateDirectory($evidence)
$harnesses = switch ($Stage) {
    'Routes' { 'DominationBotChecks' }
    'Limits' { 'TdmMatchMenuChecks,TeamDeathmatchRuntimeChecks,CompactModeUxChecks' }
    'Factory' { 'DominationRuntimeChecks,DominationPlacementChecks' }
    'Menus' { 'NativeAreaHookChecks,CompactSessionUxChecks,NativeMenuChecks' }
    'Regression' { 'DeathmatchMenuChecks,DominationPolicyChecks,DominationHudChecks' }
    'Final' { 'TeamDeathmatchRuntimeChecks,DominationHudChecks' }
}
$arguments = @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Run-ArenaVerification.ps1'),'-Harness',$harnesses)
if ($Stage -ne 'Regression') { $arguments += '-SkipUnitTests' }
& pwsh @arguments *> (Join-Path $evidence 'checks.log')
$result = $LASTEXITCODE
Get-Content -LiteralPath (Join-Path $evidence 'checks.log') -Tail 12
if ($result -ne 0) { throw "Domination $Stage checks failed; see $evidence/checks.log" }
foreach ($harness in $harnesses.Split(',')) {
    Copy-Item -LiteralPath (Join-Path $repo "artifacts/run-arena-verification/$harness.log") -Destination $evidence
}
if ($Stage -in @('Regression','Final')) {
    Copy-Item -LiteralPath (Join-Path $repo 'artifacts/run-arena-verification/run-arena-verification.trx') -Destination $evidence
    . (Join-Path $PSScriptRoot 'Read-ArenaInstalledSnapshot.ps1')
    $before = Read-ArenaInstalledSnapshot 'D:\SPT41'
    $sentinel = Join-Path $evidence 'must-not-exist'
    if (Test-Path -LiteralPath $sentinel) { throw 'No-deploy sentinel exists.' }
    foreach ($project in @('ArenaMode.Client','ArenaMode.BigBrainCompat')) {
        & dotnet build (Join-Path $repo "$project/$project.csproj") -c Release --nologo `
            "-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $PSScriptRoot 'AirPitChecks/NoDeploy.targets')" `
            "-p:SPTUserMods=$sentinel" *> (Join-Path $evidence "$project-build.log")
        if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $evidence "$project-build.log") -Tail 25; throw "$project build failed" }
        Assert-ArenaInstalledSnapshotUnchanged $before 'D:\SPT41'
    }
}
Write-Output "DOMINATION_OPTIONS_ROUTES_PASSED $Stage"
