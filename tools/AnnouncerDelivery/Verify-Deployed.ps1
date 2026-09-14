param([Parameter(Mandatory=$true)][string]$Manifest)
$ErrorActionPreference='Stop'
$deliveryRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$delivery = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json -AsHashtable
if (!$delivery.Deployed -or $delivery.Files.Count -ne 4) { throw 'Expected client and three package documents' }
$deliveryGame = (Resolve-Path -LiteralPath $delivery.SptRoot).Path
$expectedTargets = @('BepInEx/plugins/maschine-ArenaMode.Client.dll',
    'BepInEx/plugins/ArenaMode/ANNOUNCER-PACK.md', 'BepInEx/plugins/ArenaMode/ANNOUNCER-SOURCES.json',
    'BepInEx/plugins/ArenaMode/AUDIO-LICENSES.md') | ForEach-Object { Join-Path $deliveryGame $_ }
$actualTargets = @($delivery.Files | ForEach-Object { [IO.Path]::GetFullPath($_.Target) })
if (@($actualTargets | Select-Object -Unique).Count -ne 4 -or
    @($expectedTargets | Where-Object { $_ -notin $actualTargets }).Count -ne 0) { throw 'Deployment target set exceeds the allowed client and three documents' }
$expectedDependencies = @('BepInEx/plugins/maschine-Redeploy.Core.dll',
    'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll') | ForEach-Object { Join-Path $deliveryGame $_ }
if ($delivery.InstalledUnchanged.Count -ne 2 -or
    @($expectedDependencies | Where-Object { !$delivery.InstalledUnchanged.ContainsKey($_) }).Count -ne 0) { throw 'Deployment must preserve exact Core and server dependencies' }
$expectedExternal = @(
    [IO.Path]::GetFullPath((Join-Path $deliveryRepo '../Redeploy/Redeploy.Core/CoreVersion.props')),
    [IO.Path]::GetFullPath((Join-Path $deliveryRepo '../Redeploy/drivers/CoreVersionGate.cs')))
if ($delivery.ExternalBuildSources.Count -ne 2) { throw 'Deployment lacks the two external compiler input bindings' }
foreach ($path in $expectedExternal) {
    if (!$delivery.ExternalBuildSources.ContainsKey($path) -or
        (Get-FileHash -LiteralPath $path).Hash -ne $delivery.ExternalBuildSources[$path]) { throw "External compiler input drift: $path" }
}
foreach ($file in $delivery.Files) {
    if ((Get-FileHash -LiteralPath $file.Target).Hash -ne $file.SHA256) { throw "Deployed file mismatch: $($file.Target)" }
}
foreach ($path in $delivery.InstalledUnchanged.Keys) {
    if ((Get-FileHash -LiteralPath $path).Hash -ne $delivery.InstalledUnchanged[$path]) { throw "Preserved dependency mismatch: $path" }
}
$after = Get-Content -LiteralPath (Join-Path $delivery.EvidenceDirectory 'after.json') -Raw | ConvertFrom-Json -AsHashtable
$before = Get-Content -LiteralPath (Join-Path $delivery.EvidenceDirectory 'before.json') -Raw | ConvertFrom-Json -AsHashtable
if ($after.Count -ne $before.Count -or $after.Count -ne $delivery.InstalledUnchangedCount + 4) { throw 'Deployment inventory count mismatch' }
$inventoryPaths = @(Get-ChildItem -LiteralPath (Join-Path $deliveryGame 'BepInEx/plugins') -Recurse -File -Filter '*.dll' |
    Where-Object { $_.FullName -notmatch '\\AreaDrafts\\' } | ForEach-Object FullName)
$inventoryPaths += @(Get-ChildItem -LiteralPath (Join-Path $deliveryGame 'BepInEx/plugins/ArenaMode') -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\AreaDrafts\\' } | ForEach-Object FullName)
$inventoryPaths += Join-Path $deliveryGame 'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll'
$current = @{}
foreach ($path in $inventoryPaths) { $current[$path] = (Get-FileHash -LiteralPath $path).Hash }
if ($current.Count -ne $after.Count) { throw 'Live installed inventory path count changed' }
foreach ($path in $after.Keys) {
    if (!$before.ContainsKey($path) -or !$current.ContainsKey($path)) { throw "Installed inventory path set changed: $path" }
    if ($path -notin $expectedTargets -and $before[$path] -ne $after[$path]) { throw "Unrelated file changed during deployment: $path" }
    if ($current[$path] -ne $after[$path]) { throw "Installed inventory drift: $path" }
}
foreach ($file in $delivery.Files) {
    if ((Get-FileHash -LiteralPath $file.Backup).Hash -ne $before[$file.Target]) { throw 'Deployment backup mismatch' }
}
$client = @($delivery.Files | Where-Object { $_.Target.EndsWith('maschine-ArenaMode.Client.dll') })
if ($client.Count -ne 1 -or [Diagnostics.FileVersionInfo]::GetVersionInfo($client[0].Target).ProductVersion -ne ('0.1.0+'+$delivery.SourceCommit)) {
    throw 'Deployed client version mismatch'
}
Write-Output 'ANNOUNCER DEPLOYED BINDING PASSED'
