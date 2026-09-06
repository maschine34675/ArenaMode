param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$previewRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
# This focused client change needs no server, companion or external-mod rebuild.
# Client is deliberately last (and only): its project deploys Redeploy.Core too.
& dotnet build (Join-Path $previewRepo 'ArenaMode.Client/ArenaMode.Client.csproj') -c Release --no-restore "-p:SPTPath=$SptPath"
if ($LASTEXITCODE -ne 0) { throw 'Preview diagnostic client build failed' }
foreach ($pair in @(
    @('ArenaMode.Client/bin/Release/maschine-ArenaMode.Client.dll', 'BepInEx/plugins/maschine-ArenaMode.Client.dll'),
    @('../Redeploy/Redeploy.Core/bin/Release/maschine-Redeploy.Core.dll', 'BepInEx/plugins/maschine-Redeploy.Core.dll')
)) {
    $built = (Get-FileHash -LiteralPath (Join-Path $previewRepo $pair[0]) -Algorithm SHA256).Hash
    $deployed = (Get-FileHash -LiteralPath (Join-Path $SptPath $pair[1]) -Algorithm SHA256).Hash
    if ($built -ne $deployed) { throw "Preview diagnostic deployment mismatch: $($pair[1])" }
    Write-Output "$($pair[1]): $deployed"
}
Write-Output 'ARENA_PREVIEW_DIAGNOSTICS_DEPLOYED'
