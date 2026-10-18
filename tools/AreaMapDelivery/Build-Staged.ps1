param([string]$SptRoot='D:\SPT41')
. (Join-Path $PSScriptRoot '../Read-ArenaInstalledSnapshot.ps1')
$arenaSnapshotRoot = $SptRoot
$arenaLayoutSnapshot = Read-ArenaInstalledSnapshot $arenaSnapshotRoot
try {
$ErrorActionPreference = 'Stop'
$mapRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$mapGame = (Resolve-Path -LiteralPath $SptRoot).Path
$mapStage = Join-Path $mapRepo ('artifacts\area-map-delivery\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $mapStage -Force | Out-Null
$mapDlls = @('BepInEx\plugins\maschine-ArenaMode\maschine-ArenaMode.Client.dll',
    'SPT_Runtime\user\mods\ArenaMode\maschine-ArenaMode.Server.dll',
    'BepInEx\plugins\maschine-Redeploy.Core.dll', 'BepInEx\plugins\maschine-Redeploy.Client.dll')
function Read-InstalledMaps {
    $paths = @($mapDlls | ForEach-Object { Join-Path $mapGame $_ })
    foreach ($folder in @('BepInEx/plugins/ArenaMode', 'BepInEx/plugins/maschine-ArenaMode')) {
        $assetFolder = Join-Path $mapGame $folder
        if (Test-Path -LiteralPath $assetFolder) {
            $paths += @(Get-ChildItem -LiteralPath $assetFolder -File -Recurse |
                Where-Object { $_.FullName -notmatch '\\AreaDrafts\\' } | ForEach-Object FullName)
        }
    }
    $inventory = @{}
    foreach ($path in $paths) { $inventory[$path] = (Get-ArenaObservedFileHash -LiteralPath $path).Hash }
    return $inventory
}
$mapBefore = Read-InstalledMaps
$mapBefore | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $mapStage 'installed-before.json')
try {
    # This existing helper removes the Redeploy project reference, overrides
    # client/resource deploy targets and globally redirects SPT paths to scratch.
    $clientOutput = @(& (Join-Path $mapRepo 'tools\DoubleJumpChecks\Build-Staged.ps1') -SptRoot $mapGame |
        Tee-Object -FilePath (Join-Path $mapStage 'client.log'))
    $clientLines = @($clientOutput | Where-Object { "$_".StartsWith('Staged client: ') })
    if ($clientLines.Count -ne 1) { throw 'Staged helper did not identify exactly one client artifact.' }
    $clientDll = "$($clientLines[0])".Substring('Staged client: '.Length)
    $serverOutput = Join-Path $mapStage 'server'
    # The server deployment destination is a non-live nonexistent tree; no
    # target may follow the real SPTUserPath. Outputs also remain in this stage.
    & dotnet build (Join-Path $mapRepo 'ArenaMode.Server\ArenaMode.Server.csproj') -c Release --nologo `
        "-p:SPTUserPath=$mapStage\not-installed" "-p:OutputPath=$serverOutput\" 2>&1 |
        Tee-Object -FilePath (Join-Path $mapStage 'server.log') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Staged server build failed.' }
    $serverDll = Join-Path $serverOutput 'maschine-ArenaMode.Server.dll'
    if (-not (Test-Path -LiteralPath $serverDll)) { throw 'Staged server artifact missing.' }
    $contentRoot = Join-Path ([IO.Path]::GetDirectoryName($clientDll)) 'maschine-ArenaMode\docs\licenses\map-previews'
    & (Join-Path $PSScriptRoot 'Verify-Package.ps1') -ClientDll $clientDll -ContentRoot $contentRoot
    $mapManifest = [ordered]@{
        SourceCommit = (& git -C $mapRepo rev-parse HEAD)
        SourceChanges = @(& git -C $mapRepo -c core.safecrlf=false status --short)
        Client = @{ Path=$clientDll; SHA256=(Get-ArenaObservedFileHash -LiteralPath $clientDll).Hash }
        Server = @{ Path=$serverDll; SHA256=(Get-ArenaObservedFileHash -LiteralPath $serverDll).Hash }
        MapContent = $contentRoot
        InstalledFilesChecked = $mapBefore.Count
        Deployed = $false
        LiveGpuTested = $false
    }
}
finally {
    $mapAfter = Read-InstalledMaps
    $mapAfter | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $mapStage 'installed-after.json')
    $changes = @($mapBefore.Keys | Where-Object { $mapBefore[$_] -ne $mapAfter[$_] })
    $changes += @($mapAfter.Keys | Where-Object { -not $mapBefore.ContainsKey($_) })
    if ($changes.Count -gt 0) { throw ('Installed artifacts changed during staging: ' + ($changes -join ', ')) }
}
$mapManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $mapStage 'manifest.json')
Write-Output "Delivery manifest: $mapStage\manifest.json"
Write-Output 'AREA MAP STAGED DELIVERY PASSED'

} finally {
    Assert-ArenaInstalledSnapshotUnchanged $arenaLayoutSnapshot $arenaSnapshotRoot
}
