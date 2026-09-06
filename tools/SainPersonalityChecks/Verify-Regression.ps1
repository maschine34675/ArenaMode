$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'SainPersonalityChecks.csproj'
$production = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../ArenaMode.Client/Bots/ArenaSainPersonalities.cs'))
$source = [IO.File]::ReadAllText($production)
$before = (Get-FileHash -LiteralPath $production -Algorithm SHA256).Hash
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('ArenaSainPersonality-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Baseline personality checks failed' }

$scopeStart = $source.IndexOf('    private static bool IsArenaBot(object info)')
$scopeEnd = $source.IndexOf('    private static object NormalSettings()', $scopeStart)
if ($scopeStart -lt 0 -or $scopeEnd -le $scopeStart) { throw 'Scope mutation anchor ambiguous' }
$scopeRemoved = $source.Substring(0, $scopeStart) + "    private static bool IsArenaBot(object info) => true;`n`n" + $source.Substring($scopeEnd)
$mutants = @(
    @{ Name='PassiveSelectionAllowed'; Anchor='if (!IsPassive(__result) || !IsArenaBot(__instance)) return;'; Replacement='return;'; Failure='selection excludes only passive personalities' },
    @{ Name='MismatchedSettings'; Anchor='settings = normalSettings;'; Replacement='// deliberately retain the old personality settings'; Failure='selection enum and settings stay paired' },
    @{ Name='ExplicitSetterAllowed'; Anchor='personality = (TPersonality)_normal;'; Replacement='// deliberately retain a forbidden explicit assignment'; Failure='explicit setter cannot reintroduce passive personalities' },
    @{ Name='ScopeRemoved'; Text=$scopeRemoved; Failure='selection stays native outside exact Arena scope: ordinary' },
    @{ Name='UnboundedLogs'; Anchor='if (++_replacements <= 3)'; Replacement='if (++_replacements > 0)'; Failure='assignment diagnostics stay bounded over a long raid' }
)
foreach ($mutant in $mutants) {
    $text = $mutant.Text
    if (!$text) {
        if ([regex]::Matches($source, [regex]::Escape($mutant.Anchor)).Count -ne 1) { throw "Ambiguous mutation: $($mutant.Name)" }
        $text = $source.Replace($mutant.Anchor, $mutant.Replacement)
    }
    $file = Join-Path $scratch ($mutant.Name + '.cs')
    [IO.File]::WriteAllText($file, $text)
    $output = & dotnet run --project $project -c ('SainMutant' + $mutant.Name) "-p:ProductionSource=$file" 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0 -or !$output.Contains('ARENA_SAIN_PERSONALITY_CHECKS_FAILED') -or !$output.Contains($mutant.Failure)) {
        throw "Mutant did not fail its intended behavior: $($mutant.Name)`n$output"
    }
    Write-Output "Rejected SAIN personality mutant: $($mutant.Name)"
}
if ((Get-FileHash -LiteralPath $production -Algorithm SHA256).Hash -ne $before) { throw 'Production source changed during checks' }
& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Final production personality checks failed' }
Write-Output "ARENA_SAIN_PERSONALITY_REGRESSION_PASSED ($($mutants.Count) mutants; evidence: $scratch)"
