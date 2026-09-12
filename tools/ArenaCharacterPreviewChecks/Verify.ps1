$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
& dotnet run --project (Join-Path $PSScriptRoot 'ArenaCharacterPreviewChecks.csproj') -c Release -- $arenaRepo
if ($LASTEXITCODE -ne 0) { throw 'Arena preview checks failed.' }
& dotnet build (Join-Path $PSScriptRoot 'HookCompile.csproj') -c Release --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Native preview hook compilation failed.' }
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Verify-Mutations.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Preview mutation controls failed.' }
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Test-ReviewFrames.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Preview frame evidence helper failed.' }
& pwsh -NoProfile -File (Join-Path $PSScriptRoot '..\PreviewHostLifecycleChecks\Verify.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Preview host lifecycle checks failed.' }
Write-Output 'ARENA_CHARACTER_PREVIEW_CHECKS_PASSED'
