#Requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$BuildManifest, [string]$SptRoot='D:\SPT41',
    [string]$AdditionalAudioManifest)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Inventory.ps1')
$deliveryRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$deliveryGame = (Resolve-Path -LiteralPath $SptRoot).Path
$deliveryBuild = Get-Content -LiteralPath $BuildManifest -Raw | ConvertFrom-Json -AsHashtable
$deliveryCommit = (& git -C $deliveryRepo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $deliveryBuild.SourceCommit -ne $deliveryCommit -or $deliveryBuild.SourceChanges.Count -ne 0 -or
    @(& git -C $deliveryRepo status --porcelain).Count -ne 0) { throw 'Deploy requires the clean current combined source build' }
if ((& git -C $deliveryRepo branch --show-current) -ne 'master') { throw 'Deploy the combined main checkout, not an isolated feature build' }
$deliveryDll = [IO.Path]::GetFullPath($deliveryBuild.ClientPath)
$deliveryArtifacts = [IO.Path]::GetFullPath((Join-Path $deliveryRepo 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (!$deliveryDll.StartsWith($deliveryArtifacts, [StringComparison]::OrdinalIgnoreCase)) { throw 'Client must come from this checkout staging directory' }
if ((Get-FileHash -LiteralPath $deliveryDll).Hash -ne $deliveryBuild.ClientSha256 -or
    [Diagnostics.FileVersionInfo]::GetVersionInfo($deliveryDll).ProductVersion -ne ('0.1.0+'+$deliveryCommit)) { throw 'Staged client identity differs from build binding' }
$deliveryCore = Join-Path $deliveryGame 'BepInEx/plugins/maschine-Redeploy.Core.dll'
$deliveryServer = Join-Path $deliveryGame 'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll'
$deliveryAssets = Join-Path $deliveryGame 'BepInEx/plugins/maschine-ArenaMode'
$deliveryClient = Join-Path $deliveryAssets 'maschine-ArenaMode.Client.dll'
# A current staging manifest observes all layouts, including absent copies.
foreach ($path in @($deliveryCore,$deliveryServer) + @(Get-ArenaInstalledLayoutPaths $deliveryGame)) {
    if (!$deliveryBuild.InstalledUnchanged.ContainsKey($path) -or
        (Get-ArenaObservedFileHash -LiteralPath $path).Hash -ne $deliveryBuild.InstalledUnchanged[$path]) {
        throw "Installed dependency/layout changed or was not observed during staging: $path"
    }
}
$deliveryExternal = @(
    [IO.Path]::GetFullPath((Join-Path $deliveryRepo '../Redeploy/Redeploy.Core/CoreVersion.props')),
    [IO.Path]::GetFullPath((Join-Path $deliveryRepo '../Redeploy/drivers/CoreVersionGate.cs')))
if ($deliveryBuild.ExternalBuildSources.Count -ne $deliveryExternal.Count) { throw 'Build must bind both external Redeploy compiler inputs' }
foreach ($path in $deliveryExternal) {
    if (!$deliveryBuild.ExternalBuildSources.ContainsKey($path) -or
        (Get-FileHash -LiteralPath $path).Hash -ne $deliveryBuild.ExternalBuildSources[$path]) {
        throw "External compiler input changed since staging: $path"
    }
}
$deliveryStage = Join-Path $deliveryRepo ('artifacts/announcer-deploy/' + [guid]::NewGuid().ToString('N'))
$deliveryPayload = Join-Path $deliveryStage 'payload'
[void][IO.Directory]::CreateDirectory($deliveryPayload)
# Use the same complete owned payload as normal builds and ZIPs. In particular,
# attribution links and JSON sound paths must be rewritten after moving to docs/.
& (Join-Path $deliveryRepo 'tools/Write-ArenaAssets.ps1') -SourceRoot $deliveryRepo -Destination $deliveryPayload -SourceRevision $deliveryCommit | Out-Host
if ($AdditionalAudioManifest) {
    if ($AdditionalAudioManifest -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]*\.json$' -or
        !(Test-Path -LiteralPath (Join-Path $deliveryPayload ('docs/licenses/audio/'+$AdditionalAudioManifest)) -PathType Leaf)) {
        throw 'AdditionalAudioManifest must name an announcer manifest included in the tracked shared payload.'
    }
}
$deliveryPairs = @(Get-ChildItem -LiteralPath $deliveryPayload -Recurse -File | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($deliveryPayload, $_.FullName)
    @{ Source=$_.FullName; Target=(Join-Path $deliveryAssets $relative); SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash }
})
$deliveryPairs += @{ Source=$deliveryDll; Target=$deliveryClient; SHA256=(Get-FileHash -LiteralPath $deliveryDll).Hash }
$deliveryBefore = Read-AnnouncerDeliveryInventory $deliveryGame
$deliveryBefore | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $deliveryStage 'before.json')
$deliveryExpected = ConvertTo-AnnouncerCanonicalInventory $deliveryBefore $deliveryGame
# A Release client deployment archives its own old debug symbols.
$deliveryExpected.Remove([IO.Path]::ChangeExtension($deliveryClient, '.pdb'))
foreach ($pair in $deliveryPairs) { $deliveryExpected[$pair.Target] = $pair.SHA256 }
$deliveryExpected | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $deliveryStage 'expected.json')
# The common deployer owns migration/archive safety for old flat and grouped
# installs. It preserves Core, configuration, drafts and custom pickup layouts.
& (Join-Path $deliveryRepo 'tools/Deploy-ArenaClient.ps1') -AssemblyPath $deliveryDll -SptPath $deliveryGame -Configuration Release | Out-Host
foreach ($pair in $deliveryPairs | Where-Object Target -NE $deliveryClient) {
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($pair.Target))
    Copy-Item -LiteralPath $pair.Source -Destination $pair.Target -Force
    if ((Get-FileHash -LiteralPath $pair.Target).Hash -ne $pair.SHA256) { throw "Copied asset differs: $($pair.Target)" }
}
$deliveryAfter = Read-AnnouncerDeliveryInventory $deliveryGame
Assert-AnnouncerInventory $deliveryExpected $deliveryAfter
$deliveryAfter | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $deliveryStage 'after.json')
$deliveryRecord = [ordered]@{
    SchemaVersion=2; SourceCommit=$deliveryCommit; ProductVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo($deliveryClient).ProductVersion;
    Deployed=$true; LiveGpuTested=$false; GpuMirrorVerified=$false; DateUtc=[DateTime]::UtcNow.ToString('o');
    SptRoot=$deliveryGame; Files=$deliveryPairs;
    InstalledUnchanged=@{ $deliveryCore=$deliveryAfter[$deliveryCore]; $deliveryServer=$deliveryAfter[$deliveryServer] };
    ExternalBuildSources=$deliveryBuild.ExternalBuildSources; EvidenceDirectory=$deliveryStage
}
$deliveryManifest = Join-Path $deliveryStage 'manifest.json'
$deliveryRecord | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $deliveryManifest
Write-Output "Deployment manifest: $deliveryManifest"
Write-Output "ANNOUNCER DEPLOYMENT PASSED; $($deliveryPairs.Count) owned payload files, shared migration verified"
