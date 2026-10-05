$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$evidence = Join-Path $repo ('artifacts\akimbo-checks\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$project = Join-Path $PSScriptRoot 'AkimboChecks.csproj'
$source = [IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Client\Combat\Akimbo\AkimboLease.cs'))
$faults = @(
  @('body', 'ReferenceEquals(body, _body)', 'true', 'replacement identity rejected 0'),
  @('expiry', 'now < _until', 'now <= _until', 'deadline exclusive'),
  @('original', '|| !runOriginal', '', 'all shot guards required 29'),
  @('busy', '|| !ready', '', 'all shot guards required 27'),
  @('nested', '|| _emitting', '', 'nested bonus rejected')
)
try {
  $output = (& dotnet run --project $project -c Release 2>&1 | Out-String)
  if ($LASTEXITCODE -ne 0 -or !$output.Contains('AKIMBO_CHECKS_PASSED')) { throw $output }
  Write-Output $output.Trim()
  foreach ($fault in $faults) {
    if ($source.Split([string[]]@($fault[1]), [StringSplitOptions]::None).Count -ne 2) { throw 'Mutation site changed' }
    $file = Join-Path $evidence ($fault[0]+'.cs')
    [IO.File]::WriteAllText($file,$source.Replace($fault[1],$fault[2]))
    $output = (& dotnet run --project $project -c Release "-p:LeaseSource=$file" 2>&1 | Out-String)
    $code = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $evidence ($fault[0]+'.log')),$output)
    if ($code -eq 0 -or $output.Contains('error CS') -or !$output.Contains($fault[3])) { throw $output }
    Write-Output "NEGATIVE_CONTROL_PASSED $($fault[0])"
  }
} finally { & dotnet build $project -c Release -t:Rebuild --nologo -v:quiet; if ($LASTEXITCODE -ne 0) { throw 'Restore failed' } }
& dotnet run --project $project -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Final production run failed' }
Write-Output 'AKIMBO_REGRESSION_PASSED'
