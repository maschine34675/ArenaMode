param([string]$SptRoot='D:\SPT41')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$serverInstalled=Join-Path $SptRoot 'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll'
$before=(Get-FileHash -LiteralPath $serverInstalled).Hash
$output=& pwsh -NoProfile -File (Join-Path $repo 'tools/DoubleJumpChecks/Build-Staged.ps1') -SptRoot $SptRoot 2>&1
$output | Write-Output
if($LASTEXITCODE -ne 0){throw 'Client staged build failed'}
$clientLine=@($output | Where-Object { "$_" -like 'Staged client: *' })
if($clientLine.Count -ne 1){throw 'Missing exact client build identity'}
$clientPath=([string]$clientLine[0]).Substring('Staged client: '.Length)
$manifestPath=Join-Path (Split-Path (Split-Path $clientPath)) 'manifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$stage=Split-Path $manifestPath
$noDeploy=Join-Path $stage 'must-not-exist'
& dotnet build (Join-Path $repo 'ArenaMode.Server/ArenaMode.Server.csproj') -c Release -t:Rebuild --nologo -v:minimal "-p:SPTUserMods=$noDeploy" "-p:OutputPath=$stage/server/out/"
if($LASTEXITCODE -ne 0){throw 'Server staged build failed'}
if((Test-Path -LiteralPath $noDeploy) -or (Get-FileHash -LiteralPath $serverInstalled).Hash -ne $before){throw 'Build changed installation'}
foreach($entry in $manifest.InstalledUnchanged.PSObject.Properties){
 if((Get-FileHash -LiteralPath $entry.Name).Hash -ne $entry.Value){throw 'Installed artifact changed during paired build'}
}
if((& git -C $repo rev-parse HEAD) -ne $manifest.SourceCommit -or ((@(& git -C $repo status --short) -join "`n") -cne (@($manifest.SourceChanges) -join "`n"))){throw 'Repository changed during paired build'}
$server=Join-Path $stage 'server/out/maschine-ArenaMode.Server.dll'
$manifest | Add-Member NoteProperty ServerPath $server -Force
$manifest | Add-Member NoteProperty ServerSha256 (Get-FileHash -LiteralPath $server).Hash -Force
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath
Write-Output "Paired manifest: $manifestPath"
Write-Output 'CUSTOMS AREA STAGED BUILD PASSED'
