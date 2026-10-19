param([string]$PublishedAssembly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'AiLimitCompatChecks.csproj'
$scope = Join-Path $repo 'ArenaMode.Client/Compat/ArenaAiLimitScope.cs'
$compat = Join-Path $repo 'ArenaMode.Client/Compat/ArenaAiLimitCompat.cs'
$run = [Guid]::NewGuid().ToString('N')
$evidence = Join-Path $repo "artifacts/ai-limit-checks/$run"
[IO.Directory]::CreateDirectory($evidence) | Out-Null
if ($PublishedAssembly -and !(Test-Path -LiteralPath $PublishedAssembly -PathType Leaf)) {
    throw 'PublishedAssembly must name the separately downloaded AI Limit 1.9.2 DLL; it is read as metadata only.'
}

function Check([string]$Name, [string]$ScopeFile, [string]$CompatFile, [string]$ExpectedFailure = '') {
    $log = Join-Path $evidence "$Name.log"
    $arguments = @('run', '--project', $project, '-c', "AiLimit_${run}_$Name",
        "-p:ScopeSource=$ScopeFile", "-p:CompatSource=$CompatFile")
    if ($PublishedAssembly) { $arguments += @('--', $PublishedAssembly) }
    & dotnet @arguments *> $log
    $code = $LASTEXITCODE
    $output = [IO.File]::ReadAllText($log)
    if ($ExpectedFailure) {
        if ($code -eq 0 -or $output -match 'error (CS|MSB|NETSDK)\d+' -or
            !$output.Contains("ASSERT: $ExpectedFailure")) {
            throw "Mutant did not compile and fail its intended behavior: $Name ($log)`n$output"
        }
        Write-Output "Rejected compiled mutant: $Name"
    } elseif ($code -ne 0 -or $output -notmatch 'AI_LIMIT_COMPAT_CHECKS_PASSED \(\d+\)') {
        throw "Production checks failed: $log`n$output"
    } else { Write-Output ($output.Trim()) }
}

Check 'before' $scope $compat
$mutants = @(
    @{ Name='ordinary-suppression'; Source='scope'; Find='if (exactActiveRaid) return true;'; Replace='return true;'; Failure='ordinary PMC runs original' },
    @{ Name='late-enable-unguarded'; Source='scope'; Find='if (exactActiveRaid) return true;'; Replace='if (exactActiveRaid) return false;'; Failure='exact active raid stays paused after token consumption' },
    @{ Name='request-only-token'; Source='scope'; Find='&& Protocol.HasNativeEndIdentity(nativeIdentity, pendingToken)'; Replace='&& true'; Failure='request-only start marker is not a completed native start' },
    @{ Name='unconfirmed-arm'; Source='scope'; Find='return armed && pendingArm'; Replace='return armed'; Failure='unconfirmed selection runs original' },
    @{ Name='foreign-world'; Source='compat'; Find='!ReferenceEquals(game.GameWorld, world)'; Replace='false'; Failure='foreign same-map world runs original' },
    @{ Name='foreign-bot-game'; Source='compat'; Find='!ReferenceEquals(Singleton<IBotGame>.Instance, game)'; Replace='false'; Failure='foreign bot game runs original' },
    @{ Name='scav-inheritance'; Source='compat'; Find='pmc: settings?.playerSide == ESideType.Pmc'; Replace='pmc: true'; Failure='stale arm cannot suppress Scav' }
)
try {
    foreach ($mutant in $mutants) {
        $sourcePath = if ($mutant.Source -eq 'scope') { $scope } else { $compat }
        $text = [IO.File]::ReadAllText($sourcePath)
        if ([regex]::Matches($text, [regex]::Escape($mutant.Find)).Count -ne 1) { throw "Ambiguous mutation anchor: $($mutant.Name)" }
        $path = Join-Path $evidence ($mutant.Name + '.cs')
        [IO.File]::WriteAllText($path, $text.Replace($mutant.Find, $mutant.Replace))
        $changedScope = if ($mutant.Source -eq 'scope') { $path } else { $scope }
        $changedCompat = if ($mutant.Source -eq 'compat') { $path } else { $compat }
        Check $mutant.Name $changedScope $changedCompat $mutant.Failure
    }
} finally { Check 'after' $scope $compat }
Write-Output "AI_LIMIT_COMPAT_MUTANTS_PASSED ($($mutants.Count)); evidence: $evidence"
