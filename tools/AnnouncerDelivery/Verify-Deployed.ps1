#Requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$Manifest)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Inventory.ps1')
$deliveryRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$delivery = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json -AsHashtable
if (!$delivery.Deployed -or $delivery.SchemaVersion -ne 2 -or $delivery.Files.Count -lt 4) { throw 'Expected current shared-payload deployment manifest' }
$deliveryGame = (Resolve-Path -LiteralPath $delivery.SptRoot).Path
$assetRoot = [IO.Path]::GetFullPath((Join-Path $deliveryGame 'BepInEx/plugins/maschine-ArenaMode')) + [IO.Path]::DirectorySeparatorChar
$targets = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($file in $delivery.Files) {
    $target = [IO.Path]::GetFullPath($file.Target)
    if (!$target.StartsWith($assetRoot, [StringComparison]::OrdinalIgnoreCase) -or !$targets.Add($target)) {
        throw 'Duplicated or noncanonical payload destination'
    }
    if ((Get-FileHash -LiteralPath $target).Hash -ne $file.SHA256) { throw "Deployed file mismatch: $target" }
}
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
foreach ($path in $delivery.InstalledUnchanged.Keys) {
    if ((Get-FileHash -LiteralPath $path).Hash -ne $delivery.InstalledUnchanged[$path]) { throw "Preserved dependency mismatch: $path" }
}
$before = Get-Content -LiteralPath (Join-Path $delivery.EvidenceDirectory 'before.json') -Raw | ConvertFrom-Json -AsHashtable
$after = Get-Content -LiteralPath (Join-Path $delivery.EvidenceDirectory 'after.json') -Raw | ConvertFrom-Json -AsHashtable
$expected = ConvertTo-AnnouncerCanonicalInventory $before $deliveryGame
$expected.Remove((Join-Path $assetRoot 'maschine-ArenaMode.Client.pdb'))
foreach ($file in $delivery.Files) { $expected[$file.Target] = $file.SHA256 }
Assert-AnnouncerInventory $expected $after
Assert-AnnouncerInventory $after (Read-AnnouncerDeliveryInventory $deliveryGame)
$client = @($delivery.Files | Where-Object { $_.Target.EndsWith('maschine-ArenaMode.Client.dll') })
if ($client.Count -ne 1 -or [Diagnostics.FileVersionInfo]::GetVersionInfo($client[0].Target).ProductVersion -ne ('0.1.0+'+$delivery.SourceCommit)) {
    throw 'Deployed client version mismatch'
}
Write-Output 'ANNOUNCER DEPLOYED BINDING PASSED'
