$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$arenaProject = Join-Path $PSScriptRoot 'CompactModeUxChecks.csproj'
$arenaSourcePath = Join-Path $arenaRepo 'ArenaMode.Client/Session/ArenaCompactModeRules.cs'
$arenaSource = [IO.File]::ReadAllText($arenaSourcePath)
$arenaScratch = Join-Path $arenaRepo ('artifacts/compact-mode-ux/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $arenaScratch -Force | Out-Null
function Invoke-Check([string]$Name, [string]$Source = '') {
    $arguments = @('run', '--project', $arenaProject, '-c', 'Release')
    if ($Source) { $arguments += "-p:ProductionCompactRules=$Source" }
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
    @('fixed-three-points', '? controlPoints +', '? 3 +', 'Domination copy uses the actual map objective count')
)
foreach ($fault in $faults) {
    if ([regex]::Matches($arenaSource, [regex]::Escape($fault[1])).Count -ne 1) { throw "Ambiguous mutant: $($fault[0])" }
    $copy = Join-Path $arenaScratch ($fault[0] + '.cs')
    [IO.File]::WriteAllText($copy, $arenaSource.Replace($fault[1], $fault[2]))
    $result = Invoke-Check $fault[0] $copy
    if ($result.Code -eq 0 -or $result.Output.Contains('error CS') -or !$result.Output.Contains($fault[3])) { throw $result.Output }
    Write-Output "NEGATIVE CONTROL PASSED: $($fault[0])"
}
$final = Invoke-Check 'production-final'
if ($final.Code -ne 0 -or !$final.Output.Contains('COMPACT_MODE_UX_CHECKS_PASSED')) { throw $final.Output }
Write-Output $final.Output
Write-Output 'COMPACT_MODE_UX_MUTATIONS_PASSED'
