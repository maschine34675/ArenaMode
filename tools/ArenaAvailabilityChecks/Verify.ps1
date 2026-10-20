$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
& dotnet run --project (Join-Path $repo 'tools/NativeMenuChecks/NativeMenuChecks.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Native menu admission/lifecycle checks failed.' }
& pwsh -NoProfile -File (Join-Path $repo 'tools/OptionalModAdmissionChecks/Verify.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Optional mod admission checks or fault controls failed.' }
Write-Output 'ARENA_AVAILABILITY_VERIFIED'
