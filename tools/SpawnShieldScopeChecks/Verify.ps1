param()
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'SpawnShieldScopeChecks.csproj'
$modePath = Join-Path $PSScriptRoot '../../ArenaMode.Client/Modes/TeamDeathmatchMode.cs'
$source = [IO.File]::ReadAllText($modePath)
$originalHash = (Get-FileHash -LiteralPath $modePath -Algorithm SHA256).Hash
$scratch = Join-Path $PSScriptRoot ('obj/scope-controls/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null

function Invoke-Checks([string] $name, [string] $modeOverride = '') {
    $arguments = @('run', '--project', $project, '--configuration', 'Release')
    if ($modeOverride) { $arguments += "-p:ModeSource=$modeOverride" }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & dotnet @arguments 2>&1 | ForEach-Object { $_.ToString() } | Out-String -Width 10000
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
    [IO.File]::WriteAllText((Join-Path $scratch ($name + '.log')), $output)
    return @{ Code = $code; Output = $output }
}

$baseline = Invoke-Checks 'baseline'
if ($baseline.Code -ne 0 -or $baseline.Output -notmatch 'ARENA_SPAWN_SHIELD_SCOPE_CHECKS_PASSED') {
    throw ('Scope baseline failed: ' + $baseline.Output)
}
Write-Output $baseline.Output.Trim()
$faults = @(
    @{
        Name = 'all-maps';
        Find = 'if (Shared.ArenaTeamBoxCatalog.Applies(_world.LocationId, ArenaSession.ActiveMode))';
        Replace = 'if (true)';
        Failure = 'non-box live bot must not receive spawn immunity'
    },
    @{
        Name = 'no-box-maps';
        Find = 'if (Shared.ArenaTeamBoxCatalog.Applies(_world.LocationId, ArenaSession.ActiveMode))';
        Replace = 'if (false)';
        Failure = 'ported team-box bots must receive shields'
    },
    @{
        Name = 'pft-admission';
        Find = '|| Compat.PitFireTeamCompat.HasFrozenTeamRoster'; Replace = '';
        Failure = 'unarmed, PFT or stale raid cannot initialize shield authority'
    },
    @{
        Name = 'refill-duration-refresh';
        Find = 'if (!_population.TryReserve(usec, CountBots(usec), Time.realtimeSinceStartup, out var ticket)) return;';
        Replace = "_spawnShieldSeconds = Settings.SpawnProtectionSeconds.Value;`n        if (!_population.TryReserve(usec, CountBots(usec), Time.realtimeSinceStartup, out var ticket)) return;";
        Failure = 'replacement keeps the original match duration'
    },
    @{
        Name = 'stale-current-match';
        Find = '&& ReferenceEquals(ArenaMatchService.Current, _match)'; Replace = '';
        Failure = 'lost current match or raid ownership revokes shield authority'
    }
)
foreach ($fault in $faults) {
    $count = [regex]::Matches($source, [regex]::Escape($fault.Find)).Count
    if ($count -ne 1) { throw "Fault $($fault.Name) expected one unique production seam; found $count." }
    $path = Join-Path $scratch ($fault.Name + '.cs')
    [IO.File]::WriteAllText($path, $source.Replace($fault.Find, $fault.Replace))
    $result = Invoke-Checks $fault.Name $path
    if ($result.Code -eq 0 -or $result.Output -notmatch [regex]::Escape($fault.Failure) -or
        $result.Output -match 'error CS\d+') {
        throw "Fault $($fault.Name) did not fail for its expected behavior: $($result.Output)"
    }
    Write-Output "REJECTED: $($fault.Name) — $($fault.Failure)"
}
if ((Get-FileHash -LiteralPath $modePath -Algorithm SHA256).Hash -ne $originalHash) {
    throw 'Production mode source changed while checks ran; re-run against a stable source.'
}
$restored = Invoke-Checks 'final-production'
if ($restored.Code -ne 0 -or $restored.Output -notmatch 'ARENA_SPAWN_SHIELD_SCOPE_CHECKS_PASSED') {
    throw ('Final production baseline failed: ' + $restored.Output)
}
Write-Output "PASS: $($faults.Count) compiled fault controls rejected; source unchanged; final production baseline passed."
Write-Output "Evidence: $scratch"
Write-Output 'ARENA_SPAWN_SHIELD_SCOPE_REGRESSION_PASSED'
