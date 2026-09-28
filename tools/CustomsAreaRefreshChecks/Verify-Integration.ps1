param([string]$SptRoot='D:\SPT41')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $repo
try {
 foreach($check in @('AreaConfigChecks','AreaTeamMenuChecks','AreaMapPreviewChecks','AreaTeamRuntimeChecks')) {
   & pwsh -NoProfile -File "tools/$check/Verify.ps1"
   if($LASTEXITCODE -ne 0){throw "Integration failure: $check"}
 }
 & dotnet run --project tools/TdmMatchMenuChecks/TdmMatchMenuChecks.csproj -c Release
 if($LASTEXITCODE -ne 0){throw 'Integration failure: TdmMatchMenuChecks'}
 Write-Output 'CUSTOMS AREA INTEGRATION CHECKS PASSED'
} finally {Pop-Location}
