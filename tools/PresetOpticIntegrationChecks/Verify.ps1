param([string]$SptPath='D:\SPT41', [switch]$CaptureBaselineOnly)
$ErrorActionPreference='Stop'
$presetRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $presetRoot 'tools/WeaponLaserIntegrationChecks/ExternalFiles.ps1') -SptPath $SptPath
$presetBaseline=Join-Path $presetRoot '.unlazy/preset-optics/external-before.json'
if($CaptureBaselineOnly){
    if(Test-Path -LiteralPath $presetBaseline){throw 'Do not overwrite the task baseline.'}
    Get-ArenaExternalHashes | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $presetBaseline -Encoding utf8
    Write-Output 'Preset/optic external baseline captured; no build or deployment.'
    exit 0
}
if(!(Test-Path -LiteralPath $presetBaseline)){throw 'Capture the external baseline before running this integration oracle.'}
$before=Get-Content -LiteralPath $presetBaseline -Raw | ConvertFrom-Json -AsHashtable
& dotnet test (Join-Path $presetRoot 'ArenaMode.Tests/ArenaMode.Tests.csproj') -c Release --no-restore --verbosity quiet "-p:SPTPath=$SptPath" "-p:SPTUserPath=$(Join-Path $SptPath 'SPT_Runtime')"
if($LASTEXITCODE -ne 0){throw 'Combined server/catalog/lifecycle tests failed.'}
# All builds deploy; this order leaves the final linked Redeploy.Core Release in place.
foreach($project in @('ArenaMode.Server/ArenaMode.Server.csproj','ArenaMode.BigBrainCompat/ArenaMode.BigBrainCompat.csproj','ArenaMode.Client/ArenaMode.Client.csproj')){
    & dotnet build (Join-Path $presetRoot $project) -c Release --no-restore "-p:SPTPath=$SptPath" "-p:SPTUserPath=$(Join-Path $SptPath 'SPT_Runtime')"
    if($LASTEXITCODE -ne 0){throw "Final Release build failed: $project"}
}
foreach($pair in @(
    @('ArenaMode.Client/bin/Release/maschine-ArenaMode.Client.dll','BepInEx/plugins/maschine-ArenaMode.Client.dll'),
    @('ArenaMode.BigBrainCompat/bin/Release/maschine-ArenaMode.BigBrainCompat.dll','BepInEx/plugins/maschine-ArenaMode.BigBrainCompat.dll'),
    @('build/server/Release/maschine-ArenaMode.Server/maschine-ArenaMode.Server.dll','SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll'),
    @('../Redeploy/Redeploy.Core/bin/Release/maschine-Redeploy.Core.dll','BepInEx/plugins/maschine-Redeploy.Core.dll')
)){
    $built=(Get-FileHash -LiteralPath (Join-Path $presetRoot $pair[0])).Hash
    $live=(Get-FileHash -LiteralPath (Join-Path $SptPath $pair[1])).Hash
    if($built -ne $live){throw "Built/deployed hash mismatch: $($pair[1])"}
    Write-Output "$($pair[1]): $live"
}
$after=Get-ArenaExternalHashes
if($before.Count -ne $after.Count){throw 'External/config/catalog file set changed.'}
foreach($path in $before.Keys){if($before[$path] -ne $after[$path]){throw "External file changed: $path"}}
Write-Output "External/config/catalog files unchanged: $($before.Count)"
Write-Output 'ARENA_PRESET_OPTIC_INTEGRATION_PASSED'
