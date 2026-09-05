param()
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'TeamDeathmatchRuntimeChecks.csproj'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$mode = Join-Path $repo 'ArenaMode.Client/Modes/TeamDeathmatchMode.cs'
$population = Join-Path $repo 'ArenaMode.Client/Modes/TeamDeathmatchPopulation.cs'
$relations = Join-Path $repo 'ArenaMode.Client/Bots/TeamDeathmatchRelations.cs'
$sources = @($mode, $population, $relations)
$before = @{}
foreach ($source in $sources) { $before[$source] = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash }
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('arena-tdm-runtime-mutations-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null

function Invoke-Model([string[]] $properties) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & dotnet run --project $project --configuration Release @properties 2>&1 |
            ForEach-Object { $_.ToString() } | Out-String -Width 10000
        $code = $LASTEXITCODE
        return @{ Output = $output; Code = $code }
    }
    finally { $ErrorActionPreference = $previous }
}

$baseline = Invoke-Model @()
if ($baseline.Code -ne 0 -or $baseline.Output -notmatch 'ARENA_TDM_RUNTIME_CHECKS_PASSED') {
    throw ('Runtime model baseline failed: ' + $baseline.Output)
}
$cases = @(
    @{
        Name = 'human-slot'; Path = $population; Property = 'PopulationSource';
        Find = '=> TeamSize - (usec == LocalUsec ? 1 : 0);'; Replace = '=> TeamSize;';
        Failure = 'human capacity: local faction has exactly N minus one bots'
    },
    @{
        Name = 'friendly-native-admission'; Path = $relations; Property = 'RelationsSource';
        Find = 'if (!TryGroupRelationship(__instance, person, out bool enemies) || enemies) return true;';
        Replace = 'if (true) return true;';
        Failure = 'native friendly admission rejects initial, revenge and direct causes'
    },
    @{
        Name = 'partial-respawn'; Path = $mode; Property = 'ModeSource';
        Find = 'if (!Respawn.ArenaRespawnDriver.IsRespawnInProgress'; Replace = 'if (true';
        Failure = 'intermediate local body published during respawn must not be adopted before readiness'
    }
)
foreach ($case in $cases) {
    $text = [IO.File]::ReadAllText($case.Path)
    if (($text.Split(@($case.Find), [StringSplitOptions]::None).Length - 1) -ne 1) {
        throw ('Mutation anchor must occur exactly once: ' + $case.Name)
    }
    $copy = Join-Path $scratch ($case.Name + '.cs')
    [IO.File]::WriteAllText($copy, $text.Replace($case.Find, $case.Replace))
    $result = Invoke-Model @('-p:' + $case.Property + '=' + $copy)
    if ($result.Code -eq 0 -or $result.Output -notmatch [regex]::Escape($case.Failure)) {
        throw ('Mutation did not fail for its intended behavioral assertion: ' + $case.Name + [Environment]::NewLine + $result.Output)
    }
    Write-Output ('PASS mutation: ' + $case.Name)
}
foreach ($source in $sources) {
    if ($before[$source] -ne (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash) {
        throw ('Production source changed during regression runner: ' + $source)
    }
}
$restored = Invoke-Model @()
if ($restored.Code -ne 0 -or $restored.Output -notmatch 'ARENA_TDM_RUNTIME_CHECKS_PASSED') {
    throw ('Unchanged production source failed after mutation controls: ' + $restored.Output)
}
Write-Output $restored.Output.Trim()
Write-Output 'ARENA_TDM_RUNTIME_REGRESSION_PASSED'
