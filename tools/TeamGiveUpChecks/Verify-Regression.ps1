#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'TeamGiveUpChecks.csproj'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$modePath = Join-Path $repo 'ArenaMode.Client/Modes/TeamDeathmatchMode.cs'
$giveUpPath = Join-Path $repo 'ArenaMode.Client/Combat/ArenaGiveUp.cs'
$scoreboardPath = Join-Path $repo 'ArenaMode.Client/Modes/ArenaScoreboard.cs'
$originalHashes = @{}
foreach ($path in @($modePath, $giveUpPath, $scoreboardPath)) {
    $originalHashes[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}
$scratch = Join-Path $PSScriptRoot ('obj/giveup-regression-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
function Invoke-Baseline {
    $output = & dotnet run --project $project -c Release 2>&1
    if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -notmatch 'ARENA_TEAM_GIVE_UP_CHECKS_PASSED') {
        throw "Baseline failed: $($output -join [Environment]::NewLine)"
    }
    $output | Write-Output
}
Invoke-Baseline
$mutants = @(
    @{ Name = 'missing-forfeit'; Source = $modePath; Property = 'ModeSource';
       From = '_match.Scoreboard.ReportOpponentForfeitKill();'; To = '/* missing original give-up team score */';
       Failure = 'give-up grants exactly one opposing PMC kill and base points' },
    @{ Name = 'unconfirmed-death'; Source = $modePath; Property = 'ModeSource';
       From = 'if (!deathConfirmed || !IsCurrentLocalGiveUpLife(player)'; To = 'if (!IsCurrentLocalGiveUpLife(player)';
       AlsoFrom = '|| player.ActiveHealthController?.IsAlive != false'; AlsoTo = '';
       Failure = 'suppressed/throwing native kill cannot score' },
    @{ Name = 'stale-context'; Source = $modePath; Property = 'ModeSource';
       From = 'if (!deathConfirmed || !IsCurrentLocalGiveUpLife(player)'; To = 'if (!deathConfirmed';
       Failure = 'stale match/world/mode/body/profile/health cannot receive give-up point' },
    @{ Name = 'stale-aggressor-gate'; Source = $giveUpPath; Property = 'GiveUpSource';
       From = 'using (ArenaAdministrativeDeathGate.Enter(player.ProfileId))'; To = '';
       Failure = 'give-up killed current life' },
    @{ Name = 'wrong-team'; Source = $scoreboardPath; Property = 'ScoreboardSource';
       From = 'if (side == ArenaScoreboardSide.Opponent)'; To = 'if (side == ArenaScoreboardSide.Friendly)';
       Failure = 'give-up grants exactly one opposing PMC kill and base points' },
    @{ Name = 'historical-visual-owner'; Source = $modePath; Property = 'ModeSource';
       From = '|| !ReferenceEquals(_world.GetAlivePlayerByProfileID(player.ProfileId), player)'; To = '';
       Failure = 'historical profile/new body alias cannot borrow visual affiliation' }
)
foreach ($mutant in $mutants) {
    $source = Get-Content -LiteralPath $mutant.Source -Raw
    if (($source.Split($mutant.From)).Count -ne 2) { throw "Mutation target must occur exactly once: $($mutant.Name)" }
    $source = $source.Replace($mutant.From, $mutant.To)
    if ($mutant.ContainsKey('AlsoFrom')) {
        if (($source.Split($mutant.AlsoFrom)).Count -ne 2) { throw "Second mutation target missing: $($mutant.Name)" }
        $source = $source.Replace($mutant.AlsoFrom, $mutant.AlsoTo)
    }
    $copy = Join-Path $scratch ($mutant.Name + '.cs')
    [IO.File]::WriteAllText($copy, $source)
    $argument = '-p:' + $mutant.Property + '=' + $copy
    $output = & dotnet run --project $project -c ('GiveUpMutant-' + $mutant.Name) $argument 2>&1
    $exitCode = $LASTEXITCODE
    $combined = $output -join "`n"
    if ($exitCode -eq 0 -or $combined -match 'error CS\d+' -or $combined -notmatch [regex]::Escape($mutant.Failure)) {
        throw "Mutant did not fail its behavioral assertion: $($mutant.Name), exit=$exitCode`n$combined"
    }
    Write-Output ('REJECTED ' + $mutant.Name + ': ' + $mutant.Failure)
}
foreach ($path in $originalHashes.Keys) {
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $originalHashes[$path]) {
        throw "Production source changed during regression run: $path"
    }
}
Invoke-Baseline
Write-Output 'ARENA_TEAM_GIVE_UP_REGRESSION_PASSED'
