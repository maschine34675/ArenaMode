param([string]$SptRoot = 'D:\SPT41')
. (Join-Path $PSScriptRoot '../Read-ArenaInstalledSnapshot.ps1')
$arenaSnapshotRoot = $SptRoot
$arenaLayoutSnapshot = Read-ArenaInstalledSnapshot $arenaSnapshotRoot
try {
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$installed = @{}
foreach ($relative in @('BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Client.dll','BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.BigBrainCompat.dll',
    'BepInEx/plugins/maschine-Redeploy.Core.dll','SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll')) {
    $path = Join-Path $SptRoot $relative
    $installed[$path] = (Get-ArenaObservedFileHash -LiteralPath $path).Hash
}
$output = & (Join-Path $repo 'tools/MolotovChecks/Build-Staged.ps1') -SptRoot $SptRoot
$output | Write-Output
$line = @($output | Where-Object { $_ -like 'Pickup build manifest: *' })
if ($line.Count -ne 1) { throw 'No unique staged build manifest' }
$manifestPath = $line[0].Substring('Pickup build manifest: '.Length)
$stage = Split-Path $manifestPath -Parent
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$targets = Join-Path $stage 'ServerNoDeploy.targets'
[IO.File]::WriteAllText($targets, '<Project><Target Name="DeployToSpt" /></Project>')
try {
    & dotnet build (Join-Path $repo 'ArenaMode.Server/ArenaMode.Server.csproj') -c Release --nologo `
        "-p:CustomAfterMicrosoftCommonTargets=$targets" "-p:SPTPath=$stage/install" "-p:SPTUserPath=$stage/install/SPT_Runtime" `
        "-p:OutputPath=$stage/server" 2>&1 | Tee-Object -FilePath (Join-Path $stage 'server-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Staged server build failed' }
    $server = Join-Path $stage 'server/maschine-ArenaMode.Server.dll'
    $manifest | Add-Member -NotePropertyName ServerPath -NotePropertyValue $server
    $manifest | Add-Member -NotePropertyName ServerSha256 -NotePropertyValue (Get-ArenaObservedFileHash -LiteralPath $server).Hash
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath
} finally {
    foreach ($path in $installed.Keys) {
        if ((Get-ArenaObservedFileHash -LiteralPath $path).Hash -ne $installed[$path]) { throw "Installed artifact changed: $path" }
    }
}
if ((& git -C $repo rev-parse HEAD) -ne $manifest.SourceCommit -or
    (@(& git -C $repo status --short) -join "`n") -cne (@($manifest.SourceChanges) -join "`n")) { throw 'Source state changed during staged build' }
Write-Output "Molotov item build manifest: $manifestPath"
Write-Output 'ARENA_MOLOTOV_ITEM_BUILD_PASSED'

} finally {
    Assert-ArenaInstalledSnapshotUnchanged $arenaLayoutSnapshot $arenaSnapshotRoot
}
