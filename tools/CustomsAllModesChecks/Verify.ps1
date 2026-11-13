param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'CustomsAllModesChecks.csproj'
$run = Join-Path $repo ('artifacts/customs-all-modes-checks/' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($run)
$source = [IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Shared/ArenaAreaCatalog.cs'))
$selectionSource = [IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Client/Session/ArenaAreaSelectionState.cs'))
function Check([string]$name, [string]$mutant = '', [string]$property = 'AreaCatalogSource') {
    $arguments = @('build', $project, '-c', 'Release', '-v:q', "-p:OutputPath=$run/$name/out/", "-p:BaseIntermediateOutputPath=$run/$name/obj/")
    if ($mutant) { $arguments += "-p:$property=$mutant" }
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
    @('pft-all-modes', 'if (pitFireTeam && !string.Equals(mode, "tactical", StringComparison.OrdinalIgnoreCase)', 'if (false && pitFireTeam && !string.Equals(mode, "tactical", StringComparison.OrdinalIgnoreCase)', 'ALL-MODE-AREA-ADMISSION'),
    # Domination shares Team Deathmatch's roster, so it negotiates the area team size too: the rule, and the menu that offers it.
    @('team-size-tdm-only', 'if (!pitFireTeam && ArenaPopulationPolicy.IsIndependentTeam(mode))', 'if (!pitFireTeam && string.Equals(mode, "teamdeathmatch", StringComparison.OrdinalIgnoreCase))', 'MODE-OWNED-FROZEN-SIZE'),
    @('domination-bounded-refused', '&& !string.Equals(mode, "domination", StringComparison.OrdinalIgnoreCase)', '', 'EXACT-OFFERED-AREAS'),
    @('size-ui-tdm-only', '&& ArenaPopulationPolicy.IsIndependentTeam(mode);', '&& mode == ArenaModeCatalog.TeamDeathmatchId;', 'TEAM-MODES-SIZE-UI', 'selection', 'AreaSelectionSource')
)
foreach ($fault in $faults) {
    $text = if ($fault.Count -gt 4 -and $fault[4] -eq 'selection') { $selectionSource } else { $source }
    $property = if ($fault.Count -gt 5) { $fault[5] } else { 'AreaCatalogSource' }
    if (-not $text.Contains($fault[1])) { throw "Mutation anchor missing: $($fault[0])" }
    $path = Join-Path $run ($fault[0] + '.cs')
    [IO.File]::WriteAllText($path, $text.Replace($fault[1], $fault[2]))
    $result = Check $fault[0] $path $property
    if ($result.Code -eq 0 -or $result.Output -notmatch $fault[3]) { throw "Uncaught semantic mutant $($fault[0]): $($result.Output)" }
    Write-Output "Negative control passed: $($fault[0])"
}
Write-Output "Evidence: $run"
Write-Output 'CUSTOMS_ALL_MODES_MUTATION_CHECKS_PASSED'
