$ErrorActionPreference='Stop'
$arenaRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project=Join-Path $arenaRepo 'tools/ArenaPickupChecks/ArenaPickupChecks.csproj'
node (Join-Path $PSScriptRoot 'verify-assets.mjs')
if($LASTEXITCODE -ne 0){throw 'Asset check failed'}
$result=(& dotnet run --project $project -c Release 2>&1 | Out-String)
if($LASTEXITCODE -ne 0 -or !$result.Contains('PICKUP_AUDIO_RUNTIME_PASSED')){throw $result}
Write-Output $result.Trim()
$source=[IO.File]::ReadAllText((Join-Path $arenaRepo 'ArenaMode.Client/Pickups/ArenaPickupAudio.cs'))
$evidence=Join-Path $arenaRepo ('artifacts/pickup-audio-checks/'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($evidence)
try {
  foreach($fault in @(
    @('bot','player.IsAI || !player.IsYourPlayer','false', 'bot collection is silent'),
    @('pause','|| AudioListener.pause','', 'paused listener never queues pickup'),
    @('live','!ArenaPickupRuntime.IsLiveActor(player) ||','', 'lost exact raid cannot emit late audio'),
    @('load','if (!_loadAttempted)','if (true)', 'missing cue is attempted once per round')
  )) {
    if(!$source.Contains($fault[1])){throw 'Mutation anchor missing'}
    $path=Join-Path $evidence ($fault[0]+'.cs')
    [IO.File]::WriteAllText($path,$source.Replace($fault[1],$fault[2]))
    $result=(& dotnet run --project $project -c Release "-p:PickupAudioSource=$path" 2>&1 | Out-String)
    if($LASTEXITCODE -eq 0 -or $result.Contains('error CS') -or !$result.Contains($fault[3])){throw $result}
    Write-Output "NEGATIVE_CONTROL_PASSED $($fault[0])"
  }
} finally { & dotnet build $project -c Release -t:Rebuild --nologo -v:quiet; if($LASTEXITCODE -ne 0){throw 'Restore failed'} }
Write-Output 'PICKUP_AUDIO_CHECKS_PASSED'
