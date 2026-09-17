param([Parameter(Mandatory=$true)][string]$BuildManifest, [string]$SptRoot='D:\SPT41',
    [string]$AdditionalAudioManifest)
$ErrorActionPreference = 'Stop'
$deliveryRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$deliveryGame = (Resolve-Path -LiteralPath $SptRoot).Path
$deliveryBuild = Get-Content -LiteralPath $BuildManifest -Raw | ConvertFrom-Json -AsHashtable
$deliveryCommit = & git -C $deliveryRepo rev-parse HEAD
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
$deliveryClient = Join-Path $deliveryGame 'BepInEx/plugins/maschine-ArenaMode.Client.dll'
foreach ($path in @($deliveryCore,$deliveryServer,$deliveryClient)) {
    if (!$deliveryBuild.InstalledUnchanged.ContainsKey($path) -or
        (Get-FileHash -LiteralPath $path).Hash -ne $deliveryBuild.InstalledUnchanged[$path]) {
        throw "Installed dependency changed since staging: $path"
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
[void][IO.Directory]::CreateDirectory($deliveryStage)
$deliveryPairs = @()
foreach ($name in @('ANNOUNCER-PACK.md','ANNOUNCER-SOURCES.json','AUDIO-LICENSES.md')) {
    $deliveryPairs += @{ Source=(Join-Path $deliveryRepo ('ArenaMode.Client/Assets/ArenaMode/'+$name)); Target=(Join-Path $deliveryGame ('BepInEx/plugins/ArenaMode/'+$name)) }
}
$deliveryPairs += @{ Source=$deliveryDll; Target=$deliveryClient }
# Optional, reviewed additions travel with their provenance in the same guarded
# transaction as the client. Never copy an entire plugin folder or user drafts.
if ($AdditionalAudioManifest) {
    $assetRoot = [IO.Path]::GetFullPath((Join-Path $deliveryRepo 'ArenaMode.Client/Assets/ArenaMode'))
    if ($AdditionalAudioManifest -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]*\.json$') {
        throw 'AdditionalAudioManifest must be a JSON filename inside the tracked audio asset folder'
    }
    $manifestSource = Join-Path $assetRoot $AdditionalAudioManifest
    $audioAdditions = Get-Content -LiteralPath $manifestSource -Raw | ConvertFrom-Json
    if (@($audioAdditions.files).Count -eq 0) { throw 'Additional audio manifest has no files' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $audioAdditions.files) {
        $relative = [string]$entry.file
        if ($relative -cnotmatch '^sounds/(reserve/)?[a-z0-9_]+\.ogg$' -or !$seen.Add($relative)) {
            throw "Invalid or duplicate additional audio destination: $relative"
        }
        $source = Join-Path $assetRoot $relative
        if ((Get-FileHash -LiteralPath $source).Hash -ne $entry.sha256) { throw "Additional audio hash differs: $relative" }
        $deliveryPairs += @{ Source=$source; Target=(Join-Path $deliveryGame ('BepInEx/plugins/ArenaMode/'+$relative)) }
    }
    $deliveryPairs += @{ Source=$manifestSource; Target=(Join-Path $deliveryGame ('BepInEx/plugins/ArenaMode/'+$AdditionalAudioManifest)) }
}
function Read-Inventory {
    # Include every plugin DLL and all Arena package files; user survey drafts
    # are deliberately outside deployment. No folder replacement or deletion.
    $paths = @(Get-ChildItem -LiteralPath (Join-Path $deliveryGame 'BepInEx/plugins') -Recurse -File -Filter '*.dll' |
        Where-Object { $_.FullName -notmatch '\\AreaDrafts\\' } | ForEach-Object FullName)
    $paths += @(Get-ChildItem -LiteralPath (Join-Path $deliveryGame 'BepInEx/plugins/ArenaMode') -Recurse -File |
        Where-Object { $_.FullName -notmatch '\\AreaDrafts\\' } | ForEach-Object FullName)
    $paths += $deliveryServer
    $hashes = @{}
    foreach ($path in $paths) { $hashes[$path] = (Get-FileHash -LiteralPath $path).Hash }
    return $hashes
}
$deliveryBefore = Read-Inventory
$deliveryBefore | ConvertTo-Json | Set-Content (Join-Path $deliveryStage 'before.json')
$index = 0
foreach ($pair in $deliveryPairs) {
    $pair.Backup = Join-Path $deliveryStage ('backup-'+$index+'-'+[IO.Path]::GetFileName($pair.Target))
    $pair.SHA256 = (Get-FileHash -LiteralPath $pair.Source).Hash
    $pair.Existed = Test-Path -LiteralPath $pair.Target -PathType Leaf
    # Check all existing destinations before the first file mutation. Newly
    # added recordings only target the already installed sounds/reserve folders.
    if ($pair.Existed) {
        $handle = [IO.File]::Open($pair.Target, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $handle.Dispose()
        Copy-Item -LiteralPath $pair.Target -Destination $pair.Backup
        if ((Get-FileHash -LiteralPath $pair.Backup).Hash -ne $deliveryBefore[$pair.Target]) { throw 'Backup hash mismatch' }
    } elseif (!(Test-Path -LiteralPath ([IO.Path]::GetDirectoryName($pair.Target)) -PathType Container)) {
        throw "Additional audio destination folder is missing: $($pair.Target)"
    }
    $index++
}
$deliveryChanged = @()
try {
    foreach ($pair in $deliveryPairs) {
        if (!$pair.Existed -and (Test-Path -LiteralPath $pair.Target)) { throw 'A new destination appeared during deployment' }
        $deliveryChanged += $pair
        Copy-Item -LiteralPath $pair.Source -Destination $pair.Target -Force
        if ((Get-FileHash -LiteralPath $pair.Target).Hash -ne $pair.SHA256) { throw "Copied artifact mismatch: $($pair.Target)" }
    }
    $deliveryAfter = Read-Inventory
    $newCount = @($deliveryPairs | Where-Object { !$_.Existed }).Count
    if ($deliveryBefore.Count + $newCount -ne $deliveryAfter.Count) { throw 'Installed inventory changed unexpectedly' }
    $targets = @($deliveryPairs | ForEach-Object Target)
    foreach ($path in $deliveryBefore.Keys) {
        if (!$deliveryAfter.ContainsKey($path)) { throw "Installed inventory path disappeared: $path" }
        if ($path -notin $targets -and $deliveryBefore[$path] -ne $deliveryAfter[$path]) { throw "Unrelated installed file changed: $path" }
    }
    $deliveryAfter | ConvertTo-Json | Set-Content (Join-Path $deliveryStage 'after.json')
    $deliveryRecord = [ordered]@{
        SourceCommit=$deliveryCommit; ProductVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo($deliveryClient).ProductVersion;
        Deployed=$true; LiveGpuTested=$false; GpuMirrorVerified=$false; DateUtc=[DateTime]::UtcNow.ToString('o');
        SptRoot=$deliveryGame; Files=$deliveryPairs; InstalledUnchangedCount=($deliveryBefore.Count-($deliveryPairs.Count-$newCount));
        InstalledUnchanged=@{ $deliveryCore=$deliveryAfter[$deliveryCore]; $deliveryServer=$deliveryAfter[$deliveryServer] };
        ExternalBuildSources=$deliveryBuild.ExternalBuildSources; EvidenceDirectory=$deliveryStage
    }
    $deliveryManifest = Join-Path $deliveryStage 'manifest.json'
    $deliveryRecord | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $deliveryManifest
} catch {
    $deliveryError = $_
    $rollbackErrors = [Collections.Generic.List[string]]::new()
    # Attempt every restoration even if one destination became locked. Backups
    # remain available for manual recovery; unrelated files are never restored.
    for ($i = $deliveryChanged.Count - 1; $i -ge 0; $i--) {
        $pair = $deliveryChanged[$i]
        try {
            if (!$pair.Existed) {
                # Remove only this transaction's new file, never a concurrent
                # replacement, and never a directory or computed outside path.
                $assetTargetRoot = [IO.Path]::GetFullPath((Join-Path $deliveryGame 'BepInEx/plugins/ArenaMode')) + [IO.Path]::DirectorySeparatorChar
                $resolvedTarget = [IO.Path]::GetFullPath($pair.Target)
                if (!$resolvedTarget.StartsWith($assetTargetRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe rollback destination' }
                if (Test-Path -LiteralPath $resolvedTarget -PathType Leaf) {
                    if ((Get-FileHash -LiteralPath $resolvedTarget).Hash -ne $pair.SHA256) { throw 'New destination changed; not removing it' }
                    Remove-Item -LiteralPath $resolvedTarget
                }
                continue
            }
            $originalHash = $deliveryBefore[$pair.Target]
            $currentHash = if (Test-Path -LiteralPath $pair.Target -PathType Leaf) { (Get-FileHash -LiteralPath $pair.Target).Hash } else { $null }
            if ($currentHash -ne $originalHash) {
                if ((Get-FileHash -LiteralPath $pair.Backup).Hash -ne $originalHash) { throw 'Original backup hash changed' }
                Copy-Item -LiteralPath $pair.Backup -Destination $pair.Target -Force
            }
            if ((Get-FileHash -LiteralPath $pair.Target).Hash -ne $originalHash) { throw 'Restored file hash differs from original' }
        } catch { $rollbackErrors.Add("$($pair.Target): $($_.Exception.Message)") }
    }
    if ($rollbackErrors.Count -gt 0) {
        $message = "Deployment failed: $($deliveryError.Exception.Message). Rollback incomplete: $($rollbackErrors -join '; '). Backups: $deliveryStage"
        throw [InvalidOperationException]::new($message, $deliveryError.Exception)
    }
    throw $deliveryError
}
Write-Output "Deployment manifest: $deliveryManifest"
Write-Output "ANNOUNCER DEPLOYMENT PASSED; $($deliveryPairs.Count) files copied, $($deliveryRecord.InstalledUnchangedCount) unrelated files unchanged"
