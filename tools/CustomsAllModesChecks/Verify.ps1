param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'CustomsAllModesChecks.csproj'
$run = Join-Path $repo ('artifacts/customs-all-modes-checks/' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($run)
$source = [IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Shared/ArenaAreaCatalog.cs'))
function Check([string]$name, [string]$catalog = '') {
    $arguments = @('build', $project, '-c', 'Release', '-v:q', "-p:OutputPath=$run/$name/out/", "-p:BaseIntermediateOutputPath=$run/$name/obj/")
    if ($catalog) { $arguments += "-p:AreaCatalogSource=$catalog" }
    & dotnet @arguments > (Join-Path $run ($name + '-build.log')) 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Source harness compile failed: $name" }
    $output = & dotnet (Join-Path $run "$name/out/CustomsAllModesChecks.dll") 2>&1
    $code = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $run ($name + '.log')), ($output -join "`n"))
    return @{ Code = $code; Output = ($output -join "`n") }
}
$baseline = Check 'baseline'
if ($baseline.Code -ne 0 -or $baseline.Output -notmatch 'CUSTOMS_ALL_MODES_CHECKS_PASSED') { throw $baseline.Output }
Write-Output $baseline.Output
$faults = @(
    @('whole-customs', 'if (string.Equals(map, ArenaMapCatalog.CustomsId, StringComparison.OrdinalIgnoreCase))', 'if (false)', 'NO-WHOLE-CUSTOMS'),
    @('bounded-tdm-only', 'if (pitFireTeam && !string.Equals(mode, "tactical", StringComparison.OrdinalIgnoreCase)', 'if (!string.Equals(mode, "teamdeathmatch", StringComparison.OrdinalIgnoreCase) || pitFireTeam && !string.Equals(mode, "tactical", StringComparison.OrdinalIgnoreCase)', 'EXACT-OFFERED-AREAS'),
    @('pft-all-modes', 'if (pitFireTeam && !string.Equals(mode, "tactical", StringComparison.OrdinalIgnoreCase)', 'if (false && pitFireTeam && !string.Equals(mode, "tactical", StringComparison.OrdinalIgnoreCase)', 'ALL-MODE-AREA-ADMISSION')
)
foreach ($fault in $faults) {
    if (-not $source.Contains($fault[1])) { throw "Mutation anchor missing: $($fault[0])" }
    $path = Join-Path $run ($fault[0] + '.cs')
    [IO.File]::WriteAllText($path, $source.Replace($fault[1], $fault[2]))
    $result = Check $fault[0] $path
    if ($result.Code -eq 0 -or $result.Output -notmatch $fault[3]) { throw "Uncaught semantic mutant $($fault[0]): $($result.Output)" }
    Write-Output "Negative control passed: $($fault[0])"
}
Write-Output "Evidence: $run"
Write-Output 'CUSTOMS_ALL_MODES_MUTATION_CHECKS_PASSED'
