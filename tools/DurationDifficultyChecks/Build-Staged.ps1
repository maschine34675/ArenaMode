param([string]$SptRoot)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if([string]::IsNullOrWhiteSpace($SptRoot)) { $SptRoot=[IO.Path]::GetFullPath((Join-Path $repo '../..')) }
$stages=Join-Path $repo 'artifacts/double-jump-build'
$old=@(Get-ChildItem -LiteralPath $stages -Directory | ForEach-Object FullName)
& (Join-Path $repo 'tools/DoubleJumpChecks/Build-Staged.ps1') -SptRoot $SptRoot
$new=@(Get-ChildItem -LiteralPath $stages -Directory | Where-Object FullName -NotIn $old)
if($new.Count -ne 1) { throw 'Cannot identify unique staged client build' }
$client=Get-Content -LiteralPath (Join-Path $new[0].FullName 'manifest.json') -Raw | ConvertFrom-Json
$stage=Join-Path $repo ('artifacts/duration-balance/build/'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
try {
    & dotnet build (Join-Path $repo 'ArenaMode.Server/ArenaMode.Server.csproj') -c Release --nologo "-p:SPTUserPath=$stage/install" "-p:OutputPath=$stage/server" 2>&1 | Tee-Object (Join-Path $stage 'server-build.log')
    if($LASTEXITCODE -ne 0) { throw 'Staged server build failed' }
} finally {
    foreach($entry in $client.InstalledUnchanged.PSObject.Properties) {
        if((Get-FileHash -LiteralPath $entry.Name -Algorithm SHA256).Hash -ne $entry.Value) { throw ('Installed artifact changed: '+$entry.Name) }
    }
}
$dll=Join-Path $stage 'server/maschine-ArenaMode.Server.dll'
[ordered]@{
    SourceCommit=(& git -C $repo rev-parse HEAD); SourceChanges=@(& git -C $repo status --short)
    ClientPath=$client.ClientPath; ClientSha256=$client.ClientSha256
    ServerPath=$dll; ServerSha256=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    InstalledUnchanged=$client.InstalledUnchanged; Deployed=$false; LiveGpuTested=$false
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stage 'manifest.json')
Write-Output "Combined staging manifest: $stage/manifest.json"
Write-Output 'ARENA_DURATION_STAGED_BUILD_PASSED'
