$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
& dotnet run --project (Join-Path $PSScriptRoot 'HitmarkerAudioChecks.csproj') -c Release -- $arenaRepo
if ($LASTEXITCODE -ne 0) { throw 'Production hitmarker checks failed.' }
& pwsh -NoProfile -File (Join-Path $arenaRepo 'tools\AnnouncerChecks\Verify-Regression.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Existing announcer regression checks failed.' }
Write-Output 'HITMARKER_AUDIO_CHECKS_VERIFIED'
