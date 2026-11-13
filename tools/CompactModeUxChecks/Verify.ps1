$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$arenaProject = Join-Path $PSScriptRoot 'CompactModeUxChecks.csproj'
$arenaSourcePath = Join-Path $arenaRepo 'ArenaMode.Client/Session/ArenaCompactModeRules.cs'
$arenaSource = [IO.File]::ReadAllText($arenaSourcePath)
$arenaScratch = Join-Path $arenaRepo ('artifacts/compact-mode-ux/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $arenaScratch -Force | Out-Null
function Invoke-Check([string]$Name, [string]$Source = '', [string]$Property = 'ProductionCompactRules') {
    $arguments = @('run', '--project', $arenaProject, '-c', 'Release')
    if ($Source) { $arguments += "-p:$Property=$Source" }
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = (& dotnet @arguments 2>&1 | ForEach-Object { "$_" }) -join "`n"
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previous }
    [IO.File]::WriteAllText((Join-Path $arenaScratch ($Name + '.log')), $output)
    return [pscustomobject]@{ Code = $code; Output = $output }
}
$baseline = Invoke-Check 'baseline'
if ($baseline.Code -ne 0 -or !$baseline.Output.Contains('COMPACT_MODE_UX_CHECKS_PASSED')) { throw $baseline.Output }
$faults = @(
    @('continuous-winner', 'string objective = kills == 0 && seconds == 0 ?', 'string objective = false ?', 'continuous DM has no winner'),
    @('squad-one-life', 'return (team ? "Team wipe ends the match.', 'return (false ? "Team wipe ends the match.', 'PFT Zombies never promises'),
    @('fixed-three-points', '? controlPoints +', '? 3 +', 'Domination copy uses the actual map objective count'),
    @('objective-copy-ignores-map', 'bool point = tacticalObjective && controlPoints > 0;', 'bool point = tacticalObjective;', 'Tactical copy promises the point only when it is on and the map has one')
)
foreach ($fault in $faults) {
    if ([regex]::Matches($arenaSource, [regex]::Escape($fault[1])).Count -ne 1) { throw "Ambiguous mutant: $($fault[0])" }
    $copy = Join-Path $arenaScratch ($fault[0] + '.cs')
    [IO.File]::WriteAllText($copy, $arenaSource.Replace($fault[1], $fault[2]))
    $result = Invoke-Check $fault[0] $copy
    if ($result.Code -eq 0 -or $result.Output.Contains('error CS') -or !$result.Output.Contains($fault[3])) { throw $result.Output }
    Write-Output "NEGATIVE CONTROL PASSED: $($fault[0])"
}
# Mutants of the layout's two counts: Tactical's ControlPointCount must not gain The Lab, and Domination's own count must answer it.
$arenaLayoutPath = Join-Path $arenaRepo 'ArenaMode.Client/Modes/DominationMapLayout.cs'
$arenaLayout = [IO.File]::ReadAllText($arenaLayoutPath).Replace("`r`n", "`n")
$layoutFaults = @(
    @('tactical-count-gains-lab', "|| ArenaMapCatalog.IsIceberg(map) || ArenaMapCatalog.IsAutoService(map))) return 0;`n        return Resolve(map).Points.Count;",
      "|| ArenaMapCatalog.IsIceberg(map) || ArenaMapCatalog.IsAutoService(map)`n            || string.Equals(map, ArenaMapCatalog.LaboratoryId, StringComparison.OrdinalIgnoreCase))) return 0;`n        return ResolveDomination(map, null).Points.Count;",
      'menu objective count for laboratory: expected 0, got 3'),
    @('domination-count-lab-missing', 'return ResolveDomination(map, areaId).Points.Count;', 'return 0;',
      'Domination objective count for laboratory/whole map: expected 3, got 0'),
    @('customs-area-count-missing', 'return ResolveArea(map, areaId)?.Points.Count ?? 0;', 'return 0;',
      'Domination objective count for bigmap/customs-yard-polygon-v1: expected 3, got 0'),
    @('customs-area-count-any-map', 'if (!string.Equals(map, ArenaMapCatalog.CustomsId, StringComparison.OrdinalIgnoreCase)) return null;', '',
      'Domination objective count for laboratory/customs-yard-polygon-v1: expected 0, got 3'),
    @('labyrinth-two-points', 'new Point("X", new Vector3(-9.92f, .1f, 23.92f), new Vector3(5.92f, .1f, 20.22f),',
      'new Point("Y", new Vector3(-30f, .1f, 20f)), new Point("X", new Vector3(-9.92f, .1f, 23.92f), new Vector3(5.92f, .1f, 20.22f),',
      'Domination objective count for labyrinth/whole map: expected 1, got 2')
)
foreach ($fault in $layoutFaults) {
    if ([regex]::Matches($arenaLayout, [regex]::Escape($fault[1])).Count -ne 1) { throw "Ambiguous mutant: $($fault[0])" }
    $copy = Join-Path $arenaScratch ($fault[0] + '.cs')
    [IO.File]::WriteAllText($copy, $arenaLayout.Replace($fault[1], $fault[2]))
    $result = Invoke-Check $fault[0] $copy 'ProductionMapLayout'
    if ($result.Code -eq 0 -or $result.Output.Contains('error CS') -or !$result.Output.Contains($fault[3])) { throw $result.Output }
    Write-Output "NEGATIVE CONTROL PASSED: $($fault[0])"
}
$final = Invoke-Check 'production-final'
if ($final.Code -ne 0 -or !$final.Output.Contains('COMPACT_MODE_UX_CHECKS_PASSED')) { throw $final.Output }
Write-Output $final.Output
Write-Output 'COMPACT_MODE_UX_MUTATIONS_PASSED'
