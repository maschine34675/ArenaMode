$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourcePath = Join-Path $repo 'ArenaMode.Client/Combat/ArenaSkills.cs'
$source = [IO.File]::ReadAllText($sourcePath)
$scratch = Join-Path $repo ('artifacts/skill-isolation-checks/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
function Run-Check([string]$name, [string]$path = $sourcePath) {
    $output = & dotnet run --project (Join-Path $PSScriptRoot 'SkillIsolationChecks.csproj') -c $name "-p:SkillsSource=$path" 2>&1 | Out-String
    $code = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $scratch ($name + '.log')), $output)
    return @{ Code=$code; Output=$output }
}
$baseline = Run-Check 'Release'
if ($baseline.Code -ne 0 -or !$baseline.Output.Contains('SKILL_ISOLATION_CHECKS_PASSED')) { throw $baseline.Output }
Write-Output $baseline.Output.Trim()
$faults = @(
    @{Name='InactiveTick'; From='!_boostActive || !Settings.MaxSkills.Value || !IsActiveWorld(world)'; To='!_boostActive || !Settings.MaxSkills.Value'; Failure='ordinary transition restores original values'},
    @{Name='ReapplyPending'; From='if (HasSnapshot) return;'; To=''; Failure='failed restore cannot reapply or replace original baselines'},
    @{Name='ReplayMastery'; From='MasterySnapshot.Remove(mastery);'; To='if (BuffSnapshot.Count == 0) MasterySnapshot.Remove(mastery);'; Failure='successful restores are not replayed over ordinary gains'}
)
foreach ($fault in $faults) {
    if (!$source.Contains($fault.From)) { throw "Missing fault anchor $($fault.Name)" }
    $candidate = Join-Path $scratch ($fault.Name + '.cs')
    [IO.File]::WriteAllText($candidate, $source.Replace($fault.From, $fault.To))
    $result = Run-Check $fault.Name $candidate
    if ($result.Code -eq 0 -or $result.Output -match 'error CS\d+' -or !$result.Output.Contains('ASSERT: ' + $fault.Failure)) {
        throw "Incorrect negative control $($fault.Name): $($result.Output)"
    }
    Write-Output "Rejected skill isolation fault: $($fault.Name)"
}
if ([IO.File]::ReadAllText($sourcePath) -cne $source) { throw 'Production source changed during checks' }
Write-Output "SKILL_ISOLATION_REGRESSION_VERIFIED ($($faults.Count) negative controls; evidence $scratch)"
